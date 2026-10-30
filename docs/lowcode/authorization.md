# 权限与数据范围

更新于 2026-10-30。分两层，不能合并：**功能权限**决定能不能进、能不能点；**数据范围**决定进去后能看到哪些行。

## 1. 功能权限：复用现有模型

低代码不新建权限体系，直接把权限码写进现有的 `PERMISSION` 表，把动作节点写进 `MENU` 表。角色授权界面、权限树、前端按钮鉴权**全部零改动**——因为它们读的就是这两张表。

### 权限码规则

```
{app}.{entity}.{action}        实体操作
{app}.{page}.read              页面访问
```

`action` 取值：`read` `create` `update` `delete` `export`，以及全局专用的 `read.all`。

示例（实体 `station`，应用 `mes`）：

```
mes.station.read
mes.station.create
mes.station.update
mes.station.delete
mes.station.export
mes.station.read.all      ← 跨 SBU 查看，默认不授予任何角色
```

### 必须改的两列

现有 `PERMISSION` 只有 `CODE` + `IS_ACTIVE`，无法区分"系统内置"和"低代码动态注册"。加两列：

```sql
ALTER TABLE ZX_IDENTITY.ZX_IDENTITY_PERMISSION ADD (
  SOURCE      NVARCHAR2(16) DEFAULT 'system' NOT NULL,
  OWNER_CODE  NVARCHAR2(100)
);

UPDATE ZX_IDENTITY.ZX_IDENTITY_PERMISSION SET SOURCE = 'system' WHERE SOURCE IS NULL;
```

| 列 | 用途 |
| --- | --- |
| `SOURCE` | `system`（编译期常量）/ `lowcode`（运行时注册） |
| `OWNER_CODE` | 归属对象，如 `mes.station`；退役时按它精准清理 |

`OWNER_CODE` 是退役能力的前提。没有它，下线一个实体只能靠 `LIKE 'mes.station.%'` 猜，早晚删错。

### 初始化逻辑必须改（否则动态权限活不过一次重启）

现状：

```csharp
var definitions = IdentityPermissions.All.ToHashSet(StringComparer.Ordinal);
foreach (var permission in stored) permission.SetActive(definitions.Contains(permission.Code));
```

`stored` 里包含所有权限行，凡是不在编译期常量里的都会被停用。低代码注册的权限，**重启一次就全废**，而且不报错、不打日志，表现是"菜单在、按钮全灰"。

改成只裁剪系统权限：

```csharp
var definitions = IdentityPermissions.All.ToHashSet(StringComparer.Ordinal);
foreach (var permission in stored.Where(p => p.Source == PermissionSource.System))
    permission.SetActive(definitions.Contains(permission.Code));
```

必须配一个测试：造一条 `SOURCE='lowcode'` 的权限，跑一次初始化，断言它仍然 active。

### 菜单注册

发布实体 / 页面时，通过 `IMenuRegistrar` 写入菜单行：

| 节点 | kind | component | permissionCode |
| --- | --- | --- | --- |
| 应用目录 | `directory` | — | — |
| 实体页面 | `page` | `LowCodePage` | `{app}.{entity}.read` |
| 操作节点 ×5 | `action` | — | `{app}.{entity}.{action}` |

规则：

- **只补不改**。管理员改过标题、图标、排序后，再次发布不覆盖（沿用 `DefaultMenus.AddMissing` 的语义）。
- 业务页面**共用 `LowCodePage` 一个组件**，页面 code 从 path 末段取。白名单永远只加这一项，不需要每页一个组件。
- 菜单行标记 `OWNER_CODE`，退役时按归属清理。

## 2. 数据范围：按 SBU

### 实体声明

`scopeMode` 在实体定义里固定，页面配置无权修改：

| 值 | 含义 | 第一期 |
| --- | --- | --- |
| `none` | 全局实体，不按 SBU 隔离（字典、参数表） | ✅ |
| `column` | 表里有 `SBU_ID`，按当前 SBU 过滤 | ✅ |
| `relation` | 通过父表 / 关联表推导归属 | ❌ 不实现 |

**`relation` 明确不做。** 推导出来的归属在 MES 场景最容易出错，而"无法判定归属的记录不得默认返回"是硬要求。确有需要时，由业务方写 handler 提供受控数据集，而不是开放一个推导配置。

