# 业务模块开发模板

[开发者培训指南](developer-training.md) · [后端开发者指南](developer-guide.md) · [前端开发者指南](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/developer-guide.md)

这份文档是开发者接入生产、质量、设备等业务模块时的操作模板，重点说明“在哪个目录做什么”。示例使用“设备点检记录”，示例名称和代码不能直接当成已实现功能。

本文默认先把业务模块接入现有 Identity 服务，便于开发者熟悉当前仓库。业务规模扩大或需要独立部署时，再按最后一节拆成独立服务。

## 阅读顺序

1. 先看模块边界和目录落点。
2. 再按后端文件顺序实现实体、DTO、Repository、Service、Controller 和 Migration。
3. 再按前端文件顺序实现 API、页面、组件、权限和菜单。
4. 最后阅读完整代码示例，对照自己的模块逐文件替换名称和字段。

## 一、先确定模块边界

开始写代码前，先把下面的信息写进模块设计单：

| 项目 | 设备点检示例 | 必须确定的内容 |
| --- | --- | --- |
| 模块名称 | 设备点检 | 业务名称和菜单名称 |
| 数据归属 | 当前平台 Oracle | 哪个服务拥有表和写入权 |
| SBU 范围 | 每条记录属于一个 SBU | 是否必须按 SBU 隔离 |
| 页面 | 点检记录列表、创建、修改 | 页面路径和组件标识 |
| 权限 | 查看、新增、修改 | 权限编码和角色分配方式 |
| 外部数据 | 暂不读取 MES | 是否需要外部 Oracle 别名 |

不要先建菜单再倒推业务规则。先确认数据归属、权限和 SBU 范围，再建表和接口。

## 二、当前仓库中的落点

### 1. 在现有 Identity 服务内开发

适合平台基础能力或暂时和 Identity 一起部署的模块：

```text
ZX.Platform/
├── Services/Identity/
│   ├── ZX.Platform.Identity.Api/
│   │   └── Controllers/                         # HTTP 接口
│   ├── ZX.Platform.Identity.Application/
│   │   ├── Equipment/                           # 业务 DTO、Service、Repository 契约
│   │   └── Authorization/IdentityPermissions.cs # 权限目录
│   ├── ZX.Platform.Identity.Domain/
│   │   └── Entities/                            # 实体和状态规则
│   └── ZX.Platform.Identity.Infrastructure/
│       ├── Persistence/Configurations/          # EF/Oracle 映射
│       ├── Persistence/Repositories/            # Repository 实现
│       └── Persistence/Migrations/              # 数据库 Migration
└── ZX.Platform.Gateway/                         # 统一入口和转发
```

### 2. 前端落点

```text
ZX.front.vue/
├── src/api/equipment/                           # 接口方法和类型
├── src/views/equipment/                         # 页面和页面专用组件
├── src/router/dynamic.ts                        # 后端组件标识到 Vue 组件的映射
├── src/api/identity/permissions.ts              # 前端权限常量
├── src/locales/zh-CN、src/locales/en-US/       # 中英文文案
└── src/icons/svg/lucide/                        # 本地业务图标
```

页面业务状态放 `views`，接口方法放 `api`，不要在页面里创建 Axios 实例，也不要把业务查询结果长期放进全局 store。

## 三、后端按文件实施

下面按“设备点检记录”列出需要创建或修改的文件。

### 第 1 步：定义 Domain 实体

创建：

```text
Services/Identity/ZX.Platform.Identity.Domain/Entities/InspectionRecord.cs
```

实体至少包含：

```text
Id                  主键
SbuId               数据范围
EquipmentCode       设备编号
InspectedAtUtc      点检时间，UTC
Result              点检结果
Remark              备注
CreatedAtUtc        审计时间
IsDeleted           软删除标记
```

实体使用私有 `set`、私有构造函数和 `Create` 工厂。把“结果是否允许”“修改后状态如何变化”等只依赖自身字段的规则写成实体方法，不在 Controller 里直接修改字段。

### 第 2 步：定义 Application DTO、Service 和 Repository 契约

创建目录：

```text
Services/Identity/ZX.Platform.Identity.Application/Equipment/
├── Dtos/
│   ├── CreateInspectionRecordDto.cs
│   ├── UpdateInspectionRecordDto.cs
│   └── InspectionRecordDto.cs
├── IInspectionRecordService.cs
├── InspectionRecordService.cs
└── IInspectionRecordRepository.cs
```

