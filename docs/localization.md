# API 多语言

[文档导航](README.md) · [API 响应与错误](http.md)

Identity 已支持 `zh-CN` 和 `en-US` 的接口提示。翻译只影响展示消息，不改变 HTTP 状态、业务码、`data.reason`、业务数据或 traceId。

## 请求流程

```text
前端界面语言
→ 请求头 Accept-Language
→ UseRequestLocalization 选择本次请求文化
→ Controller / MVC 校验 / 异常处理器查找对应资源
→ JSON message + Content-Language
```

只使用 `AcceptLanguageHeaderRequestCultureProvider`，URL 和 Cookie 不参与语言覆盖。缺少请求头或无法匹配支持语言时回退中文；请求语言由 .NET 请求本地化中间件协商，响应的 `Content-Language` 标识实际选择。

前端每次请求发送当前界面语言，调用方明确设置的同名请求头优先。切换界面语言不会重发已经发送的请求；账号、显示名称和厂区名称等业务数据不会自动翻译。

## 注册与中间件

`Program.cs` 的配套设置包括：

- `AddApiLocalization()`：注册 `AddLocalization`、资源目录、消息查找服务和语言选项。
- `AddDataAnnotationsLocalization()`：让 DTO 的错误键使用 `ValidationMessages` 翻译。
- `AddUnifiedApiBehavior()`：把模型验证失败统一为业务响应。
- `UseRequestLocalization()`：在异常处理和 MVC 之前设置当前语言。

因此仅添加 `app.UseRequestLocalization()` 不会自动创建资源或替换已有硬编码提示。服务注册、语言选项、资源和调用位置必须配套。

[LocalizationExtensions.cs](../Services/Identity/ZX.Platform.Identity.Api/Extensions/LocalizationExtensions.cs) 还配置了模型绑定提示，并关闭 JSON 输入异常细节的直接展示。

## 资源按职责拆分

标记类位于 Api 的 `Localization/`，资源位于 `Resources/Localization/`：

| 标记类               | 消息键             | 默认中文资源              | 英文资源                        |
| -------------------- | ------------------ | ------------------------- | ------------------------------- |
| `CommonMessages`     | `Error.*` 等公共键 | `CommonMessages.resx`     | `CommonMessages.en-US.resx`     |
| `ValidationMessages` | `Validation.*`     | `ValidationMessages.resx` | `ValidationMessages.en-US.resx` |
| `UserMessages`       | `User.*`           | `UserMessages.resx`       | `UserMessages.en-US.resx`       |
| `FactoryMessages`    | `Factory.*`        | `FactoryMessages.resx`    | `FactoryMessages.en-US.resx`    |

默认资源本身是中文，不额外依赖一个 `zh-CN` 文件。类型名、命名空间、目录和资源文件名保持匹配，资源由 .NET 编译装载。

[ApiMessageLocalizer](../Services/Identity/ZX.Platform.Identity.Api/Localization/ApiMessageLocalizer.cs) 实现 `IApiMessageLocalizer`，按 `Validation.`、`User.`、`Factory.` 前缀选择资源，其余使用公共组。它是消息查找入口，不把各模块内容重新合并到一个大文件。

## 成功提示与异常提示

Controller 注入 `IApiMessageLocalizer`，读取成功资源：

```csharp
Message = _messages["User.Created"].Value;
```

Application 只携带资源键，不直接取得翻译：

```csharp
throw new BusinessException(
    ErrorCodes.NotFound,
    "User.NotFound");
```

Domain 使用自己的异常，同样只携带键：

```csharp
throw new DomainException("User.RestoreBeforeEnable");
```

`GlobalExceptionHandler` 最后翻译 `MessageKey`，维持内层对 API 的独立性。业务异常键缺失时使用 `Error.OperationFailed`，领域异常键缺失时使用 `Error.InvalidState`，并记录警告。

该缺键兜底是异常处理器的行为；Controller 直接查资源时不能假定也有同一兜底，因此新增成功键同样要核对资源齐全。

## DTO 校验提示

DTO 不引用 API 或具体资源类，DataAnnotations 的 `ErrorMessage` 写稳定键：

```csharp
[Required(ErrorMessage = "Validation.DisplayNameRequired")]
[MaxLength(100, ErrorMessage = "Validation.DisplayNameMaxLength")]
public string DisplayName { get; set; } = string.Empty;
```

MVC 使用 `IStringLocalizer<ValidationMessages>` 查找提示，按验证属性规则填充长度 / 范围等占位参数。参数绑定、缺少请求体和分页溢出也用校验组的资源，不通过匹配中文判断错误类型。

## 新增一条消息

1. 选对应资源组，增加稳定键，例如 `User.NotFound`。
2. 在默认中文和 `en-US` 资源中同时增加同一键，保留相同格式占位参数。
3. 成功提示在 Controller 引用；业务或领域错误在异常中引用；DTO 校验使用 `Validation.*`。
4. 核对调用键、拼写、资源文件名与占位参数，分别发中文和英文请求检查。

`.resx` 条目示例：

```xml
<data name="User.NotFound" xml:space="preserve">
  <value>用户不存在。</value>
</data>
```

## 新增业务资源组

随着模块增长，新增对应的资源标记类和中英文文件。若继续通过 `IApiMessageLocalizer` 访问新组，需要同时在 `ApiMessageLocalizer` 注入该组的 `IStringLocalizer<T>` 并添加前缀分派，否则新键会落到公共资源。

新微服务拥有自己的 API 资源和语言注册，不为了共享少量提示让 Domain / Application 反向引用 Identity.Api。

## 验收

用 `Accept-Language: zh-CN`、`en-US`、缺失头和不支持语言检查成功、DTO 校验、绑定错误、对象不存在、唯一冲突与限流响应。确认语言变化只影响 message / Content-Language，code 和结构化字段仍相同。

源码入口：[资源目录](../Services/Identity/ZX.Platform.Identity.Api/Resources/Localization)、[语言注册](../Services/Identity/ZX.Platform.Identity.Api/Extensions/LocalizationExtensions.cs)、[全局异常处理](../Services/Identity/ZX.Platform.Identity.Api/ErrorHandling/GlobalExceptionHandler.cs)。
