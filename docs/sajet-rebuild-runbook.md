# SAJET Identity 定向重建交接

[迁移操作手册](migration-operations.md) · [数据库初始化](database-initialization.md)

适用范围：`SAJET` 下截图确认的九张 `ZX_IDENTITY%` 表，当前迁移历史仅有 `20261026114558_InitialSajet19IdentitySchema`。用户已确认这些 Identity 数据均可丢弃，正式库尚未发布。**这不是保留数据的增量迁移**；现有用户、角色、菜单、权限和厂区关系都会删除。`SAJET` 下其他业务表不在脚本清单内。

## 交给 DBA 的文件

在本机仓库的 `output/mom-deploy/formal-sajet-rebuild/` 中按顺序执行：

1. [01-reset-identity.sql](../output/mom-deploy/formal-sajet-rebuild/01-reset-identity.sql)：检查旧迁移 ID、历史表只有一条记录及当前精确的九张 Identity 表，然后删除明确列出的 Identity 表。
2. [02-initialize-baseline.sql](../output/mom-deploy/formal-sajet-rebuild/02-initialize-baseline.sql)：检查目标对象不存在，创建当前代码的 `20261027000000_InitialIdentitySchema` 基线。
3. [03-replace-factories-with-sbus.sql](../output/mom-deploy/formal-sajet-rebuild/03-replace-factories-with-sbus.sql)：检查基线迁移 ID，删除旧厂区表，创建 SBU 表，记录 `20261028084818_ReplaceFactoriesWithSbus`。

三份文件已从当前 EF 迁移离线生成，目标 schema 固定为 `SAJET`，不包含数据库密码，也不会连接数据库。它们不预填 DBA 登录账号和服务名；DBA 须在执行前确认当前数据库连接。**只交付这三份 `.sql`，不要交付同目录之外的 `*.generated.sql`。**

## DBA 执行顺序

1. 停止所有连接该 schema 的 Identity API 实例；按正式库变更流程留存执行前备份或快照。`DROP TABLE ... PURGE` 不进入回收站，不能依靠 `ROLLBACK` 恢复。
2. 在拟执行脚本的同一数据库会话中查询 `SERVICE_NAME`、`SESSION_USER`，确认连接的是目标正式库且账号有 `SAJET` 目标对象的 DDL 权限。核对当前九张 Identity 表及唯一旧迁移记录。
3. 使用数据库客户端的**执行脚本**模式，依次执行 `01`、`02`、`03`。每份执行成功后再开始下一份；遇到错误立即停止并保留日志。Oracle DDL 会隐式提交，不能直接重跑整套文件。
4. 对重建后的表重新授予 Identity 应用登录账号所需的读写权限。原表被删除后，原对象授权不会自动保留。
5. 核对表和迁移历史，再由负责人员执行针对 `SAJET` 审核过的首个管理员初始化 SQL。迁移只创建结构，不创建用户；应用首次启动只补齐权限目录和默认菜单。
6. 发布并启动与这套结构匹配的 Identity 应用，验证登录、SBU 管理和用户 SBU 关联。

执行前的只读核对：

```sql
SELECT SYS_CONTEXT('USERENV', 'SERVICE_NAME') AS SERVICE_NAME,
       SYS_CONTEXT('USERENV', 'SESSION_USER') AS LOGIN_USER
FROM dual;

SELECT table_name FROM all_tables
WHERE owner = 'SAJET' AND table_name LIKE 'ZX_IDENTITY%'
ORDER BY table_name;

SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

三份脚本成功后的核对：迁移历史应依次是 `20261027000000_InitialIdentitySchema` 和 `20261028084818_ReplaceFactoriesWithSbus`；存在 `ZX_IDENTITY_SBU`、`ZX_IDENTITY_USER_SBU`，不再存在 `ZX_IDENTITY_FACTORY`、`ZX_IDENTITY_USER_FACTORY`。此时用户、角色等业务表为空，权限和默认菜单由新应用启动后同步，首个管理员仍需单独初始化。

## 重新生成 SQL

仅在三份 SQL 尚未交付或执行、且当前迁移代码未变化时重新生成。先在仓库根目录离线导出两段 EF SQL：

```bash
dotnet ef migrations script 0 20261027000000_InitialIdentitySchema --no-build \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/formal-reset-template/01-initialize.generated.sql

dotnet ef migrations script 20261027000000_InitialIdentitySchema 20261028084818_ReplaceFactoriesWithSbus --no-build \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/formal-reset-template/02-sbu.generated.sql
```

再运行导出工具：

```bash
python3 deploy/docker/export-identity-reset-sql.py \
  output/mom-deploy/formal-sajet-rebuild/01-reset-identity.sql \
  SAJET --session-agnostic \
  --expected-migration 20261026114558_InitialSajet19IdentitySchema \
  --expected-tables ZX_IDENTITY_FACTORY,ZX_IDENTITY_MENU,ZX_IDENTITY_MIGRATIONS,ZX_IDENTITY_PERMISSION,ZX_IDENTITY_ROLE,ZX_IDENTITY_ROLE_PERMISSION,ZX_IDENTITY_USER,ZX_IDENTITY_USER_FACTORY,ZX_IDENTITY_USER_ROLE

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/formal-reset-template/01-initialize.generated.sql \
  output/mom-deploy/formal-sajet-rebuild/02-initialize-baseline.sql \
  SAJET --session-agnostic

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/formal-reset-template/02-sbu.generated.sql \
  output/mom-deploy/formal-sajet-rebuild/03-replace-factories-with-sbus.sql \
  SAJET --session-agnostic \
  --from-migration 20261027000000_InitialIdentitySchema \
  --to-migration 20261028084818_ReplaceFactoriesWithSbus
```

`--no-build` 仅在当前项目已经成功编译后使用；重新生成后仍需审核 SQL。任何迁移文件、正式库现状或目标 schema 变化，都要重新评估，不能机械重发这套脚本。