这里分别做：

- DTO：定义接口输入和输出，不直接暴露 Domain 实体。
- Service：编排查询、创建、修改、SBU 校验和保存。
- Repository 接口：只描述业务需要的查询和持久化操作，不引用 EF Core 类型。

Service 方法统一接收 `CancellationToken`，写入流程通常是“查询实体 → 调用实体方法 → `SaveChangesAsync`”。不为了每个接口增加一层没有业务意义的包装。

### 第 3 步：增加权限声明

修改：

```text
Services/Identity/ZX.Platform.Identity.Application/Authorization/IdentityPermissions.cs
```

增加类似常量：

```csharp
public const string EquipmentInspectionRead = "equipment.inspection.read";
public const string EquipmentInspectionCreate = "equipment.inspection.create";
public const string EquipmentInspectionUpdate = "equipment.inspection.update";
```

权限目录会在 Identity 启动时同步到权限表。不要先在数据库手工插入一个没有代码声明的权限；角色授权只承认当前代码目录中的有效权限。

### 第 4 步：配置 Oracle 映射和 Repository

创建：

```text
Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations/InspectionRecordConfiguration.cs
Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Repositories/InspectionRecordRepository.cs
```

在 `PlatformIdentityDbContext` 注册 `DbSet<InspectionRecord>`，并在 `OnModelCreating` 或现有配置扫描规则中注册配置。

建议的表和索引：

```text
EQUIPMENT_INSPECTION_RECORD
  ID
  SBU_ID
  EQUIPMENT_CODE
  INSPECTED_AT_UTC
  RESULT
  REMARK
  CREATED_AT_UTC
  UPDATED_AT_UTC
  IS_DELETED

索引：SBU_ID、EQUIPMENT_CODE、INSPECTED_AT_UTC
```

表名、列名、长度、唯一性和软删除条件必须在 Configuration 中明确写出。不要依赖 Oracle 默认命名。

### 第 5 步：生成和检查 Migration

在后端仓库根目录执行：

```bash
dotnet ef migrations add AddInspectionRecords \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api
```

检查：

```bash
dotnet ef migrations list --no-connect \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api
```

本地和测试库先验证生成 SQL。正式库按[迁移操作手册](migration-operations.md)交给 DBA 或发布流程执行，不在正式库客户端临时修改表结构。

### 第 6 步：编写 Service 实现和 SBU 校验

在 `InspectionRecordService.cs` 中：

1. 列表查询必须带 `currentSbuId` 条件。
2. 查询详情、修改和删除前，必须检查记录的 `SbuId` 等于当前 SBU。
3. 新建记录时，`SbuId` 使用服务端已验证的当前 SBU，不能信任请求体里的 SBU。
4. 记录不存在、记录属于其他 SBU 或 SBU 已停用时，返回对应业务错误。
5. 外部 MES 查询也必须先经过同样的 SBU 范围判断。

如果接口放在当前 Identity API，可直接使用：

```csharp
[Authorize(Policy = IdentityPermissions.EquipmentInspectionRead)]
[RequireSbu]
```

Controller 从 `CurrentSbuContext` 取得服务端验证后的 SBU，再传给 Application Service。`RequireSbuAttribute` 的实现位置是：

```text
Services/Identity/ZX.Platform.Identity.Api/Authorization/RequireSbuAttribute.cs
```

### 第 7 步：编写 Controller 和注册依赖

创建：

```text
Services/Identity/ZX.Platform.Identity.Api/Controllers/InspectionRecordsController.cs
```

Controller 只做四件事：接收请求、绑定 DTO、调用 Service、返回 `ApiResponse<T>`。不要在 Controller 中写 EF 查询或拼接 SQL。

接口示例：

```text
GET  /api/InspectionRecords/list
POST /api/InspectionRecords/add
PUT  /api/InspectionRecords/update
```

每个动作使用对应的 `[Authorize(Policy = ...)]`。在 `Program.cs` 注册：

```csharp
builder.Services.AddScoped<IInspectionRecordRepository, InspectionRecordRepository>();
builder.Services.AddScoped<IInspectionRecordService, InspectionRecordService>();
```

同时补充：

- `Api/Resources/Localization/AccessMessages.resx`
- `Api/Resources/Localization/AccessMessages.en-US.resx`
- 校验和业务异常资源
- `docs/api.md` 中的请求字段、响应和错误码

## 四、前端按文件实施

### 第 1 步：定义 API 类型和请求方法

