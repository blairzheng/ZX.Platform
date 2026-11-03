using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class JwtOptions
{
    [Required]
    public string Issuer { get; set; } = "ZX.Platform.Identity";

    [Required]
    public string Audience { get; set; } = "ZX.Platform";

    // Base64 编码的随机密钥；Development 提供公开开发默认值，生产通过安全配置注入独立密钥。
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    public TokenValidationParameters CreateValidationParameters()
    {
        byte[] key;
        try { key = Convert.FromBase64String(SigningKey); }
        catch (FormatException)
        {
            throw new InvalidOperationException("AuthenticationJwt:SigningKey 必须为 Base64 编码的随机密钥。");
        }
        if (key.Length < 32)
            throw new InvalidOperationException("AuthenticationJwt:SigningKey 解码后至少需要 32 字节。");

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = ["at+jwt"],
            NameClaimType = "name"
        };
    }
}
