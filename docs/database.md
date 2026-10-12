# 数据库与持久化

[文档导航](README.md) · [开发者指南](developer-guide.md)

Identity 使用 EF Core 10 与 SQL Server。数据库由本服务拥有；其他业务服务通过接口或集成事件协作，不直接读写 Identity 的表。

## 模型与表

| 实体          | 表                         | 主键 / 唯一性                                 | 生命周期           |
| ------------- | -------------------------- | --------------------------------------------- | ------------------ |
| `User`        | `ZX_IDENTITY_USER`         | 应用生成 `Guid`；`NormalizedAccount` 全表唯一 | 软删除与恢复       |
| `Factory`     | `ZX_IDENTITY_FACTORY`      | 应用生成 `Guid`；`Code` 全表唯一              | 软删除与恢复       |
| `UserFactory` | `ZX_IDENTITY_USER_FACTORY` | `UserId + FactoryId` 联合主键                 | 新增或物理删除关系 |

`UserFactory` 表示用户和厂区的多对多关系，包含 `IsDefault` 和审计字段，**没有 `IsEnabled` 或软删除字段**。默认厂区的实体方法已经存在，当前接口创建关系时均使用 `IsDefault=false`，尚未建立设置默认厂区的用例。

当前不用实体导航属性和数据库物理外键。主键 / 唯一索引仍由数据库保证；对象是否存在及关系是否允许建立，由 Application 校验。关联查询显式写出筛选、`Any` 或按用户批量取关系，避免隐式 Include。

源码入口：[Entities](../Services/Identity/ZX.Platform.Identity.Domain/Entities)、[Configurations](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations)。

## 账号与厂区编码

用户创建时对账号 `Trim()`，`NormalizedAccount` 使用 `ToUpperInvariant()`；账号唯一性按标准化值判断。厂区编码 `Trim()` 后转为大写。

两种唯一索引都没有按 `IsDeleted` 过滤：软删除后原账号和厂区编码仍被保留，不能用同一标识建立新的主数据。创建前的预查询使用 `IgnoreQueryFilters()`，因此能给出“已删除 / 已禁用”的友好冲突提示。

数据库映射允许账号最长 100、厂区编码最长 50，但当前创建用户 DTO 将账号限制为最多 8 个字符；接口允许值以 DTO 为准，不能从列长推断 API 约束。请求字段详见[Identity 接口](identity.md)。

## 软删除与状态

`User` 和 `Factory` 继承 `SoftDeletableEntity`。`MarkDeleted()` 设置 `IsDeleted=true`，各实体同时设 `IsEnabled=false`；`Restore()` 清除删除状态并重新启用。

普通查询默认使用 `!IsDeleted` 的全局过滤器，包括详情、分页和启用厂区查询。恢复用例通过专用查询包含已删除数据；普通启用接口不会绕过过滤器取得已删除对象。

删除或停用主数据时，现有用户厂区关系不会自动删除。恢复主数据后这些关系仍在。关系列表本身不联查厂区状态，因此用户 DTO 的 `factoryIds` 可能包含后来停用或删除的厂区；读取关系不等于验证当前访问权限。

`IgnoreQueryFilters()` 只在恢复、唯一性判断等明确场景使用，避免普通列表意外显示删除数据。状态接口和恢复时的 HTTP 行为见[Identity](identity.md#状态与厂区关系)。

## 审计字段

`AuditableEntity` 提供：

| 字段              | 当前写入方式                                       |
| ----------------- | -------------------------------------------------- |
| `CreatedAtUtc`    | 新增时由拦截器使用 `TimeProvider.GetUtcNow()` 填写 |
| `UpdatedAtUtc`    | 修改时由拦截器填写                                 |
| `CreatedByUserId` | 字段已存在，当前没有自动填写登录用户               |
| `UpdatedByUserId` | 字段已存在，当前没有自动填写登录用户               |

拦截器在修改时保护创建时间和创建人，不让本次更新覆盖原值。时间统一使用 `DateTimeOffset` 和 UTC；普通 DTO 输出 ISO 8601 日期字符串。

审计时间不是操作日志或数据变更历史。谁修改了哪些字段、前后值和原因，需要后续独立的业务审计方案。

源码：[AuditableEntitySaveChangesInterceptor.cs](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Interceptors/AuditableEntitySaveChangesInterceptor.cs)。

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

用户分页支持账号 / 显示名称关键字、启用状态和厂区关系筛选；厂区分页支持编码 / 名称关键字。查询使用 `AsNoTracking()`，按 `CreatedAtUtc` 倒序、`Id` 次序分页，`total` 是同一筛选条件下未分页的总数。

用户列表获取当前页后，批量读取这些用户的关系并分组，避免每个用户单独查关系。启用厂区列表没有分页和明确排序，调用方不要依赖数据库返回顺序。

当前未为列表与总数建立一致性快照，在其他请求同时修改数据时，两次查询可能看到不同的时点。筛选与参数边界见[分页与结果](identity.md#分页与结果)。

## 迁移现状

Migration 和模型快照放在 Infrastructure 的 `Persistence/Migrations/`，并纳入 Git。当前迁移顺序为：

1. `20260915065433_InitialIdentitySchema`。
2. `20260916013615_AlignUserFactoryQueryFilter`。
3. `20260916030633_RemoveIdentityNavigations`。
4. `20260920085118_RemoveUserFactoryIsEnabled`。

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

`database update` 会修改所配置的数据库，执行前核对环境和目标。应用启动时没有自动调用 `Database.Migrate()`。

模型或 Fluent API 发生变化时新增迁移，名称改为描述实际变更的名字：

```powershell
dotnet ef migrations add <描述本次模型变更的名称> --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext --output-dir Persistence/Migrations
```

检查生成的 `Up/Down`、列类型、默认值、索引和可能的数据丢失，再决定应用方式。不要屏蔽 `PendingModelChangesWarning`，也不要通过删除迁移绕过真实变更。

## 验收重点

在测试数据库验证创建用户与关系一次提交、重复账号 / 编码、软删除后标识仍不能复用、删除 / 恢复状态、关系差异更新和保存失败回滚。模型更新后检查数据库结构与快照一致。

厂区关系尚未实现真正的授权检查或动态切库；无物理外键时的跨请求关系一致性也需在后续业务设计与验收中明确，不能仅凭创建时的查询认定所有并发场景已解决。
