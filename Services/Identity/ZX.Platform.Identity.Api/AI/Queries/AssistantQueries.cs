using System.ComponentModel;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.AI.Queries;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Api.AI.Queries;

// Invokes only discovered MVC GET endpoints. There is no model-supplied URL, SQL, user or header.
public sealed class AssistantQueries(AiQueryCatalog catalog, IHttpContextAccessor contexts,
    IServiceScopeFactory scopes, LinkGenerator links, AssistantLimits limits, TimeProvider clock,
    ILogger<AssistantQueries> logger) : IAssistantQueries
{
    private const int MaxRawResponseBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<AssistantToolResult> ListAsync(string? query, int offset, CancellationToken cancellationToken)
    {
        var outer = contexts.HttpContext;
        if (outer?.User.Identity?.IsAuthenticated != true) return Status("forbidden");
        if (offset < 0) return Status("invalid_arguments");
        var terms = (query ?? string.Empty).Split([' ', '\t', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries);
        var matches = catalog.Entries.Select(entry => new
        {
            Entry = entry,
            Score = terms.Count(term => SearchText(entry).Contains(term, StringComparison.OrdinalIgnoreCase))
        }).Where(match => terms.Length == 0 || match.Score > 0)
            .OrderByDescending(match => match.Score).ThenBy(match => match.Entry.Id, StringComparer.Ordinal);
        var available = new List<object>();
        var more = false;
        var skipped = 0;
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var policy = await PolicyAsync(match.Entry, outer.RequestServices);
            if (policy is null) continue;
            // The outer caller was authenticated by the normal chat endpoint. Execution authenticates again.
            var allowed = await outer.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(outer.User, outer, policy);
            if (!allowed.Succeeded) continue;
            if (skipped < offset) { skipped++; continue; }
            if (available.Count == 8) { more = true; break; }
            var entry = match.Entry;
            available.Add(new
            {
                id = entry.Id,
                description = entry.Description,
                controller = entry.Action.ControllerName,
                action = entry.Action.ActionName,
                route = entry.RouteTemplate,
                sourcePath = entry.SourcePath,
                responseType = entry.ResponseType,
                requiresSbu = entry.RequiresSbu,
                resultShape = entry.ResultShape switch
                {
                    AiQueryResultShape.CompleteList => "complete_list",
                    AiQueryResultShape.CompleteTree => "complete_tree",
                    _ => "automatic"
                },
                parameters = entry.Parameters.Select(parameter => new
                {
                    name = parameter.Name,
                    type = TypeName(parameter.Type),
                    required = parameter.IsRequired,
                    location = parameter.IsRoute ? "path" : "query",
                    defaultValue = parameter.DefaultValue,
                    description = parameter.Description
                }).ToArray()
            });
        }
        return Result(new
        {
            status = "ok", queries = available, offset, hasMore = more,
            nextOffset = more ? (int?)(offset + available.Count) : null,
            note = "仅列当前账号有权限的已标记查询，每页最多8条。hasMore为true时保持相同query、使用nextOffset继续；没有搜索结果时先不传query浏览，或搜索源码取得英文Controller名再搜索，不能据此断言系统无此功能。description可为空，sourcePath可能因发布资料过滤而不可读，可结合返回类型及相关DTO/页面理解用途。parameters仅为原接口参数。filters只筛选平面列表顶层标量字段，树形查询不要传filters，应读取完整children。查询数据分页不能用于全局计算；需要SBU的接口使用界面当前选择，模型不能指定请求头。"
        });
    }

    public async Task<AssistantToolResult> ExecuteAsync(string id, JsonElement parameters, JsonElement? filters,
        CancellationToken cancellationToken)
    {
        var outer = contexts.HttpContext;
        if (outer?.User.Identity?.IsAuthenticated != true) return Status("forbidden");
        var entry = catalog.Entries.SingleOrDefault(candidate => candidate.Id.Equals(id, StringComparison.Ordinal));
        if (entry is null) return Status("unknown_query", "此接口未明确开放给AI，未执行任何查询。");
        if (parameters.ValueKind != JsonValueKind.Object) return Status("invalid_arguments");

        try
        {
            var routeValues = new RouteValueDictionary(entry.Action.RouteValues.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
            var queryValues = new List<KeyValuePair<string, string?>>();
            var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in parameters.EnumerateObject())
            {
                var parameter = entry.Parameters.SingleOrDefault(candidate => candidate.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter is null || !supplied.Add(parameter.Name)) return Status("invalid_arguments");
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    if (parameter.IsRequired) return Status("invalid_arguments");
                    continue;
                }
                var values = ParameterValues(property.Value, parameter.Type);
                if (parameter.IsRoute)
                {
                    if (values.Count != 1) return Status("invalid_arguments");
                    routeValues[parameter.Name] = values[0];
                }
                else
                {
                    queryValues.AddRange(values.Select(value => new KeyValuePair<string, string?>(parameter.Name, value)));
                }
            }
            if (entry.Parameters.Any(parameter => parameter.IsRequired && !supplied.Contains(parameter.Name)))
                return Status("invalid_arguments", "缺少原接口要求的查询参数，请核对目录中的parameters。");

            var path = links.GetPathByAction(entry.Action.ActionName, entry.Action.ControllerName, routeValues);
            if (path is null || path.Contains('?') || path.StartsWith("//", StringComparison.Ordinal))
                return Status("invalid_arguments");
            var queryString = QueryString.Create(queryValues);
            if (queryString.Value?.Length > 8000) return Status("invalid_arguments");

            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            return await InIsolatedContextAsync(async () =>
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
                await using var scope = scopes.CreateAsyncScope();
                await using var buffer = new LimitedResponseStream(MaxRawResponseBytes);
                var child = new DefaultHttpContext
                {
                    RequestServices = scope.ServiceProvider,
                    RequestAborted = cancellationToken,
                    TraceIdentifier = outer.TraceIdentifier
                };
                child.Request.Method = HttpMethods.Get;
                child.Request.Scheme = outer.Request.Scheme;
                child.Request.Host = outer.Request.Host;
                child.Request.PathBase = outer.Request.PathBase;
                child.Request.Path = PathString.FromUriComponent(path);
                child.Request.QueryString = queryString;
                child.Request.RouteValues = routeValues;
                child.Connection.RemoteIpAddress = outer.Connection.RemoteIpAddress;
                child.Response.Body = buffer;
                child.SetEndpoint(entry.Endpoint);
                foreach (var name in new[] { "Authorization", "Accept-Language", "X-SBU-ID" })
                    if (outer.Request.Headers.TryGetValue(name, out var value)) child.Request.Headers[name] = value;

                contexts.HttpContext = child;
                try
                {
                    var policy = await PolicyAsync(entry, scope.ServiceProvider);
                    if (policy is null) return Status("forbidden");
                    // PolicyEvaluator without an explicit scheme normally trusts AuthenticationMiddleware's result.
                    // This isolated request has no such middleware result, so authenticate the default scheme explicitly.
                    if (policy.AuthenticationSchemes.Count == 0)
                    {
                        var scheme = await scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>()
                            .GetDefaultAuthenticateSchemeAsync();
                        if (scheme is null) return Status("forbidden");
                        policy = new AuthorizationPolicyBuilder(policy).AddAuthenticationSchemes(scheme.Name).Build();
                    }
                    var evaluator = scope.ServiceProvider.GetRequiredService<IPolicyEvaluator>();
                    var authentication = await evaluator.AuthenticateAsync(policy, child);
                    if (!authentication.Succeeded ||
                        child.User.FindFirstValue(JwtRegisteredClaimNames.Sub) != outer.User.FindFirstValue(JwtRegisteredClaimNames.Sub))
                        return Status("forbidden", "当前登录会话已失效或不允许此查询。");
                    var authorization = await evaluator.AuthorizeAsync(policy, authentication, child, child);
                    if (!authorization.Succeeded) return Status("forbidden", "当前账号没有该查询权限，未取得业务数据。");

                    // The MVC request delegate retains model binding, validation, RequireSbu and action/result filters.
                    // Never call MethodInfo.Invoke: doing so would skip these parts of the original endpoint.
                    await entry.Endpoint.RequestDelegate!(child);
                    await child.Response.BodyWriter.FlushAsync(cancellationToken);
                    if (child.Response.StatusCode is < 200 or >= 300)
                        return Status(child.Response.StatusCode switch
                        {
                            401 or 403 => "forbidden",
                            400 or 422 => "invalid_arguments",
                            404 => "not_found",
                            _ => "unavailable"
                        });
                    if (child.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                        return Status("unavailable", "该接口未返回受支持的JSON业务响应。");
                    buffer.Position = 0;
                    using var payload = await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken);
                    var root = payload.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var code) ||
                        !code.TryGetInt32(out var businessCode) || businessCode != 0 || !root.TryGetProperty("data", out var data))
                        return Status("unavailable", "该接口未返回成功的业务数据，不能据此计算或沿用历史值。");
                    Guid? selectedSbu = entry.RequiresSbu && Guid.TryParse(child.Request.Headers["X-SBU-ID"].ToString(), out var sbuId)
                        ? sbuId : null;
                    return AssistantQueryResult.Create(data, filters, entry.Id, entry.RequiresSbu, selectedSbu,
                        clock.GetUtcNow(), limits.MaxToolResultBytes, entry.ResultShape);
                }
                finally
                {
                    contexts.HttpContext = null;
                }
            }, cancellationToken);
        }
        catch (BusinessException exception)
        {
            if (exception.MessageKey == "Sbu.SelectionRequired")
                return Status("selection_required", "此查询需要SBU，请先在系统界面选择SBU，再重新提问。");
            return Status(exception.Code switch
            {
                ErrorCodes.Forbidden or ErrorCodes.Unauthorized => "forbidden",
                ErrorCodes.ValidationFailed => "invalid_arguments",
                ErrorCodes.NotFound => "not_found",
                _ => "unavailable"
            });
        }
        catch (QueryResponseTooLargeException)
        {
            return Status("too_large", "接口原始响应超过16MiB，未向模型返回任何部分数据。请使用接口本身的筛选参数缩小范围，后置filters不能绕过此上限。");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return Status("invalid_arguments");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // No response body, source, query values, token or exception message is logged.
            logger.LogWarning("Assistant query failed. ErrorType={ErrorType}", exception.GetType().Name);
            return Status("unavailable", "查询暂时不可用，本次未取得可用业务数据。");
        }
    }

    private static Task<AuthorizationPolicy?> PolicyAsync(AiQueryEndpoint entry, IServiceProvider services)
        => AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(),
            entry.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            entry.Endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

    private static Task<AssistantToolResult> InIsolatedContextAsync(Func<Task<AssistantToolResult>> action,
        CancellationToken cancellationToken)
    {
        // HttpContextAccessor clears the previous shared holder when assigned. Suppressing the ambient
        // context only while creating this awaited task prevents a child from clearing the caller's holder.
        // No work is left running after the query finishes; the child owns its DI scope and cancellation.
        if (ExecutionContext.IsFlowSuppressed()) return Task.Run(action, cancellationToken);
        using (ExecutionContext.SuppressFlow()) return Task.Run(action, cancellationToken);
    }

    private static IReadOnlyList<string> ParameterValues(JsonElement value, Type type)
    {
        if (type.IsArray)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is 0 or > 30)
                throw new ArgumentException("Invalid array parameter.");
            return value.EnumerateArray().Select(item => ParameterText(item, type.GetElementType()!)).ToArray();
        }
        return [ParameterText(value, type)];
    }

    private static string ParameterText(JsonElement value, Type type)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("Invalid scalar parameter.");
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        if (text.Length > 1000 || text.Any(char.IsControl)) throw new ArgumentException("Invalid parameter text.");
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return text;
        var converter = TypeDescriptor.GetConverter(type);
        var converted = converter.ConvertFromInvariantString(text);
        if (converted is null || type.IsEnum && !Enum.IsDefined(type, converted) ||
            converted is double number && !double.IsFinite(number) ||
            converted is float single && !float.IsFinite(single))
            throw new ArgumentException("Invalid scalar value.");
        return converted switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            _ => converter.ConvertToInvariantString(converted) ?? throw new ArgumentException("Invalid scalar value.")
        };
    }

    private static string SearchText(AiQueryEndpoint entry)
        => string.Join(' ', new[] { entry.Id, entry.Description, entry.RouteTemplate, entry.SourcePath, entry.ResponseType }
            .Concat(entry.Parameters.Select(parameter => parameter.Name)));

    private static string TypeName(Type type)
        => type.IsArray ? $"{TypeName(type.GetElementType()!)}[]" :
            Nullable.GetUnderlyingType(type) is { } inner ? $"{TypeName(inner)}?" : type.Name;

    private AssistantToolResult Result(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return bytes.Length <= limits.MaxToolResultBytes
            ? new AssistantToolResult(Encoding.UTF8.GetString(bytes), [])
            : Status("too_large", "查询目录超过容量，请用更明确的模块或接口名称搜索。");
    }

    private static AssistantToolResult Status(string status, string? message = null)
        => new(JsonSerializer.Serialize(new { status, message }, JsonOptions), []);

    private sealed class QueryResponseTooLargeException : IOException { }

    private sealed class LimitedResponseStream(int maximum) : MemoryStream
    {
        private void CheckSize(int count)
        {
            if (Position + count > maximum) throw new QueryResponseTooLargeException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckSize(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckSize(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            CheckSize(1);
            base.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            CheckSize(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CheckSize(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void SetLength(long value)
        {
            if (value > maximum) throw new QueryResponseTooLargeException();
            base.SetLength(value);
        }
    }
}
