# 基于项目源码的 AI 助手

[返回首页](../README.md) · [本机配置与启动](01-start.md)

助手通过内部 GPUStack 模型理解问题，自主选择只读工具搜索和阅读本版本的业务源码，再组织操作说明。普通问候可以直接回答；“菜单怎么新增”通过阅读菜单页面、表单和语言词条回答；“最大库存是多少”才从获准的查询目录中选择盘点接口取数。后端不按问题关键词路由，也不每轮注入完整盘点数据。

前端使用可拖动的头像入口和聊天浮窗，支持 Markdown、连续对话、复制、新对话和取消；不显示源码引用区域。

回答统一面向业务用户：源码和工具元数据供模型内部理解、查询和核实，最终只用业务名称、界面字段、操作步骤和实际结果说明。`Application/AI/AssistantService.cs` 的系统指令明确要求不输出代码片段、接口地址或方法名、工具名、源码路径与行号、程序字段、数据库表名及权限码，历史对话中已有的实现标识也不沿用。例如查询用户总数后回答“当前查询范围内共有 2 个用户”，不展示 `Users.GetPageList` 或原始返回字段。必要的筛选范围、分页与权限限制仍须如实说明，账号、工号和料号等真实业务值保持原样；不能凭猜测补充 SBU 或权限原因。此处通过模型指令约束回答表达，不新增模型调用或前端文本删改；实际输出效果仍需使用模型验收。

## 模型配置

Identity.Api 的本机 User Secrets 继续使用以下配置；已有模型配置无需重填。部署时使用服务端机密配置，不把 Key 放入源码或前端。

| 配置键 | 说明 | 默认值 |
| --- | --- | --- |
| `AI:BaseUrl` | 内部 GPUStack 的 `/v1/` 基础地址 | 空 |
| `AI:Model` | 部署的模型名称，例如 `deepseek-v4.1-flash` | 空 |
| `AI:ApiKey` | 服务端 API Key | 空 |
| `AI:TimeoutSeconds` | 整个请求的总时限，包含多轮模型、源码工具和业务查询，5～60 秒 | 60 |
| `AI:MaxTokens` | 每次模型生成的输出 token 上限，256～4096 | 2048 |
| `AI:MaxModelRounds` | 每次聊天最多模型调用次数，2～6；最后一次要求直接回答 | 5 |
| `AI:MaxToolResultBytes` | 单次工具结果的 UTF-8 字节上限，4096～131072 | 65536 |

模型服务必须支持 Chat Completions 的 `tools`、`tool_choice` 和 `tool_calls`。本次代码没有调用真实模型验证部署兼容性。若服务返回 HTTP 400，界面说明模型未接受本次请求，提示管理员检查消息格式、工具调用及上下文配置；400 本身不能确定是工具能力缺失，不会退回无依据的固定回答。普通请求仍是非流式，完整回答生成后显示。

系统指令始终只有开头一条 `system` 消息。达到模型轮数或工具次数预算时，将收尾提示合并到首条系统消息，保留已取得的工具调用和结果顺序，并使用 `tool_choice=none` 要求直接回答。Qwen3.8 的官方聊天模板会拒绝中途出现的 `system`，因此不能在工具结果之后另加系统消息。此次兼容调整仅做静态核对，未调用内部模型验证；切换模型后仍需验证普通回答、工具查询及达到预算的收尾轮次。

本机配置沿用 `docs/01-start.md` 的 User Secrets 导入方法。服务器对应环境变量为 `AI__BaseUrl`、`AI__Model`、`AI__ApiKey`，以及可选的同名双下划线配置项。配置更新后重启 Identity。模型未配置只影响 AI 请求，不阻止其他功能启动。

## 源码资料如何产生

