# ZX.Platform 开发交接文档

前端管理端的独立开发和验收要求见 [`FRONTEND.DEVELOPMENT.md`](./FRONTEND.DEVELOPMENT.md)。

> 本文用于记录 ZX.Platform 的项目目标、架构设计、技术选择、已完成内容、讨论结论和后续计划。
>
> 新开 Codex/ChatGPT 窗口后，应先完整阅读本文，再检查实际代码。本文记录的是截至 2026-09-17 的讨论结论；由于原项目位于 `E:\myproject\ZX.Platform`，当前会话尚未读取并核验其最新代码，所以“正在进行”部分必须以实际文件为准。

## 1. 项目定位

项目名称：

```text
ZX.Platform
```

项目目标：

> 建设一个公司级后台基础平台。以后新增 Identity、MES、SPC、日志、文件、消息等业务时，都可以在这个平台的架构基础上继续开发。

这不是对旧单体项目进行简单搬运，而是一个用于学习和验证微服务架构的新项目。旧项目可以作为业务规则和功能需求的参考，但不应直接把整个单体项目塞进某个微服务。

当前阶段最重要的目标不是一次性拆出很多服务，而是先完整打通一个 Identity 微服务，理解并验证：

- 微服务的项目组织方式；
- 分层和依赖方向；
- Gateway 到微服务的请求转发；
- EF Core、Migration 和数据库；
- 领域模型、Application Service、Repository、Unit of Work；
- 统一接口响应和异常处理；
- 后续认证、授权、多租户和部署的扩展方式。

未来预计包含的业务能力：

```text
ZX.Platform
├── Gateway       统一入口和路由
├── Identity      用户、厂区、认证、授权
├── MES           制造执行和数据追溯
├── SPC           统计过程控制
├── Audit         操作审计和变更历史
├── Files         文件与附件
└── Notifications 消息和通知
```

这些边界只是方向，不要求现在全部创建。只有业务边界、独立部署或扩缩容需求足够明确时，才新增微服务。

## 2. 当前技术栈和环境

当前选择：

- .NET 10；
- ASP.NET Core Web API；
- 一个代码仓库和一个总解决方案；
- YARP Gateway；
- EF Core 10；
- SQL Server 2022；
- Swagger/OpenAPI；
- BCrypt 密码哈希；
- Visual Studio；
- WSL + Docker 用于本地基础设施。

数据库环境：

```text
Docker 容器：sqlserver-test
镜像：mcr.microsoft.com/mssql/server:2022-latest
主机：127.0.0.1
主机端口：14330
容器端口：1433
Identity 数据库：ZX_PLATFORM_IDENTITY
```

连接字符串保存在 User Secrets，不提交到仓库中的 `appsettings.json`。

最初考虑使用 Oracle，但拉取 Oracle 镜像时遇到 WSL/DNS/代理问题。为了优先完成架构学习，当前改用已经运行的 SQL Server。架构不应绑定死 SQL Server，将来可以按微服务需要选择 Oracle、SQL Server、MySQL 或其他数据库。

## 3. 总体组织方式

当前采用 Monorepo：

```text
一个代码仓库
└── 一个总解决方案
    ├── Gateway
    └── 多个微服务
        └── 每个微服务内部再分层
```

当前项目：

```text
ZX.Platform.slnx
├── ZX.Platform.Gateway
├── ZX.Platform.Identity.Api
├── ZX.Platform.Identity.Application
├── ZX.Platform.Identity.Domain
└── ZX.Platform.Identity.Infrastructure
```

推荐的物理目录方向：

```text
src/
├── Gateway/
│   └── ZX.Platform.Gateway/
└── Services/
    └── Identity/
        ├── ZX.Platform.Identity.Api/
        ├── ZX.Platform.Identity.Application/
        ├── ZX.Platform.Identity.Domain/
        └── ZX.Platform.Identity.Infrastructure/
```

Visual Studio 的“解决方案文件夹”只是逻辑分组，不一定会在磁盘上创建真实文件夹。为了在资源管理器、命令行和 CI 中也容易识别，真实目录也应按服务分组。

一个微服务不等于一个 `.csproj`。Identity 微服务由 Api、Application、Domain、Infrastructure 四个项目共同组成。

未来如果新增 MES，可以使用：

```text
ZX.Platform.Mes.Api
ZX.Platform.Mes.Application
ZX.Platform.Mes.Domain
ZX.Platform.Mes.Infrastructure
```

不要为了“看起来像微服务”提前创建很多空服务，也不要把每张表单独做成一个微服务。

## 4. 分层职责和依赖方向

当前依赖关系：

```text
Domain          ← 不引用其他业务层项目
  ↑
Application     → Domain
  ↑
Infrastructure  → Application + Domain
  ↑
Api             → Application + Infrastructure

Api 如需在全局异常处理器中直接识别 DomainException，
可以额外引用 Domain。
```

依赖原则：

- 内层不知道外层；
- Domain 不知道 HTTP、Controller、数据库、EF Core 或 Application；
- Application 描述业务用例，并依赖 Domain 中的实体；
- Application 定义 Repository、Unit of Work、密码哈希等抽象；
- Infrastructure 实现数据库访问、BCrypt 等技术细节；
- Api 负责 HTTP 协议、参数绑定、响应格式和异常到 HTTP 的映射；
- Gateway 只负责入口和转发，不直接访问 Identity 数据库。

各层职责：

### Domain

- `User`、`Factory`、`UserFactory` 等领域实体；
- 实体自身能够判断的业务规则；
- 审计和软删除基类；
- `DomainException`；
- 不引用 Application。

### Application

- 业务用例编排；
- DTO；
- Service 接口和实现；
- Repository、Unit of Work、PasswordHasher 等接口；
- 查询多个对象后才能判断的业务规则；
- `BusinessException` 和通用业务错误码。

### Infrastructure

- `PlatformIdentityDbContext`；
- EF Core Configuration；
- Repository 实现；
- Unit of Work 实现；
- BCrypt 实现；
- Migration；
- SQL Server 等具体技术。

### Api

- Controller；
- HTTP 状态码；
- `ApiResponse<T>`；
- 参数校验响应；
- `GlobalExceptionHandler`；
- Swagger/OpenAPI；
- 依赖注入注册。

### Gateway

- 使用 YARP 提供统一入口；
- 把请求转发到 Identity 等微服务；
- 后续可增加认证、限流、负载均衡、日志和路由策略；
- 不承载具体业务规则。

### 4.1 四层职责的直观理解

可以记成：**Api 管通信，Application 管流程，Domain 管规则和状态，Infrastructure 管技术实现。**

- Api 与客户端和 Application 打交道。它负责路由、GET/POST/PUT、参数绑定、HTTP 状态码、`ApiResponse` 和异常到 HTTP 的转换，然后调用 Application Service 完成业务操作。
- Application 实现业务用例的流程编排。例如新增用户时，先检查账号是否存在，再创建用户、登记待新增数据并统一保存。Application 不直接使用 EF Core，而是先定义 `IUserRepository` 等契约，描述该业务流程需要哪些数据访问能力。
- Domain 定义实体具有哪些属性、状态和行为，并保护实体自身能够判断的规则。例如 `User.Disable()`、`Factory.ChangeName()` 和 `Restore()`。
- Infrastructure 实现 Application 定义的技术契约，负责 EF Core、DbContext、Repository、SQL Server、BCrypt、Migration 和审计拦截器等具体技术。

