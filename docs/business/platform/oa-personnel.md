# OA 人员同步与组织视图

## 功能与范围

由有权限的用户在右上角用户下拉中打开“同步OA人员”，手动将 LDAP 中归属 BU17 的目录人员同步到 MOM 用户表 `ZX_IDENTITY_USER`，并查看部门人员树、人员汇报树及人员详情。新人员同步后直接出现在用户管理中，管理员无需逐个创建账号。前端操作说明见 `ZX.front.vue` 仓库同名文档。

新增用户为 LDAP 认证类型，MOM 账号与工号一致，默认 `Pending` 待审批，不保存或生成员工密码，不自动分配角色或 SBU。目录状态为 `Enabled` 才启用 MOM 用户，`Disabled` 或 `Unknown` 时创建为禁用。员工使用本人的 OA 密码登录，待审批账号沿用现有受限外壳，审批通过并授予角色、SBU 后才能访问业务。

同步范围为 BU17 共用账号与组织资料，不随当前 SBU 切换；接口有独立的查看和同步权限，不向所有已登录用户开放。组织资料作为用户的可空 OA 元数据保存，不参与认证或授权判定。

本机网络无法直连 LDAP 时，支持通过 [远程 LDAP 模式](../../directory-relay.md) 开发：服务器只读取目录并返回人员，本机原同步用例负责去重、待审批账号建档、组织资料和审计入库，目标为本机配置的开发 schema。目录接口随同一后端包发布到 177、178，用独立密钥校验请求；本机在 `appsettings.Development.json` 配置远程调用，线上继续直连 LDAP。此模式不改变下述去重、补资料、审批、权限或 SBU 规则。

## 同步规则

- 服务端使用独立的只读服务账号，按部门字段的精确 BU 后缀读取，不只查询名为 BU17 的 OU。HTTP 请求不接受目录服务器、凭据、BU、BaseDn 或 LDAP 过滤器。
- 必须完整读完同一服务器全部分页，才开始入库。连接失败可从备用节点重新读取；目录超时、截断、referral、数量超限均不提交半批人员。
- 按全系统用户去重，包含软删除记录及其他 BU；标准化 MOM 登录账号、工号或同来源 objectGUID 任一已占用即跳过，批次内同样去重。MOM 登录账号固定使用目录工号，目录登录名另外保存在 `OA_ACCOUNT`；工号保留字母和前导零，不把纯数字账号当作人员筛选条件。
- 已有账号保留账号主体、认证身份、密码、显示名称、审批、启用、锁定、安全版本、角色和 SBU。仍占用标准化登录名的本地账号、未绑定的预开通 OA 账号、已删除的 OA 账号及身份冲突账号均跳过。已删除 OA 用户的工号和来源/GUID 继续占位。已删除本地用户若已按原有规则把 `NormalizedAccount` 释放为 `DELETED:LOCAL:...`，原登录名可创建独立的新 OA 用户，不继承旧用户 ID、角色或 SBU。同步本身不调用 `ReleaseDeletedLocalAccountName`，也不自动释放尚未释放的历史本地账号名，不重新绑定或恢复旧账号。
- 有一项首次补资料规则：已有用户必须为未删除、已绑定的 LDAP 用户，来源和 GUID 匹配，工号与目录返回值按区分大小写的 `Ordinal` 完全一致，且 `OaSyncedAtUtc` 为空，才补一次组织元数据。此次仅填充 OA 字段，不改变原用户主体及授权。`profileAddedCount` 记录这部分数量，是 `skippedCount` 的子集，不计入新增账号数。
- 首次补资料以 `OaSyncedAtUtc` 作为并发令牌，两个请求争用同一份空资料时返回 409，防止后保存者覆盖已写入的资料；此操作不改变 `SecurityVersion`，不使已有会话失效。
- 已有 OA 元数据后再次同步全部跳过，不更新部门、主管、邮箱或目录状态快照。初次因目录禁用或未知而创建为禁用的用户，即使之后目录恢复正常，也需管理员显式启用。后续若需要持续调岗或状态同步，应增加独立更新规则。
- 缺少有效身份、账号、工号、部门或字段超长的记录计入无效数量，不截断后冒充有效数据。新用户工号最长 32、用户显示名称最长 100；完整目录显示名可存于最长 256 的 OA 元数据。其余字段长度以实体和映射为准。
- 全批新增与首次补资料通过单次保存提交。新增用户同批追加现有 `UserApprovalAudit` 的 `Provisioned` 事件，记录建档操作；该事件不是审批通过或业务授权。沿用用户表的账号、工号、来源及 GUID 唯一约束防止并发重复；如两个请求竞争同一账号或首次组织资料，冲突请求本次写入整批回滚并返回 409。重新同步后已有账号会被跳过。
- `readCount = addedCount + skippedCount + invalidCount`，`0 <= profileAddedCount <= skippedCount`；`totalCount` 是该 BU 已保存 OA 元数据且未删除的用户数。数量代表目录账号记录，不代表已核实的 HR 在职人数。

