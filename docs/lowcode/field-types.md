# 字段类型

更新于 2026-10-30。字段类型同时决定**物理列型、前端控件、校验规则、可筛选性、查询算子**。它是最底层的公共依赖，改动成本高于元数据 schema 版本。

## 1. 类型清单（13 种）

| type | Oracle 列型 | 参数 | 前端控件 |
| --- | --- | --- | --- |
| `text` | `NVARCHAR2(n)` | `maxLength` 1–2000，默认 200 | 输入框 |
| `longtext` | `CLOB` | `maxLength` ≤ 32000（仅前端校验） | 多行文本 |
| `number` | `NUMBER(p,0)` | `precision` 1–18，默认 18 | 数字输入 |
| `amount` | `NUMBER(18,4)` | 固定 | 金额输入 |
| `percent` | `NUMBER(9,4)` | 固定，取值 0–100 | 百分比输入 |
| `date` | `DATE` | — | 日期选择 |
| `datetime` | `TIMESTAMP(7) WITH TIME ZONE` | — | 日期时间选择 |
| `boolean` | `NUMBER(1)` | — | 开关 |
| `select` | `NVARCHAR2(64)` | `options` 必填 | 下拉 |
| `multiselect` | `NVARCHAR2(2000)` | `options` 必填，存 JSON 数组 | 多选 |
| `relation` | `RAW(16)` | `refEntity`、`refDisplayField` 必填 | 关联选择 |
| `attachment` | `CLOB` | 存 JSON 数组（文件元数据） | 上传 |
| `autonumber` | `NVARCHAR2(40)` | `prefix` 可选 | 只读 |

**`formula` 不在第一期。** 公式需要表达式引擎，和"平台不做表达式引擎"的边界冲突。第二批若要做，只支持受限四则运算（`{a} + {b}`、`{a} * {b}`），不做函数、不做条件分支。

## 2. 类型与查询算子

查询引擎只接受下表组合，其余一律 `400 / 400001`。

| type | 允许的算子 | 可筛选 | 可排序 |
| --- | --- | --- | --- |
| `text` | `eq` `like` `in` `isNull` | ✅ | ✅ |
| `longtext` | `isNull` | ⚠️ 仅判空 | ❌ |
| `number` / `amount` / `percent` | `eq` `ne` `gt` `ge` `lt` `le` `between` `in` | ✅ | ✅ |
| `date` / `datetime` | `eq` `ge` `le` `between` | ✅ | ✅ |
| `boolean` | `eq` | ✅ | ✅ |
| `select` | `eq` `in` | ✅ | ✅ |
| `multiselect` | `contains` | ⚠️ 近似匹配 | ❌ |
| `relation` | `eq` `in` | ✅ | ✅ |
| `attachment` | — | ❌ | ❌ |
| `autonumber` | `eq` `like` `in` | ✅ | ✅ |

两条限制必须写进前端，不能只在后端拦：

- **`like` 只允许前缀匹配**（`abc%`），禁止 `%abc%`。中缀匹配在百万行表上必然全表扫描，这类查询应该做成独立报表接口。
- **`multiselect` 的 `contains` 是近似实现**：存的是 JSON 数组，查询用 `LIKE '%"value"%'`。要求选项值不含引号。精确匹配（子表）放第二批。

## 3. 保留列

每个业务表固定包含以下列，字段 key 不得与之冲突：

```sql
ID                   RAW(16)        -- 主键
SBU_ID               RAW(16)        -- scopeMode = column 时存在
CREATED_AT_UTC       TIMESTAMP(7) WITH TIME ZONE
CREATED_BY_USER_ID   RAW(16)
UPDATED_AT_UTC       TIMESTAMP(7) WITH TIME ZONE
UPDATED_BY_USER_ID   RAW(16)
IS_DELETED           NUMBER(1)      -- 软删
REVISION             NUMBER(10)     -- 乐观锁
```

## 4. 索引规则

| 场景 | 建什么 |
| --- | --- |
| 主键 | `ID` 上建 PK |
| `scopeMode = column` | `SBU_ID` 上建普通索引（按 SBU 过滤是最高频查询） |
| `unique: true` | 虚拟列 `ACTIVE_{KEY}` + 唯一索引，模拟部分索引 |
| `indexed: true` | 该列普通索引 |
| `relation` 字段 | 自动建索引（关联查询几乎必然按它过滤） |

唯一索引沿用 Identity 里 `MENU` 的做法——用虚拟列绕开 Oracle 不支持部分索引：

```sql
ALTER TABLE LC_MES_STATION ADD (
  ACTIVE_CODE NVARCHAR2(64)
    GENERATED ALWAYS AS (CASE WHEN IS_DELETED = 0 THEN CODE END) VIRTUAL
);
CREATE UNIQUE INDEX UX_LC_MES_STATION_ACTIVE_CODE
  ON LC_MES_STATION (ACTIVE_CODE);
```

这样"删掉一条还能用同一个编码新建"是成立的，而物理删除的历史记录不会挡路。

## 5. 类型演进规则

这几条是硬约束，写进校验器：

1. **不允许修改已有字段的类型。** 要改就新建字段 + 数据迁移脚本 + 切换视图引用，旧字段标记停用。
2. **新增字段必须可空**，除非同时给 `defaultValue`。空表可以例外，由 DDL 生成器判断。
3. **删除字段 = 标记停用**，物理列永远保留。`DROP COLUMN` 不在平台能力范围内。
4. **重命名字段 = 新建 + 迁移**，不做 `RENAME COLUMN`。因为重命名会让历史版本的 JSON 全部失效，回滚就崩。

第 1 条和第 4 条看起来保守，但它们是"元数据与物理表不会漂移"的唯一保障。一旦允许原地改类型，历史数据和历史版本的关系就再也说不清了。

## 6. 默认值

`defaultValue` 是字符串，写入时按类型解析：

| type | 解析规则 |
| --- | --- |
| `text` / `longtext` / `select` / `autonumber` | 原样 |
| `number` / `amount` / `percent` | `decimal.Parse`，失败即校验错误 |
| `boolean` | `"true"` / `"false"` |
| `date` / `datetime` | ISO 8601；支持特殊值 `$now` |
| `relation` | 不支持默认值 |
| `multiselect` / `attachment` | JSON 数组字符串 |

特殊值只允许 `$now` 和 `$currentUserId`，其余一律拒绝——不做表达式。

## 7. 长度与精度上限

| 项 | 上限 | 原因 |
| --- | --- | --- |
| 字段数 | 200 / 实体 | 再多应该拆实体 |
| `text` 长度 | 2000 | 超过应使用 `longtext` |
| 表名 | 30 字符 | 兼容旧客户端与工具链 |
| 列名 | 30 字符 | 同上 |
| 单行估算大小 | 8 KB（不含 CLOB） | 避免行迁移 |