“Api 规定客户端给什么数据”需要再区分两层含义：Api 决定数据如何通过 HTTP 进入系统，例如来自请求体、查询参数还是路由参数；Application DTO 描述一个业务用例需要哪些输入和输出字段。因此当前 DTO 放在 Application，由 Api 用于模型绑定并传给 Application Service，是合理的。

运行时的调用链路是：

```text
客户端
  ↓
Api Controller
  ↓
Application Service
  ↓
IRepository（Application 定义契约）
  ↓
Repository（Infrastructure 提供实现）
  ↓
DbContext / 数据库

Application Service
  ↓
Domain 实体方法
```

编译期的项目引用关系是：

```text
Application    → Domain
Infrastructure → Application + Domain
Api            → Application + Infrastructure
Domain         → 不引用其他业务层项目
```

运行时虽然是 Application Service 调用了 Infrastructure 中的 Repository 实现，但 Application 在编译时只依赖自己定义的接口。Api 在程序启动时通过依赖注入把接口和实现连接起来：

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

因此更完整的关系可以表示为：

```text
客户端 → Api → Application → Domain
                   ↓
             IRepository 契约
                   ↑
           Infrastructure 实现
                   ↓
                数据库
```

## 5. 为什么当前不直接把所有东西做成微服务

微服务不是“多个小项目”的同义词，它首先是一种独立业务边界和独立部署方式。

微服务通常会增加：

- 多进程和多服务运维；
- 服务发现、Gateway、配置管理；
- 网络超时、重试、幂等；
- 分布式事务和最终一致性；
- 日志、指标和链路追踪；
- Docker、CI/CD，规模变大后还可能需要 Kubernetes；
- 跨服务版本兼容。

因此当前路线是：

```text
先把 Identity 做成边界清晰、可独立部署的服务
    ↓
跑通分层、数据库、异常和响应
    ↓
再添加认证、授权、审计
    ↓
等 MES/SPC 业务边界明确后再增加服务
```

同一台开发机、同一台数据库服务器仍然可以运行多个微服务。微服务的“独立”首先是代码、部署和数据所有权独立，不代表每个服务从第一天起就必须购买一台物理服务器。

## 6. Gateway 和本地运行

YARP 是微软 .NET 生态中的反向代理组件。当前请求路径是：

```text
客户端
  ↓
ZX.Platform.Gateway
  ↓
ZX.Platform.Identity.Api
```

YARP 与 Nginx 的部分能力相似，但 YARP 可以直接集成到 ASP.NET Core 项目中；Nginx 通常作为独立基础设施部署。

Identity API 曾使用以下开发地址：

```text
https://localhost:7131
http://localhost:5025
```

实际端口必须以当前 `launchSettings.json` 为准。

Visual Studio 可以通过“多个启动项目”同时启动 Gateway 和 Identity.Api，也可以分别执行：

```bash
dotnet run
```

本地没有多台服务器并不影响微服务学习。可以用多个本地进程、不同端口、WSL 和 Docker 模拟分布式部署。

## 7. 数据库与分布式讨论结论

### 7.1 数据库分布式解决什么问题

数据库分布式主要解决：

- 单机容量上限；
- 读写吞吐上限；
- 高可用；
- 跨地域访问和容灾。

“吞吐”指单位时间内数据库能够处理的查询、插入、更新、删除和事务数量，不只是存储容量。

### 7.2 一致性

多台数据库服务器的数据一致性不能靠业务代码随意复制，通常依赖成熟方案：

- 主从复制；
- 同步复制或异步复制；
- 数据库集群；
- 分片中间件；
- 共识协议；
- 消息和补偿机制；
- 数据库产品自带的高可用方案。

Oracle RAC 是多个实例访问同一份共享存储。它可以扩展计算和连接处理能力并提高可用性，但共享存储本身仍可能成为 IOPS 瓶颈。遇到共享存储瓶颈时，需要从存储硬件、缓存、SQL 和索引优化、数据分区、冷热分层，甚至无共享分片架构等方面处理。

不同数据库的成熟方案不同：

- Oracle：RAC、Data Guard、分区等；
- SQL Server：Always On、复制、分区等；
- MySQL：主从复制、Group Replication、InnoDB Cluster、分库分表方案等。

当前项目不需要一开始就引入数据库集群。先做好索引、SQL、分页、归档、数据生命周期和业务边界，再根据真实瓶颈演进。

### 7.3 微服务的数据所有权

开发阶段可以在同一个 SQL Server 实例中创建多个数据库：

```text
Identity → ZX_PLATFORM_IDENTITY
MES      → ZX_PLATFORM_MES
SPC      → ZX_PLATFORM_SPC
```

微服务之间不应该直接读写其他服务的表。服务间通信应使用：

- HTTP；
- gRPC；
- 消息队列；
- 领域事件或集成事件。

## 8. Identity 数据模型

当前实体：

```text
User
Factory
UserFactory
```

关系：

```text
User N ── N Factory
      UserFactory
```

`UserFactory` 是用户和厂区之间的关系实体，而不是没有业务含义的普通列表。

示例：

```text
用户1：厂区1、厂区2、厂区3
修改为：
用户1：厂区1、厂区5
```

合理的关系更新方式是计算差异：

- 保留厂区1；
- 撤销厂区2和厂区3；
- 新增厂区5；
- 不需要把厂区1先删掉再重新插入。

整套操作由 Application Service 编排，并通过一次 `SaveChangesAsync` 尽量放在同一事务中提交。

## 9. 厂区与多租户

公司的实际场景是：

- 一个厂区一个数据库；
- 或几个厂区共用一个数据库；
- 用户可能被分配到一个或多个厂区；
- 前端需要选择当前操作厂区。

这属于多租户/数据隔离问题的一种形式。当前可以先把 `Factory` 当作厂区边界，`UserFactory` 表示用户有权访问哪些厂区。

不能只在 `User` 表中保存一个 `FactoryId`，因为用户可能属于多个厂区。

前端选择厂区后可以把 `FactoryId` 传给服务端，但服务端必须重新验证：

```text
前端选择 FactoryId
    ↓
服务端识别当前登录用户
    ↓
查询 UserFactory
    ↓
确认该用户拥有 FactoryId 的访问权限
    ↓
继续业务操作
```

不能信任前端传来的厂区编号，否则修改请求参数可能造成越权访问。

以后如果不同厂区对应不同数据库，还需要设计：

- 租户/厂区到连接信息的映射；
- 当前请求的租户上下文；
- 连接选择策略；
- 跨厂区查询策略；
- 不允许跨库事务时的业务边界。

当前阶段先不急着实现动态切库，先完成 Identity 基础能力。

### 9.1 跨微服务权限边界（后续规划）

当前只记录设计方向，不在用户基础功能阶段提前实现完整权限系统：

