using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Personnel;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.Api.Extensions;

public static class DirectoryRelayExtensions
{
    public static IServiceCollection AddDirectoryAccess(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var relay = configuration.GetSection("DirectoryRelay").Get<DirectoryRelayOptions>() ?? new();
        var useRelay = environment.IsDevelopment() && relay.Enabled;
        if (useRelay)
            relay.ValidateClient();
        services.AddSingleton(relay);
        var sync = configuration.GetSection("OaPersonnelSync").Get<OaPersonnelSyncOptions>() ?? new();
        services.AddSingleton(sync);
        services.AddSingleton<LdapPersonnelDirectory>();
        services.AddSingleton<IOaPersonnelDirectory>(provider =>
            provider.GetRequiredService<LdapPersonnelDirectory>());

        if (useRelay)
        {
            services.AddHttpClient<DirectoryRelayClient>(client =>
            {
                // 认证和人员读取由客户端以单次、可取消的总预算控制，不自动重试。
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            }).RemoveAllLoggers();
            services.AddTransient<ILdapAuthenticator, RemoteLdapAuthenticator>();
            services.AddTransient<IOaPersonnelDirectory, RemoteOaPersonnelDirectory>();
        }
        return services;
    }
}
