using Microsoft.Extensions.Localization;

namespace ZX.Platform.Identity.Api.Localization
{
    /// <summary>
    /// 按消息键前缀从对应资源文件查找提示。
    /// </summary>
    public sealed class ApiMessageLocalizer : IApiMessageLocalizer
    {
        private readonly IStringLocalizer<CommonMessages> _common;
        private readonly IStringLocalizer<ValidationMessages> _validation;
        private readonly IStringLocalizer<UserMessages> _user;
        private readonly IStringLocalizer<FactoryMessages> _factory;

        public ApiMessageLocalizer(
            IStringLocalizer<CommonMessages> common,
            IStringLocalizer<ValidationMessages> validation,
            IStringLocalizer<UserMessages> user,
            IStringLocalizer<FactoryMessages> factory)
        {
            _common = common;
            _validation = validation;
            _user = user;
            _factory = factory;
        }

        public LocalizedString this[string key, params object[] arguments]
        {
            get
            {
                var localizer = Resolve(key);
                return localizer[key, arguments];
            }
        }

        private IStringLocalizer Resolve(string key)
        {
            if (key.StartsWith("Validation.", StringComparison.Ordinal))
                return _validation;

            if (key.StartsWith("User.", StringComparison.Ordinal))
                return _user;

            if (key.StartsWith("Factory.", StringComparison.Ordinal))
                return _factory;

            return _common;
        }
    }
}
