# MOM 后端发布：GitLab Runner

[文档导航](README.md) · [当前部署拓扑](deployment-topology.md) · [数据库迁移](migration-operations.md)

日常操作可看[三步发布简版](https://bu17gitlab.luxshare.com.cn/awe/mom_img/-/blob/main/QUICKSTART.md)。

当前流程：**开发机构建镜像 → 上传 GitLab Package Registry → 新建流水线 → 校验通过后手动发布**。服务器没有外网，Runner 只下载成品并执行 Docker 部署，不在服务器还原 NuGet 或构建代码。

## 发布入口与分工

| 项目 | 当前用途 |
| --- | --- |
| `ZX.Platform` | 后端源码、Dockerfile、EF 迁移 |
| [GitLab awe/mom_img](https://bu17gitlab.luxshare.com.cn/awe/mom_img) | `.gitlab-ci.yml`、镜像包和前端包；项目 ID `80` |
| 开发机 | Docker 构建两个 `linux/amd64` 镜像、导出压缩包、上传 |
| 176 Runner | Shell/Bash executor，标签 `zxnet_runner`，账号 `gitlab-runner` |
| 177、178 | 分别运行 `mom-identity`、`mom-gateway`；Runner 通过内网 SSH 顺序更新 |

`git push` 只更新代码或流水线配置，**不会上传镜像包，也不会直接替换正在运行的容器**。镜像包上传成功后仍需在 GitLab 手动发布。前端独立打包和发布，见[前端发布说明](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/deployment.md)。

## 一次性准备

当前环境已完成接入。重新搭建环境时须先核对：

- 开发机有 Docker Engine / Docker Desktop / Colima 和 buildx，能获取基础镜像及 NuGet 依赖。
- `mom_img` 项目已提交当前 `.gitlab-ci.yml`，默认分支为 `main`，已启用 `zxnet_runner`。
- 176 有 Bash、`curl`、`ssh`、`scp`、`sshpass`、`tar`、`gzip`、`sha256sum`、`base64`，能访问内网 GitLab 和两台后端。
- 177、178 有 Docker、Bash、`curl`、`flock`、`sha256sum`；部署账号可执行 `sudo -n docker`。
- 两台已有运行中的 MOM 容器、`mom-net` 和当前固定端口/IP，见[部署拓扑](deployment-topology.md)。流水线接管现有环境，不负责空服务器初始化、建库或配置 Nginx。
- 本地上传使用项目 Deploy Token，具备 `read_package_registry`、`write_package_registry`。在 GitLab **Settings → Repository → Deploy tokens** 管理；Token 不写进 Git。Runner 下载同项目包使用自动提供的 `CI_JOB_TOKEN`，无需再配置下载 Token。

## GitLab 配置变量

在 `mom_img` 的 **Settings → CI/CD → Variables** 维护。Group 已有的变量可继承，先核对名称、作用域和权限，再决定是否添加项目覆盖值。

| 变量 | 内容 |
| --- | --- |
| `DEPLOY_USER` | SSH 账号，当前为 `luxuser1` |
| `DEPLOY_PASSWORD_177`、`DEPLOY_PASSWORD_178` | 对应节点的 SSH 密码，当前从 `awe` Group 继承 |
| `MOM_IDENTITY_DB_SCHEMA` | 实际 MOM 表 owner，使用大写 schema 名；与连接登录账号分别配置 |
| `MOM_IDENTITY_DB_CONNECTION` | 应用数据库完整连接字符串 |
| `MOM_IDENTITY_REDIS_CONNECTION` | Redis/Sentinel 完整连接字符串，包含实际密码、Sentinel 地址及 `serviceName` |
| `MOM_AUTH_REDIS_PREFIX` | 会话与权限缓存前缀；开发与线上区分，两台线上实例保持一致 |
| `MOM_JWT_SIGNING_KEY` | 独立 Base64 密钥，解码后至少 32 字节；两台相同，后续发布复用 |
| `MOM_JWT_ISSUER`、`MOM_JWT_AUDIENCE` | 沿用当前环境的签发者和受众，两台相同 |
| `MOM_COOKIE_SECURE` | 当前 HTTP 入口为 `false`；HTTPS 为 `true` |
| `MOM_COOKIE_ALLOW_INSECURE_HTTP` | 当前 HTTP 入口为 `true`；HTTPS 为 `false` |

密码、连接字符串、JWT 密钥设置为 Masked；密码类值关闭 Expand variable reference。当前可用作用域为 `*`；若收紧作用域，后端任务的 environment 为 `production`，前端为 `production/frontend`。Protected 变量只会提供给受保护分支/标签，使用时需同步保护 `main`。

新的容器配置由 GitLab 变量注入；本地 User Secrets 不进入镜像，也不供服务器读取。服务器原有 `/etc/mom` 文件不参与当前发布。修改变量后，需要重新运行发布任务重建容器才生效。容器运行环境保存在 Docker 元数据中，查看时不要输出完整环境变量或截图传播。

## 1. 本地构建并上传

先启动本机 Docker，在 `ZX.Platform` 根目录执行，版本号换成本次新版本：

```bash
bash scripts/publish-images.sh 20261029-02
```

脚本依次构建 `linux/amd64` 的 Identity、Gateway 镜像，导出镜像包，计算 SHA256，然后提示输入 Deploy Token。粘贴后回车，输入不回显；上传显示进度，成功后输出 GitLab 要填的四项参数。脚本使用 Bash，可直接从 macOS 的 zsh 终端运行，不依赖 Python。

产物为 `output/mom-images-<版本>.tar.gz` 和同名 `.sha256`。外层包包含 `mom-images-<版本>/images.tar.gz`，其中是该版本的两个 Docker 镜像。脚本输出的 SHA256 对应外层发布包，可直接用于流水线。

本地需要 Docker/buildx、Bash、curl、tar、gzip、shasum，以及获取基础镜像与 NuGet 的网络或缓存。构建失败会停止；上传失败会保留完整发布包，并提示用 `--upload-only` 重传。代码变化使用新版本号，脚本拒绝覆盖已有版本包。

## 2. 只打包或重传

只构建打包，暂不上传：

```bash
bash scripts/publish-images.sh --package-only 20261029-02
```

上传该版本已有包，不重新构建，也不需要本地 Docker 运行：

```bash
bash scripts/publish-images.sh --upload-only 20261029-02
```

`HTTP 201` 表示上传成功，可到项目 **Deploy → Package registry** 查看 `mom-images` 对应版本。上传不会自动部署；新建流水线后仍需手动运行发布任务。不要把 `.tar.gz` 提交为 Git 文件，也不要覆盖已经发布的包。

## 3. 新建流水线并发布

1. 若有数据库结构变化，先按[迁移手册](migration-operations.md#正式库交-dba)交 DBA 执行本次增量 SQL，核对目标 Migration ID；无模型变化则跳过。两台共用数据库，SQL 执行一次。
2. 打开 [mom_img Pipelines](https://bu17gitlab.luxshare.com.cn/awe/mom_img/-/pipelines)，点击 **New pipeline**，分支选 `main`。
3. 明确填写下表四项，不沿用上次发布的版本或摘要。
4. `release_check` 自动检查包和两台节点。通过后，点击 **`deploy_mom`** 右侧的运行按钮。
5. 打开任务日志查看结果。Runner 先更新 177，启动检查通过后再更新 178。两台都成功后自动运行 **`cleanup_mom`**，清理更早的历史容器和无引用镜像。

| 参数 | 本次填写 |
| --- | --- |
| `MOM_COMPONENT` | `backend` |
| `MOM_ACTION` | `deploy` |
| `MOM_RELEASE` | 本次上传版本，例如 `20261029-02` |
| `MOM_PACKAGE_SHA256` | 本次外层压缩包的 64 位 SHA256 |

修改参数后要 **New pipeline**，Retry 旧任务会沿用原流水线参数。前后端任务由 `MOM_COMPONENT` 选择，提交说明里写“前端”不会改变任务类型。`mom-production` 资源组使发布任务串行运行。

## 4. 发布检查与运行查询

流水线校验 SHA256，导入镜像后按镜像 ID 启动，禁止临时联网拉取。旧容器保留用于回退。启动检查要求认证接口连续三次返回 `401`、业务码 `600401`，并检查容器运行与重启次数；这只证明认证链路可响应，不能替代数据库、Redis、登录和业务验证。

发布后打开 `http://10.32.15.176:8088/`，确认登录、菜单、用户/SBU 管理、刷新会话与退出。两台后端分别核对，在对应服务器执行：

```bash
sudo docker ps -a --filter name=mom-
sudo docker inspect --format '{{.Name}} {{.Config.Image}} {{.Image}} {{json .Config.Labels}}' mom-identity mom-gateway
sudo docker logs --tail 100 mom-identity
sudo docker logs --tail 100 mom-gateway
curl -i --connect-timeout 5 --max-time 10 \
  http://127.0.0.1:18080/api/identity/api/Auth/me
```

当前流程不修改 Nginx upstream 或摘流；替换容器期间可能出现短暂请求失败，不承诺不停机发布。

## 5. 回滚

在 `mom_img` **New pipeline → main**，设置 `MOM_COMPONENT=backend`、`MOM_ACTION=rollback`。`MOM_RELEASE` 保留一个格式有效的明确版本值；回滚目标由服务器保存的上一组容器决定，**不按此参数选择历史镜像**。回滚不下载包，无需重新上传或填写包摘要。

`release_check` 通过后手动运行 **`rollback_mom`**，随后核对两台节点并验证网页登录。回滚会恢复各节点上一组容器及其原环境配置；GitLab Variables 的当前值不会随回滚恢复，下一次发布前需校正变量。

普通发布失败时会尝试恢复当前节点；第二台失败时，还会尝试恢复已更新的第一台。断网、断电或强制终止可能导致恢复未完成，应依据日志分别核对节点状态。状态位于部署账号的 `$HOME/.local/state/mom-ci/`，不要随意删除 `pending`、`previous` 或事务记录。手动回滚失败时先保留现场排查，不连续重试或手工删容器。

应用回滚不撤销数据库结构或数据变更。发布前需确认旧应用与升级后的库兼容；数据库恢复另由 DBA 处理。

## 自动清理

两台发布都成功后，独立的 `cleanup_mom` 任务逐台清理，默认保留**当前运行版本及最近 2 组历史容器**。`MOM_BACKEND_HISTORY_KEEP` 在 YAML 中默认 `2`，可在 GitLab 覆盖为 `1–99`；当前及回滚容器优先保护。存在未完成事务，或节点已被其他流水线切换版本时跳过清理。

候选容器必须有本流水线的历史事务引用、名称匹配 `mom-identity.ci-*` / `mom-gateway.ci-*`，并处于停止、无挂载、无网络状态。删除后，仅清理历史记录或这些容器关联、且没有任何容器引用的镜像；带其他应用标签的镜像保留。不会调用全局 prune、强制删镜像或删除卷。事务记录本身体积小，继续保留以便追溯。

清理与发布、回滚共用资源锁。日志记录删除内容及 Docker 所在文件系统可用空间变化（会受其他进程写入影响）。清理失败只给出任务警告，不回滚已发布应用；排查后可重试 `cleanup_mom`。手工遗留容器、其他项目或旧 admin 占用需单独盘点。


## 常见问题

| 现象 | 检查 |
| --- | --- |
| 任务一直 Pending | Runner 在线状态、项目启用情况、`zxnet_runner` 标签及受保护分支设置 |
| 提示缺少变量 | 项目/Group Variables、environment scope、Protected 与 `main` 的保护状态 |
| 下载 401/403/404 | 本地上传 Token 权限、同项目包位置、版本和文件名；Runner 使用 `CI_JOB_TOKEN` |
| SHA256 错误 | 版本与外层包摘要是否配对；不要用旧摘要，改完参数新建流水线 |
| SSH/sudo 失败 | 176 到目标节点连通、Group 账号密码、目标账号 `sudo -n docker` 权限 |
| 新容器启动失败 | Identity/Gateway 日志、GitLab 运行变量、目标数据库 Migration ID |
| HTTP 登录后没有刷新 Cookie | 当前入口需 Cookie `false/true`；改变量后重新部署；协议切换后清理旧 Secure Cookie |

数据库初始化与管理员 SQL 见[数据库首次初始化](database-initialization.md)，日常增量升级见[迁移操作手册](migration-operations.md)。SQL 导出工具只在开发机运行，服务器及 Runner 发布不依赖 Python。
