namespace ZX.Platform.Identity.Application.Exceptions
{
    /// <summary>
    /// 并发令牌不一致：记录已被其他请求修改。调用方必须重新读取状态，不能静默覆盖。
    /// </summary>
    public class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
