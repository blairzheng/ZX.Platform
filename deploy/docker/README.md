# MOM 一键离线部署

当前脚本用于 **177、178 的后端**：本机一次打包，输入两台的临时 SSH 信息后并行上传、并行部署。当前前端和 Nginx 尚未接入，这一版允许两台后端同时中断，不做不停机流量切换，也不连接 176。数据库 DDL 先由 DBA 执行，脚本不执行迁移或清库，也不改 Redis 服务。

## 推荐入口：本机使用两台临时 SSH 并行部署

本机打包后执行，无需再手工上传镜像或登录两台执行命令：

```bash
bash deploy/docker/package.sh 20261027-01

# 首次准备：上传发布包，在两台创建不覆盖已有文件的配置模板，然后结束。
bash deploy/docker/remote.sh output/mom-deploy/mom-backend-20261027-01.tar.gz --action prepare

# 按下方第 2 节填写两台 /etc/mom/ 下的配置、由 DBA 完成数据库初始化后执行。
bash deploy/docker/remote.sh output/mom-deploy/mom-backend-20261027-01.tar.gz
```

每次运行依次提示输入：177 的 `ssh 临时账号@bu17jump.luxshare.com.cn -p 2222`、临时密码、sudo 密码，再输入 178 的对应信息。密码隐藏输入，不写入命令行、配置文件或发布包；免密 sudo 直接回车，sudo 与 SSH 密码相同则输入 `=`。请在脚本提示后再申请/粘贴有效期内的临时凭据，不在命令后拼接密码。

本机使用系统自带的 `ssh`、`sftp` 和 Python 3.9+ 标准库，**不下载依赖、不创建 Python 虚拟环境**。在 Mac 上直接执行 `bash deploy/docker/remote.sh ...`，无需本机 `sudo`。SSH 按本机 `known_hosts` 校验主机密钥；未知指纹需在终端核对并输入 `yes`，主机密钥变更不会跳过检查。

一次运行的阶段如下，两台在每个阶段内并行，前一个阶段全部成功才进入下一阶段：

1. 各建立一次 SSH 连接，核对实际目标分别为 `10.32.15.177` / `10.32.15.178`。
2. 系统 SSH 保持命令会话，系统 SFTP 通过本机临时控制套接字复用同一次登录；上传/下载随机小文件，命令会话再核对同一文件的 SHA256 后删除，确认 SFTP 没有停留在跳板机虚拟目录。
3. 上传同一离线包、核对压缩包 SHA256、解压，准备或复用配置；核对 sudo 可用。
4. 检查两台配置、网段、端口、离线包，并在不显示密码的情况下比对数据库、Redis、JWT 共享配置是否一致。任一项不通过，两台都不开始替换容器。
5. 两台同时执行各自的镜像导入和容器部署，分别显示启动检查及结果。

177 此前已通过登录、同一连接上的命令与 SFTP 小文件核对；178 在连接阶段失败，尚未确认具体原因。**本次改用系统 SSH/SFTP 后的两台连接、镜像大文件传输及并行部署仍需实机验收**。错误会显示失败阶段和系统工具的具体输出。跳板机不支持连接复用时，脚本会在上传镜像和停止旧服务前结束；连接断开后不会用同一密码自动重新登录。可先只验证连接复用和文件传输：

```bash
bash deploy/docker/remote.sh --action probe
```

`probe` 也要求两台各提供一组临时凭据，只创建并删除小文件，不上传镜像、不执行 sudo 或部署。首次配置填完后，也可用下面的命令只上传并检查：

```bash
bash deploy/docker/remote.sh output/mom-deploy/mom-backend-20261027-01.tar.gz --action check
```

`prepare`、`probe`、`check` 各是独立运行，临时凭据如已用过或失效，下一次运行重新申请。两台同时启动可能短暂竞争权限目录插入，现有初始化逻辑会处理一次唯一冲突并重新读取。

部署失败时，各节点沿用服务器脚本的失败恢复逻辑；另一台已经成功则保持运行，不自动跟随回滚，终端明确列出各自结果。SSH 断开时不能假定服务端已完成或已回滚，应重新获取凭据查看 `status`，存在未完成事务再执行 `recover`。成功会清理本次 `/tmp/mom-remote-*` 上传目录；失败且已开始部署时保留目录供核查，不清除日志或数据库状态。修改后的 `/etc/mom/` 文件始终保留。

以下为首次配置和服务器手工执行方式；不使用远程入口时仍可照此操作。后续接入前端、Nginx 后再增加不停机部署能力。

## 1. 本机打包

在 `ZX.Platform` 根目录执行，Docker Desktop / Colima 需已启动：

```bash
bash deploy/docker/package.sh 20261027-01
```

