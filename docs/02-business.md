# 新增一个业务模块

[返回首页](../README.md) · [本机配置与启动](01-start.md)

以下用“设备点检记录”演示新增模块的完整后端链路：创建实体 → 配置 Oracle 映射和 DbSet → 生成迁移并更新测试库 → 编写业务接口 → 接入权限、SBU 和菜单 → 联调。

设备点检是本文的开发示例，仓库中尚未实现。示例先实现“分页查询、新增”两个接口，修改、删除和状态流转按文末要求扩展，不要把文档中的接口当作已有功能。

需要参考真实业务时，可查看已有的一键盘点：API 的 `InventoryStocktakeController.cs`、Application 的 `Inventory/Stocktake/` 和 Infrastructure 的 `Inventory/StocktakeReportRepository.cs`。它只读 `MesEDA` 的 `EDA.G_WMS_KEYBOARD`，不区分 SBU、全量返回后由前端过滤，不需要新增表或 Migration；不要把这套取数范围直接套到本文的 SBU 分页示例上。

当前模块的说明从 [业务文档目录](business/README.md) 查找，一键盘点的实际契约见 [一键盘点](business/inventory/stocktake.md)。成员如何编写和更新文档，统一按 [成员业务文档编写规范](03-documentation.md)；本文件继续维护通用开发步骤。

所有命令在后端仓库根目录执行。多行命令使用 bash/zsh 写法；PowerShell 中可去掉行末反斜杠，将同一条命令合成一行执行。

## 1. 确定本次要做的业务

| 项目 | 本例约定 |
| --- | --- |
| 业务名称 | 设备点检 |
| 数据 | 每条记录包含设备编号、点检时间、结果和备注，归属一个 SBU |
| 结果值 | `pass` / `fail` |
| 功能权限 | `equipment.inspection.read`、`equipment.inspection.create` |
| 数据范围 | 用户只能查询、创建当前已授权 SBU 的记录 |
| 菜单路径 / 组件标识 | `/equipment/inspection` / `EquipmentInspection` |

实际需求先明确字段长度、必填项、唯一性、状态流转和数据范围，再按下面步骤替换业务名称。在自己的业务分支实施，完成后按文末要求交付，由负责人审核合并。

下面列出的各层项目均位于 `Services/Identity/` 下：

| 项目 | 新增或修改的位置 |
| --- | --- |
| `ZX.Platform.Identity.Domain` | `Entities/InspectionRecord.cs` |
| `ZX.Platform.Identity.Infrastructure` | `Persistence/Configurations/InspectionRecordConfiguration.cs`、`Persistence/PlatformIdentityDbContext.cs`、`Persistence/Migrations/`、`Persistence/Repositories/InspectionRecordRepository.cs` |
| `ZX.Platform.Identity.Application` | `InspectionRecords/` 下的 DTO、Service、Repository 接口；`Authorization/IdentityPermissions.cs` |
| `ZX.Platform.Identity.Api` | `Controllers/InspectionRecordsController.cs`、`Program.cs`、`Resources/Localization/` |

## 2. 创建实体

新增 `Services/Identity/ZX.Platform.Identity.Domain/Entities/InspectionRecord.cs`：

```csharp
using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities;

public sealed class InspectionRecord : SoftDeletableEntity
{
    private InspectionRecord() { }

    public Guid Id { get; private set; }
    public Guid SbuId { get; private set; }
    public string EquipmentCode { get; private set; } = string.Empty;
    public DateTimeOffset InspectedAtUtc { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string? Remark { get; private set; }

    public static InspectionRecord Create(Guid sbuId, string equipmentCode,
        DateTimeOffset inspectedAtUtc, string result, string? remark)
    {
        if (sbuId == Guid.Empty || string.IsNullOrWhiteSpace(equipmentCode) ||
            equipmentCode.Trim().Length > 100 || inspectedAtUtc == default ||
            result is not ("pass" or "fail") || (remark?.Trim().Length ?? 0) > 500)
            throw new DomainException("Inspection.InvalidData");

        return new InspectionRecord
        {
            Id = Guid.NewGuid(),
            SbuId = sbuId,
            EquipmentCode = equipmentCode.Trim(),
            InspectedAtUtc = inspectedAtUtc.ToUniversalTime(),
            Result = result,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    protected override void OnDeleted() { }
    protected override void OnRestored() { }
}
```

实体使用私有 setter 和行为方法维护规则。`SoftDeletableEntity` 已提供审计和软删除字段，不重复声明。当前拦截器自动维护创建、修改时间，创建人和修改人还需要具体用例接入，不能认为继承基类就已记录操作者。

本例不允许普通编辑修改 `SbuId`。Oracle 将空字符串视为 NULL，备注使用可空属性和可空列，不通过空字符串模拟非空值。

## 3. 配置映射和 DbSet

