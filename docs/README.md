# 开发文档

[项目首页](../README.md) · [开发者指南](developer-guide.md)

这里是后端文档入口。第一次接手按“启动 → 理解分层 → 维护一个业务功能”的顺序阅读；排查某个模块时直接查对应专题。

## 第一次接手

1. [开发环境](development.md#本地启动)：配置 SDK、数据库和开发证书，运行 Identity 与 Gateway。
2. [开发者指南](developer-guide.md)：理解目录职责、依赖方向、请求链和当前能力边界。
3. [数据库与持久化](database.md)：理解用户、厂区、关系表、审计与软删除。
4. [新增一个业务功能](developer-guide.md#新增一个业务功能)：从 DTO、实体和服务到接口、资源与验收的流程。

## 按任务查阅

| 任务                | 文档                             | 重点                                          |
| ------------------- | -------------------------------- | --------------------------------------------- |
| 理解架构 / 新增业务 | [开发者指南](developer-guide.md) | 四层职责、Repository / Unit of Work、开发约定 |
| 启动 / 配置 / 排错  | [开发环境](development.md)       | User Secrets、端口、HTTPS、Gateway 和 Git     |
| 修改实体 / 数据库   | [数据库与持久化](database.md)    | 唯一索引、软删除、审计、关系和 Migration      |
| 运维按当地时间查数据 | [运维查询时间](database.md#运维查询时间) | SQL Server 查询模板、Oracle 时间类型与换算参考 |
| 联调用户 / 厂区     | [Identity 接口契约](api.md)      | 路由、HTTP 方法、请求字段、分页与错误负载     |
| 理解用户 / 厂区规则 | [Identity 业务语义](identity.md) | 状态、厂区关系与当前能力边界                  |
| 处理接口成功 / 失败 | [API 响应与错误](http.md)        | ApiResponse、错误码、结构化原因与取消         |
| 增加中英文提示      | [API 多语言](localization.md)    | Accept-Language、资源、DTO 校验与异常消息键   |
| 调整请求频率        | [接口限流](rate-limiting.md)     | 配置、分段窗口、可信代理与计数边界            |
| 设计下一阶段        | [后续规划与设计取舍](roadmap.md) | 认证、厂区权限、幂等、日志和基础设施          |
| 查阅过去讨论        | [历史文档](history/README.md)    | 原交接记录与前端迁移方案                      |

## 前端仓库

前端代码和页面规范由 [ZX.front.vue](https://github.com/blairzheng/ZX.front.vue) 单独维护：

- [前端文档导航](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/README.md)。
- [统一请求层](https://github.com/blairzheng/ZX.front.vue/blob/main/docs/http.md)：Gateway、loading、错误、超时、取消及文件请求。

后端接口契约以本仓库的 Controller 和 DTO 为准；前端专题维护消费这些契约的方式，避免两边复制整套页面规范。

## 文档职责与维护

[README](../README.md) 负责项目状态、快速开始和入口；开发者指南负责架构和跨模块开发流程；专题负责对应契约；规划文档负责尚未实现的设计。

一个契约优先在一个专题维护，其他文档链接过去。修改接口时更新 Identity 接口契约、业务语义与 HTTP 专题；修改实体时更新数据库专题；修改提示或限流时更新对应专题。调整架构边界时更新开发者指南，完成新能力后更新首页和规划。

本文档在 2026-10-12 按当前源码整理。历史记录中的阶段状态和示意代码可能已经过时，以当前专题和实际源码为准。
