# MOM 平台开发者培训指南

[后端开发文档](README.md) · [后端开发者指南](developer-guide.md) · [前端开发者指南](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/developer-guide.md)

本文面向第一次接手 MOM 平台的开发者，目标是让开发者理解框架设计，并能独立接入一个业务模块。

本文以当前已落地的基础框架为准，包含 Gateway、Identity、Oracle、Redis、Vue 前端、认证、角色权限、SBU 和动态菜单。具体生产、质量、设备等业务模块需要按本文的方式继续接入。

## 一、先理解整体架构

### 1. 系统组成

```text
浏览器
  ↓
Vue 3 前端
  ↓ /api
Gateway（统一入口、转发）
  ↓
Identity API（登录、用户、角色、权限、SBU、菜单）
  ├── Oracle：平台数据和业务数据
  └── Redis：登录会话和权限缓存
```

外部 MES Oracle 库由业务模块通过后端连接工厂访问，与 Identity 自己的 Oracle 库分开。浏览器不能直接连接任何数据库，也不能自己决定访问哪个外部数据源。

### 2. 一次请求怎么走

以“查询 SBU 列表”为例：

1. 前端页面调用 `src/api/identity/sbus.ts`。
2. 统一请求层将请求发到同源 `/api`。
3. Gateway 按路由转发到 Identity API。
4. Controller 绑定请求并检查登录状态和权限 Policy。
5. Application Service 执行业务流程，调用 Repository 查询数据。
6. Infrastructure 使用 EF Core 从 Oracle 读取数据。
7. Controller 返回统一的 `ApiResponse<T>`。
8. 前端更新页面状态，DataTable 负责展示。

```text
页面 → api 方法 → request → Gateway → Controller
                                      ↓
                              Application Service
                                ↓          ↓
                         Domain 规则   Repository
                                             ↓
                                        Oracle / Redis
```

## 二、代码怎么分层

### 1. 后端目录职责

后端一个业务服务由四个项目组成，不是一个项目对应一个层级：

| 位置 | 负责什么 | 不负责什么 |
| --- | --- | --- |
| `Identity.Api` | Controller、HTTP 路由、请求绑定、响应、异常和依赖注入 | 不直接写数据库查询 |
| `Identity.Application` | DTO、用例服务、业务流程、仓储接口、权限常量 | 不引用 EF Core |
| `Identity.Domain` | 实体、状态变化和不依赖数据库的业务规则 | 不处理 HTTP 和数据库 |
| `Identity.Infrastructure` | EF Core、Oracle 映射、Repository、Migration、Redis 技术实现 | 不承载页面逻辑 |
| `ZX.Platform.Gateway` | 统一入口、代理和转发 | 不实现业务规则 |

依赖方向固定为：

```text
Api → Application → Domain
Api → Infrastructure
Infrastructure → Application + Domain
Gateway 不引用 Identity 业务项目
```

### 2. 前端目录职责

| 位置 | 负责什么 |
| --- | --- |
| `src/api/` | 后端接口方法、请求参数和响应类型 |
| `src/views/` | 页面查询、表单、表格和页面级状态 |
| `src/components/` | 多个页面复用的组件 |
| `src/router/` | 固定路由、动态菜单、路由权限和缓存元数据 |
| `src/stores/` | 跨页面共享状态，例如登录、布局和标签 |
| `src/utils/request.ts` | Token、统一响应、错误、取消和 loading |
| `src/locales/` | 中英文界面文案 |
| `src/icons/` | 本地 SVG 图标 |
| `src/styles/` | 主题变量和公共样式 |

页面不创建 Axios 实例，不直接 `fetch`，不把所有页面数据放进全局 store。页面专有状态留在页面，真正跨页面共享的状态才进入 store 或 composable。

## 三、框架的几个核心设计

### 1. 认证和权限是两件事

