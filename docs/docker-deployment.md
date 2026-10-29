# MOM 后端 Docker 离线部署手册

更新日期：2026-10-28。本文按当前源码整理操作步骤，不表示服务器已完成本版本部署或验收。

日常发布推荐使用[一键离线部署脚本说明](../deploy/docker/README.md)：本机 `bash deploy/docker/package.sh <版本号>` 生成一个压缩包，再执行 `bash deploy/docker/remote.sh <发布包>`，输入两台临时 SSH 命令和隐藏输入的密码，使用系统 `ssh`、`sftp` 并行上传和部署，不下载额外依赖。首次用 `--action prepare` 建立配置模板，填写后重新申请临时凭据执行部署；可先用 `--action probe` 只测试两台文件传输和连接复用。当前按用户确认先部署后端、允许两台同时中断，暂不连接 176 或操作 Nginx。数据库第 7/8 节仍由 DBA 独立执行。以下完整手工流程保留后续 Nginx、逐台发布步骤供参考；前端和 Nginx 并不因此视为已部署。同次发布选择脚本或手工一种方式，不要交叉替换容器，以免失去脚本的回滚状态。

本文用于手动部署 MOM 后端。目标拓扑如下：

- `176`：现有 Nginx 和前端，转发 `/api` 到 `177`、`178`。
- `177`：`Gateway` 和 `Identity` 两个 Docker 容器。
- `178`：`Gateway` 和 `Identity` 两个 Docker 容器。
- Redis：沿用服务器已有的 Redis/Sentinel，不把 Redis 打进 MOM 后端镜像。
- Oracle：由 Identity 连接；数据库初始化和升级 SQL 在数据库工具或 DBA 环境执行。

本文假设服务器没有外网，镜像在本地构建后通过压缩包或文件上传到服务器。

先区分三种操作，避免每次发布都重新初始化数据库：

| 场景 | 数据库操作 | 应用操作 |
| --- | --- | --- |
| 新环境首次部署 | DBA 创建 schema、赋予权限并执行数据库结构 SQL；运维执行本机保存的管理员 SQL | 启动并验证 177、178 |
| 修改表、字段、索引 | 开发库验证 EF 迁移，再导出目标库当前版本到新版本的增量 SQL，由 DBA 执行一次 | 发布对应版本镜像 |
| 只改业务代码或连接配置 | 无表结构变化就不生成迁移；更换数据库时另行确认目标库已初始化 | 代码变化重新构建镜像；配置变化重建对应容器 |

镜像只携带程序。数据库表和账号在 Oracle 中，会话在 Redis 中；配置放在服务器 `/etc/mom/`。本机 User Secrets 不会随镜像上传。完整首次部署顺序为：构建并导入镜像 → 准备私有配置 → DBA 初始化数据库 → 运维在数据库客户端执行本机保存的管理员 SQL → 启动并验收 177、178。后续发布不再执行首次管理员 SQL。

## 1. 发布前准备

本地需要：

- Docker Desktop 或 Colima，能够执行 `docker buildx build`、`docker save`。
- 本仓库已经能正常 `dotnet build`。
- 确认服务器 CPU 架构。当前服务器按 `linux/amd64` 构建。
- 从 DBA 获取目标 Oracle 的主机、端口、服务名、登录账号、密码和 schema。
- 获取 Redis/Sentinel 的三个地址、Sentinel 服务名、账号和密码。

服务器需要：

- 已安装 Docker Engine，执行 `docker --version` 能返回版本。
- 177、178 之间能访问现有 Redis/Sentinel。
- 177、178 能访问 Oracle。
- 176 能访问 177、178 的 Gateway 端口。
- 177、178 的 `18080` 端口没有被其他服务占用，或者改成实际可用端口。

不要把数据库密码、Redis 密码、JWT 密钥写进代码、Dockerfile、镜像或 Git。

## 2. 本地构建镜像

在仓库根目录执行。下面的版本号只是示例；每次发布使用新的版本号，不要覆盖已经验收的标签。

```bash
cd /path/to/ZX.Platform

export RELEASE=20261027-01

docker buildx build \
  --platform linux/amd64 \
  --file Services/Identity/ZX.Platform.Identity.Api/Dockerfile \
  --tag mom-identity:${RELEASE} \
  --load \
  .

docker buildx build \
  --platform linux/amd64 \
  --file ZX.Platform.Gateway/Dockerfile \
  --tag mom-gateway:${RELEASE} \
  --load \
  .
```

检查镜像架构和标签：

```bash
docker image inspect mom-identity:${RELEASE} \
  --format '{{.RepoTags}} {{.Os}}/{{.Architecture}}'
docker image inspect mom-gateway:${RELEASE} \
  --format '{{.RepoTags}} {{.Os}}/{{.Architecture}}'
```

应看到 `linux/amd64`。构建时如果本机没有基础镜像，Docker 需要访问镜像仓库；服务器本身不需要外网。可以在有外网的构建机提前拉取并完成构建。

## 3. 导出离线包

将两个镜像导出为一个 Docker 镜像包。`docker save` 的输出才能在服务器上用 `docker load` 导入。

