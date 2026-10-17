# MOM 平台开发交接：Identity 基础版

> 更新日期：2026-10-17。用于换电脑、新开会话或交给另一位开发助手时恢复上下文。
> 本文记录阶段状态和已接受的设计；具体接口以源码及专题为准。接手时先查看 Git 状态，不要假定工作区仍与本文一致。

## 先读这一段

我们正在开发 MOM 制造平台，目前完成的是 Identity 与管理端基础版。用户、厂区、登录会话、角色权限、动态菜单和运行日志已经落地，可以进入实际 MOM 业务。尚未确定先做生产、质量还是设备；下一步应与用户梳理第一条真实业务流程，不要自行创建 MES/SPC 项目或重新设计认证。

用户重视代码直观、容易维护，反对为了极少见情况增加大量封装。重要业务规则、架构和服务拆分先讨论；常规实现、修复、文档维护在已授权范围内直接推进。

Refresh 滑动续期刚重新讨论过，用户明确决定“这一块暂时不实现”。当前维持 Access 15 分钟、Refresh 从首次登录起固定 7 天。不要把此前的方案讨论误认为已经实现或要求现在实现。

## 仓库和版本

旧电脑工作根目录是 `E:/myproject`，它本身不是 Git 仓库。下面是两个独立仓库，新电脑可以换盘符，按实际目录调整命令。

| 仓库 | Git 地址 | 分支 | 本文编写前的代码版本 |
| --- | --- | --- | --- |
| 后端 `ZX.Platform` | https://github.com/blairzheng/ZX.Platform.git | main | `d0f0400` |
| 前端 `ZX.front.vue` | https://github.com/blairzheng/ZX.front.vue.git | main | `8da4e50` |

开始编写本文前，两仓库工作区均干净。后端本地跟踪的 `origin/main` 与 `d0f0400` 一致；前端 `main` 当前未配置 upstream，不据此确认其远程是否最新。换电脑前确认两个仓库都已 push，本交接文档也要提交和 push 才会随 Git 迁移。上表是代码基线，不包含新增本文的后续文档提交。

后端近期提交：

- `d0f0400 chore(identity): 配置首次管理员账号 1130`。
- `ef29ef9 feat(logging): 完善请求日志与 journald 部署说明`，同时修复两个文件末尾误写的字面量 `\n`。
- `9d46f74 feat(identity): 实现角色权限与动态菜单`。
- `0f67185 feat(auth): 实现 JWT 登录与 Redis 会话管理`。

前端近期提交：

- `8da4e50 fix(menu): 调整菜单操作并默认收起树形控件`。
- `04073ab feat(auth): 接入角色权限与动态菜单`。
- `6aa6e5b feat(auth): 接入登录页面与会话管理`。

## 已完成什么

| 能力 | 当前结果和边界 |
| --- | --- |
| 用户 | 分页、账号/名称关键字、状态、厂区、创建日期范围筛选；新增、改名、分配厂区、启停用、软删除/恢复；批量软删除一次提交 ID 数组 |
| 厂区 | 新增、详情、列表、改名、启停用、软删除/恢复；列表不分页 |
| 持久化 | EF Core、SQL Server、Migration、唯一索引、软删除过滤、UTC 创建/修改时间；当前用户审计字段自动填写未实现 |
| 认证 | JWT Access、随机 Refresh、Redis 单账号会话、接管确认、刷新轮换、退出、强制下线、自助改密、失败锁定 |
| 授权 | 用户多角色、权限并集、Policy 校验、Redis 权限缓存与版本失效；厂区数据范围校验未实现 |
| 菜单 | 后端动态菜单、组件标识映射、目录/页面/操作、权限树、缓存和标签配置 |
| 管理端 | 用户、厂区、角色、菜单页面；登录、多语言、主题、标签与 KeepAlive |
| HTTP | 统一响应与业务码、参数校验、全局异常、本地化、按 IP/接口限频 |
| 运行日志 | Gateway/Identity 请求摘要、TraceId、异常；控制台输出，Linux 用 journald 收集 |

