# Identity 数据库迁移操作手册

[文档导航](README.md) · [数据库模型](database.md) · [Runner 发布](deployment.md)

适用于当前 `PlatformIdentityDbContext` 的 Oracle 迁移。在 `ZX.Platform` 仓库根目录执行命令。三个环境使用同一套迁移文件，但分别维护自己的 `ZX_IDENTITY_MIGRATIONS` 版本记录；本机更新成功不代表测试库或正式库已更新。

| 环境 | 表结构由谁更新 | 操作方式 |
| --- | --- | --- |
| 本机开发库 | 开发者 | `dotnet ef database update --connection ...` |
| 测试库 | 有建表权限的测试库账号或指定人员 | schema 为 `ZX_IDENTITY` 时可直接 `database update`；其他 schema 按下文生成目标 SQL 后由测试库账号执行 |
| 正式库 | DBA | 开发者只生成并审核目标 SQL，交 DBA 执行；不在正式库运行 `database update` |

## 每次改表的顺序

1. 修改实体和 EF 映射。只改业务逻辑而没有表结构变化时，不创建迁移。
2. 在仓库根目录创建一次迁移，并检查生成的 `Up`、`Down` 和模型快照；迁移文件随代码提交。
3. 更新本机库并验证业务，再更新测试库并验证。
4. 查询正式库当前迁移版本，以它为起点生成增量 SQL；DBA 执行后核对版本，再发布依赖新结构的应用。

```bash
cd /Users/luxshare-ict/Desktop/project/ZX.Platform

dotnet ef migrations add 描述变更的英文名称 \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output-dir Persistence/Migrations
```

**本次 SBU 迁移已经创建并在本机执行，不要再次运行 `migrations add`。**它的 ID 是 `20261028084818_ReplaceFactoriesWithSbus`。以后新改表才创建新迁移。

## 本机更新

先停止本机 Identity API。确认本机 Oracle 已启动，然后在仓库根目录执行。密码从 Git 忽略的 `.env.identity.local` 读取，不要用 `dotnet user-secrets list` 打印密码。

```bash
cd /Users/luxshare-ict/Desktop/project/ZX.Platform
(
  db_password=$(sed -n 's/^IDENTITY_ORACLE_PASSWORD=//p' .env.identity.local)
  if [ -z "$db_password" ]; then
    echo '未读取到本机数据库密码' >&2
    exit 1
  fi
  dotnet ef database update \
    --context PlatformIdentityDbContext \
    --project Services/Identity/ZX.Platform.Identity.Infrastructure \
    --startup-project Services/Identity/ZX.Platform.Identity.Api \
    --connection "User Id=ZX_IDENTITY;Password=${db_password};Data Source=127.0.0.1:1521/FREEPDB1;"
)
```

在数据库客户端检查迁移版本；最后一行应是当前代码要求的目标版本：

```sql
SELECT "MigrationId", "ProductVersion"
FROM ZX_IDENTITY.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

本机已有 SBU 迁移时再次运行 `database update` 不会重复执行它，但也不需要为了确认版本反复更新。`dotnet ef` 使用设计时工厂；未传 `--connection` 时会连到仅供生成 SQL 的占位地址 `127.0.0.1:1`，报 `ORA-50201`。切勿把这个占位地址当数据库故障。

## 测试库更新

先由指定人员确认测试库的服务名、登录账号、表所属 schema 和当前迁移版本。测试库允许建表，不代表可以省略环境核对。共享测试库只由指定人员升级一次。

- 如果测试库表 owner 也是 `ZX_IDENTITY`，可使用上面的 `database update` 命令，将 `--connection` 换成测试库实际连接。先停用依赖旧结构的测试 API，升级后再启动。
- 如果表 owner 是 `SAJET` 等其他 schema，当前设计时工厂仍固定生成 `ZX_IDENTITY` 对象；**只换连接字符串不会改表 owner**。按正式库一节的步骤生成目标 schema SQL，由有权限的测试库账号在测试库执行，不必交 DBA。

本机及测试库的登录账号需要目标表的 DDL 权限；应用日常运行账号不应因此获得正式库 DDL 权限。测试库执行目标 SQL 时，`LOGIN_USER` 填实际执行脚本的账号，`DB_SERVICE` 填实际服务名。

## 正式库交 DBA

若查询到旧迁移 `20261026114558_InitialSajet19IdentitySchema`，该 ID 不在当前迁移链中，不能直接套用下方增量命令或手工改写历史。仅在旧九表状态与数据可丢弃条件均得到确认时，参考[SAJET 定向重建](sajet-rebuild-runbook.md)；已投入使用的数据应先由 DBA 评估保留与转换方案。

先让 DBA 查询正式库当前版本。下面的 `SAJET` 改成实际表 owner：

```sql
SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

