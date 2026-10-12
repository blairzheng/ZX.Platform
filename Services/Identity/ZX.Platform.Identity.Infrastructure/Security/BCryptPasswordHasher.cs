using System.Text;
using ZX.Platform.Identity.Application.Security;

namespace ZX.Platform.Identity.Infrastructure.Security
{
    public sealed class BCryptPasswordHasher : IPasswordHasher
    {
        // 成本因子越高，破解成本越高，同时登录验证耗时也越长。
        // 当前先使用 12，后续根据服务器压测结果调整。
        private const int WorkFactor = 12;

        // 标准 BCrypt 最多安全处理 72 字节的密码。
        private const int MaximumPasswordBytes = 72;

        public string Hash(string password)
        {
            ValidatePassword(password);
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public bool Verify(string password, string passwordHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            {
                return false;
            }

            if (Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes)
            {
                return false;
            }

            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }

        private static void ValidatePassword(string password)
        {
            ArgumentNullException.ThrowIfNull(password);

            if (password.Length == 0)
            {
                throw new ArgumentException(
                    "密码不能为空。",
                    nameof(password));
            }

            if (Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes)
            {
                throw new ArgumentException(
                    "密码的 UTF-8 编码长度不能超过 72 字节。",
                    nameof(password));
            }
        }
    }
}
