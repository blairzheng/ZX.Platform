using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities
{
    public sealed class User : SoftDeletableEntity
    {
        private User()
        {
        }

        private User(
            Guid id,
            string account,
            string displayName,
            string passwordHash)
        {
            Id = id;
            Account = account;
            NormalizedAccount = account.ToUpperInvariant();
            DisplayName = displayName;
            PasswordHash = passwordHash;
            IsEnabled = true;
        }

        /// <summary>
        /// 用户唯一标识
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// 用户登录账号
        /// </summary>
        public string Account { get; private set; } = string.Empty;

        /// <summary>
        /// 标准化账号，用于账号查询和唯一性比较
        /// </summary>
        public string NormalizedAccount { get; private set; } = string.Empty;

        /// <summary>
        /// 用户显示名称
        /// </summary>
        public string DisplayName { get; private set; } = string.Empty;

        /// <summary>
        /// 经过安全哈希处理后的密码，不能保存明文密码
        /// </summary>
        public string PasswordHash { get; private set; } = string.Empty;

        /// <summary>
        /// 用户是否启用
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// 连续登录失败次数
        /// </summary>
        public int AccessFailedCount { get; private set; }

        /// <summary>
        /// 账户锁定结束时间；为空表示当前没有锁定时间
        /// </summary>
        public DateTimeOffset? LockoutEndUtc { get; private set; }

        /// <summary>
        /// 创建并初始化用户实体，不负责将用户保存到数据库
        /// </summary>
        /// <param name="account"></param>
        /// <param name="displayName"></param>
        /// <param name="passwordHash"></param>
        /// <param name="createdAtUtc"></param>
        /// <returns></returns>
        public static User Create(
            string account,
            string displayName,
            string passwordHash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(account);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

            return new User(
                Guid.NewGuid(),
                account.Trim(),
                displayName.Trim(),
                passwordHash);
        }

        /// <summary>
        /// 修改用户显示名称
        /// </summary>
        /// <param name="displayName"></param>
        public void ChangeDisplayName(string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            DisplayName = displayName.Trim();
        }

        /// <summary>
        /// 修改密码哈希，并清除原有登录失败和锁定状态
        /// </summary>
        /// <param name="passwordHash"></param>
        public void ChangePasswordHash(string passwordHash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
            PasswordHash = passwordHash;
            RecordLoginSuccess();
        }

        /// <summary>
        /// 启用用户
        /// </summary>
        public void Enable()
        {
            if (IsDeleted)
                throw new DomainException("User.RestoreBeforeEnable");
            IsEnabled = true;
        }

        /// <summary>
        /// 禁用用户
        /// </summary>
        public void Disable() => IsEnabled = false;

        /// <summary>
        /// 判断用户在指定时间是否处于锁定状态
        /// </summary>
        /// <param name="now"></param>
        /// <returns></returns>
        public bool IsLocked(DateTimeOffset now) =>
            LockoutEndUtc.HasValue && LockoutEndUtc.Value > now;

        /// <summary>
        /// 判断用户当前是否允许登录
        /// </summary>
        /// <param name="now"></param>
        /// <returns></returns>
        public bool CanLogin(DateTimeOffset now) =>
            !IsDeleted && IsEnabled && !IsLocked(now);

        /// <summary>
        /// 记录一次登录失败，达到限制后锁定账户
        /// </summary>
        /// <param name="now"></param>
        /// <param name="maximumFailedAttempts"></param>
        /// <param name="lockoutDuration"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void RecordLoginFailure(
            DateTimeOffset now,
            int maximumFailedAttempts,
            TimeSpan lockoutDuration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumFailedAttempts, 1);

            if (lockoutDuration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(lockoutDuration));

            if (LockoutEndUtc.HasValue && LockoutEndUtc.Value <= now)
                RecordLoginSuccess();

            AccessFailedCount++;

            if (AccessFailedCount >= maximumFailedAttempts)
                LockoutEndUtc = now.Add(lockoutDuration);
        }

        /// <summary>
        /// 记录登录成功，并清除登录失败和锁定状态
        /// </summary>
        public void RecordLoginSuccess()
        {
            AccessFailedCount = 0;
            LockoutEndUtc = null;
        }

        /// <summary>
        /// 逻辑删除时触发
        /// </summary>
        protected override void OnDeleted()
        {
            IsEnabled = false;
        }

        /// <summary>
        /// 逻辑删除恢复时触发
        /// </summary>
        protected override void OnRestored()
        {
            IsEnabled = true;
        }

    }
}