“基础版完成”不等于正式生产上线验收全部完成。后面列出的故障、部署和数据权限工作仍然保留。

## 架构和代码入口

后端 .NET 10，解决方案为 `ZX.Platform.slnx`；前端 Vue 3 + TypeScript + Vite + Element Plus + Pinia + vue-i18n，使用 pnpm。

```text
浏览器 /api/identity/api/...
  → Vite 开发代理 / 生产反向代理
  → Gateway (YARP，移除 /api/identity)
  → Identity Api → Application → Domain
                      ↓ 接口契约由 Infrastructure 实现
                  SQL Server / Redis
```

Identity 四层：Api 负责通信、响应和装配；Application 负责用例及仓储/技术接口；Domain 负责实体状态和规则；Infrastructure 负责 EF Core、Redis、密码和 JWT 实现。一个服务由多个 csproj 组成，不是每个项目一个微服务。一个 DbContext 的业务变更尽量一次 SaveChangesAsync，不并行使用同一个 DbContext。

后端重点入口（路径相对于 ZX.Platform）：

| 路径 | 用途 |
| --- | --- |
| `Services/Identity/ZX.Platform.Identity.Api/Program.cs` | DI、启动初始化、中间件 |
| `Services/Identity/ZX.Platform.Identity.Api/Controllers/` | Auth / Users / Factories / Roles / Menus |
| `Services/Identity/ZX.Platform.Identity.Api/Extensions/` | 认证、授权、限流、本地化、请求日志等装配 |
| `Services/Identity/ZX.Platform.Identity.Api/Authentication/JwtSessionEvents.cs` | JWT 成功校验后检查 Redis 会话 |
| `Services/Identity/ZX.Platform.Identity.Application/Authentication/AuthService.cs` | 登录、刷新、退出、改密流程 |
| `Services/Identity/ZX.Platform.Identity.Application/Authorization/IdentityPermissions.cs` | 权限定义来源 |
| `Services/Identity/ZX.Platform.Identity.Application/Menus/MenuService.cs` | 菜单业务与允许组件标识 |
| `Services/Identity/ZX.Platform.Identity.Infrastructure/Security/RedisAuthSessionStore.cs` | Redis 会话、刷新轮换和撤销 |
| `Services/Identity/ZX.Platform.Identity.Infrastructure/Security/RedisPermissionCache.cs` | 权限快照及版本 |
| `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/AuthorizationInitializer.cs` | 权限目录同步、默认菜单、首次管理员关系 |
| `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Migrations/` | Migration 和模型快照 |
| `Tests/ZX.Platform.Identity.AuthChecks/` | 独立认证与权限检查程序 |

前端重点入口（路径相对于 ZX.front.vue）：

| 路径 | 用途 |
| --- | --- |
| `src/utils/request.ts`、`src/api/codes.ts` | 统一请求、业务码、失败处理和 loading |
| `src/api/identity/` | 各 Identity 接口、DTO |
| `src/stores/auth.ts`、`src/utils/authSession.ts` | 登录身份、Access 存储、刷新和跨标签页协调 |
| `src/router/auth.ts` | 认证装配、路由守卫、401 与会话同步 |
| `src/router/dynamic.ts`、`src/router/mockRouter.ts` | 正式菜单转换与保留的 Mock 数据 |
| `src/views/system/user/`、`factory/`、`role/`、`menu/` | 四类管理页面 |
| `src/components/DataTable/`、`src/components/TableRowActions.vue` | 通用表格和行操作 |
| `src/styles/`、`src/locales/` | 公共样式、主题变量和中英文 |

详细架构先读[后端开发者指南](developer-guide.md)和[前端开发者指南](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/developer-guide.md)。

## 认证：保持当前方案，避免重新绕一遍