创建：

```text
ZX.front.vue/src/api/equipment/inspection.ts
```

文件中定义：

- `InspectionRecord` 行类型。
- `InspectionRecordQuery` 查询参数。
- `CreateInspectionRecordInput` 和 `UpdateInspectionRecordInput`。
- `getInspectionRecords`、`createInspectionRecord`、`updateInspectionRecord`。

所有请求使用：

```ts
import request from '@/utils/request'
```

不要在页面里直接调用 Axios，也不要手工处理 Token、401、全屏 loading 和统一错误提示。

### 第 2 步：创建页面和表单组件

创建：

```text
ZX.front.vue/src/views/equipment/inspection/index.vue
ZX.front.vue/src/views/equipment/inspection/components/InspectionEditor.vue
```

`index.vue` 负责：

- 查询条件和分页。
- 调用 API 和刷新列表。
- 打开新增/编辑弹窗。
- 根据权限显示操作入口。

`InspectionEditor.vue` 负责：

- 表单字段和本地校验。
- 接收编辑数据。
- 通过保存回调把 DTO 交给页面提交。

列表优先使用 `DataTable`。列配置使用 `computed`，文案使用 `t('equipmentInspection.xxx')`，不要把中文标题写死在列定义中。

### 第 3 步：添加页面组件映射

修改：

```text
ZX.front.vue/src/router/dynamic.ts
```

增加显式映射：

```ts
EquipmentInspection: () => import('@/views/equipment/inspection/index.vue'),
```

后端菜单的 `component` 必须填写 `EquipmentInspection`，不能填写任意文件路径。前端只加载 `dynamic.ts` 中登记过的组件。

同时修改后端 `MenuService` 的组件白名单，使 `EquipmentInspection` 能通过菜单校验。当前白名单位置：

```text
Services/Identity/ZX.Platform.Identity.Application/Menus/MenuService.cs
```

### 第 4 步：添加前端权限和文案

修改或创建：

```text
ZX.front.vue/src/api/identity/permissions.ts
ZX.front.vue/src/locales/zh-CN/equipmentInspection.ts
ZX.front.vue/src/locales/en-US/equipmentInspection.ts
```

前端权限常量必须和后端权限编码一致。页面按钮可以这样判断：

```ts
const auth = useAuthStore()
const canCreate = computed(() => auth.hasPermission(Permission.EquipmentInspectionCreate))
```

这只负责隐藏按钮，后端 Controller 仍必须独立检查权限。

### 第 5 步：配置菜单

在“菜单管理”中新增：

1. 目录：例如“设备管理”。
2. 页面：例如“设备点检”，类型为 `page`。
3. 页面路径：`/equipment/inspection`。
4. 组件标识：`EquipmentInspection`。
5. 查看权限：`equipment.inspection.read`。
6. 页面操作节点：新增、修改等权限。

页面菜单和操作菜单的权限都要分配给相应角色。只建菜单、不分配角色权限时，普通用户仍然看不到入口。

## 五、外部数据源的接入方式

业务模块需要 MES Oracle 时：

1. 在后端部署环境或 User Secrets 配置 `ExternalOracle:Sources:<Alias>`。
2. Application 只依赖 `IExternalOracleConnectionFactory` 接口。
3. Infrastructure 使用别名打开连接，并在操作结束时 `await using` 释放。
4. 别名由服务端代码决定，不能从 HTTP 参数直接传入连接工厂。
5. 外部表不加入 Identity 的 EF Migration。
6. 查询结果仍按当前 SBU 做业务过滤。

具体连接生命周期见[外部 Oracle 数据源](external-oracle-data-sources.md)。

## 六、验收清单

### 后端

- [ ] 实体、DTO、Service、Repository 和 Controller 分层正确。
- [ ] 所有接口都有登录和功能权限检查。
- [ ] 需要 SBU 的接口都验证了 `X-SBU-ID` 和记录归属。
- [ ] 权限常量、资源文件、菜单操作节点一致。
- [ ] Migration 只包含预期表、列、索引和数据变更。
- [ ] 没有把真实连接字符串、密码或管理员 SQL 提交到 Git。

### 前端

- [ ] API 方法统一使用 `@/utils/request`。
- [ ] 页面和表单职责分开，列表使用 `DataTable`。
- [ ] 动态组件映射、后端组件白名单和菜单组件标识一致。
- [ ] 页面权限和操作权限分别配置。
- [ ] 中文、英文、Light、Dark、Dark Sidebar 都能使用。
- [ ] 没有权限时按钮隐藏，接口拒绝时页面不会误报成功。

