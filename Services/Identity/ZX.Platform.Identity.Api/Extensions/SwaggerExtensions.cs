using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using ZX.Platform.Identity.Api.Authentication;

namespace ZX.Platform.Identity.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddIdentitySwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "登录后填入 accessToken；JWT 验证通过后还会检查 Redis 当前会话。"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            options.AddSecurityDefinition(GoFlowClientAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "basic",
                Description = "仅供 GoFlow 后端兑换 SSO 票据；填写该环境配置的客户端标识和独立服务密钥，必须使用 HTTPS。"
            });
            options.OperationFilter<AuthOperationFilter>();
            options.DocumentFilter<GoFlowSecurityDocumentFilter>();
        });
        return services;
    }

    private sealed class AuthOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [];

            if ((context.MethodInfo.DeclaringType?.IsDefined(typeof(RequireIdentityCsrfHeaderAttribute), true) == true ||
                context.MethodInfo.IsDefined(typeof(RequireIdentityCsrfHeaderAttribute), true)) &&
                context.ApiDescription.HttpMethod != "GET")
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-Identity-CSRF",
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "身份相关写入接口的请求校验头，固定值 1。",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create("1") }
                });
            }
        }
    }

    private sealed class GoFlowSecurityDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument document, DocumentFilterContext context)
        {
            foreach (var api in context.ApiDescriptions.Where(api =>
                api.ActionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>()
                    .Any(attribute => attribute.AuthenticationSchemes == GoFlowClientAuthenticationHandler.SchemeName)))
            {
                if (!document.Paths.TryGetValue("/" + api.RelativePath, out var path) || path.Operations is null)
                    continue;
                foreach (var (method, operation) in path.Operations)
                {
                    if (!string.Equals(method.ToString(), api.HttpMethod, StringComparison.OrdinalIgnoreCase)) continue;
                    operation.Security = [new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(GoFlowClientAuthenticationHandler.SchemeName, document)] = []
                    }];
                }
            }
        }
    }
}