- Access 是标准 JWT：HS256、sub/name/sid/jti/iat/nbf/exp/iss/aud；JWT 不保存角色权限，签名不等于加密。
- Refresh 是随机秘密凭据，放 HttpOnly Cookie；Redis 只保存其哈希以及已消费哈希，不保存完整 Token。
- 每个用户一个 Redis JSON String，会话键为 `zx:identity:auth:session:{userId}`。JWT 标准验证通过后，再核对 Redis 的 sid 和当前 jti。
- 同账号一处有效会话；已有会话时登录返回接管确认，确认后替换；关浏览器不代表退出，“在线”指会话有效，不是浏览器心跳。
- 创建/接管只做一次 SET，接受两个并发登录都返回成功但最后写入者生效的窗口；不加 Lua、SemaphoreSlim 或分布式用户锁。
- 刷新、退出仍保留比较原 JSON 的条件事务，避免旧会话覆盖/删除新会话；不是所有操作都无条件 SET/DEL。
- 每次刷新轮换 Access 和 Refresh，旧 Access 也失效；重放已消费 Refresh 会撤销对应会话。响应丢失时不能无条件重试旧 Refresh。
- 禁用、删除、自助改密先保存数据库，再尝试撤销 Redis 会话。数据库与 Redis 不是同一事务；Redis 故障可能返回 503，但数据库已提交，恢复后应查询真实状态并重试下线。
- 用户已接受数据库安全变更与登录并发的小窗口，不能承诺严格立即失效；极端竞态的 Access 最长可继续一个 Access 周期，刷新会重查账号状态和密码指纹。
- Redis 不可用返回 503，不回退本机内存或放行。单例 ConnectionMultiplexer 复用连接。
- 当前 Access 15 分钟、Refresh 从登录起绝对 7 天，RotateAsync 保留原 ExpiresAtUtc；2026-10-17 用户再次明确暂缓滑动续期。
- 前端 Access 放 localStorage，Refresh 只在 HttpOnly Cookie。单标签合并刷新，多标签用 Web Locks 协调；业务页面不自己调用刷新。
- Refresh Cookie：SameSite=Strict、Path=/api、默认 Secure；Auth POST/PUT 带 `X-Identity-CSRF: 1`。生产使用同源 HTTPS；未做跨源凭据 CORS。
- 当前滑块是 vue3-slide-verify 前端交互，后端没有挑战校验，不能称作服务端防机器人能力。

详细规则和全部配置见[后端认证](authentication.md)、[前端认证](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/authentication.md)。

## 授权和动态菜单

用户 → 多个启用角色 → 权限并集；没有用户直接授权或拒绝规则。前端菜单和按钮只控制展示，后端 Policy 独立限制接口。

权限代码由 IdentityPermissions 定义，启动同步权限表：新增插入，移除标为不活跃。角色关联可以保留历史，但已移除权限不再生效。新增功能需同步权限定义、Policy、资源和前端入口，不能只在数据库添加任意权限就期望生效。

每个认证请求先查 Redis 会话；需要功能权限时再 MGET 全局版本和用户快照，未命中才查数据库。快照 TTL 5 分钟，改权递增共享版本；只在同一请求内复用，不建立跨请求本机 L1。缓存失效与数据库提交也不是一个事务，保留故障边界。

菜单由后端统一维护，不再拆成“开发环境本地一套、生产环境后台一套”。后端返回菜单树，前端在 dynamic.ts 显式映射组件并生成动态路由；菜单路径、标题、图标、排序、缓存、标签显示和固定标签由后台维护。角色授予 PermissionCode，没有独立 RoleMenu 表；按查看权限筛选页面，目录随下级保留。

前端先开发可用 mockRouter.ts 提供与 Auth/me 相同结构的菜单；联调时恢复真实响应。Mock 文件可以保留，默认不接生产。组件标识目前需前端映射与后端 MenuService 白名单一致。不要重新引入 local.ts/dev 路由体系，也不增加重复 MenuType/Perms 字段。

菜单翻译当前仍为 Title / TitleEn；将来越南文等语言拟改翻译表，已记录规划，尚未实现。所有树默认收起；菜单名称移除“内置”标签，行操作“新增下级”排第一。

详见[角色与权限](authorization.md)和[前端路由](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/routing.md)。

## 用户明确的开发偏好