- Identity 负责用户身份、厂区归属、角色和授权数据；`UserFactory` 只表示厂区访问权，不能代替功能权限。
- 各业务服务定义并检查自己的权限代码，例如 `mes.orders.read`、`spc.reports.read`；MES 等服务还应自行判断工单归属等业务规则。
- 后续由 Identity 签发 Token；Gateway 可以做入口校验，但 Identity、MES、SPC 等服务也必须独立验证 Token 并执行授权，不能只信任 Gateway。
- 前端传入的当前 `FactoryId` 仅是用户选择，各服务必须核实该用户对此厂区有访问权及相应权限；服务间不直接读取其他服务的数据库。
- 权限获取的初期候选方案是业务服务按用户和厂区向 Identity 查询并短时缓存；缓存失效且无法确认权限时拒绝授权。权限放入 Token、权限变更事件同步等方案，留待实际需求和规模明确后再评估。

实施顺序仍是先完成用户、厂区及用户厂区授权，再设计登录、Token、角色与权限；具体数据模型、缓存失效和服务间认证尚未定稿。

## 10. 领域模型风格

当前选择充血模型，而不是只有公开属性的贫血实体。

核心原则：

```text
实体             维护自身状态和自身能够判断的规则
Application Service 编排跨实体、跨仓储的业务流程
Repository       查询和登记持久化
Unit of Work     统一提交
```

可以记成：**Repository 管数据访问，Service 管业务流程，实体管自身状态和规则，Unit of Work 管保存。**

以修改用户显示名称为例：

1. Api 接收请求，建议通过 DTO 校验参数，再调用 `UserService`。
2. `UserService` 通过 `IUserRepository.GetByIdAsync` 查用户；不存在时返回业务上的“用户不存在”。
3. `UserService` 调用 `user.ChangeDisplayName(displayName)`；`User` 自己保证名称符合实体规则并修改自身状态。
4. `UserService` 调用 `IUnitOfWork.SaveChangesAsync`，提交本次修改。

Repository 不负责判断“用户不存在该如何响应”，Service 不直接操作 EF Core 或绕开实体修改属性。当前 `GetByIdAsync` 返回的实体由 EF Core 跟踪，因此调用实体方法后直接保存即可；不必再次查询，也不必调用 `DbSet.Update(user)` 将整个实体标记为修改。Repository 也不只是查询：例如 `Add(user)` 会把新实体加入待保存状态，真正写入数据库仍发生在 `SaveChangesAsync`。

实体通常采用：

- `sealed`，明确当前不用于继承；
- `private set`，避免外部任意修改状态；
- 私有构造函数，防止创建不完整实体，同时允许 EF Core 还原对象；
- 静态 `Create` 工厂方法，统一创建规则；
- `Enable`、`Disable`、`ChangePasswordHash` 等行为方法。

静态 `User.Create()` 与依赖注入没有冲突。依赖注入用于 Service、Repository、DbContext、Logger、密码服务和外部客户端；实体本身是业务对象，正常情况下直接创建。

示意：

```csharp
public sealed class User : SoftDeletableEntity
{
    private User()
    {
    }

    public string PasswordHash { get; private set; } = string.Empty;

    public static User Create(
        string normalizedAccount,
        string displayName,
        string passwordHash)
    {
        // 校验并创建完整实体
        return new User
        {
            // 赋值
        };
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("密码哈希不能为空。");
        }

        PasswordHash = passwordHash;
    }
}
```

不是所有规则都放进实体：

- 实体自己就能判断的规则放 Domain；
- 必须查询数据库、访问其他实体或调用外部服务才能判断的规则放 Application Service；
- Controller 不承载业务规则。

## 11. 审计和软删除

通用审计基类：

```text
AuditableEntity
├── CreatedAtUtc
├── CreatedByUserId
├── UpdatedAtUtc
└── UpdatedByUserId
```

支持软删除的基类：

```text
SoftDeletableEntity : AuditableEntity
├── IsDeleted
├── MarkDeleted()
├── Restore()
├── OnDeleted()
└── OnRestored()
```

当前决定：

```text
User        → 软删除
Factory     → 软删除
UserFactory → 不软删除，使用 IsEnabled 表示授权是否有效
```

软删除不是所有表都必须使用。频繁导入、频繁删除且历史价值低的数据，如果全部软删除，会造成无效数据持续增长，影响索引、查询和维护。应按业务选择：

- 需要恢复、审计和追溯的主数据可以软删除；
- 纯临时数据、关系快照或可重建数据可以物理删除；
- 需要长期追溯的 MES 数据不能只依赖软删除，应建立版本和历史机制。

`User` 和 `Factory` 使用 EF Core 全局查询过滤器排除 `IsDeleted` 数据。

查询账号是否可创建时，必须使用包含已删除数据的查询，因为当前规则是：

> 用户软删除后，原账号仍然不能被新用户占用；应恢复原用户或使用新账号。

`NormalizedAccount` 保持全表唯一，避免历史数据、日志和业务关联产生身份歧义。

## 12. MES 数据追溯结论

MES 对追溯要求高，未来需要把以下能力作为正式架构内容，而不是仅依赖应用日志：

- 谁在什么时间修改了什么；
- 修改前值和修改后值；
- 操作原因；
- 数据版本；
- 审批记录；
- 工艺、批次、生产过程追溯；
- 业务补偿；
- 版本回退或版本切换。

数据库事务只能回滚尚未提交的操作。已经提交的数据不能靠普通事务“回到过去”，需要：

- 历史表；
- 数据版本；
- 事件记录；
- 审计日志；
- 补偿操作；
- 必要时的事件溯源。

应用版本可以回滚，不代表数据库结构和业务数据可以自动回滚。将来做 MES 时，数据库 Migration 必须考虑向前兼容、回滚脚本和数据迁移策略。

## 13. EF Core 决策

当前使用：

- EF Core 10；
- SQL Server Provider；
- Fluent API Configuration；
- Migration；
- `PlatformIdentityDbContext`。

当前不使用：

- 数据库物理外键；
- 实体导航属性；
- 自动 Include；
- 为 Migration 自动移除外键的自定义 `ModelDiffer`。

实体之间保留逻辑关联字段：

```text
UserFactory.UserId
UserFactory.FactoryId
```

查询时由 Repository 使用显式 `join`，关系一致性由 Application Service 维护。

放弃导航属性的原因：

- 导航属性、全局软删除过滤器和必需关系组合时产生额外警告和隐式行为；
- 当前希望明确看见每次联表查询；
- 不希望 Migration 生成物理外键；
- 避免大型模型中 Include 链逐渐失控。

这不是说导航属性本身不好，而是当前项目为了学习和控制复杂度作出的选择。以后有明确收益时可以重新评估。

EF Core 配置中的关键含义：

```csharp
builder.HasKey(x => x.Id);
```

表示配置主键。

```csharp
builder.Property(x => x.Id)
    .HasColumnName("ID")
    .ValueGeneratedNever();
```

表示映射为 `ID` 列，并且编号由应用生成，数据库不自增。当前实体主要使用 `Guid`。

Migration 变更模型后必须新增 Migration，再执行 `Update-Database`；不能通过忽略 `PendingModelChangesWarning` 掩盖模型变化。

## 14. Repository 和 Unit of Work

Application 中定义抽象，Infrastructure 中提供实现。

当前目录方向：

