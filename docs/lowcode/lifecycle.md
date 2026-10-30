# 生命周期：发布、版本、退役

更新于 2026-10-30。发布和退役是一对：一个让对象上线，一个让对象干净地下线。两者都必须可追溯、可回滚。

## 1. 对象状态

```
Draft ──发布──> Active ──停用──> Disabled ──退役──> Retired ──清理──> Purged
  ↑                │                  │
  └──── 回滚 ───────┴──────────────────┘
```

| 状态 | 菜单 | 接口 | 数据 |
| --- | --- | --- | --- |
| `Draft` | 不可见 | 404 | 表未建 |
| `Active` | 可见 | 正常 | 可读写 |
| `Disabled` | 隐藏 | 410 | 只读，可恢复 |
| `Retired` | 已删 | 410 | 只读，等待清理 |
| `Purged` | 已删 | 410 | 表已 DROP |

`Disabled` 必须可逆，且不丢数据——**90% 的"下线"需求到这一步就够了**，不要轻易走到 `Retired`。

## 2. SchemaChange：唯一的结构变更产物

无论测试环境自动执行还是生产交 DBA，产物都是同一个结构化对象，SQL 只是它的渲染。

```json
{
  "id": "...",
  "appCode": "mes",
  "entityCode": "station",
  "changeType": "addColumn",
  "payload": {
    "table": "LC_MES_STATION",
    "column": "REMARK",
    "oracleType": "CLOB",
    "nullable": true
  },
  "status": "pending",
  "scriptSql": "ALTER TABLE \"ZX_LOWCODE\".\"LC_MES_STATION\" ADD (\"REMARK\" CLOB NULL)",
  "revision": 1
}
```

`changeType` 只允许：`createTable` `addColumn` `addIndex` `addUniqueIndex` `dropTable`。**没有 `alterColumnType`、没有 `dropColumn`**——见 [field-types.md](field-types.md) 的演进规则。

### 状态机

| 状态 | 测试环境 | 生产环境 |
| --- | --- | --- |
| `Pending` | 生成后待执行 | 生成后待导出 |
| `Executing` | 平台执行中 | — |
| `Exported` | — | 已导出，等 DBA |
| `Verified` | — | 平台校验通过 |
| `Applied` | 执行成功 | 校验成功 |
| `Failed` | 停在失败那条 | 校验不通过 / DBA 报错 |

**元数据版本能否激活，只看一件事：它依赖的所有 change 都是 `Applied`。**

### 两条路径，一套代码

```
设计器改结构
     ↓
生成 SchemaChange（结构化）  ← 唯一产物
     ↓
  ┌──┴──────────────────────────┐
测试环境                     生产环境
平台执行 + 记录              导出 SQL → DBA 执行 → 平台校验
  └──┬──────────────────────────┘
     ↓
激活元数据版本
```

生产路径的校验是查 `ALL_TAB_COLUMNS` / `ALL_INDEXES`，确认列和索引确实存在，才允许把 `Verified` 推进到 `Applied`。

### Oracle 上的五个坑

| 坑 | 处理 |
| --- | --- |
| DDL 隐式提交 | 绝不在业务事务里执行 DDL，单独连接、单独提交 |
| 不可回滚 | 一个 change set 里每条 DDL 单独记状态，失败即停，其余保持 `Pending` |
| 没有 `ADD COLUMN IF NOT EXISTS` | 执行前查 `ALL_TAB_COLUMNS`，存在即跳过（保证幂等） |
| 大表建索引锁表 | 一律 `CREATE INDEX ... ONLINE`，测试环境也一样 |
| `DROP TABLE` 进回收站 | 模板写死 `DROP TABLE ... PURGE`，不给选择 |

最后一条是实际踩过的：不加 `PURGE` 会留一堆 `BIN$...==$0`，测试库看起来删了、实际没释放。

### 发布顺序

**DDL 先行，元数据后激活。** 顺序不能反：

```
1. 生成 change → 2. 执行/导出 → 3. 校验 Applied → 4. 发布元数据 → 5. 注册菜单与权限
```

第 4 步之前的任何时刻，运行时读到的仍是上一版 `PUBLISHED_JSON`，页面不会因为"表加了列但元数据没发"而报错。