构建两个 `linux/amd64` 镜像，生成：

```text
output/mom-deploy/mom-backend-20261027-01.tar.gz
```

上传这一个文件到 177、178 的 `/tmp`。每次发布换一个版本号；镜像标签和同名离线包已存在时脚本会拒绝覆盖。构建机需要能获取基础镜像和 NuGet 依赖，服务器导入和运行不需要外网。

包里只有两个镜像、版本/镜像 ID 清单、SHA256 校验文件、部署脚本、配置模板和本说明，不包含数据库密码或本机 User Secrets。数据库初始化/升级 SQL 按完整手册第 7/8 节另交 DBA。

本机打包使用 Python 标准库，不携带 macOS 的 `._*` 文件元数据；发布包仍按完整文件清单和 SHA256 严格校验。

## 2. 首次准备配置（每台只做一次）

在服务器执行：

```bash
cd /tmp
tar -xzf mom-backend-20261027-01.tar.gz
cd mom-backend-20261027-01
sudo bash deploy.sh init
sudo vi /etc/mom/identity.env
sudo vi /etc/mom/deploy.json
```

`init` 创建模板，不覆盖已有文件，也不启动应用。服务器需要 Docker Engine、Python 3 和 `ip`（Oracle Linux 9 通常已有后两项），无需安装 .NET Runtime 或 EF。

首次填写：

| 文件 | 需要填写的内容 |
| --- | --- |
| `/etc/mom/identity.env` | 数据库连接及 Schema、Redis 密码、共享 JWT 密钥；所有 `CHANGE_ME` 都要替换 |
| `/etc/mom/deploy.json` | 默认网段/IP/端口可用时保留；脚本检查与服务器路由、其他 Docker 网络是否重叠 |
| `/etc/mom/gateway.env` | 默认可不修改，供额外 Gateway 设置使用 |

两台 Identity 的数据库、Redis 服务名和前缀、JWT 密钥/Issuer/Audience 必须一致。JWT 密钥用 `openssl rand -base64 32` **生成一次**，再填入两台。测试和正式使用不同的连接、Schema、Redis 前缀及 JWT 配置。

数据库连接形状如下，换成 DBA 给出的实际值；值不加外层引号：

```dotenv
IdentityDatabase__Schema=SAJET
ConnectionStrings__IdentityDatabase=User Id=应用账号;Password=应用密码;Data Source=//数据库IP:1521/服务名;Min Pool Size=0;Max Pool Size=50;Connection Lifetime=300;
```

脚本统一设置 Production、容器 HTTP `:8080`、可信代理和 Gateway 到 Identity 的地址，无需在 env 里重复填写；已有文件中若含冲突值，会在停止应用前报错。Cookie 默认 Secure=true，内网 HTTP 测试按下一段显式开启。网络参数只修改 `deploy.json`，不要修改脚本。

内网 HTTP 测试：先将这次修改后的前后端重新构建发布（旧版本不支持该开关）。在 **177、178** 的 `/etc/mom/identity.env` 中将现有 Secure 行改为 `AuthenticationCookie__Secure=false`，另加 `AuthenticationCookie__AllowInsecureHttp=true`，不要出现重复键。脚本不覆盖服务器现有配置，也不替操作者切换协议。HTTP 不加密密码和会话流量；恢复 HTTPS 时设 Secure=true、AllowInsecureHttp=false 并重新部署或执行新版本 `apply-config`。

176 的 `/etc/nginx/conf.d/mom.conf` 改为 `listen 8088;`，移除当前 `ssl_certificate`、`ssl_certificate_key`、`ssl_protocols` 三行，保留 API 代理和 `X-Forwarded-Proto $scheme`，执行 `sudo nginx -t && sudo systemctl reload nginx`。浏览器使用 `http://10.32.15.176:8088/`；从 HTTPS 切过来时删除 MOM 的 `zx_identity_refresh` Cookie 后重新登录，避免同 IP 已有 Secure Cookie 阻止 HTTP 写入。Cookie 不按端口隔离，不要同时使用 HTTP 和 HTTPS 登录这套测试系统。

从当前 HTTPS 版本切换 HTTP 的操作顺序：

1. 本机 `ZX.front.vue` 执行 `pnpm run build`，重新压缩 `dist` 内容并上传到 176，解压至现有 `/var/www/mom/`。
2. 在 177、178 各自执行 `sudo vi /etc/mom/identity.env`，修改 Secure 行并添加 AllowInsecureHttp 行；数据库、Redis、JWT 值保留。
3. 本机 `ZX.Platform` 执行 `bash deploy/docker/package.sh 20261027-03` 生成新包；再执行 `bash deploy/docker/remote.sh output/mom-deploy/mom-backend-20261027-03.tar.gz`，输入两台新临时 SSH。若该版本已用，换新版本号。
4. 176 按上一段改 Nginx，检查并 reload；删除浏览器内 MOM 的旧 Secure Cookie，从 HTTP 8088 重新登录，并验证刷新及退出。

