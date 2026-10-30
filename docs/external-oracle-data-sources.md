# 外部 Oracle 数据源接入

[实施交接](sbu-mes-lowcode-handoff.md) · [数据库与持久化](database.md)

第一阶段在现有后端内按业务模块接入 MES；外部 Oracle 连接与 Identity 的 `PlatformIdentityDbContext` 独立。暂不为每个 MES 库或 SBU 新建服务，也不建立全局的“SBU → 数据库”映射。后续若业务需要独立部署，再迁出业务模块。

## 配置与连接生命周期

`ExternalOracle:Sources` 是服务端连接别名到 Oracle 连接字符串的映射。例如部署环境可提供 `ExternalOracle__Sources__MesE` 和 `ExternalOracle__Sources__MesF`；别名仅由后端业务代码选择，不能从 HTTP 参数直接传给连接工厂。真实连接字符串通过部署密钥或本地 User Secrets 注入，不写进仓库或前端。没有配置任何外部库时，现有 Identity 接口仍可启动；请求未知别名时连接工厂明确失败，不回退到 Identity 库或其他 MES 库。

`IExternalOracleConnectionFactory.OpenAsync(alias, cancellationToken)` 每次创建一个连接对象并打开连接。使用方在一次业务操作结束时 `await using` 释放连接；Oracle 驱动的连接池负责复用对应连接字符串的物理连接。打开失败时工厂释放连接对象。每个库的连接池上限、连接寿命等由它自己的连接字符串配置，和 Identity 数据库连接池互不混用。

当前本机已配置并验证三个外部别名：`MesE`、`MesF`、`MesEB`。站点查询第一版的业务对照为：`MesE` → SBU1、SBU8；`MesF` → SBU2、SBU3；`MesEB` → SBU4。VNK → SBU7 暂不接入。这个对照属于站点查询模块的策略，不是全局 SBU 数据源绑定；业务仍须按已验证的 SBU 过滤记录。

```csharp
await using var connection = await connections.OpenAsync("MesE", cancellationToken);
await using var command = connection.CreateCommand();
command.CommandTimeout = 10; // 按具体业务查询调整，不作为所有 MES 操作的统一值。
```

连接工厂不限制 SQL 为只读。查询、插入和更新分别由业务模块实现，账号权限由部署环境配置；具体业务操作仍须校验功能权限及记录的 SBU 归属。单库写入使用该连接上的事务；跨 Identity 与 MES 或 E/F 两库的操作不自动共享事务，重复写入需要业务幂等规则。站点查询只开放已确认归属和过滤条件的数据。

## 下一步

先确认 E/F/EB 库的站点到机种关联链、机种 SBU 字段和三库差异，再实现站点查询的查询策略。VNK 当前不接入。每项业务自行决定使用哪个外部数据源、怎样过滤 SBU；外部 MES 表不纳入本平台的 EF Migration。当前连接工厂仅完成配置与连接生命周期，尚未实现站点业务接口。

本机可在仓库根目录运行定向连通性检查。它读取当前 Identity API 的 User Secrets，逐个打开并释放已配置的数据源，不执行业务 SQL：

```bash
dotnet run --project Tests/ZX.Platform.Identity.AuthChecks/ZX.Platform.Identity.AuthChecks.csproj \
  --no-launch-profile -- --external-oracle-connectivity
```

输出只包含数据源别名、连接状态和非敏感的错误编号/类型；连接失败时不会打印连接字符串或完整异常消息。2026-10-29 修正 E/EB 的服务名后，本机 `MesE`、`MesEB`、`MesF` 均成功打开并释放连接。此检查不执行 SQL，尚未验证具体表的访问权限和站点查询规则。
