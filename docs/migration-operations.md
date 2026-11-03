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

## OA 登录升级（已生成）

`20261102111516_AddLdapAccountApproval` 的增量脚本已生成并纳入仓库：`deploy/database/upgrade-add-ldap-account-approval.sql`。目标 schema `SAJET`，起点 `20261030100000_AddMenuLauncherMetadata`，终点 `20261102111516_AddLdapAccountApproval`，采用 `--session-agnostic`，执行前由 DBA 在同一会话确认库、登录账号与当前版本。

只在该迁移代码未变化时复用这份文件；迁移文件、目标 schema 或起点版本任何一项变化都要重新生成，不能机械重发。重新生成命令：

```bash
cd /Users/luxshare-ict/Desktop/project/ZX.Platform
FROM_MIGRATION='20261030100000_AddMenuLauncherMetadata'
TO_MIGRATION='20261102111516_AddLdapAccountApproval'

dotnet ef migrations script "$FROM_MIGRATION" "$TO_MIGRATION" \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/ldap-upgrade.generated.sql

python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/ldap-upgrade.generated.sql \
  deploy/database/upgrade-add-ldap-account-approval.sql \
  SAJET --session-agnostic \
  --from-migration "$FROM_MIGRATION" \
  --to-migration "$TO_MIGRATION"
```

执行前必须核对的三点：

- 脚本按 `FROM_MIGRATION` 校验迁移历史，当前版本不符会直接 `RAISE_APPLICATION_ERROR(-20003)` 停止，不要改这段校验来绕过。
- 会先重命名账号唯一索引为 `UX_ZID_USER_ACCOUNT`。该索引不存在或已被改名时，说明库不在预期状态，停止并核实，不要手工补建。
- 迁移不删除任何表或列，只加列、加索引、加 CHECK 约束并放宽 `PASSWORD_HASH` 可空；存量用户按 `LOCAL / Approved / SECURITY_VERSION=1` 补齐，密码哈希、角色和 SBU 不动。

执行后重新查询迁移历史，应出现 `20261102111516_AddLdapAccountApproval`；并确认 `ZX_IDENTITY_USER` 上有四个 `CK_ZID_USER_*` 约束与四个新索引。升级会让已存在的登录会话在刷新时失效，用户需重新登录一次，属于预期行为。

## 审批审计表（已生成）

`20261102144318_AddUserApprovalAudit` 的增量脚本：`deploy/database/upgrade-add-user-approval-audit.sql`。目标 schema `SAJET`，起点 `20261102111516_AddLdapAccountApproval`，终点 `20261102144318_AddUserApprovalAudit`，同样采用 `--session-agnostic`。

这条迁移只建 `ZX_IDENTITY_USER_APPROVAL_AUDIT` 和两个索引，不改任何既有表或行，可以在 OA 登录已上线后单独执行。前置校验会要求迁移历史停在 `20261102111516`；如果目标库还没执行上一条，先执行账号审批脚本。重新生成时把 `FROM_MIGRATION` / `TO_MIGRATION` 换成上面两个版本，输出文件名改为 `upgrade-add-user-approval-audit.sql`。

执行后确认表与索引存在，并抽查一条预开通或审批记录能写入 `ACTION`、`FROM_STATUS`、`TO_STATUS`、`ACTOR_USER_ID` 和 `TRACE_ID`。审计表只追加，运维不应手工 `UPDATE` 或 `DELETE`；需要清理只能按保留期整批归档。

## 审计快照容量（已生成）

`20261102150741_WidenApprovalAuditSnapshot` 的增量脚本：`deploy/database/upgrade-widen-approval-audit-snapshot.sql`。目标 schema `SAJET`，起点 `20261102144318_AddUserApprovalAudit`，终点 `20261102150741_WidenApprovalAuditSnapshot`。

`ROLE_IDS` / `SBU_IDS` 存逗号分隔的 32 位 GUID，61 个不同 GUID 拼接后为 2012 字符，超过原 `NVARCHAR2(2000)`，会让一次合法审批因为写审计而整笔失败。**Oracle 不允许把字符列直接 `MODIFY` 成 `CLOB`（ORA-22858）**，所以迁移按“新增 `*_CLOB` 列 → `TO_CLOB` 复制原值 → 删除旧列 → `RENAME COLUMN` 改名”执行。复制而不是截断，既有审计证据原样保留；列顺序会变化，但所有访问都按列名，不影响应用。

**执行前提（2026-11-03 补充）：迁移期间审计表必须没有并发写入。**当前脚本的复制、删列和改名不是一个原子操作；Oracle DDL 会隐式提交。复制后新增的审批记录可能没有写入临时 CLOB 列，删除旧列会丢失这些记录的快照。本轮保留现有迁移与 SQL 实现，以以下发布顺序控制该风险：

