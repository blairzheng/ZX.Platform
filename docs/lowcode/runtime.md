# 运行时：查询与写入

更新于 2026-10-30。运行时只读 `PUBLISHED_JSON`，不接受任何来自客户端的 SQL、表名或列名。

## 1. 通用原则

1. **所有标识符来自元数据**。表名、列名从已发布定义里取，客户端只能传字段 key。
2. **所有值参数化绑定**。拼进 SQL 的只有白名单校验过的标识符，永远不拼值。
3. **先鉴权再查询**。功能权限 → SBU 范围 → 字段白名单，三步都过才生成 SQL。
4. **结果集必须有上限**。分页上限 200，导出上限 50000，外部数据集默认 5000。

## 2. 查询

```
GET /api/lowcode/records/{entity}?view=default&page=1&pageSize=20&sort=code:asc&filter=lineCode:eq:L1&keyword=abc
```

| 参数 | 说明 |
| --- | --- |
| `view` | 视图 code，缺省用 `default`；决定返回字段与默认排序 |
| `page` / `pageSize` | `pageSize` 上限 200 |
| `sort` | `field:asc` 或 `field:desc`，可重复；字段必须在视图内且 `sortable` |
| `filter` | `field:operator:value`，可重复；`between` 用 `~` 连接两端 |
| `keyword` | 对视图 `quickSearch` 里的字段做前缀匹配，OR 连接 |

`filter` 示例：

```
filter=code:like:ST
filter=isEnabled:eq:true
filter=createdAtUtc:between:2026-01-01T00:00:00Z~2026-06-30T23:59:59Z
filter=lineCode:in:L1,L2
```

### 处理顺序

```
1. 取已发布实体定义          → 不存在 600002
2. 校验功能权限 {app}.{entity}.read
3. 解析 scopeMode            → column 时必须拿到已验证的 SBU
4. 校验视图与字段白名单
5. 校验算子（类型 × 算子矩阵）
6. 生成参数化 SQL
7. 执行 + 截断
```

### 返回结构

```json
{
  "code": 0,
  "message": "查询成功",
  "traceId": "...",
  "data": {
    "items": [{ "id": "...", "code": "ST001", "name": "一站" }],
    "total": 128,
    "page": 1,
    "pageSize": 20
  }
}
```

只返回视图里 `visible: true` 的字段。**未声明的字段一律不返回**，即使它存在于表里——这是防止"多加一列就泄露"的最后一道闸。

### 性能护栏

| 条件 | 行为 |
| --- | --- |
| `like` 非前缀匹配 | 拒绝 `610003` |
| 筛选字段无索引且表行数 > 10 万 | 拒绝 `610003`，提示改用带索引字段或导出 |
| `pageSize` > 200 | 截断为 200，不报错 |
| `page × pageSize` > 10 万 | 拒绝 `610004`，提示收窄条件 |
| 无任何筛选且表行数 > 50 万 | 拒绝 `610004`，要求至少一个条件 |
| 单次查询超时 | 10 秒，超时 `600004` |

行数从 `ALL_TABLES.NUM_ROWS` 取近似值，不做 `COUNT(*)`。允许 10% 误差——护栏不需要精确。

## 3. 写入

| 操作 | 路由 | 权限 |
| --- | --- | --- |
| 新增 | `POST /records/{entity}` | `{app}.{entity}.create` |
| 修改 | `PUT /records/{entity}/{id}` | `{app}.{entity}.update` |
| 删除（软） | `DELETE /records/{entity}/{id}` | `{app}.{entity}.delete` |
| 批量删除 | `POST /records/{entity}/batch-delete` | `{app}.{entity}.delete` |

### 校验顺序

```
1. 功能权限
2. SBU 范围（见 authorization.md）
3. 字段白名单：请求里出现未声明字段 → 600001
4. 必填 / 类型 / 长度 / 选项合法性
5. 关联字段存在性（refEntity 必须已发布且记录存在）
6. 唯一约束（冲突 → 600003）
7. 乐观锁（修改时 REVISION 必须匹配 → 不匹配 600003）
```

### 服务端强制写入的列

客户端传了也忽略，不报错：

| 列 | 值 |
| --- | --- |
| `ID` | 新增时生成 `Guid` |
| `SBU_ID` | `scopeMode = column` 时取当前 SBU |
| `CREATED_*` / `UPDATED_*` | 服务端时间与用户 |
| `IS_DELETED` | `0` |
| `REVISION` | 新增 `1`，修改 +1 |
| `autonumber` 字段 | 按规则生成 |

`SBU_ID` **不允许在修改时变更**。跨 SBU 搬迁属于独立高危操作，不在普通表单能力内。

### 删除

一律软删：`IS_DELETED = 1`，`UPDATED_*` 更新。所有查询默认追加 `IS_DELETED = 0`。

**平台不提供物理删除。** 需要清理数据时走退役流程（[lifecycle.md](lifecycle.md)），由 DBA 执行脚本。

### 批量

- 单次上限 200 条
- 一个事务，全成功或全失败
- 任一条失败返回 `600003`，并给出失败项的 id 列表
- 不做"部分成功"——那会让前端状态无法收敛

## 4. 事务边界

| 操作 | 事务 |
| --- | --- |
| 单条新增 / 修改 / 删除 | 一个事务 |
| 批量删除 | 一个事务 |
| handler 自定义操作 | handler 自己声明，平台只包最外层 |

平台**不做跨实体事务**。一个页面里两个 section 的写入是两次独立请求——跨表一致性由业务 handler 负责，不交给页面配置。

## 5. 错误码

沿用现有 `ErrorCodes` 的 `600xxx` 段，低代码新增 `610xxx`：

| code | HTTP | 场景 |
| --- | --- | --- |
| 600001 | 400 | 参数 / 元数据校验失败 |
| 600002 | 404 | 实体、页面、记录不存在 |
| 600003 | 409 | 唯一冲突、乐观锁冲突、版本冲突 |
| 600004 | 409 | 状态不允许（未发布、结构变更未完成、查询超时） |
| 600005 | 403 | 无功能权限 |
| 610001 | 400 | 该实体要求选择 SBU，但请求未携带 |
| 610002 | 403 | SBU 不属于当前用户、已停用或已删除 |
| 610003 | 400 | 字段不支持该算子 / 未索引 |
| 610004 | 400 | 分页或结果集超限 |
| 610005 | 400 | 外部数据集只读，拒绝写入 |

新增的 `610xxx` 需同步到 `ErrorCodes.cs`、`ApiMessageLocalizer` 的资源键，以及前端 `src/api/identity/codes.ts`（或新建 `src/api/lowcode/codes.ts`）。

## 6. SQL 生成

只允许两种语句形态，均由代码模板产生：

```sql
-- 查询
SELECT {columns} FROM {table}
 WHERE IS_DELETED = 0
   AND {scope}
   AND {filters}
 ORDER BY {sorts}
 OFFSET :offset ROWS FETCH NEXT :limit ROWS ONLY;

-- 写入
INSERT INTO {table} ({columns}) VALUES ({binds});
UPDATE {table} SET {assignments}, REVISION = REVISION + 1
 WHERE ID = :id AND REVISION = :revision AND IS_DELETED = 0;
```

`{columns}`、`{table}`、`{sorts}` 来自元数据并再次过白名单；`{filters}` 只产生 `列 算子 :参数` 三元组，算子来自固定枚举。**没有任何路径能把用户输入拼进 SQL 文本。**