正常后端发布会自动生成源码资料，无需每次手工导出。`scripts/publish-images.sh` 在构建镜像前调用 `scripts/export-assistant-sources.py`；`scripts/package-release.py` 复用同一入口，因此本地打包和 GitHub Actions 生成后端发布包都会更新快照。原有 `deploy/docker/package.sh` 离线打包入口也在构建前自动导出。导出失败会停止打包，避免带着旧资料继续构建。`--upload-only` 只重传已有镜像包，`--database-only` 只生成数据库包，这两种模式不重新导出源码。

导出器使用 Python 3.10 及以上版本的标准库，只在打包机器或 CI 执行。它读取前后端业务源码并生成 JSON，不启动 API、不读取其机密配置，也不调用数据库或模型。生产容器仍运行 .NET，服务器不需要 Python 或项目源码。

本地打包默认从后端仓库相邻的 `ZX.front.vue` 读取前端源码；目录布局不同时，在打包机器配置环境变量 `MOM_ASSISTANT_FRONTEND_DIR` 为前端仓库绝对路径。正常发布继续使用原来的打包命令，不需要额外运行导出命令。

GitHub 的“生成后端发布包”工作流会同时获取前后端源码。运行时将 **Use workflow from** 选择为 `team`，前端默认读取 `blairzheng/ZX.front.vue` 的 `team`。仓库位置不同时，一次配置仓库变量 `MOM_FRONTEND_REPOSITORY`；跨私有仓库访问时，一次配置具有目标仓库只读权限的 secret `MOM_FRONTEND_READ_TOKEN`。工作流的可选输入 `frontend_ref` 默认为 `team`，需要与特定前端版本配套时可填写对应 tag 或完整提交 SHA；若页面沿用默认分支旧工作流的 `main` 输入值，运行前手动改为 `team` 或对应提交。每次运行会使用实际检出的前端提交生成资料；`release.json` 的 `assistantSources` 记录前端提交、资料包 SHA256、文件数和导出时间，便于核对本次资料来源。

快照仍位于 `Services/Identity/ZX.Platform.Identity.Api/AI/Knowledge/project-sources.json`，记录导出时间、项目相对路径、完整行文本和文本 SHA256。该 JSON 是自动生成的构建产物，已加入 `.gitignore`，不纳入版本控制；源码和导出脚本继续提交，资料来源通过发布包的 `release.json` 核对。取消跟踪不删除本机已有文件，后续重新生成也不会产生 Git 改动。

API 项目将生成的 JSON 复制到编译输出及发布目录，随 Identity 镜像一起部署；它不在 `wwwroot`，没有直接下载或任意文件读取接口。Git 忽略规则不影响 Docker 构建上下文，现有 `.dockerignore` 保留此文件；正常后端打包在构建镜像前生成它，因此全新检出也能通过同一流程带入镜像。

导出范围包括已登记的真实业务页面及组件、业务 API、界面词条、业务 Service/DTO、实体和业务 Controller。新增页面按现有 `menuPages.ts` 注册后，下次后端打包时自动纳入。AI 自身、演示页面、配置、User Secrets、认证实现、数据库连接、基础设施、发布脚本、日志、依赖和构建目录均排除；符号链接和越界路径拒绝处理，常见敏感字面量会脱敏。

运行时只读取随版本发布的快照，模型不能指定任意服务器文件路径，也不会在运行服务器上扫描源码。当前实现按进程缓存快照，部署新的 Identity 镜像并重启后使用新资料。前端单独发布不会更新已部署后端里的快照；新增前端业务需要 AI 同步了解时，应使用配套前端提交重新生成并发布后端包。文件数以实际导出结果及 `release.json` 为准。缺失或校验失败会向模型返回资料不可用，不把空结果当作系统没有功能。

全新检出的仓库不包含该 JSON。直接执行裸 `dotnet build`、`dotnet publish` 或 `docker build` 不经过上述发布脚本，不会自动生成或更新快照。本地需要调试 AI 源码检索或排查资料内容时，在后端根目录单独执行以下命令；前端目录不相邻时可加 `--frontend /前端仓库绝对路径`。生成或更新后重启 Identity，使进程加载新资料：

