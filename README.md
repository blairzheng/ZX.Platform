# ZX.Platform 业务开发

本仓库是业务开发的后端基础，使用 .NET 10、ASP.NET Core、EF Core 和 Oracle。平台提供登录、用户、角色、权限、菜单及 SBU 等基础能力，新增业务模块在 Identity 服务现有分层内开发。前端在独立的 `ZX.front.vue` 仓库维护。

成员负责具体业务的开发、联调、数据库迁移和代码交付；负责人审核合并，并统筹公共框架调整与正式发布。

## 阅读顺序

| 文件 | 用途 |
| --- | --- |
| `AGENTS.md` | 代码与 AI 辅助开发约束，开发前先阅读 |
| `docs/01-start.md` | 配置本机 User Secrets、连接团队环境、启动与联调 |
| `docs/02-business.md` | 按完整示例接入实体、迁移、接口、权限、SBU、多语言、前端页面及菜单，末尾说明交付要求 |
| [成员业务文档编写规范](docs/03-documentation.md) | 成员完成业务开发后该更新什么、怎样写及前后端分工 |
| [内置菜单越南语翻译清单](docs/vietnamese-menu-translations.md) | 内置菜单稳定 Code 的三语内容、维护脚本与校译状态 |
| [业务文档目录](docs/business/README.md) | 已实现模块的接口契约、业务规则、权限与数据范围 |
| [基于项目源码的 AI 助手](docs/ai-integration.md) | 配置 GPUStack 工具调用、导出业务源码资料、开通悬浮助手及只读查询 |
| [GoFlow 免登录对接](docs/business/integrations/goflow.md) | MOM 票据接口、GoFlow 后续接入约定及当前限制 |

AI 后续维护：[源码容量预警与检索优化待办](docs/ai-integration.md#源码容量与检索优化待办)。业务模块增加时提前跟进；当前有超限阻断，接近上限的自动预警尚未实现。

使用 AI 辅助开发时，先让它读取根目录 `AGENTS.md` 和当前任务对应的文档，再参考现有代码实施。

仓库附带 [ZX 后端业务开发 Skill](.agents/skills/zx-backend-business/SKILL.md)。使用支持仓库技能的 AI 工具时，可以这样描述任务：

> 使用 `$zx-backend-business` 开发设备维修申请，支持提交、审批和撤回，按 SBU 隔离。

技能读取现有代码和开发文档后按任务实施，不代表自动授权执行共享测试库迁移、提交代码或发布。

## 开发环境怎么用

- 前后端应用运行在成员本机，数据库连接团队提供的远程 Oracle 测试库。
- Redis 使用现有正式实例。本机开发设置独立的个人 `AuthenticationRedis:KeyPrefix`，与线上及其他成员隔离会话和缓存。
- 数据库、Redis 连接信息由负责人提供，写入本机 User Secrets，不提交到仓库。
- 不需要在本机安装或启动 Oracle、Redis 容器。普通业务所需的 NuGet 依赖已在项目中声明，拉取代码后统一还原。

配置项、还原命令和启动命令统一写在 `docs/01-start.md`。

## 业务代码写在哪里

以下目录均位于 `Services/Identity/` 下：

| 项目 | 放什么 |
| --- | --- |
| `ZX.Platform.Identity.Domain` | 业务实体、实体状态与领域规则 |
| `ZX.Platform.Identity.Application` | DTO、业务服务、仓储接口与用例流程 |
| `ZX.Platform.Identity.Infrastructure` | 仓储实现、Oracle 映射、DbContext 和数据库迁移 |
| `ZX.Platform.Identity.Api` | Controller、请求响应、资源词条与依赖注入 |

`ZX.Platform.Gateway/` 是请求转发入口，具体业务写在 Identity 内。新增模块按 `docs/02-business.md` 的步骤接入，复用现有认证、权限、响应、异常、日志和数据访问方式。

## 一次业务开发的流程

1. 同步团队最新基线，为当前任务创建自己的业务分支。
2. 涉及新增表或修改字段时，先完成业务实体、EF 映射及 DbSet 配置，再生成并检查迁移，协调应用到共享测试库。
3. 完成业务接口及权限，接入菜单和适用的 SBU 数据范围；访问新增表或字段前，确认测试库已执行对应迁移。
4. 在本机完成接口和前后端联调，核对正常、异常及权限场景。
5. 提交业务代码和相关迁移文件，说明改动、验证结果及待确认事项，发起合并请求交负责人审核。

迁移是业务开发步骤的一部分。生成命令必须带 `--output-dir Persistence/Migrations`，所有新增迁移统一放入 Infrastructure 项目的该目录。完整命令与测试库更新方式统一写在 `docs/02-business.md`。

迁移 `.cs`、对应的 `.Designer.cs` 和更新后的 `PlatformIdentityDbContextModelSnapshot.cs` 随业务代码一起提交。应用启动不会自动建表或执行数据库迁移；共享测试库的结构更新需要协调，按迁移文档执行。