### 联调

- [ ] 管理员可以完整操作。
- [ ] 普通账号只能看到被授权菜单。
- [ ] 不同 SBU 账号不能互相读取或修改数据。
- [ ] 直接输入页面地址仍经过前端路由和后端接口校验。
- [ ] 修改角色、菜单或 SBU 后刷新权限，路由、标签和缓存正确更新。

## 七、独立业务服务什么时候拆分

以下情况再拆成独立的 Api、Application、Domain、Infrastructure：

- 业务有独立数据库和独立发布节奏。
- 业务接口数量、数据量或故障影响明显扩大。
- 需要独立扩容、权限边界或团队维护。

拆分后仍要通过 Gateway 暴露接口，不能让前端绕过 Gateway。独立服务不能直接引用 Identity API 层的 `RequireSbuAttribute`，需要按平台统一的认证和 SBU 校验契约实现自己的服务端校验；在平台确认共享组件前，不要自行复制一套互不兼容的权限目录。

## 八、提交前命令

后端仓库根目录：

```bash
dotnet build ZX.Platform.slnx
git diff --check
```

前端仓库根目录：

```bash
pnpm type-check
pnpm exec vitest run
pnpm build-only
git diff --check
```

只提交本次模块相关文件，检查 `git status` 和 `git diff --stat`，确认没有把构建产物、图片、连接配置或其他模块的改动一起提交。

## 九、完整代码示例

这一节把“设备点检记录”按当前项目风格写成一套代码骨架。它是培训示例，路径和字段可以复制后再按实际业务调整；由于数据库表和权限尚未创建，不能直接当成已完成业务运行。

### 1. Domain 实体

文件：`Services/Identity/ZX.Platform.Identity.Domain/Entities/InspectionRecord.cs`

```csharp
using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Domain.Entities;

public sealed class InspectionRecord : SoftDeletableEntity
{
    private InspectionRecord() { }

    private InspectionRecord(
        Guid id,
        Guid sbuId,
        string equipmentCode,
        DateTimeOffset inspectedAtUtc,
        string result,
        string remark)
    {
        Id = id;
        SbuId = sbuId;
        EquipmentCode = equipmentCode;
        InspectedAtUtc = inspectedAtUtc.ToUniversalTime();
        Result = result;
        Remark = remark;
    }

    public Guid Id { get; private set; }
    public Guid SbuId { get; private set; }
    public string EquipmentCode { get; private set; } = string.Empty;
    public DateTimeOffset InspectedAtUtc { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string Remark { get; private set; } = string.Empty;

    public static InspectionRecord Create(
        Guid sbuId,
        string equipmentCode,
        DateTimeOffset inspectedAtUtc,
        string result,
        string remark)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sbuId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        return new InspectionRecord(
            Guid.NewGuid(),
            sbuId,
            equipmentCode.Trim(),
            inspectedAtUtc,
            result.Trim(),
            remark.Trim());
    }

    public void Update(
        string equipmentCode,
        DateTimeOffset inspectedAtUtc,
        string result,
        string remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        EquipmentCode = equipmentCode.Trim();
        InspectedAtUtc = inspectedAtUtc.ToUniversalTime();
        Result = result.Trim();
        Remark = remark.Trim();
    }

    protected override void OnDeleted() { }
    protected override void OnRestored() { }
}
```

这里没有 `SbuId` 的修改方法，因为记录创建后不能被普通编辑接口跨 SBU 移动。需要调整归属时，应设计单独的管理员用例和审计规则。

### 2. DTO

目录：`Services/Identity/ZX.Platform.Identity.Application/Equipment/Dtos/`

`CreateInspectionRecordDto.cs`：

```csharp
using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Equipment.Dtos;

public sealed class CreateInspectionRecordDto
{
    [Required(ErrorMessage = "Validation.EquipmentCodeRequired")]
    [MaxLength(100, ErrorMessage = "Validation.EquipmentCodeMaxLength")]
    public string EquipmentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.InspectedAtRequired")]
    public DateTimeOffset InspectedAtUtc { get; set; }

    [Required(ErrorMessage = "Validation.InspectionResultRequired")]
    [MaxLength(30, ErrorMessage = "Validation.InspectionResultMaxLength")]
    public string Result { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Validation.InspectionRemarkMaxLength")]
    public string Remark { get; set; } = string.Empty;
}
```

