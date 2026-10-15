using System.Text.Json;
using StackExchange.Redis;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.AuthChecks;

internal static class SessionChecks
{
    public static async Task RunAsync()
    {
        var passwords = new BCryptPasswordHasher();
        var passwordHash = passwords.Hash("Example123!");
        Check.That(passwords.Verify("Example123!", passwordHash) && !passwords.Verify("wrong", passwordHash) &&
            !passwords.Verify("Example123!", string.Empty), "Real BCrypt must reject wrong and unknown-account credentials.");
        using var fixture = new Fixture();
        var store = fixture.Sessions;
        using var otherConnection = TestRedis.Connect();
        var otherStore = new RedisAuthSessionStore(otherConnection, fixture.Options, fixture.RedisOptions, fixture.AccessTokens, fixture.Clock);
        var user = fixture.User.Id;
        var first = (await store.CreateAsync(user, "u", "stamp", false, default))!;
        var database = TestRedis.Connection.GetDatabase();
        var key = fixture.RedisOptions.KeyPrefix + $"session:{{{user:N}}}";
        Check.That(await database.KeyTypeAsync(key) == RedisType.String, "Sessions must be stored as JSON strings rather than Redis hashes.");
        var storedJson = (await database.StringGetAsync(key)).ToString();
        using (var json = JsonDocument.Parse(storedJson))
        {
            Check.That(json.RootElement.GetProperty("Account").GetString() == "u" &&
                json.RootElement.GetProperty("SessionId").GetGuid() != Guid.Empty &&
                json.RootElement.GetProperty("ExpiresAtUtc").GetDateTimeOffset() == first.RefreshTokenExpiresAtUtc,
                "Stored JSON must retain named session properties and the original expiration.");
        }
        Check.That(!storedJson.Contains(first.AccessToken) && !storedJson.Contains(first.RefreshToken), "Redis must not store full access or refresh tokens.");
        Check.That(await database.KeyExpireTimeAsync(key) == first.RefreshTokenExpiresAtUtc.UtcDateTime, "SET must attach the exact absolute session expiration.");
        Check.That(first.AccessToken != first.RefreshToken, "JWT access and random refresh must be separate credentials.");
        Check.That(await fixture.FindAccessAsync(first.AccessToken, default, otherStore) is not null, "A second Redis client must see the same session.");
        var unknownRefresh = first.RefreshToken[..^1] + (first.RefreshToken[^1] == 'a' ? "b" : "a");
        Check.That(await store.FindByRefreshTokenAsync(unknownRefresh, default) is null &&
            await store.RotateAsync(unknownRefresh, default) is null &&
            await fixture.FindAccessAsync(first.AccessToken, default) is not null,
            "An unknown refresh secret must fail without revoking the current session.");
        Check.That(await fixture.FindAccessAsync(first.RefreshToken, default) is null, "Refresh cannot authenticate business requests.");
        Check.That(await store.FindByRefreshTokenAsync(first.AccessToken, default) is null, "Access cannot refresh.");
        var parts = first.AccessToken.Split('.');
        parts[2] = (parts[2][0] == 'a' ? "b" : "a") + parts[2][1..];
        Check.That(await fixture.FindAccessAsync(string.Join('.', parts), default) is null, "A changed JWT signature must fail.");
        Check.That(await otherStore.CreateAsync(user, "u", "stamp", false, default) is null, "A second login must require takeover.");
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        Check.That(await fixture.FindAccessAsync(first.AccessToken, default) is null, "Access must expire at its exact boundary.");
        var rotated = await otherStore.RotateAsync(first.RefreshToken, default);
        Check.That(rotated is not null && rotated.RefreshTokenExpiresAtUtc.ToUnixTimeMilliseconds() == first.RefreshTokenExpiresAtUtc.ToUnixTimeMilliseconds(), "Refresh must not extend the absolute lifetime.");
        Check.That(await fixture.FindAccessAsync(rotated!.AccessToken, default) is not null, "Refresh must work after access expires.");
        Check.That(await database.KeyExpireTimeAsync(key) == first.RefreshTokenExpiresAtUtc.UtcDateTime, "Replacing JSON during refresh must preserve Redis expiration.");
        var rotatedAgain = (await store.RotateAsync(rotated.RefreshToken, default))!;
        Check.That(await fixture.FindAccessAsync(rotated.AccessToken, default) is null &&
            await fixture.FindAccessAsync(rotatedAgain.AccessToken, default) is not null, "Refresh must replace the previous access token.");
        Check.That(await store.RotateAsync(first.RefreshToken, default) is null, "Consumed refresh must fail.");
        Check.That(await fixture.FindAccessAsync(rotatedAgain.AccessToken, default) is null, "Replay history must survive multiple JSON replacements and revoke the latest access.");
        Check.That(await store.FindByRefreshTokenAsync(rotatedAgain.RefreshToken, default) is null, "Replay must revoke the latest refresh.");
        var old = (await store.CreateAsync(user, "u", "stamp", false, default))!;
        var oldSession = (await store.FindByRefreshTokenAsync(old.RefreshToken, default))!;
        var replacement = (await otherStore.CreateAsync(user, "u", "stamp", true, default))!;
        Check.That(await fixture.FindAccessAsync(old.AccessToken, default) is null, "Takeover invalidates old access.");
        Check.That(await store.RotateAsync(old.RefreshToken, default) is null, "Old refresh cannot rotate a replacement.");
        await store.RevokeAsync(user, oldSession.SessionId, default);
        Check.That(await fixture.FindAccessAsync(replacement.AccessToken, default, otherStore) is not null, "Old logout/replay must preserve a new session.");

        var simultaneous = replacement;
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
            await (i % 2 == 0 ? store : otherStore).RotateAsync(simultaneous.RefreshToken, default)));
        Check.That(results.Count(result => result is not null) == 1, "Only one cross-instance refresh may consume the credential.");
        Check.That(await fixture.FindAccessAsync(results.Single(result => result is not null)!.AccessToken, default) is null,
            "Concurrent replay must revoke the winner's session.");

        var creations = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
            await (i % 2 == 0 ? store : otherStore).CreateAsync(user, "u", "stamp", false, default)));
        var successfulCreations = creations.Where(result => result is not null).ToArray();
        var activeCreations = await Task.WhenAll(successfulCreations.Select(async result =>
            await fixture.FindAccessAsync(result!.AccessToken, default)));
        Check.That(successfulCreations.Length >= 1 && activeCreations.Count(result => result is not null) == 1,
            "Simultaneous logins may overwrite each other, but only the final session may authenticate.");

        // 接管、旧刷新和旧退出并发时，旧请求仍不能删除最终的新登录。
        await otherStore.RevokeUserAsync(user, default);
        var beforeTakeover = (await store.CreateAsync(user, "u", "stamp", false, default))!;
        var beforeTakeoverIdentity = (await store.FindByRefreshTokenAsync(beforeTakeover.RefreshToken, default))!;
        var racingRefresh = store.RotateAsync(beforeTakeover.RefreshToken, default);
        var racingTakeover = otherStore.CreateAsync(user, "u", "stamp", true, default);
        var racingLogout = store.RevokeAsync(user, beforeTakeoverIdentity.SessionId, default);
        await Task.WhenAll(racingRefresh, racingTakeover, racingLogout);
        Check.That(await fixture.FindAccessAsync(racingTakeover.Result!.AccessToken, default) is not null,
            "Old refresh/logout racing a takeover must preserve the replacement session.");

        // 同一会话退出与刷新并发，退出成功后不能留下轮换产生的新凭据。
        var beforeLogout = racingTakeover.Result!;
        var beforeLogoutIdentity = (await store.FindByRefreshTokenAsync(beforeLogout.RefreshToken, default))!;
        var refreshing = otherStore.RotateAsync(beforeLogout.RefreshToken, default);
        var loggingOut = store.RevokeAsync(user, beforeLogoutIdentity.SessionId, default);
        await Task.WhenAll(refreshing, loggingOut);
        Check.That(await store.FindByRefreshTokenAsync(beforeLogout.RefreshToken, default) is null &&
            (refreshing.Result is null || await fixture.FindAccessAsync(refreshing.Result.AccessToken, default) is null),
            "Logout racing a refresh must revoke the session rather than leave rotated access active.");
        await otherStore.RevokeUserAsync(user, default);
        var expiring = (await store.CreateAsync(user, "u", "stamp", false, default))!;
        var ttl = await database.KeyTimeToLiveAsync(key);
        Check.That(ttl > TimeSpan.Zero && ttl <= TimeSpan.FromDays(8), "Redis must expire the session automatically.");
        fixture.Clock.Advance(TimeSpan.FromDays(7));
        Check.That(await store.RotateAsync(expiring.RefreshToken, default) is null, "Refresh must expire at its exact boundary.");
        Check.That(await fixture.FindAccessAsync(expiring.AccessToken, default) is null, "Expired session cannot authenticate.");

        // 旧 Hash 键与新 JSON 键隔离，旧凭据失效，重新登录不发生 WRONGTYPE 或无效接管提示。
        var legacy = (await store.CreateAsync(user, "u", "stamp", false, default))!;
        var legacyIdentity = (await store.FindByRefreshTokenAsync(legacy.RefreshToken, default))!;
        await database.KeyDeleteAsync(key);
        var legacyKey = fixture.RedisOptions.KeyPrefix + $"user:{{{user:N}}}";
        await database.HashSetAsync(legacyKey, [new("session", legacyIdentity.SessionId.ToString("N")), new("accessId", "legacy-jti")]);
        await database.KeyExpireAsync(legacyKey, legacy.RefreshTokenExpiresAtUtc.UtcDateTime);
        Check.That(await store.FindByRefreshTokenAsync(legacy.RefreshToken, default) is null &&
            await store.RotateAsync(legacy.RefreshToken, default) is null &&
            await fixture.FindAccessAsync(legacy.AccessToken, default) is null, "Legacy Hash sessions must not authenticate or refresh.");
        Check.That(await store.CreateAsync(user, "u", "stamp", false, default) is not null, "Login must succeed alongside an incompatible legacy Hash without confirmation.");
        await store.RevokeAsync(user, legacyIdentity.SessionId, default);
        Check.That(await database.KeyTypeAsync(key) == RedisType.String && await database.KeyTypeAsync(legacyKey) == RedisType.Hash,
            "Migration must preserve the new session and leave old Hash cleanup to its TTL.");

        Console.WriteLine("PASS real Redis, cross-instance takeover/revoke, atomic refresh/replay, token checks and expiry");
    }
}
