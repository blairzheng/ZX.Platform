using System.Text.Json.Serialization;

namespace ZX.Platform.Identity.Api.Contracts
{
    /// <summary>
    /// API 统一响应结构。
    /// </summary>
    public class ApiResponse
    {
        /// <summary>
        /// 业务状态码。
        /// 成功为 0，失败为 600xxx。
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 返回给调用方的说明信息。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 请求追踪编号。
        /// 一般只在异常时返回。
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TraceId { get; set; }
    }

    /// <summary>
    /// 带业务数据的 API 统一响应结构。
    /// </summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    public sealed class ApiResponse<T> : ApiResponse
    {
        /// <summary>
        /// 接口返回的数据。
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public T? Data { get; set; }
    }
}