新增 `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations/InspectionRecordConfiguration.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class InspectionRecordConfiguration : IEntityTypeConfiguration<InspectionRecord>
{
    public void Configure(EntityTypeBuilder<InspectionRecord> builder)
    {
        builder.ToTable("ZX_IDENTITY_INSPECTION_RECORD");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
        builder.Property(x => x.SbuId).HasColumnName("SBU_ID").IsRequired();
        builder.Property(x => x.EquipmentCode).HasColumnName("EQUIPMENT_CODE")
            .HasMaxLength(100).IsRequired();
        builder.Property(x => x.InspectedAtUtc).HasColumnName("INSPECTED_AT_UTC")
            .HasColumnType("TIMESTAMP(7) WITH TIME ZONE").IsRequired();
        builder.Property(x => x.Result).HasColumnName("RESULT").HasMaxLength(4).IsRequired();
        builder.Property(x => x.Remark).HasColumnName("REMARK").HasMaxLength(500).IsRequired(false);
        builder.HasIndex(x => new { x.SbuId, x.InspectedAtUtc })
            .HasDatabaseName("IX_INSPECTION_SBU_TIME");
        builder.ConfigureAuditing();
        builder.ConfigureSoftDelete();
    }
}
```

在同项目 `Persistence/PlatformIdentityDbContext.cs` 中增加：

```csharp
public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
```

现有 `ApplyConfigurationsFromAssembly` 会发现新配置，不需要逐项注册。不在业务映射中写死测试库 schema，运行时沿用 `IdentityDatabase:Schema`；迁移仍统一以 `ZX_IDENTITY` 为源 schema 生成。

本例没有约定组合唯一；实际业务需要唯一性时，在配置中建立对应唯一索引。Service 的重复检查不能代替数据库唯一约束。

## 4. 生成迁移并更新测试库

完成实体、映射和 DbSet 后就处理迁移，再继续下面的接口联调。只有生成文件，没有执行到测试库，访问新增表或字段仍会失败。

### 4.1 生成迁移

先同步团队最新基线，确认没有混入其他任务尚未处理的模型变更，然后执行完整命令：

```bash
dotnet ef migrations add AddInspectionRecords \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output-dir Persistence/Migrations
```

**所有成员必须保留 `--output-dir Persistence/Migrations`。** 只按本次变更修改迁移名称。命令会编译项目；若编译失败，先修复当前代码。

检查 `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Migrations/` 下的三个文件：

| 文件 | 检查重点 |
| --- | --- |
| `<时间戳>_AddInspectionRecords.cs` | `Up` 只新增预期表、字段及索引；`Down` 与之对应 |
| `<时间戳>_AddInspectionRecords.Designer.cs` | 与本次迁移一起生成并提交 |
| `PlatformIdentityDbContextModelSnapshot.cs` | 包含新实体，并保留原有 Identity 模型 |

若出现无关的用户、角色、菜单表改动，先检查模型与合并结果，不直接执行。涉及已有表的必填列、默认值和数据回填时，单独审核存量数据转换；不把角色授权、菜单创建或其他业务操作塞进迁移。

### 4.2 更新测试库

生成迁移后，执行下面的命令，将当前分支尚未执行的迁移应用到团队测试库：

```bash
dotnet ef database update \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  -- --development
```

保留末尾的 `-- --development`。入口会读取第一份文档中已导入的 Identity.Api User Secrets，自动使用测试库连接和目标 schema，不需要再填写密码或手工导出 SQL。该命令会实际更新测试库；应用启动不会代替这一步。

只有更新成功后，才继续后面的接口联调。不要给生成迁移的 `migrations add` 命令添加 `-- --development`。

### 4.3 验证并提交迁移

确认命令执行成功，测试库出现预期的表、字段和索引；业务接口编写完成后，再验证实际读写。

本次生成的迁移文件、Designer 和 snapshot 随业务代码一起提交，负责人合并时不重复生成同一份迁移。现有 `Tests/ZX.Platform.Identity.AuthChecks/Oracle19Checks.cs` 对末位迁移有断言，新增迁移时同步该预期，保留原有检查。

多人共享测试库，更新前先同步团队基线并协调执行。遇到迁移冲突、测试库版本不一致或执行失败时，联系负责人处理，不自行删除迁移或回退共享库。

## 5. 定义 DTO、Service 和 Repository 接口

在 `Services/Identity/ZX.Platform.Identity.Application/InspectionRecords/` 下新增以下文件。

`Dtos/InspectionRecordDtos.cs`：

```csharp
using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Common;

namespace ZX.Platform.Identity.Application.InspectionRecords.Dtos;

public sealed class CreateInspectionRecordDto
{
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string EquipmentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    public DateTimeOffset? InspectedAtUtc { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [RegularExpression("^(pass|fail)$", ErrorMessage = "Validation.InvalidValue")]
    public string Result { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Validation.StringLength")]
    public string? Remark { get; set; }
}

public sealed class InspectionRecordQueryDto : PageQueryDto { }

public sealed record InspectionRecordDto(Guid Id, string EquipmentCode,
    DateTimeOffset InspectedAtUtc, string Result, string? Remark);

public sealed record InspectionRecordPageDto(List<InspectionRecordDto> List, int Total);
```

输入 DTO 不接收 `SbuId`。`DateTimeOffset` 必填用可空属性配合 `[Required]`，避免遗漏字段时被当成有效默认值。通用校验消息复用现有资源键。

`IInspectionRecordRepository.cs`：

```csharp
using ZX.Platform.Identity.Application.InspectionRecords.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.InspectionRecords;

public interface IInspectionRecordRepository
{
    Task<InspectionRecordPageDto> ListAsync(Guid sbuId,
        InspectionRecordQueryDto query, CancellationToken cancellationToken);
    void Add(InspectionRecord record);
}
```

`IInspectionRecordService.cs`：

