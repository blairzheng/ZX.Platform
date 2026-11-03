namespace ZX.Platform.Identity.Domain.Entities;

// 权限定义来自代码；数据库只维护目录和是否仍在使用。
public sealed class Permission
{
    private Permission() { }
    public string Code { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public static Permission Create(string code) => new() { Code = code, IsActive = true };
    public void SetActive(bool active) => IsActive = active;
}