## 3. 版本与回滚

- 发布 = 复制 `DRAFT_JSON` → `PUBLISHED_JSON`，`PUBLISHED_VERSION` 自增，写一条 `LC_META_VERSION`。
- 回滚 = 取历史版本的 `PAYLOAD_JSON` 写回 `DRAFT_JSON`，**再走一次发布**。
- 不做"指针直接切回旧版本"——那样版本号会倒退，审计链断裂。

回滚前必须检查：目标版本的字段与当前物理表是否兼容。如果中间删过字段（标记停用），回滚后视图引用了已停用的字段，要给出明确提示而不是静默渲染空列。

## 4. 环境流转

DDL 交给 DBA 了，但**元数据本身也要过河**。发布包必须成对：

```
release-20261101/
  schema.sql          ← 给 DBA
  metadata.json       ← 平台导入（应用 / 实体 / 页面 / 数据集）
  checksums.txt       ← 两边比对
```

导入时校验 `checksum`，不一致直接拒绝——避免"测试环境导出的包，生产环境手工改过一半"。

元数据包随制品发布走 git，**不能只靠人工在页面上点**。两次手工操作必然有差异，而且没人能 diff 出来。

## 5. 退役

一个实体牵连七类对象，手工清必然漏。平台必须提供预检 + 计划 + 脚本。

### 三阶段

| 阶段 | 动作 | 可逆 | 权限 |
| --- | --- | --- | --- |
| 停用 | 菜单隐藏、权限停用、接口 410 | ✅ | 应用管理员 |
| 归档 | 导出数据、元数据标 `Retired`、保留表 | ✅ | 应用管理员 |
| 清理 | 生成 `DROP ... PURGE` 脚本、删元数据与权限、bump 缓存 | ❌ | 独立高危权限 + DBA |

### 影响预检

点"退役"之前先给出清单：

```
实体 mes.station
├─ 数据 12,847 行
├─ 被 3 个页面引用（站点列表 / 站点详情 / 机台绑定）
├─ 权限码 6 个，已授予 2 个角色（班组长、工艺员）
├─ 菜单 6 行
└─ 最近发布 v7（2026-10-20）
```

没有这份清单，没人敢点删除；结果就是系统里堆满废弃表。

### 清理脚本

```sql
-- 由平台生成，交 DBA 执行
DROP TABLE "ZX_LOWCODE"."LC_MES_STATION" PURGE;
```

配套的元数据清理（平台自己执行，一个事务）：

```sql
DELETE FROM ZX_LOWCODE.LC_META_ENTITY WHERE CODE = 'station' AND APP_CODE = 'mes';
DELETE FROM ZX_LOWCODE.LC_META_PAGE   WHERE CODE LIKE 'mes.station%';
DELETE FROM ZX_LOWCODE.LC_META_VERSION WHERE OWNER_CODE IN ('mes.station');
DELETE FROM ZX_IDENTITY.ZX_IDENTITY_ROLE_PERMISSION WHERE PERMISSION_CODE LIKE 'mes.station.%';
DELETE FROM ZX_IDENTITY.ZX_IDENTITY_PERMISSION      WHERE OWNER_CODE = 'mes.station';
DELETE FROM ZX_IDENTITY.ZX_IDENTITY_MENU            WHERE OWNER_CODE = 'mes.station';
```

`OWNER_CODE` 的存在让这些语句都是精确匹配，不依赖 `LIKE` 猜前缀。

清理完成后必须 **bump 权限缓存版本**，否则 Redis 里还挂着已删除的权限码。

## 6. 迁移线隔离

低代码的元数据表走**自己的 DbContext + 自己的历史表**：

```
ZX_LOWCODE
├─ ZX_LOWCODE_MIGRATIONS     ← 只有低代码的迁移
├─ LC_META_*
├─ LC_SCHEMA_CHANGE
└─ LC_*                      ← 业务表，不进 EF
```

这样整体撤除低代码只需 `DROP USER ZX_LOWCODE CASCADE`，Identity 的迁移历史一行不动。上一版原型把低代码表塞进 `ZX_IDENTITY_MIGRATIONS`，导致撤除时要手工删表、删历史、清回收站——这个错误不能重犯。
