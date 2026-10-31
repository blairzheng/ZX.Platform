namespace ZX.Platform.Identity.Application.Errors
{
    public static class ErrorCodes
    {
        /// <summary>
        /// 请求参数验证失败。
        /// </summary>
        public const int ValidationFailed = 600001;

        /// <summary>
        /// 查询的数据不存在。
        /// </summary>
        public const int NotFound = 600002;

        /// <summary>
        /// 数据发生冲突，例如账号重复。
        /// </summary>
        public const int Conflict = 600003;

        /// <summary>
        /// 当前数据状态不允许执行该操作。
        /// </summary>
        public const int InvalidState = 600004;

        /// <summary>
        /// 没有权限执行该操作。
        /// </summary>
        public const int Forbidden = 600005;

        /// <summary>
        /// 低代码：实体按 SBU 隔离，但请求没带 X-SBU-ID。
        /// </summary>
        public const int SbuRequired = 610001;

        /// <summary>
        /// 低代码：当前用户无权访问请求的 SBU。
        /// </summary>
        public const int SbuDenied = 610002;

        /// <summary>
        /// 低代码：字段类型不支持该算子，或字段不存在 / 已停用。
        /// </summary>
        public const int OperatorNotAllowed = 610003;

        /// <summary>
        /// 低代码：翻页过深或结果集超过上限。
        /// </summary>
        public const int ResultTooLarge = 610004;

        /// <summary>
        /// 低代码：外部数据集只读。
        /// </summary>
        public const int ReadOnlyDataset = 610005;

        /// <summary>
        /// 未登录、凭据无效或会话已失效。
        /// </summary>
        public const int Unauthorized = 600401;

        /// <summary>
        /// 请求频率超过限制。
        /// </summary>
        public const int TooManyRequests = 600429;

        /// <summary>
        /// 系统出现未处理异常。
        /// </summary>
        public const int SystemError = 600500;

        /// <summary>
        /// 依赖服务暂时不可用。
        /// </summary>
        public const int ServiceUnavailable = 600503;
    }
}
