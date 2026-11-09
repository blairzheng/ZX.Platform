# 部署工具

团队版保留这些工具供负责人维护。业务成员按 [业务开发步骤](../../docs/02-business.md) 生成迁移并更新测试库，无需参与镜像打包和正式环境发布。

部署、DBA 交付及初始化的完整说明保留在主仓库 main 分支，由负责人统筹。本目录不作为成员的开发操作入口。

当前正式库的表 owner 为 `ZX_IDENTITY`。后端发布脚本及 GitHub Actions 默认导出至该 schema；显式传入 `--schema` 或设置 GitHub Actions Variable `MOM_IDENTITY_DB_SCHEMA` 可以覆盖，覆盖值必须与目标环境真实表 owner 一致。表、索引及索引目标表均带目标 schema。

DBA 规范要求应用表和迁移历史表均具有 `CREATE_TIME DATE DEFAULT SYSDATE NOT NULL`，列名全部大写。历史表使用 `MIGRATION_ID`、`PRODUCT_VERSION`；原来的 UTC 业务审计字段保留，`CREATE_TIME` 使用数据库服务器时间，存量记录补列后是变更执行时间。

发布包的 `verify.sql` 从当前 EF 模型离线提取精确表清单，并加入迁移历史表；只核对此清单中的表，不按 `ZX_IDENTITY_` 前缀扫描整个 schema。因此 `_BAK` 等备份表及其他系统的表不会被误判，后续新增的业务表无论是否使用该前缀都会随模型自动纳入。无法解析模型表清单时停止打包，不生成可能漏项的核对 SQL。

新版数据库升级包的完整 `upgrade.sql` 会先检查数据库原有版本及历史表结构，再兼容旧的 `"MigrationId"`、`"ProductVersion"`，最后执行迁移。历史列转换和历史表补列不重写原有迁移 ID 或产品版本。不要只从脚本中选取部分语句执行。

已有测试库需要单独通过新版 EF 更新时，先离线生成历史兼容脚本，将 `实际SCHEMA` 替换为实际表 owner：

```bash
python3 deploy/docker/export-history-compatibility-sql.py output/history-compatibility.sql 实际SCHEMA
```

可加 `--expected-migration 完整迁移ID` 在首条 DDL 前校验当前最后一条版本。生成过程不连接数据库。由负责人/DBA停用使用该 schema 的 Identity 实例后审核执行，成功后再按业务开发文档执行 EF 更新。兼容脚本只处理迁移历史表，不代替业务表的新增列迁移。脚本可识别已经转换的列，结构异常会停止；Oracle DDL 隐式提交，部分失败时先检查现场。
