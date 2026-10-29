# API 响应与错误

[文档导航](README.md) · [Identity 接口契约](api.md) · [Identity 业务语义](identity.md)

当前 Identity 普通业务 JSON 接口显式返回统一响应。HTTP 状态表示协议层的结果，JSON `code` 表示业务分类；调用方同时判断两者，不能通过匹配提示文字决定操作。

## 响应结构

```json
{
  "code": 0,
  "message": "操作成功。",
  "data": {},
  "traceId": "..."
}
```

| 字段      | 含义                                               |
| --------- | -------------------------------------------------- |
| `code`    | 成功为 0，失败为 `600xxx`                          |
| `message` | 给人看的提示，按请求语言选择                       |
| `data`    | 查询数据、创建返回的 Guid 或结构化错误信息；可省略 |
| `traceId` | 请求与日志的关联编号，主要在失败时返回；可省略     |

[ApiResponse.cs](../Services/Identity/ZX.Platform.Identity.Api/Contracts/ApiResponse.cs) 定义无数据的 `ApiResponse` 和继承它的 `ApiResponse<T>`。JSON 使用 `camelCase`，空值字段省略。错误的 traceId 优先使用 `Activity.Current?.Id`，否则使用 `HttpContext.TraceIdentifier`。

MVC 的 `AddJsonOptions` 与异常处理、限流使用的 `WriteAsJsonAsync` 读取不同的配置；[Program.cs](../Services/Identity/ZX.Platform.Identity.Api/Program.cs) 通过 `AddJsonOptions` 和 `ConfigureHttpJsonOptions` 分别设置 `WhenWritingNull`，使两条响应路径统一省略空值。泛型 `Data` 不标注 `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`：无约束的 `T?` 在 `T` 为 `Guid` 时仍是不可空值类型，使用该特性会导致序列化异常。全局空值规则不影响 `0`、`false` 等非空值的输出。

## 时间字段格式

创建、修改等事件时间使用 `DateTimeOffset`，数据库和领域层统一保存 UTC（见[数据库与持久化](database.md)）。接口使用 System.Text.Json 默认的 ISO 8601 格式，例如 `2026-10-13T03:09:30.9701695+00:00`，保留偏移量和小数秒；`+00:00` 和 `Z` 都表示 UTC。

当前不再使用自定义 UTC JSON 转换器。审计时间通过 `TimeProvider.GetUtcNow()` 生成，查询时间边界通过 `ToUniversalTime()` 显式归一化；默认 JSON 序列化负责保留时间值及偏移量，不负责自动转换时区。

接口传递实际时刻，不按服务器所在地区换算。前端公共 `formatDateTime` 默认跟随用户电脑时区：上例在中国时区显示为 2026-10-13 11:09，在越南时区显示为 10:09。SBU业务需要固定显示时区时显式传入 `timeZone`，例如 `Asia/Ho_Chi_Minh`。语言切换只改变呈现格式，不改变时区。

同一页面的时间显示与日期范围筛选必须使用同一时区；若改为固定业务时区，查询边界也必须按该时区的当地零点换算为 UTC，不能继续沿用电脑本地零点。

当前用户和SBU接口的审计时间由后端生成，不接收前端填写的时间。后续新增时间输入接口时，应要求表示实际时刻的字段携带 `Z` / 偏移量，并在保存前显式转成 UTC；默认反序列化不替业务自动归一化偏移量。不要通过全局转换器猜测无偏移时间所属的时区。

仅表示日期的字段（例如业务日期）使用 `DateOnly` / `yyyy-MM-dd`，不按时间点转换。重复推送等定时功能尚未实现，后续应保存本地执行时间与业务时区，再计算下一次 UTC 执行时间。

时间传输格式与页面显示格式分别维护；接口不返回无偏移的 `yyyy-MM-dd HH:mm:ss`，前端不手动加减小时数。