```text
ZX.Platform.Identity.Application/
├── Common/
│   └── PageQueryDto.cs
├── Errors/
├── Exceptions/
├── Factories/
│   ├── Dtos/
│   │   ├── CreateFactoryDto.cs
│   │   ├── FactoryDetailDto.cs
│   │   ├── FactoryPageDto.cs
│   │   ├── FactoryQueryDto.cs
│   │   └── UpdateFactoryNameDto.cs
│   ├── IFactoryService.cs
│   └── FactoryService.cs
├── Persistence/
│   ├── IFactoryRepository.cs
│   ├── IUserRepository.cs
│   └── IUnitOfWork.cs
├── Security/
│   └── IPasswordHasher.cs
└── Users/
    ├── Dtos/
    │   ├── CreateUserDto.cs
    │   ├── UpdateUserDisplayNameDto.cs
    │   ├── UpdateUserPasswordDto.cs
    │   ├── UserDetailDto.cs
    │   ├── UserPageDto.cs
    │   └── UserQueryDto.cs
    ├── IUserService.cs
    └── UserService.cs
```

`IUserRepository` 示例：

```csharp
Task<User?> GetByIdAsync(
    Guid userId,
    CancellationToken cancellationToken);

Task<User?> GetByNormalizedAccountAsync(
    string normalizedAccount,
    CancellationToken cancellationToken);

void Add(User user);
```

`Add` 不使用异步，因为它只把实体加入 EF Core 内存跟踪，不产生数据库 I/O。真正访问数据库的是：

```csharp
await _unitOfWork.SaveChangesAsync(cancellationToken);
```

### 14.1 为什么接口和实现位于不同项目

Repository 接口由需要它的 Application 层定义，具体实现放在 Infrastructure 层：

```text
Application
└── IUserRepository       描述业务用例需要哪些数据访问能力

Infrastructure
└── UserRepository        使用 EF Core 实现这些能力
```

`UserService` 只依赖 `IUserRepository`，不知道 EF Core、SQL Server 或具体实现类。Infrastructure 引用 Application，并通过 `UserRepository : IUserRepository` 实现该约定。Api 在启动时通过依赖注入把接口和实现连接起来：

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

这里的关键是编译期依赖方向，而不是运行时谁调用谁：

```text
Application Service → IUserRepository
                          ↑
                 UserRepository
```

如果把 `IUserRepository` 也放进 Infrastructure，Application 为了使用它就必须反向引用 Infrastructure，使内层依赖数据库技术层。当前组织方式让业务流程不依赖 EF Core，并允许在不修改 Application 的情况下替换或测试数据访问实现。

这种模式不应机械地套到每张表。只有存在真实业务能力和数据访问需求时才创建 Repository 接口和实现；Factory 具有独立的创建、查询和状态管理需求，因此建立 `IFactoryRepository` 与 `FactoryRepository` 是合理的。

### 14.2 什么时候使用 `async/await`

异步方法名带 `Async`，表示它返回 `Task` 并执行异步操作，不代表方法内部必须同时写出 `async` 和 `await`。

如果方法只是把底层异步操作返回的 `Task` 原样交给调用方，可以直接返回：

```csharp
public Task<User?> GetByIdAsync(
    Guid userId,
    CancellationToken cancellationToken)
{
    return _dbContext.Users.FirstOrDefaultAsync(
        user => user.Id == userId,
        cancellationToken);
}
```

如果方法需要等待结果后继续处理、依次执行多个异步步骤或组合返回值，则使用 `async/await`。例如分页查询需要取得列表和总数后再组成返回值：

```csharp
var list = await users.ToListAsync(cancellationToken);
var count = await users.CountAsync(cancellationToken);
return (list, count);
```

如果需要在当前方法中捕获异步操作产生的异常，也必须 `await` 该操作。直接返回 `Task` 时，当前方法的 `try/catch` 无法捕获任务以后产生的异常：

```csharp
try
{
    return await base.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException exception)
{
    // 在这里识别并转换数据库异常。
}
```

可以记成：

- 只转交一个 `Task`：直接 `return`；
- 要处理异步结果、执行多个异步步骤或捕获异步异常：使用 `async/await`；
- 没有异步 I/O：不要为了写法统一强行使用 `async`。

`IUnitOfWork` 用来把一个业务用例中的多个新增、修改和删除统一提交：

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken);
}
```

不机械地为每张表创建 `IRepository/Repository/IService/Service`。只有存在真实业务能力和数据访问需求时才增加接口和实现。

## 15. CancellationToken

当前约定：异步数据库和外部 I/O 方法显式接收 `CancellationToken`，不提供 `= default`，从而让调用方在编译期必须传递。

```csharp
Task<User?> GetByIdAsync(
    Guid userId,
    CancellationToken cancellationToken);
```

它本身只是取消信号，不会主动取消。HTTP 请求断开、客户端取消、Gateway 超时或服务端主动执行 `CancellationTokenSource.Cancel()` 时，令牌会被标记为已取消。

Controller 接收 ASP.NET Core 提供的请求令牌，并依次传递：

```text
Controller
  ↓
Application Service
  ↓
Repository
  ↓
EF Core
```

只在方法参数上写令牌还不够，必须继续传给 `ToListAsync`、`SingleOrDefaultAsync`、`SaveChangesAsync` 等真正执行异步 I/O 的方法。

## 16. 密码处理

不使用 Microsoft Identity 的实体和默认表结构，使用自定义 `User` 模型。

但密码算法不自行发明，当前使用 BCrypt。

依赖方向：

```text
Application
└── IPasswordHasher

Infrastructure
└── BCryptPasswordHasher : IPasswordHasher
```

创建用户流程：

```text
前端提交明文密码
    ↓
UserService 调用 IPasswordHasher
    ↓
BCryptPasswordHasher 生成哈希
    ↓
User.Create 接收哈希
    ↓
数据库只保存 PasswordHash
```

数据库中的值类似：

```text
$2a$12$...
```

禁止保存明文密码。

## 17. 已跑通的创建用户链路

当前已经成功插入过用户，完整链路是：

```text
Swagger
  ↓
UsersController
  ↓
UserService
  ↓
IPasswordHasher
  ↓
BCryptPasswordHasher
  ↓
User.Create
  ↓
IUserRepository.Add
  ↓
IUnitOfWork.SaveChangesAsync
  ↓
SQL Server
```

已确认：

- BCrypt 哈希成功写入数据库；
- `CreatedAtUtc` 能正确写入；
- Migration 已建立 User、Factory、UserFactory 相关表；
- 没有生成物理外键；
- Swagger 创建用户接口已经测试成功；
- YARP 转发已经配置并跑通。

## 18. 统一响应设计

普通业务 JSON 接口统一使用：

```json
{
  "code": 0,
  "message": "操作成功。",
  "data": {}
}
```

业务异常：

```json
{
  "code": 600003,
  "message": "该账号已经存在。",
  "data": {},
  "traceId": "00-..."
}
```

系统异常：

```json
{
  "code": 600500,
  "message": "系统发生异常，请稍后重试。",
  "traceId": "00-..."
}
```

字段含义：

```text
code     程序判断的大类业务码；0 表示成功
message  给人看的具体提示
data     业务数据或供前端判断的结构化附加信息
traceId  日志追踪编号，主要用于失败排查
```

HTTP 状态码仍然保持标准：

| 场景           | HTTP 状态码 | 业务 code |
| -------------- | ----------: | --------: |
| 查询或修改成功 |         200 |         0 |
| 创建成功       |         201 |         0 |
| 参数错误       |         400 |    600001 |
| 未认证         |         401 |  后续补充 |
| 无权限         |         403 |    600005 |
| 数据不存在     |         404 |    600002 |
| 数据冲突       |         409 |    600003 |
| 状态不允许     |         409 |    600004 |
| 请求频率超限   |         429 |    600429 |
| 系统异常       |         500 |    600500 |

不能使用非标准 HTTP 600。`600xxx` 只存在于 JSON 响应体的 `code` 中。

`ApiResponse<T>` 当前采用容易理解的普通 class：

```csharp
using System.Text.Json.Serialization;

