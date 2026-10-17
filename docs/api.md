# Identity 接口契约

[文档导航](README.md) · [业务语义](identity.md) · [API 响应与错误](http.md)

这里记录当前 Controller 和 DTO 的 HTTP 契约：路由、HTTP 方法、请求字段、分页参数和错误负载。用例行为、状态规则与当前能力边界见[业务语义](identity.md)。

所有路径是 Identity 自身路径；经 Gateway 调用时在前面加 `/api/identity`，例如 `/api/identity/api/Users/getUserList`。

普通 JSON 接口统一返回 `ApiResponse`：创建成功 HTTP 201，其他成功 HTTP 200，业务 `code=0`。无数据的操作也返回 JSON，不使用带响应体的 204。响应结构、错误码、`traceId` 与取消语义见[API 响应与错误](http.md)。

## 认证接口

| HTTP | Identity 路径 | 凭据 / 输入 | 成功 `data` |
| --- | --- | --- | --- |
| POST | `/api/Auth/login` | JSON `{ account, password, replaceExistingSession? }`，匿名可调用 | `AccessTokenDto`，设置 Refresh Cookie |
| POST | `/api/Auth/refresh` | Refresh Cookie，Access 可过期 | `AccessTokenDto`，轮换 Cookie |
| POST | `/api/Auth/logout` | Refresh Cookie 或有效 Bearer；无凭据也成功 | 无，删除 Cookie |
| GET | `/api/Auth/me` | 有效 Bearer | `{ id, account, displayName, isAdministrator, roles, permissions, menus }` |
| PUT | `/api/Auth/changePassword` | 有效 Bearer，JSON `{ currentPassword, newPassword }` | 无，撤销会话并删除 Cookie |

Auth 的 POST / PUT 均须请求头 `X-Identity-CSRF: 1`，缺少或不匹配返回 `403 / 600005`。登录账号必填、最多 8 字符，密码必填、最多 72 UTF-8 字节；当前密码同样最多 72 字节，新密码至少 8 字符且最多 72 UTF-8 字节。参数错误返回 `400 / 600001`。密码不 Trim。

登录无效、账号不可登录或 Refresh 失效返回 `401 / 600401`，带本地化提示、traceId 及 `WWW-Authenticate: Bearer`；Refresh 失败还删除 Cookie。当前密码错误返回 `400 / 600001`，不撤销会话；改密成功后客户端须重新登录。

单账号只允许一处会话。正确密码登录但已有有效会话时返回 `409 / 600003`，`data={ reason: "AlreadyOnline" }`，提示使用后端 message；不改变原 Cookie / 会话。用户确认接管后，重新提交相同账号、密码和 `replaceExistingSession=true`，再次验密并替换旧会话。Redis 不可用返回 `503 / 600503`，不会被当成 401 或清除 Refresh Cookie。

登录 / 刷新成功示例（`accessToken` 是签名 JWT，前端直接作为 Bearer 使用）：

```json
{
  "code": 0,
  "message": "登录成功。",
  "data": {
    "tokenType": "Bearer",
    "accessToken": "<signed-jwt>",
    "accessTokenExpiresAtUtc": "2026-10-14T00:15:00+00:00",
    "refreshTokenExpiresAtUtc": "2026-10-21T00:00:00+00:00"
  }
}
```

Refresh 不在 JSON 中返回，使用 `zx_identity_refresh` 的 HttpOnly Cookie。会话默认 15 分钟 / 7 天绝对有效期、每次刷新替换旧 Access / Refresh、旧 Refresh 重放撤销对应会话。Cookie 属性、CSRF 原因、并发刷新要求和管理员配置见[登录与会话](authentication.md)。认证响应不缓存。

## 访问控制

