# OA / LDAP 登录实施方案

[文档导航](README.md) · [现有登录与会话](authentication.md) · [角色与权限](authorization.md) · [数据库迁移操作](migration-operations.md)

确认日期：2026-11-02；登录/业务授权规则更新于 2026-11-03（10.8 节）。状态：后端与前端已实现主要流程，当前进入联调，已知限制与本轮延期决定见 10.7 节，实施结果、验证范围和未联调项见第 10 节。文中产品规则、数据模型和接口契约仍是当前实现依据；LDAP 凭据验证结果只代表目录可达，不代表 ZX 已在目标环境完成全部联调。

## 1. 已确定的产品规则

### 1.1 两类独立账号

| 项目 | 本地管理账号 | OA 员工账号 |
| --- | --- | --- |
| 认证类型 | `LOCAL` | `LDAP` |
| 登录入口 | 系统登录 | OA 登录 |
| 用户输入 | 系统账号、本地密码 | 工号、OA 密码 |
| 密码验证 | ZX 校验 BCrypt 哈希 | LDAP 验证 OA 密码 |
| ZX 保存密码 | 仅保存 BCrypt 哈希 | 不保存 OA 密码，`PasswordHash = null` |
| 开通方式 | 有权限的管理员手工创建 | 管理员提前开通，或首次认证后创建待审批申请 |
| 授权来源 | ZX 本地角色、菜单、SBU | ZX 本地角色、菜单、SBU |

两类账号共用现有 `ZX_IDENTITY_USER`、角色关系、SBU 关系和会话基础设施。它们使用独立身份记录，不做自动绑定、合并、密码同步，也不允许本地密码失败后自动尝试 LDAP。

本地账号建议使用 `admin`、`opsadmin` 等名称；OA 账号使用工号。现有本地登录和新增用户 DTO 最多允许 8 个字符，推荐名称须满足这个限制。存量内部账号全部保留为 `LOCAL`，不自动改为 OA 账号，不自动增加管理员角色。

`AuthType` 决定认证来源；管理员能力仍由原来的管理员角色与保护规则决定，不能写成 `AuthType == LOCAL` 就拥有全部权限。

### 1.2 OA 的两条开通路径

**路径 A：管理员提前开通。**适用于已知工号的负责人、员工或具有公司 OA 身份的客户。用户不需要先登录、提交申请或提供 OA 密码。

1. 管理员在用户管理点击“预开通 OA 用户”，输入工号并点击“查询 LDAP”；后台通过服务账号查找用户，界面显示姓名、部门和目录账号状态供核对。此次查询不创建 ZX 用户。
2. 工号存在、目录身份完整且账号正常时，管理员选择角色和 SBU，点击“创建并开通”。后台重新查询并校验 LDAP 身份后，在同一数据库事务中生成 UserId、保存 OA 身份、分配角色 / SBU、记录审批通过并启用。
3. 用户第一次使用 OA 密码登录，核对到相同目录身份后，直接进入系统并使用预分配权限；不再申请、审批或生成第二条用户记录。

管理员有创建和授权能力时，一次提交就完成预开通，不要求先创建待审批用户再手工找到它点审批。创建、审批和授权的权限均在服务端检查；界面上一步完成不代表跳过审批记录或校验。也可以选择“仅创建待审批”，留给审核人后续处理。

**路径 B：用户首次登录申请。**仅适用于管理员尚未预开通的工号：

1. 员工选择“OA 登录”，输入工号和 OA 密码。
2. LDAP 认证成功后，ZX 查找对应的 OA 本地用户。
3. 工号不存在时，创建 `LDAP + Pending + IsEnabled=true` 用户，记录可信工号、目录唯一标识和姓名；没有密码、角色或 SBU。
4. 建立 Redis 会话，签发 Access / Refresh 并设置 HttpOnly Cookie，进入应用中心。提示“账号已创建，请联系管理员分配访问权限。”；不默认分配任何业务能力。
5. 再次登录且仍待业务审批时，沿用同一 UserId 和单会话接管规则，不重复创建，不自动恢复禁用或拒绝状态。
6. 管理员审批并分配角色、SBU，业务授权开始生效。Pending 若已禁用则批准仍保留禁用，需要显式启用；Rejected 的显式重新批准恢复启用。
7. 审批通过/拒绝会递增安全版本并撤销旧会话。员工批准后重新 OA 登录，加载业务权限。

待审批用户无需注册、设置 ZX 密码或记忆第二套密码。待业务授权用户可以访问系统外壳、me、刷新和退出；应用中心显示空状态，不开放业务页面或业务接口。

### 1.3 预分配、冲突与离职

- 管理员可以提前按工号查询 LDAP，一次完成用户创建、审批和角色 / SBU 分配；员工首次登录即可进入。预开通不需要用户 OA 密码，也不证明用户已完成认证，首次实际登录仍须验证 OA 密码。
- 同工号已经存在 `LOCAL` 账号时返回账号冲突，由管理员处理；不覆盖密码、不改变认证类型、不继承权限、不再创建第二个同名用户。
- 已删除的工号和目录唯一标识仍占位，登录时不自动重建、恢复或重新审批。
- 被拒绝的申请保持拒绝状态；重复 OA 登录不自动变回待审批。管理员可以显式重新审核。
- 被禁用的已审批账号保持禁用；LDAP 认证成功不能自动启用。
- 公司工号唯一，离职后再入职分配新工号。新工号创建新用户，不按姓名、邮箱或旧账号合并，不继承旧权限。
- 相同工号对应的 LDAP 唯一标识发生变化时拒绝自动关联；相同目录标识对应另一工号时也拒绝，交管理员核实。
- 上述预开通针对 LDAP 中已存在的 OA 身份。客户若没有公司 OA 身份，不能凭一个手工填写的工号就开通 OA 登录；外部客户认证需另定方案，不能发放本地管理账号或共享管理密码作为替代。

上线前须盘点已有的数字型本地账号。当前用户管理只有修改显示名，没有修改登录账号的接口；若要腾出同名工号，需单独审批维护操作：为本地账号确定新的唯一名称，保留其 UserId，更新 `ACCOUNT` / `NORMALIZED_ACCOUNT` 并撤销原会话，然后让 OA 登录创建独立的待审批用户。不能只修改显示名，也不能依靠软删除腾出工号。该维护操作不混入本次自动 Migration，不把原本地权限迁给新的 OA 用户。

## 2. LDAP 配置与验证范围

### 2.1 已知配置

| 项目 | 配置 / 说明 |
| --- | --- |
| 主节点 | `10.41.2.10:389` |
| 备用节点 | `10.32.12.10:389` |
| 目录类型 | Active Directory，LDAP v3 |
| 本次验证的连接模式 | 普通 LDAP，simple bind，未使用 TLS |
| Base DN | `OU=立讯集团,DC=luxshare,DC=com,DC=cn` |
| 服务账号 bind 名称 | `itks_joinad@luxshare.com.cn` |
| 服务账号密码 | 从部署密钥配置获取，本文及仓库不保存实际值 |
| 用户查找属性 | `sAMAccountName` |
| 用户过滤器 | `(&(objectCategory=Person)(objectClass=user)(sAMAccountName=<转义后的工号>))` |
| 姓名、邮箱、部门、职位 | `cn`、`mail`、`department`、`title` |
| 稳定身份标识 | 拟使用 AD `objectGUID`，需读取验证 |
| 工号属性 | 以 IT 确认的目录定义为准，见 2.4 |

首次只接入上述两台明确配置的节点；其他历史地址、端口不作为隐含重试目标。

### 2.2 实测结果

2026-11-02，通过临时 SSH 登录服务器 `KSD5-BU17-ZS01`（`10.32.15.176`），使用经用户授权的 OA 测试账号做 LDAP simple bind。只记录结论，不保存个人密码、SSH 临时口令或目录 DN。

| bind 名称格式 | `10.41.2.10:389` | `10.32.12.10:389` |
| --- | --- | --- |
| 裸工号 | LDAP result `49`，认证拒绝 | LDAP result `49`，认证拒绝 |
| `工号@luxshare.com.cn` | LDAP result `0`，认证成功 | LDAP result `0`，认证成功 |
| `LUXSHARE\工号` | LDAP result `0`，认证成功 | LDAP result `0`，认证成功 |

这证明从该服务器出发，LDAP 能验证该 OA 凭据。用户界面仍只输入工号，后台负责查询用户和选择正确的 bind 名称。

以下尚未由上述测试证明：服务账号当前可查询目录、该 Base DN 可搜索到用户、`objectGUID` / 工号字段的真实取值、禁用字段读取、TLS 连接、其他员工账号、ZX 容器运行时兼容性、ZX 完整登录会话。

本地 Mac 曾出现 TCP 可连但 LDAP 返回 `Can't contact LDAP server`，这是未取得有效 LDAP 响应，不能归因为 OA 密码错误，也不能推断网络失败的具体原因。

当前部署文档列出 `10.32.15.177` / `10.32.15.178` 为后端节点。开发验收必须从两台实际 Identity 容器再验证，不能以 176 上的成功代替。

### 2.3 新增配置契约

在 Identity API 新增 `AuthenticationLdap` 配置节，以下是待实现的配置示例，不是当前程序已支持的参数。初始保持关闭，配置完整后开启。

