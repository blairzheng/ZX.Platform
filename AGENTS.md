# 后端业务开发约定

本文件约束本仓库中的业务开发及 AI 辅助修改。开始任务前阅读 [配置与启动](docs/01-start.md) 和 [业务开发步骤](docs/02-business.md)，再查看最接近当前需求的实际代码。文档中的设备点检是开发示例，不能当成已实现功能。

## 修改范围

业务模块在现有 Identity 服务内按四层结构开发。围绕当前任务修改，保留成员已有改动，只格式化本次涉及的文件，不顺带重构其他模块。

新增业务所需的实体、DTO、Service、Repository、Controller、映射、迁移、权限常量、词条和依赖注册，按业务需求正常补齐。涉及改变登录、会话、LDAP、权限判定、SBU 校验、迁移基础设施、Gateway 或发布流程时，先说明原因和影响，确认任务授权后实施；不因写文档、加一个业务接口就顺手改造这些机制。明确授权的工作继续完成，不反复请求同一项确认。

不自行拆服务、引入低代码平台、增加通用框架或升级依赖。普通业务复用项目已声明的 NuGet 包；确有新增依赖需求时先说明用途。

## 业务文档维护

具体业务说明放在 [业务文档目录](docs/business/README.md) 下，编写和更新时机按 [成员业务文档编写规范](docs/03-documentation.md) 执行。新增模块补文档和索引；业务规则、接口、权限或数据范围改变时同步对应段落。后端维护接口契约与业务口径，前端维护页面使用说明。开发规范、配置与启动、发布说明等系统级文档保持原位置，不列为普通业务每次必改项。

## 分层与代码写法

以下项目均位于 `Services/Identity/`：

| 项目 | 职责 |
| --- | --- |
| `ZX.Platform.Identity.Domain` | 实体、状态、实体自身可判断的领域规则 |
| `ZX.Platform.Identity.Application` | DTO、用例流程、业务服务、仓储及外部能力接口 |
| `ZX.Platform.Identity.Infrastructure` | EF Core、Oracle 映射、仓储实现、迁移及外部系统适配 |
| `ZX.Platform.Identity.Api` | Controller、绑定、响应、本地化及依赖注入 |

Domain 不引用其他业务层；Application 依赖 Domain，不引用 EF Core、Oracle 驱动或 API。Infrastructure 实现 Application 的接口，API 负责注册实现。Controller 不直接使用 DbContext，不放状态流转和跨模块业务规则。

优先普通 class 和直接可读的代码。类型和公开成员用 PascalCase，参数和局部变量用 camelCase，私有字段用 `_camelCase`；DTO 使用 `Dto` 后缀。跟随所修改文件的命名空间、缩进和括号风格，不整仓调整格式。

异步 I/O 方法以 `Async` 结尾，接收并向下传递 `CancellationToken`；框架 override 遵循框架签名。只转交 Task 可以直接返回，需要顺序处理、结果加工或捕获异步异常时使用 async/await。不通过 `.Result`、`.Wait()` 阻塞异步调用。

不机械地为每张表增加一套空壳 Service / Repository，不为普通用例引入 CQRS、Mediator 或新的通用仓储基类。

## 实体与持久化

输入的必填、长度和格式由 DTO 校验；实体自身的状态规则放 Domain 行为方法；需要查询其他对象才能判断的规则放 Application Service。实体沿用私有构造、`Create` 工厂、private setter 和行为方法，不把 EF 实体直接作为请求 DTO。

按实际需要复用 `AuditableEntity` / `SoftDeletableEntity` 及对应映射扩展，不重复声明基类字段。当前审计拦截器只维护时间，不能声称已自动记录操作者；业务需要创建人、修改人或审批轨迹时，在用例中明确接入。软删除和状态变更通过实体行为完成，不绕过已有查询过滤器。

读取列表使用 `AsNoTracking()`，写入时取得跟踪实体并调用行为方法，再由 Service 通过 `IUnitOfWork.SaveChangesAsync` 提交。Repository 的 Add / Remove 只登记变更，不分散保存。DbContext 与 UnitOfWork 共用 scoped 实例，不在同一个 DbContext 上并行查询。

分页复用 `PageQueryDto`，返回 `{ list, total }`；列表和总数使用相同筛选条件，排序包含稳定的次级键。唯一性由数据库唯一约束兜底，不能只做查询后插入。当前模型不使用导航属性和物理外键，关系按现有显式查询和用例规则维护。