namespace ZX.Platform.Identity.Api.Contracts;

/// <summary>
/// API 统一响应结构。
/// </summary>
/// <typeparam name="T">响应数据类型。</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// 业务状态码，0 表示成功，600xxx 表示失败。
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 返回给调用方的提示信息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 接口返回的数据。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    /// <summary>
    /// 请求追踪编号，主要用于异常排查。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }
}
```

当前不使用 `record`，目的是保持写法与普通 DTO 一致，便于学习。

## 19. 错误码策略

不为每一条中文提示都建立错误码，先按错误类型归类：

```csharp
namespace ZX.Platform.Identity.Application.Errors;

public static class ErrorCodes
{
    public const int ValidationFailed = 600001;
    public const int NotFound = 600002;
    public const int Conflict = 600003;
    public const int InvalidState = 600004;
    public const int Forbidden = 600005;
    public const int TooManyRequests = 600429;
    public const int SystemError = 600500;
}
```

原则：

- 前端只需要显示提示：共用大类 code，具体内容放 `message`；
- 前端需要执行不同动作：返回稳定的结构化 `data`；
- 前端不能通过匹配中文 `message` 决定业务行为；
- 将来确实需要跨系统约定时，再增加更细的错误码。

例如账号冲突都可以使用 `600003`，通过 `data.reason` 区分：

```json
{
  "code": 600003,
  "message": "该账号已被禁用。",
  "data": {
    "reason": "AccountDisabled",
    "allowedAction": "Enable",
    "userId": "..."
  },
  "traceId": "..."
}
```

建议区分动作语义：

- 已删除账号：`allowedAction = "Restore"`；
- 已禁用账号：`allowedAction = "Enable"`；
- 正常存在账号：`allowedAction = null`。

早期讨论使用过 `CanRestore`；它无法准确表达“禁用后重新启用”，后续检查实际代码时应决定是否改为 `AllowedAction` 或分别使用 `CanRestore`、`CanEnable`。

## 20. DomainException 与 BusinessException

### 20.1 DomainException

`Domain` 不能引用 `Application`，否则会形成：

```text
Application → Domain
Domain      → Application
```

这会造成循环引用。

因此实体内部规则使用定义在 Domain 中的异常：

```csharp
namespace ZX.Platform.Identity.Domain.Exceptions;

public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
```

示例：

```csharp
public void Enable()
{
    if (IsDeleted)
    {
        throw new DomainException(
            "已删除的用户必须先恢复，不能直接启用。");
    }

    IsEnabled = true;
}
```

### 20.2 BusinessException

Application Service 中需要查询或编排后才能发现的预期业务问题，使用 `BusinessException`：

- 账号重复；
- 用户不存在；
- 用户无厂区权限；
- 当前业务状态不允许操作。

业务异常建议包含：

```text
Code
ErrorType
Message（继承自 Exception）
ResponseData
```

附加属性不要命名为 `Data`，因为 `System.Exception` 已经有一个 `Data` 属性，使用 `ResponseData` 更清楚。

```csharp
public enum BusinessErrorType
{
    Validation,
    NotFound,
    Conflict,
    InvalidState,
    Forbidden
}

public sealed class BusinessException : Exception
{
    private BusinessException(
        int code,
        string message,
        BusinessErrorType errorType,
        object? responseData)
        : base(message)
    {
        Code = code;
        ErrorType = errorType;
        ResponseData = responseData;
    }

    public int Code { get; }

    public BusinessErrorType ErrorType { get; }

    public object? ResponseData { get; }

    public static BusinessException Validation(
        string message,
        object? responseData = null)
    {
        return new BusinessException(
            ErrorCodes.ValidationFailed,
            message,
            BusinessErrorType.Validation,
            responseData);
    }

    public static BusinessException NotFound(
        string message,
        object? responseData = null)
    {
        return new BusinessException(
            ErrorCodes.NotFound,
            message,
            BusinessErrorType.NotFound,
            responseData);
    }

    public static BusinessException Conflict(
        string message,
        object? responseData = null)
    {
        return new BusinessException(
            ErrorCodes.Conflict,
            message,
            BusinessErrorType.Conflict,
            responseData);
    }

    public static BusinessException InvalidState(
        string message,
        object? responseData = null)
    {
        return new BusinessException(
            ErrorCodes.InvalidState,
            message,
            BusinessErrorType.InvalidState,
            responseData);
    }

    public static BusinessException Forbidden(
        string message,
        object? responseData = null)
    {
        return new BusinessException(
            ErrorCodes.Forbidden,
            message,
            BusinessErrorType.Forbidden,
            responseData);
    }
}
```

## 21. 全局异常处理

API 使用 `IExceptionHandler` 统一处理：

- `BusinessException`；
- `DomainException`；
- 未知系统异常。

映射规则：

```text
Validation   → HTTP 400
NotFound    → HTTP 404
Conflict    → HTTP 409
InvalidState→ HTTP 409
Forbidden   → HTTP 403
DomainException → HTTP 409 + 600004
未知异常    → HTTP 500 + 600500
```

未知系统异常不能把 `exception.Message` 直接返回给前端，因为可能泄漏 SQL、表名、连接信息、内部路径或堆栈细节。详细异常写日志，对外只返回：

```json
{
  "code": 600500,
  "message": "系统发生异常，请稍后重试。",
  "traceId": "..."
}
```

注册：

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
```

中间件：

```csharp
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
```

开发调试时 Visual Studio 可能先在 `throw` 处暂停。按 F5 后异常才继续向上传递给 `GlobalExceptionHandler`。这不表示异常处理器失效；生产环境没有 Visual Studio 调试器，会直接进入异常处理中间件。

## 22. DTO 参数验证与统一格式

`[ApiController]` 会在 Controller 执行前自动处理无效的 `ModelState`。

默认参数错误响应不是当前统一格式，因此配置：

```csharp
options.InvalidModelStateResponseFactory = context => ...
```

它处理：

- `[Required]` 失败；
- 字符串长度或范围校验失败；
- JSON 类型转换失败；
- DTO 参数绑定失败。

它不处理：

- 数据库中账号已存在；
- 用户已删除或已禁用；
- 用户是否有厂区权限；
- Domain 规则异常；
- 未知系统异常。

职责划分：

```text
DTO / ModelState
    是否为空、长度、格式、范围和类型

Application Service
    查询数据库后才能判断的业务规则

Domain
    实体自身状态就能判断的规则
```

为了避免 `Program.cs` 过长，已经决定把该配置提取到：

```text
ZX.Platform.Identity.Api/
└── Extensions/
    └── ApiBehaviorExtensions.cs
```

Program.cs 最终只保留：

```csharp
builder.Services
    .AddControllers()
    .AddUnifiedApiBehavior();
```

