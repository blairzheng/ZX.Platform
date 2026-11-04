---
name: zx-backend-business
description: "在 ZX.Platform 现有 Identity 四层内新增或扩展业务接口、单据、审批及状态流转，贯通实体、持久化、权限、SBU 与多语言。用于后端业务实施；纯前端页面、仅审查代码、登录或部署基础设施调整不由本技能主导。"
---

# ZX 后端业务开发

完成用户要求的后端业务链路，并给出准确的前端接入契约。简单变更只补需要的环节，复杂业务按实际规则设计；不要默认把所有需求做成 CRUD，也不要仅给计划而留下已授权的代码未实现。

## 先定位当前仓库和需求

本技能随后端仓库维护。相对本文件向上三级是仓库根目录；根据实际工作目录定位，不依赖 `ZX.Platform-team` 这个本机目录名或前端的相邻路径。

先读根目录 [AGENTS.md](../../../AGENTS.md)。按任务阅读 [02-business.md](../../../docs/02-business.md) 的对应章节；只有涉及配置、启动或测试库执行时再读 [01-start.md](../../../docs/01-start.md)。以当前分支源码为准，文档里的设备点检不是现成模块。

先检查工作区改动，确定本次是新增用例还是扩展已有用例，查找最相近的真实实现。不要覆盖已有改动、默认切分支，或机械复制文档示例生成无关文件。

从用户需求和现有代码确定数据归属、字段约束、动作与状态转换、SBU 范围及外部依赖。会影响业务正确性的缺失规则先查代码和文档；仍无法确定时提出具体问题，同时继续不依赖答案的工作。一般命名和现有模式可自行沿用。

## 按任务查找实际落点

以下项目简写都位于 `Services/Identity/ZX.Platform.Identity.*`，不是新建目录规范。

| 本次涉及 | 优先查看 | 实施细节 |
| --- | --- | --- |
| 实体、状态、审计 | `Domain/Entities/`、`Domain/Auditing/` | `02` 第 2、10 节；分清实体规则与需查询的规则 |
| 表、字段、索引 | `Infrastructure/Persistence/Configurations/`、`PlatformIdentityDbContext.cs`、`Migrations/` | 第 3、4 节；区分模型、迁移文件与数据库实际执行 |
| DTO、服务与事务 | `Application/Users/`、`Application/Persistence/IUnitOfWork.cs`、对应仓储 | 第 5、6 节；扩展现有契约，避免新增空壳层 |
| 路由、状态码、错误 | `Api/Controllers/`、`Contracts/ApiResponse.cs`、`ErrorHandling/GlobalExceptionHandler.cs`、`Application/Errors/ErrorCodes.cs` | 第 7 节；Controller 不直接操作 EF |
| 功能权限 | `Application/Authorization/IdentityPermissions.cs`、`Api/Extensions/AuthorizationExtensions.cs` | 第 7 节；追加常量和动作 Policy，保留原有目录机制 |
| SBU 隔离 | `Api/Authorization/RequireSbuAttribute.cs`、`CurrentSbuContext.cs`、当前业务仓储 | 第 6、7 节；校验归属后仍需逐查询过滤 |
| 服务注册与提示词条 | `Api/Program.cs`、`Localization/ApiMessageLocalizer.cs`、`Resources/Localization/` | 第 8 节；中英文资源、DI 都要贯通 |
| 页面接入与菜单 | `Application/Menus/MenuService.cs`、实际前端仓库的 `src/router/menuPages.ts` | 第 9 节；前端统一登记组件，仅有前端任务授权时修改对应前端仓库 |
| 外部 Oracle 取数 | `Infrastructure/ExternalData/`、`Api/Extensions/ExternalOracleDataSourcesExtensions.cs` | 第 10 节；复用服务端数据源别名，明确 SBU 与失败边界 |

表中是需要检查的入口，不要求每次都修改这些文件。阅读最小相关范围，不把所有源码和整份长文档一次性加载。

