# 一键盘点报表

[文档导航](README.md) · [外部 Oracle 数据源](external-oracle-data-sources.md) · [角色与权限](authorization.md)

报表查询、展示并汇总现有 MES 盘点记录，不执行盘点差异计算或修改源表。数据源固定为 `MesEDA`，查询对象固定为 `EDA.G_WMS_KEYBOARD`；`BU17EDA` 是数据库名，表的 owner 是 `EDA`，两者不能混作 SQL 前缀。连接别名、owner 和表名不接受前端输入。该表不纳入 Identity 的 EF 模型，无需新增 Migration。

## 查询与权限

Identity 接口为 `GET /api/InventoryStocktake/list`，经 Gateway 的完整路径为 `/api/identity/api/InventoryStocktake/list`。前端统一请求层的 `url` 填 `/identity/api/InventoryStocktake/list`，由默认 `baseURL=/api` 补齐前缀。

接口要求 `inventory.stocktake.read`，沿用现有静态权限、角色授权与缓存机制。该报表按业务要求不区分 SBU：后端不使用 `[RequireSbu]`，前端不启用 `sbuScoped`，工厂和库存地点是查询条件，不是 SBU 授权条件。获授该功能权限的有效业务账号可查询这张表中的全部记录，即使没有分配 SBU。

当前数据量较小，`list` 一次返回源表全部记录，不接收查询或分页参数。工厂、库存地点和料号过滤在前端完成，不向接口提交；未设置隐式行数上限，不通过多次分页请求拼出全量。

查询全部日期、全部类型，不隐含 `DATATYPE='BYTIME'` 条件。列表按工厂、库存地点、料号、ROWID 排序；ROWID 仅用于列表行标识及同值排序，不作为业务主键。连接别名、SQL、表名和排序均固定，客户端没有传入 SQL 的入口。

成功返回 `ApiResponse<StocktakeListDto>`，`data={ list, total }`；`total` 等于实际返回的 `list.Count`，不再额外执行 COUNT。每行对应关系如下，空字段可能按现有序列化配置省略：

| 返回字段 | 源字段 | 显示名称 |
| --- | --- | --- |
| `rowKey` | `ROWIDTOCHAR(ROWID)` | 表格行标识，不展示 |
| `factory` | `WERKS` | 工厂 |
| `storageLocation` | `LGORT` | 库存地点 |
| `material` | `MATNR` | 料号 |
| `wmsReelQty` | `WMS_REEL_QTY` | 库存卷数 |
| `reelQty` | `REEL_QTY` | 线边仓卷数 |
| `keepQty` | `KEEP_QTY` | 使用卷数 |
| `wmsQty` | `WMS_QTY` | 库存数量 |
| `reelInfoQty` | `REEL_INFO_QTY` | 线边仓数量（在制） |
| `keepInfoQty` | `KEEP_INFO_QTY` | 使用数量 |
| `createDate` | `CREATE_DATE` | 创建时间，接口保留，表格不展示 |
| `updateDate` | `UPDATE_DATE` | 修改时间，接口保留，表格不展示 |
| `workDate` | `WORK_DATE` | 日期，接口保留，表格不展示 |
| `dataType` | `DATATYPE` | 类型，接口保留，表格不展示 |

Oracle DATE 没有时区；`createDate` / `updateDate` 按源库时间格式化为 `yyyy-MM-dd HH:mm:ss` 字符串返回，不转 UTC 或浏览器时区。页面隐藏创建时间、修改时间、日期和类型四列，接口仍保留这些原始字段。其余字段按源值展示，不推算截图以外的业务字段。第一版不提供导出或打印入口。

## 前端过滤与看板

桌面布局顶部为四张极值卡片，下方左侧为明细表格，右侧上下排列库存 Top5、在制 Top5 两张 ECharts 横向条形图，尽量在一屏中展示；表格在自身区域内滚动，小屏改为纵向排列。去掉原汇总范围整行、工厂对比、库存地点记录占比、卡片小图和“查看明细”按钮。

页面没有独立搜索面板和分页，表格工具栏放置工厂、库存地点下拉与料号输入框。工厂、库存地点精确匹配；切换工厂清空库存地点并更新候选地点。料号忽略大小写包含匹配，多个关键词用逗号、分号或空白分隔，支持中文分隔符，匹配任一关键词即可；各维度之间取交集。

