# 数据库与持久化

[文档导航](README.md) · [开发者指南](developer-guide.md)

Identity 使用 EF Core 10 与 SQL Server。数据库由本服务拥有；其他业务服务通过接口或集成事件协作，不直接读写 Identity 的表。

## 模型与表

| 实体          | 表                         | 主键 / 唯一性                                 | 生命周期           |
| ------------- | -------------------------- | --------------------------------------------- | ------------------ |
| `User`        | `ZX_IDENTITY_USER`         | 应用生成 `Guid`；`NormalizedAccount` 全表唯一 | 软删除与恢复       |
| `Factory`     | `ZX_IDENTITY_FACTORY`      | 应用生成 `Guid`；`Code` 全表唯一              | 软删除与恢复       |
| `UserFactory` | `ZX_IDENTITY_USER_FACTORY` | `UserId + FactoryId` 联合主键                 | 新增或物理删除关系 |
| `Role` | `ZX_IDENTITY_ROLE` | Guid；`Code` 全表唯一 | 软删除；内置角色只读 |
| `Permission` | `ZX_IDENTITY_PERMISSION` | `Code` 主键 | 启动同步，移除定义后停用 |
| `UserRole` | `ZX_IDENTITY_USER_ROLE` | `UserId + RoleId` 联合主键 | 新增或物理删除关系 |
| `RolePermission` | `ZX_IDENTITY_ROLE_PERMISSION` | `RoleId + PermissionCode` 联合主键 | 新增或物理删除关系 |
| `Menu` | `ZX_IDENTITY_MENU` | Guid；Code 全表唯一；未删除页面 Path、操作 PermissionCode 分别唯一 | 软删除；内置节点仅维护名称/图标/排序 |

`UserFactory` 表示用户和厂区的多对多关系，包含 `IsDefault` 和审计字段，**没有 `IsEnabled` 或软删除字段**。默认厂区的实体方法已经存在，当前接口创建关系时均使用 `IsDefault=false`，尚未建立设置默认厂区的用例。

当前不用实体导航属性和数据库物理外键。主键 / 唯一索引仍由数据库保证；对象是否存在及关系是否允许建立，由 Application 校验。关联查询显式写出筛选、`Any` 或按用户批量取关系，避免隐式 Include。

源码入口：[Entities](../Services/Identity/ZX.Platform.Identity.Domain/Entities)、[Configurations](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations)。

## 账号与厂区编码

用户创建时对账号 `Trim()`，`NormalizedAccount` 使用 `ToUpperInvariant()`；账号唯一性按标准化值判断。厂区编码 `Trim()` 后转为大写。

两种唯一索引都没有按 `IsDeleted` 过滤：软删除后原账号和厂区编码仍被保留，不能用同一标识建立新的主数据。创建前的预查询使用 `IgnoreQueryFilters()`，因此能给出“已删除 / 已禁用”的友好冲突提示。

