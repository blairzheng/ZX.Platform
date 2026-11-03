namespace ZX.Platform.Identity.Application.Authentication;

// 仅供服务端流程传递；Controller 不直接序列化 RefreshToken。
public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public sealed record SessionIdentity(Guid SessionId, Guid UserId, string Account, string CredentialStamp);
