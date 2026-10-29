# 项目交接

[文档导航](README.md) · [开发环境](development.md) · [Docker 部署](docker-deployment.md)

## 当前数据库方案

2026-10-28：所有环境统一采用 Oracle 19c 模型。只维护 `PlatformIdentityDbContext` 和 `Persistence/Migrations/`。基线为 `20261027000000_InitialIdentitySchema`，当前目标版本为 `20261028084818_ReplaceFactoriesWithSbus`；后者以 SBU 表替换旧厂区表，旧厂区测试数据不迁移。布尔字段为 `NUMBER(1)`，迁移历史表为目标 schema 下的 `ZX_IDENTITY_MIGRATIONS`。

本地默认 schema 为 `ZX_IDENTITY`，测试库使用 `SAJET`，正式库按 DBA 提供的信息配置。连接账号与表 owner 分开配置，同一套镜像和迁移用于所有环境。设计时工厂离线生成 SQL，导出工具替换目标 schema 并检查执行服务和账号。

首次部署先执行数据库结构初始化，再由运维执行仓库外保存的一次性 SQL 创建首个管理员，之后启动服务同步权限目录和默认菜单。应用不从启动配置创建账号或管理员。后续账号和角色通过后台管理；数据库数据允许清空重建时，使用只处理九张 MOM 表的定向重置脚本。

## 部署和开发入口

- [Docker 部署手册](docker-deployment.md)：本地构建 linux/amd64 镜像、save、上传、load、配置、Oracle 初始化及增量迁移、回滚。
- [开发环境](development.md)：SDK、User Secrets、Identity / Gateway 启动。
- [本机基础设施](local-infrastructure.md)：Colima、Oracle Free、Redis。
- [数据库](database.md)：实体、唯一索引、软删除、UTC、事务及迁移。
- [角色权限](authorization.md)：本机管理员 SQL、权限缓存和后台维护。

176 承载 Nginx 入口，177、178 部署相同的 Identity 和 Gateway；Oracle、Redis Sentinel、JWT 密钥及环境配置应一致。后端不在启动时执行数据库 DDL，SQL 由操作者或 DBA 执行一次。修改源码后需重新打包镜像，不能复用之前的离线包。

## 验证边界

编译和离线模型 / SQL 检查不能代替目标数据库验收。实际清空、DDL、首个管理员、登录和双节点访问按部署手册逐项确认。初始密码只以 BCrypt 哈希写入操作者本地的初始化 SQL，不进入代码管理或发布包。
