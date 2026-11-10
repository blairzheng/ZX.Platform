# 本机配置、启动与联调

[返回首页](../README.md)

本机运行 Identity 和 Gateway，连接团队提供的远程 Oracle 测试库及现有 Redis。以下命令均在后端仓库根目录执行，即 `ZX.Platform.slnx` 所在目录。

## 1. 获取开发配置

向负责人获取测试库、Redis 的开发配置及测试账号。配置中至少包含以下四项：

| 配置键 | 填什么 |
| --- | --- |
| `ConnectionStrings:IdentityDatabase` | 远程 Oracle 测试库的完整连接字符串 |
| `IdentityDatabase:Schema` | 测试库表所属的大写 schema，不一定等于连接账号 |
| `ConnectionStrings:IdentityRedis` | 现有 Redis 的完整连接字符串；使用 Sentinel 时包含节点、密码和 serviceName |
| `AuthenticationRedis:KeyPrefix` | 个人开发前缀，例如 `mom:development:你的工号:identity:auth:` |

`IdentityDatabase:Schema` 填实际表 owner；正式脚本统一使用 `ZX_IDENTITY`，测试库以负责人提供的实际 owner 为准。该配置只指定表所属 schema，不会切换登录账号或授予跨 schema 权限。

Redis 复用现有正式实例，每位成员必须使用自己的开发前缀，与线上及其他成员区分。同一成员的本机实例使用同一前缀即可。

调试已有的一键盘点报表时，另向负责人获取 `ExternalOracle:Sources:MesEDA` 的连接配置，写入 Identity.Api 的本机 User Secrets；连接账号需要能读取 `EDA.G_WMS_KEYBOARD`。`BU17EDA` 是数据库名，`EDA` 是表 owner。未调试该报表时无需配置此项，连接信息不放入前端或源码。

使用 AI 助手时，另在 Identity.Api 的 User Secrets 配置 `AI:BaseUrl`、`AI:Model`、`AI:ApiKey`；模型需要支持工具调用。正常后端打包会使用 Python 自动生成前后端业务源码资料并随镜像发布，无需每次手工导出；打包机器需要 Python 3.10 及以上版本和配套前端源码，运行服务器不需要 Python。配置、自动生成和权限步骤见 [基于项目源码的 AI 助手](ai-integration.md)；不使用 AI 时可以不配置。

### 可选：OA / LDAP 登录

默认 `AuthenticationLdap:Enabled=false`。启用后，员工在登录页输入 OA 工号和密码，后端使用 `AuthenticationLdap:Domain`（默认 `LUXSHARE`）组成 `域名\工号` 直接向 AD 认证，再使用同一连接读取该员工的姓名、工号、objectGUID 和启用状态。普通员工必须具有读取自身这些属性的权限；读不到可信身份时返回目录不可用，不凭输入工号直接建立会话。错误密码不切换节点重试，原有超时、并发限制和登录失败限制继续生效。

本模式要求输入工号同时是 AD 的 `sAMAccountName`（当前默认配置如此），不支持先用另一工号属性查找不同登录名。

不再配置 `AuthenticationLdap:BindDn` / `BindPassword`，也不使用独立目录查询账号。目录节点、BaseDn 和属性沿用 `appsettings.json` 的默认值，环境不同再通过 User Secrets 或容器环境变量覆盖。使用默认 Plain/389 节点时，需明确设置 `AuthenticationLdap:AllowPlainText=true`；TLS 场景保持证书验证，按实际目录配置主备节点。

Linux 容器还需要系统 OpenLDAP 运行库。Identity Dockerfile 使用 .NET 10 的 Ubuntu 24.04（Noble）镜像并安装 `libldap2`，配套 `System.DirectoryServices.Protocols 10.0.13`；旧版 `8.0.2` 的库名加载规则与 Noble 不匹配，不能只安装系统包继续使用旧组件。修复 `DllNotFoundException` / `libldap-2.5.so.0` 后，需从本次提交重新构建后端镜像并部署到两台 Identity 节点，重启旧镜像不会带入这项修复。构建完成后仍需在目标环境验证 OA 登录。

