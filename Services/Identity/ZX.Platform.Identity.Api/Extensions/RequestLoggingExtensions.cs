using Microsoft.AspNetCore.HttpLogging;

namespace ZX.Platform.Identity.Api.Extensions
{
    public static class RequestLoggingExtensions
    {
        public static IServiceCollection AddRequestLogging(this IServiceCollection services)
        {
            services.Configure<LoggerFactoryOptions>(options =>
            {
                options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId
                    | ActivityTrackingOptions.SpanId
                    | ActivityTrackingOptions.ParentId;
            });
            services.AddHttpLogging(options =>
            {
                // 每次请求合并为一条摘要，不读取请求体、响应体、查询参数或请求头。
                options.LoggingFields = HttpLoggingFields.RequestMethod
                    | HttpLoggingFields.RequestPath
                    | HttpLoggingFields.ResponseStatusCode
                    | HttpLoggingFields.Duration;
                options.CombineLogs = true;
            });
            return services;
        }
    }
}
