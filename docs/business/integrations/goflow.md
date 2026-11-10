# GoFlow 集成：免登录进入首页

## 当前范围

MOM 与 GoFlow 的前后端均已加入定向 SSO 对接代码。用户从 MOM 个人门户点击“进入 GoFlow”，复用当前 MOM OA 登录，自动进入 GoFlow 首页 `/pms/workboard`。不显示授权确认页，仅在导航期间显示简短等待状态，失败时提供重试和返回入口。

本次不读取 GoFlow 业务库、不定位项目或阶段，不新增数据库表或 EF Migration。GoFlow 仍使用自己的账号、菜单和权限，不自动注册或授予角色；原有 OA 密码登录保留。本对接不提供完整 OIDC discovery、统一登出或会话实时同步。

代码已接通不代表线上免登录已验证。两边前后端均需部署，配置匹配的客户端信息、账号和传输模式后再启用（默认 HTTPS，当前内网 HTTP 需两边显式开启 AllowHttp）。配置见 [配置与启动](../../01-start.md#可选goflow-免登录)，GoFlow 配置见其仓库根目录 `MOM-SSO.md`。

## 用户、权限与账号对应

- MOM 入口要求当前用户为已审批、启用、未删除的 OA 用户，并具有 `integration.goflow.open`（“免登录进入 GoFlow”）。启动沿用现有权限目录登记，负责人手工授予角色；本地 Administrator 不能使用此入口。
- 当前工号、LDAP provider 和目录唯一标识必须完整。身份从当前有效 MOM 会话和服务端用户记录读取，签发请求不接受工号、用户 ID、角色或管理员标记。
- GoFlow 核对兑换结果的 `clientId`、`directoryProvider`、工号及首页目标。`directoryProvider` 必须与部署时配置的 MOM 目录别名一致。
- GoFlow 的 `SysUser.Account` 必须与 MOM 的 `employeeNo` 字符串完全相同，保留前导零。只允许唯一、启用且认证类型为 LDAP 的已有账号；不使用姓名、显示名称或模糊匹配。上线前必须核对实际账号是否符合这条约定，不符合时补充明确映射再开放。
- GoFlow 登录后沿用其已有角色和菜单；MOM 的管理员和 SBU 不变成 GoFlow 授权。此身份流程不按 SBU 分发，未向 AI 工具开放。

## 浏览器与服务端流程

1. MOM 门户从认证后的配置接口读取 GoFlow 公开入口。用户点击后进入 GoFlow 前端 `/sso/mom`，该页面自动导航同源 `/api/auth/sso/mom/start`。
2. GoFlow 服务端生成独立随机 state、PKCE verifier 和浏览器绑定，缓存登录事务 5 分钟；默认绑定写入 Secure、HttpOnly、SameSite=Lax 的 `__Host-GoFlowMomSso` Cookie；显式 HTTP 模式使用无 Secure 的 `GoFlowMomSsoHttp`，仍保留 HttpOnly、SameSite=Lax 和浏览器绑定。浏览器跳到固定的 MOM `/sso/goflow/authorize`，仅携带 state、S256 challenge 和固定回调。
3. MOM 页面通过现有 auth store 恢复会话；未登录时先走原 MOM 登录，随后继续。页面检查权限及固定回调，自动请求票据，再导航服务端返回的固定 GoFlow 回调，不增加用户确认动作。
4. GoFlow 回调检查 Cookie/state 并一次性消费事务，后端用 Basic 服务凭据和对应 verifier 兑换票据。MOM 原子核对 client/回调/challenge 后消费票据，再复查原用户、权限、安全版本和会话。
5. GoFlow 不在回调 URL 放 JWT，而是生成浏览器绑定的 60 秒交接码，以 fragment 跳到 `/sso/mom/complete`。前端立即清除 fragment，再向同源 complete 接口提交交接码；后端要求匹配的 Origin、`X-GoFlow-SSO: 1` 和绑定 Cookie。
6. 完成交接后，GoFlow 匹配已有 OA 账号，使用原登录机制签发自己的 Token。前端加载本系统用户、菜单和字典，整页进入固定首页，重建动态路由和页面缓存。已有不同账号会清除本地旧身份，不退回旧账号页面。

票据、state、verifier 和交接码每次由密码学随机数重新生成，随机值使用 32 字节 Base64URL。MOM 票据默认有效 60 秒；GoFlow 登录事务和交接码只在正确浏览器绑定下消费。网络或初始化失败时重新发起整次登录，不重试已消费的凭据。

## MOM API

Identity 直连前缀 `/api/sso/goflow`；经 Gateway 完整前缀 `/api/identity/api/sso/goflow`。前端请求层已有 `/api`，方法中使用 `/identity/api/sso/goflow`。

| 动作 | 认证与输入 | 返回 |
| --- | --- | --- |
| `GET configuration` | 当前 MOM 用户登录 | `enabled`、`allowHttp`、公开的 `startUrl`、`callbackUri`；关闭时两个 URL 为 null，不返回服务密钥 |
| `POST tickets` | 当前 MOM Bearer、`integration.goflow.open`、`X-Identity-CSRF: 1` | `redirectUrl`、`expiresAtUtc` |
| `POST redeem` | 独立 GoFlowClient Basic 服务认证，仅供 GoFlow 后端调用 | `clientId`、`momUserId`、`employeeNo`、`directoryProvider`、`directorySubject`、`displayName`、`target` |

成功沿用 `ApiResponse<T>`，`code=0`。tickets 和 redeem 默认仅允许 HTTPS，配置 `SingleSignOn:GoFlow:AllowHttp=true` 后允许当前内网 HTTP 联调。反向代理仅从受信任代理接收 `X-Forwarded-Proto`。

签发 JSON：

```json
{
  "redirectUri": "https://goflow-staging.example.invalid/api/auth/sso/mom/callback",
  "state": "本次 GoFlow state",
  "codeChallenge": "本次 S256 challenge",
  "target": { "page": "home" }
}
```

`redirectUri` 必须与服务端配置逐字符一致，最多 2048 字符；state 为 32～128 位 Base64URL；challenge 为 43 位 Base64URL；目标只接受 `home`，不接受任意 returnUrl 或项目定位参数。

兑换头为 `Authorization: Basic BASE64(UTF8(clientId + ':' + clientSecret))`，JSON 包含 `ticket`（43 位 Base64URL）、`codeVerifier`（43～128 位 RFC 7636 unreserved 字符）、`redirectUri`。服务密钥仅在两边后端受保护配置中保存，不能进入前端 `VITE_*`、URL 或仓库。

## 拒绝与失效

| 场景 | MOM HTTP / 业务码 |
| --- | --- |
| 参数、回调、票据或 PKCE 不正确、已使用或过期 | 400 / 600001 |
| MOM 会话无效或服务凭据错误 | 401 / 600401 |
| 未开启 AllowHttp 的 HTTP、缺少 CSRF、非 OA、未审批、停用或缺少权限 | 403 / 600005 |
| 现有限流命中 | 429 / 600429 |
| 功能未启用、Redis 或依赖不可用 | 503 / 600503 |

默认全局限流仍是同 IP、同接口 3 秒 5 次。GoFlow 兑换共用后端出口 IP，集中使用时需评估容量；本次未修改该策略。

原 MOM 用户退出、被接管或强制下线、权限撤销及安全版本变化后，尚未兑换的旧票据不能使用。已建立的 GoFlow 会话按 GoFlow 自身机制管理，MOM 退出不会自动注销 GoFlow。

## 缓存及日志

MOM 键为 `AuthenticationRedis:KeyPrefix + sso:goflow:ticket: + SHA256(ticket)`；Lua 原子比较 client、回调和 challenge 后删除，错误 verifier 不消费合法票据。只保存必要会话绑定与 challenge，不保存原始票据、密码或 Token；Redis 失败不回退内存。

GoFlow 使用原 `ICacheService` 保存登录事务和交接码，哈希化键和浏览器绑定，并通过锁保护一次性消费。单实例可使用 Memory；多实例必须共用 Redis 和环境专属 `MomSso:CachePrefix`。

SSO 响应设为 no-store，回调设置 no-referrer；GoFlow 的报文日志中间件按 SSO 路径提前排除。部署时代理 access log 也不得保存该路径的查询串、Cookie、Authorization 或请求/响应正文；代理和后端不能将完整回调 URL 写入诊断日志。

## 当前内网 HTTP 模式

MOM 配置 `SingleSignOn__GoFlow__AllowHttp=true`，GoFlow 配置 `MomSso__AllowHttp=true`；StartUri/CallbackUri/PublicOrigin 使用实际 `http://10.32.14.108:8002`，GoFlow 的 MOM 授权页和兑换接口使用实际 MOM HTTP 地址。开关默认关闭，其他身份、回调、PKCE、state 和一次性消费校验继续生效。部署模板见 GoFlow `MOM-SSO.md`。

HTTP 不提供传输加密，只用于本次批准的可信内网联调；后续改为 HTTPS 时关闭两个 AllowHttp。MOM 的普通登录、刷新 Cookie 和全局安全策略未因此修改。

## 验收

MOM 端本次按仓库约定仅静态检查，未运行 build、类型检查或真实服务调用。GoFlow 本地构建情况见交付说明；尚未进行线上端到端验证，未部署或修改共享账号、角色。

部署后依次验证：授权 OA 用户进入固定首页；已有不同 GoFlow 登录时切换到正确用户；GoFlow 不存在/停用/非 LDAP 账号拒绝；MOM 本地管理员/待审批/无权限拒绝；错误回调、state/Cookie、服务密钥及 PKCE 拒绝；过期和重复消费拒绝；MOM 退出/撤权后未兑换票据拒绝；两套环境票据不可互换；正常 GoFlow OA 登录仍可使用。
