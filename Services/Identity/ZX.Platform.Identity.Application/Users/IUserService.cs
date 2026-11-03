using ZX.Platform.Identity.Application.Users.Dtos;

namespace ZX.Platform.Identity.Application.Users
{
    public interface IUserService
    {
        Task<Guid> CreateAsync(CreateUserDto createUser, CancellationToken cancellationToken);

        Task<UserDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken);

        Task<(List<UserDetailDto>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken);

        Task UpdateDisplayNameAsync(UpdateUserDisplayNameDto updateUserDisplayName, CancellationToken cancellationToken);

        Task ForceLogoutAsync(Guid id, CancellationToken cancellationToken);

        Task SetUserEnableAsync(Guid id, CancellationToken cancellationToken);

        Task SetUserDisableAsync(Guid id, CancellationToken cancellationToken);

        Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

        Task RestoreAsync(Guid id, CancellationToken cancellationToken);

        Task UpdateUserSbusAsync(UpdateUserSbuDto updateUserSbu, CancellationToken cancellationToken);

        Task BatchDeleteAsync(List<Guid> guids, CancellationToken cancellationToken);

        /// <summary>
        /// 管理员按工号只读查询目录，返回核对信息，不创建用户。
        /// </summary>
        Task<LdapUserLookupResultDto> LdapLookupAsync(LdapUserLookupQueryDto query, CancellationToken cancellationToken);

        /// <summary>
        /// 预开通 OA 用户：仅创建待审批记录，或一次完成创建、审批和授权。
        /// </summary>
        Task<LdapUserCreatedDto> CreateLdapAsync(Guid actorId, CreateLdapUserDto input, CancellationToken cancellationToken);

        /// <summary>
        /// 审批通过 OA 账号，并在同一事务中分配角色和 SBU。
        /// </summary>
        Task ApproveLdapAsync(Guid actorId, ApproveLdapUserDto input, CancellationToken cancellationToken);

        /// <summary>
        /// 拒绝 OA 开通申请。
        /// </summary>
        Task RejectLdapAsync(Guid actorId, RejectLdapUserDto input, CancellationToken cancellationToken);
    }
}
