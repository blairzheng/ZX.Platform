# 运行与接口日志

[项目首页](../README.md) · [文档导航](README.md)

当前使用 .NET 自带的 `ILogger`、Console Provider 和 HTTP Logging 中间件。应用向标准输出写日志；在 Linux 上由 systemd 启动时，交给 journald 收集。无需引入 Serilog、日志文件组件或 MQ。

本专题仅覆盖排错和接口调用摘要。登录历史、操作审计、字段变更历史、MQ、集中日志平台及 Prometheus 监控留待后续设计。本机 Windows 开发仍看终端或 Visual Studio 输出；journald 配置需要到 Linux 部署环境实施。

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

请求线程仍需构造日志并入队，但不等待每条日志写入 journald。队列满时丢弃新日志，避免等待队列空位；运行时会尝试输出丢弃提示。该队列不区分错误与普通日志，满载时错误日志也可能丢失。进程异常退出、journald 限频或磁盘问题也可能造成日志丢失，因此这不是可靠审计存储，也不保证零开销。

## systemd 接收标准输出

假设已有部署可用的 `zx-identity.service` 和 `zx-gateway.service`。以下仅为日志配置，不能代替完整的服务部署、连接字符串、JWT 密钥、证书和反向代理配置。

在 Linux 执行 `sudo systemctl edit zx-identity.service`，补充 drop-in：

```ini
[Service]
Environment=ASPNETCORE_ENVIRONMENT=Production
StandardOutput=journal
StandardError=journal
SyslogIdentifier=zx-identity
```

Gateway 同样配置，将服务名和 `SyslogIdentifier` 改为 `zx-gateway`。确认现有配置没有用 `DOTNET_ENVIRONMENT` 覆盖成其他环境；更新后：

```bash
sudo systemctl daemon-reload
sudo systemctl restart zx-identity.service zx-gateway.service
```

应用日志中的 UTC 时间戳与 journal 显示时间可能使用不同的时区。查询时加 `--utc` 可统一查看；不加时通常按服务器当地时区展示。

## journald 持久化和容量

在 `/etc/systemd/journald.conf.d/zx-platform.conf` 配置，例如：

```ini
[Journal]
Storage=persistent
SystemMaxUse=1G
SystemKeepFree=1G
RuntimeMaxUse=128M
MaxRetentionSec=14day
```

这些是**整台服务器共享 journal 的限制**，不只是本平台：容量上限和最长保留时间共同约束，不能保证一定保留满 14 天。根据磁盘、请求量和其他服务调整；持久化目录通常为 `/var/log/journal`。

保存配置后执行：

```bash
sudo systemctl restart systemd-journald
sudo journalctl --flush
sudo journalctl --disk-usage
```

`Storage=persistent` 加持久化目录可保留正常重启前的历史；这不等于断电零丢失，也不能替代备份。journald 本身有日志限频，突发高日志量时可能看到 suppressed messages，需要结合流量评估，不建议直接关闭保护。

## 常用查询

```bash
# 最近 100 条及实时跟踪
sudo journalctl -u zx-identity.service -n 100 -o cat --no-pager
sudo journalctl -u zx-identity.service -f -o cat

# 同时查看两个服务，按 UTC 限定时间
sudo journalctl -u zx-identity.service -u zx-gateway.service --utc \
  --since '2026-10-17 08:00:00 UTC' --until '2026-10-17 09:00:00 UTC' -o cat

# 替换为实际 TraceId，跨两个服务查询
sudo journalctl -u zx-identity.service -u zx-gateway.service \
  --since today --grep='替换为32位TraceId' -o cat

# Production JSON 中筛选 Error / Critical
sudo journalctl -u zx-identity.service --since today \
  --grep='"LogLevel":"(Error|Critical)"' -o cat

# 检查历史启动记录和占用
sudo journalctl --list-boots
sudo journalctl --disk-usage
```

Console Provider 将 JSON 文本写到标准输出，JSON 的 `LogLevel` 不会自动变成 journal 的 `PRIORITY` 字段。不要只依赖 `journalctl -p err` 筛选 .NET 应用错误；按消息中的 `LogLevel` 或 TraceId 查。`-o cat` 只显示应用消息，去掉后可同时看到 journal 的时间、主机及进程信息。

需要网页查看时，可由运维安装 Cockpit 等支持 journal 浏览的工具，使用服务器授权账号查看；当前没有在平台管理端新增日志页面、Linux 命令执行接口或远程 journal 读取接口。

## 部署检查

1. 用 Production 环境启动两个服务，确认输出为 JSON，scope 有 TraceId。
2. 通过 Gateway 调用接口，确认两个服务均输出摘要；同一链路的 TraceId 一致，路径与耗时分别对应各自服务。
3. 覆盖成功、未登录、无权限、限流和系统异常，核对请求摘要状态；异常日志应能按同一 TraceId 找到。
4. 使用无敏感信息的测试标记放入查询参数、请求头、Cookie 和 JSON 请求体，确认 HTTP 摘要没有记录这些值。
5. 检查 journal 容量、文件权限和持久化；在允许的维护窗口验证重启后可查历史。

上述 Linux / systemd 步骤是部署说明，本机 Windows 修改代码并不代表已完成服务器验收。若改为 Docker 部署，标准输出通常由 Docker 日志驱动接收；使用 `docker logs` 并配置轮转，不能假定应用日志自动出现在宿主机 journald。

2026-10-17 本机验证：解决方案使用独立输出目录编译通过，仍有既有依赖告警；隔离 HTTP 程序加载两个项目的真实日志扩展和配置，分别验证 Development / Production 输出、200 / 201 / 400 / 401 / 403 / 429 / 500 摘要状态、异常处理后的 500、UTC 和 TraceId，以及测试查询参数、头、Cookie、请求 / 响应体标记未进入 HTTP 日志。该检查仅验证日志管道，没有重新验收认证业务、真实 Gateway 转发或 Linux journald。
