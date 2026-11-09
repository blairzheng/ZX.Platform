# 部署目录说明

后端成品优先通过 [GitHub 手动打包](../../docs/github-release.md)生成，内网部署统一使用 [GitLab Runner 发布文档](../../docs/deployment.md)，拓扑见[当前部署拓扑](../../docs/deployment-topology.md)。原有本地 `bash scripts/publish-images.sh <新版本号>` 构建上传入口继续保留，内网 Runner 通过 `.gitlab-ci.yml` 完成校验、发布和回滚。

本目录旧 `package.sh`、`remote.sh`、`deploy.sh` 及 Python 部署入口保留为历史实现，不属于当前发布流程；旧包格式与 GitLab `mom-images` 包格式不同，不要混用。当前发布不需要在服务器安装 Python 或维护旧 `/etc/mom` 配置。

数据库导出工具仍在本目录，供 GitHub 打包流水线或开发机离线生成 SQL：

- [增量迁移与 DBA 交付](../../docs/migration-operations.md)。
- [首次初始化、管理员与重建](../../docs/database-initialization.md)。

内网部署 Runner 不执行 SQL，正式库数据库升级仍由 DBA 单独完成。

业务成员按 [业务开发步骤](../../docs/02-business.md) 生成迁移并更新测试库；正式发布与 DBA 交付由负责人统筹。

当前正式库的表 owner 为 `ZX_IDENTITY`。后端发布脚本及 GitHub Actions 默认导出至该 schema；显式传入 `--schema` 或设置 GitHub Actions Variable `MOM_IDENTITY_DB_SCHEMA` 可以覆盖，覆盖值必须与目标环境真实表 owner 一致。表、索引及索引目标表均带目标 schema。

DBA 规范要求应用表和迁移历史表均具有 `CREATE_TIME DATE DEFAULT SYSDATE NOT NULL`，列名全部大写。历史表使用 `MIGRATION_ID`、`PRODUCT_VERSION`；原来的 UTC 业务审计字段保留，`CREATE_TIME` 使用数据库服务器时间，存量记录补列后是变更执行时间。

发布包的 `verify.sql` 从当前 EF 模型离线提取精确表清单，并加入迁移历史表；只核对此清单中的表，不按 `ZX_IDENTITY_` 前缀扫描整个 schema。因此 `_BAK` 等备份表及其他系统的表不会被误判，后续新增的业务表无论是否使用该前缀都会随模型自动纳入。无法解析模型表清单时停止打包，不生成可能漏项的核对 SQL。

新版数据库升级包的完整 `upgrade.sql` 会先检查数据库原有版本及历史表结构，再兼容旧的 `"MigrationId"`、`"ProductVersion"`，最后执行迁移。历史列转换和历史表补列不重写原有迁移 ID 或产品版本。不要只从脚本中选取部分语句执行。

已有测试库需要单独通过新版 EF 更新时，先离线生成历史兼容脚本，将 `实际SCHEMA` 替换为实际表 owner：

```bash
python3 deploy/docker/export-history-compatibility-sql.py output/history-compatibility.sql 实际SCHEMA
```

可加 `--expected-migration 完整迁移ID` 在首条 DDL 前校验当前最后一条版本。生成过程不连接数据库。由负责人/DBA停用使用该 schema 的 Identity 实例后审核执行，成功后再按业务开发文档执行 EF 更新。兼容脚本只处理迁移历史表，不代替业务表的新增列迁移。脚本可识别已经转换的列，结构异常会停止；Oracle DDL 隐式提交，部分失败时先检查现场。
