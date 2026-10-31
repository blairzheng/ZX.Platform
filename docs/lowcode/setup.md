# 开发环境与运行

更新于 2026-10-30。这份文档只讲**怎么跑起来**：数据库准备、连接串、迁移命令、测试命令，以及当前实现到哪一步。设计理由见 [README.md](README.md)。

## 1. 数据库准备

### 为什么单独一个 schema

两条理由，第二条比第一条更实际：

1. **能整体摘除**。低代码元数据和业务表都在 `ZX_LOWCODE` 里，下线就是 `DROP USER ZX_LOWCODE CASCADE`，Identity 的迁移历史一行不动。
2. **配额**。本机 `ZX_IDENTITY` 在 `ZX_IDENTITY_DATA` 上只有 1.56 MB，装完 Identity 的 9 张表已经所剩无几；低代码 6 张元数据表加上业务表，跑两天就满。

### 建用户（需要 DBA 账号）

```sql
CREATE USER ZX_LOWCODE IDENTIFIED BY "LowcodeDev2026"
  DEFAULT TABLESPACE USERS
  QUOTA UNLIMITED ON USERS;

GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE VIEW TO ZX_LOWCODE;
```

`ZX_IDENTITY` 账号只有 `CREATE SESSION / CREATE TABLE / CREATE SEQUENCE`，**没有 `CREATE USER` 也没有 `CREATE ANY TABLE`**，所以这一步必须用 DBA 账号做。

密码按你们的规范改，改完只需要更新本地环境变量，不进仓库。

### 为什么迁移里没有 schema 前缀

EF 迁移会把 schema 名烤进 SQL。如果模型里写死 `ZX_LOWCODE`，同一份迁移就装不进别的 schema；如果做成配置项，迁移文件就得按环境生成两份。

所以最终做法是：**模型里不设默认 schema，表名不带前缀，落在连接账号自己的 schema 里。**

```
连 ZX_LOWCODE  → 表建在 ZX_LOWCODE（生产）
连 ZX_IDENTITY → 表建在 ZX_IDENTITY（临时验证）
同一份迁移，两个环境都能用
```

迁移历史表固定叫 `LC_MIGRATIONS`，和 `ZX_IDENTITY_MIGRATIONS` 完全分开。

## 2. 连接串

低代码优先读 `ConnectionStrings__LowCodeDatabase`，没有就回落到 `ConnectionStrings__IdentityDatabase`（见 `LowCodeDbContextFactory`）。

```bash
# 建议写进 ~/.zshrc 或本地 .env，不要提交
export ConnectionStrings__LowCodeDatabase='User Id=ZX_LOWCODE;Password=***;Data Source=127.0.0.1:1521/FREEPDB1'
```

不配的话，`dotnet ef` 会直接报错并提示变量名，不会静默连错库。

## 3. 迁移

工作目录：仓库根目录。

```bash
# 生成迁移（不需要连库）
dotnet ef migrations add <Name> \
  --project Services/LowCode/ZX.Platform.LowCode.Infrastructure \
  --context LowCodeDbContext \
  --output-dir Persistence/Migrations

# 导出 SQL（给 DBA 或者先看一眼）
dotnet ef migrations script \
  --project Services/LowCode/ZX.Platform.LowCode.Infrastructure \
  --context LowCodeDbContext \
  --output /tmp/lowcode-metadata.sql

# 应用（开发/测试环境）
dotnet ef database update \
  --project Services/LowCode/ZX.Platform.LowCode.Infrastructure \
  --context LowCodeDbContext
```

三条注意事项：

- **`--startup-project` 不用给**。`LowCodeDbContextFactory` 是设计时工厂，classlib 自己就能跑。
- 脚本默认带幂等包装（先查 `USER_TABLES` 再建），重复执行不会报错。
- 如果提示 `tools version 10.0.12 older than runtime 10.0.14`，是全局 `dotnet-ef` 比运行时旧，不影响生成，想消掉就 `dotnet tool update --global dotnet-ef`。

### 已经生成的迁移

| 迁移 | 内容 |
| --- | --- |
| `20261030145048_InitialLowCodeMetadata` | 6 张元数据表 + 6 个索引 |

## 4. 测试

```bash
# 纯内存单测（不需要数据库、不需要 Redis）
dotnet test Tests/ZX.Platform.LowCode.Tests/ZX.Platform.LowCode.Tests.csproj

# 加上真库集成检查（7 条，连本机 Oracle 建真表、跑真 DDL）
LOWCODE_TEST_DB='User Id=ZX_IDENTITY;Password=***;Data Source=127.0.0.1:1521/FREEPDB1' \
  dotnet test Tests/ZX.Platform.LowCode.Tests/ZX.Platform.LowCode.Tests.csproj \
  --filter "FullyQualifiedName~OracleIntegrationTests"

# 低代码 HTTP 全链路（需要 Redis；元数据与业务表用内存替身）
dotnet run --project Tests/ZX.Platform.Identity.AuthChecks
```

当前 171 条单测 + 7 条真库集成检查 + 1 组 HTTP 全链路检查。

覆盖的是"最容易错、错了最难查"的部分：类型 → 列型映射、算子白名单、LIKE 转义、SBU 过滤、分页上限、乐观锁、保留列不可写、标识符注入拒绝、发布状态机、结构变更回路、值规范化、唯一性预检、DDL 执行状态机。

### 真库集成检查抓到的三个问题

这三个都只在真库上暴露，单测再密也发现不了：