```json
{
  "AuthenticationLdap": {
    "Enabled": false,
    "Provider": "luxshare-ad",
    "Servers": [
      { "Host": "10.41.2.10", "Port": 389, "Encryption": "Plain" },
      { "Host": "10.32.12.10", "Port": 389, "Encryption": "Plain" }
    ],
    "BaseDn": "OU=立讯集团,DC=luxshare,DC=com,DC=cn",
    "BindDn": "itks_joinad@luxshare.com.cn",
    "BindPassword": "<由安全配置覆盖>",
    "UserNameAttribute": "sAMAccountName",
    "EmployeeNumberAttribute": "sAMAccountName",
    "SubjectAttribute": "objectGUID",
    "DisplayNameAttribute": "cn",
    "ConnectTimeoutSeconds": 3,
    "OperationTimeoutSeconds": 5,
    "TotalTimeoutSeconds": 15,
    "MaxConcurrentOperations": 8,
    "VerifyServerCertificate": true,
    "AllowPlainText": false
  }
}
```

说明：

- `EmployeeNumberAttribute=sAMAccountName` 是在 IT 确认“该值就是唯一且不复用的工号”后的选项。若实际工号在 `employeeID` 等属性，配置为真实字段，并校验工号与输入一致；不能默认把邮箱或显示名当工号。
- 两台服务器属于同一目录，`Provider` 使用同一个稳定值。切换节点不会创建新身份；更换 `Provider` 视为身份命名空间变化，不能随部署随机修改。
- `BindPassword` 在仓库模板保持空或占位，由 `AuthenticationLdap__BindPassword`、User Secrets 或密钥挂载配置注入。不开启请求体、配置对象或 `NetworkCredential` 日志。
- `Plain` 与示例中的 `AllowPlainText=false` 故意构成默认禁止组合：启用 LDAP 前须改为可用的 TLS 配置，或由运维显式开启明文兼容。正式环境是否暂时允许明文须明确记录；不能将 389 / Plain 称为加密。
- TLS 支持 `Ldaps`（通常 636）和 `StartTls`（通常 389）；先验证 CA、目录服务器 DNS 名和证书匹配，不设置“任意证书通过”，不在 TLS 失败后降级为明文。
- LDAP 关闭时，本地登录正常工作；LDAP 相关接口返回受控的“OA 登录暂未开放”。不因 LDAP 网络不可达阻止整个 Identity 进程启动。
- 配置启用时做字段、端口、超时、传输组合等本地校验；远端连接故障在 OA 请求中返回 503。连接、查询、bind、主备尝试均计入总超时，不能只限制 TCP 连接而无限等待 LDAP 回包。

### 2.4 开发接入前须确认的目录信息

1. 服务账号可以在 Base DN 下执行只读查询，权限以查找用户和必要属性为限。
2. `sAMAccountName` 与工号的关系、真实工号字段、长度与格式。工号始终按字符串保存，保留前导零；不要转整数。
3. `objectGUID` 是否可读。按 AD 的 16 字节值转换为规范 GUID 字符串，不能用 UTF-8 解码二进制值；不能用可改名的 DN、姓名、邮箱替代稳定标识。
4. 读取 `userAccountControl` 等目录状态需要的权限；LDAP result 49 的 AD 子码区分凭据错误、锁定、禁用、密码过期等，但前端不透传内部诊断。
5. 主备节点均可从两台 Identity 容器访问；使用 TLS 时容器具备信任链和必要的原生运行库。

## 3. 数据模型与状态机

### 3.1 用户表字段

继续使用 `ZX_IDENTITY_USER`，本期不引入独立的 OA 账号表。推荐新增以下列，列类型须由现有 Oracle / EF 映射生成并复核。

| 列 | 建议 Oracle 类型 | 空值 / 存量处理 | 作用 |
| --- | --- | --- | --- |
| `AUTH_TYPE` | `VARCHAR2(16)` | `NOT NULL DEFAULT 'LOCAL'` | `LOCAL` / `LDAP` |
| `EMPLOYEE_NO` | `VARCHAR2(32)` | 可空，存量为 NULL | OA 工号 |
| `LDAP_PROVIDER` | `VARCHAR2(32)` | 可空，存量为 NULL | 目录来源 |
| `LDAP_SUBJECT` | `VARCHAR2(36)` | 可空，存量为 NULL | 规范化 `objectGUID` |
| `APPROVAL_STATUS` | `VARCHAR2(16)` | `NOT NULL DEFAULT 'Approved'` | `Pending` / `Approved` / `Rejected` |
| `SECURITY_VERSION` | `NUMBER(19,0)` | `NOT NULL DEFAULT 1` | 独立会话安全版本，同时用于账户状态变更的乐观并发检查 |
| `REVIEWED_BY_USER_ID` | 沿用现有用户 GUID 列类型 | 可空 | 最近审批人 |
| `REVIEWED_AT_UTC` | 沿用现有 UTC 时间列映射 | 可空 | 最近审批时间 |
| `REVIEW_REASON` | `NVARCHAR2(500)` | 可空 | 最近审批说明 / 拒绝原因 |

改造现有 `PASSWORD_HASH`：数据库和 C# 均允许 NULL。`LOCAL` 必须有有效哈希，`LDAP` 必须为 NULL。不生成随机假密码、空串密码或 OA 密码哈希占位。

`AUTH_TYPE` 的数据库默认值用于兼容存量本地创建；OA 创建方法必须显式写 `LDAP` 和 `Pending`，不能依赖默认值。`LOCAL` 账号的审批状态固定为 `Approved`，保留已有 `IsEnabled` 和软删除状态。

新增身份字段用于认证，不全部放入 `/me` 或前端列表。前端只返回业务需要的工号和状态；Provider / Subject 由服务端维护，普通登录、预建、审批请求均不接受客户端指定或修改它们。

索引与约束：

- 保留 `NORMALIZED_ACCOUNT` 的全表唯一索引；两类账号共享同一账号名称空间。
- `EMPLOYEE_NO` 建唯一索引；本地用户为 NULL。包含已软删除的 OA 用户，不允许登录时绕过软删除重新建档。
- `(LDAP_PROVIDER, LDAP_SUBJECT)` 建唯一索引；两列同时有值或同时为空。所有新 OA 申请在目录查询并成功 bind 后写入两列。
- 校验 `AUTH_TYPE` / `APPROVAL_STATUS` 合法值；LDAP 账号必须有工号且 `ACCOUNT` 与规范化工号一致。
- `LDAP + Pending` 新建时 `IsEnabled=true`，可被管理员禁用；`Rejected` 必须 `IsEnabled=false`；`Approved` 可以启用或禁用。
- 数据库唯一索引负责最终并发兜底，不能只靠“先查再插”。Oracle B-tree 唯一索引允许全 NULL 的键，不能给空标识统一写占位字符串。
- 索引和约束名使用显式短名称，兼容现有 Oracle 环境的标识符长度限制。

### 3.2 状态与允许操作

| 认证类型 / 状态 | IsEnabled | 登录结果 | 状态变化来源 |
| --- | --- | --- | --- |
| `LOCAL + Approved` | 保留原值 | 原本地登录规则 | 原用户管理 |
| `LDAP + Pending` | true | 建立受限会话，无业务能力 | 首次 OA 认证或管理员仅创建待授权 |
| `LDAP + Pending` | false | 拒绝登录，不自动启用 | 管理员禁用或历史待审批记录 |
| `LDAP + Rejected` | false | 显示未获批准，不发会话 | 有权限管理员拒绝 |
| `LDAP + Approved` | true | LDAP 认证通过后发会话 | 管理员审批或创建并开通 |
| `LDAP + Approved` | false | 禁用，不发会话 | 管理员禁用 |
| 任意类型已删除 | false | 拒绝，不自动重建 | 现有软删除 |

新建、待审批重复登录不改变 `SecurityVersion`。审批、拒绝、启用、禁用、删除、恢复、本地改密等会改变登录资格的操作递增版本，并按现有规则撤销会话、使权限缓存失效。改显示名不需要递增。

`CanLogin`、刷新和 `/me` 均检查未删除、启用和状态允许维持会话（OA 为 Pending/Approved）；新登录还检查锁定。`Enable()` 可以启用 Pending 以恢复外壳访问，但必须拒绝 Rejected。业务授权额外要求 Approved，不能借启用获得角色或数据范围。恢复软删除只恢复原有身份，保持原审批状态，不能自动开通。

审核接口携带 `expectedSecurityVersion`，数据库条件更新并递增该版本。并发审批、拒绝或禁用冲突返回 409，禁止后一请求静默覆盖前一状态。不能仅在 Controller 比较版本后执行无条件更新。

最近审批字段不等于完整审批历史；每次审批、拒绝和显式重新审核还须写结构化审计事件，保留操作者、目标用户、变更前后状态、时间与 TraceId。

## 4. 后端实现流程

### 4.1 LDAP 认证适配器

Application 定义 `ILdapAuthenticator`（认证并返回可信目录用户）和 `ILdapDirectory`（管理员按工号只读查人）。Infrastructure 实现 `LdapAuthenticationService` 和配置绑定；Controller 不直接操作 LDAP。

认证步骤：

