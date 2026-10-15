# 开发者指南

[项目首页](../README.md) · [文档导航](README.md)

ZX.Platform 是按服务边界组织的后台基础平台。当前先把 Identity 的用户、厂区与接口链路打通，再按明确的业务需求扩展认证、权限及 MES 等服务。

## 项目范围

后端仓库用一个解决方案管理 Gateway 和服务项目；前端在独立仓库。当前可运行服务只有 Gateway 与 Identity，Identity 内部由四个项目共同组成，一个微服务并不等于一个 `.csproj`。

| 已实现            | 具体说明                                           |
| ----------------- | -------------------------------------------------- |
| Identity 基础业务 | 用户、厂区、用户厂区关系、状态、软删除与恢复       |
| 持久化            | EF Core、SQL Server、Migration、唯一索引、审计时间 |
| HTTP 契约         | 显式成功响应、DTO 校验、全局异常处理、业务错误码   |
| 提示语言          | 中文 / English，按请求头选择                       |
| 请求入口          | YARP 转发、可信代理 IP、Identity 请求限频          |
| 认证与会话        | 登录、刷新轮换、退出、当前用户、锁定及安全变更撤销 |
| 前后端接入        | 管理端用户与厂区页面使用真实查询和写入接口         |

用户批量软删除前后端均已接入，后端认证已实现，前端登录尚未联调。当前用显式管理员账号名单保护管理接口；完整角色权限、动态菜单、当前厂区权限校验、审计用户上下文和批量启停用尚未接入。会话配置与边界见[登录与会话](authentication.md)，其他计划见[后续规划](roadmap.md)。

## 目录职责

```text
ZX.Platform/
├── README.md
├── docs/
├── ZX.Platform.slnx
├── ZX.Platform.Gateway/
├── Tests/ZX.Platform.Identity.AuthChecks/
└── Services/
    └── Identity/
        ├── ZX.Platform.Identity.Api/
        │   ├── Controllers/
        │   ├── Authentication/
        │   ├── Contracts/
        │   ├── ErrorHandling/
        │   ├── Extensions/
        │   ├── Localization/
        │   ├── Options/
        │   └── Resources/Localization/
        ├── ZX.Platform.Identity.Application/
        │   ├── Authentication/
        │   ├── Common/
        │   ├── Errors/
        │   ├── Exceptions/
        │   ├── Users/
        │   ├── Factories/
        │   ├── Persistence/
        │   └── Security/
        ├── ZX.Platform.Identity.Domain/
        │   ├── Entities/
        │   ├── Auditing/
        │   └── Exceptions/
        └── ZX.Platform.Identity.Infrastructure/
            ├── Persistence/
            │   ├── Configurations/
            │   ├── Interceptors/
            │   ├── Repositories/
            │   └── Migrations/
            └── Security/
```

Visual Studio 的解决方案文件夹是逻辑分组；命令行和部署使用真实目录。当前仓库没有额外的 `src/` 层，操作路径以这棵目录为准。

## 分层和依赖方向

**Api 管通信，Application 管流程，Domain 管状态与规则，Infrastructure 管技术实现。**

| 层             | 负责                                       | 编译期项目引用               |
| -------------- | ------------------------------------------ | ---------------------------- |
| Domain         | 实体行为、审计基类、领域异常               | 不引用其他业务层             |
| Application    | 用例、DTO、业务异常、仓储与密码服务契约    | Domain                       |
| Infrastructure | EF Core、SQL Server、Repository、BCrypt、Redis 会话 | Application + Domain         |
| Api            | 路由、绑定、响应、翻译、异常映射和依赖注入 | Application + Infrastructure |
| Gateway        | 统一入口、路径转发、代理配置               | 不引用 Identity 业务项目     |

运行时的请求链：

```text
客户端 → Gateway → Api Controller → Application Service
                                      ├→ Domain 实体行为
                                      └→ IRepository / IUnitOfWork
                                            ↓ 依赖注入提供实现
                                        Infrastructure → SQL Server
```

Application 调用的仓储接口定义在自己的 `Persistence/` 下，Infrastructure 实现这些接口。运行时会使用数据库实现，编译时业务层仍不依赖 EF Core。Api 的 [Program.cs](../Services/Identity/ZX.Platform.Identity.Api/Program.cs) 负责连接接口和实现。

DTO 描述一个用例需要哪些字段，放在 Application；Api 决定这些字段通过查询参数还是 JSON 请求体进入系统。Controller 保持直观：接收参数、调用服务、显式返回 `ApiResponse`。

## 业务规则放在哪里

- 空值、长度、范围和类型：DTO / MVC 模型验证。
- 一个实体自己能判断的状态规则：Domain。例如删除用户时同时禁用、恢复时重新启用。
- 需要查询其他对象或数据库才能判断的规则：Application。例如账号重复、厂区是否存在且启用。
- 查询和登记持久化：Repository；最终提交：Unit of Work。

实体使用 `private set`、私有构造函数、`Create` 工厂和行为方法，避免服务随意赋值。`User.Create()` 只是创建业务对象，依赖注入用于服务、仓储和技术组件，两者并不冲突。

