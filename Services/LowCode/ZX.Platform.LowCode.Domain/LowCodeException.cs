namespace ZX.Platform.LowCode.Domain;

/// <summary>低代码错误码。600xxx 是通用段，610xxx 是低代码专用段，见 runtime.md §5。</summary>
public static class LowCodeErrorCodes
{
    public const string ValidationFailed = "600001";
    public const string NotFound = "600002";
    public const string Conflict = "600003";
    public const string StateInvalid = "600004";
    public const string Forbidden = "600005";
    public const string SbuRequired = "610001";
    public const string SbuDenied = "610002";
    public const string OperatorNotAllowed = "610003";
    public const string ResultTooLarge = "610004";
    public const string ReadOnlyDataset = "610005";
}

/// <summary>带错误码的领域异常，由 API 层统一映射成 HTTP 状态码。</summary>
public sealed class LowCodeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static LowCodeException OperatorNotAllowed(string message) =>
        new(LowCodeErrorCodes.OperatorNotAllowed, message);

    public static LowCodeException ResultTooLarge(string message) =>
        new(LowCodeErrorCodes.ResultTooLarge, message);

    public static LowCodeException SbuRequired(string message) =>
        new(LowCodeErrorCodes.SbuRequired, message);
}
