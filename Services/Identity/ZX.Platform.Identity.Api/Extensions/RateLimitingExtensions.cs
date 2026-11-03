using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.RateLimiting;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Api.Options;
using ZX.Platform.Identity.Application.Errors;

namespace ZX.Platform.Identity.Api.Extensions
{
    public static class RateLimitingExtensions
    {
        /// <summary>
        /// 限制同一 IP 对同一接口的访问频率，超限直接拒绝。
        /// </summary>
        public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            var limits = configuration.GetSection("RateLimiting").Get<ApiRateLimitingOptions>()
                ?? new ApiRateLimitingOptions();
            Validator.ValidateObject(limits, new ValidationContext(limits), validateAllProperties: true);

            return services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, (string Ip, string Method, string Route)>(context =>
                {
                    var endpoint = context.GetEndpoint();
                    var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
                    if (endpoint is not RouteEndpoint route || action is null)
                        return RateLimitPartition.GetNoLimiter(("", "", ""));

                    var address = context.Connection.RemoteIpAddress;
                    if (address?.IsIPv4MappedToIPv6 == true)
                        address = address.MapToIPv4();

                    // 按路由模板识别接口，查询参数和路由参数值不产生新额度。
                    var key = (
                        Ip: address?.ToString() ?? "unknown",
                        Method: context.Request.Method,
                        Route: (route.RoutePattern.RawText ?? action.Id).ToLowerInvariant());

                    return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        SegmentsPerWindow = limits.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
                });

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    var httpContext = context.HttpContext;
                    var messages = httpContext.RequestServices.GetRequiredService<IApiMessageLocalizer>();
                    var response = new ApiResponse
                    {
                        Code = ErrorCodes.TooManyRequests,
                        Message = messages["Error.TooManyRequests"].Value,
                        TraceId = Activity.Current?.Id ?? httpContext.TraceIdentifier
                    };
                    await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
                };
            });
        }
    }
}