## Repository 与 Unit of Work

写入用例查询实体时由 EF Core 跟踪，调用实体方法后统一 `SaveChangesAsync`，不需要再 `Update(entity)` 把全部字段标为修改。分页查询使用 `AsNoTracking()`，适合读取并映射 DTO。

`Add(entity)` / `Remove(entity)` 只登记跟踪状态，是同步方法；真正的数据库 I/O 在保存时发生。一个用例内的多个变更尽量一次保存，例如创建用户与厂区关系。

`PlatformIdentityDbContext` 实现 `IUnitOfWork`，Api 将两者注册为同一个 scoped 实例。每次请求使用自己的 DbContext；不要在同一个 DbContext 上并行执行多个查询。数据库事务、软删除、唯一冲突的细节见[持久化专题](database.md)。

## 新增一个业务功能

以现有“修改用户显示名称”链路为参照：

1. 在 `Application/Users/Dtos/` 定义输入 DTO，字段用 `PascalCase`，验证错误使用 `Validation.*` 资源键。
2. 自身规则写入 Domain 行为方法，例如 `User.ChangeDisplayName`；需要查询的业务条件在 Service 判断。
3. 在 Service 接口和实现中接收 `CancellationToken`，通过仓储取得实体，调用行为后统一保存。
4. 只有查询需求发生变化时才扩展 Repository 契约和实现，不为每个接口再增加一层包装。
5. 在 Controller 声明 HTTP 方法、路由和绑定来源，调用服务并显式返回成功 code、提示和数据。
6. 在对应资源组补齐中英文提示，异常携带 `MessageKey` 和必要的结构化数据。
7. 新增服务 / 仓储时补充 DI；模型映射变化时增加 Migration。
8. 更新接口与持久化专题，按[验收要点](http.md#验收要点)核对成功和失败行为，再对接前端。

对应源码入口：

- [UpdateUserDisplayNameDto.cs](../Services/Identity/ZX.Platform.Identity.Application/Users/Dtos/UpdateUserDisplayNameDto.cs)。
- [UserService.cs](../Services/Identity/ZX.Platform.Identity.Application/Users/UserService.cs)。
- [User.cs](../Services/Identity/ZX.Platform.Identity.Domain/Entities/User.cs)。
- [UsersController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/UsersController.cs)。

如果新增独立业务服务，再建立对应的 Api、Application、Domain、Infrastructure 项目、服务自己的数据库和 Gateway 路由。跨服务不直接读写对方的表；服务拆分以业务边界和部署需求为依据。

## 启动与中间件

Identity 启动时注册代理、本地化、频率限制、认证授权、MVC 校验、异常处理、DbContext 和业务服务。数据库 / Redis 连接配置缺失、JWT 签名密钥缺失或非法、限流 / 代理 / 认证配置非法时启动失败。JWT 使用标准库签发及 AddJwtBearer 验证，JwtSessionEvents 在验证后核对 Redis 当前会话；Redis 连接与会话存储为单例，业务服务与 DbContext 为 scoped。默认管理员名单为空。密钥配置、会话规则和明确接受的并发边界见[登录与会话](authentication.md)。

开发环境 Swagger 单独提供。业务请求的主要中间件顺序：

```text
UseForwardedHeaders
→ UseRequestLocalization
→ UseExceptionHandler
→ UseHttpsRedirection
→ UseRouting
→ UseRateLimiter
→ UseAuthentication
→ UseAuthorization
→ Controller
```

先恢复客户端 IP 再限流；先设置语言再翻译响应；限流依赖匹配的接口元数据，因此在 `UseRouting()` 后执行。`UseRequestLocalization()` 还需要配套服务注册和资源，流程见[多语言专题](localization.md)。

## 开发约定

- 优先普通 class 和直接可读的代码；DTO 使用 `Dto` 后缀，业务术语统一为“厂区”。
- 类型、公开成员用 `PascalCase`，参数和局部变量用 `camelCase`，私有字段用 `_camelCase`。
- 业务异步 I/O 方法以 `Async` 结尾，显式接收并继续传递 `CancellationToken`；框架 override 可遵循框架签名。
- 只转交底层 `Task` 可以直接返回；需要处理结果、顺序调用或捕获异步异常时使用 `async/await`。
- 不机械地为每张表建立 Service / Repository，也不提前引入 CQRS、Mediator 或无真实用途的通用抽象。
- API 配置按职责放在 `Extensions/`，避免把本地化、校验和限流全部塞进一个文件；业务注册仍保持可读。
- 当前不使用导航属性和物理外键，关系由显式查询及业务用例维护；模型变化必须同步 Migration。
- HTTP 使用标准状态码，`600xxx` 只在 JSON 业务码中出现；特殊文件或流接口按自己的协议返回。
- 只格式化本次修改文件，避免把业务调整和整仓格式变更混在一起。检查命令见[开发环境](development.md#开发与检查命令)。

当前仓库尚未配置 `.editorconfig`、共享 Git Hook 或 CI 格式检查；这些是后续工程建设项。
