# 运行与接口日志

[项目首页](../README.md) · [文档导航](README.md)

当前使用 .NET 自带的 `ILogger`、Console Provider 和 HTTP Logging 中间件。应用向标准输出写日志，当前 177、178 的 Docker 容器由 `json-file` 日志驱动收集。无需引入 Serilog、日志文件组件或 MQ。

本专题仅覆盖排错和接口调用摘要。登录历史、操作审计、字段变更历史、MQ、集中日志平台及 Prometheus 监控留待后续设计。本机开发查看终端或 IDE 输出；线上通过 `docker logs` 查询，发布任务日志在 GitLab 查看。

## 应用记录什么

- Gateway 和 Identity 每个请求完成后各输出一条合并摘要：HTTP 方法、路径、状态码及处理耗时（毫秒）。Gateway 是入口及转发耗时，Identity 是自身管道耗时，不能直接相加。此摘要不代表业务操作审计，也不包含 JSON 响应中的业务码。
- 日志 scope 包含 `TraceId`、`SpanId` 和 `ParentId`。YARP 与 ASP.NET Core 默认通过 W3C Trace Context 传播链路信息，同一请求在两个服务的 `TraceId` 相同、`SpanId` 不同；直接调用 Identity 则建立自己的链路。
- 全局异常处理器继续输出业务 / 领域警告、唯一约束冲突及系统异常堆栈。错误响应中的 `traceId` 使用完整 W3C Activity ID，其中包含 scope 的 32 位 TraceId；复制该完整值，或取中间的 TraceId 部分检索异常及请求摘要。
- HTTP Logging 显式排除查询字符串、请求头、请求体和响应体；不会为了记录日志缓冲上传或下载内容。路径本身会被记录，不要在路径中设计密码或 Token。其他业务代码仍需遵循不记录凭据、密钥及连接字符串的约定。

Identity 的 HTTP Logging 放在异常处理器之前，从而记录统一异常响应的最终 HTTP 状态，包括 400、401、403、429、500 等。日志也覆盖重定向、404 和开发环境的 Swagger 请求。Gateway 的代理失败还可从 YARP 的 Warning / Error 日志查看原因。

代码入口：

- [Identity 请求日志配置](../Services/Identity/ZX.Platform.Identity.Api/Extensions/RequestLoggingExtensions.cs)。
- [Identity 中间件顺序](../Services/Identity/ZX.Platform.Identity.Api/Program.cs)。
- [Gateway 请求日志配置](../ZX.Platform.Gateway/Extensions/RequestLoggingExtensions.cs)。
- [全局异常处理](../Services/Identity/ZX.Platform.Identity.Api/ErrorHandling/GlobalExceptionHandler.cs)。

## 格式、级别与异步输出

共享 `appsettings.json` 配置单行文本、UTC 时间戳和 scope，本机开发可直接阅读。`appsettings.Production.json` 将 Console Formatter 改为单行 JSON，保留结构化字段及 scope，异常中的换行作为 JSON 字符串转义。Identity 在 Production 将 EF Core 成功 SQL 日志降至 Warning，避免每次查询额外输出 SQL；Gateway 将 YARP 常规转发日志降至 Warning，保留统一请求摘要及代理警告 / 错误。

默认日志级别为 Information，ASP.NET Core 常规日志为 Warning，单独将 `Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware` 设为 Information，防止请求摘要被框架类别过滤。可通过配置临时提高某个类别的级别，排错后恢复；不要在生产长期打开全局 Debug 或 EF 敏感数据日志。

Console Provider 自带后台输出队列，当前配置：

```json
{
  "Logging": {
    "Console": {
      "MaxQueueLength": 1024,
      "QueueFullMode": "DropWrite"
    }
  }
}
```

请求线程仍需构造日志并入队，但不等待每条日志写到标准输出。队列满时丢弃新日志，避免等待队列空位；运行时会尝试输出丢弃提示。该队列不区分错误与普通日志，满载时错误日志也可能丢失。进程异常退出、日志轮转或磁盘问题也可能造成日志丢失，因此这不是可靠审计存储，也不保证零开销。

## 当前 Docker 日志配置

Runner 创建 `mom-identity`、`mom-gateway` 时设置 `--log-driver json-file --log-opt max-size=50m --log-opt max-file=5`，单个容器最多约 250 MB，达到限制后轮转。旧备份容器也可能占用日志空间，日志保留时长取决于写入量，不保证保留固定天数。

容器内应用输出 Production 单行 JSON，Docker 保存其标准输出；不要假定应用日志会出现在宿主机 journald 中。运行版本、发布和回滚见[Runner 发布](deployment.md)。当前没有集中日志平台或 MOM 网页日志查询页面。

## 常用查询

在需要排查的后端节点执行，177、178 分别查询：

```bash
# 最近 100 条及实时跟踪。
sudo docker logs --tail 100 mom-identity
sudo docker logs --tail 100 mom-gateway
sudo docker logs --follow --tail 100 mom-identity

# 用带时区的时间限定范围，替换为实际查询窗口。
sudo docker logs --since '2026-10-29T08:00:00+08:00' \
  --until '2026-10-29T09:00:00+08:00' mom-identity

# 按实际 TraceId 检索；Docker 日志可能同时写 stdout/stderr。
sudo docker logs --since 30m mom-identity 2>&1 | rg '替换为32位TraceId'
sudo docker logs --since 30m mom-gateway 2>&1 | rg '替换为32位TraceId'

# 查看当前日志驱动，不输出容器环境变量。
sudo docker inspect --format '{{.Name}} {{json .HostConfig.LogConfig}}' mom-identity mom-gateway
```

`rg` 仅为查询示例，服务器未安装时可用 `grep`。应用 JSON 时间戳是 UTC；查询窗口带明确时区，避免把服务器当地时间与 UTC 混淆。发布后先区分当前容器与保留的旧容器，旧版本日志需按确切容器名查询。不要通过输出完整 `docker inspect` 排查日志，以免泄露运行配置。

## 部署检查

1. 用 Production 环境启动两个服务，确认输出为 JSON，scope 有 TraceId。
2. 通过 Gateway 调用接口，确认两个服务均输出摘要；同一链路的 TraceId 一致，路径与耗时分别对应各自服务。
3. 覆盖成功、未登录、无权限、限流和系统异常，核对请求摘要状态；异常日志应能按同一 TraceId 找到。
4. 使用无敏感信息的测试标记放入查询参数、请求头、Cookie 和 JSON 请求体，确认 HTTP 摘要没有记录这些值。
5. 检查 Docker 日志驱动、轮转上限和磁盘容量；需要长期留存时另行归档。

上述检查需在目标环境确认，本机代码验证不代表已完成服务器日志验收。发布流水线的启动检查也不覆盖日志留存和轮转。

2026-10-17 本机验证：解决方案使用独立输出目录编译通过，仍有既有依赖告警；隔离 HTTP 程序加载两个项目的真实日志扩展和配置，分别验证 Development / Production 输出、200 / 201 / 400 / 401 / 403 / 429 / 500 摘要状态、异常处理后的 500、UTC 和 TraceId，以及测试查询参数、头、Cookie、请求 / 响应体标记未进入 HTTP 日志。该检查仅验证日志管道，没有重新验收认证业务、真实 Gateway 转发或 Linux journald。
