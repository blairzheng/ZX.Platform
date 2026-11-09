using System.Text.Json;
using StackExchange.Redis;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authentication.Sso;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class RedisGoFlowSsoTicketStore(
    IConnectionMultiplexer connection, RedisSessionOptions redisOptions) : IGoFlowSsoTicketStore
{
    private const string ConsumeScript = """
        local value = redis.call('GET', KEYS[1])
        if not value then return nil end
        local ticket = cjson.decode(value)
        if ticket.ClientId ~= ARGV[1] or ticket.RedirectUri ~= ARGV[2]
          or ticket.CodeChallenge ~= ARGV[3] then
          return nil
        end
        redis.call('DEL', KEYS[1])
        return value
        """;

    public async Task<bool> CreateAsync(string ticketHash, GoFlowSsoTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await connection.GetDatabase().StringSetAsync(Key(ticketHash), JsonSerializer.Serialize(ticket),
                new Expiration(ticket.ExpiresAtUtc.UtcDateTime), ValueCondition.NotExists).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            throw new AuthSessionUnavailableException(exception);
        }
    }

    public async Task<GoFlowSsoTicket?> ConsumeAsync(string ticketHash, string clientId, string redirectUri,
        string codeChallenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await connection.GetDatabase().ScriptEvaluateAsync(ConsumeScript,
                [Key(ticketHash)], [clientId, redirectUri, codeChallenge]).WaitAsync(cancellationToken);
            return result.IsNull ? null : JsonSerializer.Deserialize<GoFlowSsoTicket>((string)result!);
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            // 超时后不自动重试兑换；可能已消费，应重新发起登录。
            throw new AuthSessionUnavailableException(exception);
        }
    }

    private RedisKey Key(string ticketHash) => $"{redisOptions.KeyPrefix}sso:goflow:ticket:{ticketHash}";
}
