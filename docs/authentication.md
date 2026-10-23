# 登录与会话

[文档导航](README.md) · [认证接口契约](api.md#认证接口) · [API 响应与错误](http.md)

后端已实现登录、刷新、退出、当前用户及自助改密码。本文维护会话、安全规则和配置；路由、请求字段及响应集中在[接口契约](api.md#认证接口)。ZX.front.vue 已接入登录页、接管、自动刷新、退出和改密，真实浏览器与 Gateway 联调仍待验收，不能视为已经通过端到端验证。

## 当前模式

Access Token 使用标准 JWT，由 `System.IdentityModel.Tokens.Jwt` 签发，ASP.NET Core `AddJwtBearer()` 验证签名、Issuer、Audience、有效期、HS256 算法及 `typ=at+jwt`，不自行解析和验证 Access Token。Refresh Token 继续使用密码学安全随机凭据。业务请求只接受 `Authorization: Bearer <accessToken>`，不接受 URL Token 或用 Refresh Cookie 代替业务请求认证。

数据库保存用户、BCrypt 密码哈希、账号状态及角色授权；Redis 保存当前会话与权限缓存。JWT 标准验证通过后，每个需要认证的请求查询 Redis 核对用户、SessionId 和当前 Access `jti`。功能权限检查再使用一次 MGET，缓存未命中才查询授权表，不逐请求 HTTP 调用 Identity。多个实例必须使用相同 JWT 配置和密钥、数据库、Redis、键前缀、Token 生命周期及权限目录版本。连接通过单例 `IConnectionMultiplexer` 复用，无跨请求本机缓存或内存降级。权限缓存规则见[角色与权限](authorization.md)。

存储接口为 `Application/Authentication/IAuthSessionStore`，实现为 `Infrastructure/Security/RedisAuthSessionStore`；`JwtAccessTokenIssuer` 负责调用标准库签发 JWT，API 的 `JwtSessionEvents.TokenValidated` 在 `JwtBearer` 校验成功后检查共享会话，并统一 401 / 403 响应。旧的 `SessionAuthenticationHandler`、随机 Access 格式、Access 哈希匹配已移除。JSON 会话使用新键名，与旧 `zx:identity:auth:user:{userId}` Hash 隔离，避免 Redis 类型冲突。旧随机 Token 及旧 Hash 会话均不兼容，更新后需重新登录；旧键按原 TTL 到期，不需要清空 Redis。多实例更新时需统一切换版本，不能让仍认可旧 Hash 会话的实例继续提供服务。

每个用户仅一个 Redis String：`zx:identity:auth:session:{userId}`（userId 为不带连字符的 GUID），值为 `StoredSession` 对象序列化后的 JSON，直接使用属性名：`SessionId`、`Account`、`AccessId`（JWT 的 `jti`）、`RefreshHash`（SHA-256）、`AccessExpiresAtUtc`、`ExpiresAtUtc`、`CredentialStamp` 和 `UsedRefreshHashes`。时间采用带 UTC 偏移的 JSON 日期字符串。密码指纹为 BCrypt 哈希的 SHA-256，用于刷新时识别并发改密留下的旧会话；不保存明文密码或完整 Token。已消费 Refresh 哈希作为数组随会话一起读写，每次刷新追加一项；整条会话按原绝对期限设置过期时间，到期自动清理全部重放记录，无会话数据库表。

JWT 包含 `sub`（用户 ID）、`name`（账号）、`sid`（会话 ID）、`jti`（Access 唯一编号）、`iat/nbf/exp/iss/aud`；Claims 不放密码、Refresh 或其他秘密，JWT 签名不提供内容加密。关闭默认 Claim 映射，后端通过 `sub` 读取用户 ID，`User.Identity.Name` 对应 `name`。Refresh 由用户 GUID、会话 GUID 和 32 字节随机秘密组成，GUID 只定位 Redis 键，仍须匹配完整 Refresh 的哈希；调用方不解析 Refresh。

## 生命周期

- Access 默认 15 分钟；Refresh 默认 7 天，以最初登录时间计算绝对期限，刷新不会延长这 7 天。不提供“记住登录”选项。
- 同账号只允许一处有效会话。登录首先验证账号状态和密码；查询到已有未过期会话时返回接管确认，原会话保持有效。用户确认后再次提交账号、密码和 `replaceExistingSession=true`，替换旧会话。接受两个登录请求同时查到尚未登录、随后都返回成功的窗口：以最后写入 Redis 的会话为准，之前的凭据失效。未经正确密码校验不能踢掉他人。
- 关闭浏览器不视为退出；Access 已过期但 Refresh 尚有效时仍需确认接管。这里的“在线”表示会话有效，不表示浏览器当前打开或有心跳。
- 每次刷新原子消费旧 Refresh 并生成新的 Access / Refresh，原 Access 也立即失效。
- 保留已消费 Refresh 的哈希到会话绝对过期；重放旧 Refresh 撤销该会话全部凭据。接管后旧 SessionId 的刷新、重放或退出不能撤销新会话。
- 退出撤销当前会话并删除 Cookie。Access 过期仍可用 Refresh Cookie 退出，重复退出返回成功。无 Cookie 的接口工具也可用有效 Bearer 退出。
- Refresh 响应丢失后不要自动重试旧凭据；旧凭据已被消费，重试会触发撤销，应重新登录。前端已合并单标签并发刷新，并用浏览器 Web Locks 协调同源标签页，不能绕过认证 store 各自用同一个 Cookie 同时刷新。前提是现代浏览器与 HTTPS 或本机安全上下文。

## 禁用、删除和改密码

管理员可强制下线，直接删除该用户当前会话；已经离线也返回成功，不要求先查在线。下线不禁用账号，用户随后可以重新登录。当前未实现在线用户列表或浏览器主动推送退出，客户端在下一次请求收到 401 后处理。

禁用、单个 / 批量软删除、自助改密码均先保存数据库状态，再在 `finally` 中撤销会话。不需要管理员手工先下线；管理员重置密码接口已移除，修改密码只通过本人验证旧密码的入口。启用或恢复不会复活已撤销的会话。

**明确接受并发窗口，不使用 `SemaphoreSlim` 或 Redis 分布式用户锁。** Redis 操作使用 C# 和 StackExchange.Redis，不编写 Lua。查询一次 `GET` 后反序列化 JSON，在 C# 中判断。创建/接管用一次 `SET` 覆盖完整 JSON 并同时设置绝对过期时间，无需先删除或使用事务，以最后写入者为准。刷新先核对会话和 Refresh 哈希，再通过条件事务 `Condition.StringEqual` 确认 Redis 中的完整 JSON 仍与读取时一致，才写入新 JSON，保持最初的绝对过期时间。退出先核对会话编号，再用相同条件删除，旧设备不能删除新登录。撤销遇到条件不符或 WATCH 冲突等明确未提交的结果时，重新读取，最多尝试 3 次，仍冲突则返回 503；Redis 超时等执行结果不确定的故障不自动重试。

上述事务只覆盖 Redis，数据库保存与 Redis 撤销不构成一个事务。一般情况下撤销完成后新开始的认证拒绝旧 Token，已经通过认证的请求不会自动中断。若登录已读到旧账号状态、但在撤销之后才写入会话，它仍可能短暂成功；强制下线与新登录同时发生时，以 Redis 实际执行顺序为准。

没有逐请求查用户表，因此上述竞争产生的会话不会被“下一次请求”自动发现。Access 最长可继续有效一个 Access 生命周期（默认 15 分钟）；刷新会重新检查数据库的删除、禁用状态和密码指纹，拒绝旧会话继续刷新。需要缩短容忍时间可调整 Access 有效期；未来若重新要求严格立即失效，应重新设计协调，不能把当前实现当作强一致。

撤销不使用客户端请求取消令牌，即使提交结果不确定也尝试下线。Redis 失败返回 503，数据库变更可能已经提交，旧会话在 Redis 恢复后也可能仍存在；调用方需查询状态、恢复依赖后重试下线。系统不会自动回滚数据库或假报成功。直接改数据库同样不触发撤销，运维应使用对应接口。

## 登录失败与密码

账号 Trim 后按大写标准化查询，密码不 Trim。密码用 BCrypt 成本因子 12；新密码至少 8 个字符，所有密码入口最多 72 个 UTF-8 字节，超限返回本地化 `400 / 600001`。72 字节不等于 72 个中文字符。

默认连续 5 次密码错误锁定 15 分钟。锁定期间拒绝登录；到期后的下一次失败从 1 重新计数，正确密码登录或改密码清除计数及锁定（接管确认前也已验证正确密码）。锁定只限制新登录，不撤销已有会话。登录失败统一提示，不区分账号不存在、禁用、删除、锁定或密码错误；不存在账号也执行 BCrypt 校验以减少明显耗时差异。

登录、刷新等接口沿用按 IP + 方法 + 路由模板的[滑动窗口限频](rate-limiting.md)，默认每实例 3 秒最多 5 次。账号失败计数跨 IP 保存在数据库；当前读改写未对并发失败计数做原子累加，多实例下不能将其当作严格集群限额，两者职责不同。

## Cookie 与 CSRF

Refresh 使用 `zx_identity_refresh` Cookie，`HttpOnly`、`SameSite=Strict`、`Path=/api`、不设置 Domain，默认 `Secure=true`。Path 同时覆盖 Identity 直连与现有 Gateway 前缀；更改统一入口前缀时同步检查。

Auth 的 POST / PUT 均要求 `X-Identity-CSRF: 1`。这是非简单请求头，结合 SameSite 和当前不开放跨域凭据 CORS 的设置，防止其他站点借浏览器 Cookie 操作会话。HttpOnly 只防止脚本读取 Cookie，不能单独替代 CSRF 防护，也不能防止 XSS 代发请求。

认证响应使用 `Cache-Control: no-store`，Refresh 不出现在 JSON。部署使用 HTTPS；开发建议通过 Identity HTTPS Swagger 或 HTTPS 入口验证。HTTP Vite 联调若浏览器拒收 Secure Cookie，可仅在 Development 用 User Secrets 设置 `AuthenticationCookie:Secure=false`。非 Development 配置为 false 会启动失败，不依赖浏览器对 localhost 的特殊行为。

## 配置与管理员

共享默认配置见 Api `appsettings.json`：

| 配置 | 默认 | 范围 / 用途 |
| --- | --- | --- |
| `Authentication:AccessTokenMinutes` | 15 | 1～60 分钟 |
| `Authentication:RefreshTokenDays` | 7 | 1～30 天，绝对有效期 |
| `Authentication:MaximumFailedAttempts` | 5 | 1～20 次 |
| `Authentication:LockoutMinutes` | 15 | 1～1440 分钟 |
| `Authentication:AdministratorAccounts` | `[]` | 仅首次引导内置管理员，指定已有账号、忽略大小写 |
| `AuthenticationCookie:Secure` | true | 仅 Development 可设 false |
| `ConnectionStrings:IdentityRedis` | `127.0.0.1:6379,connectTimeout=3000,asyncTimeout=3000` | Redis 地址，生产凭据外部注入 |
| `AuthenticationRedis:KeyPrefix` | `zx:identity:auth:` | 相同环境所有实例一致，不同环境必须隔离 |
| `AuthenticationJwt:Issuer` | `ZX.Platform.Identity` | JWT 签发方，必须精确匹配 |
| `AuthenticationJwt:Audience` | `ZX.Platform` | JWT 接收方，必须精确匹配 |
| `AuthenticationJwt:SigningKey` | 仅 Development 提供共享开发值 | Base64 编码的至少 32 字节密钥，生产必须通过安全配置注入独立随机值 |

开发环境通过 `appsettings.Development.json` 提供固定的共享开发密钥，拉取代码后不需要每位开发者生成或迁移 JWT 密钥。该值随仓库公开，只能用于开发；基础 `appsettings.json` 不提供生产默认值。User Secrets 和环境变量仍可覆盖，之前已配置的有效 User Secret 无需删除；同一环境中共享 Redis 会话的实例须采用同一把密钥。

生产通过 `AuthenticationJwt__SigningKey` 环境变量或部署的密钥管理配置注入独立随机密钥，不使用开发密钥、不随重启重新生成。需要在本机覆盖开发值时，可在仓库根目录的 Windows PowerShell 中执行以下命令；生产密钥用相同的随机生成方式，保存在生产配置中，不保存到开发 User Secrets，也不提交仓库：

```powershell
$identityJwtBytes = New-Object byte[] 32
$identityJwtRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$identityJwtRng.GetBytes($identityJwtBytes)
$identityJwtRng.Dispose()
$identityJwtKey = [Convert]::ToBase64String($identityJwtBytes)
dotnet user-secrets set "AuthenticationJwt:SigningKey" $identityJwtKey --project Services/Identity/ZX.Platform.Identity.Api | Out-Null
Remove-Variable identityJwtBytes, identityJwtRng, identityJwtKey
```

自定义密钥只在首次配置或计划更换时生成。缺少密钥、Base64 无效或长度不足时启动失败；非 Development 不自动加载开发配置文件，因此未注入密钥时仍拒绝启动。更换密钥会使原 JWT 无法验证；当前没有多密钥轮换机制，已有有效 Refresh 可按会话规则换取新 JWT。各实例使用一致的 Issuer、Audience 和密钥、不同环境隔离 Redis 会话，ClockSkew 为零，需同步系统时间。HS256 为共享签名密钥，持有它的服务也能签发 Token，未来向独立信任边界扩展时应采用非对称签名及公钥验证。

角色与功能权限已接入，管理接口使用各自 Policy。普通用户仍可查看自己的当前用户信息、退出、自助改密码和修改自己的显示名称；修改他人名称需要相应权限。没有公开注册接口，不自动创建默认账号或密码。

首次引导时，先应用角色迁移，再选择一个已存在、启用且知道密码的账号，例如实际账号是 `ZX001`：

```powershell
dotnet user-secrets set "Authentication:AdministratorAccounts:0" "ZX001" --project Services/Identity/ZX.Platform.Identity.Api
```

环境变量对应 `Authentication__AdministratorAccounts__0`。仅当数据库还没有 `ADMINISTRATOR` 角色时，启动使用名单创建该角色并绑定用户。角色存在后配置变更不再授予或撤销权限，后续在用户页分配角色；清空配置也不会删除已有管理员。空名单不自动授权；全新空库的首个账号仍需受控初始化。完整步骤见[首次管理员初始化](authorization.md#首次管理员初始化)。

## Redis 启动与故障

本机有 Docker 时，在仓库根目录运行：

```powershell
docker compose -f compose.redis.yml up -d
docker compose -f compose.redis.yml exec redis redis-cli ping
```

[compose.redis.yml](../compose.redis.yml) 仅启动开发 Redis 7.4：端口只绑定 `127.0.0.1:6379`，数据卷保存 `/data`，AOF 每秒刷盘，`noeviction` 避免内存淘汰会话/重放记录。`docker compose ... down` 保留数据卷；加 `-v` 会删除数据，所有会话丢失。它不是生产部署配置，不把无密码 Redis 暴露到公网。

远程或有密码的 Redis 通过 User Secrets 配置，生产用 `ConnectionStrings__IdentityRedis` 等安全配置：

```powershell
dotnet user-secrets set "ConnectionStrings:IdentityRedis" "<host>:6379,password=<password>,connectTimeout=3000,asyncTimeout=3000" --project Services/Identity/ZX.Platform.Identity.Api
```

Redis 不可用时依赖会话的操作返回本地化 `503 / 600503`，不退回内存、不按未登录返回 401、不删除 Refresh Cookie。连接配置使用 `BacklogPolicy.FailFast`，避免恢复连接后补执行积压的旧认证操作。这里的 Redis 超时仅限制依赖调用，不是所有业务接口的统一执行时限。已发出的命令超时后可能已执行，不能无条件自动重试登录或刷新。

应用重启而 Redis 会话还在时可以继续使用。Redis 数据全部丢失则所有用户需重新登录；AOF 每秒刷盘也可能丢失最近写入，包括会话撤销，恢复后不能承诺严格立即失效。生产还需监控、备份和高可用方案。各节点应同步系统时间。

## 验证与后续

先启动独立测试 Redis，再运行：

```powershell
$env:IDENTITY_TEST_REDIS = "127.0.0.1:6379"
dotnet run --project Tests/ZX.Platform.Identity.AuthChecks --no-launch-profile
```

检查使用真实 Redis、两个独立 Redis 连接、真实 BCrypt、替代用户仓储和临时 HTTP 服务。覆盖 JSON String 存储、创建及刷新后的绝对过期时间、旧 Hash 会话迁移、JWT 签名篡改、错误 Issuer / Audience / 密钥 / 类型、无签名、有效期、会话 Claims、匿名刷新携带过期 Access、跨实例接管/下线、原子轮换与多次刷新后的重放、锁定、旧退出不影响新登录、503 故障、Cookie、CSRF、权限、多语言及接受的竞争边界。每组使用随机 `zx:identity:checks:` 前缀并只清理自己的键，不清库；仍建议使用独立测试 Redis。默认不连接业务数据库；显式设置 IDENTITY_TEST_ORACLE 后会额外执行可回滚的 Oracle 集成检查，见[数据库专题](database.md#可选-oracle-集成检查)。这些检查不替代 Gateway / 浏览器部署验收。新增角色授权检查还覆盖模型与迁移快照、权限并集、Redis 版本失效和内置管理员保护，HTTP 检查覆盖旧 JWT 在授权/撤权后分别通过/拒绝。手工示例见 Api `Authorization.http` 及认证 `.http` 文件。

各业务服务仍需独立认证并检查自己的功能权限与厂区数据范围，不能只相信 Gateway，也不能将 `UserFactory` 或登录成功视为所有业务授权。Identity 的角色权限已接入，本次尚未执行新增授权的真实数据库和浏览器验收，清单见[角色与权限](authorization.md#验证与待验收)。新增业务服务时提取公共认证/授权组件，不逐请求 HTTP 调 Identity。跨服务组件、首账号初始化及审计用户上下文属于后续工作。

前端登录通过同源 Gateway 代理，Access 保存在 localStorage，Refresh 仅在 HttpOnly Cookie；HTTP 本机开发按上文调整 Development Cookie 配置。前端拼图插件目前只提供拖动交互，后端未验证挑战，不能将其当作服务端防机器人能力。前端认证说明及手工验收清单见 ZX.front.vue 的 `docs/authentication.md`。
