using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Errors;

namespace ZX.Platform.Identity.AuthChecks;

internal static class AuthFlowChecks
{
    public static async Task RunAsync()
    {
        using (var fixture = new Fixture())
        {
            for (var i = 0; i < 5; i++)
                await Check.ErrorAsync(() => fixture.LoginAsync("wrong"), ErrorCodes.Unauthorized);
            Check.That(fixture.User.IsLocked(fixture.Clock.Now), "Five failures must lock the account.");
            await Check.ErrorAsync(() => fixture.LoginAsync(), ErrorCodes.Unauthorized);
            fixture.Clock.Advance(TimeSpan.FromMinutes(15));
            await Check.ErrorAsync(() => fixture.LoginAsync("wrong"), ErrorCodes.Unauthorized);
            Check.That(fixture.User.AccessFailedCount == 1 && !fixture.User.IsLocked(fixture.Clock.Now), "Expired lockout must reset the failure counter.");
            await fixture.LoginAsync();
            Check.That(fixture.User.AccessFailedCount == 0, "Successful login must reset failures.");
        }

        foreach (var change in new[] { "disable", "delete", "batchDelete", "forceLogout", "changePassword" })
        {
            using var fixture = new Fixture();
            var one = await fixture.LoginAsync();
            switch (change)
            {
                case "disable": await fixture.UserService.SetUserDisableAsync(fixture.User.Id, default); break;
                case "delete": await fixture.UserService.SoftDeleteAsync(fixture.User.Id, default); break;
                case "batchDelete": await fixture.UserService.BatchDeleteAsync([fixture.User.Id, fixture.User.Id, Guid.NewGuid()], default); break;
                case "forceLogout": await fixture.UserService.ForceLogoutAsync(fixture.User.Id, default); break;
                case "changePassword": await fixture.Auth.ChangePasswordAsync(fixture.User.Id, new ChangePasswordDto { CurrentPassword = "Example123!", NewPassword = "NewPass123!" }, default); break;
            }
            Check.That(await fixture.FindAccessAsync(one.AccessToken, default) is null, $"{change} must revoke access.");
            await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(one.RefreshToken, default), ErrorCodes.Unauthorized);
            if (change == "disable")
            {
                await fixture.UserService.SetUserEnableAsync(fixture.User.Id, default);
                Check.That(await fixture.FindAccessAsync(one.AccessToken, default) is null, "Enable must not revive old sessions.");
            }
            if (change is "delete" or "batchDelete")
            {
                await fixture.UserService.RestoreAsync(fixture.User.Id, default);
                Check.That(await fixture.FindAccessAsync(one.AccessToken, default) is null, "Restore must not revive old sessions.");
            }
        }

        using (var fixture = new Fixture())
        {
            var original = await fixture.LoginAsync();
            await Check.ErrorAsync(() => fixture.LoginAsync(), ErrorCodes.Conflict);
            await Check.ErrorAsync(() => fixture.LoginAsync("wrong", true), ErrorCodes.Unauthorized);
            Check.That(await fixture.FindAccessAsync(original.AccessToken, default) is not null, "A wrong password must not evict an existing login.");
            var replacement = await fixture.LoginAsync(replaceExisting: true);
            Check.That(await fixture.FindAccessAsync(original.AccessToken, default) is null, "Confirmed takeover revokes the old session.");
            await fixture.UserService.ForceLogoutAsync(fixture.User.Id, default);
            Check.That(await fixture.FindAccessAsync(replacement.AccessToken, default) is null, "Force logout revokes current access.");
            await fixture.LoginAsync();
            Check.That(fixture.User.IsEnabled, "Force logout must allow later login without disabling the account.");
        }

        using (var fixture = new Fixture())
        {
            // 明确验证已接受的边界：登录已检查状态，但尚未发布会话时发生禁用。
            var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = 0;
            fixture.Unit.OnSave = async () => { if (Interlocked.Increment(ref saves) == 1) { saving.SetResult(); await resume.Task; } };
            var login = fixture.LoginAsync();
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.UserService.SetUserDisableAsync(fixture.User.Id, default);
            resume.SetResult();
            var lateSession = await login;
            Check.That(await fixture.FindAccessAsync(lateSession.AccessToken, default) is not null, "Without a user lock, an in-flight login may publish after revocation.");
            await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(lateSession.RefreshToken, default), ErrorCodes.Unauthorized);
            Check.That(await fixture.FindAccessAsync(lateSession.AccessToken, default) is null, "Refresh must detect disabled users and revoke the late session.");
        }

        using (var fixture = new Fixture())
        {
            var tokens = await fixture.LoginAsync();
            fixture.Unit.OnSave = () => throw new OperationCanceledException("Simulated uncertain commit result");
            try { await fixture.UserService.SetUserDisableAsync(fixture.User.Id, default); }
            catch (OperationCanceledException) { }
            Check.That(await fixture.FindAccessAsync(tokens.AccessToken, default) is null, "Uncertain save result must still revoke sessions.");
        }
        using (var fixture = new Fixture())
        {
            var original = await fixture.LoginAsync();
            var identity = (await fixture.Sessions.FindByRefreshTokenAsync(original.RefreshToken, default))!;
            await fixture.Auth.ChangePasswordAsync(fixture.User.Id,
                new ChangePasswordDto { CurrentPassword = "Example123!", NewPassword = "NewPass123!" }, default);
            // 模拟已通过旧密码校验的登录，在改密/撤销完成后才发布会话。
            var lateSession = (await fixture.Sessions.CreateAsync(fixture.User.Id, fixture.User.Account, identity.CredentialStamp, false, default))!;
            await Check.ErrorAsync(() => fixture.Auth.RefreshAsync(lateSession.RefreshToken, default), ErrorCodes.Unauthorized);
            Check.That(await fixture.FindAccessAsync(lateSession.AccessToken, default) is null, "Refresh must reject a late login based on the old password.");
        }
        Console.WriteLine("PASS lockout, password checks, takeover, force logout, security changes and accepted race boundary");
    }
}