```bash
python3 scripts/export-assistant-sources.py
```

## 源码容量与检索优化待办

记录日期：2026-11-05。此项尚未完成，需要在业务模块持续增加时跟进，不能等到发布超限后才处理。当前已有硬限制和超限阻断，尚未实现接近上限时的自动预警或检索索引。

| 项目 | 当前硬限制 | 达到限制后的行为 |
| --- | --- | --- |
| 整份源码资料 | 12 MiB（12582912 字节） | 超过时导出失败，停止后续镜像打包，不用旧资料继续发布 |
| 收录文件数 | 2000 个 | 超过时导出失败，停止后续镜像打包 |
| 单个源码文件 | 512 KiB（524288 字节） | 超过时导出失败；脱敏后的文本另须满足运行时长度校验 |

现有运行时首次使用时加载并缓存整份资料；搜索仍逐文件、逐行匹配关键词。模型收到的是限量检索片段，不是整个 JSON。因此后续扩容除了文件大小，还必须关注后端内存、检索耗时和能否找到正确资料。

后续维护清单（未勾选即尚未实现）：

- [ ] **增加发布容量预警**：每次自动导出记录总字节数、文件数、最大单文件大小和各自占比；任一项达到硬上限的 80% 时，在打包日志和 GitHub Actions 摘要中明确提醒，并将容量统计写入 `release.json`。80% 是计划预警线，当前代码尚无该提醒。
- [ ] **提前优化检索**：按业务模块和代码片段建立索引，减少每次全量扫描；使用菜单操作、角色权限和盘点等真实问题核对检索准确性，记录首次加载及后续检索的耗时、内存占用后再确定扩容方案。
- [ ] **处理接近或超过上限**：先排查误收录、重复或可排除的资料，再评估按模块拆分与索引；不能只提高数字或截断资料后冒充完整导出。确需调整限制时，同时修改 Python 导出器和 C# 运行时校验，保持一致。
- [ ] **完成后核对发布行为**：验证接近预警线会提示、超限会停止打包且保留旧文件、正常容量仍可随版本发布；在获得相应检查授权后执行验证并记录结果，再勾选对应事项。

维护入口：导出限制在 `scripts/export-assistant-sources.py`；运行时限制和检索在 `Services/Identity/ZX.Platform.Identity.Infrastructure/AI/Knowledge/ProjectKnowledgeStore.cs`；容量信息交付在 `scripts/package-release.py`；自动生成入口见上一节。

## 模型可以使用的工具

| 工具 | 行为 |
| --- | --- |
| `search_project_sources` | 按模型提供的中文/英文搜索词查源码，最多返回 6 个相关片段，每段最多 1500 字 |
| `read_project_source` | 阅读快照中指定文件的一段，最多 120 行、16000 字；支持接着读后续部分 |
| `get_my_permissions` | 取得当前登录账号的实际功能权限，不允许指定其他用户 |
| `list_available_queries` | 按可选搜索词分页发现当前账号获准使用的查询，取得准确 ID、参数、SBU 要求、结果形状和源码位置 |
| `execute_query` | 使用目录 ID 和允许参数执行原 GET 查询；平面列表可后置筛选，树保留层级；重新认证并校验原接口权限 |

模型可以在一次回复中先搜索，再读取前端表单和对应词条。实际数据问题先发现查询目录，再调用统一执行工具；不会将未来所有模块的接口定义一次性塞入每轮模型请求。后台顺序执行工具，最多 10 次；不提供 Shell、SQL、网络任意请求、创建菜单或修改数据库的能力。原盘点专用的 `query_stocktake` 已移除，新增业务无需再加专用工具或修改工具分发的 `switch`。

源码用于解释当前版本的功能、表单校验和显示条件；不能证明实际库存、当前页面状态或服务器故障原因。比如“为什么我的新增按钮没有显示”，应结合源码和 `get_my_permissions`，不能只因为源码里有权限条件就断言用户缺权限。