同一数据库内的跨模块变更由明确的用例统筹事务，不在各模块内部各自提交半段业务。状态流转设计允许条件、动作权限、并发冲突及审计，不通过任意修改状态字段代替业务操作。

## 接口、异常与消息

普通业务接口沿用 `ApiResponse<T>`：HTTP 使用标准状态码，成功业务码为 0，返回正确的 message 和 data；分页数据放 data 中。业务错误码复用 `Application/Errors/ErrorCodes.cs`，不随意新增或硬编码另一套码。

Controller 绑定输入、调用 Service、返回响应；失败交给现有全局异常处理器。业务失败使用现有 `BusinessException` / `DomainException` 和消息键，不把预期业务失败写成普通系统异常，也不 catch 全部异常后返回 HTTP 200。日志保留必要诊断，不向客户端返回堆栈、SQL 或连接信息。

新增成功和业务失败提示同步添加中英文资源。`Inspection.*` 等未单独分组的业务键目前放在 API 的 `Resources/Localization/CommonMessages.resx` 与 `CommonMessages.en-US.resx`；DTO 的 `Validation.*` 键放在对应 `ValidationMessages` 两份资源中。沿用 `ApiMessageLocalizer` 现有前缀分发，不只新建资源文件就认为能自动使用。

Controller 使用 `messages["业务消息键"].Value`，Domain / Application 只传键，由现有处理器翻译，不在业务层注入 Localizer。资源参数使用一致的 `{0}`、`{1}` 约定，不拼接翻译后的半句话。语言沿用 `Accept-Language` 的 zh-CN / en-US 协商，不另建语言状态或响应翻译机制。完整添加示例见 `02` 第 8 节。

## 功能权限与菜单

业务权限使用编译期常量，在 `Application/Authorization/IdentityPermissions.cs` 的现有类中追加，沿用三段式业务码，例如 `equipment.inspection.create`。`All` 已通过反射收集常量，不维护第二份目录，不直接向权限表写动态码。

受保护接口保留登录校验，各动作声明对应的 `[Authorize(Policy = ...)]`，读取、写入和业务流转按需求区分权限。不能只依赖前端隐藏按钮、菜单可见性或“调用者应该是管理员”。启动初始化器登记权限目录，不自动给普通角色授权；新增权限需要重启并配置测试角色。

保留现有权限同步和清理行为，不为新增模块移除静态目录过滤。不同分支启动会共同修改共享库权限目录，涉及并行权限联调时协调基线；个人 Redis 前缀不能解决这类数据库共享问题。

业务菜单在“菜单管理”配置，不加入 `DefaultMenus`，不通过 Migration 修改菜单、角色或用户授权。新页面按 `02` 的前端接入步骤，在前端 `src/router/menuPages.ts` 统一登记组件标识、`titleKey` 和显式加载函数。后端 `Menus/MenuService.cs` 只校验组件标识格式，不维护页面名称列表：ASCII 字母开头，后续仅字母、数字、下划线，总长 1～100 字符，目录与操作的组件为空。交付时说明菜单结构、路径、组件、权限码及中英文标题；查看权限由后端授权树自动补“查看页面”叶子，无需手工创建同码查看操作。

## SBU 数据范围

先确认当前业务是否按 SBU 隔离。需要隔离的 Controller 标注 `[RequireSbu]`，从已校验的 `CurrentSbuContext.SbuId` 取值传给用例；不把请求体中的任意 SbuId 或原始请求头直接当作授权依据。

仓储必须继续按已校验 SBU 过滤列表、总数、详情、修改、删除、导出及关联子表。按 ID 操作时同时限定 SBU，查不到按不存在处理；批量操作中每个对象同样适用，不能只检查第一条。普通编辑不允许更改记录归属，确有跨 SBU 需求时设计独立授权用例。

不能认为管理员自动绕过 SBU 分配，也不能只验证请求头就查询全部记录。前端对应 API 声明 `sbuScoped: true`，由现有请求层附加 `X-SBU-ID`；后端独立校验并过滤。

## Oracle 映射与 Migration

