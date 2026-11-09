using Microsoft.AspNetCore.Authentication;
using ZX.Platform.Identity.Api.Authentication;
using ZX.Platform.Identity.Application.Authentication.Sso;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.Api.Extensions;

public static class GoFlowSsoExtensions
{
    public static IServiceCollection AddGoFlowSso(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("SingleSignOn:GoFlow").Get<GoFlowSsoOptions>() ?? new GoFlowSsoOptions();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IGoFlowSsoTicketStore, RedisGoFlowSsoTicketStore>();
        services.AddScoped<IGoFlowSsoService, GoFlowSsoService>();
        // 不改变已有默认 Bearer 方案、登录 / 刷新 Cookie 或全局权限策略。
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, GoFlowClientAuthenticationHandler>(
            GoFlowClientAuthenticationHandler.SchemeName, _ => { });
        return services;
    }
}
