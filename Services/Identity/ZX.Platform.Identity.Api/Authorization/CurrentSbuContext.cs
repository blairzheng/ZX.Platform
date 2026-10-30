namespace ZX.Platform.Identity.Api.Authorization;

public sealed class CurrentSbuContext
{
    private Guid? _sbuId;
    private string? _sbuCode;

    public Guid SbuId => _sbuId ?? throw new InvalidOperationException(
        "当前请求未通过 SBU 校验；接口需要标注 [RequireSbu]。");

    public string SbuCode => _sbuCode ?? throw new InvalidOperationException(
        "当前请求未通过 SBU 校验；接口需要标注 [RequireSbu]。");

    internal void SetValidatedSbu(Guid sbuId, string sbuCode)
    {
        _sbuId = sbuId;
        _sbuCode = sbuCode;
    }
}
