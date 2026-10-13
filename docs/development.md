# 开发环境、命令与排错

[项目首页](../README.md) · [文档导航](README.md)

以下命令在后端仓库根目录执行，示例使用 PowerShell。Visual Studio 也可以打开 `ZX.Platform.slnx`，将 Identity.Api 和 Gateway 设置为多个启动项目。

## 环境要求

| 依赖                    | 当前用途                                          |
| ----------------------- | ------------------------------------------------- |
| .NET 10 SDK             | 项目均以 `net10.0` 为目标，编译与运行 Web API     |
| SQL Server              | Identity 数据库；已有开发环境使用 SQL Server 2022 |
| Visual Studio / VS Code | 编辑和调试，选择支持 .NET 10 的版本               |
| Docker / WSL            | 可选，用于本机 SQL Server 等基础设施              |
| Git                     | 后端独立仓库版本管理                              |

YARP、EF Core、Swashbuckle 和 BCrypt 的版本以各 `.csproj` 为准。仓库没有 `global.json` 或本地工具清单；更换 SDK / 工具时应保持与项目目标兼容。

## 数据库配置

Identity 必须读取到 `ConnectionStrings:IdentityDatabase`，否则 [Program.cs](../Services/Identity/ZX.Platform.Identity.Api/Program.cs) 会在启动时抛出配置缺失异常。Gateway 不连接数据库。

开发环境使用已经在 Api `.csproj` 中配置的 User Secrets：

```powershell
dotnet user-secrets set "ConnectionStrings:IdentityDatabase" "Server=127.0.0.1,14330;Database=ZX_PLATFORM_IDENTITY;User Id=<数据库账号>;Password=<数据库密码>;TrustServerCertificate=True" --project Services/Identity/ZX.Platform.Identity.Api
```

这里的 `14330` 来自既有 Docker 开发环境，SQL Server 容器内部端口是 `1433`，不是所有机器都必须使用这个端口。按自己的实例替换主机、端口、数据库名和凭据。

User Secrets 只用于本地开发，保存在项目之外，不是生产密钥管理方案。生产或容器环境可注入 `ConnectionStrings__IdentityDatabase` 环境变量。不要把真实连接字符串、密码或证书写入文档和提交文件。

`appsettings.json` / `appsettings.Development.json` 维护可共享设置；`appsettings.Local.json` 虽被 Git 忽略，但当前程序没有自动加载这种文件，不能只创建它就期待生效。初始化和更新数据库见[迁移命令](database.md#迁移命令)。

接口时间字段统一以 UTC 的 ISO 8601（带 `Z` / `+00:00`）传输，数据库也按 UTC 存储；前端或其他调用方在显示时再按业务时区格式化。后续定时任务应保存本地时间和明确的业务时区。约定见[时间字段格式](http.md#时间字段格式)。

## 本地启动

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

开发入口：

- Identity Swagger：`https://localhost:7075/swagger`，JSON 为 `/swagger/v1/swagger.json`。
- Gateway OpenAPI：`https://localhost:7178/openapi/v1.json`，描述 Gateway 自身接口，不会聚合 Identity Swagger。
- Gateway 业务路径：`https://localhost:7178/api/identity/api/Users/getUserList?page=1&pageSize=20`。

## Gateway 与前端联调

当前 [Gateway/appsettings.json](../ZX.Platform.Gateway/appsettings.json) 匹配 `/api/identity/{**catch-all}`，移除 `/api/identity` 后转发到 `https://localhost:7075/`。

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

当前解决方案没有测试项目或 CI 验收流程。构建成功只说明编译通过；真实数据库、业务状态、代理部署和容量仍需单独验收。接口和限流的检查场景分别见[HTTP 验收](http.md#验收要点)与[限流验收](rate-limiting.md#人工验收)。

## Git 与本机文件

[.gitignore](../.gitignore) 排除 `bin/obj`、IDE 缓存、`*.user`、`.tmp`、日志、测试产物、本地配置和私钥证书。源码、`.slnx`、`.csproj`、Migration、Dockerfile、文档和共享 appsettings 都提交。

`.gitignore` 只决定未跟踪文件，不会自动移除已经提交过的文件；首次提交后可用 `git ls-files -ci --exclude-standard` 核对有无已跟踪的忽略项。工作目录的总大小包含编译产物，不等于实际 Git 上传大小。

浏览器代理与 Git 代理配置不同。需要代理时按自己的端口设置，只限当前仓库和 GitHub，例如：

```powershell
git config --local http.https://github.com.proxy http://127.0.0.1:<代理端口>
```

代理写入本机 `.git/config`，不进入提交；不再使用代理时可通过 `git config --local --unset http.https://github.com.proxy` 清除。

## 常见问题

| 现象                              | 检查方向                                                                         |
| --------------------------------- | -------------------------------------------------------------------------------- |
| 提示未配置 IdentityDatabase       | 给 Identity.Api 设置 User Secrets；确认当前环境是 Development，或注入环境变量    |
| SQL 连接失败                      | SQL Server 是否运行、端口映射、账号权限和数据库配置；不要仅检查 Swagger 是否打开 |
| 模型与数据库不一致                | 查看待应用 Migration，按数据库专题更新；不屏蔽模型变化警告                       |
| Gateway 无法连接 Identity         | Identity 是否已启动、YARP 目标是否匹配实际端口、HTTPS 证书是否受信任             |
| 前端请求落到 HTML / 404           | 核对完整前缀 `/api/identity/api/...` 和代理规则，业务接口不能进入 SPA 回退       |
| 短时间请求返回 429                | 当前频率额度耗尽，随窗口推进恢复；查询参数变化不会获得新额度                     |
| 多个客户端共享限流额度            | 核对可信代理 IP 转发；同一出口 IP 共享额度本身是当前规则                         |
| 调试器在 `throw` 处暂停           | 继续执行后才进入异常处理器，不代表统一错误处理失效                               |
| 浏览器能访问 GitHub，Git 连接失败 | 检查 Git 使用的代理和命令行网络；`443` 连接错误通常与仓库文件大小无关            |

## 部署边界

两个 Web 项目均有 Dockerfile，但尚未建立 Docker Compose、CI/CD、健康检查或生产部署流程。当前 Identity Dockerfile 在首次 restore 前只复制 Api 的项目文件，没有复制其引用项目，因此容器构建需要进一步调整，不能将现有文件当作已验收的部署方案。

上线前需要按实际环境确定连接信息、HTTPS、代理地址、服务发现 / 目标地址、日志和验收流程。后续工作见[规划](roadmap.md)。