```bash
mkdir -p output/mom-deploy

docker save \
  --output output/mom-deploy/mom-backend-${RELEASE}.tar \
  mom-identity:${RELEASE} \
  mom-gateway:${RELEASE}

# macOS 使用 shasum；校验文件只记录文件名，上传后不依赖本机目录。
(
  cd output/mom-deploy
  shasum -a 256 mom-backend-${RELEASE}.tar > mom-backend-${RELEASE}.tar.sha256
)
ls -lh output/mom-deploy/mom-backend-${RELEASE}.tar
```

把 `.tar` 和 `.sha256` 一起上传到 177、178。不要上传 `.env`、User Secrets 或包含密码的文件。

Linux 构建机可将上面的 `shasum -a 256` 换成 `sha256sum`。

如果通过跳板机上传到服务器，可先放到服务器的 `/tmp`：

```text
/tmp/mom-backend-20261027-01.tar
/tmp/mom-backend-20261027-01.tar.sha256
```

## 4. 服务器导入镜像

177、178 都执行一次。以下命令不重启系统，也不会影响现有容器。

```bash
export RELEASE=20261027-01

(
  cd /tmp
  sha256sum -c mom-backend-${RELEASE}.tar.sha256
)
```

只有校验结果为 `OK` 时才继续导入；失败时重新核对或上传文件：

```bash
sudo docker load --input /tmp/mom-backend-${RELEASE}.tar

sudo docker image inspect mom-identity:${RELEASE} \
  --format '{{.RepoTags}} {{.Os}}/{{.Architecture}}'
sudo docker image inspect mom-gateway:${RELEASE} \
  --format '{{.RepoTags}} {{.Os}}/{{.Architecture}}'
```

确认校验结果为 `OK`、两个镜像标签都存在后，再创建 MOM 专用 Docker 网络。以下固定网段 `172.30.176.0/24` 是示例，先检查两台服务器的路由与已有 Docker 网络，确认没有重叠：

```bash
ip -4 route show
sudo docker network ls
sudo docker network ls --quiet | xargs -r sudo docker network inspect \
  --format '{{.Name}} {{range .IPAM.Config}}{{.Subnet}} {{end}}'
sudo docker network inspect mom-net

# 仅在 mom-net 不存在、并且示例网段可用时创建。
sudo docker network create --subnet 172.30.176.0/24 mom-net
```

若 `mom-net` 已存在，检查其 `IPAM.Config` 后使用该网络内可用的固定 IP，不删除正在使用的网络。下文 Gateway 固定为 `172.30.176.10`；若改网段，需同步修改第 5 节的可信代理地址、第 9/11 节所有 Gateway 的 `--ip` 参数。177、178 的 Docker 网络各自独立，可以使用同一网段和固定 IP。

## 5. 准备 Identity 配置

177、178 各自创建 `/etc/mom/identity.env`。目录和文件只允许 root 读取：

```bash
sudo install -d -m 700 /etc/mom
sudo touch /etc/mom/identity.env
sudo chmod 600 /etc/mom/identity.env
sudo vi /etc/mom/identity.env
```

配置模板如下。两台后端的 Redis、Oracle、JWT 和前缀必须一致；密码替换为实际值。这里是 Docker `--env-file` 格式，值不要加包裹用的单引号或双引号，也不要执行 `source`；连接字符串中的空格和分号直接保留。

```dotenv
DOTNET_ENVIRONMENT=Production
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080

# Identity 的直接上游是同一 Docker 网络中的 Gateway 固定 IP，不是宿主机回环地址。
ForwardedHeaders__KnownProxies__0=172.30.176.10
ForwardedHeaders__KnownProxies__1=::1

# Oracle 19c 测试库示例。正式库按 DBA 提供的值替换。
IdentityDatabase__Schema=SAJET
ConnectionStrings__IdentityDatabase=User Id=KS31377716;Password=填在服务器上的密码;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=10.32.15.47)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=KSMESEBDEV)));Min Pool Size=0;Max Pool Size=50;Connection Lifetime=300;

# Redis Sentinel。三个地址都写，不能固定只连 177 或 178。
ConnectionStrings__IdentityRedis=10.32.15.176:26379,10.32.15.177:26379,10.32.15.178:26379,serviceName=mom-redis,user=default,password=填在服务器上的Redis密码,connectTimeout=3000,asyncTimeout=3000
AuthenticationRedis__KeyPrefix=mom:staging:identity:auth:

# 177、178 必须完全相同，使用随机生成的至少 32 字节 Base64 密钥。
AuthenticationJwt__SigningKey=填入两台相同的随机密钥
AuthenticationJwt__Issuer=MOM.Staging.Identity
AuthenticationJwt__Audience=MOM.Staging

# 默认经 176 的 HTTPS 入口访问；内网 HTTP 测试按第 10 节同时设置 Cookie HTTP 开关。
AuthenticationCookie__Secure=true

```

### 配置说明

- 数据库统一按 Oracle 19c 运行，所有环境使用同一个模型和迁移。
- `IdentityDatabase__Schema` 是表所在 schema。测试库是 `SAJET`，正式库可以是 DBA 指定的其他值。
- 生产数据库账号不要求能建表。首次初始化和后续升级由 DBA 或有 DDL 权限的账号执行，应用账号只需要运行期读写权限。
- Redis 配置必须使用 Sentinel 服务名。应用会根据 Sentinel 找当前主节点，不能把当前主节点地址永久写死。
- JWT SigningKey 两台必须相同，否则一台签发的 Token 在另一台会验签失败。

### 5.1 测试环境与正式环境配置