目录每页最多 8 条，`offset` 默认 0；`hasMore=true` 时保持相同 `query`，使用返回的 `nextOffset` 继续。偏移量只计算当前账号获准查看的条目，无权限接口不占页位。目录搜索按已有元数据匹配，未写描述的中文关键词可能没有结果；此时可不传 `query` 分页浏览，或先查源码取得英文 Controller 名再搜索，不能把搜索为空当作功能不存在。找到目标后停止翻页，不重复拉取整个目录。部分认证源码仍排除于发布资料之外，目录中的 `sourcePath` 不保证可读，模型也可以结合返回类型及真实结果理解。

## 当前已接入的查询

现有 Identity 服务的 15 个受保护只读 GET 已全部标记。模型只能发现和执行当前账号原本有权访问的条目；开放查询不增加角色授权，也不改变业务数据范围。

| 查询 ID | 原 GET 路径 | 原权限 / 范围 | 返回形状 |
| --- | --- | --- | --- |
| `Auth.GetCurrentUser` | `/api/Auth/me` | 当前登录账号本人 | 资料详情，含本人角色、权限、SBU 和菜单 |
| `Users.GetById` | `/api/Users/getUser` | `identity.user.read` | 用户详情，参数 `id` |
| `Users.GetPageList` | `/api/Users/getUserList` | `identity.user.read` | `{list,total}`，沿用原筛选和分页参数 |
| `Roles.GetAll` | `/api/Roles/getRoleList` | `identity.role.read` | 完整角色列表，含角色权限码 |
| `Roles.GetOptions` | `/api/Roles/options` | `identity.user.roles` | 完整角色选项列表 |
| `Roles.GetPermissions` | `/api/Roles/permissions` | `identity.role.permissions` | 系统权限定义列表 |
| `Roles.GetPermissionTree` | `/api/Roles/permissionTree` | `identity.role.permissions` | 授权选项树，含合成节点 |
| `Roles.GetUserRoles` | `/api/Roles/userRoles` | `identity.user.roles` | 指定 `userId` 的完整角色 ID 列表 |
| `Menus.GetTree` | `/api/Menus/getMenuTree` | `identity.menu.read` | 菜单管理树，包含禁用项 |
| `Menus.GetOptions` | `/api/Menus/options` | 原 `PermissionPolicies.MenuOptions` | 菜单管理选项树 |
| `Menus.GetPermissions` | `/api/Menus/permissions` | 原 `PermissionPolicies.MenuOptions` | 系统权限定义列表 |
| `Sbus.GetById` | `/api/Sbus/getSbu` | `identity.sbu.read` | SBU 详情，参数 `id` |
| `Sbus.GetAll` | `/api/Sbus/getSbuList` | `identity.sbu.read` | 完整管理列表，包含禁用项 |
| `Sbus.GetAllEnabledSbus` | `/api/Sbus/getAllEnabledSbus` | 原 `PermissionPolicies.SbuOptions` | 完整启用选项列表，不等于本人 SBU 分配 |
| `InventoryStocktake.GetList` | `/api/InventoryStocktake/list` | `inventory.stocktake.read` | 原全局盘点 `{list,total}` |

登录、刷新令牌、退出、改密、创建、修改、删除、审批、授权和状态变更均未标记；Identity 和 Gateway 的匿名天气演示接口也不纳入业务查询。管理员预开通不再查询 LDAP，原目录预览接口已移除。当前账号资料不返回密码哈希、Token、Cookie 或会话标识。用户详情、审批说明及工号仍受原接口权限约束，不因此向所有聊天账号开放。

权限定义、授权选项树、角色的 `permissionCodes` 不是当前账号的实际权限。判断本账号权限优先使用 `get_my_permissions`。管理菜单树不是当前账号实际可见菜单，启用 SBU 选项也不是本人已分配 SBU；本人资料应使用 `Auth.GetCurrentUser`。目录声明的 `id`、`userId`、管理筛选 `sbuId` 只表示原接口允许的业务查询对象，不能替换调用者或改写已验证的请求 SBU。

