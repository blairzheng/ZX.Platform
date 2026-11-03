# GitHub 手动生成后端发布包

[文档导航](README.md) · [内网发布与回滚](deployment.md) · [迁移操作](migration-operations.md)

成员提交业务代码、迁移及 snapshot，你审核合入 `main` 后，在 GitHub 点击一次打包。表单只填写 DBA 查到的正式库完整 `MigrationId`。流水线从本次 `main` 的同一源码提交生成 Identity / Gateway 镜像和数据库升级包。

GitHub 负责构建和生成 SQL；正式库 SQL 由 DBA 执行，服务器部署继续使用内网 `mom_img`。本流程不需要把数据库、Redis、LDAP 或内网 GitLab 的密码提供给 GitHub。

## 一次性准备

1. 将 `.github/workflows/package-release.yml`、`.config/dotnet-tools.json` 和相关脚本提交推送到 GitHub 的 `main`，仓库启用 GitHub Actions。
2. 核对正式库表 owner。默认是 `SAJET`；不同时，在 GitHub **Settings → Secrets and variables → Actions → Variables** 设置仓库变量 `MOM_IDENTITY_DB_SCHEMA`，填写实际大写 schema，不是连接账号。
3. `main` 的写入和合并权限通过 GitHub 分支规则管理。本工作流拒绝从其他分支打包，但不代替仓库权限设置。

不需要配置正式库连接或上传 Token。Actions 产物保留 30 天，正式发布包应同时归档到公司存储。

## 每次发布

### 1. 获取正式库当前版本

让 DBA 在正确的正式库执行，`SAJET` 替换为实际表 owner：

```sql
SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

取最后一条完整 ID，例如 `20261102150741_WidenApprovalAuditSnapshot`。不要填应用版本号、只填时间戳，或拿测试库版本代替。正式库没有历史表时，走[首次初始化](database-initialization.md)，本流程不处理空库。

### 2. 点击生成发布包

打开 GitHub 仓库 **Actions → 生成后端发布包 → Run workflow**：

- 分支选 `main`。
- 唯一输入 `from_migration` 填上一步完整 ID。
- 点击 **Run workflow**，等待全部成功。

版本号自动生成，例如 `20261103-gh12.1-a1b2c3d4e5f6`，包含运行编号、重试次数及源码提交摘要。不覆盖旧版本，不使用 `latest`。

流水线会编译 Identity、检查模型与 snapshot 是否一致、离线读取代码迁移链。输入的版本不在迁移链中或模型漏了迁移时停止，不生成可交付发布包。输入等于最新迁移时，继续打镜像，数据库包明确标记“无数据库变更”，不生成升级 SQL。

流水线没有连接正式库；填错了一个“确实存在但不是正式库当前版本”的 ID，打包阶段无法知道，必须由 DBA 核对，执行时的起点版本守卫也会拒绝不匹配的数据库。

### 3. 下载并交付

打开成功的运行记录，在 **Artifacts** 下载 `mom-release-<版本>` 并解压：

| 文件 | 用途 |
| --- | --- |
| `mom-images-<版本>.tar.gz` / `.sha256` | 与现有内网发布流程兼容的 Identity、Gateway `linux/amd64` 镜像包及摘要 |
| `mom-database-<版本>.zip` / `.sha256` | 给 DBA 的 SQL、执行说明、起止版本、迁移清单及摘要 |
| `release.json` | 同一源码提交、镜像包摘要、数据库包摘要、schema 与起止版本 |
| `README.md` | 本次发布说明、上传命令、GitLab 发布参数 |

数据库包内只有替换成目标 schema 的最终 `upgrade.sql`，不交付 `ZX_IDENTITY` 原始中间 SQL。没有变更时只有标记文件、版本信息与只读核对 SQL，无需 DBA 改表。

先审核 SQL 中的删表删列、约束、索引和数据回填，再将数据库 ZIP 交给 DBA。脚本包含起点版本守卫和迁移历史更新；DBA 核对完整历史、目标环境、数据影响后安排停写执行。Oracle DDL 隐式提交，失败后检查现场，不直接重跑或手工补历史。

### 4. 上传并部署镜像

把下载的 `mom-images-<版本>.tar.gz` 放到本机 `ZX.Platform/output/`，按本次包内 README 的命令上传：

```bash
bash scripts/publish-images.sh --upload-only <本次版本>
```

该步骤只上传已经构建的包，不重新构建镜像，无需启动本机 Docker。输入现有内网 Package Registry 的 Deploy Token，上传成功后，在 `mom_img` 新建流水线，复制包内 README 的 `MOM_COMPONENT`、`MOM_ACTION`、`MOM_RELEASE` 和 `MOM_PACKAGE_SHA256`。

有数据库变更时，先确认 DBA 执行成功且数据库达到包内目标版本，再点击 `deploy_mom`。两台共用数据库，SQL 只执行一次。部署、验证、回滚继续按[内网发布文档](deployment.md#3-新建流水线并发布)。

GitHub 执行机不承担内网 GitLab 上传；使用本机作为文件中转，沿用现有部署权限。

## 本机排查打包问题

在后端仓库根目录执行。仅最后一条命令中的 `--database-only` 表示不构建镜像，其余行为与 Actions 的数据库导出相同；全程不执行数据库更新：

```bash
dotnet tool restore
dotnet build Services/Identity/ZX.Platform.Identity.Api --configuration Release
python3 scripts/package-release.py \
  --release local-db-check-01 \
  --from-migration 20261102150741_WidenApprovalAuditSnapshot \
  --schema SAJET \
  --database-only
```

正式库起点和本机排查版本都应替换成实际值。同版本输出目录已存在时脚本拒绝覆盖，换一个版本号。输出位于 Git 忽略的 `output/mom-release/<版本>/`。

新增迁移仍由成员执行 `migrations add` 并提交，见[迁移操作](migration-operations.md#每次改表的顺序)。发布人不重复生成迁移。当前流水线只打包后端；前端继续在独立仓库发布。