```csharp
using ZX.Platform.Identity.Application.InspectionRecords.Dtos;

namespace ZX.Platform.Identity.Application.InspectionRecords;

public interface IInspectionRecordService
{
    Task<InspectionRecordPageDto> ListAsync(Guid sbuId,
        InspectionRecordQueryDto query, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(Guid sbuId,
        CreateInspectionRecordDto input, CancellationToken cancellationToken);
}
```

## 6. 实现 Repository 和 Service

新增 `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Repositories/InspectionRecordRepository.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.InspectionRecords;
using ZX.Platform.Identity.Application.InspectionRecords.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

public sealed class InspectionRecordRepository(PlatformIdentityDbContext db)
    : IInspectionRecordRepository
{
    public async Task<InspectionRecordPageDto> ListAsync(Guid sbuId,
        InspectionRecordQueryDto query, CancellationToken cancellationToken)
    {
        var skip = query.GetSkip();
        var scoped = db.InspectionRecords.AsNoTracking().Where(x => x.SbuId == sbuId);
        var total = await scoped.CountAsync(cancellationToken);
        var list = await scoped.OrderByDescending(x => x.InspectedAtUtc).ThenBy(x => x.Id)
            .Skip(skip).Take(query.PageSize)
            .Select(x => new InspectionRecordDto(
                x.Id, x.EquipmentCode, x.InspectedAtUtc, x.Result, x.Remark))
            .ToListAsync(cancellationToken);
        return new InspectionRecordPageDto(list, total);
    }

    public void Add(InspectionRecord record) => db.InspectionRecords.Add(record);
}
```

新增 `Services/Identity/ZX.Platform.Identity.Application/InspectionRecords/InspectionRecordService.cs`：

```csharp
using ZX.Platform.Identity.Application.InspectionRecords.Dtos;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.InspectionRecords;

public sealed class InspectionRecordService(
    IInspectionRecordRepository records, IUnitOfWork unitOfWork) : IInspectionRecordService
{
    public Task<InspectionRecordPageDto> ListAsync(Guid sbuId,
        InspectionRecordQueryDto query, CancellationToken cancellationToken) =>
        records.ListAsync(sbuId, query, cancellationToken);

    public async Task<Guid> CreateAsync(Guid sbuId,
        CreateInspectionRecordDto input, CancellationToken cancellationToken)
    {
        var record = InspectionRecord.Create(sbuId, input.EquipmentCode,
            input.InspectedAtUtc.GetValueOrDefault(), input.Result, input.Remark);
        records.Add(record);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return record.Id;
    }
}
```

`Add` 只登记新增，Service 在用例末尾统一保存。现有 `PlatformIdentityDbContext` 与 `IUnitOfWork` 共用 scoped 实例，不另建一个 DbContext 或在同一实例上并行查询。

列表、分页总数、导出、详情和修改使用同一 SBU 范围。后续增加修改、删除时，仓储查找条件必须同时带记录 `Id` 和当前 `sbuId`，查不到统一返回不存在，再调用实体行为并保存；不能只按 Id 找到实体后直接操作。

## 7. 声明权限并增加 Controller

在 `Services/Identity/ZX.Platform.Identity.Application/Authorization/IdentityPermissions.cs` 的现有类中增加常量：

```csharp
public const string EquipmentInspectionRead = "equipment.inspection.read";
public const string EquipmentInspectionCreate = "equipment.inspection.create";
```

`All` 已通过反射收集常量，现有授权注册与启动初始化器会使用它们，不需要再维护一份手工列表。声明权限后重启 Identity；这只登记权限目录，不会自动给普通角色授权。

新增 `Services/Identity/ZX.Platform.Identity.Api/Controllers/InspectionRecordsController.cs`：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.InspectionRecords;
using ZX.Platform.Identity.Application.InspectionRecords.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireSbu]
public sealed class InspectionRecordsController(IInspectionRecordService records,
    CurrentSbuContext currentSbu, IApiMessageLocalizer messages) : ControllerBase
{
    [HttpGet("list")]
    [Authorize(Policy = IdentityPermissions.EquipmentInspectionRead)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] InspectionRecordQueryDto query, CancellationToken cancellationToken)
    {
        var data = await records.ListAsync(currentSbu.SbuId, query, cancellationToken);
        return Ok(new ApiResponse<InspectionRecordPageDto>
        {
            Code = 0, Message = messages["Inspection.ListLoaded"].Value, Data = data,
        });
    }

    [HttpPost("add")]
    [Authorize(Policy = IdentityPermissions.EquipmentInspectionCreate)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateInspectionRecordDto input, CancellationToken cancellationToken)
    {
        var id = await records.CreateAsync(currentSbu.SbuId, input, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<Guid>
        {
            Code = 0, Message = messages["Inspection.Created"].Value, Data = id,
        });
    }
}
```

这条链路同时包含登录校验、动作权限和 SBU 校验。`[RequireSbu]` 验证请求头 `X-SBU-ID` 中的 SBU 是否启用且已分配给当前用户，再由 `CurrentSbuContext.SbuId` 传入 Service；仓储继续按该值过滤。仅校验请求头，不在查询中限制 SBU，仍会越权。

Controller 只绑定输入、调用 Service 和返回现有 `ApiResponse`，不直接查 EF、不自行捕获全部异常并包装成功。

### 7.1 按需向 AI 开放只读查询

普通业务查询不自动全部开放给 AI。确认接口及其调用链只读、返回字段适合当前账号查看后，在需要开放的 GET 方法上加一个 `[AiQuery]`，并保留原登录、动作权限和适用的 `[RequireSbu]`：

```csharp
using ZX.Platform.Identity.Api.AI.Queries;

