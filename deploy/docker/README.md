# 部署工具

团队版保留这些工具供负责人维护。业务成员按 [业务开发步骤](../../docs/02-business.md) 生成迁移并更新测试库，无需参与镜像打包和正式环境发布。

部署、DBA 交付及初始化的完整说明保留在主仓库 main 分支，由负责人统筹。本目录不作为成员的开发操作入口。

当前发布源码统一使用 `team`：审核后将同一个本地 `team` 分别推送至 GitLab 和 GitHub 的 `team`，不再为打包合并至 `main`，也不将 `main` 的独有文档合回 `team`。前后端仓库均按此流程操作，发布前确认两个远程的 `team` 指向各自同一提交。

## 本地生成后端发布包

GitHub 下载较慢时，负责人可以直接在本机 `ZX.Platform` 的 `team` 分支运行：

```bash
python3 scripts/package-local.py
```

按提示输入 **正式库最后一条完整 MigrationId**。脚本自动生成含 UTC 时间和提交号的新版本，依次还原固定 EF 工具、编译 Release、执行与 Actions 相同的离线检查，再复用 `package-release.py` 构建 `linux/amd64` 镜像和数据库增量 SQL。不会连接或更新正式库。

本机需要 .NET 10.0 SDK、Python 3.10+、Git、Bash、tar、gzip、shasum 和已启动且支持 buildx 的 Docker；还原依赖及拉取基础镜像需要能访问相应源。配套前端源码默认位于相邻 `ZX.front.vue`，用于自动生成 AI 源码资料；其他位置通过 `MOM_ASSISTANT_FRONTEND_DIR` 指定。本入口生成后端发布包，前端静态包仍使用前端自己的打包入口。

也可直接传入迁移 ID；`完整迁移ID` 替换为 DBA 查询结果：

```bash
python3 scripts/package-local.py --from-migration 完整迁移ID
```

可选 `--release 本次新版本`、`--schema 实际SCHEMA`，默认 schema 为 `ZX_IDENTITY`。起点不在当前迁移链、模型还有未生成的迁移或任一步失败时停止；同版本输出不覆盖，重试时不传 `--release` 会生成新版本。本地打包不自动拉取或切换源码；使用当前工作区，后端和配套前端的提交号及未提交状态都会记录在 `release.json`。发布前核对代码，打包期间保持工作区稳定。

产物位于 `output/mom-release/本次版本/`，包含镜像 `tar.gz`、数据库 `zip`、SHA256、`release.json`、`README.md` 和独立 `upload.sh`。直接复制终端最后输出的 `bash .../upload.sh` 命令，按提示输入 Deploy Token 上传镜像；无需先传到 GitHub。上传成功后，在内网 `mom_img` 新建流水线，填写脚本输出的四项参数。数据库包交 DBA；无待迁移时包内标明无需升级，有变更时先确认 DBA 执行成功，再部署对应镜像。打包和上传均不自动发布。

## GitHub Actions 入口

GitHub 打开 **Actions → 生成后端发布包 → Run workflow**，将 **Use workflow from** 选择为 `team`；`from_migration` 填 DBA 从正式库查到的最后一条完整迁移 ID。`frontend_ref` 显式填写 `team`，或填写本次配套前端的完整提交 SHA；页面可能仍显示默认分支旧工作流中的 `main` 默认值，运行前必须核对。后端工作流只允许 `team` 打包，AI 资料默认也读取前端 `team`。推送分支不会自动打包，打包、上传及部署仍分别手动执行。

## 数据库交付约定

当前正式库的表 owner 为 `ZX_IDENTITY`。后端发布脚本及 GitHub Actions 默认导出至该 schema；显式传入 `--schema` 或设置 GitHub Actions Variable `MOM_IDENTITY_DB_SCHEMA` 可以覆盖，覆盖值必须与目标环境真实表 owner 一致。表、索引及索引目标表均带目标 schema。

DBA 规范要求应用表和迁移历史表均具有 `CREATE_TIME DATE DEFAULT SYSDATE NOT NULL`，列名全部大写。历史表使用 `MIGRATION_ID`、`PRODUCT_VERSION`；原来的 UTC 业务审计字段保留，`CREATE_TIME` 使用数据库服务器时间，存量记录补列后是变更执行时间。

发布包的 `verify.sql` 从当前 EF 模型离线提取精确表清单，并加入迁移历史表；只核对此清单中的表，不按 `ZX_IDENTITY_` 前缀扫描整个 schema。因此 `_BAK` 等备份表及其他系统的表不会被误判，后续新增的业务表无论是否使用该前缀都会随模型自动纳入。无法解析模型表清单时停止打包，不生成可能漏项的核对 SQL。

新版数据库升级包的完整 `upgrade.sql` 会先检查数据库原有版本及历史表结构，再兼容旧的 `"MigrationId"`、`"ProductVersion"`，最后执行迁移。历史列转换和历史表补列不重写原有迁移 ID 或产品版本。不要只从脚本中选取部分语句执行。

已有测试库需要单独通过新版 EF 更新时，先离线生成历史兼容脚本，将 `实际SCHEMA` 替换为实际表 owner：

```bash
python3 deploy/docker/export-history-compatibility-sql.py output/history-compatibility.sql 实际SCHEMA
```

可加 `--expected-migration 完整迁移ID` 在首条 DDL 前校验当前最后一条版本。生成过程不连接数据库。由负责人/DBA停用使用该 schema 的 Identity 实例后审核执行，成功后再按业务开发文档执行 EF 更新。兼容脚本只处理迁移历史表，不代替业务表的新增列迁移。脚本可识别已经转换的列，结构异常会停止；Oracle DDL 隐式提交，部分失败时先检查现场。
