using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ZX.Platform.Identity.Api.Localization;

namespace ZX.Platform.Identity.Api.Extensions
{
    public static class LocalizationExtensions
    {
        public static IServiceCollection AddApiLocalization(this IServiceCollection services)
        {
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            // 异常处理器和 MVC 配置长期持有此服务；语言由每次查找时的请求上下文决定。
            services.AddSingleton<IApiMessageLocalizer, ApiMessageLocalizer>();
            services.AddSingleton<Microsoft.AspNetCore.Mvc.DataAnnotations.IValidationAttributeAdapterProvider, PasswordValidationAdapterProvider>();
            services.Configure<RequestLocalizationOptions>(options =>
            {
                var cultures = new[] { "zh-CN", "en-US" };
                options.SetDefaultCulture("zh-CN")
                    .AddSupportedCultures(cultures)
                    .AddSupportedUICultures(cultures);
                // 仅按请求头协商语言；URL 和 Cookie 不覆盖前端当前选择。
                options.RequestCultureProviders = new List<IRequestCultureProvider>
                {
                    new AcceptLanguageHeaderRequestCultureProvider()
                };
                options.ApplyCurrentCultureToResponseHeaders = true;
            });

            services.AddOptions<MvcOptions>()
                .Configure<IStringLocalizer<ValidationMessages>>((options, messages) =>
                {
                    var provider = options.ModelBindingMessageProvider;
                    provider.SetAttemptedValueIsInvalidAccessor((_, name) => messages["Validation.ParameterInvalid", name].Value);
                    provider.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => messages["Validation.InvalidValue"].Value);
                    provider.SetUnknownValueIsInvalidAccessor(name => messages["Validation.ParameterInvalid", name].Value);
                    provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => messages["Validation.InvalidValue"].Value);
                    provider.SetValueIsInvalidAccessor(_ => messages["Validation.InvalidValue"].Value);
                    provider.SetValueMustNotBeNullAccessor(_ => messages["Validation.InvalidValue"].Value);
                    provider.SetValueMustBeANumberAccessor(name => messages["Validation.ParameterInvalid", name].Value);
                    provider.SetNonPropertyValueMustBeANumberAccessor(() => messages["Validation.InvalidValue"].Value);
                    provider.SetMissingBindRequiredValueAccessor(name => messages["Validation.ParameterInvalid", name].Value);
                    provider.SetMissingKeyOrValueAccessor(() => messages["Validation.InvalidValue"].Value);
                    provider.SetMissingRequestBodyRequiredValueAccessor(() => messages["Validation.BodyRequired"].Value);
                });
            // JSON 解析细节不作为接口提示返回，统一由模型验证失败处理器给出本地化说明。
            services.Configure<JsonOptions>(options => options.AllowInputFormatterExceptionMessages = false);
            return services;
        }
    }
}