// 本文仍是待开发的设备点检示例；只展示在现有 ListAsync 上添加的属性。
[AiQuery]
[HttpGet("list")]
[Authorize(Policy = IdentityPermissions.EquipmentInspectionRead)]
public async Task<IActionResult> ListAsync(
    [FromQuery] InspectionRecordQueryDto query, CancellationToken cancellationToken)
```

只需给前面的查询方法加属性和 using，不再复制一份方法，也不修改 AI 工具的 `switch`、新增专用 AI Service 或另写接口参数说明。标记描述可省略；难以从源码理解时，可选写成 `[AiQuery("查询设备点检记录")]`。目录自动收集已注册端点、ApiExplorer 参数和源码位置，模型通过已有 Controller、Service/DTO 和界面词条理解用途。现有用户、角色、菜单、SBU、本人资料和一键盘点共 16 个受保护只读 GET 已接入，设备点检仍需按本文开发后才存在。

本例 `{list,total}` 响应使用普通标记即可。若原接口返回已确认的完整裸数组，可选声明 `ResultShape = AiQueryResultShape.CompleteList`；完整树使用 `CompleteTree`，保留 `children` 且拒绝平面列表筛选。未声明数组不会自动视为全量，树根数也不能当作全部节点数；这些声明只描述原接口的可见结果，不扩大权限和业务范围。

自动发现要求标记方法是受 `[Authorize]` 保护的单一 GET，参数可由目录识别。支持 query/path 标量和一维标量数组，查询 DTO 会展开成这些参数；复杂 body、header/service 绑定、匿名或同名歧义等签名不开放。新增、修改、删除和状态流转不加标记；标记不代替只读代码审查。

执行查询仍重新认证当前会话、执行原动作权限、MVC 参数绑定与校验，以及 `[RequireSbu]` 等过滤器。SBU 来自当前请求中由原过滤器验证的选择，不能通过模型参数指定任意 SBU。分页响应只是当前页，AI 不能据此宣称全局合计或最大值；结果容量限制也不能靠忽略分页或截断绕过。发布时源码资料仍由现有流程自动导出，详细机制和限制见 [基于项目源码的 AI 助手](ai-integration.md#新增查询如何向-ai-开放)。

## 8. 注册依赖并添加后端多语言

### 8.1 注册 Service 和 Repository

在 `Services/Identity/ZX.Platform.Identity.Api/Program.cs` 顶部引入 Application 的业务命名空间，Repository 命名空间已有引用：

```csharp
using ZX.Platform.Identity.Application.InspectionRecords;
```

在 `builder.Build()` 之前新增注册：

```csharp
builder.Services.AddScoped<IInspectionRecordRepository, InspectionRecordRepository>();
builder.Services.AddScoped<IInspectionRecordService, InspectionRecordService>();
```

### 8.2 给消息键添加中英文内容

Controller 中的 `messages["Inspection.Created"].Value` 不是直接返回这段英文。`messages` 是注入的 `IApiMessageLocalizer`；方括号里的字符串是资源键，`.Value` 是按当前请求语言找到的提示文本。

例如，同一个创建接口：

| 请求头 | 成功响应的 `message` |
| --- | --- |
| `Accept-Language: zh-CN` | `点检记录创建成功。` |
| `Accept-Language: en-US` | `Inspection record created.` |

本例需要三个键：查询成功、新增成功、实体规则失败。修改下面两个现有文件，在各自的 `</root>` **之前**追加 `<data>` 节点，保留文件原有内容和 resheader。

`Services/Identity/ZX.Platform.Identity.Api/Resources/Localization/CommonMessages.resx`（默认中文）：

```xml
<data name="Inspection.ListLoaded" xml:space="preserve">
  <value>点检记录查询成功。</value>
</data>
<data name="Inspection.Created" xml:space="preserve">
  <value>点检记录创建成功。</value>
</data>
<data name="Inspection.InvalidData" xml:space="preserve">
  <value>点检记录字段不符合业务规则。</value>
</data>
```

`Services/Identity/ZX.Platform.Identity.Api/Resources/Localization/CommonMessages.en-US.resx`（英文）：

```xml
<data name="Inspection.ListLoaded" xml:space="preserve">
  <value>Inspection records loaded.</value>
</data>
<data name="Inspection.Created" xml:space="preserve">
  <value>Inspection record created.</value>
</data>
<data name="Inspection.InvalidData" xml:space="preserve">
  <value>Inspection data violates business rules.</value>
