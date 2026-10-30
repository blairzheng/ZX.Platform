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
dotnet test Tests/ZX.Platform.LowCode.Tests/ZX.Platform.LowCode.Tests.csproj
```

纯内存单测，**不需要数据库、不需要 Redis**。当前 103 条。

覆盖的是"最容易错、错了最难查"的部分：类型 → 列型映射、算子白名单、LIKE 转义、SBU 过滤、分页上限、乐观锁、保留列不可写、标识符注入拒绝。

## 5. 当前实现进度

### 已完成

| 层 | 内容 |
| --- | --- |
| `LowCode.Domain` | 13 种字段类型 + 类型×算子矩阵；实体/字段/视图模型；命名规则；28 条校验规则；默认值解析；元数据 POCO；错误码 600xxx/610xxx |
| `LowCode.Infrastructure` | Oracle 标识符处理、类型映射、DDL 生成、查询生成、写入生成；`LowCodeDbContext` + 6 张元数据表 + 迁移；设计时工厂 |
| `Tests` | 103 条单测 |

### 未开始

| 层 | 内容 |
| --- | --- |
| `LowCode.Application` | `MetadataService`（草稿/发布/版本/回滚）、`QueryEngine`、`RecordEngine`、`SchemaChangeService` |
| `LowCode.Api` | Controller、网关路由、`AddApplicationPart` 挂载 |
| `LowCode.Contracts` | 对外 DTO |
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