沿用 `PlatformIdentityDbContext` 和现有 Oracle 19c 映射。列名及约束名称跟随当前规范，统一使用 UTC 时间和现有 GUID 表示；注意 Oracle 空字符串等于 NULL、字段长度及标识符限制。不在业务映射中写死个人测试 schema，不迁移外部 MES 的已有表。

模型变更顺序为：实体 → EF 映射与 DbSet → 生成并检查迁移 → 经授权更新测试库 → 接口联调。生成迁移必须使用完整命令，统一保留输出目录：

```bash
dotnet ef migrations add 本次迁移名称 \
  --context PlatformIdentityDbContext \
  --project Services/Identity/ZX.Platform.Identity.Infrastructure \
  --startup-project Services/Identity/ZX.Platform.Identity.Api \
  --output-dir Persistence/Migrations
```

生成迁移不带 `-- --development`；更新测试库按 `02` 中的 `database update ... -- --development` 完整命令，读取负责人提供的 User Secrets。保留默认离线生成、统一源 schema 与开发库执行入口的区别，不为普通业务变更改造工厂或 SQL 生成器。

迁移 `.cs`、对应 Designer 和 snapshot 一起交付；snapshot 由 EF 生成，冲突时核对模型和迁移链，不简单选一侧覆盖。检查 Up / Down 及数据转换，只包含本任务需要的结构和回填；出现无关表的删除或改动先查原因。已有数据的必填列、默认值和转换需考虑存量，Oracle DDL 可能隐式提交。

不删除、重写已执行的迁移或历史，不自行回退共享库。迁移冲突、测试库版本不一致或执行失败时反馈负责人。新增迁移同步 `Oracle19Checks.cs` 的末位迁移预期，保留原有断言。

AI 不因“开发业务功能”自行连接、迁移或修改共享数据库；仅在用户明确要求相应操作时执行，既有明确授权不重复确认。正式库升级、DBA 脚本和发布由负责人统筹，不加应用启动自动迁移。

## 配置、外部系统与日志

数据库及 Redis 真实配置保存在本机 User Secrets，不写入源码、文档、迁移、日志或提交。应用本机运行，Oracle 使用远程测试库，Redis 复用现有正式实例；开发使用个人 `AuthenticationRedis:KeyPrefix`，不清空实例或操作其他前缀。

个人前缀只隔离 Redis 键，不隔离共享 Oracle 的用户、角色、菜单和业务数据。不擅自创建新环境、启动本地 Oracle / Redis 容器或变更共享账号数据。

外部 MES / LDAP 等能力通过 Application 契约与 Infrastructure 适配接入，连接按服务端配置的别名解析；请求不能任意指定数据库连接或 SQL。查询参数化，按业务所需 SBU 取数，日志不记录密码、Token、Cookie、完整连接串和敏感请求体。

外部调用明确超时、失败处理、幂等和重试边界，不无条件重试写入，不把本地事务等同于跨系统原子提交。不要直接复用用户、角色等 Identity 管理实体承载业务单据关系。

## 前端协作与交付

前端在独立的 `ZX.front.vue` 仓库，修改前阅读该仓库自己的 `AGENTS.md`；接口、页面、表单、多语言、请求层、动态组件登记及菜单配置按 `02` 第 9 节实施。本后端文件不替代前端约束，不因后端任务未经授权修改前端。

接口交付说明路径、请求字段、分页、返回类型、时间与空值约定、动作权限和 SBU 范围；区分已实现契约和计划。普通接口的操作成功提示由前端显示一次后端 message，界面文字与本地表单校验使用前端词条。

只在用户要求时执行 git commit、push、合并或发布，不覆盖成员改动、不强推共享分支。成员在自己的业务分支开发，提交后交负责人审核合并；交付包含迁移、菜单 / 权限、配置变更、验证结果和遗留事项。

## 验证方式

测试与验收由用户负责，AI 默认进行代码修改、文档维护和静态阅读，不自行运行 build、单元检查、浏览器验收、启动应用或调用真实服务。用户明确要求某项检查后执行该项；明确要求生成迁移时，允许 EF 命令必需的编译，不据此扩展其他检查。

交付时区分已执行、未执行与失败的检查。编译成功不代表数据库已升级，离线 SQL 检查不代表 Oracle 已执行，管理员可访问不代表普通角色和 SBU 已验证。发现文档与实际代码不一致时说明差异，不把未经验证的推测写成现有行为。
