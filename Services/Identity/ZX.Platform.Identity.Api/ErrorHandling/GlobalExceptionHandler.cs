using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Domain.Exceptions;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authorization;

namespace ZX.Platform.Identity.Api.ErrorHandling
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IApiMessageLocalizer _messages;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IApiMessageLocalizer messages)
        {
            _logger = logger;
            _messages = messages;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var traceId =
                Activity.Current?.Id
                ?? httpContext.TraceIdentifier;

            var statusCode = StatusCodes.Status500InternalServerError;
            var code = ErrorCodes.SystemError;
            var message = _messages["Error.System"].Value;
            object? data = null;

            if (exception is BusinessException businessException)
            {
                statusCode = GetStatusCode(
                    businessException.Code);

                code = businessException.Code;
                message = Localize(businessException.MessageKey, "Error.OperationFailed");
                data = businessException.ResponseData;

                _logger.LogWarning(
                    "业务异常。Code={Code}, Message={Message}, TraceId={TraceId}",
                    code,
                    message,
                    traceId);
            }
            else if (exception is AuthSessionUnavailableException or AuthorizationUnavailableException)
            {
                statusCode = StatusCodes.Status503ServiceUnavailable;
                code = ErrorCodes.ServiceUnavailable;
                message = _messages["Auth.ServiceUnavailable"].Value;
                _logger.LogError(exception, "认证会话存储不可用。TraceId={TraceId}", traceId);
            }
            else if (exception is DomainException domainException)
            {
                statusCode = StatusCodes.Status409Conflict;
                code = ErrorCodes.InvalidState;
                message = Localize(domainException.MessageKey, "Error.InvalidState");

                _logger.LogWarning(
                    "领域规则异常。Message={Message}, TraceId={TraceId}",
                    message,
                    traceId);
            }
            else if (exception is UniqueConstraintException)
            {
                statusCode = StatusCodes.Status409Conflict;
                code = ErrorCodes.Conflict;
                message = _messages["Error.Duplicate"].Value;

                _logger.LogWarning(
                    exception,
                    "数据库唯一约束冲突。TraceId={TraceId}",
                    traceId);
            }
            else
            {
                _logger.LogError(
                    exception,
                    "系统异常。TraceId={TraceId}",
                    traceId);
            }

            httpContext.Response.StatusCode = statusCode;
            if (statusCode == StatusCodes.Status401Unauthorized)
                httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            httpContext.Response.Headers.CacheControl = "no-store";

            var response = new ApiResponse<object?>
            {
                Code = code,
                Message = message,
                Data = data,
                TraceId = traceId
            };

            await httpContext.Response.WriteAsJsonAsync(
                response,
                cancellationToken);

            return true;
        }

        private string Localize(string key, string fallbackKey)
        {
            var message = _messages[key];
            if (!message.ResourceNotFound)
                return message.Value;

            _logger.LogWarning("缺少提示资源。Key={MessageKey}", key);
            return _messages[fallbackKey].Value;
        }

        private static int GetStatusCode(int code)
        {
            return code switch
            {
                ErrorCodes.ValidationFailed =>
                    StatusCodes.Status400BadRequest,

                ErrorCodes.NotFound =>
                    StatusCodes.Status404NotFound,

                ErrorCodes.Conflict =>
                    StatusCodes.Status409Conflict,

                ErrorCodes.InvalidState =>
                    StatusCodes.Status409Conflict,

                ErrorCodes.Forbidden =>
                    StatusCodes.Status403Forbidden,

                ErrorCodes.Unauthorized =>
                    StatusCodes.Status401Unauthorized,

                ErrorCodes.TooManyRequests =>
                    StatusCodes.Status429TooManyRequests,

                _ =>
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