受保护接口使用 `Authorization: Bearer <accessToken>`；先由标准 JwtBearer 验证 JWT，再检查 Redis 当前会话及 `jti`。缺少、签名无效、Issuer / Audience 不匹配、过期或撤销返回 `401 / 600401`，身份有效但不满足权限返回 `403 / 600005`。除匿名认证动作外，新接口默认要求登录。

用户、厂区及角色接口按功能权限授权，权限来自用户绑定的启用角色。`Users/updateDisplayName` 允许已登录用户修改自己的名称，修改他人需要 `identity.user.rename`。修改密码只通过 `Auth/changePassword` 本人验证旧密码。`Auth/me` 的 `roles` 是有效角色编码数组，`permissions` 是权限代码数组，内置管理员的数组包含全部当前权限。初始化、缓存与管理员保护见[角色与权限](authorization.md)。厂区数据范围授权仍待实现。

| 接口动作 | 所需权限 |
| --- | --- |
| Users/getUser、getUserList | `identity.user.read` |
| Users/add | `identity.user.create` |
| Users/setUserEnable、setUserDisable | `identity.user.status` |
| Users/logicDelete、batchDelete | `identity.user.delete` |
| Users/restore | `identity.user.restore` |
| Users/updateUserFactories | `identity.user.factories` |
| Users/forceLogout | `identity.user.forceLogout` |
| Factories/getFactory、getFactoryList | `identity.factory.read` |
| Factories/add | `identity.factory.create` |
| Factories/updateName | `identity.factory.rename` |
| Factories/setFactoryEnable、setFactoryDisable | `identity.factory.status` |
| Factories/logicDelete | `identity.factory.delete` |
| Factories/restore | `identity.factory.restore` |

`Factories/getAllEnabledFactories` 接受以下任一权限：`identity.factory.read`、`identity.user.read`、`identity.user.create`、`identity.user.factories`，用于用户查询及厂区选择器，无需额外授予厂区管理权限。

## 用户接口

| HTTP | Identity 路径                    | 输入                          | 成功 `data`       |
| ---- | -------------------------------- | ----------------------------- | ----------------- |
| POST | `/api/Users/add`                 | JSON `CreateUserDto`          | 新用户 Guid       |
| GET  | `/api/Users/getUser`             | query `id`                    | `UserDetailDto`   |
| GET  | `/api/Users/getUserList`         | query `UserQueryDto`          | `{ list, total }` |
| PUT  | `/api/Users/updateDisplayName`   | JSON `{ id, displayName }`    | 无                |
| PUT  | `/api/Users/setUserEnable`       | query `id`                    | 无                |
| PUT  | `/api/Users/setUserDisable`      | query `id`                    | 无                |
| POST | `/api/Users/forceLogout`         | query `id`                    | 无                |
| PUT  | `/api/Users/logicDelete`         | query `id`                    | 无                |
| PUT  | `/api/Users/batchDelete`         | JSON Guid 数组                | 无                |
| PUT  | `/api/Users/restore`             | query `id`                    | 无                |
| PUT  | `/api/Users/updateUserFactories` | JSON `{ userId, factoryIds }` | 无                |

注意软删除接口实际使用 **PUT `logicDelete`**，不是 HTTP DELETE。修改显示名称和厂区关系是两个独立用例，并非一个整体用户更新接口。

