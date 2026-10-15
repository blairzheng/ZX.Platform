using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using StackExchange.Redis;
using ZX.Platform.Identity.Api.Authentication;
using ZX.Platform.Identity.Api.Options;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Authentication").Get<AuthOptions>() ?? new AuthOptions();
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
        if (options.AdministratorAccounts is null || options.AdministratorAccounts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Authentication:AdministratorAccounts 不能包含空账号。");

        var cookie = configuration.GetSection("AuthenticationCookie").Get<AuthCookieOptions>() ?? new AuthCookieOptions();
        if (!environment.IsDevelopment() && !cookie.Secure)
            throw new InvalidOperationException("非 Development 环境必须启用 Secure 刷新 Cookie。");

        services.AddSingleton(options);
        services.AddSingleton(cookie);
        var redis = configuration.GetSection("AuthenticationRedis").Get<RedisSessionOptions>() ?? new RedisSessionOptions();
        Validator.ValidateObject(redis, new ValidationContext(redis), validateAllProperties: true);
        var redisConnection = configuration.GetConnectionString("IdentityRedis")
            ?? throw new InvalidOperationException("未配置 IdentityRedis 连接字符串。");
        var redisConfiguration = ConfigurationOptions.Parse(redisConnection);
        redisConfiguration.AbortOnConnectFail = false;
        // 故障时及时返回 503，不排队等待 Redis 恢复后补执行登录/下线等旧操作。
        redisConfiguration.BacklogPolicy = BacklogPolicy.FailFast;
        services.AddSingleton(redis);
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfiguration));
        var jwt = configuration.GetSection("AuthenticationJwt").Get<JwtOptions>() ?? new JwtOptions();
        Validator.ValidateObject(jwt, new ValidationContext(jwt), validateAllProperties: true);
        var validation = jwt.CreateValidationParameters();
        services.AddSingleton(jwt);
        services.AddSingleton<JwtAccessTokenIssuer>();
        services.AddSingleton<IAuthSessionStore, RedisAuthSessionStore>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<JwtSessionEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = validation;
                bearer.EventsType = typeof(JwtSessionEvents);
            });
        services.AddAuthorization(authorization =>
        {
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            authorization.AddPolicy(JwtSessionEvents.AdministratorPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    context.User.Identity?.Name is string account && options.IsAdministrator(account)));
        });
        return services;
    }
}
