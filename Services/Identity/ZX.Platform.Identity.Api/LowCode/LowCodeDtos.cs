using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.Identity.Api.LowCode;

/// <summary>保存草稿。Definition 保持 JsonElement，交给 MetadataJson 解析。</summary>
/// <remarks>
/// 不直接绑定成 EntityDefinition，是为了让"设计器提交的 JSON"和"落库的 JSON"
/// 走同一个解析器（MetadataJson.Options 里有两个自定义转换器）。
/// 走 MVC 的全局 JSON 配置就会出现两套规则，字段类型写错时两边表现不一致。
/// </remarks>
public sealed class EntityDraftRequest
{
    public int Revision { get; init; }

    public JsonElement Definition { get; init; }
}

public sealed class EntityCreateRequest
{
    public JsonElement Definition { get; init; }
}

public sealed class RecordWriteRequest
{
    /// <summary>修改时必填，用于乐观锁。</summary>
    public int? Revision { get; init; }

    public JsonElement Values { get; init; }
}

public sealed record EntitySummaryDto(
    Guid Id,
    string AppCode,
    string Code,
    string Name,
    string Status,
    int Revision,
    int PublishedVersion);

public sealed record PublishResponseDto(
    bool Published,
    int Version,
    IReadOnlyList<SchemaChangeDto> PendingChanges);

/// <summary>
/// 列表查询入参。算子用字符串（"eq" / "between" …），
/// 因为 <see cref="QueryOperator"/> 是枚举，直接绑定会要求前端传数字，
/// 而前端和后端约定的线格式是 <c>QueryOperators.ToWireName()</c> 那套小写词。
/// </summary>
public sealed class LowCodeQueryRequest
{
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<FilterDto> Filters { get; init; } = [];

    public IReadOnlyList<ViewSort> Sorts { get; init; } = [];

    public int Skip { get; init; }

    public int Take { get; init; } = ViewDefinition.DefaultPageSize;

    public RecordQueryRequest ToRequest() => new()
    {
        Fields = Fields,
        Filters = [.. Filters.Select(item => item.ToCondition())],
        Sorts = Sorts,
        Skip = Skip,
        Take = Take
    };
}

public sealed class FilterDto
{
    public string Field { get; init; } = string.Empty;

    public string Op { get; init; } = "eq";

    public string? Value { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public FilterCondition ToCondition() =>
        QueryOperators.TryParse(Op, out var op)
            ? new FilterCondition(Field, op, Value, Values)
            : throw new LowCodeException(LowCodeErrorCodes.OperatorNotAllowed, $"未知算子「{Op}」。");
}

public sealed record SchemaChangeDto(
    Guid Id,
    string EntityCode,
    string Kind,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc)
{
    public static SchemaChangeDto From(SchemaChangeRecord record) => new(
        record.Id, record.EntityCode, record.Kind, record.Status, record.ErrorMessage, record.CreatedAtUtc);
}

/// <summary>
/// JSON → 写入值。System.Text.Json 把 object 值都变成 JsonElement，
/// 直接交给 Dapper 会把 JsonElement 绑到 Oracle 参数上，必须在这里落成 CLR 原语。
/// </summary>
public static class JsonValues
{
    public static IReadOnlyDictionary<string, object?> ToValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, "values 必须是一个 JSON 对象。");

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            values[property.Name] = ToValue(property.Value);

        return values;
    }

    private static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        // 数组（multiselect / attachment）保留原始 JSON 文本，由字段类型决定怎么落库。
        _ => value.GetRawText()
    };
}

/// <summary>
/// 权限与数据范围。两条都只从会话取，绝不读请求体。
///
/// 权限码是动态的（每个实体一组，形如 <c>mes.stocktake.read</c>），
/// 所以**不能**用 <see cref="IPermissionService.HasAsync"/>：那个方法第一步就查
/// <c>IdentityPermissions.All</c> 静态目录，动态码一律不在里面，结果所有低代码接口
/// 连管理员都 403。这里直接读授权快照——快照来自权限表与角色授权，本身就是权威来源。
///
/// SBU 归属校验与 [RequireSbu] 用的是同一个仓储方法。
/// </summary>
public sealed class LowCodePermissionGuard(IPermissionService permissions, IUserSbuRepository userSbus)
{
    public async Task<Guid?> RequireAsync(HttpContext http, string code, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId(http);
        if (userId is null)
            throw new LowCodeException(LowCodeErrorCodes.Forbidden, "未登录或凭据已失效。");

        var snapshot = await permissions.GetAsync(userId.Value, cancellationToken);
        if (snapshot.IsAdministrator || snapshot.Permissions.Contains(code, StringComparer.Ordinal))
            return userId;

        throw new LowCodeException(LowCodeErrorCodes.Forbidden, $"缺少权限 {code}。");
    }

    /// <summary>按实体的 scopeMode 算出数据范围。实体要求 SBU 时必须带 X-SBU-ID 且用户属于该 SBU。</summary>
    public async Task<RecordScope> ResolveScopeAsync(
        EntityDefinition entity, HttpContext http, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId(http);

        if (!entity.HasSbuScope) return RecordScope.Unscoped(userId);

        var header = http.Request.Headers["X-SBU-ID"];
        if (header.Count != 1 || !Guid.TryParse(header[0], out var sbuId) || sbuId == Guid.Empty)
            throw LowCodeException.SbuRequired($"实体「{entity.Code}」按 SBU 隔离，请在请求头带上 X-SBU-ID。");

        if (userId is null || await userSbus.GetEnabledSbuForUserAsync(userId.Value, sbuId, cancellationToken) is null)
            throw new LowCodeException(LowCodeErrorCodes.SbuDenied, "当前用户无权访问该 SBU。");

        return new RecordScope(userId, sbuId, false);
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ? userId : null;
}
