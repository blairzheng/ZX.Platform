using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.LowCode;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.Identity.Api.Controllers;

/// <summary>
/// 低代码元数据。设计器读写草稿、发布、看版本、回滚。
///
/// 所有接口都用 <see cref="ApiResponse{T}"/>：低代码是 Identity 进程里的一个模块，
/// 响应结构必须和其余接口一致，否则前端 request.ts 的解包逻辑要写两套。
/// </summary>
[Route("api/lowcode/entities")]
[ApiController]
[Authorize]
public sealed class LowCodeMetadataController(
    MetadataService metadata,
    IMetadataStore store,
    LowCodePermissionGuard guard) : ControllerBase
{
    private const string MetaRead = IdentityPermissions.LowCodeMetaRead;
    private const string MetaWrite = IdentityPermissions.LowCodeMetaWrite;
    private const string MetaPublish = IdentityPermissions.LowCodeMetaPublish;

    [HttpGet("{appCode}")]
    public async Task<IActionResult> ListAsync(string appCode, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, MetaRead, cancellationToken);

        var entities = await store.ListEntitiesAsync(appCode, cancellationToken);
        var data = entities
            .Select(item => new EntitySummaryDto(
                item.Id, item.AppCode, item.Code, item.Name, item.Status,
                item.Revision, item.PublishedVersion))
            .ToList();

        return Ok(new ApiResponse<List<EntitySummaryDto>> { Code = 0, Message = "ok", Data = data });
    }

    [HttpPost("{appCode}")]
    public async Task<IActionResult> CreateAsync(
        string appCode, [FromBody] EntityCreateRequest request, CancellationToken cancellationToken)
    {
        var userId = await guard.RequireAsync(HttpContext, MetaWrite, cancellationToken);

        var definition = MetadataJson.Deserialize<EntityDefinition>(request.Definition.GetRawText());
        var record = await metadata.CreateAsync(appCode, definition, userId, cancellationToken);

        var data = new EntitySummaryDto(
            record.Id, record.AppCode, record.Code, record.Name, record.Status,
            record.Revision, record.PublishedVersion);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<EntitySummaryDto> { Code = 0, Message = "实体已创建。", Data = data });
    }

    [HttpGet("{appCode}/{code}/draft")]
    public async Task<IActionResult> GetDraftAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, MetaRead, cancellationToken);

        var draft = await metadata.GetDraftAsync(appCode, code, cancellationToken);
        return Ok(new ApiResponse<EntityDefinition> { Code = 0, Message = "ok", Data = draft });
    }

    [HttpGet("{appCode}/{code}/published")]
    public async Task<IActionResult> GetPublishedAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, MetaRead, cancellationToken);

        var published = await metadata.GetPublishedAsync(appCode, code, cancellationToken);
        return Ok(new ApiResponse<EntityDefinition?> { Code = 0, Message = "ok", Data = published });
    }

    [HttpPut("{appCode}/{code}/draft")]
    public async Task<IActionResult> SaveDraftAsync(
        string appCode, string code, [FromBody] EntityDraftRequest request, CancellationToken cancellationToken)
    {
        var userId = await guard.RequireAsync(HttpContext, MetaWrite, cancellationToken);

        var definition = MetadataJson.Deserialize<EntityDefinition>(request.Definition.GetRawText());
        await metadata.SaveDraftAsync(appCode, code, definition, request.Revision, userId, cancellationToken);

        return Ok(new ApiResponse { Code = 0, Message = "草稿已保存。" });
    }

    [HttpPost("{appCode}/{code}/publish")]
    public async Task<IActionResult> PublishAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        var userId = await guard.RequireAsync(HttpContext, MetaPublish, cancellationToken);

        var result = await metadata.PublishAsync(appCode, code, userId, cancellationToken);
        var data = new PublishResponseDto(
            result.Published,
            result.Version,
            [.. result.PendingChanges.Select(SchemaChangeDto.From)]);

        // 被结构变更挡住不是错误，是正常状态：200 + published = false，
        // 前端据此跳到"结构变更"页，而不是弹一个红色的报错。
        return Ok(new ApiResponse<PublishResponseDto>
        {
            Code = 0,
            Message = result.Published ? "已发布。" : "存在未执行的结构变更，已生成变更计划。",
            Data = data
        });
    }

    [HttpGet("{appCode}/{code}/versions")]
    public async Task<IActionResult> ListVersionsAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, MetaRead, cancellationToken);

        var versions = await metadata.ListVersionsAsync(appCode, code, cancellationToken);
        var data = versions
            .Select(item => new { item.Version, item.CreatedAtUtc, item.CreatedByUserId })
            .ToList();

        return Ok(new ApiResponse<object> { Code = 0, Message = "ok", Data = data });
    }

    [HttpPost("{appCode}/{code}/rollback")]
    public async Task<IActionResult> RollbackAsync(
        string appCode, string code, [FromQuery] int version, CancellationToken cancellationToken)
    {
        var userId = await guard.RequireAsync(HttpContext, MetaPublish, cancellationToken);

        var restored = await metadata.RollbackAsync(appCode, code, version, userId, cancellationToken);
        return Ok(new ApiResponse<EntityDefinition>
        {
            Code = 0,
            Message = "已回退到草稿，需重新发布才会生效。",
            Data = restored
        });
    }
}

