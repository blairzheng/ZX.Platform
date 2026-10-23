# 开发环境、命令与排错

[项目首页](../README.md) · [文档导航](README.md)

以下命令在后端仓库根目录执行，示例使用 PowerShell。Visual Studio 也可以打开 `ZX.Platform.slnx`，将 Identity.Api 和 Gateway 设置为多个启动项目。

## 环境要求

| 依赖                    | 当前用途                                          |
| ----------------------- | ------------------------------------------------- |
| .NET 10 SDK             | 项目均以 `net10.0` 为目标，编译与运行 Web API     |
| Oracle 23ai / 26ai       | Identity 数据库；本机使用 Oracle 26ai Free |
| Redis                   | 共享认证会话；开发 Compose 使用 Redis 7.4         |
| Visual Studio / VS Code | 编辑和调试，选择支持 .NET 10 的版本               |
| Docker / WSL            | 可选，用于本机 Oracle、Redis 等基础设施              |
| Git                     | 后端独立仓库版本管理                              |

YARP、EF Core、Swashbuckle、BCrypt 和 StackExchange.Redis 的版本以各 `.csproj` 为准。仓库没有 `global.json` 或本地工具清单；更换 SDK / 工具时应保持与项目目标兼容。

## 数据库配置

Identity 必须读取到 `ConnectionStrings:IdentityDatabase`，否则 [Program.cs](../Services/Identity/ZX.Platform.Identity.Api/Program.cs) 会在启动时抛出配置缺失异常。Gateway 不连接数据库。

开发环境使用已经在 Api `.csproj` 中配置的 User Secrets：

```powershell
dotnet user-secrets set "ConnectionStrings:IdentityDatabase" "User Id=ZX_IDENTITY;Password=<数据库密码>;Data Source=127.0.0.1:1521/FREEPDB1" --project Services/Identity/ZX.Platform.Identity.Api
```

本机 Oracle 使用端口 `1521`、PDB 服务名 `FREEPDB1` 和独立 schema `ZX_IDENTITY`。按自己的实例替换连接地址、服务名和凭据，不使用 SYSTEM 作为应用账号。

User Secrets 只用于本地开发，保存在项目之外，不是生产密钥管理方案。生产或容器环境可注入 `ConnectionStrings__IdentityDatabase` 环境变量。不要把真实连接字符串、密码或证书写入文档和提交文件。

