namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>本机开发可选的目录访问；服务器仅用同一 AccessKey 校验两个目录接口。</summary>
public sealed class DirectoryRelayOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 110;

    public void ValidateClient()
    {
        if (!IsValidAccessKey(AccessKey))
            throw new InvalidOperationException("DirectoryRelay:AccessKey 必须为 32-256 位 ASCII 可打印非空白字符。");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(address.Host) ||
            !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment) || BaseUrl.Any(char.IsControl))
            throw new InvalidOperationException("DirectoryRelay:BaseUrl 必须为不含凭据、查询参数或片段的 HTTP(S) 绝对地址。");
        if (TimeoutSeconds is < 1 or > 180)
            throw new InvalidOperationException("DirectoryRelay:TimeoutSeconds 必须在 1-180 之间。");
    }

    public static bool IsValidAccessKey(string? value) =>
        value is { Length: >= 32 and <= 256 } &&
        value.All(character => character is >= '!' and <= '~');
}
