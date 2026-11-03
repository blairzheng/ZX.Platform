namespace ZX.Platform.Identity.Domain.Entities
{
    /// <summary>
    /// OA 账号开通审批状态。本地账号固定为 Approved。
    /// </summary>
    public static class UserApprovalStatus
    {
        /// <summary>
        /// 待业务授权。已启用时可登录系统，但没有有效角色、权限、菜单和 SBU。
        /// </summary>
        public const string Pending = "Pending";

        /// <summary>
        /// 业务审批通过，分配的角色和 SBU 可以生效；登录仍受启停用状态控制。
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