扩展方法返回 `IMvcBuilder`，内部调用 `ConfigureApiBehaviorOptions`。这个扩展只是在集中配置 MVC 行为，不是新建一个业务服务。

> 最新状态：用户已对这部分代码做过修改，但当前会话没有读取 `E:\myproject\ZX.Platform` 的实际文件。新会话应优先检查 `ApiBehaviorExtensions.cs`、`Program.cs` 和命名空间，而不是假设它们已经完全正确。

## 23. 成功响应约定

创建成功：

```http
HTTP 201 Created
```

```json
{
  "code": 0,
  "message": "用户创建成功。",
  "data": "用户ID"
}
```

查询或修改成功：

```http
HTTP 200 OK
```

没有返回数据的删除/操作成功接口，如果仍要返回统一 JSON，就使用 HTTP 200，不使用 HTTP 204，因为 204 按规范不能带响应体。

当前不做“自动包装所有响应”的全局过滤器。Controller 显式返回 `ApiResponse<T>` 更容易理解，也不会误伤：

- 文件下载；
- 图片和流；
- 健康检查；
- 第三方回调；
- 特殊协议响应。

## 24. 创建用户时的业务冲突

创建用户前通过 `NormalizedAccount` 查询，包括软删除的数据。

三种冲突：

```text
正常存在   → AccountExists
已经删除   → AccountDeleted，可 Restore
已经禁用   → AccountDisabled，可 Enable
```

三种情况可以共用 `600003`，具体原因放在 `ResponseData`。

数据库中的唯一索引仍然必须保留。Service 先查询只能提供友好提示，不能完全防止两个并发请求同时通过检查。后续要捕获数据库唯一约束产生的 `DbUpdateException`，并转换为同一个友好的业务冲突响应。

## 25. 当前已完成内容

以下内容已在对话中确认完成：

- 创建 `ZX.Platform` 总解决方案；
- 采用 .NET 10；
- 创建 Gateway；
- 创建 Identity.Api、Application、Domain、Infrastructure；
- 配置项目引用；
- 安装并配置 YARP；
- Gateway 成功转发 Identity.Api；
- Identity.Api 可以启动；
- Swagger/OpenAPI 已接入；
- Visual Studio 可配置多个启动项目；
- WSL Docker 可用；
- 使用 SQL Server 2022 容器；
- 创建 `ZX_PLATFORM_IDENTITY` 数据库；
- 安装 EF Core 相关包；
- 创建 DbContext 和 Entity Configuration；
- Migration 成功执行；
- 创建 User、Factory、UserFactory 表；
- Migration 未生成物理外键；
- 创建 User、Factory、UserFactory 实体；
- 使用充血模型；
- User、Factory 使用软删除；
- UserFactory 使用 `IsEnabled` 表示授权状态；
- 建立审计字段；
- `CreatedAtUtc` 能自动写入；
- 建立 `IUserRepository`；
- 建立 `IUnitOfWork`；
- 建立 `IPasswordHasher`；
- 接入 BCrypt；
- 创建用户接口已成功插入数据库；
- 已开始实现错误码、业务异常、领域异常和统一响应。

## 26. 当前正在进行

当前开发位置是“统一 API 响应和异常处理”，包含：

- `ApiResponse<T>`；
- `ErrorCodes`；
- `DomainException`；
- `BusinessException`；
- `GlobalExceptionHandler`；
- `ApiBehaviorExtensions`；
- Controller 成功响应包装；
- 账号重复、删除、禁用时的结构化 `ResponseData`。

这些代码用户已经开始修改，但新会话必须读取实际项目确认完成度。

## 27. 新会话的第一步

新窗口的项目目录应设置为：

```text
E:\myproject\ZX.Platform
```

先只检查，不急着修改。检查顺序：

1. 读取本文；
2. 查看解决方案和真实目录结构；
3. 检查所有 `.csproj` 的 `ProjectReference`；
4. 检查 `Program.cs`；
5. 检查 `ApiResponse<T>`；
6. 检查 `ErrorCodes`、`BusinessException`、`DomainException`；
7. 检查 `GlobalExceptionHandler`；
8. 检查 `ApiBehaviorExtensions`；
9. 检查 `UserService.CreateAsync`；
10. 检查 Controller 的成功响应；
11. 构建整个解决方案；
12. 使用 Swagger 验证成功、参数错误、账号重复和系统异常。

必须重点验证：

```text
Domain.csproj 没有引用 Application
Application → Domain
Infrastructure → Application + Domain
Api → Application + Infrastructure（必要时 + Domain）
```

## 28. 下一步验证用例

### 用例一：创建成功

期望：

```text
HTTP 201
code = 0
data = 新用户 Guid
```

### 用例二：DTO 参数错误

例如密码少于 8 位。

期望：

```text
HTTP 400
code = 600001
进入 InvalidModelStateResponseFactory
不进入 Controller 业务方法
```

### 用例三：账号已经存在

期望：

```text
HTTP 409
code = 600003
data.reason = AccountExists
```

### 用例四：账号已删除

期望：

```text
HTTP 409
code = 600003
data.reason = AccountDeleted
data.allowedAction = Restore
```

### 用例五：账号已禁用

期望：

```text
HTTP 409
code = 600003
data.reason = AccountDisabled
data.allowedAction = Enable
```

### 用例六：DomainException

例如直接启用已经删除的用户。

期望：

```text
HTTP 409
code = 600004
```

### 用例七：未知异常

期望：

```text
HTTP 500
code = 600500
响应不暴露内部异常
日志包含完整异常和 traceId
```

## 29. 统一响应完成后的开发计划

建议按以下顺序继续：

1. 完成并验证统一响应和异常处理；
2. 处理并发创建时的数据库唯一索引异常；
3. 完成用户查询；
4. 完成用户启用、禁用、软删除、恢复；
5. 完成 Factory 的新增、修改、查询、停用；
6. 完成用户厂区授权和撤销；
7. 在 MES 报工等累加型写操作中实现业务幂等性；
8. 引入当前登录用户上下文，自动填写 `CreatedByUserId`、`UpdatedByUserId`；
9. 设计登录和 Token，不使用 Microsoft Identity 表模型也可以使用 JWT；
10. 实现认证和授权；
11. 增加操作日志和审计日志；
12. 补充集成测试；
13. 再整理依赖注入扩展方法；
14. 再接入 Git 托管和 CI/CD；
15. 再做 Docker Compose；
16. Identity 稳定后再创建 MES 或其他微服务。

### 29.1 幂等、限流与 Redis 的取舍

本地测试曾出现过以下情况：调用方最终收到 `500/600500`，但用户数据实际上已经写入；调用方再次提交后，由账号唯一约束提示数据已存在。这说明“客户端收到失败”不一定代表数据库没有提交。

需要区分以下概念：

```text
事务       保证同一次保存中的多条数据库变更一起成功或一起失败
幂等       保证同一个业务操作重复请求时只产生一次业务效果
限流       限制单位时间内或同时执行的请求数量，保护服务
Redis 抢占 减少相同请求在多个实例中同时执行，不是最终一致性保证
```

#### Identity 创建用户

创建用户已经具有天然业务唯一键 `NormalizedAccount`，并由数据库唯一索引保证不会创建两个相同账号。当前属于低频后台管理操作，可以接受重复提交时返回 `409`，因此暂不为创建用户建立通用幂等表，也不为此引入 Redis。

