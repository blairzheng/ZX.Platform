# 部署目录说明

后端成品优先通过 [GitHub 手动打包](../../docs/github-release.md)生成，内网部署统一使用 [GitLab Runner 发布文档](../../docs/deployment.md)，拓扑见[当前部署拓扑](../../docs/deployment-topology.md)。原有本地 `bash scripts/publish-images.sh <新版本号>` 构建上传入口继续保留，内网 Runner 通过 `.gitlab-ci.yml` 完成校验、发布和回滚。

本目录旧 `package.sh`、`remote.sh`、`deploy.sh` 及 Python 部署入口保留为历史实现，不属于当前发布流程；旧包格式与 GitLab `mom-images` 包格式不同，不要混用。当前发布不需要在服务器安装 Python 或维护旧 `/etc/mom` 配置。

数据库导出工具仍在本目录，供 GitHub 打包流水线或开发机离线生成 SQL：

- [增量迁移与 DBA 交付](../../docs/migration-operations.md)。
- [首次初始化、管理员与重建](../../docs/database-initialization.md)。

内网部署 Runner 不执行 SQL，正式库数据库升级仍由 DBA 单独完成。
