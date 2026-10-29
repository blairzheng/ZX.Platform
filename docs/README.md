# 开发文档

[项目首页](../README.md) · [开发者指南](developer-guide.md)

这里是后端文档入口。第一次接手按“启动 → 理解分层 → 维护一个业务功能”的顺序阅读；排查某个模块时直接查对应专题。

## 第一次接手

换电脑或新会话接续开发，先读[项目交接说明](project-handoff.md)，了解当前版本、已接受的设计和环境恢复步骤。

1. [开发环境](development.md#本地启动)：配置 SDK、数据库和开发证书，运行 Identity 与 Gateway。
2. [开发者指南](developer-guide.md)：理解目录职责、依赖方向、请求链和当前能力边界。
3. [数据库与持久化](database.md)：理解用户、SBU、关系表、审计与软删除。
4. [新增一个业务功能](developer-guide.md#新增一个业务功能)：从 DTO、实体和服务到接口、资源与验收的流程。

## 按任务查阅

| 任务                | 文档                             | 重点                                          |
| ------------------- | -------------------------------- | --------------------------------------------- |
| 理解架构 / 新增业务 | [开发者指南](developer-guide.md) | 四层职责、Repository / Unit of Work、开发约定 |
| 启动 / 配置 / 排错  | [开发环境](development.md)       | User Secrets、端口、HTTPS、Gateway 和 Git     |
| 规划生产部署        | [MOM 一期部署架构草案](deployment-topology.md) | 双应用节点、Redis Sentinel、集团 Oracle 与待确认事项 |
| 准备当前预发布 | [Docker 离线部署](docker-deployment.md) | 176 独立 HTTPS 端口、177/178 容器、配置与发布顺序；[原生方案](native-deployment.md)仅供参考 |
| 查运行和接口日志 | [运行与接口日志](logging.md) | 请求摘要、TraceId、异步控制台输出、Linux journald |
| 修改实体 / 数据库   | [数据库与持久化](database.md)    | 唯一索引、软删除、审计、关系和 Migration      |
| 执行数据库迁移 | [迁移操作手册](migration-operations.md) | 本机、测试库直接更新与正式库交 DBA 的步骤 |
| Docker 离线部署 / 数据库迁移 | [Docker 部署手册](docker-deployment.md) | 打包、load、配置、数据库初始化、管理员 SQL 和升级 |
| 一键离线打包 / 并行远程部署 / 回滚 | [部署脚本说明](../deploy/docker/README.md) | 本机 package、remote 临时 SSH/SFTP，两台并行；服务器 init、deploy、status、rollback |
| 运维按当地时间查数据 | [运维查询时间](database.md#运维查询时间) | Oracle UTC 时间类型、当地时间查询与换算 |
| 联调用户 / SBU     | [Identity 接口契约](api.md)      | 路由、HTTP 方法、请求字段、分页与错误负载     |
| 理解用户 / SBU规则 | [Identity 业务语义](identity.md) | 状态、SBU关系与当前能力边界                  |
| 处理接口成功 / 失败 | [API 响应与错误](http.md)        | ApiResponse、错误码、结构化原因与取消         |
| 增加中英文提示      | [API 多语言](localization.md)    | Accept-Language、资源、DTO 校验与异常消息键   |
| 调整请求频率        | [接口限流](rate-limiting.md)     | 配置、分段窗口、可信代理与计数边界            |
| 联调登录与会话      | [登录与会话](authentication.md) | Redis、Cookie、接管、强制下线及并发边界 |
| 管理角色与权限 | [角色与权限](authorization.md) | 管理员 SQL、权限目录、菜单树授权、动态菜单及缓存失效 |
| 设计下一阶段        | [后续规划与设计取舍](roadmap.md) | 认证、SBU权限、幂等、日志和基础设施          |
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