## 新增查询如何向 AI 开放

业务成员正常开发查询接口，在确认实现确实只读后，给对应方法加 `[AiQuery]`：

```csharp
using ZX.Platform.Identity.Api.AI.Queries;

[AiQuery]
[HttpGet("list")]
[Authorize(Policy = IdentityPermissions.EquipmentInspectionRead)]
public async Task<IActionResult> ListAsync(
    [FromQuery] InspectionRecordQueryDto query, CancellationToken cancellationToken)
{
    var data = await records.ListAsync(currentSbu.SbuId, query, cancellationToken);
    return Ok(new ApiResponse<InspectionRecordPageDto>
    {
        Code = 0, Message = messages["Inspection.ListLoaded"].Value, Data = data,
    });
}
```

这只是给本文开发示例说明标记位置，不代表设备点检已经实现。上节现有查询均复用原接口、Service、Repository 和字段，不新增一套 AI 业务接口。后续新增方法仍需显式标记，不会自动开放新模块的所有 GET。

标记只允许放在方法上，描述可省略。`[AiQuery("查询设备点检记录")]` 也是可选写法，但不要求成员再维护一份说明。目录从已注册 MVC 端点和 ApiExplorer 收集路由、参数、返回类型等已有元数据，接口 ID 使用 `Controller.Action`，例如 `InventoryStocktake.GetList`。没有描述时，模型结合目录给出的源码位置、Service/DTO 和前端词条理解用途；元数据或源码不足时应说明缺少依据，不保证单凭方法名称就能正确理解业务。

普通 `{list,total}` 接口保持 `[AiQuery]` 即可，运行时按 `total` 与原始返回行数判断完整性。已确认原接口返回全量裸数组时，可用 `[AiQuery(ResultShape = AiQueryResultShape.CompleteList)]` 声明其完整可见列表；完整树使用 `CompleteTree`。这是可选的结果契约，不是业务描述，不改变原 HTTP 返回。未声明的裸数组仍标记为未知完整性，不因为返回数组就假定是全量。本次角色、菜单和 SBU 的现有全量查询已逐项核对并声明。

只有同时满足显式标记、单一 GET、受 `[Authorize]` 保护且输入可识别的接口才进入目录。允许现有 query/path 的标量和一维标量数组；查询 DTO 通过 ApiExplorer 展开为这些参数。复杂请求体、Header/Service 绑定等不作为模型输入；不支持的签名、匿名接口或有歧义的同名接口不会随意选一个开放。模型只能提交目录中的参数名和值，不能提供任意 URL、HTTP 方法、Token、请求头或 SQL。实际业务需要复杂查询契约时，先完善正常业务接口，不靠 AI 绕过原契约。

`[AiQuery]` 是开发者确认可以开放查询的声明，不授予用户权限，也不能证明 GET 方法内部绝不会写数据。代码审查仍需确认方法及其调用链只读；新增、修改、删除或状态流转不加该标记，非 GET 即使误标也不收录。

执行器在独立请求作用域中建立子 HttpContext，重新执行 Bearer/会话认证，组合并检查原端点的授权策略，再通过原 MVC 请求委托执行模型绑定、输入校验及 `RequireSbu` 等过滤器。不会用反射直接调用 Controller 方法跳过这些步骤。当前用户身份和 SBU 请求上下文来自服务端收到的请求，模型不能改写。

这是服务内的 MVC 调用，不重新经过 HTTP 中间件管线；原查询路由的中间件限流不会再次执行，聊天请求本身仍受限流及工具预算约束。后续接口如依赖额外的路由中间件授权或数据范围处理，应先将其纳入执行约束，再开放标记。

当前公共限流默认按单实例内的 IP、HTTP 方法和路由计算，3 秒 5 次，超限返回 429，同样适用于普通页面操作。本次开放查询未改变该公共配置；一次聊天最多实际执行 10 次工具调用且顺序执行，总时限默认 60 秒。按账号的 AI 查询限频、跨请求并发上限和业务结果缓存尚未实现，不能将单次工具预算视为这些限制。

