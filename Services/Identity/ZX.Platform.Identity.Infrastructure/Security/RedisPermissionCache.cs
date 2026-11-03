using System.Text.Json;
using StackExchange.Redis;
using ZX.Platform.Identity.Application.Authorization;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class RedisPermissionCache(IConnectionMultiplexer connection, RedisSessionOptions options) : IPermissionCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };
    private RedisKey VersionKey => $"{options.KeyPrefix}authorization:{{permissions}}:version";
    private RedisKey UserKey(Guid userId) => $"{options.KeyPrefix}authorization:{{permissions}}:user:{userId:N}";

    public async Task<CachedAuthorization> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            // 一次 MGET 同时读版本和用户权限；各实例使用同一份 Redis 状态。
            var values = await connection.GetDatabase().StringGetAsync([VersionKey, UserKey(userId)]).WaitAsync(cancellationToken);
            var version = values[0].IsNull ? "0" : values[0].ToString();
            if (values[1].IsNull) return new CachedAuthorization(version, null);
            var cached = JsonSerializer.Deserialize<StoredAuthorization>(values[1].ToString(), JsonOptions)
                ?? throw new JsonException("权限缓存不能为空。");
            return new CachedAuthorization(version, cached.Version == version ? cached.Snapshot : null);
        }
        catch (Exception exception) when (exception is RedisException or JsonException)
        {
            throw new AuthorizationUnavailableException(exception);
        }
    }

    public async Task StoreAsync(Guid userId, string version, AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            // 保留查询开始时的版本。并发改权后即便旧查询回填，也无法匹配新版本。
            await connection.GetDatabase().StringSetAsync(UserKey(userId), JsonSerializer.Serialize(new StoredAuthorization(version, snapshot)),
                new Expiration(TimeSpan.FromMinutes(5)), ValueCondition.Always).WaitAsync(cancellationToken);
        }
        catch (RedisException exception) { throw new AuthorizationUnavailableException(exception); }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        try { await connection.GetDatabase().StringIncrementAsync(VersionKey).WaitAsync(cancellationToken); }
        catch (RedisException exception) { throw new AuthorizationUnavailableException(exception); }
    }

    private sealed record StoredAuthorization(string Version, AuthorizationSnapshot Snapshot);
}