`appsettings.json` / `appsettings.Development.json` 维护可共享设置；`appsettings.Local.json` 虽被 Git 忽略，但当前程序没有自动加载这种文件，不能只创建它就期待生效。初始化和更新数据库见[迁移命令](database.md#迁移命令)。

Development 已在 `appsettings.Development.json` 提供共享开发 JWT 密钥，拉代码或换电脑后无需单独生成；已有 User Secrets 或环境变量优先覆盖。开发密钥公开且仅用于开发，生产需注入独立密钥。先应用 Oracle 迁移，再按[首次管理员初始化](authorization.md#首次管理员初始化)配置已有账号；名单仅引导一次，后续通过用户角色维护。Cookie 默认 Secure，开发 HTTP 入口的调整见[登录与会话](authentication.md)。Redis 默认 `127.0.0.1:6379`，通过 `ConnectionStrings:IdentityRedis` 覆盖；会话无法连接时接口返回 503，不退回内存。

接口时间字段统一以 UTC 的 ISO 8601（带 `Z` / `+00:00`）传输，数据库也按 UTC 存储；前端或其他调用方在显示时再按业务时区格式化。后续定时任务应保存本地时间和明确的业务时区。约定见[时间字段格式](http.md#时间字段格式)。

## 本地启动

Apple Silicon Mac 上的 Colima、Oracle Free 和 Redis 容器配置见[macOS 本机基础设施](local-infrastructure.md)。Identity 已使用 Oracle Provider；Oracle 迁移位于 `Persistence/OracleMigrations/`。旧 SQL Server 迁移仅存档，不编译、不用于 Oracle。

有 Docker 时先启动开发 Redis；没有 Docker 可使用已部署的 Redis 并修改连接配置，见[Redis 启动与故障](authentication.md#redis-启动与故障)。

```powershell
docker compose -f compose.redis.yml up -d
```

```powershell
dotnet restore ZX.Platform.slnx
dotnet dev-certs https --trust
```

在两个终端分别运行：

```powershell
dotnet run --project Services/Identity/ZX.Platform.Identity.Api --launch-profile https
```

```powershell
dotnet run --project ZX.Platform.Gateway --launch-profile https
```

| 服务     | `https` profile                                            | `http` profile          |
| -------- | ---------------------------------------------------------- | ----------------------- |
| Identity | `https://localhost:7075`，同时监听 `http://localhost:5210` | `http://localhost:5210` |
| Gateway  | `https://localhost:7178`，同时监听 `http://localhost:5113` | `http://localhost:5113` |

`https` profile 同时设置 `ASPNETCORE_ENVIRONMENT=Development`，启用 User Secrets 和开发接口文档。只运行 `http` profile 时，还必须调整 Gateway 目标地址和前端代理，不能沿用当前 HTTPS 目标。

### VS Code 调试

安装微软 C# 扩展（可同时安装 C# Dev Kit），在 VS Code 中打开后端仓库 `ZX.Platform` 文件夹。完成依赖还原后，在“运行和调试”的下拉列表选择 `Identity API (HTTPS)` 或 `Gateway (HTTPS)`，按 F5 调试；选择 `Identity API + Gateway` 可同时调试两个服务，停止该组合时会停止两个服务。

仓库的 `.vscode/launch.json` 使用两个项目已有的 `https` profile，启动前通过 `.vscode/tasks.json` 编译对应项目。编译任务使用 `--no-restore`，首次克隆或修改 NuGet 依赖后先执行 `dotnet restore ZX.Platform.slnx`。

启动配置不会创建数据库或 Redis，也不会迁移旧电脑的 User Secrets。Identity 仍需按上文配置数据库连接、准备数据库和 Redis；如果提示未配置 `IdentityDatabase`，先恢复连接配置。Gateway 能启动不代表 Identity 已启动。

开发入口：

- Identity Swagger：`https://localhost:7075/swagger`，JSON 为 `/swagger/v1/swagger.json`。
- Gateway OpenAPI：`https://localhost:7178/openapi/v1.json`，描述 Gateway 自身接口，不会聚合 Identity Swagger。
- Gateway 业务路径：`https://localhost:7178/api/identity/api/Users/getUserList?page=1&pageSize=20`。

## Gateway 与前端联调

当前 [Gateway/appsettings.json](../ZX.Platform.Gateway/appsettings.json) 匹配 `/api/identity/{**catch-all}`，移除 `/api/identity` 后转发到 `https://localhost:7075/`。

本机 Development 配置对该 Identity cluster 设置 `HttpClient:DangerousAcceptAnyServerCertificate=true`，用于连接本机 ASP.NET Core 自签名开发证书，避免 Identity 正常但 Gateway 因证书不受信任返回 502。此设置仅放在 `appsettings.Development.json`；生产环境必须使用受信任证书，并通过实际部署配置设置目标地址。

```text
浏览器 /api/identity/api/Users/getUserList
→ Vite 代理到 Gateway https://localhost:7178
→ YARP 移除 /api/identity
→ Identity https://localhost:7075/api/Users/getUserList
```

前端用同源 `/api`，本机覆盖配置在前端 `.env.development.local`。完整说明由[前端统一请求层](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/http.md#环境与-gateway)维护。

Vite、Gateway 和 Identity 共同传递客户端 IP。每层只信任已配置的直接上游代理；部署时要调整 `ForwardedHeaders:KnownProxies` 和 YARP 目标地址，见[代理配置](rate-limiting.md#客户端-ip-与代理)。当前程序只处理 `X-Forwarded-For`，没有根据代理的 `X-Forwarded-Proto` 改写 scheme，HTTPS 重定向仍按当前连接处理。

## 开发与检查命令

```powershell
dotnet build ZX.Platform.slnx
$env:IDENTITY_TEST_REDIS = "127.0.0.1:6379"
dotnet run --project Tests/ZX.Platform.Identity.AuthChecks --no-launch-profile
```

只检查某个服务时指定项目：

```powershell
dotnet build Services/Identity/ZX.Platform.Identity.Api/ZX.Platform.Identity.Api.csproj --no-restore
dotnet build ZX.Platform.Gateway/ZX.Platform.Gateway.csproj --no-restore
```

格式化应限定本次文件，例如在仓库根目录执行：

```powershell
dotnet format whitespace . --folder --include Services/Identity/ZX.Platform.Identity.Api/Program.cs --verbosity minimal
git diff --check
```

认证检查项目不依赖额外测试框架，以进程退出码表示结果，连接真实 Redis，验证两个独立客户端、接管/撤销、轮换重放、503 故障、认证流程及临时本地 HTTP 接口；只使用随机测试前缀，不清库；默认不连接业务数据库。显式提供 `IDENTITY_TEST_ORACLE` 时额外执行可回滚的 Oracle 实库检查，见[数据库专题](database.md#可选-oracle-集成检查)。尚无 CI 验收流程；真实数据库、Gateway、浏览器 Cookie 和容量仍需单独验收。接口和限流场景分别见[HTTP 验收](http.md#验收要点)与[限流验收](rate-limiting.md#人工验收)。

2026-10-15 本轮已通过解决方案编译及上述检查。最初使用隔离端口的临时 Windows Redis 5.0.14.1，检查后已关闭；随后通过仓库 Compose 在 WSL Docker 启动 Redis 7.4.11，确认 Windows 可连接 `127.0.0.1:6379`。替换为标准 JWT 后，再次通过解决方案编译及全部认证检查，包括实际 JwtBearer HTTP 验证、Redis 会话撤销及旧随机 Access 会话迁移。真实 SQL Server / Gateway / 浏览器链路及生产部署仍需单独验收。本次 JWT 依赖从本机 NuGet 缓存还原，还原命令仅本次设置 `NuGetAudit=false`；既有传递依赖 `System.Security.Cryptography.Pkcs 9.0.11` 的 NU1902 / NU1903 提示尚未处理，不能将本次无警告编译视为安全审计通过，依赖升级另行跟进。

认证会话已移除全部 Lua，存储改为 JSON String，创建/接管只用一次 `SET` 保存 JSON 及绝对过期时间，同时登录以最后写入者为准。C# 读取判断，刷新和退出保留比较原 JSON 的条件事务。解决方案编译、真实 Redis / HTTP 认证检查再次通过，覆盖 JSON 存储和绝对过期时间、旧 Hash 迁移、多次刷新后的重放、旧刷新/退出不影响并发接管的新登录，以及退出与刷新并发后不会留下有效凭据。

## Git 与本机文件

[.gitignore](../.gitignore) 排除 `bin/obj`、IDE 缓存、`*.user`、`.tmp`、日志、测试产物、本地配置和私钥证书。源码、`.slnx`、`.csproj`、Migration、Dockerfile、文档和共享 appsettings 都提交。

`.gitignore` 只决定未跟踪文件，不会自动移除已经提交过的文件；首次提交后可用 `git ls-files -ci --exclude-standard` 核对有无已跟踪的忽略项。工作目录的总大小包含编译产物，不等于实际 Git 上传大小。

浏览器代理与 Git 代理配置不同。需要代理时按自己的端口设置，只限当前仓库和 GitHub，例如：

```powershell
git config --local http.https://github.com.proxy http://127.0.0.1:<代理端口>
```

代理写入本机 `.git/config`，不进入提交；不再使用代理时可通过 `git config --local --unset http.https://github.com.proxy` 清除。

2026-10-22 Oracle 切换验证：解决方案构建零警告、零错误，AuthChecks 的 Redis / HTTP 认证检查及 Oracle 实库检查均通过。Oracle 已应用独立初始迁移，八张业务表与索引已建立；真实 Identity HTTPS 启动成功，Swagger 返回 200，匿名管理请求返回 401。启动同步 26 条权限和 32 条默认菜单，用户、角色、厂区仍为空；按本次要求不创建管理员。Oracle 测试写入已回滚，验证进程已关闭，VS Code 可按原启动配置调试。`System.Security.Cryptography.Pkcs` 已固定至修复版本 10.0.13，不屏蔽 NuGet 审计。随后按用户要求受控创建管理员 `31377716`，真实 API 登录、26 项管理员权限和用户管理接口均验证通过，验证会话已退出；完整浏览器与 Gateway 联调仍待继续。

## 常见问题

运行和接口排错日志见[运行与接口日志](logging.md)：本机查看控制台，Linux systemd 部署后用 journald 查询。生产输出单行 JSON，包含请求摘要与 TraceId；持久化、容量及网页查看方式由部署环境配置。

| 现象                              | 检查方向                                                                         |
| --------------------------------- | -------------------------------------------------------------------------------- |
| 提示未配置 IdentityDatabase       | 给 Identity.Api 设置 User Secrets；确认当前环境是 Development，或注入环境变量    |
| 提示 SigningKey 缺失 / 非法        | 开发确认使用 Development 环境且旧 User Secrets / 环境变量没有覆盖为无效值；生产按认证文档注入独立密钥 |
| Oracle 连接失败                  | 容器是否 healthy、1521 端口、FREEPDB1 服务名、schema 账号权限和 User Secrets |
| 认证返回 503 / 600503              | Redis 服务、地址、端口、ACL、网络；恢复后重试下线，确认先前数据库写入结果         |
| 模型与数据库不一致                | 查看待应用 Migration，按数据库专题更新；不屏蔽模型变化警告                       |
| Gateway 无法连接 Identity         | Identity 是否已启动、YARP 目标是否匹配实际端口、HTTPS 证书是否受信任             |
| 前端请求落到 HTML / 404           | 核对完整前缀 `/api/identity/api/...` 和代理规则，业务接口不能进入 SPA 回退       |
| 短时间请求返回 429                | 当前频率额度耗尽，随窗口推进恢复；查询参数变化不会获得新额度                     |
| 多个客户端共享限流额度            | 核对可信代理 IP 转发；同一出口 IP 共享额度本身是当前规则                         |
| 调试器在 `throw` 处暂停           | 继续执行后才进入异常处理器，不代表统一错误处理失效                               |
| 浏览器能访问 GitHub，Git 连接失败 | 检查 Git 使用的代理和命令行网络；`443` 连接错误通常与仓库文件大小无关            |

## 部署边界

两个 Web 项目均有 Dockerfile，另有仅供开发的 Redis Compose 及 Redis 健康检查；尚未建立完整平台 Compose、CI/CD、应用健康检查或生产部署流程。当前 Identity Dockerfile 在首次 restore 前只复制 Api 的项目文件，没有复制其引用项目，因此容器构建需要进一步调整，不能将现有文件当作已验收的部署方案。

上线前需要按实际环境确定连接信息、HTTPS、代理地址、服务发现 / 目标地址，并落实[journald 日志配置与验收](logging.md)。后续工作见[规划](roadmap.md)。