这一步不执行数据库迁移，也不会创建或重置账号。登录提示账号或密码错误时，应核对部署所连数据库内的账号状态；切换协议不会修复账号本身的问题。

默认 Gateway IP 为 `172.30.176.10`，Identity IP 为 `172.30.176.11`，属于 `mom-net` 的 `172.30.176.0/24`，宿主机发布 `18080`。177、178 网络各自独立，可以使用相同设置。已有 `mom-net` 必须与配置匹配，脚本不会删除其他网络。

首次空库时，完成数据库结构初始化后，由运维人员在数据库客户端执行本机保存的一次性管理员 SQL，再启动 Identity。177、178 共用数据库，因此 SQL 只执行一次；应用启动不会创建用户或管理员，只同步权限目录和默认菜单。后续账号和角色通过后台维护，详见[角色与权限](../../docs/authorization.md#首个管理员)。

## 3. 在服务器直接部署单个节点（备用方式）

DBA 已完成初始化或本次增量 SQL 后，在当前解压目录执行：

```bash
# 可选：只检查包、配置、网段、端口，不导入镜像或替换容器。
sudo bash deploy.sh check

# 正式执行当前节点部署。
sudo bash deploy.sh
```

脚本自动校验发布包、检查配置与端口、导入镜像、创建或复用 `mom-net`，按固定 IP 创建两个 MOM 容器，并等待认证接口连续三次返回预期 `401 / 600401`。这是启动检查，不是完整登录、数据库权限或 Redis 故障切换验收。最后需从 176 的 **HTTPS** 页面验证登录、刷新和退出。

当前两台后端可由远程入口并行启动。首次部署前需先在共享数据库中执行管理员 SQL；后续发布不再重复执行。前端和 Nginx 准备好后再完整验收登录。账号保存在数据库中，不会随容器重建删除。

以后有旧版本接流量且要求不停机时，需另行实现并验证流量切换，不能直接沿用当前“两台同时替换”的方式。完整手册保留 Nginx 逐台摘除的手工流程供届时参考。

## 4. 后续更新与常用命令

以后每次只需：本机用新版本号打包 → `bash deploy/docker/remote.sh <发布包>` → 输入两台新临时 SSH 信息。已有 `/etc/mom/` 配置自动复用，无需每次填写。也可继续手工上传、解压并在服务器运行 `deploy.sh`。

| 命令 | 作用 |
| --- | --- |
| `sudo bash deploy.sh status` | 查看当前/上一版本和两个 MOM 容器状态，不输出密码 |
| `sudo bash deploy.sh apply-config` | 用当前镜像重建两个容器，加载修改后的 `/etc/mom/` 配置 |
| `sudo bash deploy.sh rollback` | 恢复上一代容器及其原配置，不回退数据库 |
| `sudo bash deploy.sh recover` | 异常断电、强制终止等留下事务记录时，恢复部署前容器 |
| `sudo docker logs --tail 100 mom-identity` | 查看 Identity 启动错误 |
| `sudo docker logs --tail 100 mom-gateway` | 查看 Gateway 启动错误 |

新版本启动检查失败时，脚本自动移除本次创建的容器，恢复原容器及其启动前状态。首次部署没有旧容器时只清理失败的新容器。失败或回滚不会撤销数据库写入（包括操作者单独执行的 SQL），也不修改你编辑过的 env 文件。

回滚使用保留的容器原配置，便于数据库/Redis/JWT 配置改错时恢复；因此可能与当前磁盘上的 env 内容不同，回滚后先核对再执行 `apply-config`。旧容器已被外部删除时无法自动恢复，脚本会保留事务记录并提示检查。

成功部署只保留上一代容器；更早的本脚本备份会删除，不清理其他业务资源。镜像和 `/tmp` 上传包不会被自动删除，按已验收版本定期管理磁盘容量。日志按每容器最多约 `50 MB × 5` 轮转。不要在回滚保留期间执行全局 `docker container prune` 或删除备份容器。

## 5. 数据库变化

新增/修改表时仍是：开发创建并验证 EF 迁移 → 导出目标 Schema 的增量 SQL → DBA 在对应数据库执行一次 → 执行部署脚本。共享数据库的 177、178 不重复执行迁移。迁移文件随代码提交，不为测试/正式各写一套。

详见仓库内[完整部署手册第 8 节](../../docs/docker-deployment.md#8-后续数据库迁移命令)。离线包中的相对仓库链接仅供回到源码时使用，离线包本身不携带完整源码文档。
