# 元数据模型

更新于 2026-10-30。元数据是平台的**唯一真相**，物理表结构、页面渲染、权限码都由它推导。

## 1. 存储策略

元数据表放 `ZX_LOWCODE` schema，由平台自己的 EF Core DbContext 管理（静态模型，走 `ZX_LOWCODE_MIGRATIONS`）。业务表不归 EF 管，见 [lifecycle.md](lifecycle.md)。

| 表 | 作用 |
| --- | --- |
| `LC_META_APP` | 应用 |
| `LC_META_ENTITY` | 实体，字段与视图存在 JSON 里 |
| `LC_META_PAGE` | 页面 |
| `LC_META_DATASET` | 外部只读数据集 |
| `LC_META_VERSION` | 发布版本快照（各对象共用） |
| `LC_SCHEMA_CHANGE` | 结构变更计划与执行状态 |

**字段和视图不单独建表，作为实体 JSON 的一部分。** 元数据永远是整体加载的聚合，拆表只会让写入变成跨表事务，没有任何查询收益。

公共列（除 `LC_META_VERSION`）：

```sql
ID              RAW(16)                 -- 应用生成
CODE            NVARCHAR2(100)          -- 业务标识，唯一
NAME            NVARCHAR2(200)
NAME_EN         NVARCHAR2(200)
STATUS          NVARCHAR2(16)           -- draft | active | disabled | retired
REVISION        NUMBER(10)              -- 乐观锁
CREATED_AT_UTC  TIMESTAMP(7) WITH TIME ZONE
CREATED_BY_USER_ID  RAW(16)
UPDATED_AT_UTC  TIMESTAMP(7) WITH TIME ZONE
UPDATED_BY_USER_ID  RAW(16)
IS_DELETED      NUMBER(1)
```

`LC_META_ENTITY` / `LC_META_PAGE` 额外两列：

```sql
DRAFT_JSON        CLOB    -- 设计器正在编辑的版本
PUBLISHED_JSON    CLOB    -- 线上生效的版本
PUBLISHED_VERSION NUMBER(10)
```

## 2. 实体 JSON

`schemaVersion` 必填，用于结构演进。当前为 `1`。

```json
{
  "schemaVersion": 1,
  "code": "station",
  "appCode": "mes",
  "name": "站点",
  "nameEn": "Station",
  "scopeMode": "column",
  "scopeField": "SBU_ID",
  "auditEnabled": true,
  "fields": [
    {
      "key": "code",
      "type": "text",
      "name": "站点编码",
      "nameEn": "Station code",
      "required": true,
      "unique": true,
      "indexed": true,
      "maxLength": 64,
      "sort": 10
    },
    {
      "key": "name",
      "type": "text",
      "name": "站点名称",
      "nameEn": "Station name",
      "required": true,
      "maxLength": 200,
      "sort": 20
    },
    {
      "key": "lineCode",
      "type": "select",
      "name": "产线",
      "nameEn": "Production line",
      "options": [
        { "value": "L1", "label": "一线", "labelEn": "Line 1" },
        { "value": "L2", "label": "二线", "labelEn": "Line 2" }
      ],
      "sort": 30
    },
    {
      "key": "isEnabled",
      "type": "boolean",
      "name": "启用",
      "nameEn": "Enabled",
      "defaultValue": "true",
      "sort": 40
    },
    {
      "key": "remark",
      "type": "longtext",
      "name": "备注",
      "nameEn": "Remark",
      "maxLength": 2000,
      "sort": 50
    }
  ],
  "views": [
    {
      "code": "default",
      "name": "全部站点",
      "nameEn": "All stations",
      "pageSize": 20,
      "columns": [
        { "field": "code", "width": 160, "visible": true, "sort": 10 },
        { "field": "name", "width": 220, "visible": true, "sort": 20 },
        { "field": "lineCode", "width": 120, "visible": true, "sort": 30 },
        { "field": "isEnabled", "width": 100, "visible": true, "sort": 40 },
        { "field": "remark", "width": 240, "visible": false, "sort": 50 }
      ],
      "filters": [
        { "field": "code", "operator": "like", "required": false },
        { "field": "lineCode", "operator": "eq", "required": false },
        { "field": "isEnabled", "operator": "eq", "required": false }
      ],
      "sorts": [{ "field": "code", "direction": "asc" }],
      "quickSearch": ["code", "name"]
    }
  ]
}
```

