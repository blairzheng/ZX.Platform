using System.Security.Cryptography;
using System.Text;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Authentication.Sso;

public sealed class GoFlowSsoService(
    GoFlowSsoOptions options, IGoFlowSsoTicketStore tickets, IAuthSessionStore sessions,
    IUserRepository users, IPermissionService permissions, TimeProvider timeProvider) : IGoFlowSsoService
{
    public async Task<GoFlowTicketIssuedDto> IssueAsync(Guid userId, Guid sessionId,
        IssueGoFlowTicketDto input, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!string.Equals(input.RedirectUri, options.CallbackUri, StringComparison.Ordinal) ||
            input.Target is null || input.Target.Page != "home")
            throw new BusinessException(ErrorCodes.ValidationFailed, "Sso.RequestInvalid");

        var user = await RequireUserAsync(userId, cancellationToken);
        var stamp = AuthService.CredentialStamp(user);
        if (!await sessions.IsSessionActiveAsync(user.Id, sessionId, stamp, cancellationToken))
            throw new BusinessException(ErrorCodes.Unauthorized, "Auth.SessionInvalid");

        var expires = timeProvider.GetUtcNow().AddSeconds(options.TicketLifetimeSeconds);
        var ticket = Base64Url(RandomNumberGenerator.GetBytes(32));
        var stored = new GoFlowSsoTicket(options.ClientId, options.CallbackUri, input.CodeChallenge,
            user.Id, sessionId, stamp, expires, input.Target);
        if (!await tickets.CreateAsync(Hash(ticket), stored, cancellationToken))
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Sso.Unavailable");

        // 回调完全来自服务端配置，绝不接受任意 returnUrl。
        var redirect = $"{options.CallbackUri}?ticket={Uri.EscapeDataString(ticket)}&state={Uri.EscapeDataString(input.State)}";
        return new GoFlowTicketIssuedDto(redirect, expires);
    }

    public async Task<GoFlowTicketIdentityDto> RedeemAsync(string authenticatedClientId,
        RedeemGoFlowTicketDto input, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!string.Equals(authenticatedClientId, options.ClientId, StringComparison.Ordinal))
            throw new BusinessException(ErrorCodes.Unauthorized, "Sso.ClientInvalid");
        if (!string.Equals(input.RedirectUri, options.CallbackUri, StringComparison.Ordinal))
            throw InvalidTicket();

        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(input.CodeVerifier)));
        var ticket = await tickets.ConsumeAsync(Hash(input.Ticket), authenticatedClientId,
            input.RedirectUri, challenge, cancellationToken);
        if (ticket is null || ticket.ExpiresAtUtc <= timeProvider.GetUtcNow())
            throw InvalidTicket();

        // 先消费再核对当前状态：停用、撤权、退出、接管登录及安全版本改变均拒绝兑换。
        var user = await RequireUserAsync(ticket.UserId, cancellationToken);
        if (ticket.CredentialStamp != AuthService.CredentialStamp(user) ||
            !await sessions.IsSessionActiveAsync(user.Id, ticket.SessionId, ticket.CredentialStamp, cancellationToken))
            throw InvalidTicket();

        return new GoFlowTicketIdentityDto(options.ClientId, user.Id, user.EmployeeNo!,
            user.LdapProvider!, user.LdapSubject!, user.DisplayName, ticket.Target);
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsLdap || !user.HasBusinessAccess ||
            string.IsNullOrWhiteSpace(user.EmployeeNo) || string.IsNullOrWhiteSpace(user.LdapProvider) ||
            string.IsNullOrWhiteSpace(user.LdapSubject) ||
            !await permissions.HasAsync(userId, IdentityPermissions.GoFlowOpen, cancellationToken))
            throw new BusinessException(ErrorCodes.Forbidden, "Sso.UserNotAllowed");
        return user;
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Sso.Unavailable");
    }

    // 服务凭据已经认证通过；票据/PKCE 失败属于无效兑换请求，不发起 MOM Bearer 登录挑战。
    private static BusinessException InvalidTicket() => new(ErrorCodes.ValidationFailed, "Sso.TicketInvalid");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
