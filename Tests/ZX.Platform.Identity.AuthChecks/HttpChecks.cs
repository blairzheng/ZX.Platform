using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZX.Platform.Identity.Api.Controllers;
using ZX.Platform.Identity.Api.ErrorHandling;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Infrastructure.Security;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZX.Platform.Identity.Infrastructure.Persistence;

namespace ZX.Platform.Identity.AuthChecks;

internal static class HttpChecks
{
    public static async Task RunAsync()
    {
        using var fixture = new Fixture();
        var offlineOptions = ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100,connectRetry=0");
        offlineOptions.BacklogPolicy = BacklogPolicy.FailFast;
        using var offlineConnection = await ConnectionMultiplexer.ConnectAsync(offlineOptions);
        var offlineSessions = new RedisAuthSessionStore(offlineConnection, fixture.Options, fixture.RedisOptions, fixture.AccessTokens, fixture.Clock);
        var simulateOutage = false;
        var ordinary = User.Create("USER", "Ordinary", fixture.Passwords.Hash("Example123!"));
        fixture.Users.Add(ordinary);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:AdministratorAccounts:0"] = "ADMIN",
            ["AuthenticationCookie:Secure"] = "false",
            ["AuthenticationJwt:SigningKey"] = fixture.Jwt.SigningKey,
            ["ConnectionStrings:IdentityRedis"] = TestRedis.ConnectionString
        });
        builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
        builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
        // HTTP 检查使用替代仓储，不注册/运行真实数据库初始化。
        builder.Services.RemoveAll<IRoleRepository>();
        builder.Services.RemoveAll<IMenuRepository>();
        builder.Services.RemoveAll<AuthorizationInitializer>();
        builder.Services.AddScoped<IAuthSessionStore>(_ => simulateOutage ? offlineSessions : fixture.Sessions);
        builder.Services.AddSingleton<IUserRepository>(fixture.Users);
        builder.Services.AddSingleton<IRoleRepository>(fixture.Roles);
        builder.Services.AddSingleton<IMenuRepository>(fixture.Menus);
        builder.Services.AddSingleton<IPermissionCache>(fixture.PermissionCache);
        builder.Services.AddSingleton<IUnitOfWork>(fixture.Unit);
        builder.Services.AddSingleton<IPasswordHasher>(fixture.Passwords);
        builder.Services.AddScoped<IUserService>(_ => fixture.UserService);
        builder.Services.AddApiLocalization();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddIdentitySwagger();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)
            .AddDataAnnotationsLocalization(options => options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(ValidationMessages)))
            .AddUnifiedApiBehavior();
        await using var app = builder.Build();
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseSwagger();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(app.Urls.Single()) };

        using (var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")))
        {
            var loginOperation = swagger.RootElement.GetProperty("paths").GetProperty("/api/Auth/login").GetProperty("post");
            Check.That(loginOperation.GetProperty("security").GetArrayLength() == 0, "Swagger login must allow anonymous calls.");
            Check.That(loginOperation.GetProperty("parameters").EnumerateArray().Any(parameter => parameter.GetProperty("name").GetString() == "X-Identity-CSRF"), "Swagger must expose the CSRF header.");
        }

        async Task<Reply> SendAsync(HttpMethod method, string path, object? body = null, string? access = null,
            string? cookie = null, bool csrf = true, string language = "zh-CN")
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.AcceptLanguage.ParseAdd(language);
            if (csrf) request.Headers.Add("X-Identity-CSRF", "1");
            if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return new Reply(response.StatusCode, json.RootElement.Clone(),
                response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.Single() : null,
                response.Headers.WwwAuthenticate.ToString(), response.Headers.CacheControl?.NoStore == true);
        }

        Task<Reply> LoginAsync(string account = "ADMIN", bool replaceExisting = false) => SendAsync(HttpMethod.Post, "/api/Auth/login", new { account, password = "Example123!", replaceExistingSession = replaceExisting });
        var missing = await SendAsync(HttpMethod.Get, "/api/Auth/me");
        Check.That(missing.Status == HttpStatusCode.Unauthorized && missing.Json.GetProperty("code").GetInt32() == 600401 && missing.Challenge == "Bearer", "Missing access must yield unified 401/Bearer.");
        var wrong = await SendAsync(HttpMethod.Post, "/api/Auth/login", new { account = "NONE", password = "wrong" }, language: "en-US");
        Check.That(wrong.Status == HttpStatusCode.Unauthorized && wrong.Json.GetProperty("message").GetString()!.StartsWith("Invalid account") &&
            !wrong.Json.TryGetProperty("data", out _), "Login failure must be localized and omit null data.");
        var csrfFailure = await SendAsync(HttpMethod.Post, "/api/Auth/login", new { account = "ADMIN", password = "Example123!" }, csrf: false);
        Check.That(csrfFailure.Status == HttpStatusCode.Forbidden, "Cookie-changing requests must require the CSRF header.");
        var oversized = await SendAsync(HttpMethod.Post, "/api/Auth/login", new { account = "ADMIN", password = new string('中', 25) }, language: "en-US");
        Check.That(oversized.Status == HttpStatusCode.BadRequest && oversized.Json.GetProperty("message").GetString()!.Contains("72 bytes"),
            $"BCrypt byte overflow must be localized 400. Received {(int)oversized.Status}: {oversized.Json}");

        var login = await LoginAsync();
        Check.That(login.Status == HttpStatusCode.OK && login.NoStore && !login.Json.GetProperty("data").TryGetProperty("refreshToken", out _), "Login must not expose refresh in JSON or allow caching.");
        Check.That(login.SetCookie is not null && login.SetCookie.Contains("httponly") && login.SetCookie.Contains("samesite=strict") && login.SetCookie.Contains("path=/api"), "Refresh cookie must have the intended attributes.");
        var access = Access(login);
        var cookie = Cookie(login);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access);
        Check.That(jwt.Header.Alg == SecurityAlgorithms.HmacSha256 && jwt.Header.Typ == "at+jwt" &&
            jwt.Subject == fixture.User.Id.ToString() && jwt.Payload.ContainsKey("sid") && !string.IsNullOrEmpty(jwt.Id),
            "Login must issue a standard signed JWT with sub/sid/jti.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: cookie.Split('=', 2)[1])).Status == HttpStatusCode.Unauthorized,
            "Refresh must not authenticate business requests.");
        var segments = access.Split('.');
        segments[2] = (segments[2][0] == 'a' ? "b" : "a") + segments[2][1..];
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: string.Join('.', segments))).Status == HttpStatusCode.Unauthorized,
            "JwtBearer must reject a tampered signature.");
        var now = DateTimeOffset.UtcNow;
        var claims = jwt.Claims.Where(claim => claim.Type is "sub" or "sid" or "jti" or "name").ToArray();
        string Issue(string issuer, string audience, string signingKey, DateTimeOffset start, DateTimeOffset end,
            string algorithm = SecurityAlgorithms.HmacSha256, string type = "at+jwt", Claim[]? tokenClaims = null)
        {
            var invalid = new JwtSecurityToken(issuer, audience, tokenClaims ?? claims, start.UtcDateTime, end.UtcDateTime,
                new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(signingKey)), algorithm));
            invalid.Header["typ"] = type;
            return new JwtSecurityTokenHandler().WriteToken(invalid);
        }
        foreach (var invalid in new[]
        {
            Issue("wrong-issuer", fixture.Jwt.Audience, fixture.Jwt.SigningKey, now, now.AddMinutes(1)),
            Issue(fixture.Jwt.Issuer, "wrong-audience", fixture.Jwt.SigningKey, now, now.AddMinutes(1)),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, Convert.ToBase64String(new byte[32]), now, now.AddMinutes(1)),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now.AddMinutes(-2), now.AddMinutes(-1)),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now.AddMinutes(1), now.AddMinutes(2)),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now, now.AddMinutes(1), type: "JWT"),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now, now.AddMinutes(1), tokenClaims: claims.Where(claim => claim.Type != "sid").ToArray()),
            Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now, now.AddMinutes(1), tokenClaims: claims.Select(claim => claim.Type == "jti" ? new Claim("jti", Guid.NewGuid().ToString("N")) : claim).ToArray())
        })
            Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: invalid)).Status == HttpStatusCode.Unauthorized,
                "JwtBearer must reject wrong issuer/audience/key/type, invalid lifetime/claims and revoked jti.");
        var unsigned = new JwtSecurityToken(fixture.Jwt.Issuer, fixture.Jwt.Audience, claims,
            now.UtcDateTime, now.AddMinutes(1).UtcDateTime);
        unsigned.Header["typ"] = "at+jwt";
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: new JwtSecurityTokenHandler().WriteToken(unsigned))).Status == HttpStatusCode.Unauthorized,
            "Unsigned JWTs must never authenticate.");
        var conflict = await LoginAsync();
        Check.That(conflict.Status == HttpStatusCode.Conflict && conflict.Json.GetProperty("data").GetProperty("reason").GetString() == "AlreadyOnline" && conflict.SetCookie is null,
            "Existing login must require explicit takeover without changing its cookie.");
        var takeover = await LoginAsync(replaceExisting: true);
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: access)).Status == HttpStatusCode.Unauthorized, "Takeover invalidates old access over HTTP.");
        await SendAsync(HttpMethod.Post, "/api/Auth/logout", cookie: cookie);
        access = Access(takeover);
        cookie = Cookie(takeover);
        var me = await SendAsync(HttpMethod.Get, "/api/Auth/me", access: access);
        Check.That(me.Status == HttpStatusCode.OK && me.Json.GetProperty("data").GetProperty("isAdministrator").GetBoolean(), "Current user must reflect the administrator role.");
        Check.That(me.Json.GetProperty("data").GetProperty("menus").GetArrayLength() > 0, "Current user must include the visible menu tree.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Menus/getMenuTree", access: access)).Status == HttpStatusCode.OK,
            "Administrator must be able to read the management menu tree.");
        var permissionTree = await SendAsync(HttpMethod.Get, "/api/Roles/permissionTree", access: access);
        Check.That(permissionTree.Status == HttpStatusCode.OK && permissionTree.Json.GetProperty("data").GetArrayLength() > 0,
            "Role assignment must expose the menu permission hierarchy.");

        var userLogin = await LoginAsync("USER");
        var userAccess = Access(userLogin);
        Check.That((await SendAsync(HttpMethod.Get, "/api/Menus/getMenuTree", access: userAccess)).Status == HttpStatusCode.Forbidden,
            "Menu management must enforce its own policy.");
        var forbidden = await SendAsync(HttpMethod.Get, "/api/Users/getUserList", access: userAccess, language: "en-US");
        Check.That(forbidden.Status == HttpStatusCode.Forbidden && forbidden.Json.GetProperty("code").GetInt32() == 600005 &&
            forbidden.Json.GetProperty("message").GetString()!.StartsWith("You do not"), "Ordinary user must receive localized 403 for management.");
        Check.That((await SendAsync(HttpMethod.Put, "/api/Users/updateDisplayName", new { id = ordinary.Id, displayName = "Own name" }, userAccess)).Status == HttpStatusCode.OK,
            "Ordinary user may change their own name.");
        Check.That((await SendAsync(HttpMethod.Put, "/api/Users/updateDisplayName", new { id = fixture.User.Id, displayName = "Other name" }, userAccess)).Status == HttpStatusCode.Forbidden,
            "Ordinary user cannot change another user's name.");

        var roleCreated = await SendAsync(HttpMethod.Post, "/api/Roles/add", new { code = "HTTP_READER", name = "HTTP reader" }, access);
        Check.That(roleCreated.Status == HttpStatusCode.Created, "An administrator may create roles.");
        var roleId = roleCreated.Json.GetProperty("data").GetGuid();
        var granted = await SendAsync(HttpMethod.Put, "/api/Roles/updatePermissions",
            new { roleId, permissionCodes = new[] { IdentityPermissions.RoleRead } }, access);
        Check.That(granted.Status == HttpStatusCode.OK, "An administrator may grant declared permissions.");
        var assigned = await SendAsync(HttpMethod.Put, "/api/Roles/updateUserRoles",
            new { userId = ordinary.Id, roleIds = new[] { roleId } }, access);
        Check.That(assigned.Status == HttpStatusCode.OK &&
            (await SendAsync(HttpMethod.Get, "/api/Roles/getRoleList", access: userAccess)).Status == HttpStatusCode.OK,
            "Assigned roles must authorize an existing JWT without signing in again.");
        Check.That((await SendAsync(HttpMethod.Put, "/api/Roles/update", new { id = roleId, name = "Denied" }, userAccess)).Status == HttpStatusCode.Forbidden,
            "Read permission must not grant role modification.");
        await SendAsync(HttpMethod.Put, "/api/Roles/updatePermissions", new { roleId, permissionCodes = Array.Empty<string>() }, access);
        Check.That((await SendAsync(HttpMethod.Get, "/api/Roles/getRoleList", access: userAccess)).Status == HttpStatusCode.Forbidden,
            "Revoked permissions must reject the same existing JWT on the next request.");
        Check.That((await SendAsync(HttpMethod.Post, $"/api/Users/forceLogout?id={fixture.User.Id}", access: userAccess)).Status == HttpStatusCode.Forbidden,
            "An ordinary user must not force another user offline.");
        Check.That((await SendAsync(HttpMethod.Post, $"/api/Users/forceLogout?id={ordinary.Id}", access: access)).Status == HttpStatusCode.OK,
            "An administrator may force a user offline.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: userAccess)).Status == HttpStatusCode.Unauthorized, "Force logout invalidates access.");
        userAccess = Access(await LoginAsync("USER"));

        var expiredAccess = Issue(fixture.Jwt.Issuer, fixture.Jwt.Audience, fixture.Jwt.SigningKey, now.AddMinutes(-2), now.AddMinutes(-1));
        var refresh = await SendAsync(HttpMethod.Post, "/api/Auth/refresh", access: expiredAccess, cookie: cookie);
        Check.That(refresh.Status == HttpStatusCode.OK && Access(refresh) != access && Cookie(refresh) != cookie, "Refresh must rotate both credentials.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: access)).Status == HttpStatusCode.Unauthorized, "Rotation must invalidate old access.");
        var replay = await SendAsync(HttpMethod.Post, "/api/Auth/refresh", cookie: cookie);
        Check.That(replay.Status == HttpStatusCode.Unauthorized && replay.SetCookie is not null && replay.SetCookie.Contains("expires="), "Failed refresh must clear its cookie.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: Access(refresh))).Status == HttpStatusCode.Unauthorized, "Refresh replay must revoke the active access.");

        var adminAgain = await LoginAsync();
        Check.That((await SendAsync(HttpMethod.Put, $"/api/Users/setUserDisable?id={ordinary.Id}", access: Access(adminAgain))).Status == HttpStatusCode.OK,
            "Configured admin must be allowed to disable a user.");
        Check.That((await SendAsync(HttpMethod.Get, "/api/Auth/me", access: userAccess)).Status == HttpStatusCode.Unauthorized, "Disable must be visible to HTTP authentication immediately.");

        simulateOutage = true;
        var malformedDuringOutage = await SendAsync(HttpMethod.Get, "/api/Auth/me", access: "invalid-jwt");
        Check.That(malformedDuringOutage.Status == HttpStatusCode.Unauthorized, "JWT must fail validation before consulting Redis.");
        var unavailable = await SendAsync(HttpMethod.Get, "/api/Auth/me", access: Access(adminAgain), language: "en-US");
        Check.That(unavailable.Status == HttpStatusCode.ServiceUnavailable && unavailable.Json.GetProperty("code").GetInt32() == 600503 &&
            unavailable.Json.GetProperty("message").GetString()!.Contains("temporarily unavailable"), "Redis outage must yield localized 503, not 401 or a bypass.");
        var refreshUnavailable = await SendAsync(HttpMethod.Post, "/api/Auth/refresh", cookie: Cookie(adminAgain));
        Check.That(refreshUnavailable.Status == HttpStatusCode.ServiceUnavailable && refreshUnavailable.SetCookie is null, "Redis outage must preserve the refresh cookie.");
        simulateOutage = false;

        var bearerLogout = await LoginAsync(replaceExisting: true);
        Check.That((await SendAsync(HttpMethod.Post, "/api/Auth/logout", access: Access(bearerLogout))).Status == HttpStatusCode.OK &&
            (await SendAsync(HttpMethod.Post, "/api/Auth/refresh", cookie: Cookie(bearerLogout))).Status == HttpStatusCode.Unauthorized,
            "Bearer-only logout must read sub/sid and revoke the matching session.");
        var expiringLogout = await LoginAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        Check.That((await SendAsync(HttpMethod.Post, "/api/Auth/logout", access: expiredAccess, cookie: Cookie(expiringLogout))).Status == HttpStatusCode.OK, "Logout must work with expired access.");
        Check.That((await SendAsync(HttpMethod.Post, "/api/Auth/refresh", cookie: Cookie(expiringLogout))).Status == HttpStatusCode.Unauthorized, "Logout must revoke refresh.");
        Check.That((await SendAsync(HttpMethod.Post, "/api/Auth/logout")).Status == HttpStatusCode.OK, "Repeated logout must be idempotent.");
        await app.StopAsync();
        Console.WriteLine("PASS real HTTP/Redis authentication, takeover, force logout, outage, authorization, localization and cookies");
    }

    private static string Access(Reply reply) => reply.Json.GetProperty("data").GetProperty("accessToken").GetString()!;
    private static string Cookie(Reply reply) => reply.SetCookie!.Split(';')[0];
    private sealed record Reply(HttpStatusCode Status, JsonElement Json, string? SetCookie, string Challenge, bool NoStore);
}
