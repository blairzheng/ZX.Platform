using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ZX.Platform.Identity.Application.Security;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordBytesAttribute : ValidationAttribute
{
    public PasswordBytesAttribute() => ErrorMessage = "Validation.PasswordMaxBytes";

    public override bool IsValid(object? value) => value is null ||
        value is string password && Encoding.UTF8.GetByteCount(password) <= 72;
}
