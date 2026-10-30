# 部署目录说明

当前日常发布统一使用 [GitLab Runner 发布文档](../../docs/deployment.md)，拓扑见[当前部署拓扑](../../docs/deployment-topology.md)。在后端根目录执行 `bash scripts/publish-images.sh <新版本号>` 构建并上传成品，Runner 通过 `.gitlab-ci.yml` 完成校验、发布和回滚。

本目录旧 `package.sh`、`remote.sh`、`deploy.sh` 及 Python 部署入口保留为历史实现，不属于当前发布流程；旧包格式与 GitLab `mom-images` 包格式不同，不要混用。当前发布不需要在服务器安装 Python 或维护旧 `/etc/mom` 配置。

数据库导出工具仍在本目录，供开发机生成 SQL：

- [增量迁移与 DBA 交付](../../docs/migration-operations.md)。
- [首次初始化、管理员与重建](../../docs/database-initialization.md)。

SQL 工具不由 Runner 执行，数据库升级始终由操作者或 DBA 单独完成。
