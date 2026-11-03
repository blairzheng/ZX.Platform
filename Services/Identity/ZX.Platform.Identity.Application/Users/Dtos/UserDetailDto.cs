namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UserDetailDto
    {
        /// <summary>
        /// 用户唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// 用户登录账号
        /// </summary>
        public string Account { get; set; } = string.Empty;

        /// <summary>
        /// 用户显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 用户是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 创建时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset CreatedAtUtc { get; set; }

        /// <summary>
        /// 最后修改时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset? UpdatedAtUtc { get; set; }

        public List<Guid> SbuIds { get; set; } = new();

        /// <summary>
        /// 认证类型，LOCAL 或 LDAP。
        /// </summary>
        public string AuthType { get; set; } = string.Empty;

        /// <summary>
        /// OA 工号；本地账号为空。
        /// </summary>
        public string? EmployeeNo { get; set; }

        /// <summary>
        /// 开通审批状态，Pending / Approved / Rejected。
        /// </summary>
        public string ApprovalStatus { get; set; } = string.Empty;

        /// <summary>
        /// 最近一次审批或拒绝的操作人。
        /// </summary>
        public Guid? ReviewedByUserId { get; set; }

        /// <summary>
        /// 最近一次审批或拒绝的时间，UTC。
        /// </summary>
        public DateTimeOffset? ReviewedAtUtc { get; set; }

        /// <summary>
        /// 最近一次审批说明或拒绝原因。
        /// </summary>
        public string? ReviewReason { get; set; }

        /// <summary>
        /// 当前会话安全版本，审批类操作需要回传以做并发检查。
        /// </summary>
        public long SecurityVersion { get; set; }
    }
}