`UpdateInspectionRecordDto.cs`：

```csharp
using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Equipment.Dtos;

public sealed class UpdateInspectionRecordDto
{
    [Required]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Validation.EquipmentCodeRequired")]
    [MaxLength(100, ErrorMessage = "Validation.EquipmentCodeMaxLength")]
    public string EquipmentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.InspectedAtRequired")]
    public DateTimeOffset InspectedAtUtc { get; set; }

    [Required(ErrorMessage = "Validation.InspectionResultRequired")]
    [MaxLength(30, ErrorMessage = "Validation.InspectionResultMaxLength")]
    public string Result { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Validation.InspectionRemarkMaxLength")]
    public string Remark { get; set; } = string.Empty;
}
```

`InspectionRecordQueryDto.cs` 和 `InspectionRecordDto.cs`：

```csharp
using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Equipment.Dtos;

public sealed class InspectionRecordQueryDto
{
    [MaxLength(100)]
    public string? EquipmentCode { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
}

public sealed class InspectionRecordDto
{
    public Guid Id { get; init; }
    public Guid SbuId { get; init; }
    public string EquipmentCode { get; init; } = string.Empty;
    public DateTimeOffset InspectedAtUtc { get; init; }
    public string Result { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
}
```

请求 DTO 不接收 `SbuId`。SBU 来自 `[RequireSbu]` 校验后的 `CurrentSbuContext`，避免客户端伪造范围。

### 3. Repository

接口文件：`Services/Identity/ZX.Platform.Identity.Application/Equipment/IInspectionRecordRepository.cs`

```csharp
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Equipment;

public interface IInspectionRecordRepository
{
    Task<List<InspectionRecord>> ListAsync(
        Guid sbuId,
        string? equipmentCode,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken);

    Task<InspectionRecord?> GetAsync(
        Guid id,
        Guid sbuId,
        CancellationToken cancellationToken);

    void Add(InspectionRecord record);
}
```

实现文件：`Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Repositories/InspectionRecordRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Equipment;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

public sealed class InspectionRecordRepository(PlatformIdentityDbContext db)
    : IInspectionRecordRepository
{
    public async Task<List<InspectionRecord>> ListAsync(
        Guid sbuId,
        string? equipmentCode,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var query = db.InspectionRecords
            .AsNoTracking()
            .Where(item => item.SbuId == sbuId);

        if (!string.IsNullOrWhiteSpace(equipmentCode))
            query = query.Where(item => item.EquipmentCode == equipmentCode.Trim());
        if (fromUtc is not null)
            query = query.Where(item => item.InspectedAtUtc >= fromUtc.Value.ToUniversalTime());
        if (toUtc is not null)
            query = query.Where(item => item.InspectedAtUtc <= toUtc.Value.ToUniversalTime());

        return await query
            .OrderByDescending(item => item.InspectedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<InspectionRecord?> GetAsync(
        Guid id,
        Guid sbuId,
        CancellationToken cancellationToken) =>
        db.InspectionRecords.FirstOrDefaultAsync(
            item => item.Id == id && item.SbuId == sbuId,
            cancellationToken);

    public void Add(InspectionRecord record) => db.InspectionRecords.Add(record);
}
```

Repository 的每个查询都带 `sbuId`，这样 Service 少传一次条件也不会意外查出其他 SBU 的记录。

### 4. EF 配置和 DbContext

配置文件：`Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations/InspectionRecordConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class InspectionRecordConfiguration
    : IEntityTypeConfiguration<InspectionRecord>
{
    public void Configure(EntityTypeBuilder<InspectionRecord> builder)
    {
        builder.ToTable("ZX_IDENTITY_INSPECTION_RECORD");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
        builder.Property(item => item.SbuId).HasColumnName("SBU_ID").IsRequired();
        builder.Property(item => item.EquipmentCode)
            .HasColumnName("EQUIPMENT_CODE").HasMaxLength(100).IsRequired();
        builder.Property(item => item.InspectedAtUtc)
            .HasColumnName("INSPECTED_AT_UTC").IsRequired();
        builder.Property(item => item.Result)
            .HasColumnName("RESULT").HasMaxLength(30).IsRequired();
        builder.Property(item => item.Remark)
            .HasColumnName("REMARK").HasMaxLength(500).IsRequired(false);
        builder.HasIndex(item => new
        {
            item.SbuId,
            item.EquipmentCode,
            item.InspectedAtUtc,
        });
        builder.ConfigureAuditing();
        builder.ConfigureSoftDelete();
    }
}
```

