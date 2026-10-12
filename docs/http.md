# API 响应与错误

[文档导航](README.md) · [Identity 接口](identity.md)

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
| 请求频率超限                   |  429 | 600429 |
| 未知系统异常                   |  500 | 600500 |

业务码定义在 [ErrorCodes.cs](../Services/Identity/ZX.Platform.Identity.Application/Errors/ErrorCodes.cs)。`Forbidden` 的映射已经存在，但实际认证授权尚未接入；401 协议与业务码等待登录设计后确定。

没有数据的成功操作仍返回 `200 + ApiResponse`；204 按协议不能携带 JSON 响应体。`600xxx` 不作为 HTTP 状态使用。

当前没有自动包装所有响应的全局过滤器。Swagger、Gateway 自身 OpenAPI、WeatherForecast 模板接口、未知路由 404 和未来文件流不应被假定都返回此业务格式。Gateway 自身连接 / 转发失败也不一定带 Identity 的业务码，前端仍需处理普通 HTTP 错误。

## 校验与异常分工

| 位置                 | 处理内容                                     | 当前实现                                            |
| -------------------- | -------------------------------------------- | --------------------------------------------------- |
| DTO / MVC ModelState | 必填、长度、范围、格式、绑定和 JSON 输入错误 | `AddUnifiedApiBehavior` 返回 `400 / 600001`         |
| Application          | 查库后发现的业务问题                         | `BusinessException(Code, MessageKey, ResponseData)` |
| Domain               | 实体自身状态和规则                           | `DomainException(MessageKey)`                       |
| Infrastructure       | SQL Server 唯一约束错误                      | `UniqueConstraintException`                         |
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

附加数据不是每种错误都有。当前存在的冲突字段见[创建冲突](identity.md#创建冲突)，不要从历史设计中推断额外字段，也不要把消息文本当作 `reason`。

## 取消、超时与重复提交

Controller 的 `CancellationToken` 来自请求取消信号，继续传入 Application、Repository 和 EF Core 查询 / 保存。客户端断开连接或中止请求时，下游支持取消的操作可以响应信号。

取消是协作式的：同步计算和没有观察令牌的操作不会自动停止。数据库已经提交的变更也不会因为前端取消而撤销。一次保存中的事务与跨请求的重复提交分别处理，见[保存与事务](database.md#保存与事务)。

当前没有统一后端执行超时或固定总并发限制。请求限频按时间窗口计数，不根据执行时长计数，也不能替代幂等。SQL Server、YARP 和 HTTP 服务器仍有各自的底层超时设置。

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

取消与网络错误的验收需同时核对客户端结果和数据库实际状态，不能只观察前端错误提示。
