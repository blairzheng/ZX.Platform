using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using ZX.Platform.Identity.Api.Controllers;

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
            options.OperationFilter<AuthOperationFilter>();
        });
        return services;
    }

    private sealed class AuthOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [];

            if (context.MethodInfo.DeclaringType == typeof(AuthController) && context.ApiDescription.HttpMethod != "GET")
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-Identity-CSRF",
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "刷新 Cookie 的请求校验头，固定值 1。",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create("1") }
                });
            }
        }
    }
}