开发者有正式库查询权限时可自行查询；执行变更仍交 DBA。再在本次发布对应的代码中列出迁移：

```bash
dotnet ef migrations list --no-connect \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api
```

此列表代表代码迁移，不代表任何库已执行。目标库查询到的最后一条是 `FROM_MIGRATION`；本次发布代码中已在测试库验证的目标版本是 `TO_MIGRATION`。两者相同则无需升级。历史表不存在、版本不在当前代码迁移链里时，先核实库和 schema，不要猜起点或手工填写迁移历史。

应用版本 v1.1/v1.2 不等于 Migration ID。测试库用于确认目标版本及业务已验证，不能把测试库当前 ID 当作正式库的起点。以下示例是从旧基线升级到 SBU。替换为正式库的真实 schema、DBA 执行账号、服务名和实际起止版本；命令**只生成文件，不连接正式库**。

```bash
cd /Users/luxshare-ict/Desktop/project/ZX.Platform
FROM_MIGRATION='20261027000000_InitialIdentitySchema'
TO_MIGRATION='20261028084818_ReplaceFactoriesWithSbus'
TARGET_SCHEMA='SAJET'
LOGIN_USER='DBA实际执行账号'
DB_SERVICE='正式库服务名'

mkdir -p output/mom-deploy
dotnet ef migrations script "$FROM_MIGRATION" "$TO_MIGRATION" \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/upgrade.generated.sql

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/upgrade.generated.sql \
  output/mom-deploy/upgrade.sql \
  "$TARGET_SCHEMA" "$LOGIN_USER" "$DB_SERVICE" \
  --from-migration "$FROM_MIGRATION" \
  --to-migration "$TO_MIGRATION"
```

同时向 DBA 提供目标服务、schema、执行账号、起止 Migration ID、应用版本及预期变更。最终 SQL 已包含迁移历史更新，成功后无需手工补版本记录。

审核 `output/mom-deploy/upgrade.sql` 的目标 schema、起止版本、删表/删列、索引和数据影响。**交给 DBA 的是 `upgrade.sql`，不是 `upgrade.generated.sql`。**本次 SBU 迁移会删除旧厂区及用户厂区测试数据；用户和角色表保留。DBA 执行后，重新查询迁移历史，确认最后一条为 `TO_MIGRATION`，并核对新表及应用账号权限。Oracle DDL 会隐式提交；失败后先检查已执行到哪一步，不要直接重跑。

全新正式库没有迁移历史时，不使用上述增量命令；首次初始化和首个管理员创建按[数据库初始化与重建](database-initialization.md)执行。

## 常见错误

| 现象 | 原因与处理 |
| --- | --- |
| `ORA-50201`，目标 `127.0.0.1:1` | 忘传 `--connection`，EF 使用设计时占位连接；指定真实数据库连接后重试。 |
| `sed: .env.identity.local: No such file`、`Unable to retrieve project metadata` | 当前在 `ZX.Platform.Identity.Api` 子目录；先 `cd` 到 `ZX.Platform` 根目录。 |
| 显示 `Pending` | 迁移文件存在，但该数据库尚未执行；按环境选择本机/测试库更新或正式库 SQL。 |
| 显示 `Done` | 当前命令执行成功；仍要核对迁移历史和业务功能。 |
| `ORA-01031` | 执行账号缺少目标 schema 的 DDL 权限；找测试库管理员或正式库 DBA 核对，不给正式应用账号临时加建表权限。 |

不要把连接密码、`dotnet user-secrets list` 输出或带密码的终端截图发到群里。连接字符串中的 IP、端口和服务名只是目标数据库位置；本机为 `127.0.0.1:1521/FREEPDB1`，测试/正式库按各自实际地址填写。
