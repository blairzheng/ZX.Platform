using ZX.Platform.Identity.Application.Authentication.Dtos;

namespace ZX.Platform.Identity.Application.Authentication;

public interface IAuthService
{
    Task<AuthTokens> LoginAsync(LoginDto input, CancellationToken cancellationToken);
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto input, CancellationToken cancellationToken);
}
