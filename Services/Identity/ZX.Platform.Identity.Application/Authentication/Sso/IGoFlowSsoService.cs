namespace ZX.Platform.Identity.Application.Authentication.Sso;

public interface IGoFlowSsoService
{
    Task<GoFlowTicketIssuedDto> IssueAsync(Guid userId, Guid sessionId, IssueGoFlowTicketDto input,
        CancellationToken cancellationToken);
    Task<GoFlowTicketIdentityDto> RedeemAsync(string authenticatedClientId, RedeemGoFlowTicketDto input,
        CancellationToken cancellationToken);
}

public interface IGoFlowSsoTicketStore
{
    Task<bool> CreateAsync(string ticketHash, GoFlowSsoTicket ticket, CancellationToken cancellationToken);

    // 校验绑定信息与删除必须在同一个 Redis 原子操作中执行。
    // 错误 client / 回调 / PKCE 不消耗合法票据；并发兑换至多一个成功。
    Task<GoFlowSsoTicket?> ConsumeAsync(string ticketHash, string clientId, string redirectUri,
        string codeChallenge, CancellationToken cancellationToken);
}

public sealed record GoFlowSsoTicket(
    string ClientId, string RedirectUri, string CodeChallenge, Guid UserId, Guid SessionId,
    string CredentialStamp, DateTimeOffset ExpiresAtUtc, GoFlowSsoTargetDto Target);
