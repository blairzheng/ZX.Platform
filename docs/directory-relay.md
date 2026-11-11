# 本机通过远程接口访问 LDAP

[本机配置与启动](01-start.md) · [OA 人员同步配置](business/platform/oa-personnel-configuration.md)

本机无法直连 LDAP 时，由现有 Identity 服务提供两个目录接口。本机在 `Development` 环境启用远程调用，线上登录和人员同步继续直接访问 LDAP。两个接口随同一份后端包发布到 177、178，通过现有网关访问，无需单独部署服务或新增 GitLab 变量。

```text
本机前端 → 本机后端 → 服务器目录接口 → LDAP
```

服务器只执行目录认证和人员读取；账号匹配、建档、审批、会话与同步入库继续由本机原用例处理，使用本机已有的数据库和 Redis 配置。本机仍使用测试库 schema 和个人 `AuthenticationRedis:KeyPrefix`。

## 服务器配置

服务器保留现有 `AuthenticationLdap` 和 `OaPersonnelSync` 配置，`DirectoryRelay:AccessKey` 使用独立随机密钥，两台 Identity 节点使用同一值。按当前内网联调约定，已在服务器 `appsettings.json` 和本机 `appsettings.Development.json` 配置同一 256 位随机密钥，随本次后端包发布，不需要新增 GitLab 变量。以下仅说明配置结构，不展示实际密钥。

```json
{
  "DirectoryRelay": {
    "AccessKey": "REPLACE_WITH_INDEPENDENT_RANDOM_SECRET"
  }
}
```

密钥为 32～256 位 ASCII 可打印、非空白随机字符，不复用 OA 密码、LDAP 服务账号密码或 JWT 密钥。密钥为空时目录接口拒绝调用，原有登录和同步接口不受影响。无需额外配置服务端开关。

认证接口沿用 `AuthenticationLdap:Enabled`，人员接口沿用 `OaPersonnelSync:Enabled` 及只读服务账号。LDAP 节点、BaseDn、属性、Provider 和 BU 范围全部由服务器现有配置决定，请求不能覆盖。

## 本机开发配置

Identity.Api 的 `appsettings.Development.json` 已启用远程调用并配置 177 地址和与服务器一致的密钥，保留已有数据库、开发 JWT 和 Redis 配置。下面展示当前结构，密钥以占位值表示。

```json
{
  "DirectoryRelay": {
    "Enabled": true,
    "BaseUrl": "http://10.32.15.177:18080/api/identity/",
    "AccessKey": "REPLACE_WITH_SAME_INDEPENDENT_RANDOM_SECRET",
    "TimeoutSeconds": 110
  },
  "AuthenticationLdap": {
    "Enabled": true
  },
  "OaPersonnelSync": {
    "Enabled": true,
    "BusinessUnit": "BU17"
  }
}
```

只测试哪项能力，就启用对应原有开关。双方 Provider、BU 保持一致；本机无需同步服务账号密码、LDAP 直连节点或 `AuthenticationLdap:AllowPlainText=true`。远程调用仅在 `Development` 且 `DirectoryRelay:Enabled=true` 时生效，其他环境始终使用原有直连实现。

`BaseUrl` 填固定 Identity 基地址；经过现有网关时保留 `/api/identity/` 前缀。上述配置对应认证地址 `http://10.32.15.177:18080/api/identity/api/DirectoryRelay/authenticate`；直连 Identity 时基地址使用 `https://identity.example.invalid/`。地址不含用户名密码、查询参数或 fragment。当前 177 地址根据部署端口和网关路由配置确定，尚未通过实际请求验证。

支持 HTTP(S)，优先使用 HTTPS 并保留证书校验。HTTP 会明文传输 OA 密码、访问密钥和人员资料，只在批准的可信内网使用。客户端不跟随重定向，不自动重试密码认证。

`TimeoutSeconds` 默认 110、范围 1～180 秒，作为远程请求上限。实际登录预算取该上限与 `AuthenticationLdap.TotalTimeoutSeconds + 3` 秒的较小值；人员读取取该上限与 `OaPersonnelSync.TotalTimeoutSeconds + 5` 秒的较小值，默认分别为 18、95 秒，仍受调用取消和代理超时约束。

## 接口与验证

两个接口标注 `[AllowAnonymous]`，各自校验 `X-Directory-Key` 请求头，不使用 MOM 登录 Token。请求和响应沿用 `ApiResponse<T>`；返回结果禁止缓存，凭据、人员正文不写日志。

| 方法与路径 | 请求 | 成功 data |
| --- | --- | --- |
| `POST /api/DirectoryRelay/authenticate` | `EmployeeNo`、本人 OA `Password` | `LdapDirectoryUser` 目录身份 |
| `POST /api/DirectoryRelay/personnel` | 无业务参数 | 固定 `Provider`、`BusinessUnit` 和完整 `People` 列表 |

服务器认证保留现有 Redis 登录失败守卫，不建 MOM 用户、不写 Oracle、不签发 MOM Token。人员接口只读 LDAP，不执行服务器同步入库。本机继续校验目录来源、身份和人员完整性，并按原规则执行账号状态检查、去重与保存；网络或密钥错误不会当作员工密码错误。

当前已完成代码及 177 地址、两端一致密钥的配置，尚未发布服务器、编译或执行真实 LDAP 登录与同步。部署本次后端包后，需在目标环境验证入口、密钥、代理超时和实际调用。