1. 首次上线 OA 功能时，在启动支持 OA 的新版 Identity 前完成三条增量迁移。按实际迁移历史依次执行账号审批、审批审计、快照扩容脚本；先确认目标库确实没有支持 OA 审批的运行实例或其他审计表写入者。
2. 若目标库已经有 OA 审批功能在运行，先停止全部相关 Identity 实例及其他写入者，等待在途审批、预开通事务结束。仅关闭一个页面或停止一个节点不足以满足停写条件。涉及该表的回退同样需要停写。
3. 确认服务、schema、起点版本，备份审计表，并保存迁移前行数及逐行 `ROLE_IDS` / `SBU_IDS` 快照。
4. 执行扩容脚本；失败时保持停写，核对实际列与迁移历史，按已完成步骤处理，不能直接重跑或依赖事务回滚撤销全部 DDL。
5. 核对终点版本为 `20261102150741_WidenApprovalAuditSnapshot`、两列为 `CLOB`、行数和既有快照内容与备份一致，再启动相应应用版本、恢复审批与预开通入口，并验收一条完整写入流程。

若不能保证整个窗口无写入，应先安排维护窗口或另行设计支持并发写入的迁移方式；本脚本不支持在线并发审批扩容。此项操作约束属于本次发布要求，其他延期事项见[实施方案 10.7 节](oa-ldap-login-implementation.md#107-本轮延期决定与联调安排2026-11-03)。

- 前置校验要求迁移历史停在 `20261102144318`；未执行审计表脚本时先执行上一条。
- 回退（`Down`）会先检查是否有超过 2000 字符的快照，有则 `RAISE_APPLICATION_ERROR(-20010)` 停止，不会截断数据。
- 执行后确认 `ROLE_IDS`、`SBU_IDS` 的 `DATA_TYPE` 为 `CLOB`；本地 Oracle 19c 已用 SQL 探针插入 2012 字符并回滚，并用 EF 以 80 个角色 + 80 个 SBU（各 2639 字符）写入后读回一致，探针数据已删除。

## 常见错误

| 现象 | 原因与处理 |
| --- | --- |
| `ORA-50201`，目标 `127.0.0.1:1` | 忘传 `--connection`，EF 使用设计时占位连接；指定真实数据库连接后重试。 |
| `sed: .env.identity.local: No such file`、`Unable to retrieve project metadata` | 当前在 `ZX.Platform.Identity.Api` 子目录；先 `cd` 到 `ZX.Platform` 根目录。 |
| 显示 `Pending` | 迁移文件存在，但该数据库尚未执行；按环境选择本机/测试库更新或正式库 SQL。 |
| 显示 `Done` | 当前命令执行成功；仍要核对迁移历史和业务功能。 |
| `ORA-01031` | 执行账号缺少目标 schema 的 DDL 权限；找测试库管理员或正式库 DBA 核对，不给正式应用账号临时加建表权限。 |

不要把连接密码、`dotnet user-secrets list` 输出或带密码的终端截图发到群里。连接字符串中的 IP、端口和服务名只是目标数据库位置；本机为 `127.0.0.1:1521/FREEPDB1`，测试/正式库按各自实际地址填写。

## 待业务审批 OA 登录（2026-11-03，新增未执行）

新迁移 `20261103090000_AllowPendingLdapLogin`，起点 `20261102150741_WidenApprovalAuditSnapshot`，交付脚本 `deploy/database/upgrade-allow-pending-ldap-login.sql`（SAJET）。只替换 `CK_ZID_USER_LDAP_ENABLED`，允许 Pending 启用，继续禁止 Rejected 启用。不更新任何用户行、角色、SBU 或菜单。

1. 核对目标库、schema、迁移历史与备份；若尚未完成旧迁移，按历史顺序补齐，不能跳过审计 CLOB 扩容。
2. 停止全部 Identity 实例及直接写入者，等在途请求结束。Oracle DROP / ADD 约束会隐式提交，替换期间不能有写入。
3. 由部署流程/DBA 执行此增量脚本或等效 EF 升级。检查新 CHECK 已 ENABLED / VALIDATED、历史终点正确，用户启停用状态及关联未变化。
4. 统一部署新后端及匹配的前端后再启动。启动权限/菜单初始化不承担迁移，也不批量开通用户；旧前端把 Pending 当作未登录，旧后端拒绝 Pending 的 me/刷新，不能混跑。
5. 存量 Pending / Disabled 保留禁用；业务确认后用启用接口逐个恢复（仍无业务权限），或先审批再显式启用。Rejected 不能直接启用，需显式重新审核。
6. 验收新 OA 首次登录、无业务菜单、直调业务接口 403、伪造 SBU 拒绝、刷新/退出，以及批准/拒绝后旧会话撤销。回退需先停写；Down 在有启用 Pending 时会报 `ORA-20011`，须人工制定兼容处置，不能直接执行旧应用或自动禁用账号。

本轮只交付迁移与脚本，未对本地或目标 Oracle 执行。已有历史迁移不修改，后续仅在模型改变时生成新的迁移。
