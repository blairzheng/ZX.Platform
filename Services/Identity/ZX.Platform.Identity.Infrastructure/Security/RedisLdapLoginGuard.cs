using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 按规范工号限制 OA 登录失败次数。键只保存工号摘要，不保存密码，多个实例共享计数。
/// </summary>
public sealed class RedisLdapLoginGuard(
    IConnectionMultiplexer connection,
    LdapOptions options,
    RedisSessionOptions redisOptions) : ILdapLoginGuard
{
    /// <summary>
    /// 递增与补 TTL 必须原子执行：分两步时，第一次 INCR 成功但设置过期失败（或进程退出）
    /// 会留下没有 TTL 的计数键，达到阈值后永远返回 429。
    /// </summary>
    private const string RecordFailureScript = """
        local count = redis.call('INCR', KEYS[1])
        if redis.call('TTL', KEYS[1]) < 0 then
          redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    /// <summary>
    /// 检查阶段同样要原子地补 TTL：旧版本可能已经留下了“达到阈值但没有过期时间”的键，
    /// 只在递增时补 TTL 的话，这类键会永远拦住该工号，永远走不到递增。
    /// 先补窗口再返回计数，保证 429 最多持续一个失败窗口。
    /// </summary>
    private const string EnsureAllowedScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then
          return 0
        end
        if redis.call('TTL', KEYS[1]) < 0 then
          redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return tonumber(redis.call('GET', KEYS[1]) or '0')
        """;

    public async Task EnsureAllowedAsync(string employeeNo, CancellationToken cancellationToken)
    {
        try
        {
            var value = await connection.GetDatabase()
                .ScriptEvaluateAsync(
                    EnsureAllowedScript,
                    [Key(employeeNo)],
                    [options.FailureWindowSeconds])
                .WaitAsync(cancellationToken);
            if ((long)value >= options.MaxFailedAttempts)
                throw new BusinessException(ErrorCodes.TooManyRequests, "Auth.LdapTooManyAttempts",
                    new { Reason = "TooManyRequests" });
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    public async Task RecordFailureAsync(string employeeNo, CancellationToken cancellationToken)
    {
        try
        {
            await connection.GetDatabase()
                .ScriptEvaluateAsync(
                    RecordFailureScript,
                    [Key(employeeNo)],
                    [options.FailureWindowSeconds])
                .WaitAsync(cancellationToken);
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    public async Task ResetAsync(string employeeNo, CancellationToken cancellationToken)
    {
        try
        {
            await connection.GetDatabase().KeyDeleteAsync(Key(employeeNo)).WaitAsync(cancellationToken);
        }
        catch (RedisException exception)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    // 工号摘要作为计数键，避免把工号明文写进 Redis 键并从键名枚举员工。
    private RedisKey Key(string employeeNo)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(employeeNo.Trim().ToUpperInvariant())));
        return $"{redisOptions.KeyPrefix}ldap-fail:{digest[..32]}";
    }
}