### 读写规则

| 场景 | 规则 |
| --- | --- |
| 查询 `column` 实体 | 追加 `SBU_ID = :sbuId`，`sbuId` 来自服务端已验证的 `CurrentSbuContext` |
| 查询 `none` 实体 | 不追加条件，不要求 `X-SBU-ID` |
| 新增 `column` 实体 | 服务端写入 `SBU_ID = 当前 SBU`，忽略客户端传值 |
| 修改 `column` 实体 | `SBU_ID` 只读；跨 SBU 搬迁不在表单能力内 |
| 无 SBU 的用户访问 `column` 实体 | `403 / 610002`，**不回退为查全部** |
| 携带不属于自己的 `X-SBU-ID` | `403 / 610002` |
| 携带已停用 / 已删除的 SBU | `403 / 610002` |
| 需要跨全部 SBU | 必须持有 `{app}.{entity}.read.all`，且请求不携带 `X-SBU-ID` |

最后一条是唯一合法的"看全部"路径，且权限码默认不授予任何角色。**不能通过"用户关联了多个 SBU"自动获得**。

### 为什么不能直接用 `[RequireSbu]`

`[RequireSbu]` 是 attribute，挂在 Controller 上就固定生效。但低代码是**一个通用 Controller 服务所有实体**——全局实体不需要 SBU，按 SBU 的实体必须校验。

所以不能这样写：

```csharp
[RequireSbu]   // ❌ 全局实体会被误伤
public sealed class LowCodeRecordController : ControllerBase
```

改成按实体定义动态判定：

```csharp
public interface ISbuScopeResolver
{
    // scopeMode = none 时返回 null；= column 时校验并返回已验证的 SBU
    Task<CurrentSbu?> ResolveAsync(EntityDefinition entity, HttpContext context, CancellationToken ct);
}
```

实现内部复用现有 `IUserSbuRepository.GetEnabledSbuForUserAsync`，不重复写校验逻辑。

### 前端只有一个 SBU 来源

顶栏的切换器（`LayoutHeader`）是唯一入口，低代码页面**不再做第二个选择器**。切换后 `LayoutContent` 按 `currentSbuId` 重建缓存树，旧数据自然丢弃。

请求通过现有 `sbuScoped: true` 自动携带 `X-SBU-ID`，并校验"切换后才返回的旧响应"被丢弃——这套机制已在主分支实现，直接复用。

### 外部数据集

外部数据集的 SBU 过滤**必须在 SQL 里**，不能拉回内存过滤：百万行表会拖死服务，且内存过滤一旦漏判就是全量泄露。

`LC_META_DATASET.sbuFilter` 由数据集实现方声明（`none` / `parameter`），页面配置无权更改。MES 那边按 SBU 选 E / F / EB 库的策略属于业务接口的职责，低代码只负责把 `:sbuId` 绑定变量传下去。

## 3. 验收用例

这 6 类必须全部覆盖，缺一条不算完成：

| # | 场景 | 期望 |
| --- | --- | --- |
| 1 | 无任何 SBU 的用户访问 `column` 实体 | `403 / 610002` |
| 2 | 请求携带不属于自己的 `X-SBU-ID` | `403 / 610002` |
| 3 | 请求携带已停用的 SBU | `403 / 610002` |
| 4 | 用户在 SBU-A 下查询，库里有 SBU-B 的记录 | 只返回 A 的记录 |
| 5 | 切换 SBU 后，前一次请求的响应返回 | 被丢弃，不渲染 |
| 6 | 没有 `read.all` 却想查全部 | 拒绝；授予后且不带 `X-SBU-ID` 才通过 |

另外三条写入用例：

| # | 场景 | 期望 |
| --- | --- | --- |
| 7 | 新增时请求体里伪造 `SBU_ID` | 被忽略，写入当前 SBU |
| 8 | 修改时试图改 `SBU_ID` | 字段只读，`400 / 600001` |
| 9 | 删除他人 SBU 的记录（知道 ID） | `404 / 600002`，不暴露存在性 |

第 9 条容易被忽略：范围过滤必须同时作用于按 ID 取单条，否则知道 ID 就能越权读取。