`parameters` 用于原接口本来支持的查询和路径参数；`filters` 是可选的返回后筛选，不能改变数据库查询或扩展读取范围。原接口返回完整列表时，可按顶层标量字段筛选，例如盘点接口没有查询参数时：

```json
{
  "id": "InventoryStocktake.GetList",
  "parameters": {},
  "filters": {
    "factory": "1712",
    "storageLocation": { "eq": "N1D4" },
    "material": { "contains": "029-001H-0271-1" }
  }
}
```

字段直接对应标量默认为相等，也可显式使用单一 `eq` 或字符串 `contains` 操作；字段名和含义先看目录或源码，未知字段、复杂嵌套字段及任意表达式拒绝处理。分页接口不能把当前页当成完整结果；后置筛选也不能将分页结果变为全量。最大值、合计和排名只有在取得问题范围内的完整数据后才可计算。

`CompleteList` 的 `isComplete=true` 基于原接口结果契约，`completenessBasis=endpoint_contract`；原响应没有 `total` 时，`sourceTotal` 保持 `null`，不捏造总数，可按真实返回行数统计该可见集合。分页响应的 `sourceTotal` 是原接口筛选范围内的总记录数，可以直接说明；它不是后置 `filters` 后的全量匹配数。

树结果使用 `collectionKind=tree`，在 `data` 中保留完整 `children`，不套用平面列表 `filters`，防止筛掉父节点时遗漏子节点。`rootCount` 只计根，`nodeCount` 递归计这份接口视图里的节点；权限树含合成的“查看页面”和“未归类权限”节点，因此节点数不等于菜单表记录数。按菜单类型或权限码统计时需要区分类型并去重。超出容量仍整体拒绝，不截断树后宣称完整。

## 权限、数据范围和限制

悬浮入口和聊天接口沿用 `ai.chat.use`。后端启动会登记权限，管理员在角色管理中授权后，用户重新登录或刷新菜单与权限即可，无需独立聊天页面或菜单。源码工具只查询已筛选的业务资料；当前账号权限工具只返回本账号权限。

盘点查询仍要求原接口的 `inventory.stocktake.read`。后端使用实际登录用户检查权限，模型的声明和工具参数不能绕过。盘点沿用原报表的全局范围，不按 SBU 隔离；聊天不会继承盘点页面的前端筛选，用户可在问题中说明范围，由模型形成允许的参数或完整列表上的结果筛选。

前端有当前所选 SBU 时，通过现有请求层随聊天请求附加 `X-SBU-ID`；实际执行需要 SBU 的接口时，仍由原 `[RequireSbu]` 验证是否启用且分配给当前账号，仓储继续按已校验 SBU 查询。未选 SBU 不阻断普通聊天和全局盘点；需要 SBU 的查询会提示先选择，不能让模型自己传入另一个 SBU。

通用执行器限制完整接口响应最多 16 MiB；超过时在应用后置 `filters` 前就会失败，应缩小原接口参数范围或完善正常分页/筛选接口，不能靠后置筛选绕过该上限。交给模型的单次工具结果还受 `AI:MaxToolResultBytes` 限制，默认 64 KiB。超过时返回 `too_large`，不发送截断后假称完整的数据；分页响应会保留其未完整的含义。数据源失败返回不可用状态，不把异常正文发给模型。模型依据工具数据自行分析，重要数字仍应和报表核对；源码和当前报表均不能提供没有接入的历史、价格或生产计划。

每轮请求的工具过程只在本次后端请求内保留。浏览器仅发送最近最多 10 轮用户/助手文字，最多 21 条消息、每条 16000 字符、合计 48000 字符。源码和查询结果不保存在前端会话历史中，下一轮由模型根据语境重新选择所需工具。整体模型上下文有额外大小预算，不无限追加资料。界面历史在刷新、退出登录、账号或 SBU 切换后清空。

