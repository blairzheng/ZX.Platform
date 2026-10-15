using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using ZX.Platform.Identity.Application.Authentication;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class RedisAuthSessionStore(
    IConnectionMultiplexer connection,
    AuthOptions options,
    RedisSessionOptions redisOptions,
    JwtAccessTokenIssuer accessTokens,
    TimeProvider timeProvider) : IAuthSessionStore
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new() { RespectRequiredConstructorParameters = true };

    public async Task<AuthTokens?> CreateAsync(Guid userId, string account, string credentialStamp, bool replaceExisting, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = SessionKey(userId);
        var now = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        if (!replaceExisting)
        {
            var (_, existing) = await ReadSessionAsync(key, cancellationToken);
            if (existing is not null && existing.ExpiresAtUtc > now)
                return null;
        }

        var sessionId = Guid.NewGuid();
        var tokenId = Guid.NewGuid().ToString("N");
        var refresh = NewRefreshToken(userId, sessionId);
        var expires = now.AddDays(options.RefreshTokenDays);
        var accessExpires = now.AddMinutes(options.AccessTokenMinutes);
        if (accessExpires > expires) accessExpires = expires;
        var access = accessTokens.Create(userId, sessionId, account, tokenId, now, accessExpires);

        var session = new StoredSession(sessionId, account, tokenId, Hash(refresh), accessExpires, expires, credentialStamp, []);
        try
        {
            // 一次 SET 保存 JSON 并设置到期时间；同时登录时最后写入的会话生效。
            await connection.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(session),
                new Expiration(expires.UtcDateTime), ValueCondition.Always).WaitAsync(cancellationToken);
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }

        return new AuthTokens(access, accessExpires, refresh, expires);
    }

    public async Task<bool> IsAccessTokenActiveAsync(Guid userId, Guid sessionId, string tokenId, CancellationToken cancellationToken)
    {
        var (_, session) = await ReadSessionAsync(SessionKey(userId), cancellationToken);
        var now = timeProvider.GetUtcNow();
        return session is not null && session.SessionId == sessionId &&
            session.AccessId == tokenId && session.AccessExpiresAtUtc > now && session.ExpiresAtUtc > now;
    }

    public async Task<SessionIdentity?> FindByRefreshTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryReadRefreshToken(token, out var userId, out var sessionId)) return null;
        var refreshHash = Hash(token);
        var (_, session) = await ReadSessionAsync(SessionKey(userId), cancellationToken);
        if (session is null || session.SessionId != sessionId || session.ExpiresAtUtc <= timeProvider.GetUtcNow())
            return null;
        // 已消费凭据也交给刷新流程识别，后续轮换会撤销发生重放的会话。
        if (session.RefreshHash != refreshHash && !session.UsedRefreshHashes.Contains(refreshHash)) return null;
        return new SessionIdentity(sessionId, userId, session.Account, session.CredentialStamp);
    }

    public async Task<AuthTokens?> RotateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!TryReadRefreshToken(refreshToken, out var userId, out var sessionId)) return null;
        var key = SessionKey(userId);
        var refreshHash = Hash(refreshToken);
        var (value, session) = await ReadSessionAsync(key, cancellationToken);
        var now = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        if (session is null || session.SessionId != sessionId || session.ExpiresAtUtc <= now)
            return null;
        if (session.RefreshHash != refreshHash)
        {
            if (session.UsedRefreshHashes.Contains(refreshHash))
                await DeleteSessionAsync(key, sessionId, refreshHash, cancellationToken);
            return null;
        }

        var tokenId = Guid.NewGuid().ToString("N");
        var refresh = NewRefreshToken(userId, sessionId);
        var expires = session.ExpiresAtUtc;
        var accessExpires = now.AddMinutes(options.AccessTokenMinutes);
        if (accessExpires > expires) accessExpires = expires;
        var access = accessTokens.Create(userId, sessionId, session.Account, tokenId, now, accessExpires);

        var updated = session with
        {
            AccessId = tokenId,
            RefreshHash = Hash(refresh),
            AccessExpiresAtUtc = accessExpires,
            UsedRefreshHashes = [.. session.UsedRefreshHashes, refreshHash]
        };
        // 读取后的会话必须未被修改，防止旧刷新覆盖新登录或重复消费凭据。
        var transaction = connection.GetDatabase().CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, value));
        var save = transaction.StringSetAsync(key, JsonSerializer.Serialize(updated),
            new Expiration(expires.UtcDateTime), ValueCondition.Always);
        if (!await CommitAsync(transaction, cancellationToken, save))
        {
            // 可能已被另一个刷新请求消费；只撤销存在该重放记录的原会话。
            await DeleteSessionAsync(key, sessionId, refreshHash, cancellationToken);
            return null;
        }

        return new AuthTokens(access, accessExpires, refresh, expires);
    }

    public Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        DeleteSessionAsync(SessionKey(userId), sessionId, null, cancellationToken);

    public async Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await connection.GetDatabase().KeyDeleteAsync(SessionKey(userId)).WaitAsync(cancellationToken);
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    private async Task DeleteSessionAsync(RedisKey key, Guid sessionId, string? usedRefreshHash, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (value, session) = await ReadSessionAsync(key, cancellationToken);
            // 旧设备退出或重放只能撤销自己的会话，不能删除之后的新登录。
            if (session is null || session.SessionId != sessionId ||
                (usedRefreshHash is not null && !session.UsedRefreshHashes.Contains(usedRefreshHash)))
                return;
            var transaction = connection.GetDatabase().CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(key, value));
            var delete = transaction.KeyDeleteAsync(key);
            if (await CommitAsync(transaction, cancellationToken, delete))
                return;
            // 条件不符或 WATCH 冲突表示未提交，重新读取；执行结果不确定的故障不重试。
        }
        throw new AuthSessionUnavailableException(new InvalidOperationException("Redis 会话持续发生并发修改，请重试下线。"));
    }

    private async Task<(RedisValue Value, StoredSession? Session)> ReadSessionAsync(RedisKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = await connection.GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken);
            if (value.IsNull) return (value, null);
            var session = JsonSerializer.Deserialize<StoredSession>(value.ToString(), SessionJsonOptions)
                ?? throw new JsonException("Redis 会话 JSON 不能为空。");
            return (value, session);
        }
        catch (Exception exception) when (exception is RedisException or JsonException)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    private static async Task<bool> CommitAsync(ITransaction transaction, CancellationToken cancellationToken, params Task[] commands)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var commit = transaction.ExecuteAsync();
            try
            {
                // 同时观察提交结果和命令结果，Redis 错误不能被当成条件不满足。
                await Task.WhenAll([commit, .. commands]).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                commit.IsCompletedSuccessfully && !commit.Result)
            {
                // 条件不满足或 WATCH 冲突时，客户端会取消队列里的命令任务。
                return false;
            }
            return await commit;
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    // 使用新键名隔离旧版 Hash 会话，旧键由原 TTL 自动清理。
    private RedisKey SessionKey(Guid userId) => $"{redisOptions.KeyPrefix}session:{{{userId:N}}}";

    private sealed record StoredSession(Guid SessionId, string Account, string AccessId, string RefreshHash,
        DateTimeOffset AccessExpiresAtUtc, DateTimeOffset ExpiresAtUtc, string CredentialStamp, string[] UsedRefreshHashes);

    // Refresh 使用随机秘密；GUID 只定位 Redis 键，仍须匹配完整凭据的哈希。
    private static string NewRefreshToken(Guid userId, Guid sessionId) =>
        $"{userId:N}.{sessionId:N}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    private static bool TryReadRefreshToken(string token, out Guid userId, out Guid sessionId)
    {
        userId = sessionId = default;
        return token.Length == 109 && token[32] == '.' && token[65] == '.' &&
            Guid.TryParseExact(token.AsSpan(0, 32), "N", out userId) &&
            Guid.TryParseExact(token.AsSpan(33, 32), "N", out sessionId);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
