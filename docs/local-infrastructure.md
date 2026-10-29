# macOS 本机 Oracle 与 Redis

[开发环境](development.md) · [文档导航](README.md)

此配置用于 Apple Silicon Mac 的本机开发。Docker Engine 由 Colima 提供，无需另外安装 Docker Desktop；已安装 Docker CLI 和 Compose 时，启动 Colima 即可使用 Docker 命令。

## 一起启动和检查

当前电脑已完成镜像下载和首次初始化。在后端仓库根目录执行：

```bash
colima start
docker --context colima compose --env-file .env.oracle.local -f compose.redis.yml -f compose.oracle.yml up -d --wait --wait-timeout 300
docker --context colima compose --env-file .env.oracle.local -f compose.redis.yml -f compose.oracle.yml ps
```

两项服务均应显示 `healthy`。同时传入两个 Compose 文件可以管理整套本机服务，避免单独操作时将另一项服务提示为 orphan（孤立容器）。

## Docker 引擎

当前本机虚拟机配置为 Arm64、4 核、6 GB 内存、60 GB 按需增长的数据磁盘。

```bash
colima start
docker --context colima info
```

首次下载虚拟机或镜像需要可用网络。若使用本机代理，启动时可按实际地址设置 HTTP_PROXY / HTTPS_PROXY；不要把其他电脑的代理端口直接复制过来。

## Redis

在后端仓库根目录执行：

```bash
docker --context colima compose -f compose.redis.yml up -d
docker --context colima compose -f compose.redis.yml ps
docker --context colima compose -f compose.redis.yml exec -T redis redis-cli ping
```

项目使用官方 `redis:7.4-alpine`，地址为 `127.0.0.1:6379`，启用 AOF，每秒刷盘；数据保存在 `identity-redis` 命名卷。

## Oracle

使用 Oracle 官方 `container-registry.oracle.com/database/free:23.26.3.0-lite-arm64`（Oracle Database 26ai Free 精简版），原生运行于 Arm64。容器内存上限 4 GB。

首次启动前，在仓库根目录的 `.env.oracle.local` 中设置 `ORACLE_PWD`。当前电脑已生成随机密码文件，权限为 `0600`；该文件由 Git 忽略，不包含在克隆或提交中。不要将真实密码写入本说明。更改环境文件不会重置已有数据库卷中的账号密码。

```bash
docker --context colima compose --env-file .env.oracle.local -f compose.oracle.yml up -d
docker --context colima compose --env-file .env.oracle.local -f compose.oracle.yml ps
docker --context colima compose --env-file .env.oracle.local -f compose.oracle.yml logs --tail 60 oracle
```

连接参数：主机 `127.0.0.1`、端口 `1521`、PDB 服务名 `FREEPDB1`；容器数据库服务名为 `FREE`。管理员 `SYSTEM` 的初始密码来自本机环境文件。应用已使用独立 schema `ZX_IDENTITY`，默认表空间 `ZX_IDENTITY_DATA`，配额 200 MB，仅授予 CREATE SESSION / CREATE TABLE / CREATE SEQUENCE。

Oracle 数据保存在 `identity-oracle` 命名卷，首次初始化需要等待。镜像有健康检查，必须等数据库就绪后再连接。Oracle Free 的功能和资源限制以官方说明为准。

## 停止和恢复

只暂停服务并保留数据：

```bash
docker --context colima compose --env-file .env.oracle.local -f compose.redis.yml -f compose.oracle.yml stop
colima stop
```

恢复时执行文首的一起启动命令。数据卷不是备份；不要执行 `down -v`、删除数据卷或 `colima delete` 来处理普通连接问题。

## 后端 Oracle 配置

Identity 使用单一 Oracle 19c 模型，迁移基线为 `20261027000000_InitialIdentitySchema`，当前目标为 `20261028084818_ReplaceFactoriesWithSbus`。本机 Oracle Free 引擎无需更换；已有库可执行增量迁移，旧厂区测试数据会丢弃。需要清空全部 MOM 数据时才按 [部署文档第 7 节](docker-deployment.md#7-oracle-19c-首次初始化或清空重建)重建。

当前电脑的应用连接已写入 Identity API 的 Development User Secrets，VS Code 的 Identity API / Identity API + Gateway 启动配置会自动读取。schema 密码另存于仓库根目录 `.env.identity.local`（0600，Git 忽略），与 SYSTEM 管理员密码分别管理，不要将它提交到仓库。

本机全新数据库的首个登录用户由本机保存、仓库外的一次性 SQL 创建；Identity 启动时只补齐权限目录与默认菜单。详见[首个管理员初始化](authorization.md#首个管理员)。

换电脑时需重建 schema、注入自己的连接并应用 Oracle 迁移；本机 User Secrets 和密码文件不会随 Git 克隆。数据库配置示例及迁移命令见[开发环境](development.md#数据库配置)和[数据库专题](database.md#迁移命令)。

首次创建 schema 时，以管理员进入本镜像的 PDB（当前电脑已经完成，无需重复执行）：

```bash
docker --context colima compose --env-file .env.oracle.local -f compose.oracle.yml exec oracle sqlplus / as sysdba
```

```sql
ALTER SESSION SET CONTAINER = FREEPDB1;
CREATE TABLESPACE ZX_IDENTITY_DATA
  DATAFILE '/opt/oracle/oradata/FREE/FREEPDB1/zx_identity_data01.dbf'
  SIZE 100M AUTOEXTEND ON NEXT 50M MAXSIZE 1G;
CREATE USER ZX_IDENTITY IDENTIFIED BY "<自行设置的随机密码>"
  DEFAULT TABLESPACE ZX_IDENTITY_DATA QUOTA 200M ON ZX_IDENTITY_DATA;
GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE TO ZX_IDENTITY;
```

示例数据文件路径仅适用于这里的 Oracle Free 镜像。应用连接使用这个 schema 的密码，SYSTEM 密码仅供数据库管理。

旧库数据未保留，本次从新空库开始，容器不会自动恢复旧用户、角色、菜单或密码。Redis 继续承担认证会话和权限缓存。

官方容器说明：[Oracle Database container images](https://github.com/oracle/docker-images/tree/main/OracleDatabase/SingleInstance)。