管理员预开通只填写工号并选择待审批或立即开通，后端仅检查 MOM 内账号冲突并保存授权，不访问 LDAP，不需要员工提供密码；姓名暂显示工号。首次成功 OA 登录后，绑定真实目录身份并补齐姓名，保留同一 UserId、审批、角色和 SBU。已绑定账号不能自动换绑；禁用、拒绝、删除、锁定或身份冲突仍拒绝登录。没有预开通的员工仍可首次 OA 登录创建待业务审批账号。原 `/api/Users/ldapLookup` 目录预览接口已移除，AI 查询目录也不再包含此项。

本次不改变表结构，不需要 EF Migration。部署切换必须同时发布兼容的后端与前端新包，再移除旧查询账号变量，不能仅删变量继续使用旧镜像。GitLab 的 `MOM_LDAP_*` 到容器变量映射以部署仓库 `mom_img` 的说明为准，应用源码不会直接读取这些 GitLab 变量。

### 可选：GoFlow 免登录

MOM 与 GoFlow 的前后端已加入免登录对接代码，默认进入 GoFlow 首页；部署配置及线上联调仍需完成。契约、权限及流程见 [GoFlow 集成](business/integrations/goflow.md)。默认 `SingleSignOn:GoFlow:Enabled=false`，无需为普通开发填写配置。

启用时在 Identity.Api User Secrets（本机）或受保护的 `identity.env`（服务器）配置下面各项。两台 Identity 实例保持相同配置；测试与正式使用不同的客户端标识、回调和服务密钥，并沿用不同 Redis 前缀。

| 配置键 | 含义 |
| --- | --- |
| `SingleSignOn:GoFlow:Enabled` | GoFlow 对接和对应传输模式准备好后设为 `true` |
| `SingleSignOn:GoFlow:AllowHttp` | 默认 false；当前可信内网 HTTP 联调时显式设为 true，GoFlow 的 `MomSso:AllowHttp` 也要设为 true |
| `SingleSignOn:GoFlow:ClientId` | 本环境的客户端标识，例如 `goflow-staging`；1～100 位字母、数字、点、下划线或连字符 |
| `SingleSignOn:GoFlow:ClientSecret` | 独立的随机服务密钥，32～256 字符；建议由安全随机 32 字节转 Base64 生成，不能复用 JWT 密钥或 OA 密码 |
| `SingleSignOn:GoFlow:CallbackUri` | 唯一 GoFlow 回调（默认 HTTPS，AllowHttp=true 时允许 HTTP），固定为 `https://GoFlow域名/api/auth/sso/mom/callback`，不含查询或 fragment |
| `SingleSignOn:GoFlow:StartUri` | 同一 GoFlow 域名的 `https://GoFlow域名/api/auth/sso/mom/start` |
| `SingleSignOn:GoFlow:TicketLifetimeSeconds` | 默认 60，允许 30～120 秒 |

Docker / systemd 环境变量用双下划线，例如 `SingleSignOn__GoFlow__ClientSecret`。现有打包流程不嵌入真实配置；变量填入目标环境 Identity 的受保护配置文件，不放进 GitHub 构建变量、前端 `VITE_*` 或仓库源码。两边服务端的 ClientId / ClientSecret 必须一致，更新配置后重启对应实例。GoFlow 还需配置 MOM 授权页 `/sso/goflow/authorize`、后端兑换地址和目录来源，详见 GoFlow 仓库 `MOM-SSO.md`。当前两边均使用 HTTP 时，显式打开 MOM 的 `SingleSignOn__GoFlow__AllowHttp=true` 和 GoFlow 的 `MomSso__AllowHttp=true`，并将相应地址配置为实际 HTTP 地址；详细模板见 GoFlow `MOM-SSO.md`。HTTP 会明文传输票据和服务凭据，仅用于批准的可信内网联调。

