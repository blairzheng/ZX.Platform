namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class AccessTokenDto
{
    public string TokenType { get; init; } = "Bearer";
    public required string AccessToken { get; init; }
    public DateTimeOffset AccessTokenExpiresAtUtc { get; init; }
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}