1. 工号 Trim / 标准化，校验长度格式；密码原样使用，不 Trim，不保存。空密码在发往 LDAP 前拒绝，防止误触发匿名或未认证 bind。
2. 用服务账号连接配置节点，执行用户搜索。过滤器值按 RFC 4515 转义；不把用户输入拼接到 DN 或允许用户提交任意过滤器。
3. 搜索必须返回唯一用户；0 条按凭据无效处理，多条按目录配置异常拒绝，不能随意取第一条。
4. 在同一节点上，用独立连接和查询结果的 DN + OA 密码做 bind。服务账号查询连接与用户认证连接不能互相重绑或并发共享。
5. bind 成功后，返回目录提供的规范工号、`sAMAccountName`、Provider、`objectGUID`、姓名和必要状态。只有完成用户 bind 后才能把结果当作员工身份；服务账号能查到用户不代表员工认证成功。
6. 主节点发生连接失败、断连、超时等可切换备用节点，并在备用节点重新查询再 bind。收到 result 49 或明确的用户拒绝结果后停止，不遍历多个格式和节点反复尝试密码，避免触发 AD 锁定。

使用 `System.DirectoryServices.Protocols` 的受支持版本并在最终 Linux 镜像验证原生 LDAP / TLS 依赖。同步 API 若放入线程池必须有并发上限、操作超时和总预算；`Task.Run` 本身不提供超时或取消底层请求的保证。每次操作及时 Dispose 连接，不建立单个全局可变 `LdapConnection`。

实测成功的 UPN 和域账号格式保留为排障依据；正式方案采用“查 DN 再 bind”，不在生产登录接口里依次猜格式。

### 4.2 OA 用户查找与建档

LDAP 认证通过后：

1. 按 Provider + Subject 查现有身份，并核对工号与账号；再按工号、规范账号检查冲突。查找必须覆盖软删除记录。
2. 命中 `LOCAL`、已删除、身份不一致时拒绝，不更改原记录。
3. 命中 OA 用户时检查身份、启用和锁定状态；启用的 Pending 可以建立受限会话，Rejected 和禁用账号仍拒绝，均不自动分配权限。
4. 未命中时创建 Pending / Enabled 用户并提交数据库，无角色 / SBU 写入，然后走正常会话创建。
5. 并发首次登录触发唯一冲突时，回滚 / 清理失败的 EF 跟踪状态，再读已创建用户并核对身份，返回其实际状态。本项目 DbContext 已把 Oracle ORA-00001 转换成 `UniqueConstraintException`，应在此业务路径处理，不能吞掉全部数据库异常。
6. 对 `Pending/Approved + Enabled` OA 用户复用现有 Redis 会话、接管确认、JWT 与 Refresh Cookie。有效业务能力仅对 Approved 开放。

匿名首次建档的 `CreatedByUserId` 可以为空；现有审计拦截器负责 UTC 时间。日志标识这是 LDAP 首次建档，不伪造管理员操作者。

### 4.3 管理员预开通与审批

用户管理增加“预开通 OA 用户”入口、认证类型 / 审批状态筛选及“待审批”视图。预开通弹窗先填写工号并查询 LDAP，显示姓名、部门和目录状态，确认后选择角色和 SBU；后台读取目录姓名和唯一标识，不接收管理员手工指定的目录身份。提供“创建并开通”和“仅创建待审批”两种提交动作。

- 普通员工审批通过至少选择一个有效角色和一个有效 SBU；空 SBU 不能解释成全部数据范围。
- 审批接口一次提交角色、SBU、审批状态、审批人和启用状态，在一个数据库事务中完成。需要复用角色 / SBU 校验规则，不能直接串调多个各自 SaveChanges 的服务后宣称原子操作。
- 校验全部角色、SBU 存在且启用，沿用内置管理员授权保护；禁止创建时默认赋管理员。
- 审批 / 拒绝提交后，执行权限缓存失效和会话撤销；Redis 失败返回 503，明确数据库可能已提交，不假报整体成功。
- “创建并开通”在一次后端请求、一个数据库事务中写用户、角色、SBU、审批信息和启用状态。与已有用户审批复用同一套授权校验和领域逻辑；不要让前端串联创建和审批两个请求来模拟原子开通。UserId 在创建时生成，用户首次登录继续使用该 ID。
- “仅创建待审批”只要求用户创建权限，产生 Pending / Enabled 用户，不分配角色 / SBU，可登录外壳。后续由有审核和授权权限的管理员开通。
- 预开通命中已有 OA 用户时返回结构化冲突，保留现有数据；待审批用户转到审核，已开通用户转到现有角色 / SBU 维护。不能无提示覆盖预分配，也不能重复新建。与首次自助申请同时创建发生唯一冲突时，返回已存在的真实状态，由管理员明确处理。
- LDAP 登录不覆盖管理员维护的角色、SBU、审批和启用状态；姓名仅首次从目录填充，后续先保留现有显示名维护方式，不每次登录覆盖。

### 4.4 管理员按工号查询 LDAP

这项查询用于预开通前确认“工号是否存在、查到的人是否正确”，不需要员工的 OA 密码。使用配置中的 LDAP 服务账号 bind，然后调用 `ILdapDirectory.GetByEmployeeNoAsync` 执行精确搜索。管理员查人不进行员工密码 bind，不代表该员工已登录或同意访问。

查询步骤与结果要求：

1. 管理员输入工号并明确点击查询；不默认提供全目录遍历、模糊检索或自动补全。复用规范化、过滤器转义、主备、操作超时和并发限制。
2. 目录中返回 0 条时，明确提示“LDAP 中未找到该工号”；多条时视为目录配置异常，不能随便取第一条。
3. 返回唯一用户时，由服务端解析工号、objectGUID 和目录状态，仅返回姓名、部门、工号、可读状态及是否可继续开通；不向浏览器暴露服务账号、DN、Provider 或 Subject。
4. 目录账号被禁用、必要身份字段缺失或状态无法判定时，不允许开通，并返回明确原因；不能将“没读到禁用标记”直接解释为正常。
5. 检查现有 ZX 账号及软删除占位。同工号已有记录时，提示已存在或冲突，按第 4.3 节转入已有用户的审批 / 授权维护，不新建重复用户。
6. 只读查询不落用户记录、不生成 UserId、不改变审批、角色、SBU 或会话。部门仅作为本次核对信息，不据此自动授予 SBU。
7. 前端修改工号后立即清空之前的查询结果，并要求重新查询；“创建并开通”按钮仅在当前工号查询通过后可用。提交创建时服务端仍重新查目录、核对必要字段和禁用状态、校验唯一性，不能相信前端的 `found` / `canProvision`，也不能依赖过期的预览结果。

“工号不存在”只适用于成功完成目录搜索且结果为 0 条；连接超时、断连、服务账号 bind 失败或查询权限不足属于依赖故障，返回 503 / `LdapUnavailable`，不能转换为工号不存在或员工密码错误。没有用户创建权限的调用方不能使用这个查询接口；匿名 OA 登录仍采用统一凭据错误，不暴露目录查询细节。

## 5. 会话与密码规则

当前 `AuthService` 用 `SHA256(PasswordHash)` 作为 `CredentialStamp`。OA 用户没有本地密码，必须改造这里，不能以空哈希作为所有 OA 用户的会话依据。

- 两类用户统一使用用户 ID + `SecurityVersion` 生成带版本前缀的凭据指纹，例如 `v2:<SHA256(userId + ':' + version)>`。
- 刷新时检查用户删除 / 启用 / 审批状态和当前安全版本；本地改密同时递增版本，维持旧会话失效语义。
- 保留 JWT 标准验证、Redis 会话、单账号一会话、接管确认、刷新轮换、重放检测、退出和强制下线。
- `/me` 返回 `authType`、`canChangePassword`；OA 用户为 false。前端隐藏本地改密表单或提示“请前往 OA 修改密码”；后端改密接口同样拒绝 OA 用户。
- 本地登录只接受 `LOCAL`；OA 登录只接受 `LDAP`。不存在账号、类型错误等本地失败继续使用统一凭据错误提示。
- 待业务授权使用同一套 JWT / Redis 会话；me 强制清空角色、权限、管理员标记、菜单和 SBU。权限服务先查用户业务资格再读取缓存，SBU 取数同样要求 Approved，不能只隐藏前端按钮。

**已有会话兼容：**上线前统一确定切换窗口。本方案选择让旧 `CredentialStamp` 会话重新登录，不增加长期双格式兼容；旧会话最迟刷新时因指纹变化失效。JWT 请求目前只核对 Redis，并不逐请求查用户表，因此不能宣称安全版本改变会瞬间阻止所有旧请求。若要求上线时全部立即重新登录，应在所有节点切换后执行受控会话撤销；数据库 Migration 不清 Redis。

**OA 密码变化与离职：**OA 改密后，下一次新登录使用新密码；ZX 的既有会话不因此自动失效。LDAP bind 也不会主动通知 ZX 员工离职。第一版沿用本地禁用 + 撤销会话，并明确由管理员处理离职；后续需要自动离职同步、定期目录检查或统一退出时另列需求。刷新不保存密码，也不重新 bind OA。

**一致性边界：**数据库提交与 Redis 撤销不是分布式事务，保留现有可接受的并发窗口和故障语义，见现有登录专题；不能承诺强一致的瞬时下线。

## 6. 接口契约