## 部门与人员关系

部门字段按“部门名称,原始编码,BU”解析，保留原始值。编码作为源目录分组信息保存，尚未认定为企微部门 ID。组织视图读取用户表中属于当前 BU、已保存 OA 元数据且未删除的用户，包含待审批和禁用用户，并保留目录状态标记；尚未补组织资料的已有账号不会凭姓名猜测部门。人员 ID 就是 MOM UserId。主管引用以相同目录来源和完整 DN 匹配，不按姓名猜测，不向上扩大范围读取其他 BU 人员。

部门视图按部门分组，在本部门内按主管关系展开人员；跨部门主管关系在人员汇报视图展示，不据此生成未经确认的“处 → 部”隶属。缺失主管、范围外主管和循环关系分别标注。自引用及多人循环的父边被切断，人员仍保留在树中。接口以人员平面集合与 `managerId` 交付，避免深层树导致 JSON 序列化失败。

`directoryStatus` 为 `Enabled` / `Disabled` / `Unknown`，仅表示同步时的目录账号状态快照。`managerRelation` 为 `Resolved` / `Missing` / `None` / `Cycle`。`lastSyncedAtUtc` 展示为最近一次**组织资料入库**的时间，包含新建用户与首次补资料；没有新增或补资料的全跳过同步不会更新它。接口返回 UTC ISO 时间，未提供的可空字段可能因统一 JSON 设置而省略。

## 接口与权限

| 方法与路径 | 权限 | 返回 |
| --- | --- | --- |
| `GET /api/OaPersonnel/tree` | `identity.personnel.read` | `businessUnit, total, departmentCount, lastSyncedAtUtc, people[]` |
| `POST /api/OaPersonnel/sync`（无请求体） | `identity.personnel.sync` | `readCount, addedCount, skippedCount, profileAddedCount, invalidCount, totalCount, syncedAtUtc` |

网关路径加 `/api/identity` 前缀。人员的 `id` 为 MOM 用户 ID，`account` 为 MOM 登录账号（工号）。返回沿用 `ApiResponse<T>`，成功提示包含实际新增、跳过、首次补资料和无效数量。未配置/目录不可达/不完整结果返回 503，唯一键或首次补资料并发冲突返回 409，权限不足返回 403。没有 `[AiQuery]`，暂不向 AI 工具目录开放。

在菜单管理现有“系统管理 → 用户管理”页面下增加两个操作节点，组件留空，供角色授权；无需新增页面或修改内置菜单：

| 权限 | 中文标题 | 英文标题 | 越南语标题 |
| --- | --- | --- | --- |
| `identity.personnel.read` | 查看OA人员组织 | View OA personnel organization | Xem cơ cấu nhân sự OA |
| `identity.personnel.sync` | 同步OA人员 | Sync OA personnel | Đồng bộ nhân sự OA |

同时授予二者的角色可同步并查看结果；只有查看权限可浏览，只有同步权限可创建缺失的待审批 OA 用户、补符合条件的首次组织资料并查看本次数量，但不读取完整人员树。新权限由现有初始化流程登记，角色和菜单配置由负责人在目标环境完成，不通过迁移修改共享授权。

用户管理沿用原有筛选：新同步用户没有 SBU，按某个 SBU 筛选时不会显示，查看本批新增账号需选“不限 SBU”。同步不会为显示账号而自动授予当前 SBU。

## 数据库与部署

在现有 `ZX_IDENTITY_USER` 增加以下 13 个可空字段，存量用户不回填虚构组织资料：

| 字段 | 用途及映射 |
| --- | --- |
| `OA_ACCOUNT` | 源目录登录名，最长 100 |
| `OA_NAME` | 人员姓名，最长 200 |
| `OA_DISPLAY_NAME` | 源目录完整显示名，最长 256 |
| `OA_EMAIL` | 邮箱，最长 320 |
| `OA_COMPANY` | 公司，最长 256 |
| `OA_BUSINESS_UNIT` | BU，最长 64 |
| `OA_DEPARTMENT_NAME` | 部门名称，最长 256 |
| `OA_DEPARTMENT_CODE` | 原始部门编码，最长 100 |
| `OA_DEPARTMENT_RAW` | 原始部门字段，最长 1000 |
| `OA_DISTINGUISHED_NAME` | 完整目录 DN，`NVARCHAR2(2000)`，最长 2000 |
| `OA_MANAGER_DN` | 主管 DN，`NVARCHAR2(2000)`，最长 2000 |
| `OA_DIRECTORY_STATUS` | 目录状态快照，最长 16 |
| `OA_SYNCED_AT_UTC` | 首次组织资料入库时间及并发令牌，`TIMESTAMP(7) WITH TIME ZONE` |

