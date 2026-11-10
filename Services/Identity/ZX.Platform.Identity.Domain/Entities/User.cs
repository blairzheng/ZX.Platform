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
            string? passwordHash,
            string authType,
            string approvalStatus,
            bool isEnabled)
        {
            Id = id;
            Account = account;
            NormalizedAccount = account.ToUpperInvariant();
            DisplayName = displayName;
            PasswordHash = passwordHash;
            AuthType = authType;
            ApprovalStatus = approvalStatus;
            IsEnabled = isEnabled;
            SecurityVersion = 1;
        }

        /// <summary>
        /// 用户唯一标识
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// 用户登录账号。本地账号由管理员指定，OA 账号与工号一致。
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
        /// 经过安全哈希处理后的密码，不能保存明文密码。
        /// 本地账号必须有值，OA 账号必须为空。
        /// </summary>
        public string? PasswordHash { get; private set; }

        /// <summary>
        /// 认证来源，见 <see cref="UserAuthType"/>。
        /// </summary>
        public string AuthType { get; private set; } = UserAuthType.Local;

        /// <summary>
        /// OA 工号；本地账号为空。按字符串保存，保留前导零。
        /// </summary>
        public string? EmployeeNo { get; private set; }

        /// <summary>
        /// 目录来源标识；更换该值等同于更换身份命名空间，不能随部署修改。
        /// </summary>
        public string? LdapProvider { get; private set; }

        /// <summary>
        /// 规范化后的目录唯一标识（AD objectGUID）；预开通记录在首次 OA 认证前为空。
        /// </summary>
        public string? LdapSubject { get; private set; }

        /// <summary>
        /// 开通审批状态，见 <see cref="UserApprovalStatus"/>。
        /// </summary>
        public string ApprovalStatus { get; private set; } = UserApprovalStatus.Approved;

        /// <summary>
        /// 会话安全版本。身份绑定、审批、拒绝、启用、禁用、删除、恢复和本地改密时递增，
        /// 用于使旧会话失效并作为账户状态变更的乐观并发检查。
        /// </summary>
        public long SecurityVersion { get; private set; } = 1;

        /// <summary>
        /// 最近一次审批或拒绝的操作人。
        /// </summary>
        public Guid? ReviewedByUserId { get; private set; }

        /// <summary>
        /// 最近一次审批或拒绝的 UTC 时间。
        /// </summary>
        public DateTimeOffset? ReviewedAtUtc { get; private set; }

        /// <summary>
        /// 最近一次审批说明或拒绝原因。
        /// </summary>
        public string? ReviewReason { get; private set; }

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
        /// 是否为 OA 员工账号。
        /// </summary>
        public bool IsLdap => AuthType == UserAuthType.Ldap;

        /// <summary>
        /// 是否允许在 ZX 修改密码；OA 账号的密码只能在 OA 修改。
        /// </summary>
        public bool CanChangePassword => AuthType == UserAuthType.Local;

        /// <summary>账号状态允许维持登录；待审批 OA 账号只有系统外壳访问权。</summary>
        public bool CanUseSession => !IsDeleted && IsEnabled &&
            (ApprovalStatus == UserApprovalStatus.Approved ||
             (IsLdap && ApprovalStatus == UserApprovalStatus.Pending));

        /// <summary>业务授权资格；待审批账号即使存在角色或 SBU 关联也不得生效。</summary>
        public bool HasBusinessAccess => !IsDeleted && IsEnabled && ApprovalStatus == UserApprovalStatus.Approved;

        /// <summary>
        /// 创建本地管理账号。本地账号必须有密码哈希，审批状态固定为通过。
        /// </summary>
        public static User CreateLocal(
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
                passwordHash,
                UserAuthType.Local,
                UserApprovalStatus.Approved,
                true);
        }

        /// <summary>
        /// 创建待业务审批的 OA 员工账号。允许登录，不保存密码、不分配角色和 SBU。
        /// </summary>
        /// <param name="account">
        /// ZX 账号，固定使用工号；目录登录名（如 sAMAccountName）只用于目录认证，不作为 ZX 账号。
        /// </param>
        public static User CreateLdap(
            string account,
            string employeeNo,
            string displayName,
            string provider,
            string subject)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(account);
            ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);

            var user = new User(
                Guid.NewGuid(),
                account.Trim(),
                displayName.Trim(),
                null,
                UserAuthType.Ldap,
                UserApprovalStatus.Pending,
                true);
            user.EmployeeNo = employeeNo.Trim();
            user.LdapProvider = provider.Trim();
            user.LdapSubject = subject.Trim();
            return user;
        }

        /// <summary>
        /// 管理员按工号预开通，不查询目录、不伪造身份。显示名称在首次 OA 认证后补齐。
        /// </summary>
        public static User CreateLdapProvision(string employeeNo)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
            var user = new User(
                Guid.NewGuid(), employeeNo.Trim(), employeeNo.Trim(), null,
                UserAuthType.Ldap, UserApprovalStatus.Pending, true);
            user.EmployeeNo = employeeNo.Trim();
            return user;
        }

        /// <summary>
        /// 仅为尚未绑定的预开通账号补齐已认证身份，保留 UserId、审批和业务授权。
        /// 递增安全版本，防止并发登录或管理员状态修改被静默覆盖。
        /// </summary>
        public void BindLdapIdentity(string employeeNo, string provider, string subject, string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            EnsureLdapAccount();
            if (!CanUseSession || LdapProvider is not null || LdapSubject is not null ||
                !string.Equals(EmployeeNo, employeeNo.Trim(), StringComparison.Ordinal))
                throw new DomainException("User.LdapIdentityConflict");

            LdapProvider = provider.Trim();
            LdapSubject = subject.Trim();
            DisplayName = displayName.Trim();
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 审批通过 OA 业务访问。角色和 SBU 由调用方在同一事务中写入。
        /// Pending 的禁用状态保留；Rejected 的显式重新审批恢复启用。
        /// </summary>
        /// <param name="reviewerUserId">审批人；匿名的首次申请由管理员后续审批，不能为空。</param>
        /// <param name="reviewedAtUtc">审批时间，统一使用 UTC。</param>
        /// <param name="reason">审批说明，可为空。</param>
        public void ApproveLdap(Guid reviewerUserId, DateTimeOffset reviewedAtUtc, string? reason)
        {
            EnsureLdapAccount();
            // 已通过的账号一律不能再次审批：否则“已通过但被禁用”可以借审批重新启用，
            // 绕过 identity.user.status 的启用权限，并顺带改写既有角色。
            // 重新启用必须走 Enable()。
            if (ApprovalStatus == UserApprovalStatus.Approved)
                throw new DomainException("User.AlreadyApproved");

            if (ApprovalStatus == UserApprovalStatus.Rejected)
                IsEnabled = true;
            ApprovalStatus = UserApprovalStatus.Approved;
            ReviewedByUserId = reviewerUserId;
            ReviewedAtUtc = reviewedAtUtc;
            ReviewReason = NormalizeReason(reason);
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 拒绝 OA 开通申请，保持禁用。已开通账号必须走禁用流程，不能改成拒绝。
        /// </summary>
        public void RejectLdap(Guid reviewerUserId, DateTimeOffset reviewedAtUtc, string? reason)
        {
            EnsureLdapAccount();
            if (ApprovalStatus == UserApprovalStatus.Approved)
                throw new DomainException("User.AlreadyApproved");

            ApprovalStatus = UserApprovalStatus.Rejected;
            IsEnabled = false;
            ReviewedByUserId = reviewerUserId;
            ReviewedAtUtc = reviewedAtUtc;
            ReviewReason = NormalizeReason(reason);
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 修改用户显示名称。显示名称不影响登录资格，不递增安全版本。
        /// </summary>
        public void ChangeDisplayName(string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            DisplayName = displayName.Trim();
        }

        /// <summary>
        /// 修改密码哈希，并清除原有登录失败和锁定状态。
        /// OA 账号的密码由目录维护，不允许在这里写入。
        /// </summary>
        public void ChangePasswordHash(string passwordHash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
            if (!CanChangePassword)
                throw new DomainException("Auth.PasswordChangeNotAllowedForLdap");

            PasswordHash = passwordHash;
            RecordLoginSuccess();
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 启用用户。待审批 OA 账号可登录但没有业务权限；已拒绝账号必须重新审批。
        /// </summary>
        public void Enable()
        {
            if (IsDeleted)
                throw new DomainException("User.RestoreBeforeEnable");
            if (IsEnabled)
                return;
            if (IsLdap && ApprovalStatus == UserApprovalStatus.Rejected)
                throw new DomainException("User.RejectedCannotEnable");

            IsEnabled = true;
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 禁用用户。
        /// </summary>
        public void Disable()
        {
            if (!IsEnabled)
                return;

            IsEnabled = false;
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 判断用户在指定时间是否处于锁定状态
        /// </summary>
        public bool IsLocked(DateTimeOffset now) =>
            LockoutEndUtc.HasValue && LockoutEndUtc.Value > now;

        /// <summary>
        /// 判断用户当前是否允许登录：状态允许维持会话且未被锁定。
        /// </summary>
        public bool CanLogin(DateTimeOffset now) =>
            CanUseSession && !IsLocked(now);

        /// <summary>
        /// 记录一次登录失败，达到限制后锁定账户
        /// </summary>
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
            IncrementSecurityVersion();
        }

        /// <summary>
        /// 逻辑删除恢复时触发。只恢复原有身份和审批状态，不自动开通 OA 账号。
        /// </summary>
        protected override void OnRestored()
        {
            if (!IsLdap)
                IsEnabled = true;
            IncrementSecurityVersion();
        }

        private void EnsureLdapAccount()
        {
            if (!IsLdap)
                throw new DomainException("User.NotLdapAccount");
            if (IsDeleted)
                throw new DomainException("User.DeletedCannotReview");
        }

        private void IncrementSecurityVersion() => SecurityVersion++;

        private static string? NormalizeReason(string? reason)
        {
            var trimmed = reason?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
