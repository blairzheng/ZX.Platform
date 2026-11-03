using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Localization;
using ZX.Platform.Identity.Application.Security;

namespace ZX.Platform.Identity.Api.Localization;

public sealed class PasswordValidationAdapterProvider : IValidationAttributeAdapterProvider
{
    private readonly ValidationAttributeAdapterProvider _defaults = new();

    public IAttributeAdapter? GetAttributeAdapter(ValidationAttribute attribute, IStringLocalizer? localizer) =>
        attribute is PasswordBytesAttribute passwordBytes
            ? new PasswordBytesAdapter(passwordBytes, localizer)
            : _defaults.GetAttributeAdapter(attribute, localizer);

    private sealed class PasswordBytesAdapter(PasswordBytesAttribute attribute, IStringLocalizer? localizer)
        : AttributeAdapterBase<PasswordBytesAttribute>(attribute, localizer)
    {
        public override string GetErrorMessage(ModelValidationContextBase context) =>
            GetErrorMessage(context.ModelMetadata, context.ModelMetadata.GetDisplayName());

        public override void AddValidation(ClientModelValidationContext context) { }
    }
}