运行环境设为 `Production` 表示启用部署安全约束，不表示连接的是正式数据库。测试和正式使用同一套镜像；切换环境时修改服务器配置，不改实体或复制一套迁移。

| 配置 | 线上测试 | 线上正式 |
| --- | --- | --- |
| `ConnectionStrings__IdentityDatabase` | 测试库服务、应用账号和密码 | DBA 提供的正式库服务、应用账号和密码 |
| `IdentityDatabase__Schema` | 本次测试示例 `SAJET` | DBA 指定的表 owner，可以与测试环境不同 |
| `AuthenticationRedis__KeyPrefix` | `mom:staging:identity:auth:` | 例如 `mom:production:identity:auth:`，与开发、测试隔离 |
| JWT SigningKey / Issuer / Audience | 测试环境的一组值 | 正式环境独立的一组值；同一环境的 177、178 完全一致 |
| 管理员 SQL | 数据库结构初始化后执行一次 | 数据库结构初始化后执行一次 |

JWT 密钥可在开发机用 `openssl rand -base64 32` 生成，再写入服务器私有文件。同一环境两台复用同一个值，不要在每台分别生成。更换 schema 不会自动建表，也不会把旧环境数据复制过去；先完成第 7/8 节，再发布配置。当前这些是 MOM Identity 数据库配置，按SBU切换多个 MES 数据库的路由尚未实现。

### 5.2 首个管理员初始化

