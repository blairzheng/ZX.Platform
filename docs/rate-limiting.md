# Identity 接口限流

[文档导航](README.md) · [API 响应与错误](http.md)

Identity 使用 ASP.NET Core 自带 RateLimiter，限制客户端访问频率。此功能已接入代码，后端编译和临时进程 HTTP 验证已通过；尚未进行真实业务负载的压力验收，默认额度需要按实际负载调整。

## 默认规则

| 限制     | 默认值                                     | 计数范围                                   |
| -------- | ------------------------------------------ | ------------------------------------------ |
| 请求频率 | 3 秒窗口内最多 5 次，分成 3 段滑动释放额度 | 客户端 IP + HTTP 方法 + 匹配的接口路由模板 |
| 队列     | 0                                          | 超限立即拒绝，不排队                       |

访问不同接口分别计数；修改查询条件、分页参数或路由参数值不会获得新额度。例如同一 IP 的 GET /api/Users/getUserList?page=1 和 page=2 共用频率额度；GET /api/Factories/getAllEnabledFactories 使用另一份额度。IPv4 和 IPv4 映射的 IPv6 地址统一计数。

滑动窗口按段释放额度，默认每段 1 秒，属于分段近似窗口，不保证任意精确的连续 3 秒都最多 5 次。拒绝的请求不进入 Controller；通过限流后即使参数校验或业务处理失败，也占用一次频率额度。

额度耗尽后无需手动重置，旧分段随时间移出窗口，对应次数就恢复可用。瞬间用完 5 次时，大约 3 秒后可再次请求；按段回收可能提前到分段边界，不等于任意精确连续 3 秒的严格统计。恢复只取决于时间，与之前请求是否处理完无关。

限流覆盖 Controller 接口，包括用户、厂区接口和模板示例接口；Swagger UI、Swagger 文档和未匹配接口的 404 不占用这些额度。未知路由的防刷由部署入口按需要处理。

## 配置

修改 [Identity.Api/appsettings.json](../Services/Identity/ZX.Platform.Identity.Api/appsettings.json)：

```json
"RateLimiting": {
  "PermitLimit": 5,
  "WindowSeconds": 3,
  "SegmentsPerWindow": 3
}
```

三项均必须为正整数；非法数值在启动时拒绝。修改后重启 Identity。环境变量可按 ASP.NET Core 的标准方式覆盖，例如 RateLimiting__PermitLimit。

当前只设置请求频率限制，实例总并发上限待实际压测后再决定。已有请求继续执行，直到处理完成或取消被业务操作感知；频率额度按时间恢复，与请求是否结束无关。本次没有增加统一的后端执行时间上限，前端超时不代表后端已经结束。Oracle、YARP、HTTP 服务器等底层超时继续按各自配置生效。

## 客户端 IP 与代理

本地链路为 浏览器 → Vite → Gateway → Identity。Vite 设置 xfwd: true；Gateway 接收可信上游的 X-Forwarded-For，YARP 使用 X-Forwarded: Set 重新生成转发头；Identity 再从可信 Gateway 还原 RemoteIpAddress。限流使用 RemoteIpAddress，不直接读取调用方任意提供的请求头。

Gateway 和 Identity 各自配置直接上游的可信地址：

```json
"ForwardedHeaders": {
  "KnownProxies": [ "127.0.0.1", "::1" ]
}
```

默认用于本地代理。每一层仅处理一个上游转发地址，且只还原客户端 IP，不改变现有 HTTPS 重定向行为。不能配置空列表使所有来源变成可信。Gateway 前面增加 Nginx 或负载均衡时，Gateway 填其实际连接地址；Identity 填 Gateway 的实际连接地址。不同机器、容器或多个 Gateway 的地址按真实部署调整并重启。上游代理应覆盖转发头或追加自己观察到的客户端地址。

同一公司出口 IP 下的多人共享同一接口频率额度；当前 3 秒 5 次只是初始配置。认证接入后可按实际需求改用用户标识。多实例分别计数，额度不跨实例共享；严格的集群总额度需要入口或分布式实现。

## 超限响应

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json; charset=utf-8
```

```json
{
  "code": 600429,
  "message": "请求过于频繁，请稍后再试。",
  "traceId": "..."
}
```

message 按 Accept-Language 使用中文或英文。当前滑动窗口不提供可靠的预计等待时间，响应不设置 Retry-After。

前端 BusinessCode.TooManyRequests 已与后端对齐；统一请求层显示后端提示、拒绝 Promise 并收尾 loading，不自动重试。调用方按错误码处理，不匹配文字。JSON 与文件下载都沿用同一错误规则。

限流不保证业务幂等。创建用户仍由账号唯一索引兜底；未来 MES 累加型写操作需要 RequestId 和数据库约束。前端默认 15 秒超时，长报表和下载接入时应单独调整。

## 已记录的验证

2026-10-12 限流接入时，Identity 与 Gateway 使用独立输出配置编译，均为 0 警告、0 错误。临时进程使用独立端口和无效参数，在访问数据库前返回验证错误；已确认 5 次放行、第 6 次 429、窗口恢复、IP 和接口分组、查询参数共用额度、IPv4 映射归一、可信代理链、中英文提示与 traceId。来自非可信地址的伪造转发头无法切换额度。

所有临时进程均已停止；未访问数据库或修改当前运行的服务。此验证覆盖中间件行为，不代替实际数据库请求、部署代理及服务器容量验收；前端仅格式化和静态核对。

## 人工验收

1. 重启 Gateway、Identity 和 Vite，让本次配置生效。
2. 在短于一个分段的时间内，同一 IP 向同一个只读接口连续发送 6 次请求，例如 GET /api/identity/api/Users/getUserList?page=1&pageSize=20；前 5 次能进入接口，第 6 次应是 429 / 600429。
3. 等窗口额度恢复后再次请求应能进入接口。更换 page 或 keyword 仍共用额度；更换接口应分别计数。
4. 分别传 Accept-Language: zh-CN 和 en-US，核对 429 提示、Content-Language 和 traceId。响应不虚构 Retry-After 等待时间。
5. 核对不同客户端地址分别计数，并确认非可信来源伪造 X-Forwarded-For 不能切换额度。经代理测试时必须按实际部署填可信代理地址。
6. 检查 Swagger 和未知接口仍保持原有行为。

联调完成后记录实际结果和选定额度。

## 实现入口

- [RateLimitingExtensions.cs](../Services/Identity/ZX.Platform.Identity.Api/Extensions/RateLimitingExtensions.cs)：滑动窗口分组、拒绝响应。
- [ApiRateLimitingOptions.cs](../Services/Identity/ZX.Platform.Identity.Api/Options/ApiRateLimitingOptions.cs)：默认值和正整数校验。
- [Identity 代理配置](../Services/Identity/ZX.Platform.Identity.Api/Extensions/ForwardedHeadersExtensions.cs)与 [Gateway 代理配置](../ZX.Platform.Gateway/Extensions/ForwardedHeadersExtensions.cs)：可信直接上游和一跳转发。
