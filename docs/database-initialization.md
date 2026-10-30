# Identity 数据库首次初始化与重建

[文档导航](README.md) · [增量迁移与 DBA 交付](migration-operations.md) · [Runner 发布](deployment.md)

本页保留原部署文档中的数据库操作，适用于首次空库初始化或经确认可以丢弃 MOM 数据的重建。日常发布使用增量迁移，不清空数据库。初始化 SQL 不创建登录账号；Oracle 用户、表空间和对象授权由 DBA 准备。

只维护 `PlatformIdentityDbContext` 和 `Persistence/Migrations/` 一套迁移，布尔字段按 Oracle 19c 使用 `NUMBER(1)`。`IdentityDatabase__Schema` 是表 owner，连接字符串中的 `User Id` 是应用登录账号，二者可不同。

## 开发机生成初始化 SQL

在后端仓库根目录执行。开发机需 .NET 10 SDK 和 `dotnet-ef 10.0.12`；首次安装使用 `dotnet tool install --global dotnet-ef --version 10.0.12`，已安装旧版本使用 `dotnet tool update --global dotnet-ef --version 10.0.12`。SQL 转换工具需开发机 Python 3，服务器和 DBA 不需要安装。

当前基线为 `20261027000000_InitialIdentitySchema`，随后是 `20261028084818_ReplaceFactoriesWithSbus`。现有导出工具的初始化检查针对基线结构，**先导出基线，再导出 SBU 增量，不把两段合并后传给初始化转换工具**。

将下列三个目标值替换为实际表 owner、DBA 执行账号和 Oracle 服务名。命令仅生成文件，不连接或修改数据库：

```bash
TARGET_SCHEMA='SAJET'
LOGIN_USER='MOM_MIGRATION'
DB_SERVICE='MOMPROD'
mkdir -p output/mom-deploy

dotnet ef migrations script 0 20261027000000_InitialIdentitySchema \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/initialize.generated.sql

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/initialize.generated.sql \
  output/mom-deploy/initialize.sql \
  "$TARGET_SCHEMA" "$LOGIN_USER" "$DB_SERVICE"

dotnet ef migrations script \
  20261027000000_InitialIdentitySchema 20261028084818_ReplaceFactoriesWithSbus \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/upgrade.generated.sql

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/upgrade.generated.sql \
  output/mom-deploy/upgrade.sql \
  "$TARGET_SCHEMA" "$LOGIN_USER" "$DB_SERVICE" \
  --from-migration 20261027000000_InitialIdentitySchema \
  --to-migration 20261028084818_ReplaceFactoriesWithSbus
```

交付 DBA 的是审核后的 `initialize.sql` 和 `upgrade.sql`，依次执行。`*.generated.sql` 是原始审查文件。以后新增迁移时，先从发布代码列出迁移，明确要到达的版本，再按[增量迁移手册](migration-operations.md#正式库交-dba)生成后续 SQL。

## 执行与核对

DBA 在同一会话中先确认连接：

```sql
SELECT SYS_CONTEXT('USERENV', 'SERVICE_NAME') AS SERVICE_NAME,
       SYS_CONTEXT('USERENV', 'SESSION_USER') AS LOGIN_USER
FROM dual;
```

空库以数据库工具的“执行脚本”模式运行完整 `initialize.sql`，成功后再执行 `upgrade.sql`。正式库由 DBA 执行；Oracle DDL 会隐式提交，遇错需保存日志、核对已执行步骤，不能用 `ROLLBACK` 撤回整套 DDL 或盲目重跑。

已有 SQL*Plus 的执行机也可创建以下 `run-initialize.sql` 包装文件，增量脚本单独使用 `@upgrade.sql` 和对应日志名：

```sql
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET DEFINE OFF
SET ECHO ON
SPOOL initialize-execution.log
@initialize.sql
SPOOL OFF
EXIT SUCCESS
```

```bash
sqlplus -L '执行账号@//数据库地址:1521/服务名' @run-initialize.sql
```

按提示输入密码，不放进脚本或命令行。执行后核对实际 owner，下面以 `SAJET` 为例：

```sql
SELECT table_name FROM all_tables
WHERE owner = 'SAJET' AND table_name LIKE 'ZX_IDENTITY%'
ORDER BY table_name;

SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

目标应包含 SBU 及用户 SBU 表，历史最后一条为本次目标 ID。DBA 还需确认应用登录账号对新对象的读写权限；运行账号无需正式库 DDL 权限。

## 首个管理员

数据库结构完成后，在数据库客户端执行操作者本地保存的一次性管理员 SQL。先确认 schema 与目标库匹配；该 SQL 创建首个用户、内置管理员角色和关联，密码使用 BCrypt 哈希，不进入 Git、镜像或发布包。

177、178 共用数据库，管理员只创建一次；之后启动或发布 Identity。应用启动只补齐权限目录和默认菜单，不从 User Secrets 或环境变量创建、重置管理员。后续用户和角色由后台维护，见[角色与权限](authorization.md#首个管理员)。

## 本机清空后的初始化

本机 owner 为 `ZX_IDENTITY` 时，可以直接按[本机迁移](migration-operations.md#本机更新)执行 `dotnet ef database update --connection ...`，一次应用全部尚未执行的迁移，无需先导出 SQL。设计时工厂不读取 User Secrets 的连接，必须显式指定真实连接。应用运行连接仍使用自己的应用账号。

迁移后打开本地保存的管理员 SQL，确认目标为本机 `ZX_IDENTITY` 后执行一次，再启动 Identity 和前端。不需要恢复任何旧首次管理员配置项。

## 需要清空重建时

仅在已确认可以丢弃全部 MOM 用户、角色、权限、菜单及关系数据时使用。先停止所有连接该 schema 的 Identity 实例，并按数据库变更流程备份。使用与初始化相同的目标值生成重置 SQL：

```bash
python3 deploy/docker/export-identity-reset-sql.py \
  output/mom-deploy/reset-identity.sql \
  "$TARGET_SCHEMA" "$LOGIN_USER" "$DB_SERVICE"
```

该命令仅生成文件。DBA 审核后执行 `reset-identity.sql`，再依次执行初始化、增量和单独的管理员 SQL。工具只处理明确列出的 MOM 表及迁移历史表，保留其他业务表；共享 schema 禁止使用 `DROP USER ... CASCADE`。删表后原对象授权需重新授予。

重置使用 `DROP TABLE ... PURGE`，不能从回收站恢复；不要清空共享 schema 的整个回收站，也不要因外部约束阻挡就扩大删除范围。旧迁移 ID 不在当前链时不能直接增量升级；针对已确认的 `20261026114558_InitialSajet19IdentitySchema` 九表状态，另见[SAJET 定向重建交接](sajet-rebuild-runbook.md)，该特定脚本不能套用到已投入使用的数据。