1. 函数使用箭头函数，Vue 模板组件 kebab-case；脚本类型、导入保留 PascalCase。
2. 页面直接用 DataTable + computed 列配置 + TSX render/formatter；不套一层仅转发的 UserTable；表格列不增加无用途 key，插槽主要用于工具栏。
3. 列表 onMounted 首次加载，KeepAlive 保留查询条件、页码和结果，返回时不自动刷新；用户主动查询/刷新。普通列表不默认增加 AbortController、请求序号、激活状态管理；特殊业务按需要另议。
4. 统一 request.ts 检查 HTTP 和业务 code，失败 throw/reject；业务 await 后只执行成功逻辑，不重复判断 code=0，也不在每处写 catch 弹同样错误。
5. 真实 API 的成功提示直接用后端 message；确认框、界面文字、本地校验仍用前端词条；ElMessage 全局 plain:true。
6. 请求层统一即时全屏半透明 loading，已移除 300ms 延迟；普通业务不重复写 saving/mutationPending。特殊后台请求用 loading:false，再按实际场景处理。
7. 批量操作调用一次批量接口，不循环单删拼批量。用户页保留批量删除，不恢复批量启停用。
8. 修改用户名称和分配厂区是两个独立动作、两个请求；本人改名与修改他人/厂区的权限按现有规则处理，不再用一个弹窗依次调用两个写接口。
9. 行操作最多两个可见入口，动作超过两个显示“第一个动作 + 更多”，不是两个动作再加更多。现有 TableRowActions 统一负责，使用 Element Plus 图标和主题主色。
10. 所有树形控件默认收起。复用公共 form/search/table 样式，支持 Light、Dark、深色侧栏及中英文，用现有 CSS 变量，不堆页面私有重复样式。
11. 后端优先直接可读的普通代码、Task、LINQ 方法链；避免无收益的 ValueTask、复杂锁、Lua、通用动作包装、CQRS/Mediator。
12. 前端 AGENTS.md 规定测试/验收默认用户负责；助手只做代码、文档和静态阅读，用户明确要求后再执行类型检查、build、测试或浏览器验收。

用户给出的子代理约定：只在重型探索、多个独立核验确有收益时派发；仅做探索核验，默认角色、fork_turns=none；修改和最终判断主助手负责。新电脑以实际可用工具及仓库 AGENTS.md 为准。当前会话偏好用 FastCtx 绝对路径 read/grep/glob，机械替换用 replace，终端 run 使用 POSIX Bash。

## 新电脑恢复：代码不等于数据和本机配置

### 1. 安装环境和拉取两仓库

需要 Git、.NET 10 SDK、Node（package.json 当前要求 `^22.18.0 || >=24.12.0`）、pnpm。数据库是 SQL Server（旧开发环境为 2022），会话/权限使用 Redis（开发 Compose 为 7.4）；可用 Docker/WSL 部署依赖。

```powershell
# 目录可自行调整；已存在则跳过创建
New-Item -ItemType Directory -Force E:\myproject
Set-Location E:\myproject
git clone https://github.com/blairzheng/ZX.Platform.git
git clone https://github.com/blairzheng/ZX.front.vue.git
```

仓库已存在时，先检查各自 git status，再分别 pull。前端有 pnpm-lock.yaml，使用 pnpm，不随手换包管理器或重生成锁文件。

### 2. 迁移/连接 SQL Server 和 Redis

Git 只有模型和 Migration，**不包含数据库业务数据**。如果原 SQL Server 在旧电脑，需备份/恢复到新电脑，或明确允许新电脑连接原实例；换成新的空库不会自动拥有用户、角色、菜单配置和业务数据。

Redis Docker 数据卷也不会通过 Git 或重新运行 compose 自动搬过去。新 Redis 没有旧会话时，用户需要重新登录；不要把这种情况误判为认证代码故障。已有开发 Redis Compose 配置 AOF，每秒刷盘和数据卷，但它不是生产高可用方案。

本机 Secrets、证书、.env.*.local、Docker 卷、旧浏览器 localStorage/Cookie 均不在 Git。不要把真实密码或私钥补进本文或仓库。

### 3. 配置后端