- 登录后，前端保存 Access Token；Refresh Token 由 HttpOnly Cookie 管理。
- Identity 用 Redis 保存会话，能够处理刷新、退出、强制下线和会话接管。
- 权限来源是“用户 → 多个角色 → 权限集合并集”。
- 后端 `IdentityPermissions.cs` 是权限目录的代码来源，Controller 使用同一个常量声明 Policy。
- 应用启动时同步权限目录；删除代码中的权限会让数据库中的旧权限变为不活跃。
- 前端按钮隐藏只改善界面，真正的接口权限必须由后端 Policy 校验。

新增一个后端功能权限时，需要同时完成：

1. 在 `IdentityPermissions.cs` 声明常量。
2. 在 Controller 使用 `[Authorize(Policy = ...)]`。
3. 补充中英文提示资源。
4. 让权限在菜单操作节点或角色授权树中可配置。
5. 在前端按钮使用权限常量控制显示。

### 2. SBU 是数据范围，不等于功能权限

功能权限回答“能不能操作这个功能”，SBU 范围回答“能看哪些数据”。需要按 SBU 隔离的接口必须在后端校验：

1. 使用 `[Authorize(Policy = "业务权限")]` 校验功能权限。
2. 使用 `[RequireSbu]` 要求请求带一个有效的 `X-SBU-ID`。
3. `RequireSbu` 查询当前用户是否关联了启用的 SBU。
4. Controller 从 `CurrentSbuContext` 取得服务端校验后的 SBU 信息。
5. Application Service 查询、详情和写入时继续校验记录归属。

不能只相信前端选择的 SBU，也不能只在 Gateway 校验请求头。

### 3. 菜单是数据库数据，页面仍然是代码

菜单表只负责组织和展示入口，不会自动生成 Vue 页面：

- `directory`：目录，只负责层级。
- `page`：页面，关联路径、权限和前端组件标识。
- `action`：按钮权限，不生成路由。

`Auth/me` 返回当前用户能看到的菜单树，前端根据 `src/router/dynamic.ts` 的组件映射注册动态路由。当前支持的页面组件由代码显式维护，例如 `SystemUser`、`SystemSbu`、`SystemRole` 和 `SystemMenu`。

没有页面子项的自定义目录会进入统一的“待建设”页面；菜单管理不会生成业务页面源码。接入正式页面时，必须同时维护后端组件白名单、前端组件映射、页面路由、权限声明和真实业务接口。

### 4. 数据库变更必须有 Migration

实体和字段变化按以下流程处理：

1. 修改 Domain 实体和 Infrastructure 映射。
2. 创建 EF Core Migration。
3. 在本地或测试库验证生成的 SQL。
4. 正式库由 DBA 或发布流程执行经过审核的 SQL。
5. 业务菜单数据使用菜单管理或专用 SQL，不把业务数据硬编码进结构 Migration。

不要直接在正式库改表后再补代码，也不要把真实密码、连接字符串和管理员初始化 SQL 提交到 Git。

## 四、用现有 SBU 管理理解一条完整链路

SBU 管理是当前已经完成的参考模块。阅读顺序如下：

### 后端

1. DTO：`Services/Identity/ZX.Platform.Identity.Application/Sbus/Dtos/`
2. 用例接口和实现：`ISbuService.cs`、`SbuService.cs`
3. 实体：`ZX.Platform.Identity.Domain/Entities/Sbu.cs`
4. Repository：`Persistence/ISbuRepository.cs` 和 Infrastructure 实现
5. Controller：`Services/Identity/ZX.Platform.Identity.Api/Controllers/SbusController.cs`
6. 权限：`Application/Authorization/IdentityPermissions.cs`
7. Oracle 映射和 Migration：`Infrastructure/Persistence/Configurations/`、`Migrations/`

例如新增 SBU 的调用关系是：

```text
POST /api/Sbus/add
  → SbusController.CreateAsync
  → ISbuService.CreateAsync
  → Sbu.Create + 业务校验
  → ISbuRepository + UnitOfWork
  → Oracle
```

### 前端

