using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class JwtAccessTokenIssuer(JwtOptions options)
{
    private readonly SigningCredentials _credentials = new(
        options.CreateValidationParameters().IssuerSigningKey, SecurityAlgorithms.HmacSha256);

    public string Create(Guid userId, Guid sessionId, string account, string tokenId,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("name", account),
                new Claim("sid", sessionId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId),
                new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);
        token.Header["typ"] = "at+jwt";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