| 问题 | 后果 | 修法 |
| --- | --- | --- |
| `new OracleParameter(name, Guid)` | ODP.NET 的隐式映射表里没有 `Guid`，抛 `ArgumentException`，**所有写操作全挂** | `CreateParameter` 按 CLR 类型显式指定 `OracleDbType`，`Guid` → `Raw` + `byte[16]` |
| `date` 字段绑 `DateTimeOffset` | 列是 `DATE`，绑定变量是 `TIMESTAMP WITH TIME ZONE`，ORA-00932 | `WriteSqlBuilder.Coerce` 按字段类型归一：`Date` → `DateTime.Date`，`DateTime` → UTC |
| RAW(16) 读出来是 `byte[]` | 序列化成 JSON 是 base64，前端拿 ID 查详情 404 | 网关 `Normalize` 里 `byte[16]` → `Guid` |

另外验证了"唯一索引靠虚拟列模拟部分索引"这套做法在真库上成立：重复编码撞 ORA-00001，软删之后同一编码可以再用。

## 5. 当前实现进度

### 已完成

| 层 | 内容 |
| --- | --- |
| `LowCode.Domain` | 13 种字段类型 + 类型×算子矩阵；实体/字段/视图模型；命名规则；28 条校验规则；默认值解析；元数据 POCO；错误码 600xxx/610xxx；`QuerySpec` 查询语义；不可逆变更检查 |
| `LowCode.Application` | `MetadataService`（草稿/发布/版本/回滚）；`QueryEngine`（列表/单条）；`RecordEngine`（新增/修改/软删 + 唯一性预检）；`RecordValueMapper`；`SchemaChangeService`（执行/导出/核对） |
| `LowCode.Infrastructure` | Oracle 标识符处理、类型映射、DDL 生成、查询生成、写入生成；`LowCodeDbContext` + 6 张元数据表 + 迁移；设计时工厂；`EfMetadataStore`；`SchemaChangePlanner`；`OracleSchemaInspector`；`OracleSchemaChangeExecutor`；`DapperRecordGateway` |
| `ZX.Platform.Identity.Api` | `LowCodeController` 三个控制器（元数据 / 业务数据 / 结构变更），13 条路由；`LowCodePermissionGuard`；`AddLowCodeModule`；`LowCodeException` → HTTP 映射 |
| `Tests` | 171 条单测 + 7 条真库集成检查 + 1 组 HTTP 全链路检查 |

### 未开始

| 层 | 内容 |
| --- | --- |
| 权限注册 | 实体发布时把 `{app}.{entity}.{action}` 写进 `ZX_IDENTITY_PERMISSION` 目录，否则非管理员永远没法授权 |
| 前端 | `LowCodePage` 渲染器、`SchemaTable` / `SchemaForm`、设计器 |
| 外部数据集 | 连接器、只读视图取数、SBU 绑定变量 |
| 导出 | MiniExcel 服务端导出 |

## 6. 几个实现上的约定

| 约定 | 说明 |
| --- | --- |
| 字段 key 小写蛇形 | `is_enabled` 而不是 `isEnabled`。key 大写即列名，中间零转换；混写会撞名 |
| 表名不截断 | 超 30 字符直接拒绝。表名同时出现在 DDL、查询、迁移里，截断会导致三处不一致 |
| 索引名可截断 | 确定性哈希后缀，同一输入永远同一结果，否则重复生成 DDL 会得到不同约束名 |
| 元数据类不用继承 | EF 会把基类当成 TPH 层次，四张表互相打架。各自独立属性 + 字符串 API 配置 |
| EF 版本对齐 10.0.12 | 与 Identity 一致。不同版本会在同一进程里出现两套 EF 程序集 |
| 业务表不走 EF | 列是运行时才有的，EF 的编译期模型用不上。走 Dapper + 手写 SQL，共用同一个连接与事务 |
| 结构变更以库为准 | 不拿"已发布定义"当基线。生产 DDL 由 DBA 执行，提前建好的列不能再算成"缺失"，否则实体永远发布不出去 |
| 查询不带 schema 前缀 | 表建在哪个 schema 由连接账号决定。只有 DDL 导出脚本支持 `schema.table`，方便 DBA 在别的账号下执行 |
| 值规范化在服务端 | Oracle 把 boolean 存成 `NUMBER(1)`、date 存成 `TIMESTAMP`。统一在 `RecordValueMapper` 转换，前端只认一种形状 |
| "待执行"= 状态不是 applied | 不是"等于 pending"。导出了、执行中、执行失败都算没完成，否则变更一从 pending 走到 exported，发布闸门就失效 |
| DDL 不当原子操作 | Oracle 的 DDL 每条隐式提交。执行失败必须报"第几条 + 前面的已生效"，不能让用户以为可以无脑重试 |
| 唯一性预检不按 SBU 过滤 | 唯一索引建在整张表上。预检只看当前 SBU 的话，两个 SBU 各插一条同编码会双双放行，最后撞索引 |
| 权限不走 `HasAsync` | 实体权限是动态的（`mes.stocktake.read`），而 `IPermissionService.HasAsync` 第一步就查 `IdentityPermissions.All` 静态目录，动态码一律 false——用它判定会让**所有低代码接口连管理员都 403**。`LowCodePermissionGuard` 直接读授权快照 |
| 绑定参数显式指定 `OracleDbType` | `new OracleParameter(name, value)` 不认 `Guid`，会抛 `ArgumentException`。ID / SBU_ID / 关联字段全是 RAW(16)，等于所有写操作都过不去 |
| 字符串按长度分流 | 超过 4000 字符绑 CLOB，否则驱动拒绝。VARCHAR2 → CLOB 在 INSERT/UPDATE 里 Oracle 允许隐式转换，反向不行，所以只单向分流 |
