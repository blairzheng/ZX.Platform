# ZX.Platform

使用 .NET 10、ASP.NET Core、YARP、EF Core 和 SQL Server 构建的后台基础平台。当前后端仓库包含 Gateway 与 Identity 微服务，供后续用户、厂区、权限和业务模块继续开发。

管理端在独立的 [ZX.front.vue 仓库](https://github.com/blairzheng/ZX.front.vue)维护。

## 当前能力

- Gateway：按 `/api/identity` 前缀转发到 Identity，处理可信代理提供的客户端 IP。
- 用户：新增、详情、分页筛选、修改显示名称和密码、启停用、单条与批量软删除、恢复、调整厂区关系。
- 厂区：新增、详情、分页查询、修改名称、启停用、软删除、恢复、查询全部启用厂区。
- 数据访问：EF Core Migration、数据库唯一约束、审计时间、软删除过滤、一次保存提交业务变更。
- API：统一 JSON 响应、参数校验、异常映射、中文 / English 提示和 `traceId`。
- 接口限频：同一 IP + HTTP 方法 + 路由模板，默认 3 秒分段滑动窗口最多 5 次，超限立即返回 `429 / 600429`。

**登录、JWT、角色权限、动态菜单、当前用户审计和批量启停用接口尚未实现。** 现有 `UseAuthorization()` 不代表已经完成权限控制。MES、SPC 和文件服务属于后续规划。当前限流只控制请求频率，未设置固定总并发门槛或统一后端执行超时。

前端用户管理已接入 Identity 的查询和写入接口，批量删除接口已在后端实现、前端仍待接入，批量操作入口暂时禁用；前端接入方式见[统一请求层](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/http.md)，页面规范从[前端文档导航](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/README.md)查阅。

## 快速开始

需要 .NET 10 SDK，以及可访问的 SQL Server。以下命令在仓库根目录执行。

```powershell
dotnet restore ZX.Platform.slnx
dotnet dev-certs https --trust
```

Identity 启动前必须设置 `ConnectionStrings:IdentityDatabase`，开发环境使用 User Secrets：

```powershell
dotnet user-secrets set "ConnectionStrings:IdentityDatabase" "Server=127.0.0.1,14330;Database=ZX_PLATFORM_IDENTITY;User Id=<数据库账号>;Password=<数据库密码>;TrustServerCertificate=True" --project Services/Identity/ZX.Platform.Identity.Api
```

替换示例中的账号和密码，并按[数据库迁移说明](docs/database.md#迁移命令)初始化或更新数据库。连接信息保存在本机，不提交到仓库。

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

## 开发文档

第一次接手先读[开发者指南](docs/developer-guide.md)，再按任务从[文档导航](docs/README.md)查阅。

| 文档                                        | 用途                                       |
| ------------------------------------------- | ------------------------------------------ |
| [开发者指南](docs/developer-guide.md)       | 项目范围、目录、分层、调用链与新增功能流程 |
| [开发环境、命令与排错](docs/development.md) | SDK、配置、启动、Gateway、Git 与部署边界   |
| [数据库与持久化](docs/database.md)          | 实体、审计、软删除、唯一约束与 Migration   |
| [Identity 接口](docs/identity.md)           | 用户和厂区路由、DTO、分页、状态及厂区关系  |
| [API 响应与错误](docs/http.md)              | HTTP / 业务码、异常、校验与取消信号        |
| [API 多语言](docs/localization.md)          | 请求语言、资源分组、消息键与扩展步骤       |
| [接口限流](docs/rate-limiting.md)           | 滑动窗口、客户端 IP、配置和验收            |
| [后续规划与设计取舍](docs/roadmap.md)       | 认证授权、多租户、MES 幂等、日志与部署     |

## 文档维护

README 维护项目现状与入口；开发者指南维护架构与开发流程；专题维护具体契约。改动接口、模型、配置或公共行为时同步对应专题，已实现能力和后续规划分别记录。

原开发交接记录与前端迁移方案保留在 [docs/history](docs/history/README.md)，用于追溯过去的讨论，不作为当前实现说明。
