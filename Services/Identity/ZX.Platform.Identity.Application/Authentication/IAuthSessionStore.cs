namespace ZX.Platform.Identity.Application.Authentication;

public interface IAuthSessionStore
{
    // 未确认接管且查到已有有效会话时返回 null；同时登录都通过检查时，最后写入者生效。
    Task<AuthTokens?> CreateAsync(Guid userId, string account, string credentialStamp, bool replaceExisting, CancellationToken cancellationToken);
    // 仅供 JWT 标准校验通过后调用；核对签名中身份对应的当前会话和 Access jti。
    Task<bool> IsAccessTokenActiveAsync(Guid userId, Guid sessionId, string tokenId, CancellationToken cancellationToken);
    Task<SessionIdentity?> FindByRefreshTokenAsync(string token, CancellationToken cancellationToken);

    // 原子消费并轮换；重放已消费凭据时撤销整个会话。
    Task<AuthTokens?> RotateAsync(string refreshToken, CancellationToken cancellationToken);
    // 退出只撤销指定会话，避免旧设备的退出请求踢掉新登录。
    Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken);
}