## 实施业务链路

先为当前用例确定输入、业务规则、返回结果与独立动作权限，再按现有分层落地：

- 领域行为负责单据自身的状态和约束；Service 统筹查询、跨对象规则、授权上下文与事务；Repository 实现数据访问。新增的服务和仓储在 API 注册。
- 有模型变更时先完成实体、映射、DbSet，再按 `02` 第 4 节处理迁移。没改模型就不生成空迁移；生成命令保留 `--output-dir Persistence/Migrations`，默认离线生成不带 `--development`。不重写已有迁移、手拼 Designer 或用个人 schema 污染 snapshot。
- 遵守根 AGENTS 的执行授权：未获生成迁移授权时提供准确命令并说明尚未生成；已明确授权生成时执行必要步骤。更新共享测试库需要对应授权，使用文档中的 `database update ... -- --development`。未执行数据库步骤仍可完成其余已授权代码，交付时标明联调前置项；不谎称新表可用。
- 为每个对外业务动作接入现有 Policy。SBU 业务只接受已校验上下文，仓储的列表、总数、详情、关联明细及写入都限定该范围。记录 ID、批量 ID 和关联父表 ID 同样不能绕过归属校验。
- 补齐 DTO 验证、成功和失败消息键、中英文资源、异常映射及依赖注册。确认词条被现有分发实际使用，不能只创建一个无人加载的资源文件。

审批、提交、撤回等动作各自核对允许的原状态、操作者权限、并发竞争及审计。不要把状态值直接交给通用 update 接口。涉及外部系统时单独明确超时、幂等和失败恢复，不能认为本地事务能包住远端操作。

无需为此开发修改身份认证、权限同步器、SBU 校验器或迁移工具。若真实需求确实依赖公共机制变化，说明具体缺口和影响，按任务授权处理。

## 对接前端

后端任务交付实际可用的路由、方法、请求字段、分页结构、时间与 NULL 约定、动作权限及 SBU 要求。需要新菜单时列出路径、组件标识和中英文标题，不自动写菜单或授权数据。

前端在 `src/router/menuPages.ts` 的 `menuPages` 中统一登记组件标识、`titleKey` 和显式 `load` 函数，路由与菜单编辑器共用该表。后端不追加前端组件名称列表；页面组件只校验 ASCII 字母开头、后续为字母/数字/下划线、总长 1～100 字符，目录与操作的组件为空。新增业务权限仍在 `IdentityPermissions` 声明，由真实接口独立鉴权。

既有组件标识保持稳定，与前端注册表键按大小写精确匹配。直接调用菜单 API 时，格式合法但前端未登记的标识可以保存，前端不会为其注册页面路由；先准备包含新页面的前端，再配置菜单。组件登记本身不改数据库结构，无需 Migration。查看权限由后端授权树自动补为“查看页面”叶子，不要求手工创建同 read 码的 action；业务操作按需配置 action 节点。

按 SBU 隔离的接口明确提醒前端设置 `sbuScoped: true`，业务成功提示显示后端 `message`。若用户也授权前端实施，先定位用户实际选定的前端仓库并读它自己的 AGENTS，再按 `02` 第 9 节完成；纯后端任务不修改前端。

## 静态核对与交付

结束前沿本次改动核对依赖注册、字段和路由契约、动作 Policy、SBU 查询条件、迁移文件及双语消息是否遗漏。只检查本次涉及的环节，不为一次小修复扩展成全系统审计。

是否执行编译、测试、应用启动或真实服务调用按根 AGENTS 和用户当前授权处理；调用本技能本身不授予这些操作、共享库写入、commit、push 或发布权限。

最终说明实现的业务行为、关键文件、接口 / 权限 / 菜单及迁移影响，区分已验证、未验证与必要的后续操作。权限只能管理员用、测试库未更新、前端未接入等限制如实写明；不把待办描述为已完成。
