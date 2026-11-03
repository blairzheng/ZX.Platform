using Microsoft.AspNetCore.Mvc.Filters;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Api.Authentication;

// SameSite 配合非简单请求头；不允许跨域携带凭据的宽泛 CORS 策略。
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireIdentityCsrfHeaderAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && request.Headers["X-Identity-CSRF"] != "1")
            throw new BusinessException(ErrorCodes.Forbidden, "Auth.CsrfRequired");
    }
}
