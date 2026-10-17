using Microsoft.AspNetCore.HttpLogging;

namespace ZX.Platform.Gateway.Extensions
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
                // Gateway 记录入口路径与整体转发耗时；业务服务记录自己的处理耗时。
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