### 实体字段说明

| 键 | 必填 | 说明 |
| --- | --- | --- |
| `scopeMode` | 是 | `none` 全局；`column` 按 SBU 隔离。`relation` 不实现，见 [authorization.md](authorization.md) |
| `scopeField` | 否 | 默认 `SBU_ID`，`scopeMode=column` 时自动建列与索引 |
| `auditEnabled` | 否 | 默认 `true`，建审计列并写入 |
| `fields[].key` | 是 | 小驼峰，`^[a-z][a-zA-Z0-9]{0,29}$`，落库转大写下划线 |
| `fields[].type` | 是 | 见 [field-types.md](field-types.md) |
| `fields[].unique` | 否 | 建唯一索引，按"未删除"过滤（虚拟列实现） |
| `fields[].indexed` | 否 | 建普通索引 |
| `fields[].defaultValue` | 否 | 字符串形式，写入时按类型解析 |
| `views[].code` | 是 | 实体内唯一 |
| `views[].pageSize` | 否 | 默认 20，上限 200 |

## 3. 页面 JSON

```json
{
  "schemaVersion": 1,
  "code": "station-list",
  "appCode": "mes",
  "name": "站点管理",
  "nameEn": "Station management",
  "path": "/mes/station",
  "permissionCode": "mes.station.read",
  "blocks": [
    { "type": "list", "entity": "station", "view": "default", "height": "auto" }
  ]
}
```

| 区块类型 | 说明 | 第一期 |
| --- | --- | --- |
| `list` | 列表，绑定实体 + 视图 | ✅ |
| `form` | 表单，绑定实体（新增 / 编辑） | ✅ |
| `stat` | 统计卡片 | ❌ 第二批 |

页面**不定义字段**，字段来自实体的视图定义。这样改一处字段，所有引用它的页面自动生效——上一版原型把字段抄进页面里，导致两处不一致，这次不重复。

## 4. 外部数据集 JSON

```json
{
  "schemaVersion": 1,
  "code": "mes.terminal",
  "name": "MES 站点",
  "nameEn": "MES terminals",
  "kind": "oracle",
  "connectorCode": "MesE",
  "object": "SAJET.SYS_TERMINAL",
  "sbuFilter": "parameter",
  "rowLimit": 5000,
  "fields": [
    { "key": "terminalName", "name": "站点", "nameEn": "Terminal", "column": "TERMINAL_NAME", "type": "text" },
    { "key": "pdlName", "name": "产线", "nameEn": "Line", "column": "PDLINE_NAME", "type": "text" }
  ]
}
```

`sbuFilter` 由数据集实现方声明，页面配置无权修改。`connectorCode` 引用服务端配置的连接别名，凭据不落元数据表。

## 5. 校验规则

写入草稿时逐项校验，任一项不通过返回 `400 / 400001`：

| 规则 | 说明 |
| --- | --- |
| code 格式 | 应用 `^[a-z][a-z0-9]{1,15}$`；实体 / 页面 `^[a-z][a-z0-9-]{1,31}$` |
| code 唯一 | 同应用内实体唯一，全局页面唯一 |
| 字段数 | 1–200 |
| 视图数 | 1–20 |
| 字段 key | 实体内唯一，不与保留列冲突 |
| 表名长度 | `LC_{APP}_{ENTITY}` ≤ 30 字符 |
| 视图字段 | 必须存在于实体字段中 |
| 筛选字段 | 必须 `filterable`，见 [field-types.md](field-types.md) |
| 排序字段 | 必须 `sortable` |
| 保留字 | `ID`、`SBU_ID`、`CREATED_*`、`UPDATED_*`、`IS_DELETED`、`REVISION` 不可用作字段 key |

## 6. 版本与草稿

- 编辑只改 `DRAFT_JSON`，`REVISION` 自增，写入时校验客户端持有的 revision，不一致返回 `409 / 409003`。
- 发布时把 `DRAFT_JSON` 复制到 `PUBLISHED_JSON`，`PUBLISHED_VERSION` 自增，同时写一条 `LC_META_VERSION`。
- 运行时**只读 `PUBLISHED_JSON`**，草稿对线上零影响。
- 回滚 = 取历史版本的 `PAYLOAD_JSON` 写回 `DRAFT_JSON`，再走一次发布。**不做"直接切换指针"**，保证版本号单调递增、可追溯。
