# 接口与 M1 验收

更新于 2026-10-30。所有接口挂在 `/api/lowcode`，由 Identity 进程托管，网关加一条路由即可。

## 1. 通用约定

| 项 | 约定 |
| --- | --- |
| 前缀 | `/api/lowcode` |
| 鉴权 | 复用现有 JWT；`FallbackPolicy` 要求已登录，管理接口另加功能权限 |
| 响应 | 复用 `ApiResponse<T>`：`{ code, message, traceId, data }` |
| 语言 | `Accept-Language`，复用 `ApiMessageLocalizer` |
| 错误码 | `600xxx` / `610xxx`，见 [runtime.md](runtime.md#5-错误码) |
| 限流 | 沿用现有滑动窗口；导出接口单独放宽 |

网关需要新增一条路由（不改写前缀，指向同一个 cluster）：

```json
{
  "lowcode-route": {
    "ClusterId": "identity-cluster",
    "Match": { "Path": "/api/lowcode/{**catch-all}" }
  }
}
```

否则前端要访问 `/api/identity/api/lowcode/...`，路径会很难看。

## 2. 元数据接口

| 方法 | 路径 | 权限 | 说明 |
| --- | --- | --- | --- |
| GET | `/apps` | `lowcode.app.read` | 应用列表 |
| POST | `/apps` | `lowcode.app.write` | 新建应用 |
| GET | `/entities` | `lowcode.meta.read` | 实体列表（按应用过滤） |
| GET | `/entities/{code}` | `lowcode.meta.read` | 已发布定义（运行时用） |
| GET | `/entities/{code}/draft` | `lowcode.meta.write` | 草稿（设计器用） |
| PUT | `/entities/{code}/draft` | `lowcode.meta.write` | 保存草稿，需带 `revision` |
| POST | `/entities/{code}/publish` | `lowcode.meta.publish` | 发布 |
| GET | `/entities/{code}/versions` | `lowcode.meta.read` | 版本列表 |
| POST | `/entities/{code}/rollback` | `lowcode.meta.publish` | 回滚到指定版本（写回草稿） |
| GET | `/pages/{code}` | 页面自身权限 | 页面定义（运行时用） |
| GET | `/datasets` | `lowcode.meta.read` | 外部数据集列表 |
| GET | `/catalog` | `lowcode.meta.read` | 可选实体 / 数据集目录（设计器用） |

`lowcode.*` 是平台自身的权限码，属于 `SOURCE=system` 范畴，在 `IdentityPermissions` 里以常量声明，不参与动态注册。

## 3. 运行时接口

| 方法 | 路径 | 权限 | 说明 |
| --- | --- | --- | --- |
| GET | `/records/{entity}` | `{app}.{entity}.read` | 列表，见 [runtime.md](runtime.md#2-查询) |
| GET | `/records/{entity}/{id}` | `{app}.{entity}.read` | 详情 |
| POST | `/records/{entity}` | `{app}.{entity}.create` | 新增 |
| PUT | `/records/{entity}/{id}` | `{app}.{entity}.update` | 修改 |
| DELETE | `/records/{entity}/{id}` | `{app}.{entity}.delete` | 软删 |
| POST | `/records/{entity}/batch-delete` | `{app}.{entity}.delete` | 批量软删，上限 200 |
| GET | `/records/{entity}/export` | `{app}.{entity}.export` | 导出，上限 50000 行 |
| GET | `/datasets/{code}/rows` | 数据集声明的权限 | 外部只读数据，**永不支持写入** |

`{entity}` 用实体的 `code`，不带应用前缀；应用从已发布定义里反查。这样页面 URL 和接口路径都能保持短。

### 示例：新增

```http
POST /api/lowcode/records/station
X-SBU-ID: 3f2a...
Content-Type: application/json

{ "code": "ST001", "name": "一站", "lineCode": "L1", "isEnabled": true }
```

```json
{
  "code": 0,
  "message": "新增成功",
  "traceId": "0HN7...",
  "data": { "id": "9c1e...", "revision": 1 }
}
```

服务端会忽略请求体里的 `SBU_ID`、`CREATED_*`、`REVISION`，按当前上下文写入。

### 示例：列表

```http
GET /api/lowcode/records/station?view=default&page=1&pageSize=20&sort=code:asc&filter=isEnabled:eq:true
X-SBU-ID: 3f2a...
```

## 4. 结构变更接口

| 方法 | 路径 | 权限 | 说明 |
| --- | --- | --- | --- |
| GET | `/schema-changes` | `lowcode.schema.read` | 变更列表（按实体 / 状态过滤） |
| POST | `/schema-changes/generate` | `lowcode.meta.write` | 按草稿差异生成变更 |
| POST | `/schema-changes/{id}/execute` | `lowcode.schema.execute` | 测试环境执行（生产返回 409） |
| GET | `/schema-changes/{id}/script` | `lowcode.schema.read` | 导出 SQL（给 DBA） |
| POST | `/schema-changes/{id}/verify` | `lowcode.schema.execute` | 生产环境校验已执行 |
| POST | `/schema-changes/{id}/activate` | `lowcode.meta.publish` | 全部 Applied 后激活元数据版本 |

`/execute` 在生产环境必须拒绝，不是靠配置开关判断，而是**同时判断账号是否有 DDL 权限**（查 `SESSION_PRIVS`）。配置可能写错，探测不会。

## 5. 工程结构

```
Services/
  Identity/
    ZX.Platform.Identity.Contracts        ← 新增：低代码需要的三个接口
  LowCode/
    ZX.Platform.LowCode.Api               ← Controller（classlib）
    ZX.Platform.LowCode.Application
    ZX.Platform.LowCode.Domain
    ZX.Platform.LowCode.Infrastructure
    ZX.Platform.LowCode.Contracts
```

`Identity.Contracts` 只放三样：

```csharp
public interface IPermissionChecker  { Task<bool> HasAsync(Guid userId, string code, CancellationToken ct); }
public interface IMenuRegistrar       { Task UpsertAsync(MenuRegistration menu, CancellationToken ct); }
public interface IPermissionRegistrar { Task RegisterAsync(IReadOnlyList<PermissionRegistration> items, CancellationToken ct); }
```

三条硬规则：

1. Identity 不引用 LowCode 的任何项目。
2. LowCode 只依赖 `Identity.Contracts`，不依赖 `Identity.Application`。
3. 低代码有总开关 `LowCode:Enabled`，关闭时路由不存在、启动不初始化元数据，Identity 照常工作。

`LowCode.Api` 是 classlib，由 `Identity.Api` 引用并 `AddApplicationPart(typeof(LowCodeController).Assembly)` 挂载。将来拆服务时，把它换成 Web host + 一条网关路由即可。

## 6. M1 垂直切片

目标：**不写一行 Vue、不写一行 C# 业务代码，就能加出一个能增删改查的页面。**

| # | 交付 | 验收方式 |
| --- | --- | --- |
| 1 | Domain 模型 + 13 种字段类型枚举 | 单测：类型 → Oracle 列型全覆盖 |
| 2 | `ZX_LOWCODE` 元数据表 + 独立迁移 | 建库后能读出空目录 |
| 3 | MetadataService（草稿 / 发布 / 版本） | 注册一个实体，能查回来；重复发布版本号递增 |
| 4 | SqlBuilder（建表 / 加列 / 查询 / 写入） | 渲染结果与手写 SQL 一致；标识符全部带引号 |
| 5 | SchemaChangeService | 测试执行成功；生产只导出不执行；重复执行幂等 |
| 6 | QueryEngine | 分页、排序、筛选、字段裁剪；越界字段被拒 |
| 7 | RecordEngine | 增改删、乐观锁、审计、软删 |
| 8 | PermissionGuard + SBU 范围 | 6 类越权用例全过 |
| 9 | Controller + 网关路由 | Swagger 调通全链路 |
| 10 | 前端 `ListPage` + `FormDialog` | 配置驱动，不新增业务 `.vue` |
| 11 | 站点实体跑通 | 截图验收，真实数据 100 行以上 |

### M1 的验收用例（必须全过）

**功能**
- 新增 → 列表可见 → 修改 → 删除 → 列表不可见
- 唯一字段重复 → `600003`
- 并发修改同一记录 → 后者 `600003`

**权限**
- 无 `read` 权限 → `403 / 600005`
- 直接调 API 绕过前端按钮 → 同样被拒
- 未发布实体 → `404 / 600002`

**数据范围**
- 无 SBU 访问 `column` 实体 → `403 / 610002`
- 伪造 `X-SBU-ID` → `403 / 610002`
- 跨 SBU 按 ID 取单条 → `404 / 600002`
- 新增时伪造 `SBU_ID` → 被忽略

**查询护栏**
- `like` 中缀 → `610003`
- `pageSize=1000` → 截断为 200
- 深翻页超限 → `610004`

**结构变更**
- 加字段 → 生成 change → 执行 → 校验 → 激活，四步都成功
- 重复执行同一 change → 幂等，不报错
- DDL 未完成时激活元数据 → `409 / 600004`

这 18 条过完，M1 才算完成。少一条都会在第二期以 bug 的形式回来。