</data>
```

也可以在 Visual Studio 的资源编辑器中添加：名称填消息键，值填对应语言的文案。两个文件的键必须完全相同且不重复，大小写与调用代码一致。直接改 XML 时，文案中的 `&`、`<` 等字符要按 XML 规则转义。

当前 `Localization/ApiMessageLocalizer.cs` 将 `Access.*`、`Validation.*`、`User.*`、`Sbu.*`、`Auth.*` 分到对应资源，其余键回退到 `CommonMessages`。所以本例的 `Inspection.*` 放到上面的文件即可，**不用新增 Localizer 类、修改分发或手写资源加载代码**。不要只新增 `InspectionMessages.resx` 就认为它会自动被使用。

### 8.3 在不同位置使用消息键

- Controller 成功响应使用 `messages["Inspection.Created"].Value`。
- Domain / Application 只传键，例如 `throw new DomainException("Inspection.InvalidData")`，由已有异常处理器翻译，不在领域层注入 Localizer。
- DTO 上的 `[Required]`、`[StringLength]` 等复用本文已有的 `Validation.*` 键，位于 `ValidationMessages.resx` 和 `ValidationMessages.en-US.resx`。增加新的校验键时，在这两个文件中补中英文；不要把校验文案只加到 CommonMessages。

若提示有参数，在资源中使用 `{0}`、`{1}`，调用时写 `messages["Inspection.SomeKey", value].Value`，两个语言版本使用相同参数约定。不要拼接已经翻译的半句话。

修改 `.resx` 后重新编译并重启 Identity。联调时分别发送 `zh-CN` 和 `en-US` 请求，检查查询、新增成功以及业务失败的提示。成功响应出现原始键（例如 `Inspection.Created`）通常表示键名或资源位置有误；异常提示退回通用内容时，检查后端缺少资源的日志。无有效语言时系统默认中文。

后端返回的 `message` 由前端原样显示；页面标题、字段标签、按钮、本地校验提示使用下一节的前端词条，两边分别维护。


## 9. 实现前端页面并接入菜单

以下路径均相对于前端仓库 `ZX.front.vue`。本节仍是开发示例，需要成员实际创建文件；本文不会自动生成页面。按顺序完成 API → 中英文词条 → 列表与新增弹窗 → 权限及组件登记 → 菜单配置。

| 文件 | 操作 |
| --- | --- |
| `src/api/equipment/inspection.ts` | 新增接口与输入、返回类型 |
| `src/locales/zh-CN/inspection.ts`、`src/locales/en-US/inspection.ts` | 新增中英文界面和校验词条 |
| `src/locales/zh-CN/index.ts`、`src/locales/en-US/index.ts` | 注册词条模块 |
| `src/views/equipment/inspection/index.vue` | 新增分页列表页 |
| `src/views/equipment/inspection/components/InspectionCreateDialog.vue` | 新增表单弹窗 |
| `src/api/identity/permissions.ts` | 追加权限常量 |
| `src/router/menuPages.ts` | 在统一注册表追加组件标识、选项词条和加载函数 |

### 9.1 新增 API 和类型

新建 `src/api/equipment/inspection.ts`：

```ts
import { request } from '@/utils/request'
import type { PageQuery, PageResult } from '@/api/types'

export type InspectionResult = 'pass' | 'fail'

export interface InspectionRecord {
  id: string
  equipmentCode: string
  inspectedAtUtc: string
  result: InspectionResult
  remark?: string | null
}

export interface CreateInspectionRecordInput {
  equipmentCode: string
  inspectedAtUtc: string
  result: InspectionResult
  remark: string | null
}

export const getInspectionRecords = (params: PageQuery) =>
  request<PageResult<InspectionRecord>>({
    url: '/identity/api/InspectionRecords/list',
    method: 'get',
    params,
    sbuScoped: true,
  })

export const createInspectionRecord = (data: CreateInspectionRecordInput) =>
  request<string, CreateInspectionRecordInput>({
    url: '/identity/api/InspectionRecords/add',
    method: 'post',
    data,
    sbuScoped: true,
  })
```

本例两个 API 都必须设置 `sbuScoped: true`。统一请求层读取当前所选 SBU 并写入 `X-SBU-ID`，没有可用 SBU 时拒绝发送，并在 SBU 已切换时丢弃旧请求响应。不要自己从 localStorage 取 Token、拼认证头或让表单提交 `SbuId`。

请求层基础地址已有 `/api`，API 方法只填 `/identity/api/...`；浏览器最终请求 `/api/identity/api/InspectionRecords/...`。`request<T>()` 返回完整 `ApiResponse<T>`，列表从 `response.data?.list` 和 `response.data?.total` 读取。

查询参数只有 `page`、`pageSize`，因此本例不添加设备编号或结果搜索框。实际要加筛选时，先贯通后端 Query DTO、Service、Repository 和分页总数查询，再增加前端筛选组件，不能只过滤当前页。

### 9.2 新增并注册前端中英文词条

新建 `src/locales/zh-CN/inspection.ts`：

```ts
export default {
  title: '设备点检',
  list: '点检记录',
  equipmentCode: '设备编号',
  inspectedAt: '点检时间',
  result: '点检结果',
  remark: '备注',
  pass: '合格',
  fail: '不合格',
  createTitle: '新增点检记录',
  create: '新增',
  empty: '暂无点检记录',
  noSbu: '请先选择已授权的 SBU；没有可选项时请联系管理员。',
  validation: {
    equipmentRequired: '请输入设备编号',
    equipmentLength: '设备编号不能超过 100 个字符',
    timeRequired: '请选择有效的点检时间',
    resultRequired: '请选择点检结果',
    remarkLength: '备注不能超过 500 个字符',
  },
}
```

新建 `src/locales/en-US/inspection.ts`，键结构与中文一致：

```ts
export default {
  title: 'Equipment inspection',
  list: 'Inspection records',
  equipmentCode: 'Equipment code',
  inspectedAt: 'Inspection time',
  result: 'Result',
  remark: 'Remark',
  pass: 'Pass',
  fail: 'Fail',
  createTitle: 'New inspection record',
  create: 'Create',
  empty: 'No inspection records',
  noSbu: 'Select an assigned SBU. Contact an administrator if none is available.',
  validation: {
    equipmentRequired: 'Enter an equipment code',
    equipmentLength: 'Equipment code cannot exceed 100 characters',
    timeRequired: 'Select a valid inspection time',
    resultRequired: 'Select a result',
    remarkLength: 'Remark cannot exceed 500 characters',
  },
}
```

然后**同时修改** `src/locales/zh-CN/index.ts` 和 `src/locales/en-US/index.ts`：在现有 import 中追加下面一行，并在现有 `export default { ... }` 对象内追加 `inspection,`，保留原有词条。

```ts
import inspection from './inspection'
```

新增文件不会自动注册。完成注册后，页面通过 `t('inspection.equipmentCode')` 取值。中文是当前前端的类型基准，英文必须包含同样的键结构；不要新增另一套 i18n 实例。

### 9.3 实现分页列表页

新建 `src/views/equipment/inspection/index.vue`：

```vue
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import {
  createInspectionRecord,
  getInspectionRecords,
  type CreateInspectionRecordInput,
  type InspectionRecord,
} from '@/api/equipment/inspection'
import { Permission } from '@/api/identity/permissions'
import DataTable from '@/components/DataTable/index.vue'
import type { DataTableColumn } from '@/components/DataTable/types'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '@/utils/format'
import InspectionCreateDialog from './components/InspectionCreateDialog.vue'