- 表格、卡片及图表使用同一份过滤后的明细，不再提供或请求 `/summary`。表格保留工厂、库存地点、料号、三项卷数和三项数量，六个数值字段不合并、不互相推算。
- 库存数量 `wmsQty` 表示库存，线边仓数量 `reelInfoQty` 表示在制。看板不再跨所有料号累计总库存或总在制，也不使用记录数作为数量排名。
- 当前筛选范围内按非空料号分组，同料号跨工厂、库存地点的 `wmsQty` 和 `reelInfoQty` 分别累加。某项全部为空时该料号对应数量保持空；真实零及负数保留。空料号记录仍保留在表格中，不进入料号排名或极值。
- 四张卡分别显示分组后料号的最大库存、最小库存、最大在制、最小在制，并标出对应料号。同值时按料号稳定排序，显示一个代表料号及并列数量；零计入最小值，空值不参与比较，无有效数量时显示 `—`。这里的最大、最小是当前筛选范围内不同料号之间的极值，不是历史最高／最低库存或库存上限。
- 库存 Top5 按各料号 `wmsQty` 降序，在制 Top5 按各料号 `reelInfoQty` 降序；两张榜单独立选取，不共用其中一张的排序。同值按料号稳定排序，空值排除，少于 5 个按实际数量显示，零与负数不隐藏。条形与提示均展示数量，不表示记录占比。
- 源数据仍包含全部日期和类型，不自动按时间选取最新记录或去重。同料号若包含多个快照，这些记录仍会累加；上述分组不能替代业务上的快照筛选，也不能解释为盘点盈亏或已核实的实时库存。

记录数、非空工厂和料号去重数、按“工厂＋非空库存地点”配对的地点数仍可在前端数据模型中计算，页面不再单独展示原汇总范围整行。表格标题显示当前筛选记录数与全量记录数。

刷新只请求一次 `list`，随后按保留的本地条件更新明细与看板。数据变多后再评估服务端分页和聚合，届时须同步改造前后端，不能只隐藏分页或提高每页条数。此次改造无需 Migration，不更改功能权限和菜单。

## 菜单与角色接入

1. 配置 `ExternalOracle:Sources:MesEDA`（本地 User Secrets，部署使用 `ExternalOracle__Sources__MesEDA`），连接账号须能读取 `EDA.G_WMS_KEYBOARD`。配置值不进入仓库。
2. 启动新版 Identity，现有 `AuthorizationInitializer` 将 `inventory.stocktake.read` 同步进权限目录并失效旧授权缓存。
3. 使用新版前端，在“菜单管理”新建页面菜单，填写下面的配置。菜单通过管理界面创建，不加入 `DefaultMenus`。
4. 给普通角色勾选该页面的“查看页面”权限，再给用户分配角色。后端会自动为页面权限生成授权树叶子，不需要额外创建按钮菜单。
5. 用户通过“刷新菜单与权限”、刷新浏览器或重新登录获取入口。

| 配置项 | 值 |
| --- | --- |
| 类型 | 页面 |
| 中文标题 | 一键盘点报表 |
| 英文标题 | Stocktake Report |
| 上级 | 按实际业务目录选择，也可放顶层 |
| 页面路径 | 建议 `/inventory/stocktake`，须未被其他菜单使用 |
| 页面组件 | `InventoryStocktake` |
| 权限编码 | `inventory.stocktake.read`（查看一键盘点报表） |
| 页面缓存 | 建议开启 |
| 菜单显示 | 开启 |

页面首次进入自动加载全部记录；本地筛选、清除筛选均不请求服务端；刷新读取源库最新数据。开启页面缓存后返回保留筛选及结果，不自动重复加载。

## 实现位置与验证范围

- API：[InventoryStocktakeController.cs](../Services/Identity/ZX.Platform.Identity.Api/Controllers/InventoryStocktakeController.cs)。
- 输出：[DTO](../Services/Identity/ZX.Platform.Identity.Application/Inventory/Stocktake/Dtos)。
- 查询服务：[StocktakeReportService.cs](../Services/Identity/ZX.Platform.Identity.Application/Inventory/Stocktake/StocktakeReportService.cs)。
- MES 查询：[StocktakeReportRepository.cs](../Services/Identity/ZX.Platform.Identity.Infrastructure/Inventory/StocktakeReportRepository.cs)。
- 前端页面：`ZX.front.vue/src/views/inventory/stocktake/index.vue`；工具栏过滤在 `components/StocktakeFilters.vue`，过滤与聚合在 `stocktakeData.ts`，请求在 `src/api/inventory/stocktake.ts`。

2026-11-03 在早期分页版本使用本机 `MesEDA` 配置只读核对：数据库名为 `BU17EDA`、表 owner 为 `EDA`；错误前缀 `BU17EDA.G_WMS_KEYBOARD` 返回 ORA-00942，修正后可读取，当时总数为 455。探针曾读取 20 条并核对三项筛选，不输出凭据或修改数据。这项历史验证仅证明当时的源库访问与映射，不能代替当前全量接口验收。

2026-11-04 用户已部署当前全量报表并实际使用。服务端排查发现 177、178 的名称解析存在秒级等待；调整 NetworkManager 的 DNS 配置并重启各节点的 Identity 容器后，宿主机正反向解析恢复为毫秒级，容器解析命令分别为 0.087 秒和 0.101 秒（包含 Docker 执行开销）。用户反馈报表访问已明显恢复；连接串恢复 SCAN 域名后的最终复测结果尚未单独确认。

提交前完成后端编译（0 警告、0 错误）、静态核对与差异检查；本轮未重新执行数据库、自动化测试或浏览器 / HTTP 验收。前后端须一起更新，重启 Identity API 后刷新页面。
