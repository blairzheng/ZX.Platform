namespace ZX.Platform.Identity.Application.Exceptions
{
    /// <summary>
    /// Application 层的业务异常。
    /// </summary>
    public sealed class BusinessException : Exception
    {
        public BusinessException(
            int code,
            string messageKey,
            object? responseData = null)
            : base(messageKey)
        {
            Code = code;
            MessageKey = messageKey;
            ResponseData = responseData;
        }

        /// <summary>
        /// 业务错误码。
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// 稳定提示资源键，由 API 层根据请求语言翻译。
        /// </summary>
        public string MessageKey { get; }

        /// <summary>
        /// 返回给前端的附加数据。
        /// </summary>
        /// <remarks>
        /// 这里不命名为 Data，是因为 Exception 自身已经有一个 Data 属性。
        /// </remarks>
        public object? ResponseData { get; }
    }
}
