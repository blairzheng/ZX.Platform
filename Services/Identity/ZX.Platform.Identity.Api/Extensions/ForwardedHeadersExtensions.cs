using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace ZX.Platform.Identity.Api.Extensions
{
    public static class ForwardedHeadersExtensions
    {
        /// <summary>
        /// 只接受已知的直接上游代理传来的客户端 IP 和协议。
        /// </summary>
        public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
        {
            var addresses = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()
                ?? new[] { "127.0.0.1", "::1" };
            if (addresses.Length == 0)
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies 至少需要一个可信代理地址。");

            var proxies = addresses.Select(IPAddress.Parse).ToArray();
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in proxies)
                    options.KnownProxies.Add(proxy);
            });
            return services;
        }
    }
}