1. 接口方法：`src/api/identity/sbus.ts`
2. 页面：`src/views/system/sbu/index.vue`
3. 公共表格：`src/components/DataTable/`
4. 表单弹窗：`src/views/system/sbu/components/`
5. 权限常量：`src/api/identity/permissions.ts`
6. 动态页面映射：`src/router/dynamic.ts` 中的 `SystemSbu`

页面只负责调用接口、维护页面状态和展示结果；成功提示使用后端返回的 `message`，失败交给统一请求层处理。

## 五、新增一个业务模块的标准流程

### 第一步：先定边界

开发前先回答四个问题：

- 这是 Identity 管理能力，还是一个独立业务模块？
- 数据属于哪个服务和数据库？
- 记录是否必须按 SBU 隔离？
- 页面需要哪些查看、创建、修改、删除权限？

如果是独立业务服务，就按自己的 Api、Application、Domain、Infrastructure 组织，并通过 Gateway 暴露入口。不要跨服务直接读写对方的表。

### 第二步：实现后端

1. 在 Domain 定义实体和状态行为，字段不要公开随意修改。
2. 在 Application 定义输入 DTO、输出 DTO 和 Service 接口。
3. 需要查询或保存时增加 Repository 契约及 Infrastructure 实现。
4. 在 Service 中编排用例，统一传递 `CancellationToken`，最后一次性保存。
5. 在 Controller 暴露 HTTP 路由，添加登录和功能权限 Policy。
6. 需要 SBU 范围时添加 `[RequireSbu]`，并在查询和写入中校验记录归属。
7. 在 `IdentityPermissions.cs` 或业务服务自己的权限目录声明权限。
8. 补充中文和英文资源、Migration、接口文档和错误处理。

### 第三步：实现前端

1. 在 `src/api/<service>/` 定义接口方法和 TypeScript 类型。
2. 在 `src/views/<module>/` 编写页面，页面级查询和弹窗状态留在页面内。
3. 列表优先复用 `DataTable`，列标题使用 `computed` 和 i18n。
4. 需要真实页面入口时，在 `src/router/dynamic.ts` 增加本地组件映射。
5. 在菜单管理中配置 `page` 菜单的路径、组件标识和查看权限。
6. 在页面按钮上用 `auth.hasPermission(...)` 控制显示，但不代替后端授权。
7. 补充中英文文案、图标、主题变量和页面专题文档。

### 第四步：联调和验收

至少使用管理员和普通角色各验证一次：

- 未登录访问是否跳转登录。
- 没有页面权限时菜单是否隐藏、直接访问是否被拦截。
- 没有操作权限时按钮是否隐藏，接口是否返回拒绝。
- 不同 SBU 用户是否只能读取和修改自己的数据范围。
- 停用角色、用户或 SBU 后，权限和数据范围是否立即生效。
- 菜单刷新后动态路由、标签和缓存是否正确清理。
- 中文、英文、三种主题和小屏布局是否正常。

## 六、完整接入示例：设备点检记录

下面用“设备点检记录”演示一次新业务接入。它是培训练习案例，当前代码中并没有这个业务功能；重点是看清每一层应该做什么。

### 1. 先定业务规则

假设每条点检记录属于一个 SBU，包含设备编号、点检时间、结果和备注。需求先确定为：

- 用户必须拥有设备点检查看或维护权限。
- 请求必须带当前 SBU，用户只能读取和修改自己有权限的 SBU 数据。
- 点检时间统一保存 UTC。
- 业务表归设备服务管理，不直接写 Identity 的表。

### 2. 后端接入

如果设备点检是独立业务服务，目录可以这样组织：

```text
Services/Equipment/
├── ZX.Platform.Equipment.Api/
├── ZX.Platform.Equipment.Application/
├── ZX.Platform.Equipment.Domain/
└── ZX.Platform.Equipment.Infrastructure/
```

按以下顺序实现：

