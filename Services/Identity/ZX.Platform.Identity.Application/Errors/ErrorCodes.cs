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
        /// 请求频率超过限制。
        /// </summary>
        public const int TooManyRequests = 600429;

        /// <summary>
        /// 系统出现未处理异常。
        /// </summary>
        public const int SystemError = 600500;
    }
}
