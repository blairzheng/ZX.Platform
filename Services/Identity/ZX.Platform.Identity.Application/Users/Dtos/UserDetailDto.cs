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
    }
}