在 `PlatformIdentityDbContext.cs` 增加：

```csharp
public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
```

`ApplyConfigurationsFromAssembly` 会自动发现上面的配置，不需要在 `OnModelCreating` 里逐个手工注册。

### 5. Application Service

接口：`Services/Identity/ZX.Platform.Identity.Application/Equipment/IInspectionRecordService.cs`

```csharp
using ZX.Platform.Identity.Application.Equipment.Dtos;

namespace ZX.Platform.Identity.Application.Equipment;

public interface IInspectionRecordService
{
    Task<List<InspectionRecordDto>> ListAsync(
        InspectionRecordQueryDto query,
        Guid sbuId,
        CancellationToken cancellationToken);

    Task<Guid> CreateAsync(
        CreateInspectionRecordDto input,
        Guid sbuId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        UpdateInspectionRecordDto input,
        Guid sbuId,
        CancellationToken cancellationToken);
}
```

实现：`Services/Identity/ZX.Platform.Identity.Application/Equipment/InspectionRecordService.cs`

```csharp
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Equipment.Dtos;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Equipment;

public sealed class InspectionRecordService(
    IInspectionRecordRepository records,
    IUnitOfWork unitOfWork) : IInspectionRecordService
{
    public async Task<List<InspectionRecordDto>> ListAsync(
        InspectionRecordQueryDto query,
        Guid sbuId,
        CancellationToken cancellationToken)
    {
        var data = await records.ListAsync(
            sbuId,
            query.EquipmentCode,
            query.FromUtc,
            query.ToUtc,
            cancellationToken);
        return data.Select(ToDto).ToList();
    }

    public async Task<Guid> CreateAsync(
        CreateInspectionRecordDto input,
        Guid sbuId,
        CancellationToken cancellationToken)
    {
        var record = InspectionRecord.Create(
            sbuId,
            input.EquipmentCode,
            input.InspectedAtUtc,
            input.Result,
            input.Remark);
        records.Add(record);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    public async Task UpdateAsync(
        UpdateInspectionRecordDto input,
        Guid sbuId,
        CancellationToken cancellationToken)
    {
        var record = await records.GetAsync(input.Id, sbuId, cancellationToken);
        if (record is null)
            throw new BusinessException(ErrorCodes.NotFound, "Equipment.InspectionNotFound");

        record.Update(
            input.EquipmentCode,
            input.InspectedAtUtc,
            input.Result,
            input.Remark);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static InspectionRecordDto ToDto(InspectionRecord record) => new()
    {
        Id = record.Id,
        SbuId = record.SbuId,
        EquipmentCode = record.EquipmentCode,
        InspectedAtUtc = record.InspectedAtUtc,
        Result = record.Result,
        Remark = record.Remark,
    };
}
```

### 6. `InspectionRecordsController`

文件：`Services/Identity/ZX.Platform.Identity.Api/Controllers/InspectionRecordsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Equipment;
using ZX.Platform.Identity.Application.Equipment.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class InspectionRecordsController(
    IInspectionRecordService records,
    CurrentSbuContext currentSbu,
    IApiMessageLocalizer messages) : ControllerBase
{
    [HttpGet("list")]
    [Authorize(Policy = IdentityPermissions.EquipmentInspectionRead)]
    [RequireSbu]
    public async Task<IActionResult> ListAsync(
        [FromQuery] InspectionRecordQueryDto query,
        CancellationToken cancellationToken)
    {
        var data = await records.ListAsync(query, currentSbu.SbuId, cancellationToken);
        return Ok(new ApiResponse<List<InspectionRecordDto>>
        {
            Code = 0,
            Message = messages["Equipment.InspectionListLoaded"].Value,
            Data = data,
        });
    }

    [HttpPost("add")]
    [Authorize(Policy = IdentityPermissions.EquipmentInspectionCreate)]
    [RequireSbu]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateInspectionRecordDto input,
        CancellationToken cancellationToken)
    {
        var id = await records.CreateAsync(input, currentSbu.SbuId, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<Guid>
            {
                Code = 0,
                Message = messages["Equipment.InspectionCreated"].Value,
                Data = id,
            });
    }

    [HttpPut("update")]
    [Authorize(Policy = IdentityPermissions.EquipmentInspectionUpdate)]
    [RequireSbu]
    public async Task<IActionResult> UpdateAsync(
        [FromBody] UpdateInspectionRecordDto input,
        CancellationToken cancellationToken)
    {
        await records.UpdateAsync(input, currentSbu.SbuId, cancellationToken);
        return Ok(new ApiResponse
        {
            Code = 0,
            Message = messages["Equipment.InspectionUpdated"].Value,
        });
    }
}
```