启用且缺少有效服务密钥或固定回调时启动配置校验失败。默认只接受 HTTPS；AllowHttp 默认关闭且只影响本 SSO 集成，不修改其他登录、Cookie 或网关策略。后续切换 HTTPS 时将两边 AllowHttp 设回 false 并重启。代理入口仍只信任明确配置的代理，不关闭证书校验。

启动会按既有行为登记 `integration.goflow.open`，由负责人为实际 OA 测试角色授权。本阶段不新增表、不需要 EF Migration，也不自动修改共享角色、菜单或用户数据。

## 2. 还原依赖与 EF 工具

```bash
dotnet restore ZX.Platform.slnx
dotnet tool restore
```

第一条还原各项目已经声明的 NuGet 包；普通业务开发不需要逐个安装这些包。第二条按仓库 `.config/dotnet-tools.json` 还原固定版本的 `dotnet-ef`，供后续生成迁移和更新测试库使用，不需要另装全局版本。

## 3. 写入本机 User Secrets

数据库和 Redis 的真实连接信息只保存在本机，不写进仓库的 `appsettings*.json`。Identity API 已配置 `UserSecretsId`，不需要再次执行 `user-secrets init`。

负责人提供的开发配置 JSON 应包含以下结构。示例中的占位内容要替换成实际值，个人前缀中的 `你的工号` 也要替换：

```json
{
  "ConnectionStrings": {
    "IdentityDatabase": "负责人提供的测试库完整连接字符串",
    "IdentityRedis": "负责人提供的 Redis 完整连接字符串"
  },
  "IdentityDatabase": {
    "Schema": "负责人提供的实际大写SCHEMA"
  },
  "AuthenticationRedis": {
    "KeyPrefix": "mom:development:你的工号:identity:auth:"
  }
}
```

把配置文件保存在仓库外，例如个人配置目录。根据本机终端选择一条导入命令，将示例文件路径替换成你的配置文件绝对路径。

PowerShell：

```powershell
Get-Content -Raw -LiteralPath 'C:\DevConfig\zx-identity.development.json' | dotnet user-secrets set --project Services/Identity/ZX.Platform.Identity.Api
```

macOS / Linux 的 bash 或 zsh：

```bash
dotnet user-secrets set --project Services/Identity/ZX.Platform.Identity.Api < "$HOME/DevConfig/zx-identity.development.json"
```

导入完成后，真实配置存入项目之外的本机 User Secrets。命令从文件读取 JSON，不需要把密码直接写进命令行。后续拉取代码通常不需要重复导入；负责人更换连接信息或个人前缀需要调整时再更新。

同一台电脑上，相同 `UserSecretsId` 的项目副本共用这份配置，切换分支或重新克隆不会自动切换 User Secrets。配置出现疑问时先核对来源；不要把 `dotnet user-secrets list` 输出中的密码截图或提交。

当前开发环境已提供开发用 JWT 配置，普通业务开发不需要自行生成 JWT 密钥。OA 登录等专项功能需要的额外配置由负责人按任务提供。

## 4. 启动后端

首次使用本机 HTTPS 开发入口时，信任开发证书：

```bash
dotnet dev-certs https --trust
```

然后打开两个终端，分别在后端仓库根目录运行。

终端一，启动 Identity API：

```bash
dotnet run --project Services/Identity/ZX.Platform.Identity.Api --launch-profile https
```

终端二，启动 Gateway：

```bash
dotnet run --project ZX.Platform.Gateway --launch-profile https
```

统一使用仓库现有的 `https` profile。该 profile 设置 `Development` 环境，Identity 会读取本机 User Secrets。默认入口如下，实际端口以项目 `Properties/launchSettings.json` 为准：

| 用途 | 默认地址 |
| --- | --- |
| Identity Swagger，直接调试后端接口 | `https://localhost:7075/swagger` |
| Identity API | `https://localhost:7075/api/...` |
| Gateway，前端的后端请求入口 | `https://localhost:7178/api/identity/api/...` |

Gateway 默认转发到 `https://localhost:7075/`。只启动 Gateway，不能代替启动 Identity。

