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
        private readonly IStringLocalizer<SbuMessages> _sbu;
        private readonly IStringLocalizer<AuthMessages> _auth;
        private readonly IStringLocalizer<AccessMessages> _access;

        public ApiMessageLocalizer(
            IStringLocalizer<CommonMessages> common,
            IStringLocalizer<ValidationMessages> validation,
            IStringLocalizer<UserMessages> user,
            IStringLocalizer<SbuMessages> sbu,
            IStringLocalizer<AuthMessages> auth,
            IStringLocalizer<AccessMessages> access)
        {
            _common = common;
            _validation = validation;
            _user = user;
            _sbu = sbu;
            _auth = auth;
            _access = access;
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
            if (key.StartsWith("Access.", StringComparison.Ordinal))
                return _access;

            if (key.StartsWith("Validation.", StringComparison.Ordinal))
                return _validation;

            if (key.StartsWith("User.", StringComparison.Ordinal))
                return _user;

            if (key.StartsWith("Sbu.", StringComparison.Ordinal))
                return _sbu;

            if (key.StartsWith("Auth.", StringComparison.Ordinal))
                return _auth;

            return _common;
        }
    }
}