EF 迁移只创建表、索引和迁移历史。数据库结构初始化完成后，由运维人员在数据库客户端执行本机保存、未纳入代码管理的一次性 SQL，创建首个用户、内置 `ADMINISTRATOR` 角色及关联；密码列保存 BCrypt 哈希。SQL 只对目标数据库执行一次，账号格式和 SQL 使用说明见[角色与权限](authorization.md#首个管理员)。

应用启动不会创建用户、角色或管理员，也不需要任何管理员环境变量；它只同步代码中的权限目录和默认菜单。177、178 共用数据库，因此两台服务都可以按常规方式启动。后续用户和角色通过后台管理。

## 6. 准备 Gateway 配置

177、178 各自创建 `/etc/mom/gateway.env`：

```bash
sudo touch /etc/mom/gateway.env
sudo chmod 600 /etc/mom/gateway.env
sudo vi /etc/mom/gateway.env
```

177 和 178 的配置可以相同，因为容器内使用服务名访问同机 Identity：

```dotenv
DOTNET_ENVIRONMENT=Production
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080

# Gateway 的直接上游是 176 的 Nginx
ForwardedHeaders__KnownProxies__0=10.32.15.176

# Gateway 通过 mom-net 访问同机 Identity 容器
ReverseProxy__Clusters__identity-cluster__Destinations__identity-api__Address=http://mom-identity:8080/
```

## 7. Oracle 19c 首次初始化或清空重建

全项目只有 `PlatformIdentityDbContext` 和 `Persistence/Migrations/` 一套迁移，基线为 `20261027000000_InitialIdentitySchema`。布尔字段统一为 `NUMBER(1)`。本地 Oracle Free 可以继续使用，应用与 SQL 都按 19c 标准生成。数据库表由 SQL 初始化；Identity 首次启动时同步菜单和权限，首个管理员由运维单独执行本机 SQL 创建。

本机 `ZX_IDENTITY / FREEPDB1` 直接执行 EF 迁移见第 7.4 节。以下流程用于导出部署 SQL 供服务器操作者或 DBA 执行；同一个目标库选择一种初始化方式即可。`IdentityDatabase__Schema` 是表 owner，连接字符串的 `User Id` 是登录用户，二者可以不同；EF 不负责创建 Oracle 登录用户、分配表空间或授予跨 schema 权限，这些由 DBA 预先准备。

### 7.1 开发机生成初始化 SQL

以下命令在开发机仓库根目录运行，离线服务器不需要安装 SDK / EF 工具。已安装 EF 工具时将 `install` 换成 `update`：

```bash
cd /path/to/ZX.Platform
dotnet tool install --global dotnet-ef --version 10.0.12
dotnet ef --version
mkdir -p output/mom-deploy

dotnet ef migrations script 0 20261027000000_InitialIdentitySchema \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/initialize.generated.sql
```

设计时工厂只使用占位地址，不连接真实数据库。生成 SQL 固定使用 `ZX_IDENTITY` 作为源 schema；下一步按环境替换，无需为每个环境新建 Migration。

测试库示例（最后三个参数依次是表所属 schema、执行 SQL 的登录账号、Oracle 服务名）：

```bash
python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/initialize.generated.sql \
  output/mom-deploy/initialize.sql \
  SAJET KS31377716 KSMESEBDEV
```

本地开发改用 `ZX_IDENTITY ZX_IDENTITY FREEPDB1`；正式库按 DBA 提供的三个值填写。执行账号与表 owner 可以不同；如果由 DBA 使用其他账号执行，导出时填写 DBA 的实际执行账号。工具同时替换带引号的对象 schema 和 owner 判断，并生成服务名、账号及同名对象检查。初始化脚本只创建旧基线结构。当前代码还需要执行 `20261028084818_ReplaceFactoriesWithSbus` 增量 SQL，之后才能启动 Identity；按第 8 节从基线 ID 导出到该迁移 ID，新空库也需要这一步。

### 7.2 需要清空重建时

新空库跳过这一步。需要丢弃已有 MOM 数据时，先停止连接这个 schema 的全部 Identity 实例，再生成单独的重置 SQL：

```bash
python3 deploy/docker/export-identity-reset-sql.py \
  output/mom-deploy/reset-identity.sql \
  SAJET KS31377716 KSMESEBDEV
```

这会生成删除当前八张 MOM 业务表、旧厂区基线的两张表（若存在）及 `ZX_IDENTITY_MIGRATIONS` 的 SQL，**执行后原有用户、角色、菜单等数据会被删除**。生成命令本身不会连接或修改数据库；审核并执行后，再执行 `initialize.sql` 和第 8 节的 SBU 增量 SQL。脚本只操作明确列出的 MOM 表，不删除 schema 用户或其他业务表；共享 `SAJET` 时禁止用 `DROP USER ... CASCADE` 清库。已有表若被其他系统约束引用，重置会报错，需要 DBA 处理，不能直接扩大删除范围。

脚本使用 `DROP TABLE ... PURGE`，不会再把这些表放入回收站。手动删除时出现的 `BIN$...` 是 Oracle 回收站对象；这不代表新的 MOM 迁移失败。不要为了清理 MOM 而清空共享 schema 的整个回收站。本节是主动丢弃数据后的重建流程，日常正式发布使用第 8 节增量迁移。

### 7.3 执行和核对

使用数据库工具连接目标服务，先执行：

```sql
SELECT SYS_CONTEXT('USERENV', 'SERVICE_NAME') AS SERVICE_NAME,
       SYS_CONTEXT('USERENV', 'SESSION_USER') AS LOGIN_USER
FROM dual;
```

确认无误后，以“执行脚本”模式运行完整的 `initialize.sql`。重建时先执行审核过的 `reset-identity.sql`。正式库由 DBA 执行；应用运行账号只需要业务表的读写权限。

执行机已有 SQL*Plus 时，也可以在 SQL 文件目录创建 `run-initialize.sql`：

```sql
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET DEFINE OFF
SET ECHO ON
SPOOL initialize-execution.log
@initialize.sql
SPOOL OFF
EXIT SUCCESS
```

执行时按提示输入密码，不把密码放进命令行：

```bash
sqlplus -L '执行账号@//数据库地址:1521/服务名' @run-initialize.sql
```

Oracle DDL 会隐式提交。遇错停止并保存日志，检查已完成的步骤；不要认为 `ROLLBACK` 能撤销整份 DDL，也不要未核对就重复执行。

完成后执行下面的 SQL，其他环境替换 `SAJET`：

```sql
SELECT table_name FROM all_tables
WHERE owner = 'SAJET' AND table_name LIKE 'ZX_IDENTITY%'
ORDER BY table_name;

SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

基线初始化后应看到 `20261027000000_InitialIdentitySchema`；按第 8 节升级后，再确认最后一条记录为 `20261028084818_ReplaceFactoriesWithSbus`，且存在 `ZX_IDENTITY_SBU`、`ZX_IDENTITY_USER_SBU`，不存在旧厂区表。确认应用配置的 `IdentityDatabase__Schema` 与表 owner 一致，再在数据库客户端对该库执行本机保存的一次性管理员 SQL，然后启动 Identity。后台会补齐权限和菜单；应用不会创建或重置用户及管理员角色。后续用户和角色通过后台维护。

### 7.4 本机清空后的 EF 迁移与首个管理员

以下仅针对本机 `127.0.0.1:1521/FREEPDB1`、表 owner 为 `ZX_IDENTITY`。表和 `ZX_IDENTITY_MIGRATIONS` 已删除时，保持 Identity 停止，在仓库根目录执行：

```bash
dotnet ef database update \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --context PlatformIdentityDbContext \
  --connection 'User Id=ZX_IDENTITY;Password=<本机ZX_IDENTITY密码>;Data Source=127.0.0.1:1521/FREEPDB1;'
```

设计时工厂不读取 User Secrets 中的真实连接，所以这里必须提供 `--connection`。若由本机 `SYSTEM` 执行，只把连接中的登录账号和密码改成该管理账号；本机迁移的对象仍限定在 `ZX_IDENTITY` 下，不会因为登录账号变化就改成 `SYSTEM` schema。应用运行连接仍用自己的应用账号，不能沿用这个迁移管理账号。其他 schema 使用第 7.1 节生成的目标 SQL。

迁移完成后，在数据库客户端打开本机保存的 `local-admin-init.sql`，确认其表 owner 为 `ZX_IDENTITY` 后执行一次。脚本创建首个用户、内置管理员角色及关联，密码字段写入 BCrypt 哈希；不要把脚本放进仓库或发布包。然后按开发环境说明启动 Identity。启动时只同步权限目录和默认菜单，不会从 User Secrets 读取或创建管理员。前端按 `pnpm run dev` 启动并用新账号登录；后续用户和角色通过后台维护。

## 8. 后续数据库迁移命令

177、178 连接同一库和 schema 时，每份 SQL 只执行一次。测试库、正式库分别查询当前版本并申请执行。模型未变化时，普通应用发布不用数据库迁移。

每次修改表结构的顺序是：修改实体及映射 → 创建一个迁移 → 在开发库执行和验证 → 按目标库当前版本生成增量 SQL → 测试库验证 → DBA 在正式库执行 → 发布对应版本镜像。测试和正式可以处于不同迁移版本，分别导出各自的起点到本次终点，不能直接假定两个库的升级起点相同。

### 8.1 查询目标库当前版本

在目标数据库工具里执行，其他环境替换 `SAJET`：

```sql
SELECT "MigrationId", "ProductVersion"
FROM SAJET.ZX_IDENTITY_MIGRATIONS
ORDER BY "MigrationId";
```

最后一行 ID 是升级起点。历史表缺失、没有记录或版本不符时，先核实连接和初始化状态，不要手工补写记录。

### 8.2 开发者修改模型后创建迁移

只在字段、表、关系或索引等模型变化时执行；每次部署或更换 schema 都不需要重复创建迁移：

```bash
dotnet ef migrations add 描述本次变更的英文名称 \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output-dir Persistence/Migrations

dotnet ef migrations list --no-connect \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api
```

审核新生成的 `Up`、`Down` 和 `PlatformIdentityDbContextModelSnapshot.cs`，确认只有预期变更，并随代码提交。`--no-connect` 列表只代表代码中的迁移，不代表目标库已执行。

随后使用第 7.4 节的 `dotnet ef database update --connection ...` 更新本机开发库并验证业务。该命令会执行尚未应用的迁移，不要求每次清空开发库。已交付或执行过的迁移继续保留，不删除旧文件、不重新生成基线；后续变化新增一个迁移。

### 8.3 以正式库版本导出交给 DBA 的 SQL

应用版本号（例如 `v1.1`、`v1.2`）不是数据库迁移版本。每个环境以自己的 `ZX_IDENTITY_MIGRATIONS` 最后一条 `MigrationId` 为准；正式库的 ID 是升级起点，v1.2 代码中本次发布要到达的 Migration ID 是终点。测试库用于验证终点对应的迁移和业务行为。

按以下顺序手工处理：

1. 分别查询正式库、测试库的最后一条 Migration ID，并记录目标 schema。若历史表缺失、为空或正式库 ID 不在本次代码的迁移链中，先暂停并让 DBA 核实，不要手工补版本记录。
2. 切换到本次 v1.2 发布对应的代码提交，在仓库根目录列出该版本包含的迁移：

   ```bash
   dotnet ef migrations list --no-connect \
     --context PlatformIdentityDbContext \
     --project Services/Identity/ZX.Platform.Identity.Infrastructure \
     --startup-project Services/Identity/ZX.Platform.Identity.Api
   ```

   选择本次 v1.2 发布要到达的 Migration ID 作为 `TO_MIGRATION`。它必须已在测试环境验证；如果测试库还没到这个版本，先按测试库自己的当前 ID 生成并执行测试升级脚本。
3. 以正式库查询到的最后一条 ID 作为 `FROM_MIGRATION`，使用同一 `TO_MIGRATION` 生成正式升级脚本。不要把测试库当前 ID 直接当正式库起点。
4. 用下方命令从正式库当前 ID 生成 EF 增量 SQL，并转换成目标 schema 专用、含执行前版本检查的脚本。把最终的 `upgrade.sql` 审核后交给 DBA；不要把 `upgrade.generated.sql` 当作执行文件。
5. DBA 在正式库执行一次 `upgrade.sql`。执行成功后重新查询版本表，确认最后一条为目标 ID，再发布对应应用版本。

在仓库根目录的 macOS/Linux shell 中执行，先确保已安装与 EF Core 10 匹配的 `dotnet-ef`（10.0.12）。首次安装可运行 `dotnet tool install --global dotnet-ef --version 10.0.12`；已安装旧版本时运行 `dotnet tool update --global dotnet-ef --version 10.0.12`。再把示例值换成实际的正式库信息和 Migration ID；Migration ID 使用完整值，例如 `20261027000000_InitialIdentitySchema`：

```bash
FROM_MIGRATION='20261027000000_InitialIdentitySchema' # 正式库查到的最后一条
TO_MIGRATION='20261101090000_DescribeTheChange'       # v1.2 代码中的目标 Migration
TARGET_SCHEMA='SAJET'                                  # 正式库表 owner
DBA_LOGIN='MOM_MIGRATION'                              # DBA 实际执行脚本时使用的账号
DB_SERVICE='MOMPROD'                                   # 正式库连接的 Oracle 服务名

mkdir -p output/mom-deploy

# 生成 FROM（不含）到 TO（含）的 EF 原始迁移 SQL；此命令不连接数据库。
dotnet ef migrations script "$FROM_MIGRATION" "$TO_MIGRATION" \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output output/mom-deploy/upgrade.generated.sql

# 替换目标 schema，并生成带服务、执行账号和当前 Migration 检查的 DBA 脚本。
python3 deploy/docker/export-oracle19-sql.py \
  output/mom-deploy/upgrade.generated.sql \
  output/mom-deploy/upgrade.sql \
  "$TARGET_SCHEMA" "$DBA_LOGIN" "$DB_SERVICE" \
  --from-migration "$FROM_MIGRATION" \
  --to-migration "$TO_MIGRATION"
```

`dotnet ef migrations script` 只根据代码生成中间文件，不会读取正式库版本或自动替你比较测试库；Migration ID 和正式 schema 必须按查询结果填写。导出工具会检查 ID 格式及先后顺序、目标迁移是否出现在 SQL 中，并在最终脚本执行前校验 Oracle 服务、登录账号和正式库当前 Migration ID。`DBA_LOGIN` 必须与 DBA 实际连接执行脚本的登录账号一致，且该账号应具备目标 schema 所需的 DDL 权限。

交给 DBA 的文件是 `output/mom-deploy/upgrade.sql`。同时告知正式库服务、schema、执行账号、起止 Migration ID、应用版本及预期变更；原始 `upgrade.generated.sql` 留作开发审查。DBA 无需安装 .NET/EF 或拉取源码。最终脚本包含迁移历史更新，成功后不需要手工填写 `ZX_IDENTITY_MIGRATIONS`。

例如正式库仍是基线 `20261027000000_InitialIdentitySchema`，测试库已验证到 v1.2 的 `20261101090000_DescribeTheChange`，则正式导出使用这两个 ID 作为 FROM 和 TO。若正式库与测试库版本不同，仍按正式库的 FROM 生成；如果正式库已经等于 TO，则无需生成 SQL。不要省略起止 ID，否则 EF 会从零生成 SQL；不要用尚未在测试库验证的终点发布正式库。

### 8.4 审核、执行、验收

检查目标 schema、迁移 ID、列类型、默认值、索引和数据变更。涉及删表、删列或清理数据时按实际影响审核。先测试验证，再由 DBA 在正式库执行。运行中的旧应用若无法使用新结构，需要安排发布窗口。

本次 SBU 升级的终点是 `20261028084818_ReplaceFactoriesWithSbus`。若目标库当前为 `20261027000000_InitialIdentitySchema`，以该基线为 FROM、SBU 迁移为 TO 生成增量 SQL；它会删除旧厂区及用户厂区测试数据，并保留用户、角色等其他表。升级后需给相关角色授予新的 `identity.sbu.*` 和 `identity.user.sbus` 权限。

执行方式与第 7.3 节相同，将包装脚本中的 `@initialize.sql` 改为 `@upgrade.sql`，日志名改为 `upgrade-execution.log`。遇错停止发布；DDL 可能已经生效，不可盲目重跑。

执行后重新查询迁移历史，确认最新 ID 与 `TO_MIGRATION` 一致，并核对本次新增列、索引和约束。由 DBA 确认应用账号有新对象所需的权限。然后按第 9 节先发布 177、验证后再发布 178。

切回旧镜像不会自动回退数据库。需要回退时使用审核过的数据库回退或恢复方案。更换环境配置不会复制测试库业务数据到正式库。

## 9. 启动 177 或 178 的容器

首次部署时先按第 5.2 节完成管理员 SQL，再启动并验证 177、178；两台连接同一个数据库。以下命令两台都可用。Gateway 的固定 IP 必须与第 4 节网段、第 5 节可信代理一致。更新已接流量的节点时，先按第 11 节摘除该节点；本节命令只管理 MOM 的两个容器。

```bash
export RELEASE=20261027-01

# 查看是否有旧 MOM 容器。
sudo docker ps -a --filter name=^/mom-identity$ --filter name=^/mom-gateway$

# 创建或重建 Identity。应用不需要持久化卷，数据在 Oracle 和 Redis。
if sudo docker container inspect mom-identity >/dev/null 2>&1; then
  sudo docker stop --time 30 mom-identity
  sudo docker rm mom-identity
fi
sudo docker run -d \
  --name mom-identity \
  --restart unless-stopped \
  --network mom-net \
  --env-file /etc/mom/identity.env \
  --log-driver json-file \
  --log-opt max-size=50m \
  --log-opt max-file=5 \
  mom-identity:${RELEASE}

# Gateway 对外提供给 176 的端口；容器内部仍监听 8080。
if sudo docker container inspect mom-gateway >/dev/null 2>&1; then
  sudo docker stop --time 30 mom-gateway
  sudo docker rm mom-gateway
fi
sudo docker run -d \
  --name mom-gateway \
  --restart unless-stopped \
  --network mom-net \
  --ip 172.30.176.10 \
  --publish 18080:8080 \
  --env-file /etc/mom/gateway.env \
  --log-driver json-file \
  --log-opt max-size=50m \
  --log-opt max-file=5 \
  mom-gateway:${RELEASE}
```

如果服务器不允许 `18080` 对所有来源开放，应在服务器网络策略中只允许 176 访问该端口。Identity 不需要发布到宿主机端口，Gateway 通过 `mom-net` 访问它。

查看状态和日志：

```bash
sudo docker ps --filter name=^/mom-
sudo docker inspect -f '{{.State.Status}} restart={{.RestartCount}} exit={{.State.ExitCode}}' mom-identity
sudo docker logs --tail 100 mom-identity
sudo docker logs --tail 100 mom-gateway
```

当前程序没有统一 `/health` 端点，因此以容器运行状态、日志中的启动完成信息和实际 API 响应为准。Gateway 本机可先验证未登录请求能到达 Identity：

```bash
curl -i http://127.0.0.1:18080/api/identity/api/Auth/me
```

预期返回 Identity 的 `401`；这只是后端连通检查，不验证 HTTPS 入口、代理协议或浏览器 Cookie。若返回 `307/308`，检查可信代理和 HTTPS 转发头；`502` 检查 Gateway 到 Identity，`503` 还需查看 Redis 等依赖。第 10 节完成后，必须从 176 的 HTTPS 入口用真实账号验证登录、刷新和退出。

## 10. 176 配置 Nginx

176 的 Nginx 保留原有网站配置，为 MOM 新增独立端口。下面是默认 HTTPS 方案，以 `https://10.32.15.176:8443` 为例，先确认端口、前端目录和证书路径；证书需包含该 IP 的 SAN 并被访问者信任，有正式域名时改为该域名和对应证书。当前内网如先用 HTTP 测试，按[部署脚本说明](../deploy/docker/README.md#2-首次准备配置每台只做一次)同时更新前后端、显式设置两台的 Cookie HTTP 开关，并将 Nginx 切回 8088 HTTP。HTTP 不加密登录和会话流量，正式使用恢复 HTTPS 和 Secure。

前端已提供独立部署脚本，在前端仓库执行 `pnpm deploy`，先构建、打包，再输入 176 的临时 SSH 信息上传；操作与回退见前端仓库的 `docs/deployment.md`。脚本使用 `/var/www/mom` 并保留最近 3 次备份，当前校验 HTTP 8088。下面的 HTTPS 配置留作切换参考，启用时还需同步调整前端脚本的入口校验。

将前端构建产物准备到 `/var/www/mom`（目录内含 `index.html`），新增 `/etc/nginx/conf.d/mom.conf`：

```nginx
upstream mom_gateway {
    server 10.32.15.177:18080 max_fails=3 fail_timeout=10s;
    server 10.32.15.178:18080 max_fails=3 fail_timeout=10s;
    keepalive 32;
}

server {
    listen 10.32.15.176:8443 ssl;
    server_name 10.32.15.176;
    ssl_certificate /etc/nginx/tls/mom/fullchain.pem;
    ssl_certificate_key /etc/nginx/tls/mom/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;

    root /var/www/mom;
    index index.html;
    access_log /var/log/nginx/mom-access.log;
    error_log /var/log/nginx/mom-error.log warn;

    location /api/ {
        proxy_pass http://mom_gateway;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_set_header Host $http_host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $http_host;
        proxy_connect_timeout 3s;
        proxy_read_timeout 30s;
        proxy_next_upstream error timeout;
        proxy_next_upstream_tries 2;
        proxy_cache off;
    }

    location /assets/ {
        try_files $uri =404;
        expires 7d;
    }
    location = /index.html {
        add_header Cache-Control "no-cache";
    }
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

前端生产配置的 API 基址应使用同源 `/api`，不要写死 177 或 178。Nginx 的 `proxy_pass` 保留完整 `/api/identity/...`，不能在此再去掉 `/api`。HTTPS 在 176 终止，Gateway 信任直接上游 176，Identity 信任 Gateway 容器固定 IP；两个服务接收可信的 `X-Forwarded-Proto: https`，内部 HTTP 才能保持正确的外部协议判断。前端 Vite 的本机开发代理设置不用于生产 Nginx。

首次只有 177 已就绪时，先将 upstream 中 178 的行注释，待第 9 节在 178 验证完成后再恢复并 reload。改完只做配置检查和平滑 reload：

```bash
sudo nginx -t
sudo systemctl reload nginx
```

然后用浏览器验证：登录、刷新页面、退出、权限菜单、SBU 管理及用户 SBU 关联，以及关闭其中一台后端后的访问情况。当前尚无面向业务数据的 SBU 切换功能。

在 176 上也可先检查 HTTPS 到 Identity 的链路：

```bash
curl -i https://10.32.15.176:8443/api/identity/api/Auth/me
```

未登录应返回 Identity 的 `401`。企业 CA 尚未在该机器受信任时，先安装 CA 或给 curl 添加 `--cacert <CA证书路径>`；浏览器也需要信任同一证书链。

刷新 Cookie 的名称为 `zx_identity_refresh`、路径 `/api`。Cookie 不按端口隔离，若同一 IP 上还有另一套 MOM 测试/正式站点，不能只靠不同端口隔离登录，需使用独立域名等隔离方案。

## 11. 修改配置、更新镜像和回滚

### 只修改环境变量

Docker 在创建容器时从 `--env-file` 读取环境变量，运行中修改 `/etc/mom/*.env` 不会自动生效，`docker restart` 也不会重新读取文件。修改后只重建对应容器，不需要重新构建镜像。先将正在操作的节点从 176 的 upstream 临时摘除并 reload，让另一台服务接流量；首次部署尚无流量时可直接操作。

下面的 `RELEASE` 填写当前已验收镜像标签：

```bash
export RELEASE=20261027-01
sudo vi /etc/mom/identity.env
sudo docker stop --time 30 mom-identity
sudo docker rm mom-identity
sudo docker run -d \
  --name mom-identity \
  --restart unless-stopped \
  --network mom-net \
  --env-file /etc/mom/identity.env \
  --log-driver json-file --log-opt max-size=50m --log-opt max-file=5 \
  mom-identity:${RELEASE}
```

若只修改 `gateway.env`，同理重建 Gateway：

```bash
sudo vi /etc/mom/gateway.env
sudo docker stop --time 30 mom-gateway
sudo docker rm mom-gateway
sudo docker run -d \
  --name mom-gateway \
  --restart unless-stopped \
  --network mom-net \
  --ip 172.30.176.10 \
  --publish 18080:8080 \
  --env-file /etc/mom/gateway.env \
  --log-driver json-file --log-opt max-size=50m --log-opt max-file=5 \
  mom-gateway:${RELEASE}
```

验证后恢复该节点到 upstream。JWT、Redis、数据库连接或 schema 改变，都需要重新创建相应容器；不需要重启服务器。两台后端的共享配置最终必须一致。JWT 密钥或数据库/schema 的切换可能不允许新旧配置同时接流量，需要统一切换计划；普通逐台发布不能保证这类变更无中断。

### 发布新镜像

1. 在本地构建新版本并计算 SHA256；涉及模型变化时，先完成第 8 节的测试和 DBA 增量迁移。
2. 上传并 `docker load` 到 177、178，暂不替换运行中的容器。
3. 在 176 临时注释 upstream 中的 177，`nginx -t` 成功后 reload，等待该节点现有请求结束；178 继续服务。
4. 177 停止旧 MOM 容器，按第 9 节使用新 `RELEASE` 创建容器并验证。恢复 177 到 upstream，reload 后验收实际请求。
5. 177 验证通过后，摘除 178 并重复更新，最后恢复两台并验收。某台失败时先保持该台摘除。

该顺序适用于新旧应用和数据库结构允许短时混用的版本。包含不允许混用的表结构、权限目录或共享配置变化时，应另安排同步发布窗口；不要仅凭有两台服务器就假定任意变更都能不停机。

旧镜像标签不要立即删除，至少保留上一个已验收版本：

```bash
sudo docker image ls 'mom-*'
```

### 回滚

回滚程序只切回上一个镜像标签并重建容器：

```bash
export RELEASE=上一个已验收版本
sudo docker stop --time 30 mom-gateway mom-identity
sudo docker rm mom-gateway mom-identity
# 按本手册第 9 节使用旧 RELEASE 重新执行两个 docker run
```

数据库迁移不是通过切换镜像自动回滚的。回滚前必须确认新数据库结构对旧程序兼容；已经执行的破坏性数据库变更只能按 DBA 审核过的回滚 SQL 和备份恢复。

## 12. 常见问题

### `docker load` 报架构或镜像不存在

检查服务器架构和镜像架构：

```bash
uname -m
sudo docker image inspect mom-identity:${RELEASE} \
  --format '{{.Os}}/{{.Architecture}}'
```

服务器为 `x86_64` 时，本地必须用 `--platform linux/amd64` 构建。

### Identity 启动后马上退出

优先查看：

```bash
sudo docker logs mom-identity
```

常见原因：数据库连接字符串缺失、`IdentityDatabase__Schema` 与实际表 owner 不一致、数据库表尚未初始化、Redis/Sentinel 不通或 JWT SigningKey 为空。没有管理员账号不会阻止应用启动，但用户无法登录。

### 迁移成功但没有登录账号

迁移成功不等于管理员已创建。确认本机 SQL 已在正确的服务和 schema 中执行；必要时由 DBA 核对用户、`ADMINISTRATOR` 角色及关联。应用启动不会补建、重置或恢复管理员。已有账号访问异常时按 DBA 变更流程处理，不要靠删角色或清库恢复。

### 登录后刷新失败或 API 返回 307

确认浏览器访问的是 176 的 HTTPS 地址，`AuthenticationCookie__Secure=true`，Nginx 设置 `X-Forwarded-Proto $scheme`。Gateway 的可信上游必须是 176；Identity 的可信上游必须是实际 Gateway 容器 IP，可用下列命令核对：

```bash
sudo docker inspect -f '{{(index .NetworkSettings.Networks "mom-net").IPAddress}}' mom-gateway
```

结果应与 `ForwardedHeaders__KnownProxies__0` 完全一致。改配置后重建容器；只改文件或重启旧容器不会更新环境变量。不要把本机开发时忽略证书验证的设置搬到生产。

### Gateway 返回 502

确认同一 Docker 网络中的 Identity 名称和端口：

```bash
sudo docker exec mom-gateway getent hosts mom-identity
sudo docker exec mom-gateway sh -c 'wget -qSO- http://mom-identity:8080/api/Auth/me -O - 2>&1 | head'
```

如果镜像没有 `wget`，查看 Gateway 日志，并检查 `ReverseProxy__Clusters__identity-cluster__Destinations__identity-api__Address` 是否为 `http://mom-identity:8080/`。

### 数据库连接失败

先从 177 或 178 测试 Oracle 主机和端口：

```bash
nc -vz 10.32.15.47 1521
```

再核对服务名、schema、账号权限和数据库工具实际执行的迁移历史。应用账号不能建表是正常的；它只需要读写已经由 DBA 创建的 MOM 表。

### Redis 看起来连接了错误的节点

不要把连接串改成单个主节点地址。应用配置应包含三个 Sentinel 地址和同一个 `serviceName`，由 Sentinel 发现当前主节点。Redis 故障切换后，应用会重新连接发现到的主节点。

## 13. 本次发布产物

按第 2、3 节重新构建和导出当前源码。镜像和初始化 SQL 必须来自同一版本，不能复用之前导出的安装包。

```text
output/mom-deploy/mom-backend-${RELEASE}.tar
output/mom-deploy/mom-backend-${RELEASE}.tar.sha256
output/mom-deploy/initialize.generated.sql
output/mom-deploy/initialize.sql
```

以上 SQL 用于首次初始化；日常结构升级交付 `upgrade.sql`，不要向已上线库重复执行 `initialize.sql` 或重置脚本。SQL 的 schema、执行账号和服务名必须匹配本次目标环境。SQL 不放入应用镜像，密码只保存在私有部署配置中。