增加索引 `IX_ZID_USER_OA_BU_DEPT(OA_BUSINESS_UNIT,OA_DEPARTMENT_CODE)`，身份唯一性沿用现有 `UX_ZID_USER_ACCOUNT`、`UX_ZID_USER_EMPLOYEE_NO`、`UX_ZID_USER_LDAP_IDENTITY`。`CREATE_TIME` 及 UTC 审计字段继续采用用户表既有约定，不替代组织资料入库时间。此次不增加独立人员表，也不增加角色或 SBU 的物理外键。

服务端配置见 [OA 人员同步配置](oa-personnel-configuration.md)。数据库变更由 `20261111081842_AddOaPersonnelToUsers` 迁移、Designer 和模型快照交付；此前未执行的独立人员表迁移已撤销生成结果，历史迁移文件未变动。2026-11-11 经用户授权，已通过 development 入口将该迁移应用到本机配置的 `SAJET` 测试库并完成结构核验。正式库本次未操作，发布时仍由负责人应用迁移，给两台 Identity 配置相同来源及受保护的查询账号、配置角色权限，再更新前后端。应用启动不自动更新表结构、不连接 LDAP、不执行同步。

后续 `20261112062603_UseScalarOaDirectoryPaths` 迁移将上面两个 DN 字段从 `NCLOB` 转为普通 Unicode 字符串 `NVARCHAR2(2000)`，领域校验上限同步从 2048 调整为 2000，不再通过 LOB 读取这两个字段。Oracle 19c 标准字符串还受 4000 字节上限约束；迁移会在任何 DDL 前检查全部旧值的字符及 national charset 字节长度，超长或非 NULL 的零长度 LOB 会报错停止，不截断数据。两列临时字符串列完整复制并逐值比对成功后，才替换原列；回退同样复制并校验后转回 `NCLOB`。

执行该迁移前备份用户表，并停止所有使用此 schema 的 Identity 实例及其他写入。Oracle DDL 隐式提交，不能依靠迁移事务整体回滚；若执行失败，保留现场，检查临时列及当前列类型后人工处理，不自动重跑。测试库和正式库分别经负责人安排执行迁移后，再启动匹配的新后端；仅部署代码不会转换现有数据库字段。

批量同步的 LDAP 总预算默认 90 秒。前端请求与网关 Identity 集群空闲超时均为 180 秒；原生 Nginx 模板仅对同步 POST 路径设置 180 秒并关闭上游重放。现有自定义 Nginx 配置需要核对相同路径的超时；前端仓库 HTTP 模板的 `/api/` 超时为 900 秒，可覆盖此预算。延长目录预算时仍需为数据库提交留出余量。

## 验证情况

2026-11-12：已生成 `20261112062603_UseScalarOaDirectoryPaths` 及配套 Designer、快照，完成两列模型与领域长度一致性、Up / Down 数据复制和空值校验的静态检查。迁移生成所需的 Identity.Api 编译通过，0 警告、0 错误；增量 SQL 及现有发布包装脚本均离线生成成功。未执行测试、数据库迁移或真实接口验证，本次数据库类型尚未改变。

2026-11-11：已静态核对 LDAP 范围筛选、分页完整性、目录失败边界、用户去重与现有登录兼容规则。Identity.Api 编译通过，0 警告、0 错误，并生成 `20261111081842_AddOaPersonnelToUsers`；检查 Up / Down 和模型快照仅涉及本次用户 OA 元数据字段、索引及约束。三语资源的键和占位符一致，前后端差异无空白错误。

同日经用户授权，先只读确认 `SAJET` 测试库已有之前全部 10 条迁移、用户表 23 列且无 OA 字段，再定向执行本次 EF `database update ... -- --development`，返回 `Done`。执行后核验迁移历史已新增目标记录，用户表共 36 列，其中 13 个 OA 字段的类型、长度、可空性符合迁移；索引 `IX_ZID_USER_OA_BU_DEPT` 为 VALID，状态约束 `CK_ZID_USER_OA_STATUS` 为 ENABLED / VALIDATED，未创建独立人员表。未执行真实人员同步、单元测试、前端编译或浏览器验收；本次未操作正式库。

部署后仍需验证 LDAP 库依赖及连通性、只读账号权限、新增用户默认待审批及同批建档审计、目录非启用用户的禁用状态、占位账号跳过、已释放本地原名创建独立 OA 用户、符合条件的已绑定账号首次补资料及并发争用、未绑定预开通账号保持未绑定、重复同步全跳过、受限角色、本人 OA 登录及树视图。尤其需确认旧账号的审批、密码、角色、SBU、安全版本和会话状态不被同步改变。

## 开发依据

- `Services/Identity/ZX.Platform.Identity.Application/Personnel/`
- `Services/Identity/ZX.Platform.Identity.Domain/Entities/User.cs`
- `Services/Identity/ZX.Platform.Identity.Infrastructure/Security/LdapPersonnelDirectory.cs`
- `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- `Services/Identity/ZX.Platform.Identity.Api/Controllers/OaPersonnelController.cs`