运维直接查库时的当地时间显示、日期范围条件及 Oracle 类型参考，见[运维查询时间](database.md#运维查询时间)。

## HTTP 与业务码

| 场景                           | HTTP |   code |
| ------------------------------ | ---: | -----: |
| 查询 / 修改 / 单条状态操作成功 |  200 |      0 |
| 创建成功                       |  201 |      0 |
| 参数验证失败                   |  400 | 600001 |
| 查询的业务对象不存在           |  404 | 600002 |
| 数据冲突                       |  409 | 600003 |
| 状态不允许 / DomainException   |  409 | 600004 |
| 业务禁止操作                   |  403 | 600005 |
| 未登录 / 凭据无效 / 会话失效    |  401 | 600401 |
| 请求频率超限                   |  429 | 600429 |
| 未知系统异常                   |  500 | 600500 |
| 认证会话存储暂不可用           |  503 | 600503 |

业务码定义在 [ErrorCodes.cs](../Services/Identity/ZX.Platform.Identity.Application/Errors/ErrorCodes.cs)。认证失败返回 `401 / 600401` 和 `WWW-Authenticate: Bearer`；已登录但无权限返回 `403 / 600005`。JwtBearer 的 Challenge / Forbidden 事件与业务异常使用相同本地化 JSON 结构，见[登录与会话](authentication.md)。

没有数据的成功操作仍返回 `200 + ApiResponse`；204 按协议不能携带 JSON 响应体。`600xxx` 不作为 HTTP 状态使用。

当前没有自动包装所有响应的全局过滤器。Swagger、Gateway 自身 OpenAPI、WeatherForecast 模板接口、未知路由和未来文件流不应被假定都返回此业务格式。默认登录策略下，未认证请求未知路由也可能先收到 401；已认证未知路由仍可返回普通 404。Gateway 自身连接 / 转发失败也不一定带 Identity 的业务码，前端仍需处理普通 HTTP 错误。

## 校验与异常分工

| 位置                 | 处理内容                                     | 当前实现                                            |
| -------------------- | -------------------------------------------- | --------------------------------------------------- |
| DTO / MVC ModelState | 必填、长度、范围、格式、绑定和 JSON 输入错误 | `AddUnifiedApiBehavior` 返回 `400 / 600001`         |
| Application          | 查库后发现的业务问题                         | `BusinessException(Code, MessageKey, ResponseData)` |
| Domain               | 实体自身状态和规则                           | `DomainException(MessageKey)`                       |
| Infrastructure       | Oracle 唯一约束错误                      | `UniqueConstraintException`                         |
| API 全局处理器       | 映射异常、翻译提示、记录日志                 | `GlobalExceptionHandler`                            |

`[ApiController]` 在动作执行前处理无效 ModelState；统一校验扩展去重并拼接校验提示，返回 traceId。JSON 解析异常详情不直接展示给调用方。

Domain 不引用 Application，内层也不引用 API 本地化服务。业务与领域异常携带稳定的消息键，由 API 翻译。当前没有早期示例中的 `BusinessErrorType` 枚举或静态异常工厂，新增调用使用真实构造函数。

```csharp
throw new BusinessException(
    ErrorCodes.NotFound,
    "User.NotFound");
```

未知异常记录完整异常日志，客户端只获得通用系统提示和 traceId。业务与领域异常记录 Warning；数据库唯一冲突记录 Warning 并映射为通用 `409 / 600003`。缺失业务 / 领域消息键时记录警告并返回本地化兜底，具体流程见[多语言](localization.md)。

源码：[ApiBehaviorExtensions.cs](../Services/Identity/ZX.Platform.Identity.Api/Extensions/ApiBehaviorExtensions.cs)、[GlobalExceptionHandler.cs](../Services/Identity/ZX.Platform.Identity.Api/ErrorHandling/GlobalExceptionHandler.cs)。

## 结构化错误

同一大类 code 可以搭配不同 `data.reason`，例如：

```json
{
  "code": 600003,
  "message": "该账号曾被删除，不能重复创建，请联系管理员恢复原用户或使用其他账号。",
  "data": {
    "reason": "AccountDeleted",
    "userId": "11111111-1111-1111-1111-111111111111"
  },
  "traceId": "..."
}
```

附加数据不是每种错误都有。当前存在的冲突字段见[创建冲突](api.md#创建冲突)，不要从历史设计中推断额外字段，也不要把消息文本当作 `reason`。

## 取消、超时与重复提交

Controller 的 `CancellationToken` 来自请求取消信号，继续传入 Application、Repository 和 EF Core 查询 / 保存。客户端断开连接或中止请求时，下游支持取消的操作可以响应信号。

取消是协作式的：同步计算和没有观察令牌的操作不会自动停止。数据库已经提交的变更也不会因为前端取消而撤销。一次保存中的事务与跨请求的重复提交分别处理，见[保存与事务](database.md#保存与事务)。

禁用、删除、改密码的会话撤销不使用请求取消令牌：这些安全变更一旦进入保存阶段，即使客户端断开或保存结果不确定，仍尝试撤销旧会话。Redis 失败返回 503，数据库可能已提交，恢复后应检查状态并重试下线；没有跨数据库与 Redis 的事务或用户锁。并发边界见[登录与会话](authentication.md#禁用删除和改密码)。

当前没有统一后端执行超时或固定总并发限制。请求限频按时间窗口计数，不根据执行时长计数，也不能替代幂等。Oracle、YARP 和 HTTP 服务器仍有各自的底层超时设置。

前端统一请求层默认超时 15 秒，合法长任务可按单次请求调整；超时 / 网络错误时先确认保存结果，不无条件重试写请求。当前创建用户依靠账号唯一约束防止重复记录，不保证重复请求返回第一次的成功结果。

## 验收要点

在测试环境使用 Swagger 或接口工具，结合真实数据库检查：

1. 创建成功 `201 / 0`，查询和修改成功 `200 / 0`，无数据操作返回 JSON。
2. 必填 / 长度 / 范围 / 无效 Guid 字符串 / JSON 绑定错误返回 `400 / 600001`。
3. 不存在对象返回 `404 / 600002`；已删除 / 已禁用标识创建冲突携带实际结构化字段。
4. 并发唯一冲突返回通用 `409 / 600003`，状态异常返回约定的错误。
5. 系统错误包含 traceId，可关联服务日志，响应不含 SQL、连接字符串或堆栈。
6. 中英文请求的 HTTP、code 和结构化字段一致，提示与 Content-Language 跟随选择。
7. 频率超限返回 `429 / 600429`，随时间恢复；具体步骤见[限流验收](rate-limiting.md#人工验收)。
8. 时间字段使用带 `Z` 或偏移量的 ISO 8601；数据库和领域层统一保存 UTC，调用方按业务显示时区格式化，不能重复加减小时。

取消与网络错误的验收需同时核对客户端结果和数据库实际状态，不能只观察前端错误提示。
