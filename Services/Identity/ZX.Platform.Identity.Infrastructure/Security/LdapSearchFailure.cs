namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 目录搜索的失败分类。
/// 搜索阶段出现 sizeLimitExceeded 说明匹配到多条记录，属于目录数据歧义，
/// 换备用节点重试只会得到同样结果；其它失败（含服务账号被拒）才按依赖故障切换节点。
/// </summary>
public static class LdapSearchFailure
{
    /// <summary>LDAP 结果码 sizeLimitExceeded：匹配结果超过 SizeLimit，无法确定唯一身份。</summary>
    public const int SizeLimitExceeded = 4;

    /// <summary>LDAP 结果码 invalidCredentials。只在员工 bind 阶段算密码错误。</summary>
    public const int InvalidCredentials = 49;

    /// <summary>
    /// 是否属于“目录结果不唯一”。<c>LdapException</c> 走错误码，
    /// <c>DirectoryOperationException</c> 走响应里的结果码，两条路径都要判断。
    /// </summary>
    public static bool IsAmbiguous(int? ldapErrorCode, int? responseResultCode) =>
        ldapErrorCode == SizeLimitExceeded || responseResultCode == SizeLimitExceeded;
}
