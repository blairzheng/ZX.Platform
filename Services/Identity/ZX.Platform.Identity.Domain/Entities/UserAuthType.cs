namespace ZX.Platform.Identity.Domain.Entities
{
    /// <summary>
    /// 用户认证来源。认证类型在账号创建后不再变更。
    /// </summary>
    public static class UserAuthType
    {
        /// <summary>
        /// 本地管理账号，密码由 ZX 校验 BCrypt 哈希。
        /// </summary>
        public const string Local = "LOCAL";

        /// <summary>
        /// OA 员工账号，密码由 LDAP 校验，ZX 不保存密码。
        /// </summary>
        public const string Ldap = "LDAP";

        public static IReadOnlyList<string> All { get; } = [Local, Ldap];

        public static bool IsValid(string? value) => value is Local or Ldap;
    }
}