以下在后端仓库根目录执行，示例中的地址、端口和凭据必须替换为新环境。14330 只是旧 Docker SQL Server 映射端口，不是项目硬性要求。

```powershell
Set-Location E:\myproject\ZX.Platform
dotnet user-secrets set "ConnectionStrings:IdentityDatabase" "Server=<SQL主机>,<端口>;Database=ZX_PLATFORM_IDENTITY;User Id=<账号>;Password=<密码>;TrustServerCertificate=True" --project Services/Identity/ZX.Platform.Identity.Api
# 若使用仓库开发 Redis 默认值，可以不额外设置；其他地址或有密码时覆盖
dotnet user-secrets set "ConnectionStrings:IdentityRedis" "127.0.0.1:6379,connectTimeout=3000,asyncTimeout=3000" --project Services/Identity/ZX.Platform.Identity.Api

docker compose -f compose.redis.yml up -d
dotnet restore ZX.Platform.slnx
dotnet dev-certs https --trust
```

Development 已提交共享开发 JWT 密钥，换电脑通常无需重新生成；User Secrets 和环境变量优先覆盖。生产必须注入独立密钥，各实例一致，不随每次重启生成。appsettings.Local.json 当前不自动加载，不能只新建该文件就期望生效。

**首次管理员账号配置已明确提交为 `["1130"]`，不是待排除的本机改动。** 初始化只在尚无 ADMINISTRATOR 角色时给配置中的已有、启用用户绑定管理员，不创建用户或密码。已有管理员角色后，改配置不会追加授权；后续走角色分配。全新空库没有 1130 时不能直接启动完成引导，需先受控建立首账号或调整首次名单，不要加默认密码/临时账号后门。

### 4. 按目标数据库应用迁移

应用启动不自动 Migrate；启动会读取权限、角色和菜单表，缺表会失败。对备份恢复的库只补尚未应用的迁移，不重建数据。执行前核对目标。

```powershell
# 未安装工具时执行；已有则检查版本，不重复安装
dotnet tool install --global dotnet-ef --version 10.0.12
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef migrations list --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext
dotnet ef database update --project Services/Identity/ZX.Platform.Identity.Infrastructure --startup-project Services/Identity/ZX.Platform.Identity.Api --context PlatformIdentityDbContext
```

