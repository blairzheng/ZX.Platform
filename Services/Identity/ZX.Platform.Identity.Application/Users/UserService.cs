using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Users
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserFactoryRepository _userFactoryRepository;
        private readonly IFactoryRepository _factoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuthSessionStore _sessions;

        public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher,
            IFactoryRepository factoryRepository, IUserFactoryRepository userFactoryRepository, IAuthSessionStore sessions)
        {
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _factoryRepository = factoryRepository;
            _userFactoryRepository = userFactoryRepository;
            _sessions = sessions;
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
            await GetValidatedFactoriesAsync(createUser.FactoryIds, cancellationToken);
            if (createUser.FactoryIds.Count > 0)
            {
                foreach (var item in createUser.FactoryIds)
                {
                    var userFactory = UserFactory.Create(user.Id, item, false);
                    _userFactoryRepository.Add(userFactory);
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
            var userFactories = await _userFactoryRepository.GetByUserIdAsync(data.Id, cancellationToken);
            return new UserDetailDto
            {
                Account = data.Account,
                DisplayName = data.DisplayName,
                Id = data.Id,
                IsEnabled = data.IsEnabled,
                CreatedAtUtc = data.CreatedAtUtc,
                UpdatedAtUtc = data.UpdatedAtUtc,
                FactoryIds = userFactories.Select(f => f.FactoryId).ToList()
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
            var userFactories = await _userFactoryRepository.GetByUserIdsAsync(list.Select(user => user.Id).ToList(), cancellationToken);
            var factoryIdsByUser = userFactories.ToLookup(relation => relation.UserId, relation => relation.FactoryId);
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
                    FactoryIds = factoryIdsByUser[user.Id].ToList()
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
                await _sessions.RevokeUserAsync(id, CancellationToken.None);
            }
        }

        public async Task SetUserEnableAsync(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            user.Enable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
                await _sessions.RevokeUserAsync(id, CancellationToken.None);
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateUserFactoriesAsync(UpdateUserFactoryDto updateUserFactory, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(updateUserFactory.UserId, cancellationToken);
            if (user == null) throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
            // 获取当前用户已授权的厂区列表
            var userFactories = await _userFactoryRepository.GetByUserIdAsync(updateUserFactory.UserId, cancellationToken);

            // 校验传入的厂区数据是否有效，并获取新列表中的厂区信息
            var factories = await GetValidatedFactoriesAsync(updateUserFactory.FactoryIds, cancellationToken);

            // 新增厂区
            foreach (var factory in factories)
            {
                if (!userFactories.Select(e => e.FactoryId).Contains(factory.Id))
                {
                    var userFactory = UserFactory.Create(updateUserFactory.UserId, factory.Id, false);
                    _userFactoryRepository.Add(userFactory);
                }
            }

            // 删除厂区
            foreach (var item in userFactories)
            {
                if (!factories.Select(e => e.Id).Contains(item.FactoryId))
                {
                    _userFactoryRepository.Remove(item);
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
                    _sessions.RevokeUserAsync(user.Id, CancellationToken.None)));
            }
        }

        #region 校验并获取传入的厂区数据
        private async Task<List<Factory>> GetValidatedFactoriesAsync(List<Guid> factoryIds, CancellationToken cancellationToken)
        {
            var factories = await _factoryRepository.GetEnabledFactoriesByIdsAsync(factoryIds, cancellationToken);
            if (factories.Count != factoryIds.Count)
            {
                throw new BusinessException(ErrorCodes.NotFound, "User.FactoriesUnavailable");
            }
            return factories;
        }

        #endregion
    }
}