数据库映射允许账号最长 100、厂区编码最长 50，但当前创建用户 DTO 将账号限制为最多 8 个字符；接口允许值以 DTO 为准，不能从列长推断 API 约束。请求字段详见[Identity 接口契约](api.md#用户请求字段)。

## 软删除与状态

`User`、`Factory`、`Role` 和 `Menu` 继承 `SoftDeletableEntity`。`MarkDeleted()` 设置 `IsDeleted=true`，各实体同时设 `IsEnabled=false`。用户和厂区提供恢复接口；角色和菜单当前不提供恢复。角色、菜单及授权关联也有审计字段；权限目录仅保存编码和活跃状态，不继承审计实体。角色关系与缓存失效见[角色与权限](authorization.md)。

普通查询默认使用 `!IsDeleted` 的全局过滤器，包括详情、分页和启用厂区查询。恢复用例通过专用查询包含已删除数据；普通启用接口不会绕过过滤器取得已删除对象。

删除或停用主数据时，现有用户厂区关系不会自动删除。恢复主数据后这些关系仍在。关系列表本身不联查厂区状态，因此用户 DTO 的 `factoryIds` 可能包含后来停用或删除的厂区；读取关系不等于验证当前访问权限。

`IgnoreQueryFilters()` 只在恢复、唯一性判断等明确场景使用，避免普通列表意外显示删除数据。状态接口和恢复时的 HTTP 行为见[Identity 业务语义](identity.md#状态与厂区关系)。

## 审计字段

`AuditableEntity` 提供：

| 字段              | 当前写入方式                                       |
| ----------------- | -------------------------------------------------- |
| `CreatedAtUtc`    | 新增时由拦截器使用 `TimeProvider.GetUtcNow()` 填写 |
| `UpdatedAtUtc`    | 修改时由拦截器填写                                 |
| `CreatedByUserId` | 字段已存在，当前没有自动填写登录用户               |
| `UpdatedByUserId` | 字段已存在，当前没有自动填写登录用户               |

拦截器在修改时保护创建时间和创建人，不让本次更新覆盖原值。时间统一使用 `DateTimeOffset` 和 UTC 存储；接口按 ISO 8601 保留 `Z` / 偏移量输出，约定见[API 响应与错误](http.md#时间字段格式)。

审计时间不是操作日志或数据变更历史。谁修改了哪些字段、前后值和原因，需要后续独立的业务审计方案。

源码：[AuditableEntitySaveChangesInterceptor.cs](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Interceptors/AuditableEntitySaveChangesInterceptor.cs)。

## 运维查询时间

当前审计时间统一保存 UTC（`+00:00`）。运维直接查询原表，使用常用 SQL 模板按当地时间输入条件、显示结果，无需建立额外视图或人工加减小时。下面以北京时间 `2026-10-13` 全天为例：起点是当天零点，终点是次日零点，使用 `>=` / `<` 包含完整一天，避免小数秒精度造成遗漏。

### SQL Server

时间列使用 `datetimeoffset`，对应 .NET `DateTimeOffset`。带 `+08:00` 的北京时间与带 `+00:00` 的 UTC 可以直接比较，数据库按实际时刻比较。例如 `2026-10-13 11:09:30 +08:00` 和 `2026-10-13 03:09:30 +00:00` 表示同一时刻。

以下使用当前 Identity 的实际表名和列名，运维只需修改开始、结束日期：

```sql
DECLARE @start datetimeoffset = '2026-10-13T00:00:00+08:00';
DECLARE @end   datetimeoffset = '2026-10-14T00:00:00+08:00';

SELECT
    ACCOUNT,
    DISPLAY_NAME,
    CREATED_AT_UTC,
    SWITCHOFFSET(CREATED_AT_UTC, '+08:00') AS CREATED_AT_BEIJING
FROM dbo.ZX_IDENTITY_USER
WHERE IS_DELETED = 0
  AND CREATED_AT_UTC >= @start
  AND CREATED_AT_UTC < @end
ORDER BY CREATED_AT_UTC DESC;
```

`SWITCHOFFSET` 只转换结果的显示偏移，不修改原始数据；`CREATED_AT_UTC` 保留原值，`CREATED_AT_BEIJING` 便于直接阅读。原始 SQL 不会自动应用 EF Core 软删除过滤器，所以普通用户查询显式加 `IS_DELETED = 0`。

北京时间使用固定 `+08:00` 即可；涉及夏令时的地区使用地区时区规则，例如 SQL Server 中的 `CREATED_AT_UTC AT TIME ZONE 'Pacific Standard Time'` 表示美国洛杉矶所在时区，会按该时刻的规则处理夏令时。日期范围的两个边界也应分别按目标地区时区换算，不能把美国统一当作固定 `-08:00`，也不能假定当地每一天都是 24 小时。

### Oracle

Oracle 也支持带时区的时间类型，但本项目当前只接入 SQL Server。以下为类型与查询语法参考，不表示已经提供 Oracle Provider、表结构或迁移。

| Oracle 类型 | 时区行为 |
| ----------- | -------- |
| `DATE` | 保存日期和时分秒，不带时区，不保存小数秒 |
| `TIMESTAMP` | 支持小数秒，不带时区 |
| `TIMESTAMP WITH TIME ZONE` | 支持小数秒及偏移量或地区时区，可表达实际时刻 |
| `TIMESTAMP WITH LOCAL TIME ZONE` | 写入时归一到数据库时区，读取时按会话时区显示，不保留输入的原始时区 |

`TIMESTAMP WITH TIME ZONE` 具备与 SQL Server `datetimeoffset` 类似的带偏移时间能力，并支持地区时区。假设 Oracle 使用相同表名和列名，且 `CREATED_AT_UTC` 的类型为 `TIMESTAMP WITH TIME ZONE`，可这样查询：

```sql
SELECT
    ACCOUNT,
    DISPLAY_NAME,
    CREATED_AT_UTC,
    TO_CHAR(
        CREATED_AT_UTC AT TIME ZONE 'Asia/Shanghai',
        'YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM'
    ) AS CREATED_AT_BEIJING
FROM ZX_IDENTITY_USER
WHERE CREATED_AT_UTC >= TO_TIMESTAMP_TZ(
        '2026-10-13 00:00:00 +08:00',
        'YYYY-MM-DD HH24:MI:SS TZH:TZM'
    )
  AND CREATED_AT_UTC < TO_TIMESTAMP_TZ(
        '2026-10-14 00:00:00 +08:00',
        'YYYY-MM-DD HH24:MI:SS TZH:TZM'
    )
ORDER BY CREATED_AT_UTC DESC;
```

`AT TIME ZONE` 改变查询结果的时区，`TO_CHAR` 显式指定可读格式，避免依赖客户端的默认显示格式；条件仍比较原始时间列和带偏移量的边界。实际 Oracle 表应按自身映射补上软删除条件。越南可使用 `Asia/Ho_Chi_Minh`，美国需指定具体地区，例如 `America/Los_Angeles`，按该地区规则处理夏令时。Oracle 与 SQL Server 的地区时区标识不能直接混用。

两种数据库的带偏移类型都允许保存 `+08:00`，但当前项目仍统一将 `*AtUtc` / `*_AT_UTC` 写为 UTC，保持接口、审计与跨地区查询约定一致。运维查询仅换算显示与条件，不改变存储约定。时间转换放在查询结果或参数上，筛选直接比较原始列，便于使用适用的索引；实际执行计划取决于索引与数据量。

## 保存与事务

[PlatformIdentityDbContext](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/PlatformIdentityDbContext.cs) 实现 `IUnitOfWork`，与各仓储共用当前请求的 DbContext。

新增 / 删除方法登记跟踪状态，实体行为修改状态，Service 最后执行一次 `SaveChangesAsync(cancellationToken)`。EF Core 对一次保存中的多条数据库变更提供事务提交，不把创建用户与创建关系分成多个独立保存。

这个事务不覆盖保存前的全部查询，也不覆盖跨服务或外部系统。客户端响应丢失、超时或收到错误时，不能由此认定数据库没有提交。重复操作与幂等的区别见[后续规划](roadmap.md#事务幂等与-redis)。

业务接口要求显式传递 `CancellationToken`，DbContext 的 override 和 EF Core 拦截器遵循框架签名，可以保留框架的默认参数。

## 唯一冲突

Service 预查询只是友好提示，两个同时创建的请求仍可能都通过查询。最终由数据库唯一索引拒绝重复。

当前 `SaveChangesAsync` 捕获 `DbUpdateException` 的内部 SQL Server 错误 `2601` / `2627`，转换为 `UniqueConstraintException`。API 将它映射为 `409 / 600003` 和通用本地化重复提示，不把数据库异常详情返回客户端。

这个保存阶段冲突不会重新查询账号并生成专用 `data.reason`。它与 Service 预查询返回的 `AccountDeleted` 等结构化冲突有所区别，调用方要允许通用冲突响应。

## 查询与分页

只有用户列表分页，支持账号 / 显示名称关键字、启用状态、厂区关系和创建时间范围筛选；查询使用 `AsNoTracking()`，按 `CreatedAtUtc` 倒序、`Id` 次序，`total` 是同一筛选条件下未分页的总数。厂区列表不分页，直接返回全部未删除厂区，同样按 `CreatedAtUtc` 倒序、`Id` 次序。

用户列表获取当前页后，批量读取这些用户的关系并分组，避免每个用户单独查关系。启用厂区列表没有分页和明确排序，调用方不要依赖数据库返回顺序。

当前未为列表与总数建立一致性快照，在其他请求同时修改数据时，两次查询可能看到不同的时点。筛选与参数边界见[分页与结果](api.md#分页与结果)。

## 迁移现状

Migration 和模型快照放在 Infrastructure 的 `Persistence/Migrations/`，并纳入 Git。当前迁移顺序为：

1. `20260915065433_InitialIdentitySchema`。
2. `20260916013615_AlignUserFactoryQueryFilter`。
3. `20260916030633_RemoveIdentityNavigations`。
4. `20260920085118_RemoveUserFactoryIsEnabled`。
5. `20261016080000_AddRolesAndPermissions`：角色、权限目录、用户角色及角色权限四表。
6. `20261016090000_AddMenus`：菜单表、父级索引和按类型/未删除状态过滤的唯一路径及操作权限索引；启动时补齐默认菜单。
7. `20261017015833_AddMenuDisplayOptions`：菜单显示、标签显示、固定标签三列。已有行的 SHOW_IN_MENU / SHOW_IN_TAGS 设为 true，AFFIX 设为 false，保持升级前的导航、标签及缓存行为。

历史迁移反映模型演进，最终模型以当前 Configuration 和快照为准。不要只取最后一份 Migration 建库，也不要改动已应用的迁移来掩盖后续模型变化。

## 迁移命令

在仓库根目录运行。`--project` 指向保存迁移的 Infrastructure，`--startup-project` 指向负责配置数据库与 DI 的 Api。

如尚未安装 EF CLI，使用与当前 EF Core 匹配的版本；已经安装时按需更新：

```powershell
dotnet tool install --global dotnet-ef --version 10.0.12
```

先按[开发环境](development.md#数据库配置)设置连接字符串。CLI 设计时运行不使用 `launchSettings.json`，因此从 User Secrets 读取时显式设置环境：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef migrations list --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext
dotnet ef database update --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext
```

`database update` 会修改所配置的数据库，执行前核对环境和目标。应用启动时没有自动调用 `Database.Migrate()`。角色权限版本启动会访问新增表，升级后必须先应用迁移；不要先启动旧数据库结构上的新程序。当前开发库已应用上述迁移；其他环境部署前仍需先执行迁移，并核对目标数据库。

模型或 Fluent API 发生变化时新增迁移，名称改为描述实际变更的名字：

```powershell
dotnet ef migrations add <描述本次模型变更的名称> --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext --output-dir Persistence/Migrations
```

检查生成的 `Up/Down`、列类型、默认值、索引和可能的数据丢失，再决定应用方式。不要屏蔽 `PendingModelChangesWarning`，也不要通过删除迁移绕过真实变更。

## 验收重点

在测试数据库验证创建用户与关系一次提交、重复账号 / 编码、软删除后标识仍不能复用、删除 / 恢复状态、关系差异更新和保存失败回滚。模型更新后检查数据库结构与快照一致。

厂区关系尚未实现真正的授权检查或动态切库；无物理外键时的跨请求关系一致性也需在后续业务设计与验收中明确，不能仅凭创建时的查询认定所有并发场景已解决。