defineOptions({ name: 'EquipmentInspectionPage' })

const { t, locale } = useI18n()
const auth = useAuthStore()
const rows = ref<InspectionRecord[]>([])
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const createOpen = ref(false)

const load = async () => {
  if (!auth.currentSbuId) {
    rows.value = []
    total.value = 0
    return
  }
  try {
    const response = await getInspectionRecords({ page: page.value, pageSize: pageSize.value })
    rows.value = response.data?.list ?? []
    total.value = response.data?.total ?? 0
  } catch {
    // 统一请求层已处理错误提示；同一 SBU 刷新失败时保留当前结果。
  }
}

const create = async (input: CreateInspectionRecordInput) => {
  const { message } = await createInspectionRecord(input)
  ElMessage.success(message)
  page.value = 1
  await load()
}

const columns = computed<DataTableColumn<InspectionRecord>[]>(() => [
  { prop: 'equipmentCode', label: t('inspection.equipmentCode'), minWidth: 160 },
  {
    prop: 'inspectedAtUtc',
    label: t('inspection.inspectedAt'),
    minWidth: 180,
    formatter: (row) => formatDateTime(row.inspectedAtUtc, { locale: locale.value }),
  },
  {
    prop: 'result',
    label: t('inspection.result'),
    width: 120,
    formatter: (row) => t(row.result === 'pass' ? 'inspection.pass' : 'inspection.fail'),
  },
  {
    prop: 'remark',
    label: t('inspection.remark'),
    minWidth: 220,
    showOverflowTooltip: true,
    formatter: (row) => row.remark || '—',
  },
])

onMounted(load)
</script>

<template>
  <section class="inspection-page">
    <div class="surface-card inspection-page__list">
      <div class="app-table">
        <data-table
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :data="rows"
          :columns="columns"
          :total="total"
          row-key="id"
          storage-key="equipment-inspection"
          height="100%"
          pagination
          :addable="Boolean(auth.currentSbuId) && auth.hasPermission(Permission.EquipmentInspectionCreate)"
          :refreshable="Boolean(auth.currentSbuId)"
          @add="createOpen = true"
          @refresh="load"
          @pagination-change="load"
        >
          <template #toolbar>
            <div class="app-table__summary">
              <span class="app-table__title">{{ t('inspection.list') }}</span>
            </div>
          </template>
          <template #empty>
            <el-empty
              :image-size="64"
              :description="t(auth.currentSbuId ? 'inspection.empty' : 'inspection.noSbu')"
            />
          </template>
        </data-table>
      </div>
    </div>
    <inspection-create-dialog v-model="createOpen" :save="create" />
  </section>
</template>

<style scoped lang="scss">
.inspection-page {
  display: grid;
  grid-template-rows: minmax(0, 1fr);
  height: max(420px, calc(var(--app-page-available-height, 100dvh) - 28px));
  min-width: 0;
}

.inspection-page__list {
  min-width: 0;
  min-height: 0;
  padding: 14px;
}
</style>
```

页面直接复用 `DataTable`，列配置放本页，表单拆成独立弹窗，不再包装一层转发 DataTable 属性和事件的组件。格式化列使用 `formatter`；需要复杂内容时改用当前项目支持的 TSX `render`，模板组件名称仍使用 kebab-case。

本例没有修改、删除、批量和导出接口，因此不显示对应入口。请求默认已有全局 loading 和错误提示，页面不重复添加全屏 loading 或再次弹错误。新增成功只显示一次后端 `message`，不另写 `t('inspection.createSuccess')`。

普通列表只在 `onMounted` 首次加载，分页、新增后或用户刷新时再请求。当前 `LayoutContent.vue` 已按 `auth.currentSbuId` 重建内容缓存，切换 SBU 会创建新的页面实例并重新加载；请求层同时丢弃旧 SBU 响应。本例无需额外增加 SBU watcher 或 `onActivated` 自动刷新。

### 9.4 实现新增弹窗和本地表单校验

新建 `src/views/equipment/inspection/components/InspectionCreateDialog.vue`：

```vue
<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import type { CreateInspectionRecordInput, InspectionResult } from '@/api/equipment/inspection'

