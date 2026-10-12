using Microsoft.Extensions.Localization;

namespace ZX.Platform.Identity.Api.Localization
{
    public interface IApiMessageLocalizer
    {
        LocalizedString this[string key, params object[] arguments] { get; }
    }
}