也可以使用仓库已有的编辑器启动配置：

- Visual Studio：打开 `ZX.Platform.slnx`，将 Identity.Api 和 Gateway 设为多个启动项目，使用 `https` profile。
- VS Code：打开后端仓库目录，选择 `Identity API + Gateway` 调试组合；单独调试时选择对应的 `Identity API (HTTPS)` 或 `Gateway (HTTPS)`。首次使用前仍需完成依赖还原。

## 5. 与前端联调

前端在 `ZX.front.vue` 仓库启动。本机开发沿用前端现有配置：

```dotenv
VITE_API_BASE_URL=/api
API_PROXY_TARGET=https://localhost:7178
```

请求路径为：

```text
浏览器 → 前端开发代理 → Gateway → Identity API → 测试库 / Redis
```

例如，前端通过 `/api/identity/api/Users/getUserList` 请求用户接口。Gateway 移除 `/api/identity` 前缀后，Identity 收到的是 `/api/Users/getUserList`。保留这套路径，不要在页面中另写后端绝对地址。

先确认 Swagger 可打开，再通过前端使用负责人分配的测试账号验证登录、加载菜单、调用有权限的接口和退出登录。只有 Swagger 可打开，还不能说明登录、Redis 会话和业务权限均已验证。账号缺少角色权限或 SBU 时，联系负责人分配。

## 6. 共享环境的操作约定

- 首次拉取时，测试库基础结构和测试账号应已由团队准备，不需要每位成员重复初始化。应用启动不执行数据库迁移，也不创建首个管理员。
- 已有测试库升级到大写迁移历史列时，由负责人先安排执行历史表兼容 SQL，再执行新版 EF 更新，步骤见 [业务开发步骤第 4.2 节](02-business.md#42-更新测试库)。普通成员不重复初始化或自行修补共享库历史表。
- Identity 启动会按当前源码同步权限目录和内置菜单。不同业务分支新增的权限声明可能不一致，涉及新增权限的并行联调先与负责人协调权限基线。
- 个人 Redis KeyPrefix 只隔离会话、权限缓存等 Redis 键，不隔离 Oracle 测试库里的用户、角色、菜单和业务数据。修改共享数据前协调，使用能区分任务的测试数据。
- 需要改表时，先完成实体、EF 映射及 DbSet 配置，再生成迁移并协调更新测试库；访问新增表或字段前确认迁移已执行。完整步骤统一按 `docs/02-business.md` 执行。

## 7. 常见问题

| 现象 | 先检查什么 |
| --- | --- |
| 提示未配置 `IdentityDatabase` | 是否把配置导入 Identity.Api 项目的 User Secrets，启动时是否使用 `https` profile |
| 报“表或视图不存在” | 测试库连接、实际表 owner、`IdentityDatabase:Schema` 及测试库迁移版本是否一致 |
| Redis 连接失败，或登录接口返回 503 | 完整异常及日志；核对公司网络、Redis/Sentinel 连接信息和密码 |
| Gateway 返回 502 或无法转发 | Identity 是否已启动，实际监听端口是否与 Gateway 目标一致 |
| 浏览器提示本机 HTTPS 证书不受信任 | 是否完成开发证书信任，是否使用配置中的 localhost 地址 |
| 登录成功但菜单为空，或业务接口返回 403 | 测试账号的审批状态、角色权限、SBU 分配及当前分支权限声明 |
| EF 更新库连接 `127.0.0.1:1`，或提示缺少测试库配置 | 更新命令是否保留 `-- --development`，是否已把测试库连接与 schema 导入 Identity.Api 的 User Secrets；使用第二份文档中的完整命令 |
| 迁移历史表提示 `MIGRATION_ID` / `PRODUCT_VERSION` 列不存在 | 由负责人确认目标库是否已执行第 4.2 节的历史表兼容 SQL；不要删除迁移历史或直接重跑初始化 |
| 本机修改后页面仍在请求其他环境 | 前端开发代理是否指向本机 Gateway；修改前端环境配置后是否重启开发服务 |
