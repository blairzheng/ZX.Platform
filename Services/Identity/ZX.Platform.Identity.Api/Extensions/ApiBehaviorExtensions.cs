using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.Extensions.Localization;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Errors;

namespace ZX.Platform.Identity.Api.Extensions
{
    public static class ApiBehaviorExtensions
    {

        /// <summary>
        /// 配置统一的模型验证失败响应。
        /// </summary>
        public static IMvcBuilder AddUnifiedApiBehavior(this IMvcBuilder mvcBuilder)
        {
            mvcBuilder.ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<ValidationMessages>>();
                    var messages = new List<string>();

                    foreach (var item in context.ModelState)
                    {
                        if (item.Value is null)
                        {
                            continue;
                        }

                        foreach (var error in item.Value.Errors)
                        {
                            if (error.Exception != null || string.IsNullOrWhiteSpace(error.ErrorMessage))
                            {
                                messages.Add(
                                    localizer["Validation.ParameterInvalid", item.Key].Value);
                            }
                            else
                            {
                                messages.Add(error.ErrorMessage);
                            }
                        }
                    }

                    var message = string.Join(localizer["Validation.Separator"].Value, messages.Distinct());

                    if (string.IsNullOrWhiteSpace(message))
                    {
                        message = localizer["Validation.Failed"].Value;
                    }

                    var traceId =
                        Activity.Current?.Id
                        ?? context.HttpContext.TraceIdentifier;

                    var response = new ApiResponse<object?>
                    {
                        Code = ErrorCodes.ValidationFailed,
                        Message = message,
                        TraceId = traceId
                    };

                    return new BadRequestObjectResult(response);
                };
            });

            return mvcBuilder;
        }
    }
}