这个 Controller 的关键点：

- `[Authorize]` 要求登录。
- Policy 判断功能权限。
- `[RequireSbu]` 校验 `X-SBU-ID` 是否属于当前用户。
- `CurrentSbuContext.SbuId` 传给 Service，不能从请求体取 SBU。
- Controller 不写 SQL、不访问 `DbContext`。

在 `Program.cs` 注册：

```csharp
builder.Services.AddScoped<IInspectionRecordRepository, InspectionRecordRepository>();
builder.Services.AddScoped<IInspectionRecordService, InspectionRecordService>();
```

同时在 `MenuService.Validate` 的组件白名单中加入 `EquipmentInspection`，否则菜单管理保存页面时会拒绝未知组件。

### 7. 前端 API 文件

文件：`ZX.front.vue/src/api/equipment/inspection.ts`

```ts
import { request, type RequestOptions } from '@/utils/request'

export interface InspectionRecord {
  id: string
  sbuId: string
  equipmentCode: string
  inspectedAtUtc: string
  result: string
  remark: string
}

export interface InspectionRecordQuery {
  equipmentCode?: string
  fromUtc?: string
  toUtc?: string
}

export interface CreateInspectionRecordInput {
  equipmentCode: string
  inspectedAtUtc: string
  result: string
  remark: string
}

export interface UpdateInspectionRecordInput extends CreateInspectionRecordInput {
  id: string
}

type Options = Pick<RequestOptions, 'signal' | 'showError' | 'loading'>

export const getInspectionRecords = (
  params: InspectionRecordQuery = {},
  options: Options = {},
) =>
  request<InspectionRecord[]>({
    ...options,
    url: '/identity/api/InspectionRecords/list',
    method: 'get',
    params,
  })

export const createInspectionRecord = (
  data: CreateInspectionRecordInput,
  options: Options = {},
) =>
  request<string, CreateInspectionRecordInput>({
    ...options,
    url: '/identity/api/InspectionRecords/add',
    method: 'post',
    data,
  })

export const updateInspectionRecord = (
  data: UpdateInspectionRecordInput,
  options: Options = {},
) =>
  request<void, UpdateInspectionRecordInput>({
    ...options,
    url: '/identity/api/InspectionRecords/update',
    method: 'put',
    data,
  })
```

当前请求层会从登录状态自动加入 `X-SBU-ID`。API 文件不要自己从 localStorage 读取 Token 或拼接这个请求头。

### 8. 前端权限常量和动态组件

修改 `ZX.front.vue/src/api/identity/permissions.ts`：

```ts
export const Permission = {
  // 现有权限保留
  EquipmentInspectionRead: 'equipment.inspection.read',
  EquipmentInspectionCreate: 'equipment.inspection.create',
  EquipmentInspectionUpdate: 'equipment.inspection.update',
} as const
```

修改 `ZX.front.vue/src/router/dynamic.ts`：

```ts
export const menuComponents = {
  SystemUser: () => import('@/views/system/user/index.vue'),
  SystemSbu: () => import('@/views/system/sbu/index.vue'),
  SystemRole: () => import('@/views/system/role/index.vue'),
  SystemMenu: () => import('@/views/system/menu/index.vue'),
  EquipmentInspection: () =>
    import('@/views/equipment/inspection/index.vue'),
}
```

前端映射名、后端菜单 `component` 字段和后端组件白名单必须完全一致。

### 9. 前端页面

文件：`ZX.front.vue/src/views/equipment/inspection/index.vue`

下面是一个最小可运行风格的列表页骨架，实际项目还要补充筛选表单和编辑弹窗：