当前措施：

- 前端请求期间通过统一全屏 loading 减少重复点击，表单保留提交状态；
- `POST` 请求不进行无条件自动重试；
- Service 预查询用于友好提示；
- 数据库唯一索引用于并发兜底；
- 网络中断或结果不确定时，调用方重新查询用户列表确认结果；
- 继续保留 `TraceId` 和服务端异常日志用于排查。

如果将来明确要求重复请求必须返回第一次创建的 `UserId`，再为该接口加入正式幂等结果存储。

#### MES 报工

报工属于累加型业务操作。例如同一次“报工 2 小时”被执行两次，会错误地累计为 4 小时。报工通常没有天然唯一业务键，因此必须设计幂等性。

一次报工填写过程生成一个稳定的 `RequestId`：

```text
打开或开始填写报工
→ 前端生成 RequestId
→ 双击、超时重试和自动重试继续使用同一个 RequestId
→ 明确成功或开始下一次独立报工后才生成新的 RequestId
```

不能在每次 HTTP 重试时重新生成 `RequestId`，否则服务端会把它们视为不同的报工。后端预先签发请求编号并不是必需的；一般由前端生成 Guid 即可。`RequestId` 不是权限凭证，服务端仍须校验用户、工单、厂区和权限。

报工表或具有业务含义的报工提交记录保存 `RequestId`，并建立唯一索引：

```text
收到 RequestId
→ 查询是否已有成功报工
→ 已存在：返回第一次的 ReportId
→ 不存在：执行业务并在同一事务中保存报工数据和 RequestId
→ 并发请求发生唯一约束冲突：按 RequestId 查询原记录并返回原 ReportId
```

相同 `RequestId` 如果携带不同的业务内容，应返回 `409`，不能覆盖第一次报工。实现时可以比较关键字段，或保存请求摘要。

如果一次报工只创建一个主记录，可以直接把 `RequestId` 放在报工表中，不必建立全平台通用的幂等表。如果一次提交同时产生多类数据或需要处理状态，可以建立具有业务含义的“报工提交记录”，并与业务数据在同一数据库事务中保存。

#### 为什么不沿用 Redis 一次性 Token

原单体方案是在打开页面时由服务端生成 ID 并写入 Redis，提交时原子消费并删除该 ID。它可以防止重复点击，但不是完整幂等：

- Redis Key 删除后业务保存失败，合法重试也会被拒绝；
- 数据库提交成功但 HTTP 响应丢失时，重试只能知道 Key 不存在，无法返回第一次结果；
- Redis 与 SQL Server 不在同一事务中，无法保证两边一起成功或回滚；
- 即便使用 `GETDEL`、`SET NX` 或 Lua 保证 Redis 内部原子性，也不能解决 Redis 与业务数据库之间的一致性。

因此数据库中的 `RequestId` 唯一索引是幂等正确性的最终保证，Redis 最多是降低并发重复执行成本的优化层。

#### 多实例与限流

生产环境按多实例部署考虑。多个 MES 实例共享 SQL Server 时，相同 `RequestId` 即使落到不同实例，数据库唯一索引仍能保证最终只保存一次，因此多实例并不要求报工幂等必须使用 Redis。

ASP.NET Core `RateLimiter` 用于防刷和保护资源，但不能识别两个请求是否属于同一个业务操作。其默认计数保存在单个进程中，多实例部署后各实例分别计数。如果每个实例独立限流能够满足资源保护目标，可以继续使用进程内限流；如果要求所有实例合计执行严格的全局额度，则在 Gateway 或分布式限流层引入共享状态，Redis 是候选实现之一。

当前结论：

```text
Identity 创建用户   暂不增加专用幂等机制，依靠账号唯一索引
MES 报工            RequestId + 数据库唯一索引 + 同一事务
Identity 接口防刷    已接入 RateLimiter：按 IP + 方法 + 接口滑动限频
严格的多实例全局限流 根据实际指标在 Gateway/分布式限流层实现
Redis               当前不因报工幂等而引入；出现全局限流、缓存等明确需求时再引入
```

如果报工事务之外还会调用外部系统、操作设备或发送不可撤回的消息，仅靠数据库唯一索引不够；届时需要进一步设计 Outbox、消息消费幂等和补偿机制。

不要在当前阶段提前加入：

- Kubernetes；
- 服务网格；
- 分布式事务框架；
- 大量消息队列；
- 复杂 CQRS/Mediator 管道；
- 每张表一套机械式接口；
- 为“以后可能需要”创建大量抽象。

## 30. 日志与可观测性计划

后续日志至少分为：

```text
应用日志   程序运行、异常、性能
操作日志   谁执行了什么操作
审计日志   业务数据前后变化
访问日志   HTTP 请求、状态码、耗时
```

日志会有性能成本，因此不能无差别记录所有大对象和敏感字段。

原则：

- 密码、Token、连接字符串不写日志；
- 未知异常记录完整堆栈；
- 业务异常通常记录 Warning，不需要重复记录完整堆栈；
- 使用 `traceId` 关联请求和日志；
- 大量日志异步输出或交给专业日志系统；
- MES 的关键业务追溯不能只依赖普通文本日志。

## 31. 代码风格和协作约定

- 用户希望逐句理解代码，新增复杂写法前先解释原因；
- 优先普通 class 和直观代码，暂时不为简洁强行使用 `record`；
- 不过早抽象；
- Program.cs 注册项明显增多后，再提取 DependencyInjection 扩展；
- DTO 使用 `Dto` 命名，当前不使用 `Command` 命名；
- 目录保持扁平、按职责清楚，不机械地创建多层目录；
- 实体使用中文 XML 注释；
- 异步 I/O 传递 `CancellationToken`；
- `Add(entity)` 保持同步；
- 数据库唯一约束不能只靠 Service 查询替代；
- 不使用数据库物理外键，但必须由业务代码和测试维护关系一致性；
- 不使用导航属性，查询使用显式 join；
- 不使用 Microsoft Identity 的默认用户模型；
- 密码算法使用成熟库 BCrypt；
- 普通业务 JSON 接口统一响应，文件流等特殊接口不强制包装。

### 31.1 C# 命名与格式约定

- 类、接口、公开方法和公开属性使用 `PascalCase`；
- 方法参数和局部变量使用 `camelCase`；
- 私有字段使用 `_camelCase`；
- 执行异步操作并返回 `Task` 的方法以 `Async` 结尾；
- DTO 的 C# 属性仍使用 `PascalCase`，ASP.NET Core 序列化后的 JSON 字段保持 `camelCase`；
- 英文名称应准确表达实际行为和返回值，例如返回 `List<UserFactory>` 的方法不命名为 `GetFactoryIds...`；
- 中文业务术语统一使用“厂区”，不在“厂区”和“工厂”之间混用；
- `if`、`foreach` 等关键字与左括号之间保留一个空格，逗号和运算符两侧按 C# 默认格式留空格；
- `if` 只有一条简单语句时允许省略大括号；包含多条语句、嵌套判断、`else` 或较长代码时使用大括号；
- 使用 Visual Studio 的格式化文档和删除、排序 `using` 功能处理缩进、空格及无用引用；
- 当前代码整理先统一命名与格式，注释暂不批量调整，避免把风格整理和业务改动混在一起。