下表路径为 Identity 直连路径。浏览器统一入口为 `/api/identity` + 下表路径，例如 `/api/identity/api/Auth/ldapLogin`；前端 `request` 已有 `/api` baseURL，API 文件只写 `/identity/api/Auth/ldapLogin`，不要重复加 `/api`。

### 6.1 新增接口

| 方法 / 路径 | 输入 | 权限 / 结果 |
| --- | --- | --- |
| `POST /api/Auth/ldapLogin` | `employeeNo`、`password`、`replaceExistingSession` | 匿名 + CSRF + 限频；成功统一返回 Authenticated + created + tokens |
| `GET /api/Users/ldapLookup` | 查询参数 `employeeNo` | 已登录且具备 `identity.user.create`；按工号只读查询 LDAP，返回核对信息，不接收员工密码、不创建用户 |
| `POST /api/Users/addLdap` | `employeeNo`、`openImmediately`，立即开通时还传 `roleIds`、`sbuIds`、可选 `reason` | 创建权限；立即开通还要求审批、角色与 SBU 分配权限；服务器查 LDAP，不接收密码 / Subject |
| `PUT /api/Users/approveLdap` | `id`、`roleIds`、`sbuIds`、`expectedSecurityVersion`、可选 `reason` | 审批 + 分配角色 + 分配 SBU 权限；原子审批开通 |
| `PUT /api/Users/rejectLdap` | `id`、`expectedSecurityVersion`、`reason` | 审批权限；拒绝未开通申请，记录原因 |

新增一个静态权限常量 `UserApprove = "identity.user.approve"`，加入前端权限常量和默认用户管理 action 节点，沿用源码常量目录的启动同步机制。审核角色显式授予此权限。

`ldapLookup` 的结果契约：成功完成查询返回 HTTP 200 / `code=0`。找到用户时返回示例如下；`canProvision` 由服务端综合目录启用状态、必要身份字段和本地账号冲突决定。`directoryStatus` 为 `Enabled`、`Disabled` 或 `Unknown`；后两者均不能预开通。该状态表示目录账号启用情况，不承诺员工当前未锁定、密码未过期或知道正确密码；实际登录仍须 LDAP bind。

```json
{
  "code": 0,
  "message": "已查询到 OA 用户，请核对后开通。",
  "data": {
    "found": true,
    "employeeNo": "<员工工号>",
    "displayName": "<目录姓名>",
    "department": "<目录部门>",
    "directoryStatus": "Enabled",
    "canProvision": true,
    "reason": null
  }
}
```

工号不存在时，`data={"found":false,"canProvision":false,"reason":"LdapUserNotFound"}`。找到但不可开通时 `found=true`、`canProvision=false`，原因使用 `DirectoryDisabled`、`DirectoryStateUnknown`、`DirectoryIdentityIncomplete`、`AccountExists`、`AccountTypeConflict` 或 `IdentityConflict`。LDAP 超时等故障返回 HTTP 503 / `600503`；多条结果返回同样的依赖错误，原因为 `LdapAmbiguousResult`；不能伪装成 `found=false`。无登录返回 401，无创建权限返回 403。查询响应 `Cache-Control: no-store`，不缓存个人目录资料。

`approveLdap` 必须同时校验 `identity.user.approve`、`identity.user.roles`、`identity.user.sbus`，不能在多个 `[Authorize]` 中误写成任意一个即可。拒绝仅允许 `Pending/Rejected`；已开通账号使用现有禁用流程。`Rejected` 重新批准必须由审核人显式发起，保留审计。

`addLdap` 的 `openImmediately` 默认 false。为 true 时，服务端必须额外校验上述全部审批 / 授权权限，在同一事务中创建 `Approved + Enabled` 用户、角色关系和 SBU 关系并记录审批人。为 false 时仅建待审批记录，拒绝携带非空角色 / SBU 列表，避免调用方误以为已经授权。成功返回 HTTP 201 / `code=0`，`data` 至少包含 `id`、`approvalStatus`、`isEnabled`，让前端明确展示“已开通”或“待审批”。两种操作均不签发员工登录会话。

OA 登录请求示例：

```json
{
  "employeeNo": "<员工工号>",
  "password": "<用户本次输入的 OA 密码>",
  "replaceExistingSession": false
}
```

OA 登录使用独立 `LdapLoginDto`，不复用带 BCrypt 72 字节限制的 `LoginDto`。工号最大长度建议 32，真实格式待目录确认；密码只做必填和防止过大请求的合理上限，例如 1024 字符，不套用本地新密码强度规则，不 Trim。

成功统一返回 HTTP 200 / `code=0`，包括首次创建或已有 Pending 用户：

```json
{
  "code": 0,
  "message": "登录成功。",
  "data": {
    "status": "Authenticated",
    "created": true,
    "tokens": {
      "accessToken": "<signed-jwt>",
      "accessTokenExpiresAtUtc": "<UTC>",
      "refreshTokenExpiresAtUtc": "<UTC>"
    }
  }
}
```

已有用户 `created=false`。不再返回旧的 `PendingApproval` 分支；Refresh 仅通过 HttpOnly Cookie 返回。`GET /api/Auth/me` 新增 `approvalStatus`、`hasBusinessAccess`。Pending 返回 `hasBusinessAccess=false`、`isAdministrator=false` 和空 `roles/permissions/sbus/menus`，前端只允许进入应用中心，不能用 URL 进入固定工作台或演示页。

原本地登录契约不变。OA 新接口须带 `X-Identity-CSRF: 1`、响应 `Cache-Control: no-store`。未建业务权限也建立会话并按正常逻辑写 Cookie，前后端必须同步发布。

### 6.2 错误与依赖故障

| 场景 | HTTP / 业务码 | 结构化 `data.reason` |
| --- | --- | --- |
| LDAP 用户不存在、密码错误、AD 拒绝 | `401 / 600401` | `InvalidCredentials` |
| LDAP 成功但申请被拒绝 | `403 / 600005` | `ApprovalRejected` |
| LDAP 成功但 ZX 用户禁用 | `403 / 600005` | `AccountDisabled` |
| LDAP 成功但用户已软删除 | `403 / 600005` | `AccountUnavailable` |
| LDAP 成功但存在本地账号同名 / 绑定冲突 | `409 / 600003` | `AccountTypeConflict` / `IdentityConflict` |
| 审批状态 / 安全版本已变化 | `409 / 600004` | `StateChanged` |
| LDAP 关闭或依赖不可用 | `503 / 600503` | `LdapDisabled` / `LdapUnavailable` |
| 请求超频 / LDAP 并发额度已满 | `429 / 600429` | `TooManyRequests` |

匿名 OA 登录只有在 LDAP 认证成功后才返回用户具体开通状态；不能用一个匿名“查工号是否注册”接口泄露员工状态。管理员查询接口必须已登录并通过用户创建权限校验，才可以查看用于预开通的目录信息。AD 原始错误、DN、内部地址和异常栈只进入经过控制的服务端诊断，不直接返回前端。

`GlobalExceptionHandler.GetStatusCode` 已将 `ServiceUnavailable` 映射为 HTTP 503；新增 LDAP 依赖异常沿用该分支，不把业务码 `600503` 映射为 500。

沿用全局 IP / 方法 / 路由限频，并对 LDAP 增加按规范工号的共享限频和每实例并发上限；新增用户尚无数据库失败计数，也需要保护。账号限频键使用规范工号摘要和有界 TTL，不记录密码，不产生无限维度指标。LDAP 用户借用本地失败计数时，只有明确凭据错误才累计；连接故障不能计作密码错误或锁死用户。

### 6.3 扩展现有接口

- `GET /api/Auth/me`：增加 `authType`、`canChangePassword`、`approvalStatus`、`hasBusinessAccess`；Pending 的全部业务能力强制为空。
- `PUT /api/Auth/changePassword`：仅 LOCAL 可调用，OA 返回结构化类型错误；不修改 OA 凭据。
- `GET /api/Users/getUserList`：增加 `authType`、`approvalStatus` 筛选；`getUserList` / `getUser` DTO 增加认证类型、工号、审批状态、最近审批信息和 `securityVersion`。
- `PUT /api/Users/setUserEnable`：可启用 Pending / Approved OA 账号，但不能启用 Rejected；启用不等于业务授权。
- 禁用、删除、批量删除、恢复：维护审批不变、递增安全版本、按规则撤销会话，防止自动重新建档。
- `PUT /api/Roles/updateUserRoles` 和 `PUT /api/Users/updateUserSbus`：继续作为授权维护入口；给待审批用户预分配关系也不产生有效业务权限和数据范围。

## 7. 开发文件清单

以下后端路径相对 `ZX.Platform`，前端路径相对 `ZX.front.vue`。新增文件名为推荐落点，可按实际职责拆分；标为“修改”的路径已在当前代码中核对。

### 7.1 后端

