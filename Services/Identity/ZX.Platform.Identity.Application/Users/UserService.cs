using System.Diagnostics;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Users
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserSbuRepository _userSbuRepository;
        private readonly ISbuRepository _sbuRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuthSessionStore _sessions;
        private readonly IPermissionCache _permissionCache;
        private readonly IPermissionService _permissions;
        private readonly IUserApprovalAuditRepository _approvalAudits;
        private readonly TimeProvider _timeProvider;

        public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher,
            ISbuRepository sbuRepository, IUserSbuRepository userSbuRepository, IAuthSessionStore sessions,
            IPermissionCache permissionCache, IRoleRepository roleRepository, IPermissionService permissions,
            IUserApprovalAuditRepository approvalAudits, TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _sbuRepository = sbuRepository;
            _userSbuRepository = userSbuRepository;
            _sessions = sessions;
            _permissionCache = permissionCache;
            _roleRepository = roleRepository;
            _permissions = permissions;
            _approvalAudits = approvalAudits;
            _timeProvider = timeProvider;
        }

        public async Task<Guid> CreateAsync(CreateUserDto createUser, CancellationToken cancellationToken)
        {
            var account = createUser.Account.Trim();
            var normalizedAccount = account.ToUpperInvariant();
            var existingUser = await _userRepository.GetByNormalizedAccountAsync(normalizedAccount, cancellationToken);
            if (existingUser != null)
            {
                if (existingUser.IsDeleted)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "User.AccountDeleted",
                        new
                        {
                            Reason = "AccountDeleted",
                            UserId = existingUser.Id
                        });

                if (!existingUser.IsEnabled)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "User.AccountDisabled",
                        new
                        {
                            Reason = "AccountDisabled",
                            UserId = existingUser.Id
                        });
                throw new BusinessException(
                    ErrorCodes.Conflict,
                    "User.AccountExists"
                   );
            }
            var passwordHash = _passwordHasher.Hash(createUser.Password);
            var user = User.CreateLocal(account, createUser.DisplayName, passwordHash);
            await GetValidatedSbusAsync(createUser.SbuIds, cancellationToken);
            if (createUser.SbuIds.Count > 0)
            {
                foreach (var item in createUser.SbuIds)
                {
                    var userSbu = UserSbu.Create(user.Id, item, false);
                    _userSbuRepository.Add(userSbu);
                }
            }

            _userRepository.Add(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return user.Id;
        }

        public async Task<UserDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken)
        {
            var data = await _userRepository.GetByIdAsync(guid, cancellationToken);

            if (data == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            var userSbus = await _userSbuRepository.GetByUserIdAsync(data.Id, cancellationToken);
            return ToDetail(data, userSbus.Select(f => f.SbuId));
        }

        public async Task<(List<UserDetailDto>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken)
        {
            if (userQuery.CreatedFromUtc.HasValue && userQuery.CreatedToUtc.HasValue &&
                userQuery.CreatedFromUtc.Value >= userQuery.CreatedToUtc.Value)
            {
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.CreatedAtRangeInvalid");
            }

            if (!string.IsNullOrWhiteSpace(userQuery.AuthType) && !UserAuthType.IsValid(userQuery.AuthType.Trim()))
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.AuthTypeInvalid");

            if (!string.IsNullOrWhiteSpace(userQuery.ApprovalStatus) &&
                !UserApprovalStatus.IsValid(userQuery.ApprovalStatus.Trim()))
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.ApprovalStatusInvalid");

            var (list, count) = await _userRepository.GetPageListAsync(userQuery, cancellationToken);
            var userSbus = await _userSbuRepository.GetByUserIdsAsync(list.Select(user => user.Id).ToList(), cancellationToken);
            var sbuIdsByUser = userSbus.ToLookup(relation => relation.UserId, relation => relation.SbuId);
            var users = new List<UserDetailDto>(list.Count);
            foreach (var user in list)
            {
                users.Add(ToDetail(user, sbuIdsByUser[user.Id]));
            }
            return (users, count);
        }

        public async Task SetUserDisableAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            user.Disable();
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                await Task.WhenAll(_sessions.RevokeUserAsync(id, CancellationToken.None), _permissionCache.InvalidateAsync(CancellationToken.None));
            }
        }

        public async Task SetUserEnableAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            // 启用只恢复登录资格；待审批 OA 账号仍没有业务授权，已拒绝账号必须重新审批。
            user.Enable();
            try { await _unitOfWork.SaveChangesAsync(cancellationToken); }
            finally { await _permissionCache.InvalidateAsync(CancellationToken.None); }
        }

        public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            user.MarkDeleted();
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                await Task.WhenAll(_sessions.RevokeUserAsync(id, CancellationToken.None), _permissionCache.InvalidateAsync(CancellationToken.None));
            }
        }

        public async Task UpdateDisplayNameAsync(UpdateUserDisplayNameDto updateUserDisplayName, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(updateUserDisplayName.Id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            user.ChangeDisplayName(updateUserDisplayName.DisplayName);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task ForceLogoutAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdIncludeDeletedAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            await _sessions.RevokeUserAsync(id, cancellationToken);
        }

        public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdIncludeDeletedAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            if (!user.IsDeleted) throw new BusinessException(ErrorCodes.Conflict, "User.NotDeleted");
            // 只恢复原有身份和审批状态，OA 账号不会因为恢复而自动开通。
            user.Restore();
            try { await _unitOfWork.SaveChangesAsync(cancellationToken); }
            finally { await _permissionCache.InvalidateAsync(CancellationToken.None); }
        }

        public async Task UpdateUserSbusAsync(UpdateUserSbuDto updateUserSbu, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(updateUserSbu.UserId, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            // 获取当前用户已授权的SBU列表
            var userSbus = await _userSbuRepository.GetByUserIdAsync(updateUserSbu.UserId, cancellationToken);

            // 校验传入的SBU数据是否有效，并获取新列表中的SBU信息
            var sbus = await GetValidatedSbusAsync(updateUserSbu.SbuIds, cancellationToken);
            ApplySbuRelations(updateUserSbu.UserId, sbus, userSbus);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task BatchDeleteAsync(List<Guid> guids, CancellationToken cancellationToken)
        {
            if (guids == null || guids.Count == 0)
            {
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.BatchDeleteNoIds");
            }
            var users = await _userRepository.GetUsersAsync(guids, cancellationToken);
            foreach (var user in users)
            {
                user.MarkDeleted();
            }
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                // 独立 Redis 操作全部发起，避免一个撤销失败后跳过后续用户。
                await Task.WhenAll(users.Select(user =>
                    _sessions.RevokeUserAsync(user.Id, CancellationToken.None)).Append(_permissionCache.InvalidateAsync(CancellationToken.None)));
            }
        }

        #region OA 账号预开通与审批

        public async Task<LdapUserCreatedDto> CreateLdapAsync(
            Guid actorId, CreateLdapUserDto input, CancellationToken cancellationToken)
        {
            var employeeNo = NormalizeEmployeeNo(input.EmployeeNo);
            if (!input.OpenImmediately && (input.RoleIds.Count > 0 || input.SbuIds.Count > 0))
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.PendingCannotAssignGrants");

            // 一次创建并开通需要审批、角色和 SBU 分配权限，缺少任一项都拒绝。
            if (input.OpenImmediately)
            {
                await EnsurePermissionsAsync(actorId, cancellationToken,
                    IdentityPermissions.UserApprove,
                    IdentityPermissions.UserAssignRoles,
                    IdentityPermissions.UserAssignSbus);
            }

            // 预开通只检查 MOM 内的账号占用，不连接 LDAP，也不把管理员输入当成已认证身份。
            var existingUser = await FindExistingForProvisionAsync(employeeNo, cancellationToken);
            if (existingUser is not null)
                throw ProvisionConflict(existingUser.IsLdap
                    ? LdapProvisionReasons.AccountExists : LdapProvisionReasons.AccountTypeConflict,
                    existingUser.Id);

            var user = User.CreateLdapProvision(employeeNo);

            var roles = input.OpenImmediately
                ? await GetValidatedRolesAsync(actorId, input.RoleIds, cancellationToken)
                : [];
            var sbus = input.OpenImmediately
                ? await GetApprovalSbusAsync(input.SbuIds, cancellationToken)
                : [];

            if (input.OpenImmediately)
                user.ApproveLdap(actorId, _timeProvider.GetUtcNow(), input.Reason);

            _userRepository.Add(user);
            foreach (var role in roles)
                _roleRepository.AddUserRole(UserRole.Create(user.Id, role.Id));
            foreach (var sbu in sbus)
                _userSbuRepository.Add(UserSbu.Create(user.Id, sbu.Id, false));

            // 预开通同样要留痕：一次开通和“先建待审批”是两种不同的授权动作。
            _approvalAudits.Add(UserApprovalAudit.CreateForNoneStatus(
                user,
                UserApprovalActions.Provisioned,
                actorId,
                input.Reason,
                roles.Select(role => role.Id),
                sbus.Select(sbu => sbu.Id),
                CurrentTraceId()));

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (UniqueConstraintException)
            {
                _userRepository.Detach(user);
                var existing = await FindExistingForProvisionAsync(employeeNo, cancellationToken);
                throw ProvisionConflict(existing is null
                    ? LdapProvisionReasons.IdentityConflict
                    : existing.IsLdap ? LdapProvisionReasons.AccountExists : LdapProvisionReasons.AccountTypeConflict,
                    existing?.Id);
            }
            finally
            {
                if (input.OpenImmediately)
                    await _permissionCache.InvalidateAsync(CancellationToken.None);
            }

            return new LdapUserCreatedDto
            {
                Id = user.Id,
                ApprovalStatus = user.ApprovalStatus,
                IsEnabled = user.IsEnabled,
                SecurityVersion = user.SecurityVersion
            };
        }

        public async Task ApproveLdapAsync(
            Guid actorId, ApproveLdapUserDto input, CancellationToken cancellationToken)
        {
            await EnsurePermissionsAsync(actorId, cancellationToken,
                IdentityPermissions.UserApprove,
                IdentityPermissions.UserAssignRoles,
                IdentityPermissions.UserAssignSbus);

            var user = await _userRepository.GetByIdIncludeDeletedAsync(input.Id, cancellationToken)
                ?? throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            if (!user.IsLdap || user.IsDeleted)
                throw new BusinessException(ErrorCodes.InvalidState, "User.NotReviewable",
                    new { Reason = "StateChanged" });
            // 已通过的账号不能再走审批：否则被禁用的账号可以借审批重新启用，
            // 绕过 identity.user.status。重新启用请走原启用接口。
            if (user.ApprovalStatus == UserApprovalStatus.Approved)
                throw ChangedState();

            EnsureExpectedVersion(user, input.ExpectedSecurityVersion);

            var roles = await GetValidatedRolesAsync(actorId, input.RoleIds, cancellationToken);
            var sbus = await GetApprovalSbusAsync(input.SbuIds, cancellationToken);
            var existingRoles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
            // 审批会整体替换角色，必须复用角色接口的完整保护：
            // 目标是现任管理员时只有管理员能改，也不能移除自己的管理员身份。
            await EnsureRoleChangeAllowedAsync(actorId, user.Id, roles, cancellationToken);

            // 记录审批前状态，重新审核与被拒绝后通过要能和首次审批区分开。
            var fromStatus = user.ApprovalStatus;
            user.ApproveLdap(actorId, _timeProvider.GetUtcNow(), input.Reason);
            ApplyRoleRelations(user.Id, roles, existingRoles);
            var existingSbus = await _userSbuRepository.GetByUserIdAsync(user.Id, cancellationToken);
            ApplySbuRelations(user.Id, sbus, existingSbus);

            _approvalAudits.Add(UserApprovalAudit.Create(
                user,
                fromStatus == UserApprovalStatus.Rejected
                    ? UserApprovalActions.ReApproved
                    : UserApprovalActions.Approved,
                fromStatus,
                actorId,
                input.Reason,
                roles.Select(role => role.Id),
                sbus.Select(sbu => sbu.Id),
                CurrentTraceId()));

            await SaveReviewAsync(user.Id, cancellationToken);
        }

        public async Task RejectLdapAsync(
            Guid actorId, RejectLdapUserDto input, CancellationToken cancellationToken)
        {
            await EnsurePermissionsAsync(actorId, cancellationToken, IdentityPermissions.UserApprove);

            var user = await _userRepository.GetByIdIncludeDeletedAsync(input.Id, cancellationToken)
                ?? throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            if (!user.IsLdap || user.IsDeleted || user.ApprovalStatus == UserApprovalStatus.Approved)
                throw new BusinessException(ErrorCodes.InvalidState, "User.NotReviewable",
                    new { Reason = "StateChanged" });

            EnsureExpectedVersion(user, input.ExpectedSecurityVersion);

            var fromStatus = user.ApprovalStatus;
            user.RejectLdap(actorId, _timeProvider.GetUtcNow(), input.Reason);

            _approvalAudits.Add(UserApprovalAudit.Create(
                user,
                UserApprovalActions.Rejected,
                fromStatus,
                actorId,
                input.Reason,
                [],
                [],
                CurrentTraceId()));

            await SaveReviewAsync(user.Id, cancellationToken);
        }

        #endregion

        #region 校验并获取传入的SBU数据
        private async Task<List<Sbu>> GetValidatedSbusAsync(List<Guid> sbuIds, CancellationToken cancellationToken)
        {
            var sbus = await _sbuRepository.GetEnabledSbusByIdsAsync(sbuIds, cancellationToken);
            if (sbus.Count != sbuIds.Count)
            {
                throw new BusinessException(ErrorCodes.NotFound, "User.SbusUnavailable");
            }
            return sbus;
        }

        /// <summary>
        /// 审批开通至少需要一个有效 SBU；空 SBU 不能解释成全部数据范围。
        /// </summary>
        private async Task<List<Sbu>> GetApprovalSbusAsync(List<Guid> sbuIds, CancellationToken cancellationToken)
        {
            var ids = sbuIds.Distinct().ToList();
            if (ids.Count == 0)
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.ApprovalSbuRequired");
            return await GetValidatedSbusAsync(ids, cancellationToken);
        }

        private async Task<List<Role>> GetValidatedRolesAsync(
            Guid actorId, List<Guid> roleIds, CancellationToken cancellationToken)
        {
            var ids = roleIds.Distinct().ToHashSet();
            if (ids.Count == 0)
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.ApprovalRoleRequired");

            var roles = await _roleRepository.GetByIdsAsync(ids, cancellationToken);
            if (roles.Count != ids.Count || roles.Any(role => !role.IsEnabled))
                throw new BusinessException(ErrorCodes.ValidationFailed, "Access.RoleUnavailable");

            // 不能通过创建或审批默认获得管理员能力。
            if (roles.Any(role => role.IsBuiltIn && role.Code == Role.AdministratorCode) &&
                !(await _permissions.GetAsync(actorId, cancellationToken)).IsAdministrator)
                throw new BusinessException(ErrorCodes.Forbidden, "Auth.Forbidden");

            return roles;
        }

        /// <summary>
        /// 审批会整体替换目标账号的角色，因此复用角色接口的保护规则：
        /// 目标是现任管理员或将被设为管理员时，只有管理员能执行；也不能移除自己的管理员身份。
        /// </summary>
        private async Task EnsureRoleChangeAllowedAsync(
            Guid actorId, Guid targetUserId, IReadOnlyCollection<Role> roles, CancellationToken cancellationToken)
        {
            var wasAdministrator =
                (await _roleRepository.GetAdministratorUserIdsAsync([targetUserId], cancellationToken)).Count != 0;
            var becomesAdministrator =
                roles.Any(role => role.IsBuiltIn && role.Code == Role.AdministratorCode);
            if (!wasAdministrator && !becomesAdministrator)
                return;

            if (!(await _permissions.GetAsync(actorId, cancellationToken)).IsAdministrator)
                throw new BusinessException(ErrorCodes.Forbidden, "Auth.Forbidden");
            if (actorId == targetUserId && wasAdministrator && !becomesAdministrator)
                throw new BusinessException(ErrorCodes.InvalidState, "Access.CannotRemoveOwnAdministrator");
        }

        private void ApplyRoleRelations(Guid userId, List<Role> roles, List<UserRole> existing)
        {
            var target = roles.Select(role => role.Id).ToHashSet();
            foreach (var relation in existing.Where(item => !target.Contains(item.RoleId)))
                _roleRepository.RemoveUserRole(relation);
            var current = existing.Select(item => item.RoleId).ToHashSet();
            foreach (var id in target.Where(id => !current.Contains(id)))
                _roleRepository.AddUserRole(UserRole.Create(userId, id));
        }

        private void ApplySbuRelations(Guid userId, List<Sbu> sbus, List<UserSbu> existing)
        {
            var target = sbus.Select(sbu => sbu.Id).ToHashSet();
            var current = existing.Select(item => item.SbuId).ToHashSet();
            foreach (var sbu in sbus.Where(sbu => !current.Contains(sbu.Id)))
                _userSbuRepository.Add(UserSbu.Create(userId, sbu.Id, false));
            foreach (var relation in existing.Where(item => !target.Contains(item.SbuId)))
                _userSbuRepository.Remove(relation);
        }

        private async Task SaveReviewAsync(Guid userId, CancellationToken cancellationToken)
        {
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                // 并发的审批、拒绝或禁用已经改动状态，后一请求不能静默覆盖。
                throw ChangedState();
            }
            finally
            {
                // 数据库可能已提交；Redis 失败由 API 返回 503，不假报整体成功。
                await Task.WhenAll(
                    _sessions.RevokeUserAsync(userId, CancellationToken.None),
                    _permissionCache.InvalidateAsync(CancellationToken.None));
            }
        }

        private static void EnsureExpectedVersion(User user, long? expected)
        {
            if (expected.HasValue && expected.Value != user.SecurityVersion)
                throw ChangedState();
        }

        private static BusinessException ChangedState() =>
            new(ErrorCodes.InvalidState, "User.StateChanged", new { Reason = "StateChanged" });

        /// <summary>当前调用链标识；无活动 Activity 时返回 null，不影响审计落库。</summary>
        private static string? CurrentTraceId() => Activity.Current?.TraceId.ToHexString();

        private static BusinessException ProvisionConflict(string reason, Guid? userId = null) =>
            new(ErrorCodes.Conflict, reason switch
            {
                LdapProvisionReasons.AccountTypeConflict => "User.AccountTypeConflict",
                LdapProvisionReasons.AccountExists => "User.AccountExists",
                _ => "User.LdapIdentityConflict"
            }, new { Reason = reason, UserId = userId });

        private async Task<User?> FindExistingForProvisionAsync(
            string employeeNo, CancellationToken cancellationToken) =>
            await _userRepository.GetByEmployeeNoAsync(employeeNo, cancellationToken)
            ?? await _userRepository.GetByNormalizedAccountAsync(employeeNo.ToUpperInvariant(), cancellationToken);

        private async Task EnsurePermissionsAsync(
            Guid actorId, CancellationToken cancellationToken, params string[] permissions)
        {
            foreach (var permission in permissions)
                if (!await _permissions.HasAsync(actorId, permission, cancellationToken))
                    throw new BusinessException(ErrorCodes.Forbidden, "Auth.Forbidden");
        }

        private static string NormalizeEmployeeNo(string employeeNo) => employeeNo.Trim();

        private static UserDetailDto ToDetail(User user, IEnumerable<Guid> sbuIds) => new()
        {
            Account = user.Account,
            DisplayName = user.DisplayName,
            Id = user.Id,
            IsEnabled = user.IsEnabled,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            SbuIds = sbuIds.ToList(),
            AuthType = user.AuthType,
            EmployeeNo = user.EmployeeNo,
            ApprovalStatus = user.ApprovalStatus,
            ReviewedByUserId = user.ReviewedByUserId,
            ReviewedAtUtc = user.ReviewedAtUtc,
            ReviewReason = user.ReviewReason,
            SecurityVersion = user.SecurityVersion
        };

        #endregion
    }
}
