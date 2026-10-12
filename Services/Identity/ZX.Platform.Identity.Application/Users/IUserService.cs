using ZX.Platform.Identity.Application.Users.Dtos;

namespace ZX.Platform.Identity.Application.Users
{
    public interface IUserService
    {
        Task<Guid> CreateAsync(CreateUserDto createUser, CancellationToken cancellationToken);

        Task<UserDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken);

        Task<(List<UserDetailDto>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken);

        Task UpdateDisplayNameAsync(UpdateUserDisplayNameDto updateUserDisplayName, CancellationToken cancellationToken);

        Task UpdatePasswordAsync(UpdateUserPasswordDto updateUserPassword, CancellationToken cancellationToken);

        Task SetUserEnableAsync(Guid id, CancellationToken cancellationToken);

        Task SetUserDisableAsync(Guid id, CancellationToken cancellationToken);

        Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

        Task RestoreAsync(Guid id, CancellationToken cancellationToken);

        Task UpdateUserFactoriesAsync(UpdateUserFactoryDto updateUserFactory, CancellationToken cancellationToken);
    }
}
