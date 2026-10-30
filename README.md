# ZX.Platform

使用 .NET 10、ASP.NET Core、YARP、EF Core 和 Oracle 构建的后台基础平台。当前后端仓库包含 Gateway 与 Identity 微服务，供后续用户、SBU、权限和业务模块继续开发。

管理端在独立的 [ZX.front.vue 仓库](https://github.com/blairzheng/ZX.front.vue)维护。

## 当前能力

- Gateway：按 `/api/identity` 前缀转发到 Identity，处理可信代理提供的客户端 IP。
- 用户：新增、详情、分页筛选、修改显示名称、启停用、单条与批量软删除、恢复、调整SBU关系、管理员强制下线。
- SBU：新增、详情、列表查询（不分页，一次返回全部SBU）、修改名称、启停用、软删除、恢复、查询全部启用SBU。
- 数据访问：EF Core Migration、数据库唯一约束、审计时间、软删除过滤、一次保存提交业务变更。
- API：统一 JSON 响应、参数校验、异常映射、中文 / English 提示和 `traceId`。
- 运行日志：请求摘要、耗时与 TraceId，开发文本 / 生产 JSON 控制台输出，当前部署由 Docker 日志驱动收集。
- 接口限频：同一 IP + HTTP 方法 + 路由模板，默认 3 秒分段滑动窗口最多 5 次，超限立即返回 `429 / 600429`。
- 认证：JWT Access Token、随机 Refresh Token、Redis 单账号单会话、确认接管、刷新轮换、退出、当前用户、账号锁定、自助改密码及会话撤销。
- 授权：用户多角色、角色功能权限、权限目录自动同步、Redis 权限缓存及版本失效、前端菜单/页面/按钮权限。

管理接口按角色功能权限授权。首个管理员由运维单独执行本地 SQL 创建，后续通过用户角色分配维护，见[角色与权限](docs/authorization.md)。动态菜单管理与角色菜单授权树已接入。SBU数据权限、当前用户审计和批量启停用尚未实现。Identity 实例共享 Redis 会话及权限缓存，不使用用户级分布式锁；并发登录与安全变更边界见[登录与会话](docs/authentication.md)。MOM 业务与文件服务属于后续规划，未设置固定总并发门槛或统一后端执行超时。

前端用户管理与SBU管理均已接入 Identity 的查询和写入接口，其中用户批量删除前后端都已接入。批量启停用接口仍未实现，批量操作入口不提供。前端接入方式见[统一请求层](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/http.md)，页面规范从[前端文档导航](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/README.md)查阅。

前端登录及角色权限已完成代码接入，Oracle 后端切换后，Gateway 与浏览器完整联调仍需执行，不能视为已通过端到端验证。

## 快速开始

需要 .NET 10 SDK，以及可访问的 Oracle 19c 及以上和 Redis（模型统一使用 19c）。以下命令在仓库根目录执行。团队开发默认使用共享测试库和 Redis；选择本机 Redis 时，可用 Docker 启动默认监听 `127.0.0.1:6379` 的开发实例：

```powershell
docker compose -f compose.redis.yml up -d
```

远程 Redis 和密码通过 User Secrets 设置，见[Redis 配置](docs/authentication.md#redis-启动与故障)。

```powershell
dotnet restore ZX.Platform.slnx
dotnet dev-certs https --trust
```

Identity 启动前需要数据库连接。团队开发默认使用共享测试库和开发 Redis，首次拉取后按[开发环境配置](docs/development.md#数据库配置)将团队提供的连接信息写入本机 User Secrets。连接信息不提交到仓库；若需使用个人本机 Oracle，按[本机基础设施](docs/local-infrastructure.md)配置。

Development 已提供共享开发 JWT 密钥，已有 User Secrets 可继续覆盖；生产必须注入独立密钥。共享测试库的结构迁移和首个管理员 SQL 由团队指定人员初始化一次；其他开发者配置本机 User Secrets 后，使用分配的测试账号连接共享库。Identity 启动时仅同步权限目录和默认菜单。登录后用 Access Token 调用管理接口，Swagger 提供 Authorize 入口。

在两个终端分别启动：

```powershell
dotnet run --project Services/Identity/ZX.Platform.Identity.Api --launch-profile https
```

```powershell
dotnet run --project ZX.Platform.Gateway --launch-profile https
```

| 入口             | 默认开发地址                                                                   |
| ---------------- | ------------------------------------------------------------------------------ |
| Identity Swagger | `https://localhost:7075/swagger`                                               |
| Identity API     | `https://localhost:7075/api/Users/getUserList?page=1&pageSize=20`              |
| Gateway 转发入口 | `https://localhost:7178/api/identity/api/Users/getUserList?page=1&pageSize=20` |

端口以各项目的 `Properties/launchSettings.json` 为准。Gateway 目标配置和前端代理地址需要同步；完整步骤见[开发环境](docs/development.md)。

日常发布在仓库根目录执行 `bash scripts/publish-images.sh <新版本号>`，本地构建并上传后，再在 GitLab 手动发布。只打包、重传及流水线参数见[Runner 发布说明](docs/deployment.md)。

## 开发文档

换电脑或新助手接续开发时，先读[项目交接说明](docs/project-handoff.md)。

第一次接手先读[开发者指南](docs/developer-guide.md)，再按任务从[文档导航](docs/README.md)查阅。

| 文档                                        | 用途                                       |
| ------------------------------------------- | ------------------------------------------ |
| [开发者指南](docs/developer-guide.md)       | 项目范围、目录、分层、调用链与新增功能流程 |
| [开发环境、命令与排错](docs/development.md) | SDK、配置、启动、Gateway、Git 与部署边界   |
| [数据库与持久化](docs/database.md)          | 实体、审计、软删除、唯一约束与 Migration   |
| [Identity 接口契约](docs/api.md)            | 用户和SBU路由、DTO、分页与错误负载        |
| [Identity 业务语义](docs/identity.md)       | 状态、SBU关系与当前能力边界               |
| [API 响应与错误](docs/http.md)              | HTTP / 业务码、异常、校验与取消信号        |
| [运行与接口日志](docs/logging.md)          | 请求摘要、TraceId、异步输出与 Docker 日志查询 |
| [GitLab Runner 发布](docs/deployment.md) | 本地镜像打包上传、运行变量、手动发布与回滚 |
| [当前部署拓扑](docs/deployment-topology.md) | 176 入口和 Runner、177/178 后端、共享数据库与 Redis |
| [数据库初始化](docs/database-initialization.md) / [迁移操作](docs/migration-operations.md) | 首次建表、管理员 SQL、增量 SQL 交 DBA |
| [API 多语言](docs/localization.md)          | 请求语言、资源分组、消息键与扩展步骤       |
| [接口限流](docs/rate-limiting.md)           | 滑动窗口、客户端 IP、配置和验收            |
| [登录与会话](docs/authentication.md)       | Redis、接管、刷新、下线及并发边界          |
| [角色与权限](docs/authorization.md) | 模型、权限目录、本机 SQL 初始化及缓存失效 |
| [后续规划与设计取舍](docs/roadmap.md)       | 认证授权、多租户、MES 幂等、日志与部署     |
| [低代码平台契约](docs/lowcode/README.md)    | 元数据、字段类型、运行时、权限与 SBU 范围、发布与退役 |

## 文档维护

README 维护项目现状与入口；开发者指南维护架构与开发流程；专题维护具体契约。改动接口、模型、配置或公共行为时同步对应专题，已实现能力和后续规划分别记录。

原开发交接记录与前端迁移方案保留在 [docs/history](docs/history/README.md)，用于追溯过去的讨论，不作为当前实现说明。
