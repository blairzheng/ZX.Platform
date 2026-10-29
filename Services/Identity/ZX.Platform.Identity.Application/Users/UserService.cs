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
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuthSessionStore _sessions;
        private readonly IPermissionCache _permissionCache;

        public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher,
            ISbuRepository sbuRepository, IUserSbuRepository userSbuRepository, IAuthSessionStore sessions, IPermissionCache permissionCache)
        {
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _sbuRepository = sbuRepository;
            _userSbuRepository = userSbuRepository;
            _sessions = sessions;
            _permissionCache = permissionCache;
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
            var user = User.Create(account, createUser.DisplayName, passwordHash);
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
            return new UserDetailDto
            {
                Account = data.Account,
                DisplayName = data.DisplayName,
                Id = data.Id,
                IsEnabled = data.IsEnabled,
                CreatedAtUtc = data.CreatedAtUtc,
                UpdatedAtUtc = data.UpdatedAtUtc,
                SbuIds = userSbus.Select(f => f.SbuId).ToList()
            };
        }

        public async Task<(List<UserDetailDto>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken)
        {
            if (userQuery.CreatedFromUtc.HasValue && userQuery.CreatedToUtc.HasValue &&
                userQuery.CreatedFromUtc.Value >= userQuery.CreatedToUtc.Value)
            {
                throw new BusinessException(ErrorCodes.ValidationFailed, "User.CreatedAtRangeInvalid");
            }

            var (list, count) = await _userRepository.GetPageListAsync(userQuery, cancellationToken);
            var userSbus = await _userSbuRepository.GetByUserIdsAsync(list.Select(user => user.Id).ToList(), cancellationToken);
            var sbuIdsByUser = userSbus.ToLookup(relation => relation.UserId, relation => relation.SbuId);
            var users = new List<UserDetailDto>(list.Count);
            foreach (var user in list)
            {
                users.Add(new UserDetailDto
                {
                    Account = user.Account,
                    DisplayName = user.DisplayName,
                    Id = user.Id,
                    IsEnabled = user.IsEnabled,
                    CreatedAtUtc = user.CreatedAtUtc,
                    UpdatedAtUtc = user.UpdatedAtUtc,
                    SbuIds = sbuIdsByUser[user.Id].ToList()
                });
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

            // 新增SBU
            foreach (var sbu in sbus)
            {
                if (!userSbus.Select(e => e.SbuId).Contains(sbu.Id))
                {
                    var userSbu = UserSbu.Create(updateUserSbu.UserId, sbu.Id, false);
                    _userSbuRepository.Add(userSbu);
                }
            }

            // 删除SBU
            foreach (var item in userSbus)
            {
                if (!sbus.Select(e => e.Id).Contains(item.SbuId))
                {
                    _userSbuRepository.Remove(item);
                }
            }
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

        #endregion
    }
}