迁移现状和命令见[数据库专题](database.md#迁移命令)。截至当前包括 AddRolesAndPermissions、AddMenus 和 AddMenuDisplayOptions；保留全部历史 Migration，不只复制最后一份。

### 5. 分别启动后端和前端

在后端根目录打开两个终端，分别运行：

```powershell
# 终端一
dotnet run --project Services/Identity/ZX.Platform.Identity.Api --launch-profile https
```

```powershell
# 终端二
dotnet run --project ZX.Platform.Gateway --launch-profile https
```

| 入口 | 默认地址 |
| --- | --- |
| Identity | https://localhost:7075，同时 http://localhost:5210 |
| Identity Swagger | https://localhost:7075/swagger |
| Gateway | https://localhost:7178，同时 http://localhost:5113 |
| 前端 | http://127.0.0.1:5173，以 Vite 实际输出为准 |

```powershell
# 第三个终端
Set-Location E:\myproject\ZX.front.vue
pnpm install
pnpm dev
```

前端提交的 .env.development 已设置 VITE_API_BASE_URL=/api、API_PROXY_TARGET=https://localhost:7178、开发代理 TLS 校验 false。只有本机地址不同才写 .env.development.local；改完重启 Vite。该本机文件不会迁移，当前真实值不要复制到公开文档。

浏览器使用 HTTP 127.0.0.1 时，若 Secure Refresh Cookie 被拒收，可仅在 Development 配置并重启 Identity：

```powershell
Set-Location E:\myproject\ZX.Platform
dotnet user-secrets set "AuthenticationCookie:Secure" "false" --project Services/Identity/ZX.Platform.Identity.Api
```

生产仍需 HTTPS 和 Secure。Vite→Gateway 使用 HTTPS 不等于浏览器→Vite 也是 HTTPS。Web Locks 需要现代浏览器及安全上下文，localhost/127.0.0.1 可用；普通 HTTP 局域网 IP 不满足前提。保持同一个浏览器入口，localhost 和 127.0.0.1 的站点存储并不相同。

运行详细步骤见[后端开发环境](development.md)和[前端开发环境](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/development.md)。

## 日志、多实例与部署边界

目前使用 .NET 自带 ILogger + HTTP Logging + Console Provider，未额外引入 Serilog、文件日志或日志表。开发从启动终端/VS 输出查看；生产 JSON 含 UTC 时间、级别、类别和 scope。Console Provider 后台队列容量 1024、DropWrite，队列满可丢包括 Error 在内的新日志，不能称为零开销或可靠审计。

Linux 需用 systemd 将 stdout/stderr 交给 journald，配置持久化、容量和保留期限；Cockpit 可提供运维网页查看，但平台自己没有日志页面。Docker 默认由容器日志驱动收集，不能假定自动进入宿主机 journal。完整命令见[运行日志](logging.md)。

多实例基本条件：共享 SQL Server、Redis、键前缀、JWT 配置和密钥、生命周期及一致权限目录，节点同步时钟；由 Gateway/Nginx 负载均衡，无需粘性会话。完整生产多实例部署尚未验收。限流目前每实例分别计数；修改权限目录时协调版本，不能承诺任意新旧版本混跑。journald 每台服务器独立存日志，统一检索以后再接集中采集。

未来其他服务的公共 JWT/Redis 认证组件尚未提取，权限目录跨服务同步也未完成；上述是 Identity 多实例的能力边界，不代表新增任意服务已自动获得认证授权。

程序当前只处理可信代理 X-Forwarded-For，不处理 X-Forwarded-Proto；生产 TLS 终止和 HTTPS 重定向需要部署时核对，不能把开发配置原样认作上线配置。现有 Dockerfile/Compose 不是完整验收的生产部署方案，详见 development.md。

启动 AuthorizationInitializer 已访问数据库并初始化 EF 模型/连接，也操作 Redis。用户已接受预热暂缓：先在 Release、不挂调试器的环境比较首次与稳定耗时，再决定少量只读预热及 readiness，不模拟登录/写操作，不承诺所有接口首访无冷启动。

## 数据、时间和文档约定

- 时间统一 UTC 存储和 ISO 8601 传输（Z/+00:00），页面按电脑时区显示；用户时间段筛选将本地边界转换 UTC。
- 将来固定厂区时区时，显示与日期筛选边界必须使用同一时区；运维 SQL Server / Oracle 时间换算参考 database.md，不再新增一套相互冲突的 UTC 方案。
- 功能权限与厂区数据范围不同，当前 UserFactory 关系及筛选不等于真正数据隔离或动态切库。
- 没有统一总并发 100 上限或统一后端 10 秒超时；报表下载耗时按实际业务设计。前端默认请求超时目前为 15 秒，可按请求覆盖。
- 接口契约已独立到 docs/api.md，其他专题不要再揉一整份接口清单。修改契约、数据库、权限或公共行为时同步对应文档。
- 不直接跨服务读写表，跨服务原子提交尚未实现；业务需要时再讨论 Outbox、幂等和补偿，不预先堆基础设施。

## 验证和清理的真实范围

已完成的验证记录：

- 后端编译、独立 AuthChecks；认证检查使用真实 Redis、多客户端和隔离随机前缀及替代业务仓储，不连接业务数据库。
- 2026-10-17 真实 SQL Server、Redis、Gateway、浏览器核心 RBAC 联调：角色授权/清空、用户绑定、普通账号菜单按钮过滤和 API 403；同一 JWT 改权后 403→200→403；刷新权限清理失权路由、标签和缓存。
- 自定义菜单主要显示和缓存设置已验收，临时验收账号、角色、菜单及 Redis 会话已按此前记录清理；不代表本文编写时重新逐项查库。
- 日志实现通过独立输出目录解决方案编译和隔离 HTTP 检查，两项目 Development/Production 摘要、200/201/400/401/403/429/500、UTC/TraceId、敏感测试标记排除通过；该检查未重新验收真实 Gateway 链路或 Linux journald。
- 两个文件末尾字面量反斜杠 n 导致的 CS1056/CS0116 已修复，随 ef29ef9 提交。

仍未覆盖或待处理：

- 完整角色启停删、多角色真实 UI 流程、空库首次管理员初始化、用户恢复、Redis 故障时数据库部分提交、多实例目录同步。
- 完整认证 UI 场景（接管、到期刷新、多标签、故障恢复）及全部中英/主题/小屏组合；核心登录用过不代表所有场景完整验收。
- 前端历史类型检查通过，但 8da4e50 的树和菜单轻量调整仅静态检查，未再运行类型检查/build/浏览器；未完成生产构建和完整自动端到端验收。
- System.Security.Cryptography.Pkcs 9.0.11 仍有 NU1902/NU1903 传递依赖告警，需后续升级，不宣称漏洞检查已通过。
- 新电脑启动、Linux journald、正式生产配置、监控、备份和容灾仍需按实际环境验收。

生产 Program.cs 中临时建测试账号的逻辑已移除，正式源码没有留临时账号后门。独立 Tests 项目是有意保留，不需要随生产 API 启动。手工 .http 示例执行会改数据，不自动回滚。前端原 Demo /examples/users 和工作台仍用演示数据，mockRouter.ts 有意保留。旧电脑被忽略的 .tmp 里仍可能有历史截图、备份及工具产物；不要声称物理文件全部清空，也不提交这些产物。

## 下一步从哪里接

1. 用户目标是 MOM。先确认首个要落地的真实业务、角色、输入输出、状态、厂区范围和对外依赖。
2. 用户尚未选择“生产执行 / 质量 / 设备采集”，不能代替用户决定。工单→下达→开工→报工→完工只是候选例子。
3. 根据第一条流程提取必要基础数据，判断生产/质量/设备之间的数据所有权和服务边界。Identity 保留独立，其他业务不按每张表或每个菜单拆微服务。
4. 第一条闭环涉及厂区业务时，把真正的厂区授权校验纳入设计；不能只靠前端下拉框或用户列表筛选保证隔离。
5. 只在有明确需求时引入 MQ、审计服务、Prometheus/Grafana、分布式一致性方案，不在接手时把它们全部铺开。
6. 预热和滑动续期当前均未安排实现，保持后续议题；后者用户本轮明确暂缓。

新会话可直接复制这段提示：

```text
请先完整阅读后端仓库 docs/project-handoff.md，再读取两个仓库的开发者指南及 AGENTS.md，并检查 Git 状态。
这是我们 MOM 制造平台的接续开发，Identity 基础版已阶段性完成。先概括你理解的当前状态、未完成事项和新电脑环境缺口，再根据我接下来的具体需求推进。
不要重新设计认证/菜单，不要默认实现滑动续期、预热或新拆服务。代码和文档以当前仓库为准。
```

## 后续查阅入口

| 主题 | 后端文档 |
| --- | --- |
| 架构和扩展流程 | [developer-guide.md](developer-guide.md) |
| SDK、Secrets、启动和部署 | [development.md](development.md) |
| 数据库、UTC、SQL、Migration | [database.md](database.md) |
| 路由、请求、DTO 和响应 | [api.md](api.md)、[http.md](http.md) |
| 登录、Cookie、Redis 与并发 | [authentication.md](authentication.md) |
| Policy、目录、缓存、菜单 | [authorization.md](authorization.md) |
| 多语言与频率限制 | [localization.md](localization.md)、[rate-limiting.md](rate-limiting.md) |
| 运行请求日志、journald | [logging.md](logging.md) |
| 未实现和暂缓事项 | [roadmap.md](roadmap.md) |

前端以其 [AGENTS.md](https://github.com/blairzheng/ZX.front.vue/blob/main/AGENTS.md)、[文档导航](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/README.md)、[开发者指南](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/developer-guide.md) 和各专题为准；新电脑本地有两个仓库时优先读取本地文件。