## 聊天接口

原接口路径继续使用：

- `POST /api/identity/api/AiChat/reply`（浏览器/Gateway）
- `POST /api/AiChat/reply`（Identity）

请求体仍是 `{ "messages": [{ "role": "user", "content": "菜单管理里面，新增一个菜单怎么操作？" }] }`。只接受交替的 user/assistant 消息，首尾为 user；系统消息、工具列表、工具结果和用户 ID 均由服务端控制。所选 SBU 通过现有请求层的 `X-SBU-ID` 传递，不加入模型可编辑的请求体。

成功返回现有 `ApiResponse`，`data` 包含 `reply`、`model`、`isTruncated` 和 `sources: [{ path, startLine }]`。`sources` 保留真实检索来源元数据，当前前端不展示该字段，也不打开服务器文件；答复正文按业务表达规则生成。模型 Key、完整源码和中间推理不会作为额外字段返回。

输入错误为 400，未登录/缺少聊天权限沿用 401/403，模型限流为 429，模型不可用、请求被模型拒绝、认证失败、超时或无效响应为 503。业务工具的权限不足和资料缺失以工具状态交给模型说明。模型非成功响应的诊断日志仅记录 HTTP 状态、是否允许工具、消息条数、系统消息条数及输出 token 配置，不记录对话、源码、业务明细、凭据或上游错误正文；模型请求不自动重试或跟随重定向。

## 本次验证和使用

已重新生成本地源码包，包含 111 个业务文件；现有 16 个受保护业务 GET 的标记覆盖、工具定义 JSON、项目 XML、导出脚本语法和源码包文件哈希已做静态核对。已补充可单独运行的 `--assistant-query-only` 内存 MVC 检查入口，覆盖目录分页与权限过滤、参数绑定与校验、SBU、完整数组与树层级、未知或部分完整性和容量边界；本次未执行构建、测试、浏览器验收、真实服务调用或发布。正常发布由后端打包入口生成资料，不需要另做人工导出。部署新版本、重启后端并刷新前端后，建议开始新对话，验证：

1. “菜单管理里面，新增一个菜单怎么操作？”应检索相关表单和词条，只给出界面入口、操作步骤和必要条件，不展示源码或引用位置。
2. 连续问“页面组件怎么选？”应结合前文继续阅读对应代码。
3. “一键盘点怎么用？”应使用源码资料；询问指定料号数量时应先发现 `InventoryStocktake.GetList`，再执行查询和允许的结果筛选。
4. “有哪些角色、启用 SBU 有多少、某角色配置了哪些权限、当前账号分配了哪些 SBU？”应选对应查询；角色权限定义和管理选项不得当作本人已拥有的权限或 SBU。
5. 用户筛选总数直接使用原接口 `total`，当前页不能当作全量；菜单树的子节点可读取，根数不当作全部菜单数，权限树合成节点不当作菜单表记录。
6. 管理员可浏览完整的两页目录；普通账号只能发现并执行原权限允许的接口，禁用项和无权限项的范围沿用原接口。未标记 GET、误标写入接口、任意 URL/参数及无效 SBU 均不能执行。需要 SBU 的后续业务按普通角色验证其数据范围。
7. 资料缺失、完整响应或工具结果过大、分页未完整以及查询失败时应说明情况，不能计算未经证实的全局最大值。
8. 验证取消、窗口拖动、收起后继续回复、新对话及中英文展示。
9. 询问“当前有多少用户”，再追问“你怎么知道的”“给我接口代码”，均不应展示查询 ID、方法名、地址、代码或源码路径；保留有依据的总数、筛选和分页说明，不将业务账号或料号误删。已有对话中包含实现标识时，新答复同样使用业务语言。本次只静态核对提示与文档，未执行模型调用或其他运行验收；后端更新并重启后新生成的答复使用新规则，旧消息不重写。
