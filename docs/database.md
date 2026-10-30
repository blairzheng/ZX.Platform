# 数据库与持久化

[文档导航](README.md) · [开发者指南](developer-guide.md)

Identity 使用 EF Core 10 与 Oracle.EntityFrameworkCore 10.23.26400，所有环境统一采用 Oracle 19c 模型。只有 `PlatformIdentityDbContext` 和一套迁移；表所属 schema 通过 `IdentityDatabase:Schema` 配置。业务表由本服务维护，其他业务服务通过接口或集成事件协作。

## 模型与表

| 实体          | 表                         | 主键 / 唯一性                                 | 生命周期           |
| ------------- | -------------------------- | --------------------------------------------- | ------------------ |
| `User`        | `ZX_IDENTITY_USER`         | 应用生成 `Guid`；`NormalizedAccount` 全表唯一 | 软删除与恢复       |
| `Sbu`     | `ZX_IDENTITY_SBU`      | 应用生成 `Guid`；`Code` 全表唯一              | 软删除与恢复       |
| `UserSbu` | `ZX_IDENTITY_USER_SBU` | `UserId + SbuId` 联合主键                 | 新增或物理删除关系 |
| `Role` | `ZX_IDENTITY_ROLE` | Guid；`Code` 全表唯一 | 软删除；内置角色只读 |
| `Permission` | `ZX_IDENTITY_PERMISSION` | `Code` 主键 | 启动同步，移除定义后停用 |
| `UserRole` | `ZX_IDENTITY_USER_ROLE` | `UserId + RoleId` 联合主键 | 新增或物理删除关系 |
| `RolePermission` | `ZX_IDENTITY_ROLE_PERMISSION` | `RoleId + PermissionCode` 联合主键 | 新增或物理删除关系 |
| `Menu` | `ZX_IDENTITY_MENU` | Guid；Code 全表唯一；未删除页面 Path、操作 PermissionCode 分别唯一 | 软删除；内置节点仅维护名称/图标/排序 |

`UserSbu` 表示用户和SBU的多对多关系，包含 `IsDefault` 和审计字段，**没有 `IsEnabled` 或软删除字段**。默认SBU的实体方法已经存在，当前接口创建关系时均使用 `IsDefault=false`，尚未建立设置默认SBU的用例。

当前不用实体导航属性和数据库物理外键。主键 / 唯一索引仍由数据库保证；对象是否存在及关系是否允许建立，由 Application 校验。关联查询显式写出筛选、`Any` 或按用户批量取关系，避免隐式 Include。

源码入口：[Entities](../Services/Identity/ZX.Platform.Identity.Domain/Entities)、[Configurations](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations)。

## 账号与SBU编码

用户创建时对账号 `Trim()`，`NormalizedAccount` 使用 `ToUpperInvariant()`；账号唯一性按标准化值判断。SBU编码 `Trim()` 后转为大写。

两种唯一索引都没有按 `IsDeleted` 过滤：软删除后原账号和SBU编码仍被保留，不能用同一标识建立新的主数据。创建前的预查询使用 `IgnoreQueryFilters()`，因此能给出“已删除 / 已禁用”的友好冲突提示。