1. Domain 定义 `InspectionRecord`，提供创建、修改和状态变化方法。
2. Application 定义 `CreateInspectionRecordDto`、查询 DTO 和 `IInspectionRecordService`。
3. Infrastructure 配置 `EQUIPMENT_INSPECTION_RECORD` 表、Repository 和 Migration。
4. 权限目录增加 `equipment.inspection.read`、`equipment.inspection.create`、`equipment.inspection.update`。
5. Controller 增加查询、新增和修改接口，分别使用功能权限 Policy。
6. 需要 SBU 范围的接口增加 `[RequireSbu]`，Service 查询时增加 `SbuId` 条件，写入时校验记录所属 SBU。
7. 通过 Gateway 增加设备服务路由，返回统一的 `ApiResponse<T>`。

一次查询的调用链是：

```text
GET /api/equipment/inspection-records
  → InspectionRecordsController
  → RequireSbu 校验 X-SBU-ID
  → InspectionRecordService
  → Repository.Where(record.SbuId == currentSbuId)
  → Equipment Oracle
```

### 3. 前端接入

前端对应代码可以这样安排：

```text
src/api/equipment/inspection.ts
src/views/equipment/inspection/index.vue
src/views/equipment/inspection/components/InspectionEditor.vue
```

页面实现步骤：

1. 在 API 文件定义列表、创建和修改方法，以及请求和响应类型。
2. 页面使用 `DataTable` 展示记录，筛选、分页和弹窗状态留在页面内。
3. 新增或修改按钮根据 `auth.hasPermission(...)` 控制显示。
4. 在 `router/dynamic.ts` 增加明确的 `EquipmentInspection` 组件映射。
5. 在后端菜单中创建 `page` 节点，配置路径、组件标识和查看权限。
6. 在菜单的操作节点下配置新增、修改等按钮权限。
7. 补充中英文词条、图标、主题样式和页面文档。

### 4. 验收这个案例

用两个不同 SBU 的普通账号验证：

- 有查看权限的账号能看到页面，没有权限的账号看不到菜单。
- 直接访问页面地址时，前端路由和后端接口都继续校验权限。
- 用户切换 SBU 后，列表只返回新 SBU 的记录。
- 用户手工修改请求中的 `X-SBU-ID`，后端拒绝未授权的 SBU。
- 没有新增或修改权限时，按钮隐藏且接口返回拒绝。
- 管理员和普通账号分别验证新增、修改、空列表和错误提示。

这个案例讲完后，开发者可以把“设备点检”替换成实际的生产、质量或设备需求，接入步骤不变。

## 七、开发环境和交付

后端需要 .NET 10、Oracle 19c 及以上和 Redis；前端需要 Node.js 和 pnpm。连接信息使用 User Secrets 或部署环境变量，不写进代码。

后端本地启动：

```bash
dotnet restore ZX.Platform.slnx
dotnet run --project Services/Identity/ZX.Platform.Identity.Api --launch-profile https
dotnet run --project ZX.Platform.Gateway --launch-profile https
```

前端本地启动：

```bash
pnpm install
pnpm run dev
```

提交前按修改范围检查：

```bash
dotnet build ZX.Platform.slnx
git diff --check

pnpm type-check
pnpm exec vitest run
pnpm build-only
```

正式发布前确认：数据库 Migration 已审核并执行、Redis 可用、Gateway 路由和前端 API 代理正确、首个管理员和角色授权已由受控流程完成。发布过程见[部署文档](deployment.md)，数据库流程见[迁移操作手册](migration-operations.md)。

## 八、培训建议

建议按以下顺序培训：

1. 用架构图说明浏览器、前端、Gateway、Identity、Oracle 和 Redis 的关系。
2. 结合目录树说明后端四层和前端各目录职责。
3. 用 SBU 管理代码走一遍“DTO → Service → Entity → Repository → Controller → API → 页面”的完整链路。
4. 让开发者设计一个小型业务模块，先确定权限和 SBU 范围，再实现接口和页面。
5. 用管理员和普通角色做权限、菜单和数据范围验收。

培训结束的标准是：开发者能够说明一次请求怎么走，知道新代码放在哪里，并能独立完成一个带权限和 SBU 校验的简单业务功能。