### 31.2 自动格式检查方向

代码格式可以在 Git Hook 中检查，但格式规则应先通过仓库根目录的 `.editorconfig` 固化，避免不同开发环境产生不同结果。

推荐后续采用以下分工：

- Visual Studio 和开发者本地负责主动格式化；
- `pre-commit` Hook 运行 `dotnet format --verify-no-changes`，只检查并阻止不符合规范的提交，不静默修改和重新暂存文件；
- CI 再执行相同检查，防止有人未安装或绕过本地 Hook；
- Git 默认不会提交 `.git/hooks` 中的文件。如需团队共享，应把 Hook 脚本放入仓库中的 `.githooks` 等目录，再提供统一的安装或 `core.hooksPath` 配置方式。

当前仓库尚未创建 `.editorconfig` 和 Git Hook。先完成现有 Identity 代码的人工整理并确认规则，再引入自动检查，避免首次执行时产生大范围无关格式变更。

## 32. 参考方向

当前设计思想主要参照以下通用实践，而不是照搬某一个项目：

- Microsoft ASP.NET Core 分层和依赖注入；
- Microsoft EF Core Fluent API、Migration 和 DbContext 生命周期；
- Microsoft YARP；
- .NET `IExceptionHandler`；
- DDD 中的实体行为和领域异常；
- Clean Architecture 的依赖方向；
- Repository 和 Unit of Work 的按需使用；
- eShop 等微软示例中按业务边界组织服务的思路。

后续查看 GitHub 示例时，应重点比较：

- 项目引用方向；
- 微服务物理目录；
- 每个服务是否拥有独立数据；
- 业务异常如何映射 HTTP；
- 认证和授权放在哪一层；
- 日志、追踪和集成事件如何处理；
- 是否为了模板完整而引入了当前项目不需要的复杂度。

不要仅因为大型开源项目使用某种模式，就直接复制进当前项目。要先回答它解决了什么实际问题。

## 33. 尚未最终决定的问题

以下内容尚未最终落地或需要以后重新确认：

- 厂区与租户是否完全等价；
- 一个厂区一个库时的动态连接管理；
- 登录、JWT、刷新 Token 的具体方案；
- 角色、权限、菜单的数据模型；
- 跨服务权限获取、缓存失效和服务间认证的具体方案；
- 账号已删除和已禁用时前端动作字段的最终命名；
- 审计日志的存储方式；
- MES 的版本和追溯模型；
- 服务间通信采用 HTTP、gRPC 还是消息；
- GitHub Actions 或 GitLab Runner；
- Docker Compose 和生产部署方式；
- 是否以及何时引入 Redis；
- 是否以及何时引入消息队列；
- 是否以及何时使用 Kubernetes。

这些问题不阻塞当前 Identity 的基础 CRUD、状态规则和统一响应。

## 34. 重要提醒

1. 本文是架构和讨论记录，不替代实际代码。
2. 新会话必须先构建并检查代码，不能只根据本文假设项目可运行。
3. 用户当前希望自己动手写代码，助手以教学、解释、代码示例和检查为主。
4. 未经用户明确要求，不要直接大规模重构项目。
5. 当前优先完成一条端到端可验证链路，再扩展下一个能力。

## 35. API 提示多语言（2026-10-10）

Identity 已加入中文和英文的 API 提示资源，前端统一请求层按当前界面语言发送 `Accept-Language: zh-CN` 或 `en-US`；调用方显式提供同名请求头时优先。

- 只通过 `Accept-Language` 协商语言，缺失或不支持的语言回退到中文；响应的 `Content-Language` 表示实际选择的语言。
- `Program.cs` 注册 `AddApiLocalization` 和 `AddDataAnnotationsLocalization`，在异常处理和 MVC 之前调用 `UseRequestLocalization`。
- API 按职责使用 `Localization/CommonMessages.cs`、`ValidationMessages.cs`、`UserMessages.cs` 和 `FactoryMessages.cs` 作为资源标记；`Resources/Localization/` 下每个标记各有默认中文和 `en-US` 资源文件。
- Controller 和异常处理器使用 `IApiMessageLocalizer` 按 `Common.*`、`Validation.*`、`User.*`、`Factory.*` 前缀查找对应资源；DTO 的 `ErrorMessage` 使用校验资源键，由 MVC 翻译并按原有校验规则填充长度、范围等占位参数。
- Application 的 `BusinessException` 和 Domain 的 `DomainException` 携带稳定的 `MessageKey`，由 API 的 `GlobalExceptionHandler` 翻译。内层不引用 API 或本地化服务。
- 参数绑定失败、请求体错误、分页溢出、数据库唯一约束和系统异常也使用对应语言的统一提示。JSON 解析异常详情不直接返回给调用方。
- HTTP 状态、业务 `code`、`data.reason`、业务数据和 `traceId` 的含义不随语言改变；不通过匹配提示文字判断业务行为。

新增消息时，同一模块的中英文资源必须使用相同的键和格式占位参数；新增模块时增加对应的资源标记类和中英文资源对。业务或领域异常引用的键缺失时，记录警告并返回通用本地化提示，避免把资源键直接显示给用户。前文第 20 节中的异常示例属于历史设计，当前异常的字符串参数表示资源键，以实际代码为准。

联调时可在 Swagger 或接口工具中分别传入 `Accept-Language: zh-CN` 和 `en-US`，检查查询成功、DTO 校验、用户不存在、重复账号、分页溢出及系统错误提示；同时确认原有状态码和结构化字段一致。无请求头或不支持的语言应返回中文。

本轮仅完成静态代码与资源核对，尚未运行构建、测试或真实请求验收。用户管理页面仍使用模拟数据，接入真实接口另行进行。

---

## 36. 接口限流（2026-10-12）

Identity API 已接入内置 RateLimiter。默认同一 IP 调用同一 HTTP 方法和接口路由，在 3 秒分段滑动窗口内最多 5 次，超限直接返回 HTTP 429 / 600429，不排队。参数位于 Identity.Api/appsettings.json，启动时校验正整数，修改后重启。实例总并发上限待实际压测后再决定。

Gateway 和 Identity 已配置可信代理头处理，默认仅信任本地回环地址；Gateway 重写转发头，Identity 使用还原后的客户端 IP 分组。部署到其他机器或容器时须配置每层直接上游的实际代理地址。限流额度按实例保存，多人共用出口 IP 时共享接口额度。

本次没有增加统一后端请求超时或业务幂等机制。现有取消信号继续传到数据库；长报表和下载不因限流被强制截断。429 沿用统一响应、中英文提示和 traceId，前端同步错误码且不自动重试。配置、实现位置、代理部署、适用边界与人工验收见 [Identity 接口限流](RATE-LIMITING.md)。

本轮 Identity 与 Gateway 编译通过（0 警告、0 错误）。临时独立进程通过 HTTP 验证了第 6 次拒绝、窗口恢复、IP / 接口分别计数、查询参数共用额度、IPv4 映射归一、中英文 429、非可信代理头不能绕过限流。验证使用无效参数，不访问数据库，不修改现有运行实例；前端未运行构建或浏览器验收。真实负载与数据库处理尚未压测，频率额度须按实际使用情况调整。

最后更新：2026-10-12
