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

Redis 复用现有正式实例，每位成员必须使用自己的开发前缀，与线上及其他成员区分。同一成员的本机实例使用同一前缀即可。

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
| 本机修改后页面仍在请求其他环境 | 前端开发代理是否指向本机 Gateway；修改前端环境配置后是否重启开发服务 |