/// <summary>
/// 业务数据读写。路由里带 appCode 和实体 code，权限码按实体算：
/// <c>{app}.{entity}.read / create / update / delete</c>。
/// </summary>
[Route("api/lowcode/records")]
[ApiController]
[Authorize]
public sealed class LowCodeRecordController(
    MetadataService metadata,
    QueryEngine query,
    RecordEngine records,
    LowCodePermissionGuard guard) : ControllerBase
{
    [HttpPost("{appCode}/{code}/query")]
    public async Task<IActionResult> QueryAsync(
        string appCode, string code, [FromBody] LowCodeQueryRequest request, CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, code, cancellationToken);
        await guard.RequireAsync(HttpContext, Permission(entity, "read"), cancellationToken);

        var scope = await guard.ResolveScopeAsync(entity, HttpContext, cancellationToken);
        var page = await query.QueryAsync(appCode, code, request.ToRequest(), scope, cancellationToken);

        return Ok(new ApiResponse<RecordPage> { Code = 0, Message = "ok", Data = page });
    }

    [HttpGet("{appCode}/{code}/{id:guid}")]
    public async Task<IActionResult> FindAsync(
        string appCode, string code, Guid id, CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, code, cancellationToken);
        await guard.RequireAsync(HttpContext, Permission(entity, "read"), cancellationToken);

        var scope = await guard.ResolveScopeAsync(entity, HttpContext, cancellationToken);
        var row = await query.FindAsync(appCode, code, id, scope, cancellationToken);

        if (row is null)
            throw new LowCodeException(LowCodeErrorCodes.NotFound, "记录不存在或已删除。");

        return Ok(new ApiResponse<IReadOnlyDictionary<string, object?>> { Code = 0, Message = "ok", Data = row });
    }

    [HttpPost("{appCode}/{code}")]
    public async Task<IActionResult> CreateAsync(
        string appCode, string code, [FromBody] RecordWriteRequest request, CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, code, cancellationToken);
        await guard.RequireAsync(HttpContext, Permission(entity, "create"), cancellationToken);

        var scope = await guard.ResolveScopeAsync(entity, HttpContext, cancellationToken);
        var id = await records.CreateAsync(
            appCode, code, JsonValues.ToValues(request.Values), scope, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<Guid> { Code = 0, Message = "已新增。", Data = id });
    }

    [HttpPut("{appCode}/{code}/{id:guid}")]
    public async Task<IActionResult> UpdateAsync(
        string appCode, string code, Guid id,
        [FromBody] RecordWriteRequest request, CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, code, cancellationToken);
        await guard.RequireAsync(HttpContext, Permission(entity, "update"), cancellationToken);

        if (request.Revision is null)
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, "修改必须带 revision。");

        var scope = await guard.ResolveScopeAsync(entity, HttpContext, cancellationToken);
        await records.UpdateAsync(
            appCode, code, id, request.Revision.Value,
            JsonValues.ToValues(request.Values), scope, cancellationToken);

        return Ok(new ApiResponse { Code = 0, Message = "已保存。" });
    }

    [HttpDelete("{appCode}/{code}/{id:guid}")]
    public async Task<IActionResult> DeleteAsync(
        string appCode, string code, Guid id,
        [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, code, cancellationToken);
        await guard.RequireAsync(HttpContext, Permission(entity, "delete"), cancellationToken);

        var scope = await guard.ResolveScopeAsync(entity, HttpContext, cancellationToken);
        await records.DeleteAsync(appCode, code, id, revision, scope, cancellationToken);

        return Ok(new ApiResponse { Code = 0, Message = "已删除。" });
    }

    private static string Permission(EntityDefinition entity, string action) =>
        $"{entity.PermissionPrefix}.{action}";
}

/// <summary>
/// 结构变更。测试环境执行、生产导出、核对后放行发布。
/// 这些接口不对业务用户开放，权限码固定为 lowcode.schema.*。
/// </summary>
[Route("api/lowcode/schema-changes")]
[ApiController]
[Authorize]
public sealed class LowCodeSchemaChangeController(
    SchemaChangeService changes,
    LowCodePermissionGuard guard) : ControllerBase
{
    private const string SchemaRead = IdentityPermissions.LowCodeSchemaRead;
    private const string SchemaExecute = IdentityPermissions.LowCodeSchemaExecute;

    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string entityCode, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, SchemaRead, cancellationToken);

        var list = await changes.ListAsync(entityCode, cancellationToken);
        var data = list.Select(SchemaChangeDto.From).ToList();

        return Ok(new ApiResponse<List<SchemaChangeDto>> { Code = 0, Message = "ok", Data = data });
    }

    [HttpGet("{id:guid}/script")]
    public async Task<IActionResult> ScriptAsync(Guid id, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, SchemaRead, cancellationToken);

        var script = await changes.ExportAsync(id, cancellationToken);
        return Ok(new ApiResponse<string> { Code = 0, Message = "ok", Data = script });
    }

    [HttpPost("{id:guid}/execute")]
    public async Task<IActionResult> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = await guard.RequireAsync(HttpContext, SchemaExecute, cancellationToken);

        var change = await changes.ExecuteAsync(id, userId, cancellationToken);
        return Ok(new ApiResponse<SchemaChangeDto>
        {
            Code = 0,
            Message = "已执行并核对通过。",
            Data = SchemaChangeDto.From(change)
        });
    }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> VerifyAsync(Guid id, CancellationToken cancellationToken)
    {
        await guard.RequireAsync(HttpContext, SchemaExecute, cancellationToken);

        var result = await changes.VerifyAsync(id, cancellationToken);
        return Ok(new ApiResponse<VerifyResult>
        {
            Code = 0,
            Message = result.Verified ? "核对通过，可以发布。" : "库结构还没跟上计划。",
            Data = result
        });
    }
}