const open = defineModel<boolean>({ required: true })
const props = defineProps<{
  save: (input: CreateInspectionRecordInput) => Promise<void>
}>()
const { t } = useI18n()

type InspectionForm = {
  equipmentCode: string
  inspectedAt: Date | null
  result: InspectionResult
  remark: string
}
const blankForm = (): InspectionForm => ({
  equipmentCode: '',
  inspectedAt: new Date(),
  result: 'pass',
  remark: '',
})
const form = reactive(blankForm())
const formRef = ref<FormInstance>()
const rules = computed<FormRules<InspectionForm>>(() => ({
  equipmentCode: [
    { required: true, whitespace: true, message: t('inspection.validation.equipmentRequired'), trigger: 'blur' },
    { max: 100, message: t('inspection.validation.equipmentLength'), trigger: 'blur' },
  ],
  inspectedAt: [
    { type: 'date', required: true, message: t('inspection.validation.timeRequired'), trigger: 'change' },
  ],
  result: [
    { required: true, message: t('inspection.validation.resultRequired'), trigger: 'change' },
  ],
  remark: [
    { max: 500, message: t('inspection.validation.remarkLength'), trigger: 'blur' },
  ],
}))

watch(open, async (visible) => {
  if (!visible) return
  Object.assign(form, blankForm())
  await nextTick()
  formRef.value?.clearValidate()
})

const submit = async () => {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid || !form.inspectedAt || !Number.isFinite(form.inspectedAt.getTime())) return
  await props.save({
    equipmentCode: form.equipmentCode.trim(),
    inspectedAtUtc: form.inspectedAt.toISOString(),
    result: form.result,
    remark: form.remark.trim() || null,
  })
  open.value = false
}
</script>

<template>
  <el-dialog
    v-model="open"
    :title="t('inspection.createTitle')"
    width="600px"
    class="app-dialog"
    align-center
    append-to-body
    destroy-on-close
    :close-on-click-modal="false"
  >
    <el-form
      ref="formRef"
      :model="form"
      :rules="rules"
      class="app-form"
      label-position="top"
      :validate-on-rule-change="false"
      @submit.prevent="submit"
    >
      <el-form-item :label="t('inspection.equipmentCode')" prop="equipmentCode">
        <el-input v-model="form.equipmentCode" maxlength="100" />
      </el-form-item>
      <el-form-item :label="t('inspection.inspectedAt')" prop="inspectedAt">
        <el-date-picker v-model="form.inspectedAt" type="datetime" />
      </el-form-item>
      <el-form-item :label="t('inspection.result')" prop="result">
        <el-select v-model="form.result">
          <el-option value="pass" :label="t('inspection.pass')" />
          <el-option value="fail" :label="t('inspection.fail')" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('inspection.remark')" prop="remark">
        <el-input v-model="form.remark" type="textarea" maxlength="500" :rows="3" />
      </el-form-item>
      <button type="submit" hidden aria-hidden="true" tabindex="-1" />
    </el-form>
    <template #footer>
      <el-button @click="open = false">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" @click="submit">{{ t('inspection.create') }}</el-button>
    </template>
  </el-dialog>
