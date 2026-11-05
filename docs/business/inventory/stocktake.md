# 一键盘点：后端业务说明

[业务文档目录](../README.md) · [成员文档编写规范](../../03-documentation.md)

## 功能与范围

一键盘点读取现有 MES 库存及线边仓记录，为页面展示库存数量、在制数量、卷数和按料号分析提供数据。当前只有查询能力，不生成盘点单、不修改库存，也没有审批或状态流转。

这是全局报表，不按当前 SBU 隔离。当前账号必须具有 `inventory.stocktake.read` 功能权限；不能把可查询全局盘点解释为拥有其他 SBU 业务的访问权限。

页面入口、筛选方式、图表和导出操作见独立 `ZX.front.vue` 仓库的 `docs/business/inventory/stocktake.md`。菜单路径及层级以环境中的菜单配置为准，前端页面组件标识为 `InventoryStocktake`。

## 查询契约

| 项目 | 当前行为 |
| --- | --- |
| 请求方法 | `GET` |
| Identity 路径 | `/api/InventoryStocktake/list` |
| 浏览器经 Gateway 的路径 | `/api/identity/api/InventoryStocktake/list` |
| 请求参数 | 无业务查询参数；不接收分页、工厂、库位、料号或 SBU 筛选参数 |
| 权限 | 已登录，且满足 `inventory.stocktake.read` 策略 |
| SBU | 未声明 `[RequireSbu]`，不要求选择 SBU，不按 SBU 过滤 |
| 成功响应 | 统一 `ApiResponse`，HTTP 200，`code = 0`，`data = { list, total }` |
| `list` | 本次完整读取的记录数组 |
| `total` | 本次返回记录数，等于 `list` 的长度，不是前端本地筛选后的数量 |

后端固定按工厂、库位、料号、Oracle 行标识排序。当前不做服务端分页或业务汇总，页面从这次完整结果进行本地筛选和按料号统计；不能将该特例复制为所有业务的列表模式。

## 字段与口径

响应字段采用 JSON 驼峰名称。除 `rowKey` 外，下列字段都可能为 `null`；数量字段对应后端可空 `decimal`，空值保留，不在后端换成零。

| 字段 | 含义与约定 |
| --- | --- |
| `rowKey` | 本次列表行标识，来自 Oracle ROWID；不作为业务主键或库存单据编号使用 |
| `factory` | 工厂，来自 `WERKS` |
| `storageLocation` | 库位，来自 `LGORT` |
| `material` | 料号，来自 `MATNR` |
| `wmsReelQty` | WMS 库存卷数 |
| `reelQty` | 线边仓卷数 |
| `keepQty` | 使用卷数 |
| `wmsQty` | WMS 库存物料数量 |
| `reelInfoQty` | 线边仓在制物料数量 |
| `keepInfoQty` | 使用物料数量 |
| `createDate` | 源库创建时间，格式 `yyyy-MM-dd HH:mm:ss`，没有时区信息 |
| `updateDate` | 源库更新时间，格式同上，没有时区信息 |
| `workDate` | 源库工作日期文本，按原字段读取，没有新增格式或时区转换 |
| `dataType` | 源库数据类型文本，当前接口没有额外声明枚举含义 |

数量和卷数是不同指标，不能混用。不同料号的计量口径不能直接视为一致，不能机械相加为一个总库存。页面的同料号汇总、极值及 Top5 规则由前端业务文档说明，后端没有为这些结果提供单独接口。

MES 的创建、更新时间按源库无时区时间返回，不添加 `Z`，不冒充 UTC，也不要求浏览器按本地时区换算。

## 数据来源与限制

仓储通过服务端固定别名 `MesEDA` 读取 `EDA.G_WMS_KEYBOARD`。客户端不能指定数据源、表名或 SQL。查询命令超时为 10 秒，并向下传递取消令牌；连接与查询失败沿用平台统一异常处理，失败不等于查询成功且库存为零。

该模块不创建本地业务表，不需要为此报表新增实体映射或 Migration。数据源配置方法沿用现有系统配置文档，本业务文档不记录真实连接串、账号或密码。

当前结果反映读取时数据源可见的记录，未提供历史快照、单价、用料计划或指定时点查询。因此不能据此承诺历史趋势、库存金额或可生产天数。页面本地筛选不能减少后端的全量读取；数据量增加后是否改为服务端筛选、分页或聚合，需要另行设计契约。

## AI 查询

现有查询方法已标记 `[AiQuery]`，助手可在当前账号拥有原业务权限时通过统一查询目录发现并执行它，不新增专用盘点 AI 接口。执行仍由后端校验原权限，查询不获得修改或删除能力。

AI 的筛选和全局计算必须基于工具确认的完整结果，遵守容量限制；失败、空记录或缺失字段不能当作数量零。AI 工具机制、接入步骤及容量限制统一见 [AI 接入文档](../../ai-integration.md)，本页只维护该业务的查询范围和计算口径。

## 维护入口与验证状态

| 职责 | 源码 |
| --- | --- |
| 路由、响应、权限与 AI 标记 | [InventoryStocktakeController.cs](../../../Services/Identity/ZX.Platform.Identity.Api/Controllers/InventoryStocktakeController.cs) |
| 业务服务 | [StocktakeReportService.cs](../../../Services/Identity/ZX.Platform.Identity.Application/Inventory/Stocktake/StocktakeReportService.cs) |
| 数据来源、读取与排序 | [StocktakeReportRepository.cs](../../../Services/Identity/ZX.Platform.Identity.Infrastructure/Inventory/StocktakeReportRepository.cs) |
| 行字段与空值约定 | [StocktakeRowDto.cs](../../../Services/Identity/ZX.Platform.Identity.Application/Inventory/Stocktake/Dtos/StocktakeRowDto.cs) |
| 列表结构 | [StocktakeListDto.cs](../../../Services/Identity/ZX.Platform.Identity.Application/Inventory/Stocktake/Dtos/StocktakeListDto.cs) |

本页依据当前源码静态整理，未运行构建、测试或真实数据库、接口及 AI 调用。接口契约、字段口径、权限或数据范围改变时更新本页；实际联调结果和未验证事项在对应提交或合并请求中如实说明。
