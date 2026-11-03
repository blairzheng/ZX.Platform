namespace ZX.Platform.Identity.Application.Authentication;

/// <summary>
/// OA 登录的服务端结果。仅 <see cref="Dtos.LdapLoginStatus.Authenticated"/> 携带令牌，
/// Controller 负责把 AccessToken 返回浏览器、把 RefreshToken 写入 HttpOnly Cookie。
/// </summary>
public sealed record LdapLoginOutcome(string Status, bool Created, AuthTokens Tokens);