`forceLogout` 要求 `identity.user.forceLogout`，用户不存在返回 404；已有或已无会话均返回 200。只下线、不禁用，允许随后重新登录。禁用、删除会自动撤销会话，无需先调用下线接口。禁用及删除（含批量）不能包含操作者本人；操作其他内置管理员还要求操作者为内置管理员。并发竞争及 Redis 写入失败时数据库可能已提交的边界见[登录与会话](authentication.md#禁用删除和改密码)。

源码：[UsersController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/UsersController.cs)、[用户 DTO](../Services/Identity/ZX.Platform.Identity.Application/Users/Dtos)。

## 用户请求字段

| DTO / 字段                  | 当前校验                                          |
| --------------------------- | ------------------------------------------------- |
| `CreateUserDto.account`     | 必填，最多 8 个字符；服务 Trim 后比较标准化账号   |
| `CreateUserDto.displayName` | 必填，最多 100 个字符                             |
| `CreateUserDto.password`    | 必填，至少 8 个字符且最多 72 个 UTF-8 字节        |
| `CreateUserDto.factoryIds`  | 必填数组，至少 1 个；所有厂区必须存在且启用       |
| `UpdateUserDisplayNameDto`  | `id` 为 Guid；`displayName` 必填且最多 100 个字符 |
| `UpdateUserFactoryDto`      | `userId` 为 Guid；`factoryIds` 必填且至少 1 个    |

普通字符串长度约束按 .NET 字符计数；密码另用 `PasswordBytes` 统一限制最多 72 UTF-8 字节，中文等多字节字符按实际编码长度计算。密码交由 BCrypt 哈希，详情和分页都不返回密码或哈希。

创建示例：厂区 Guid 要替换为数据库中实际存在并启用的厂区。

```json
{
  "account": "ZX001",
  "displayName": "示例用户",
  "password": "Example123!",
  "factoryIds": ["11111111-1111-1111-1111-111111111111"]
}
```

`factoryIds` 应提交不重复的厂区 Guid。当前 Service 用查询返回数量与输入数量比较，重复或不可用的厂区都可能返回 `404 / 600002`，没有单独的重复数组元素错误码。Guid 类型能够绑定不代表已经验证 `Guid.Empty`；不要假定 `[Required]` 会拒绝空 Guid 值。

## 厂区接口

| HTTP | Identity 路径                           | 输入                  | 成功 `data`          |
| ---- | --------------------------------------- | --------------------- | -------------------- |
| POST | `/api/Factories/add`                    | JSON `{ code, name }` | 新厂区 Guid          |
| GET  | `/api/Factories/getFactory`             | query `id`            | `FactoryDetailDto`   |
| GET  | `/api/Factories/getFactoryList`         | 无                    | `FactoryDetailDto[]` |
| PUT  | `/api/Factories/updateName`             | JSON `{ id, name }`   | 无                   |
| PUT  | `/api/Factories/setFactoryEnable`       | query `id`            | 无                   |
| PUT  | `/api/Factories/setFactoryDisable`      | query `id`            | 无                   |
| PUT  | `/api/Factories/logicDelete`            | query `id`            | 无                   |
| PUT  | `/api/Factories/restore`                | query `id`            | 无                   |
| GET  | `/api/Factories/getAllEnabledFactories` | 无                    | `FactoryDetailDto[]` |

创建时 `code` 必填、最多 50 个字符，Trim 后转大写；`name` 必填、最多 200 个字符。修改名称使用相同名称约束。厂区编码没有修改接口。

源码：[FactoriesController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/FactoriesController.cs)、[厂区 DTO](../Services/Identity/ZX.Platform.Identity.Application/Factories/Dtos)。

## 角色与权限接口

| HTTP | Identity 路径 | 输入 | 成功 `data` | 权限 |
| --- | --- | --- | --- | --- |
| GET | `/api/Roles/getRoleList` | 无，不分页 | `RoleDto[]` | `identity.role.read` |
| GET | `/api/Roles/options` | 无 | `{ id, code, name, isEnabled, isBuiltIn }[]` | `identity.user.roles` |
| POST | `/api/Roles/add` | JSON `{ code, name, description? }` | 新角色 Guid | `identity.role.create` |
| PUT | `/api/Roles/update` | JSON `{ id, name, description? }` | 无 | `identity.role.update` |
| PUT | `/api/Roles/setRoleEnable` | query `id` | 无 | `identity.role.status` |
| PUT | `/api/Roles/setRoleDisable` | query `id` | 无 | `identity.role.status` |
| PUT | `/api/Roles/logicDelete` | query `id` | 无 | `identity.role.delete` |
| GET | `/api/Roles/permissions` | 无 | `{ code, group, name, groupName }[]` | `identity.role.permissions` |
| GET | `/api/Roles/permissionTree` | 无 | `MenuDto[]`，菜单分组及权限叶子 | `identity.role.permissions` |
| PUT | `/api/Roles/updatePermissions` | JSON `{ roleId, permissionCodes }` | 无 | `identity.role.permissions` |
| GET | `/api/Roles/userRoles` | query `userId` | Guid 数组 | `identity.user.roles` |
| PUT | `/api/Roles/updateUserRoles` | JSON `{ userId, roleIds }` | 无 | `identity.user.roles` |

`RoleDto` 包含 `id`、`code`、`name`、`description`、`isEnabled`、`isBuiltIn`、`createdAtUtc`、`permissionCodes`。列表排除软删除角色，内置角色优先、再按编码排序。角色编码必填、最多 50 字符，首字符为英文字母，其余为字母/数字/下划线，保存为大写；不允许修改编码，已删除编码不能复用。名称必填且最多 200 字符，描述最多 500 字符。

`permissionCodes` 和 `roleIds` 必须是数组，分别最多 500 / 200 项；空数组清空绑定，重复值自动去重。权限代码必须来自当前目录。新增绑定的角色必须存在、未删除且启用，已有停用角色可保留或移除；停用角色不产生有效权限。`userRoles` 返回未删除角色关系，包含已有停用角色；不存在用户返回 404。权限及分组名称按请求语言返回。

内置 `ADMINISTRATOR` 角色不能编辑、停用、删除或调整权限，相关操作返回 `409 / 600004`。只有内置管理员能增删他人的内置管理员角色（否则 403），且不能移除自己的管理员角色。其他参数及状态错误沿用统一业务码与后端本地化 message。

源码：[RolesController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/RolesController.cs)、[角色 DTO](../Services/Identity/ZX.Platform.Identity.Application/Roles/Dtos)。

## 菜单接口

| HTTP | Identity 路径 | 输入 | 成功 `data` | 权限 |
| --- | --- | --- | --- | --- |
| GET | `/api/Menus/getMenuTree` | 无，不分页 | 完整管理树 `MenuDto[]` | `identity.menu.read` |
| GET | `/api/Menus/options` | 无 | 完整树，供上级选择 | `identity.menu.create` 或 `identity.menu.update` |
| GET | `/api/Menus/permissions` | 无 | `{ code, name }[]`，当前有效权限 | `identity.menu.create` 或 `identity.menu.update` |
| POST | `/api/Menus/add` | JSON `CreateMenuDto` | 新菜单 Guid | `identity.menu.create` |
| PUT | `/api/Menus/update` | JSON `UpdateMenuDto` | 无 | `identity.menu.update` |
| PUT | `/api/Menus/setEnabled` | query `id, enabled` | 无 | `identity.menu.status` |
| PUT | `/api/Menus/logicDelete` | query `id` | 无 | `identity.menu.delete` |

`MenuDto` 字段：`id`、可空 `parentId`、`code`、`kind`、`title`、`titleEn`、`icon`、`path`、`component`、`permissionCode`、`sort`、`keepAlive`、`showInMenu`、`showInTags`、`affix`、`isEnabled`、`isBuiltIn`、`children`。父级优先，再按 sort、code 排序。`Auth/me.menus` 使用相同结构，但只含启用且有查看权限的目录/页面，不含操作节点；隐藏导航的页面仍返回以注册路由，不是管理全量树。

创建输入为 `code` 加上述可维护字段；修改为 `id` 加可维护字段，不修改 code。编码必填，最多100字符，以字母开头，其余字母/数字/下划线/连字符；删除后也不能复用。kind 必须是 `directory` / `page` / `action`，title 必填且最多200字符，titleEn 最多200字符；icon/component/permissionCode 最多100，path 最多200；sort 为0～10000；keepAlive、showInMenu、showInTags 为 bool，省略时默认 true；affix 为 bool，默认 false。

| 显示字段 | 作用 |
| --- | --- |
| `showInMenu` | 目录/页面是否显示在导航；false 仅隐藏入口，仍返回用户有权访问的页面路由。隐藏目录同时隐藏下级入口，空导航目录不展示。 |
| `showInTags` | 页面是否显示标签；false 映射为前端 hideInTags，同时不缓存该页面。 |
| `keepAlive` | 页面是否缓存；仅在 showInTags=true 时生效。 |
| `affix` | 页面是否固定标签；true 时必须是 page 且 showInTags=true，否则返回 400。 |

操作节点不生成导航或页面，上述显示配置不参与授权。停用节点会从 Auth/me 的树中排除，页面路由也随之移除；这与 showInMenu=false 只隐藏导航不同。

目录不能填路由、组件、权限；页面须填写绝对路径、登记组件及已声明权限；操作须挂在页面下且无路由、组件。目录/页面仅允许根或目录父级，不能自引用、产生循环或移动到过深层级。路径仅允许由字母/数字/下划线/连字符组成的绝对路径段，不能覆盖 `/login`、`/overview`、`/examples/users`（忽略大小写）；组件限 `SystemUser` / `SystemFactory` / `SystemRole` / `SystemMenu`。图标为空或 `svg:name` / `ep:Name`，名称同样只含字母/数字/下划线/连字符且以字母开头。

未知权限、非法层级/字段、带子节点删除返回 `400 / 600001`；编码/路径/操作权限重复返回 `409 / 600003`；不存在返回404；内置节点改结构、启停或删除返回 `409 / 600004`。内置节点仅允许维护 title、titleEn、icon、sort。页面路径、操作权限只在未删除的同类节点中唯一，页面与操作可关联相同查看权限。

`Roles/permissionTree` 临时补齐页面查看权限叶子，并将未挂菜单的有效权限放到“未归类权限”；虚拟节点只用于角色授权，不作为菜单 id 调用维护接口。保存角色仍提交权限代码数组，不提交菜单 ID。菜单停用/删除不撤销接口授权，详见[动态菜单与授权树](authorization.md#动态菜单与授权树)。

## 分页与结果

只有用户列表使用分页；厂区与角色列表不分页，分别直接返回全部未删除记录。

| 参数             | 默认值 / 范围                        | 用户列表                 |
| ---------------- | ------------------------------------ | ------------------------ |
| `page`           | 默认 1，1～`int.MaxValue`            | 支持                     |
| `pageSize`       | 默认 20，1～10000                    | 支持                     |
| `keyword`        | 可选，Trim 后搜索                    | 账号或显示名称包含关键字 |
| `isEnabled`      | 可选 bool；省略表示不筛状态          | 支持                     |
| `factoryId`      | 可选 Guid                            | 按关系筛选               |
| `createdFromUtc` | 可选 DateTimeOffset，ISO 8601 带偏移 | 创建时间下界，包含该时刻 |
| `createdToUtc`   | 可选 DateTimeOffset，ISO 8601 带偏移 | 创建时间上界，不包含该时刻 |

用户列表的页码 / 页大小越界、绑定失败和分页偏移量超过 `int.MaxValue` 返回 `400 / 600001`。用户列表和厂区列表都按创建时间倒序，再按 Id 排序。普通列表排除软删除数据。

创建时间范围由调用方将当地时间换算成 UTC 后提交，例如北京时间 `2026-10-13` 全天传 `createdFromUtc=2026-10-12T16:00:00.000Z`、`createdToUtc=2026-10-13T16:00:00.000Z`。仓储将参数归一为 UTC，按 `CreatedAtUtc >= createdFromUtc` / `CreatedAtUtc < createdToUtc` 过滤，分页和 total 使用同一组条件；不转换数据库列。两个边界都可单独省略；同时提供时开始必须早于结束，否则返回 `400 / 600001` 和 `User.CreatedAtRangeInvalid` 多语言提示。日期选择器若要包含结束日全天，应提交该日期次日当地零点对应的 UTC 上界。

```json
{
  "code": 0,
  "message": "用户分页列表获取成功。",
  "data": {
    "list": [],
    "total": 0
  }
}
```

厂区列表的 `data` 直接是 `FactoryDetailDto` 数组，没有 `list` / `total` 包裹。

`message` 是展示提示，以当前语言资源为准，不作为固定协议字符串。

`UserDetailDto` 字段为 `id`、`account`、`displayName`、`isEnabled`、`createdAtUtc`、可空 `updatedAtUtc` 和 `factoryIds`。`FactoryDetailDto` 为 `id`、`code`、`name`、`isEnabled`、`createdAtUtc` 和可空 `updatedAtUtc`。空字段可被序列化省略；Guid 是字符串，时间字段数据库存 UTC，接口以带 `Z` / `+00:00` 偏移的 ISO 8601 输出，见[时间字段格式](http.md#时间字段格式)。

启用厂区列表排除已删除和已停用厂区；`getFactoryList` 返回全部未删除厂区（含已停用），两者都不分页，后者按创建时间倒序、再按 Id 排序。用户详情的 `factoryIds` 表示保存的关系，不保证这些厂区现在仍启用。

## 批量软删除

PUT `/api/Users/batchDelete` 的请求体直接使用用户 Guid 数组，不包装为 `{ ids: [...] }`：

```json
[
  "11111111-1111-1111-1111-111111111111",
  "22222222-2222-2222-2222-222222222222"
]
```

空数组由 Service 返回 `400 / 600001`，提示键为 `User.BatchDeleteNoIds`；Service 同时防御 null 列表。无效 Guid 字符串或无法绑定的请求体由 MVC 返回 `400 / 600001`。成功返回 `200 / 0`，使用 `User.BatchDeleted` 中英文提示，不返回删除数量。仓储一次查询未删除用户，Service 调用 `MarkDeleted()`，最后一次 `SaveChangesAsync` 提交全部已查询到的实体变更。数据影响和未命中 ID 的处理见[批量软删除](identity.md#批量软删除)。

## 创建冲突

创建前查询包含软删除数据，同一账号 / 编码不能因软删除后重新创建。

| 情况                     | HTTP / code    | 当前附加 `data`                               |
| ------------------------ | -------------- | --------------------------------------------- |
| 用户账号已删除           | `409 / 600003` | `{ reason: "AccountDeleted", userId }`        |
| 用户账号已禁用           | `409 / 600003` | `{ reason: "AccountDisabled", userId }`       |
| 用户账号正常存在         | `409 / 600003` | 无                                            |
| 厂区编码已删除           | `409 / 600003` | `{ reason: "FactoryCodeDeleted", factoryId }` |
| 厂区已禁用               | `409 / 600003` | `{ reason: "FactoryDisabled", factoryId }`    |
| 厂区编码正常存在         | `409 / 600003` | 无                                            |
| 保存时数据库唯一约束冲突 | `409 / 600003` | 无，通用重复提示                              |

当前响应没有 `allowedAction`、`canRestore` 或 `canEnable`。正常存在时也没有 `AccountExists` / `FactoryCodeExists` 的 `data.reason`；这些曾出现在早期设计示例中，不能当成已经实现的字段。调用方依据真实 code 和可选的结构化数据处理。

## 相关文档

- [业务语义](identity.md)：状态变化、厂区关系与当前能力边界。
- [API 响应与错误](http.md)：响应结构、错误码、`traceId` 与取消信号。
- [数据库与持久化](database.md)：唯一索引、软删除过滤与审计字段。
