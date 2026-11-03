namespace ZX.Platform.Identity.Application.Security
{
    public interface IPasswordHasher
    {
        /// <summary>
        /// 将明文密码转换为可安全保存的密码哈希。
        /// </summary>
        string Hash(string password);

        /// <summary>
        /// 验证明文密码是否与已保存的密码哈希匹配。
        /// </summary>
        bool Verify(string password, string passwordHash);
    }
}