| 操作 | 文件 | 改动 |
| --- | --- | --- |
| 修改 | `Services/Identity/ZX.Platform.Identity.Domain/Entities/User.cs` | 认证类型、工号、目录身份、审批、安全版本；OA 创建、审批、拒绝和启用约束 |
| 新增 | `Services/Identity/ZX.Platform.Identity.Domain/Entities/UserAuthType.cs`、`UserApprovalStatus.cs` | 类型 / 状态常量 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations/UserConfiguration.cs` | 列映射、可空密码、索引、约束、安全版本并发检查 |
| 新增 / 修改 | `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Migrations/<新迁移>.*`、`PlatformIdentityDbContextModelSnapshot.cs` | Oracle 迁移、Designer、Snapshot |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Persistence/IUserRepository.cs` | 包含软删除的工号 / 目录身份查询 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Repositories/UserRepository.cs` | 身份查找、筛选、并发建档复查 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Authentication/AuthService.cs`、`IAuthService.cs` | 本地类型校验、OA 编排、刷新资格、安全版本、OA 禁止改密 |
| 新增 | `Services/Identity/ZX.Platform.Identity.Application/Authentication/ILdapAuthenticator.cs`、`ILdapDirectory.cs`、`LdapIdentity.cs` | 认证及 `GetByEmployeeNoAsync` 只读查询、目录结果对象、受控错误 |
| 新增 | `Services/Identity/ZX.Platform.Identity.Application/Authentication/Dtos/LdapLoginDto.cs`、`LdapLoginResultDto.cs` | OA 请求和建立会话结果 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Authentication/Dtos/CurrentUserDto.cs` | `authType`、`canChangePassword` |
| 新增 | `Services/Identity/ZX.Platform.Identity.Infrastructure/Security/LdapOptions.cs`、`LdapAuthenticationService.cs` | 配置、LDAP 查询 / bind、主备、超时、证书 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Infrastructure/ZX.Platform.Identity.Infrastructure.csproj` | LDAP 客户端包依赖 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/Controllers/AuthController.cs` | OA 登录 action；成功才创建 Cookie |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Users/IUserService.cs`、`UserService.cs` | 管理员 LDAP 查询、提交时重新查人、原子预开通、审批 / 拒绝事务、状态防绕过、审计 |
| 新增 | `Services/Identity/ZX.Platform.Identity.Application/Users/Dtos/CreateLdapUserDto.cs`、`ApproveLdapUserDto.cs`、`RejectLdapUserDto.cs` | 管理操作请求 |
| 新增 | `Services/Identity/ZX.Platform.Identity.Application/Users/Dtos/LdapUserLookupQueryDto.cs`、`LdapUserLookupResultDto.cs` | 工号查询输入及姓名 / 部门 / 目录状态 / 可开通结果，不返回 DN 或目录唯一标识 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Users/Dtos/UserDetailDto.cs`、`UserQueryDto.cs` | 类型、审批筛选与展示 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/Controllers/UsersController.cs` | 受保护的工号查询、预建、审批 / 拒绝接口和授权 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Application/Authorization/IdentityPermissions.cs` | 静态 `identity.user.approve` |
| 修改 | `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/DefaultMenus.cs` | 用户管理下增加审批 action 节点，供权限树授权 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/Extensions/AuthenticationExtensions.cs`、`Program.cs` | 注册目录服务、配置校验及有界执行器 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/Extensions/RateLimitingExtensions.cs` | OA 账号维度限频；保留原全局限频 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/ErrorHandling/GlobalExceptionHandler.cs` | LDAP 依赖故障的 503 映射 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/Resources/Localization/AuthMessages.resx`、`AuthMessages.en-US.resx`、`UserMessages.resx`、`UserMessages.en-US.resx` | 双语业务结果、错误、校验提示 |
| 修改 | `Services/Identity/ZX.Platform.Identity.Api/appsettings.json` | LDAP 关闭时的非敏感默认配置 |
| 核查 / 按需修改 | `Services/Identity/ZX.Platform.Identity.Api/Dockerfile`、部署时的 Identity 环境变量模板 | LDAP 原生库、CA、各节点运行配置；镜像中不写密钥 |

`RedisAuthSessionStore` / `IAuthSessionStore` 已接收 `credentialStamp` 字符串，若只更换指纹生成规则，可保留存储接口；开发仍须检查序列化和旧会话失效行为，不能无理由重写整套会话服务。

`AuthorizationInitializer` 继续只同步静态权限和缺失菜单。本次新增审批权限沿用原机制，不在此处创建 OA 用户、不访问 LDAP、不审批或授角色。

### 7.2 前端

| 操作 | 文件 | 改动 |
| --- | --- | --- |
| 修改 | `src/views/login/components/LoginForm.vue`，必要时 `src/views/login/index.vue` | 系统 / OA 两入口、OA 工号密码、待审批和拒绝提示、独立字段规则 |
| 修改 | `src/api/identity/auth.ts` | OA 请求、判别结果类型、当前用户能力字段 |
| 修改 | `src/stores/auth.ts` | 成功校验并保存 tokens；按 me.hasBusinessAccess 控制空应用中心和业务路由；保留跨标签锁和接管 |
| 修改 | `src/api/identity/users.ts` | LDAP 工号查询、预建、审批、拒绝、筛选和返回字段 |
| 修改 | `src/api/identity/permissions.ts` | `UserApprove` 常量 |
| 修改 | `src/views/system/user/index.vue`、`types.ts`、`components/UserSearch.vue` | 类型与审批列表、操作按钮及权限 |
| 修改 / 新增 | `src/views/system/user/components/UserCreateDialog.vue`；新增 `UserLdapCreateDialog.vue`、`UserApprovalDialog.vue` | OA 预开通先查工号、核对姓名部门，再分配角色 / SBU；修改工号清空旧结果；一次创建并开通；不填员工密码 |
| 修改 | `src/components/auth/UserMenu.vue`、必要时 `ChangePasswordDialog.vue` | OA 不展示本地改密表单，提示到 OA 修改 |
| 修改 | `src/locales/zh-CN/auth.ts`、`src/locales/en-US/auth.ts`、`src/locales/zh-CN/userManagement.ts`、`src/locales/en-US/userManagement.ts` | 两类登录和审批相关界面词条 |
| 文档同步 | `docs/authentication.md`、`docs/user-management.md`、`docs/authorization.md` | 实现后更新真实使用方式和验收状态 |

沿用仓库 `AGENTS.md`：统一请求层、`ApiResponse`、结构化原因、中英文、主题变量和原生组件规范。前端按 `data.status` / `data.reason` 处理业务分支，不匹配中文 message，不把 OA 密码写入 localStorage、sessionStorage、URL、日志或“记住密码”。

## 8. Migration 与发布顺序

1. **先完成目录只读查询联调。**核实服务账号、工号和 objectGUID，再定最终字段校验。实测 bind 成功不等于查询阶段已完成。
2. **修改领域与 EF 映射，生成一次新增迁移。**不要重写已经发布的旧迁移。包含新列、存量回填、密码可空、索引 / 约束；保留所有存量用户 ID、密码哈希、角色和 SBU。
3. **核对 Oracle SQL。**新必填列可按“先可空添加 → 更新存量 → 设置 DEFAULT → 确认无 NULL → NOT NULL”执行；直接 `ADD DEFAULT NOT NULL` 也须确认目标版本、锁和表规模。Oracle 空串视为 NULL，不使用 `''` 回填工号 / 标识；状态 / 类型明确回填有效字符串。Review 时间沿用 UTC 映射，不使用数据库本地时间代替 UTC。
4. **完成数据库门槛检查。**创建唯一索引前检查重复值、长度及字段组合，确认新 CHECK 约束对全部存量有效。明确控制长事务和 DDL 锁；Oracle DDL 会隐式提交，失败不能假定整份脚本已回滚。保留生成的迁移 Designer / Snapshot，不手改未来迁移来“补”本次逻辑。
5. **完成后端与前端实现和验证。**先禁用 LDAP 开关，验证本地登录、改密、授权和用户管理；再在测试环境启用 OA 流程。
6. **正式先迁移，再统一更新各后端实例。**数据库迁移由部署流程 / DBA 显式执行；应用启动不承担本次表结构迁移。新版本启动后运行原有静态权限 / 菜单初始化，再由管理员授予审核权限。
7. **各节点配置 LDAP 与依赖，再开放 OA 入口。**LDAP 新用户写入前，确认所有 Identity 节点均已更新。旧实例不会理解空密码或审批状态，不能让新旧实例混跑并启用 OA。各节点的目录 Provider、JWT、Redis 前缀一致。
8. **回归与回退。**LDAP 故障可关闭 OA 开关，保留本地管理账号。若要回退旧应用，在停写并备份后单独制定兼容策略；已有 OA 空密码用户不能直接搭配旧版 `PasswordHash NOT NULL` 模型，不能简单执行 Down 删除用户和审批数据。

迁移仅负责结构、默认值、存量状态和索引，不连接 LDAP、不注册员工、不审批、不绑定身份、不改角色或菜单授权、不删除 Redis 会话。后续迁移自动沿用 Snapshot 中的列；只有新需求改变模型时才再产生相应差异。

本轮追加 `20261103090000_AllowPendingLdapLogin`，只替换 OA 启用 CHECK，不回填任何用户启停用状态、角色或菜单。先停写迁移，再统一部署匹配前后端；历史 Pending / Disabled 由管理员逐个显式启用。Down 检查启用 Pending 并阻止不兼容回退。

实现完成后同步后端 `docs/api.md`、`authentication.md`、`identity.md`、`database.md`、`authorization.md` 和实际部署配置说明，区分“计划”与“已验证”。

## 9. 验收清单

测试使用独立用户、独立测试环境和有效的 OA 测试凭据；错误密码案例只做最小次数，避免锁定真实 AD 账号。逻辑测试优先用替代目录适配器，真实 LDAP 联调只验证目录特性和运行环境。

| 场景 | 必须结果 |
| --- | --- |
| LDAP 关闭 / 主备均故障 | OA 返回可识别状态；本地管理登录继续可用 |
| 存量本地账号迁移后 | ID、密码、状态、角色、SBU 保留；本地登录 / 改密 / 接管 / 退出正常 |
| LDAP 错密码、用户不存在、空密码 | 拒绝、不建用户、不分配关系、不发 Token / Cookie；空密码不发送 bind |
| 新工号首次 OA 认证成功 | 创建一条 Pending / Enabled 用户，密码为 NULL、无角色和 SBU；建立会话并进入空应用中心 |
| 相同工号重复 / 并发首次登录 | 保持一条记录；状态和已有预分配不被重置，唯一冲突受控 |
| 待审批用户调用登录 / 刷新 / me / 业务接口 | 登录、刷新、me 可用；me 无业务能力，业务接口 403；启用不产生业务授权 |
| 审批通过 | 角色、SBU、审批与启用同事务提交；用户仍用 OA 密码正常登录 |
| 无审批 / 角色 / SBU 权限调用审批 | 服务端拒绝；隐藏按钮不能代替接口鉴权 |
| 两位审核人同时审批 / 拒绝，或审批与禁用竞争 | 一个成功，另一个 409；不静默覆盖 |
| 管理员查询存在且正常的 OA 工号 | 返回姓名、部门、目录状态及可开通结果；不要求员工密码，不生成 UserId 或其他数据库记录 |
| LDAP 搜索成功但工号不存在 | 返回明确的未找到结果，不能开通；与依赖故障区分 |
| 目录账号禁用、状态未知、必要字段缺失或结果多条 | 不能预开通，返回相应受控原因；多条属于目录配置异常 |
| LDAP 查询超时、服务账号 bind 失败或权限不足 | 返回 503，不误报工号不存在或员工密码错误 |
| 未登录 / 无用户创建权限调用 ldapLookup | 分别返回 401 / 403，不提供目录检索信息 |
| 查询后修改工号或提交前目录状态变化 | 前端旧结果失效；后端重新查目录并阻止无效开通，不信任前端预览状态 |
| 管理员一次创建并开通 | 用户未登录过也能生成 UserId、角色 / SBU 和审批信息；员工首次登录直接进入并沿用同一 UserId；不需要提交申请 |
| 预开通权限不足或事务中途失败 | 缺少创建 / 审批 / 角色 / SBU 任一所需权限则拒绝；数据库事务失败不留下部分开通和半套授权 |
| 管理员仅创建待审批 | 不要求员工先登录；记录为 Pending / Enabled，可登录外壳，后续审批业务权限 |
| 预开通与首次申请并发 / 预开通重复提交 | 一条用户记录；冲突受控，不自动覆盖审批或已有角色 / SBU |
| 拒绝、禁用、删除后重复 OA 登录 | 不自动重新申请、启用或重建；无权限复活 |
| 同工号已有 LOCAL 用户 | 冲突并保留原用户、密码、角色；不合并、不改认证来源 |
| 同工号 objectGUID 变化 / 标识工号不一致 | 拒绝自动绑定；无旧权限继承 |
| OA 在错误登录入口 / 调本地改密 | 服务端拒绝，不能建立本地密码 |
| 主节点超时、备用正常 | 总预算内成功；每个节点独立查 DN；明确密码拒绝不触发主备轮询 |
| Redis、数据库或 LDAP 故障 | 对应受控响应；不误报密码错、不假报审批原子完成、不暴露凭据 |
| OA 修改密码 | 新登录使用新密码；既有 ZX 会话按已说明边界处理 |
| 离职再入职新工号 | 新 UserId / 新审批；不继承旧账号权限 |
| 安全版本、刷新与旧会话 | 本地改密仍撤销旧会话；OA 无密码可正常刷新；旧指纹按发布策略要求重新登录 |
| 用户只获一个 SBU / 一组角色 | 菜单、按钮、后端 API 与 SBU 范围均按本地授权生效 |
| 177 / 178 最终 Identity 容器 | 目录查询、用户 bind、TLS / 原生依赖、超时和故障切换均实测 |
| 用户体验 | 两种入口清楚、待业务授权进入空应用中心、无第二套 OA 密码、中英文和深浅主题正常 |

交付物包括：代码、一次 Oracle Migration 与经审阅的部署 SQL、无实际密码的配置模板、接口文档、逻辑验证结果、两节点 LDAP 联调结果及浏览器验收记录。记录失败阶段与 TraceId，不附带个人密码、服务账号密码或临时 SSH 凭据。

## 10. 实施结果与联调状态

第 10.1—10.7 节保留各轮实现与验证的历史记录，其中“Pending 不签发会话”等旧规则已由 10.8 节取代；历史通过结果不代表新规则已经联调。

### 10.1 已实现

后端（`ZX.Platform`，main 分支）：

- 领域与映射：`User` 增加 `AuthType`、`EmployeeNo`、`LdapProvider`、`LdapSubject`、`ApprovalStatus`、`SecurityVersion`、`ReviewedByUserId`、`ReviewedAtUtc`、`ReviewReason`；`PasswordHash` 改可空；工厂拆为 `CreateLocal` / `CreateLdap`，状态变更收敛到 `ApproveLdap` / `RejectLdap` / `Enable` / `Disable` 等方法，改密、禁用、删除、恢复、审批、拒绝统一递增安全版本。
- LDAP 适配器：`LdapAuthenticationService` 用服务账号查 DN、再用独立连接以用户 DN bind；主备节点遍历、`StartTls`/`Ldaps`/`Plain` 三种传输、RFC 4515 过滤器转义、`objectGUID` 16 字节转 Guid、`userAccountControl & 0x2` 判禁用、结果多条判歧义、总超时预算与并发信号量。
- 认证：`AuthService.LdapLoginAsync` 只对已审批且启用的账号签发会话，待审批返回 `PendingApproval` 且不写 Cookie；`RefreshAsync` 与 `GetCurrentUserAsync` 追加审批状态检查；凭据指纹改为 `v2:` + `SHA256("{userId:N}:{securityVersion}")`，使无密码账号也能识别旧会话。
- 管理接口：`Users/ldapLookup`（只读查目录）、`Users/addLdap`（预开通，可选一次开通）、`Users/approveLdap` / `Users/rejectLdap`（带 `expectedSecurityVersion` 的乐观并发审批）。
- 依赖治理：`ConcurrencyConflictException` → `409 / 600004`，`ErrorCodes.ServiceUnavailable` → `503 / 600503`；目录故障不计入工号失败次数，`RedisLdapLoginGuard` 按工号摘要计数并在超限时返回 `429`。
- 权限与菜单：新增 `identity.user.approve`，`DefaultMenus` 补齐“审批OA账号”操作节点。
- 中英文资源：Auth、User、Validation、Common 四组新增词条，登录页与用户管理页按后端语言返回提示。

前端（`ZX.front.vue`）：

- `api/identity/auth.ts` 新增 `ldapLogin`、`LdapLoginResult`、`UserAuthType`，`CurrentUser` 增加 `authType`、`canChangePassword`。
- `stores/auth.ts` 新增 `ldapLogin`：只有 `Authenticated` 建立会话，`PendingApproval` 不写本地会话；`canChangePassword` 作为能力开关。
- 登录页新增“系统登录 / OA 登录”两个入口与 `LdapLoginForm.vue`（工号、OA 密码、滑动验证、接管确认、待审批提示、OA 不可用提示）；两个表单共用 380px 列宽与同一套标题、表单间距、验证按钮和提交按钮样式，`v-show` 切换保留各自已输入的账号；顶部用户菜单在无本地密码时隐藏“修改密码”。
- 用户管理新增认证类型与审批状态筛选、两列展示、工具栏“只看待审批”、“预开通 OA 用户”与“审批开通”弹窗，`api/identity/users.ts` 与 `permissions.ts` 同步。

数据库与部署：

- 迁移 `20261102111516_AddLdapAccountApproval`（含 Snapshot）已生成，脚本已导出到 `deploy/database/upgrade-add-ldap-account-approval.sql`。
- 配置模板：`deploy/docker/templates/identity.env.example` 增加 `AuthenticationLdap__*` 注释段，默认不启用，服务账号密码只写在服务器本地 env 文件。

### 10.2 已验证

| 项目 | 结果 |
| --- | --- |
| 后端解决方案编译 | `dotnet build ZX.Platform.slnx` 0 警告 0 错误 |
| AuthChecks 全量 | `All authentication checks passed.`，含 `LDAP 账号开通检查通过。`、Oracle 19c 模型与迁移 SQL、SBU 作用域、转发头、真实 Redis 会话、授权与菜单检查 |
| LDAP 逻辑场景 | 首次登录建档、重复登录不重复建档、审批后可登录、拒绝/禁用阻止登录、同名本地账号冲突、已删除账号不重建、一次开通、安全版本冲突、目录故障受控、查询结果状态 —— 使用替代目录适配器 |
| 前端类型检查 | `pnpm type-check`（`vue-tsc --build`）通过 |
| 本地 Oracle 19c 迁移 | 本地库从 `20261030100000_AddMenuLauncherMetadata` 实际升级到 `20261102111516_AddLdapAccountApproval`；执行后 `PASSWORD_HASH` 可空，4 个 `CK_ZID_USER_*` 约束与 4 个新索引存在 |
| 存量数据保留 | 升级后原 2 个本地账号的 `PASSWORD_HASH`、角色与 SBU 不变，`AUTH_TYPE=LOCAL`、`APPROVAL_STATUS=Approved`、`SECURITY_VERSION=1` |
| 约束行为 | 用真实 UPDATE 探针确认：未审批 OA 账号启用、OA 账号带本地密码、本地账号空密码、未知认证类型、未知审批状态均被 `ORA-02290` 拒绝（探针已回滚） |

### 10.3 未验证 / 待联调

- **目录服务账号查询**：两台节点只验证过用户 bind，服务账号 bind + 按工号搜索 + 读取 `objectGUID` 尚未在目标 AD 上实测。
- **属性映射**：`EmployeeNumberAttribute` 目前默认 `sAMAccountName`，实际工号属性（如 `employeeID` / `employeeNumber`）需 IT 确认后修改配置，不改代码。
- **传输与证书**：`Plain` 389 在 `AllowPlainText=false` 时会被拒绝启动；若 AD 只开 389，需要运维确认后才可开启明文，否则应先配 `Ldaps` / `StartTls` 并确认证书链。
- **容器内连接**：`System.DirectoryServices.Protocols` 在 Linux 容器内对 TLS 和 Kerberos 的原生依赖未在 177 / 178 实际容器中验证。
- **交付脚本执行**：`deploy/database/upgrade-add-ldap-account-approval.sql` 已生成但未在 `SAJET` 上执行；本地升级使用的是等价 DDL（`IF` 分支与交付脚本一致），包裹脚本本身未跑过。
- **浏览器端到端**：OA 登录页、接管、待审批提示、用户管理预开通与审批流程未做真实浏览器与 Gateway 联调，中英文与三种主题未逐一目视确认。
- **两个表单的排版基线**：`LdapLoginForm.vue` 已对齐 `LoginForm.vue` 的标题层级、380px 列宽、表单间距、验证按钮与提交按钮样式，切换入口不跳列宽；但输入框标签、底部提示文案的换行在两套表单上长短不同，仍需在真实浏览器里目视确认（尤其英文与窄屏）。

### 10.4 与本文不一致之处

- **目录查询失败仍返回 200**：`ldapLookup` 以 `found=false` + `reason` 表达“查不到”，只有依赖故障才返回 503，便于页面区分“工号不存在”和“目录不可用”。
- **`AlterColumn` 改为显式 DDL**：Oracle 提供程序把 `PASSWORD_HASH` 放宽可空渲染成 PL/SQL，`else` 分支缺少 `NULL` 关键字会生成非法语句；迁移改用等价且幂等的显式 `ALTER TABLE ... MODIFY (... NULL)`，避免把无法执行的语句交给 DBA。
- **`BaseDn` 不再用 `[Required]`**：改为在 `LdapOptions.Validate()` 里只于 `Enabled=true` 时校验，否则关闭 LDAP 的环境必须填写无效配置才能启动。
- **工号维度失败限制使用 Redis**：Redis 记录按工号共享的失败次数；现有实现还会对已建档且未删除的 OA 账号累计数据库失败次数并执行锁定。目录依赖故障不计入凭据失败。取消请求后的记账限制见 10.7 节。

### 10.5 静态审查后的修复（第二轮）

第一轮实现通过了编译与逻辑自查，但静态审查发现 5 个 P1 与 5 个 P2 问题，均已修复。这部分改变了几处对外行为，联调时按新行为验收。

**P1**

| 问题 | 修复 |
| --- | --- |
| 预开通弹窗可能把权限开给错误的人：查询 A 后把输入改成 B，A 的姓名、部门和 `canProvision` 仍在，提交却发 B | 工号变化立即清空预览；记录 `lookedUpEmployeeNo`，提交要求当前输入与预览工号一致；查询递增序号，响应回来时核对工号与序号，过期结果直接丢弃 |
| 本地登录入口能锁住 OA 账号：OA 无本地密码，`validPassword` 恒为 false 却会累计数据库失败次数 | `LoginAsync` 读到非 `LOCAL` 账号时执行一次占位校验后直接返回统一凭据错误，不读写失败次数与锁定状态 |
| 审批接口能绕过 `identity.user.status`：`ApproveLdap` 只拒绝“已通过且启用”，被禁用的已通过账号可再次审批恢复启用 | 改为拒绝所有 `Approved`；服务层同时拦截并返回 `409 / 600004`；角色替换复用角色接口的管理员保护 |
| `TotalTimeoutSeconds` 没有真正限制总耗时：工作委托忽略令牌，排队在计时之前，超时后仍释放并发名额 | 从排队前开始计时；新增 `LdapOperation`，连接、搜索、bind、切节点前按剩余预算收紧超时；超时后并发名额等底层调用真正结束再归还 |
| TLS 证书校验可被配置直接绕过 | 删除无条件放行的 `VerifyServerCertificate` 回调；`Validate()` 在 `Enabled=true` 且该值为 false 时拒绝启动 |

**P2**

| 问题 | 修复 |
| --- | --- |
| 前端额外要求角色分配权限：打开预开通/审批都调用 `getRoleOptions()` | 仅在具备 `identity.user.roles` 时加载；无权限时预开通只能“仅创建待审批”，审批弹窗只能拒绝并说明原因 |
| 被拒绝用户没有重新审批入口 | 行操作对 `Rejected` 显示“重新审核”，复用审批弹窗并继续回传 `securityVersion` |
| 工号属性未贯通：查询固定用 `UserNameAttribute`，工号读不到时用输入值冒充 | 新增 `EmployeeLookupAttribute`，留空跟随 `EmployeeNumberAttribute`；工号或登录名属性缺失、返回工号与查询值不一致时按依赖故障拒绝，不再回填 |
| Redis 失败计数可能留下永久锁定键 | `INCR` 与补 TTL 合并为一段 Lua 原子执行，已有但无 TTL 的异常键会被补齐窗口 |
| 审批历史没有持久化审计 | 新增 `ZX_IDENTITY_USER_APPROVAL_AUDIT`（迁移 `20261102144318`），预开通、通过、拒绝、重新审核各写一条，含操作人、前后状态、原因、角色/SBU 快照与 TraceId |

**新增验证**

- `LDAP 账号开通检查通过。` 与 `All authentication checks passed.` 均通过，新增三个场景：本地入口不能锁 OA 账号、已通过账号不能再次审批、审批审计记录完整。
- 审计迁移在本地 Oracle 19c 上实际执行通过，`dotnet ef migrations list` 显示六条迁移全部已应用。
- 前端 `vue-tsc --build` 通过，改动文件已过 Prettier。
- `Oracle19Checks` 增加审计迁移断言：表、`ACTION`、前后状态、`TRACE_ID` 存在，且脚本不改写既有用户行。

**仍然未验证**

- 上述修复都没有在真实 AD 上验证：超时预算与证书严格校验需要目标节点联调，证书链错误的表现要与运维确认。
- 容器内 `System.DirectoryServices.Protocols` 的 TLS 原生依赖仍未验证。
- 前端权限拆分的实际效果需要分别用“仅有创建”“仅有审批”“有全部权限”三种账号在浏览器里过一遍。

### 10.6 静态审查后的收尾（第三轮）

第二轮修复后静态复查仍有 6 处需要收尾（1 个 P1、5 个 P2），下表记录对应改动。后续复查发现的取消记账、迁移并发写入与日志分类边界，以及本轮延期决定，见 10.7 节。

**P1**

| 问题 | 修复 |
| --- | --- |
| 客户端取消请求时提前释放 LDAP 并发名额：预算超时走专用分支，浏览器断开只落到通用 `catch`，立即 `Release()`，此时同步调用仍在执行 | 并发名额与总预算抽到 `LdapOperationRunner`：`work` 在独立任务中执行，`_limiter.Release()` 只写在它的 `finally`，正常返回、预算耗尽、调用方取消都不提前释放；停止等待后的任务用 `ContinueWith(OnlyOnFaulted)` 统一观察异常。`LdapAuthenticationService` 只保留目录协议，全部调用走这个 runner |

**P2**

| 问题 | 修复 |
| --- | --- |
| 预开通的“立即开通”仍只看角色分配权限，具备“创建 + 角色”的人能选中并提交必然 403 的选项 | 页面新增 `canOpenImmediately`（`create` + `approve` + `roles` + `sbus`），弹窗属性由 `canAssignRoles` 改为 `canOpenImmediately`；缺少任一项时隐藏该选项、默认且只允许创建待审批，中英文提示同步改为“缺少创建、审批、角色或 SBU 分配权限之一” |
| 已存在且达到阈值的“无 TTL 键”仍修不好：补 TTL 只在递增时发生，检查阶段先返回 429，永远走不到递增 | `EnsureAllowedAsync` 改为 Lua：`EXISTS` → 缺 TTL 时 `EXPIRE` → 再返回计数；先补窗口再判断超限，旧异常键在下次检查时自愈 |
| 审计快照按 2000 字符存储，61 个 GUID 就超限，合法审批会因写审计而整笔失败 | `ROLE_IDS` / `SBU_IDS` 改为 `CLOB`。Oracle 不允许直接 `MODIFY` 成 CLOB（ORA-22858），迁移 `20261102150741_WidenApprovalAuditSnapshot` 用“新增 `*_CLOB` 列 → `TO_CLOB` 复制 → 删除旧列 → 改名”，复制不截断；`Down` 先检查没有超过 2000 字符的快照，否则 `ORA-20010` 停止。新增部署脚本 `deploy/database/upgrade-widen-approval-audit-snapshot.sql` |
| 工号与目录登录名不同时，ZX 账号仍取登录名，导致账号定义与方案不一致、冲突检查查错对象 | 首次建档和预开通都改为 `Account = 工号`（`NormalizedAccount` 随之为工号），冲突检查也按工号查；`User.CreateLdap` 与 `LdapDirectoryUser.Account` 的注释明确“目录登录名只用于目录认证” |
| 搜索结果异常分类不完整：只捕获 `LdapException.ErrorCode=4`，搜索阶段还把 49 当成员工密码错误 | 搜索阶段不再把服务账号的 49 转成员工凭据错误，按依赖故障处理；管理员查人路径捕获 `LdapException.ErrorCode=49` 时记录 `LdapServiceAccountRejected` 与 Error 日志，员工登录路径目前仍使用通用依赖故障与 Warning 日志，统一日志分类已延期。`sizeLimitExceeded` 同时判断 `LdapException.ErrorCode` 与 `DirectoryOperationException.Response.ResultCode`，统一映射为结果歧义。分类集中在 `LdapSearchFailure` |

**新增回归检查**

- `LimiterIsHeldUntilWorkFinishesAsync`：单名额 runner，取消后断言底层仍在执行、第二个请求只能排队、`maxActive` 始终为 1，任务结束后名额归还。
- `GuardRepairsKeyWithoutExpiryAsync`：真实 Redis，构造“达到阈值且无 TTL”的键，断言检查阶段仍返回 429，但键被补回窗口。
- `SearchFailuresAreClassified`：错误码 4、响应码 4 都判为歧义，49 不是歧义。
- `DirectoryLoginNameIsNotTheZxAccountAsync`：目录登录名 `zhangsan`、工号 `10001` 时账号仍为工号；本地账号占用工号返回冲突；预开通同样取工号。

**验证**

- `dotnet build ZX.Platform.slnx`：0 警告 0 错误；`All authentication checks passed.`（含 `LDAP 账号开通检查通过。`）。
- `20261102150741_WidenApprovalAuditSnapshot` 在本地 Oracle 19c 实际执行成功，迁移历史共七条全部应用；`ROLE_IDS` / `SBU_IDS` 现为 `CLOB`。
- 审计快照容量实测：先用 SQL 插入 2012 字符并回滚；再用 EF 以 80 个角色 + 80 个 SBU（各 2639 字符）写入并读回，内容完全一致，探针数据已删除。
- 前端 `vue-tsc --build` 通过，改动文件通过 Prettier，预开通弹窗通过 eslint。

**仍然未验证**

- 真实 AD 上的取消路径（浏览器断开、网关超时）与严格证书校验表现未联调；runner 的取消/并发语义已由检查覆盖，但没有走真实 LDAP socket。
- `DirectoryOperationException` 携带 `sizeLimitExceeded` 的分支未在目标 AD 上真实触发，只验证了分类映射。
- 交付 SQL（含新增的扩容脚本）未在 `SAJET` 上执行。
- 若测试库已按旧规则用目录登录名建过 OA 账号，本方案不会自动改名；这类账号需要人工确认后处理，正式环境尚未发布，不存在该存量。

### 10.7 本轮延期决定与联调安排（2026-11-03）

本轮决定先保持现有代码与迁移实现，进入目标环境联调。以下事项按已知限制管理；延期不代表已修复或已完成上线验收。

| 事项 | 当前边界与影响 | 本轮处理与后续要求 |
| --- | --- | --- |
| P2：请求取消后可能漏记凭据失败 | 员工 bind 已执行，但调用方先取消或总预算先到期，后台随后返回凭据错误时，runner 只观察任务异常；`AuthService` 的失败处理分支可能收不到该异常，Redis 与数据库失败次数可能未累计。LDAP 并发上限和现有 IP 限流仍生效，但不能保证按工号的失败限制覆盖这种路径 | 暂缓改代码，保留为已知安全缺口，扩大使用范围前优先修复。后续将失败记账绑定实际认证完成，避免受请求取消影响，并保证只记一次；仅给现有计数调用换取消令牌不足以覆盖未进入失败处理分支的情况 |
| P2：快照扩容迁移存在并发写入窗口 | 复制旧列后、删除旧列前，若仍有审批或预开通写入，新行的 CLOB 临时列可能为空，最终丢失快照 | 本次不改迁移实现，以发布顺序控制：首次上线先迁移，再启动支持 OA 的新版；若已有写入者，先停止全部相关写入并等待在途事务结束。备份、迁移、数据核对、恢复服务的具体顺序见迁移手册“审计快照容量”一节 |
| P3：服务账号拒绝的日志分类不统一 | 管理员查人与员工登录路径的错误原因及日志级别不同；两条路径都按依赖故障处理，服务账号失败不计入员工凭据失败 | 暂缓统一日志分类，后续在服务账号 bind / 搜索边界集中处理，保持两个入口一致 |

**接下来的联调与发布验收**

1. 在 177 / 178 最终 Identity 容器验证服务账号查人、实际工号属性、`objectGUID`、员工 bind、严格 TLS 证书校验与原生依赖。验证主备故障切换和总预算；取消后的失败记账仍按上表作为已知限制记录。
2. 验证首次 OA 登录创建待业务审批用户并签发受限会话；预开通用户无需先登录；审批通过后复用同一 UserId；拒绝、禁用、删除后不能自动恢复；原 LOCAL 登录与改密仍正常。
3. 浏览器分别使用“仅有创建”“仅有审批”“具备创建、审批、角色和 SBU 分配权限”的账号验收入口与提交结果，并核对菜单、按钮、后端权限和 SBU 数据范围。
4. 按迁移手册在目标 schema 验证交付 SQL、最终迁移版本与 CLOB 字段，并保存现有快照迁移前后的核对记录。本地 Oracle 验证结果不能替代目标库验收。

本节只记录延期决定、操作前提与待验收事项，未新增构建、测试、真实 LDAP、浏览器或目标库迁移的通过结论。

### 10.8 登录资格与业务授权分离（2026-11-03）

本轮按产品确认调整：OA 认证通过即可登录系统外壳，未开通业务权限也有 UserId 和会话。新建用户为 Pending / Enabled，无默认角色和 SBU；应用中心提示“账号已创建，请联系管理员分配访问权限。”OA 与管理员登录页共用标题、说明和底部帮助文案，不在登录说明中单独强调 OA 或权限分配规则。

- `User.CanUseSession` 与 `HasBusinessAccess` 分开；Pending 可以维持会话，只有 Approved 才有业务资格。禁用、删除、拒绝仍拒绝登录。
- `AuthService` 的登录/刷新/me 支持受限会话；me 强制清空 Pending 的业务能力。`PermissionService` 在缓存前查询用户状态，`RoleRepository` 和 `UserSbuRepository` 查询均要求 Approved，预分配关系或旧管理员缓存不能放行。此状态查询增加权限判断的数据库读取，不再声称缓存命中完全不查用户表。
- 管理员预开通保留。审批通过/拒绝仍递增版本、写审计、撤销旧会话并使权限缓存失效；用户需重新登录。Pending 被显式禁用后，批准也保留禁用。Rejected 的显式重新审批恢复启用；Approved 不允许重新审批。
- 前端 OA 成功统一存会话；根据 me 的业务资格拦截直达地址、固定演示页、工作台和固定标签。应用中心无业务导航，侧栏不补工作台入口，角色及 SBU 分配沿用原有体系。
- 迁移 `20261103090000_AllowPendingLdapLogin` 只替换启用约束，不改任何用户行或授权，不批量恢复历史 Pending / Disabled。历史用户需要显式启用。Designer / Snapshot 已同步，SAJET 脚本为 `deploy/database/upgrade-allow-pending-ldap-login.sql`。先停止所有写入者、迁移、统一部署前后端、再启动。Down 有启用 Pending 时明确拒绝。
- 回归检查覆盖首次会话、重复登录接管、Pending 刷新/me、预分配管理员角色和旧缓存拒绝、SBU 拒绝、禁用 Pending 不自动恢复、审批/拒绝撤销会话、预创建 Pending 可登录以及 HTTP 403 与 Oracle 迁移模型。本轮 `dotnet build ZX.Platform.slnx --no-restore --disable-build-servers -m:1 /p:UseSharedCompilation=false` 为 0 警告 0 错误；完整 `AuthChecks` 输出 `All authentication checks passed.`，包含真实本机 Redis / HTTP、LDAP 替代适配器和离线 Oracle 模型/SQL 检查。未配置 Oracle 集成连接，未连接真实 Oracle 或 AD。

本轮未执行 Oracle 迁移，未做真实 AD、Gateway 和浏览器端到端。前端遵守仓库约定仅静态修改，构建、类型检查和浏览器验收由用户执行。
