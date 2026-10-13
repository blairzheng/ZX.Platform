# Identity 接口契约

[文档导航](README.md) · [业务语义](identity.md) · [API 响应与错误](http.md)

这里记录当前 Controller 和 DTO 的 HTTP 契约：路由、HTTP 方法、请求字段、分页参数和错误负载。用例行为、状态规则与当前能力边界见[业务语义](identity.md)。

所有路径是 Identity 自身路径；经 Gateway 调用时在前面加 `/api/identity`，例如 `/api/identity/api/Users/getUserList`。

普通 JSON 接口统一返回 `ApiResponse`：创建成功 HTTP 201，其他成功 HTTP 200，业务 `code=0`。无数据的操作也返回 JSON，不使用带响应体的 204。响应结构、错误码、`traceId` 与取消语义见[API 响应与错误](http.md)。

## 用户接口

| HTTP | Identity 路径                    | 输入                          | 成功 `data`       |
| ---- | -------------------------------- | ----------------------------- | ----------------- |
| POST | `/api/Users/add`                 | JSON `CreateUserDto`          | 新用户 Guid       |
| GET  | `/api/Users/getUser`             | query `id`                    | `UserDetailDto`   |
| GET  | `/api/Users/getUserList`         | query `UserQueryDto`          | `{ list, total }` |
| PUT  | `/api/Users/updateDisplayName`   | JSON `{ id, displayName }`    | 无                |
| PUT  | `/api/Users/setUserEnable`       | query `id`                    | 无                |
| PUT  | `/api/Users/setUserDisable`      | query `id`                    | 无                |
| PUT  | `/api/Users/updatePassword`      | JSON `{ id, newPassword }`    | 无                |
| PUT  | `/api/Users/logicDelete`         | query `id`                    | 无                |
| PUT  | `/api/Users/batchDelete`         | JSON Guid 数组                | 无                |
| PUT  | `/api/Users/restore`             | query `id`                    | 无                |
| PUT  | `/api/Users/updateUserFactories` | JSON `{ userId, factoryIds }` | 无                |

注意软删除接口实际使用 **PUT `logicDelete`**，不是 HTTP DELETE。修改显示名称和厂区关系是两个独立用例，并非一个整体用户更新接口。

源码：[UsersController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/UsersController.cs)、[用户 DTO](../Services/Identity/ZX.Platform.Identity.Application/Users/Dtos)。

## 用户请求字段

| DTO / 字段                  | 当前校验                                          |
| --------------------------- | ------------------------------------------------- |
| `CreateUserDto.account`     | 必填，最多 8 个字符；服务 Trim 后比较标准化账号   |
| `CreateUserDto.displayName` | 必填，最多 100 个字符                             |
| `CreateUserDto.password`    | 必填，至少 8 个字符；当前 DTO 没有声明最大长度    |
| `CreateUserDto.factoryIds`  | 必填数组，至少 1 个；所有厂区必须存在且启用       |
| `UpdateUserDisplayNameDto`  | `id` 为 Guid；`displayName` 必填且最多 100 个字符 |
| `UpdateUserPasswordDto`     | `id` 为 Guid；`newPassword` 必填，8～72 个字符    |
| `UpdateUserFactoryDto`      | `userId` 为 Guid；`factoryIds` 必填且至少 1 个    |

当前 DTO 的字符串长度约束按 .NET 字符计数；不要把修改密码的 72 字符注解理解为所有密码入口统一的字节上限。密码交由 BCrypt 哈希，详情和分页都不返回密码或哈希。

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

## 分页与结果

只有用户列表使用分页；厂区列表不分页，`getFactoryList` 直接返回全部未删除厂区。

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
