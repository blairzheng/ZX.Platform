using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZX.Platform.Identity.Api.LowCode;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.Identity.AuthChecks;

/// <summary>
/// 低代码的权限闸门。这里守的是一个真实踩过的坑：
///
/// <c>IPermissionService.HasAsync</c> 第一步就查 <c>IdentityPermissions.All</c> 静态目录，
/// 而低代码的实体权限是**动态生成的**（<c>mes.stocktake.read</c> 这类，实体发布时才写进权限表）。
/// 一旦用它判定，所有低代码接口连管理员都 403，而且要到前端接上才被发现。
///
/// 所以这里不用 HTTP，直接测闸门本身——它判定错了，上面所有接口都是错的。
/// </summary>
internal static class LowCodeChecks
{
    private const string DynamicPermission = "mes.stocktake.read";

    public static async Task RunAsync()
    {
        var userId = Guid.NewGuid();

        var granted = new LowCodePermissionGuard(
            new FakePermissions(false, [DynamicPermission]), new TestUserSbus());
        Check.That(await granted.RequireAsync(Context(userId), DynamicPermission, default) == userId,
            "动态权限码必须按授权快照判定，不能被静态目录挡掉。");

        var missing = new LowCodePermissionGuard(new FakePermissions(false, []), new TestUserSbus());
        await ExpectAsync(() => missing.RequireAsync(Context(userId), DynamicPermission, default),
            LowCodeErrorCodes.Forbidden);

        var administrator = new LowCodePermissionGuard(new FakePermissions(true, []), new TestUserSbus());
        Check.That(await administrator.RequireAsync(Context(userId), DynamicPermission, default) == userId,
            "管理员必须能访问尚未授予任何角色的动态权限。");

        var anonymous = new LowCodePermissionGuard(new FakePermissions(true, []), new TestUserSbus());
        await ExpectAsync(() => anonymous.RequireAsync(new DefaultHttpContext(), DynamicPermission, default),
            LowCodeErrorCodes.Forbidden);

        await CheckScopeAsync(userId);
        Console.WriteLine("PASS low-code permission guard and SBU scope");
    }

    private static async Task CheckScopeAsync(Guid userId)
    {
        var guard = new LowCodePermissionGuard(new FakePermissions(true, []), new TestUserSbus());

        // 不要求 SBU 的实体：范围直接来自会话，不需要请求头。
        var unscoped = await guard.ResolveScopeAsync(
            new EntityDefinition { AppCode = "mes", Code = "station", Name = "站点" },
            Context(userId), default);
        Check.That(unscoped.SbuId is null && unscoped.AllSbus && unscoped.UserId == userId,
            "不按 SBU 隔离的实体必须给出无范围限制的数据范围。");

        var scoped = new EntityDefinition
        {
            AppCode = "mes", Code = "stocktake", Name = "盘点单", ScopeMode = ScopeMode.Column
        };

        // 少了请求头必须拒绝，而不是默默按"全部 SBU"放行。
        await ExpectAsync(() => guard.ResolveScopeAsync(scoped, Context(userId), default),
            LowCodeErrorCodes.SbuRequired);

        // 空 GUID 不算"没带"，它一定是前端拼错了。
        await ExpectAsync(
            () => guard.ResolveScopeAsync(scoped, Context(userId, Guid.Empty.ToString()), default),
            LowCodeErrorCodes.SbuRequired);

        // 用户不属于该 SBU：TestUserSbus 里没有任何关系，必然拿不到。
        await ExpectAsync(
            () => guard.ResolveScopeAsync(scoped, Context(userId, Guid.NewGuid().ToString()), default),
            LowCodeErrorCodes.SbuDenied);
    }

    private static HttpContext Context(Guid userId, string? sbuId = null)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString())], "check"));
        if (sbuId is not null) http.Request.Headers["X-SBU-ID"] = sbuId;
        return http;
    }

    private static async Task ExpectAsync(Func<Task> action, string expectedCode)
    {
        try
        {
            await action();
        }
        catch (LowCodeException exception) when (exception.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"Expected low-code error {expectedCode}.");
    }

    private sealed class FakePermissions(bool administrator, string[] granted) : IPermissionService
    {
        public Task<AuthorizationSnapshot> GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthorizationSnapshot(administrator, [], granted));

        // 刻意复刻真实实现：静态目录里没有的码一律 false。
        public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityPermissions.All.Contains(permission, StringComparer.Ordinal)
                            && (administrator || granted.Contains(permission, StringComparer.Ordinal)));
    }
}