数据库映射允许账号最长 100、SBU编码最长 50，但当前创建用户 DTO 将账号限制为最多 8 个字符；接口允许值以 DTO 为准，不能从列长推断 API 约束。请求字段详见[Identity 接口契约](api.md#用户请求字段)。

## Oracle 类型与空字符串

Guid 映射为 `RAW(16)`，布尔值为 `NUMBER(1)`，UTC 时间为 `TIMESTAMP(7) WITH TIME ZONE`，有长度限制的 Unicode 文本为 `NVARCHAR2(n)`。

Oracle 将空字符串存为 NULL。菜单的英文标题、图标、路径、组件、权限以及角色说明允许数据库 NULL，实体通过后备字段将它们还原为空字符串，保持接口契约。账号、编码、中文标题等必填字段仍为 NOT NULL。

Oracle 不支持 SQL Server 的筛选索引。菜单通过两个虚拟列及唯一索引实现条件唯一性：仅未删除页面产生 `ACTIVE_PAGE_PATH`，仅未删除操作产生 `ACTIVE_ACTION_PERMISSION`，其余行返回 NULL。19c 数值布尔条件使用 `IS_DELETED = 0`；Oracle B-tree 索引不保存全 NULL 的键。停用但未删除的菜单仍占用索引值，删除后可复用路径或操作权限，编码始终保留。

## 软删除与状态

`User`、`Sbu`、`Role` 和 `Menu` 继承 `SoftDeletableEntity`。`MarkDeleted()` 设置 `IsDeleted=true`，各实体同时设 `IsEnabled=false`。用户和SBU提供恢复接口；角色和菜单当前不提供恢复。角色、菜单及授权关联也有审计字段；权限目录仅保存编码和活跃状态，不继承审计实体。角色关系与缓存失效见[角色与权限](authorization.md)。

普通查询默认使用 `!IsDeleted` 的全局过滤器，包括详情、分页和启用SBU查询。恢复用例通过专用查询包含已删除数据；普通启用接口不会绕过过滤器取得已删除对象。

删除或停用主数据时，现有用户SBU关系不会自动删除。恢复主数据后这些关系仍在。关系列表本身不联查SBU状态，因此用户 DTO 的 `sbuIds` 可能包含后来停用或删除的SBU；读取关系不等于验证当前访问权限。

`IgnoreQueryFilters()` 只在恢复、唯一性判断等明确场景使用，避免普通列表意外显示删除数据。状态接口和恢复时的 HTTP 行为见[Identity 业务语义](identity.md#状态与SBU关系)。

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

| Oracle 类型 | 时区行为 |
| ----------- | -------- |
| `DATE` | 保存日期和时分秒，不带时区，不保存小数秒 |
| `TIMESTAMP` | 支持小数秒，不带时区 |
| `TIMESTAMP WITH TIME ZONE` | 支持小数秒及偏移量或地区时区，可表达实际时刻 |
| `TIMESTAMP WITH LOCAL TIME ZONE` | 写入时归一到数据库时区，读取时按会话时区显示，不保留输入的原始时区 |

当前 `CREATED_AT_UTC` 映射为 `TIMESTAMP(7) WITH TIME ZONE`，可这样查询：

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
WHERE IS_DELETED = 0
  AND CREATED_AT_UTC >= TO_TIMESTAMP_TZ(
        '2026-10-13 00:00:00 +08:00',
        'YYYY-MM-DD HH24:MI:SS TZH:TZM'
    )
  AND CREATED_AT_UTC < TO_TIMESTAMP_TZ(
        '2026-10-14 00:00:00 +08:00',
        'YYYY-MM-DD HH24:MI:SS TZH:TZM'
    )
ORDER BY CREATED_AT_UTC DESC;
```

`AT TIME ZONE` 改变查询结果的时区，`TO_CHAR` 显式指定可读格式，避免依赖客户端的默认显示格式；条件仍比较原始时间列和带偏移量的边界。原始 SQL 不自动应用 EF 查询过滤器，普通业务查询应保留示例的软删除条件。越南可使用 `Asia/Ho_Chi_Minh`，美国需指定具体地区，例如 `America/Los_Angeles`，按该地区规则处理夏令时。地区时区使用 Oracle 支持的标识。

带偏移类型允许保存 `+08:00`，但当前项目统一将 `*AtUtc` / `*_AT_UTC` 写为 UTC，保持接口、审计与跨地区查询约定一致。运维查询仅换算显示与条件，不改变存储约定。时间转换放在查询结果或参数上，筛选直接比较原始列，便于使用适用的索引；实际执行计划取决于索引与数据量。

## 保存与事务

[PlatformIdentityDbContext](../Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/PlatformIdentityDbContext.cs) 实现 `IUnitOfWork`，与各仓储共用当前请求的 DbContext。

新增 / 删除方法登记跟踪状态，实体行为修改状态，Service 最后执行一次 `SaveChangesAsync(cancellationToken)`。EF Core 对一次保存中的多条数据库变更提供事务提交，不把创建用户与创建关系分成多个独立保存。

这个事务不覆盖保存前的全部查询，也不覆盖跨服务或外部系统。客户端响应丢失、超时或收到错误时，不能由此认定数据库没有提交。重复操作与幂等的区别见[后续规划](roadmap.md#事务幂等与-redis)。

业务接口要求显式传递 `CancellationToken`，DbContext 的 override 和 EF Core 拦截器遵循框架签名，可以保留框架的默认参数。

## 唯一冲突

Service 预查询只是友好提示，两个同时创建的请求仍可能都通过查询。最终由数据库唯一索引拒绝重复。

当前 `SaveChangesAsync` 捕获 `DbUpdateException` 的内部 Oracle 错误 `ORA-00001`（Number = 1），转换为 `UniqueConstraintException`。API 将它映射为 `409 / 600003` 和通用本地化重复提示，不把数据库异常详情返回客户端。

这个保存阶段冲突不会重新查询账号并生成专用 `data.reason`。它与 Service 预查询返回的 `AccountDeleted` 等结构化冲突有所区别，调用方要允许通用冲突响应。

## 查询与分页

只有用户列表分页，支持账号 / 显示名称关键字、启用状态、SBU关系和创建时间范围筛选；查询使用 `AsNoTracking()`，按 `CreatedAtUtc` 倒序、`Id` 次序，`total` 是同一筛选条件下未分页的总数。SBU列表不分页，直接返回全部未删除SBU，同样按 `CreatedAtUtc` 倒序、`Id` 次序。

用户列表获取当前页后，批量读取这些用户的关系并分组，避免每个用户单独查关系。启用SBU列表没有分页和明确排序，调用方不要依赖数据库返回顺序。

当前未为列表与总数建立一致性快照，在其他请求同时修改数据时，两次查询可能看到不同的时点。筛选与参数边界见[分页与结果](api.md#分页与结果)。

## 迁移现状

唯一迁移目录为 `Persistence/Migrations/`，快照为 `PlatformIdentityDbContextModelSnapshot`。基线 `20261027000000_InitialIdentitySchema` 创建旧厂区模型；`20261028084818_ReplaceFactoriesWithSbus` 删除旧厂区及用户厂区表，创建 SBU 及用户 SBU 表，并清理旧内置厂区菜单；`20261030100000_AddMenuLauncherMetadata` 增加菜单宫格颜色和中英文描述字段。旧厂区测试数据不会迁移；用户、角色等其他表保留。部署当前代码必须将目标库升级到第三条迁移。本地、测试、正式使用相同迁移链，仅连接和 schema 不同。

应用不会自动执行 DDL。空库先执行初始化 SQL；启动后自动补齐菜单和权限。首次使用前，由运维通过本地保存的单独 SQL 创建首个用户和内置管理员关联，应用不会从配置创建管理员。需要清空已有 MOM 数据时，按[数据库初始化与重建](database-initialization.md)中的定向重置步骤重新初始化。

## 迁移命令

日常按环境操作请先看[迁移操作手册](migration-operations.md)：本机和有 DDL 权限的测试库可自行更新，正式库只导出目标 SQL 交 DBA。

### 本机直接执行 EF 迁移

本机 `ZX_IDENTITY / FREEPDB1` 首次建表或已有库增量升级，均可直接执行以下命令。先保持 Identity.Api 停止，将密码占位符换成本机 `ZX_IDENTITY` 的密码：

```bash
dotnet ef database update \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --context PlatformIdentityDbContext \
  --connection 'User Id=ZX_IDENTITY;Password=<本机ZX_IDENTITY密码>;Data Source=127.0.0.1:1521/FREEPDB1;'
```

设计时工厂默认使用占位连接，直接更新数据库必须显式提供 `--connection`。这条命令会连接实际数据库，按顺序执行全部尚未应用的迁移并记录版本。这里的固定源 schema 是 `ZX_IDENTITY`，此命令用于本机；其他 schema 的部署按下文导出目标 SQL。

本机迁移可由有权限的管理账号执行，但表仍在 `ZX_IDENTITY`，应用运行账号与表 owner 的配置另行保持正确。迁移不会创建登录用户；清空后需用本机保存的管理员初始化 SQL 建立首个账号，再启动 Identity，详见[本机迁移与首个管理员](database-initialization.md#本机清空后的初始化)。

### 导出部署 SQL

在仓库根目录执行；设计时工厂不读取真实数据库连接，生成 SQL 不会操作数据库：

```bash
dotnet tool install --global dotnet-ef --version 10.0.12

dotnet ef migrations list --no-connect \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --context PlatformIdentityDbContext

dotnet ef migrations script 0 20261027000000_InitialIdentitySchema \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --context PlatformIdentityDbContext \
  --output output/mom-deploy/initialize.generated.sql
```

已安装工具时用 `dotnet tool update --global dotnet-ef --version 10.0.12`。模型变化后才执行 `dotnet ef migrations add <名称> --context PlatformIdentityDbContext --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --output-dir Persistence/Migrations`。

生成目标 schema 的 SQL、清空重建、增量迁移起止版本、DBA 执行与验收，统一按 [数据库初始化与重建](database-initialization.md)操作。增量迁移必须明确起止 ID；不在生产容器自动执行 `database update`。创建迁移与生成 SQL 固定以 `ZX_IDENTITY` 为源 schema，导出工具负责替换成实际目标，避免每个环境重复创建迁移。

## 可选 Oracle 集成检查

AuthChecks 默认仍只连接 Redis。显式设置 `IDENTITY_TEST_ORACLE` 为已应用迁移的测试 schema 连接字符串，并通过 `IDENTITY_TEST_ORACLE_SCHEMA` 指定表 owner（默认 `ZX_IDENTITY`）后，额外验证 Oracle 中文/Guid/数值布尔/UTC 往返、文本筛选和分页、集合查询、可选空文本、唯一异常、软删除及菜单路径/权限复用。检查仅在一个事务内写入随机测试记录，最终全部回滚，不执行 DDL、不创建管理员。应使用测试库，连接凭据不要提交到 Git。

## 验收重点

在测试数据库验证创建用户与关系一次提交、重复账号 / 编码、软删除后标识仍不能复用、删除 / 恢复状态、关系差异更新和保存失败回滚。模型更新后检查数据库结构与快照一致。

SBU关系尚未实现真正的授权检查或动态切库；无物理外键时的跨请求关系一致性也需在后续业务设计与验收中明确，不能仅凭创建时的查询认定所有并发场景已解决。
