namespace ZX.Platform.Identity.Domain.Entities
{
    /// <summary>
    /// OA 账号开通审批状态。本地账号固定为 Approved。
    /// </summary>
    public static class UserApprovalStatus
    {
        /// <summary>
        /// 已提交申请或由管理员仅创建待审批，尚未开通。
        /// </summary>
        public const string Pending = "Pending";

        /// <summary>
        /// 审批通过，可以登录。
        /// </summary>
        public const string Approved = "Approved";

        /// <summary>
        /// 申请被拒绝，重复登录不会自动回到待审批。
        /// </summary>
        public const string Rejected = "Rejected";

        public static IReadOnlyList<string> All { get; } = [Pending, Approved, Rejected];

        public static bool IsValid(string? value) => value is Pending or Approved or Rejected;
    }
}