</template>
```

日期选择器保留 `Date` 类型，不设置会把值变成无时区文本的 `value-format`；提交时用 `toISOString()` 转 UTC，列表用公共 `formatDateTime` 按用户电脑时区显示。可空备注传 `null`。保存失败时 `props.save` 抛错，弹窗保留，统一请求层显示错误。

页面、弹窗复用现有 `surface-card`、`app-table`、`app-dialog`、`app-form`。自定义样式只处理布局；新增颜色使用 `--app-*` / `--el-*` 变量，适配浅色、深色和深色侧栏，不自行增加阴影、边框或主题状态。

### 9.5 登记动作权限和页面组件

在 `src/api/identity/permissions.ts` 的**现有** `Permission` 对象内追加，与后端常量逐字一致：

```ts
EquipmentInspectionRead: 'equipment.inspection.read',
EquipmentInspectionCreate: 'equipment.inspection.create',
```

在 `src/router/menuPages.ts` 的**现有** `menuPages` 对象内追加：

```ts
EquipmentInspection: {
  titleKey: 'inspection.title',
  load: () => import('@/views/equipment/inspection/index.vue'),
},
```

页面通过 `auth.hasPermission(Permission.EquipmentInspectionCreate)` 控制新增入口；页面访问权限由动态菜单路由及守卫处理。不要仅隐藏菜单就认为后端有保护，也不要为这一个业务页再在 `src/router/index.ts` 添加绕过动态菜单的固定路由。

路由与菜单编辑器共用 `menuPages`，不再分别修改 `dynamic.ts` 或维护选项名称字典。`titleKey` 使用第 9.2 节注册的中英文词条，控制组件下拉选项名称；实际菜单及标签标题仍来自菜单配置。

后端 `Menus/MenuService.cs` 只校验页面组件标识格式，不追加页面名称：以 ASCII 字母开头，后续仅字母、数字、下划线，总长 1～100 字符。`EquipmentInspection`、`InventoryStocktake` 合法；`equipment/inspection`、`Equipment.Inspection`、`1Inspection` 不合法。菜单组件必须与前端注册表键按大小写精确匹配，目录和操作的组件留空。

直接调用菜单 API 可以保存格式合法但前端未登记的标识，前端不会为它注册页面路由；菜单编辑器只允许保存已登记的组件。先准备包含新页面的前端，再配置菜单。组件登记本身不改数据库结构，不需要 Migration；业务新增表或字段仍按第 4 节处理。

### 9.6 在界面创建菜单并授权

前后端代码完成、Identity 重启登记新权限后，由有菜单管理权限的账号在“菜单管理”创建：

| 节点 | 配置 |
| --- | --- |
| 目录 | 新建或选择“设备管理”，英文标题 `Equipment management`，`kind=directory`；不填路径、组件、权限 |
| 页面 | 中文标题“设备点检”，英文标题 `Equipment inspection`，`kind=page`；路径 `/equipment/inspection`，组件 `EquipmentInspection`，权限 `equipment.inspection.read`；按需开启页面缓存、菜单和标签显示 |
| 页面下的操作 | 中文标题“新增”，英文标题 `Create`，`kind=action`；权限 `equipment.inspection.create`；不填路径和组件 |

菜单和标签标题来自菜单管理中的中英文标题；页面内部的表头和表单文字来自 `inspection.ts`，二者分别配置。图标如需填写，使用项目已有 Lucide 标识，不另建图标体系。

业务菜单由界面创建，不写入 `DefaultMenus` 或数据库迁移。角色管理中勾选查看、新增权限，再给测试用户分配角色及启用的 SBU。测试账号刷新菜单或重新登录后进入页面，选择正确的 SBU 再新增和查询。

后端授权树会为页面的 read 权限自动补“查看页面”叶子，无需手工再创建同码查看操作。新增、审批等独立动作按实际权限创建 action 节点；菜单本身不等于角色授权。

仅测试库配置了菜单不代表其他环境也已配置；交付时记录菜单路径、组件标识、权限码、节点结构及中英文标题，由负责人安排目标环境配置。

### 9.7 前后端联调顺序

在前端仓库根目录还原已有依赖并启动开发服务；已还原且依赖未变时无需重复安装：

```bash
pnpm install --frozen-lockfile
pnpm dev
```

前端沿用 `VITE_API_BASE_URL=/api`；需要指定本机 Gateway 时，在不提交的 `.env.development.local` 中设置 `API_PROXY_TARGET=https://localhost:7178`，更改后重启开发服务。Identity 和 Gateway 的启动方式见第一份文档。

1. 后端迁移执行成功，新增业务代码完成依赖注册、资源配置并启动；前端接通第一份文档中的本机 Gateway。
2. 用非管理员账号确认菜单授权，进入设备点检页面，检查列表请求参数为 `page`、`pageSize`，请求头带当前 `X-SBU-ID`。
3. 新增一条记录，检查提交时间是 UTC ISO 字符串、备注可为 `null`、响应为 HTTP 201 与 `code=0`；成功提示直接显示后端 `message`，列表刷新。
4. 切换系统语言，分别确认表头、表单、本地校验、菜单标题及后端操作提示的中英文。前端会自动携带 `Accept-Language`。
5. 仅授予查看时新增入口不显示，直接调用新增接口仍被后端拒绝；切换 SBU 后列表和新增弹窗重新初始化，另一 SBU 的记录不出现。
6. 验证分页、空列表、无可用 SBU、接口失败时保留表单，以及三种主题下的显示；未执行的检查如实列入交付说明。


## 10. 联调与扩展

使用非管理员测试账号核对：

1. 只有查看权限时能分页查询，不能新增；直接请求新增接口也应被拒绝。
2. 有查看和新增权限时，可以在已分配且启用的 SBU 创建记录并查询。
3. 缺失、伪造或无权访问的 `X-SBU-ID` 被拒绝；另一 SBU 的记录不出现在列表及总数中。
4. 必填、长度、结果值、UTC 时间与可空备注符合约定；中英文消息正确。
5. 数据库包含本次迁移，菜单组件与前端 `menuPages` 的键精确匹配，路径和动作权限配置一致。

管理员能访问，不代表普通角色的功能权限和 SBU 数据范围已经验证。带上实际验证结果再交付，不把尚未执行的检查写成已通过。

实际模块需要修改、删除或状态流转时，先补对应的领域行为、Service 用例和独立动作权限；涉及并发更新时定义并发令牌和冲突处理。同一用例内的数据库变更通过统一事务边界提交，状态变更的业务审计要单独设计，不能把请求日志当作审批历史。

接入外部 MES 时，由 Infrastructure 按服务端配置的别名访问，查询参数化并应用业务对应的 SBU 条件；外部现有表不纳入 Identity 的迁移。跨系统调用另行明确失败重试与幂等规则，不认为一次本地数据库保存就能保证外部系统同时成功。

## 11. 交付本次业务

新增或改变业务行为时，同步 `docs/business/` 中对应模块文档；新模块补业务目录入口。文档范围、前后端分工及提交前核对见 [成员业务文档编写规范](03-documentation.md)，无需每次改写系统级文档。

业务完成后，将代码、必要的中英文资源及迁移文件一起提交到自己的业务分支，在 GitLab 发起合并请求，由负责人审核合并。不要直接推团队基线或自行发布。

交付说明写清四项：完成的功能与接口；已验证和未验证的场景；新增的菜单、权限、配置及迁移；遗留问题。迁移已在测试库执行时记录完整迁移 ID，不附带真实连接密码。菜单与权限的目标环境配置由负责人安排。