```vue
<script setup lang="tsx">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import DataTable from '@/components/DataTable/index.vue'
import type { DataTableColumn } from '@/components/DataTable/types'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/api/identity/permissions'
import {
  createInspectionRecord,
  getInspectionRecords,
  type InspectionRecord,
} from '@/api/equipment/inspection'

const { t } = useI18n()
const auth = useAuthStore()
const rows = ref<InspectionRecord[]>([])
const loading = ref(false)

const load = async () => {
  loading.value = true
  try {
    const response = await getInspectionRecords()
    rows.value = response.data ?? []
  } finally {
    loading.value = false
  }
}

const create = async () => {
  const response = await createInspectionRecord({
    equipmentCode: 'DEMO-001',
    inspectedAtUtc: new Date().toISOString(),
    result: 'pass',
    remark: '',
  })
  ElMessage.success(response.message)
  await load()
}

const columns = computed<DataTableColumn<InspectionRecord>[]>(() => [
  { prop: 'equipmentCode', label: t('equipmentInspection.equipmentCode'), minWidth: 160 },
  { prop: 'inspectedAtUtc', label: t('equipmentInspection.inspectedAt'), minWidth: 180 },
  { prop: 'result', label: t('equipmentInspection.result'), minWidth: 120 },
  { prop: 'remark', label: t('equipmentInspection.remark'), minWidth: 240 },
])

onMounted(load)
</script>

<template>
  <section class="equipment-inspection">
    <div class="surface-card">
      <data-table
        :data="rows"
        :columns="columns"
        row-key="id"
        storage-key="equipment-inspection"
        :loading="loading"
        :addable="auth.hasPermission(Permission.EquipmentInspectionCreate)"
        refreshable
        @add="create"
        @refresh="load"
      />
    </div>
  </section>
</template>
```

上面的 `create` 只是为了展示调用链，正式页面应打开 `InspectionEditor.vue` 收集用户输入，不能使用固定的 `DEMO-001`。页面按钮权限只控制入口，接口仍由后端 Policy 最终判断。

### 10. 表单组件

文件：`ZX.front.vue/src/views/equipment/inspection/components/InspectionEditor.vue`

表单组件只负责收集字段和校验，通过 `save` 回调把数据交给页面：

```vue
<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import type { FormInstance } from 'element-plus'
import type { CreateInspectionRecordInput } from '@/api/equipment/inspection'

const open = defineModel<boolean>({ required: true })
const props = defineProps<{
  save: (input: CreateInspectionRecordInput) => void | Promise<void>
}>()

const formRef = ref<FormInstance>()
const form = reactive<CreateInspectionRecordInput>({
  equipmentCode: '',
  inspectedAtUtc: new Date().toISOString(),
  result: 'pass',
  remark: '',
})

watch(open, (visible) => {
  if (visible) {
    form.equipmentCode = ''
    form.inspectedAtUtc = new Date().toISOString()
    form.result = 'pass'
    form.remark = ''
  }
})

const submit = async () => {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  await props.save({ ...form, equipmentCode: form.equipmentCode.trim() })
  open.value = false
}
</script>

<template>
  <el-dialog v-model="open" title="新增设备点检" width="520px" destroy-on-close>
    <el-form ref="formRef" :model="form" label-position="top">
      <el-form-item label="设备编号" prop="equipmentCode">
        <el-input v-model="form.equipmentCode" maxlength="100" />
      </el-form-item>
      <el-form-item label="点检结果" prop="result">
        <el-select v-model="form.result">
          <el-option label="通过" value="pass" />
          <el-option label="异常" value="fail" />
        </el-select>
      </el-form-item>
      <el-form-item label="备注" prop="remark">
        <el-input v-model="form.remark" type="textarea" maxlength="500" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="open = false">取消</el-button>
      <el-button type="primary" @click="submit">保存</el-button>
    </template>
  </el-dialog>
</template>
```

正式代码要把中文文字替换为 `equipmentInspection` 词条，并按现有主题公共类补充样式。

### 11. 菜单配置后的完整访问链路

代码完成并部署后，在菜单管理中配置：

```text
目录：设备管理
  └── 页面：设备点检
      code：由后端创建时生成
      kind：page
      path：/equipment/inspection
      component：EquipmentInspection
      permission：equipment.inspection.read
      操作：equipment.inspection.create
            equipment.inspection.update
```

用户登录后：

```text
Auth/me 返回菜单
  → 前端检查组件映射和查看权限
  → 动态注册 /equipment/inspection
  → 页面请求 /InspectionRecords/list
  → Gateway 转发到 Identity
  → Policy 检查功能权限
  → RequireSbu 检查用户与 SBU 关系
  → Repository 按 SbuId 查询
  → 页面展示当前 SBU 数据
```

这条链路中任何一层缺失，都会出现对应问题：菜单不显示、路由无法注册、接口 403、SBU 校验失败或数据范围错误。排查时按这条链路从前往后检查。
