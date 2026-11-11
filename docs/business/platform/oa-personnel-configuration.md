# OA 人员同步配置

本功能通过独立的只读服务账号访问 LDAP，沿用 `AuthenticationLdap` 的目录来源、主备服务器、BaseDn、属性与传输设置。同步直接在 MOM 用户表 `ZX_IDENTITY_USER` 创建缺失的待审批 OA 账号，并保存组织元数据。`OaPersonnelSync.Enabled` 单独控制人员同步，不要求开启 OA 密码登录；员工实际使用 OA 账号登录时，仍须开启原有 `AuthenticationLdap.Enabled` 并使用本人的 OA 密码。

## 服务端配置

在 Identity.Api 的本机 User Secrets 或目标服务器受保护的环境配置中提供以下内容。两台 Identity 节点使用相同目录来源和 BU 范围；服务账号需要读取该范围内的人员及必要属性。下面仅是占位模板，不能把真实凭据加入源码或前端配置。

```json
{
  "OaPersonnelSync": {
    "Enabled": true,
    "BindUser": "只读目录服务账号",
    "BindPassword": "通过受保护配置提供",
    "BusinessUnit": "BU17",
    "PageSize": 200,
    "MaxPeople": 10000,
    "TotalTimeoutSeconds": 90
  }
}
```

容器环境变量对应 `OaPersonnelSync__Enabled`、`OaPersonnelSync__BindUser`、`OaPersonnelSync__BindPassword` 等。新增配置不会自动传入容器，部署时需将这些受保护变量传给 Identity 进程；浏览器无需也不能获得这些值。

通过 `mom_img` GitLab 流水线发布时，先更新该仓库的变量映射，再配置以下 CI/CD 变量并新建后端发布流水线。类型使用普通 Variable，值不加引号、不含换行，关闭变量展开；密码设为 Masked。同步和复用的 `MOM_LDAP_*` 变量环境范围统一使用 `*`，以便没有 environment 的 `release_check` 读取完整配置。配置在发布时写入两台 Identity 容器，不能只添加变量后重试仍使用旧 YAML 的任务。

| GitLab 变量 | Identity 环境变量 | 填写内容 |
| --- | --- | --- |
| `MOM_OA_PERSONNEL_SYNC_ENABLED` | `OaPersonnelSync__Enabled` | 启用填 `true`，默认 `false` |
| `MOM_OA_PERSONNEL_SYNC_BIND_USER` | `OaPersonnelSync__BindUser` | 启用时必填，只读目录服务账号 |
| `MOM_OA_PERSONNEL_SYNC_BIND_PASSWORD` | `OaPersonnelSync__BindPassword` | 启用时必填，对应服务账号密码 |
| `MOM_OA_PERSONNEL_SYNC_BUSINESS_UNIT` | `OaPersonnelSync__BusinessUnit` | 可选，默认 `BU17` |
| `MOM_OA_PERSONNEL_SYNC_PAGE_SIZE` | `OaPersonnelSync__PageSize` | 可选，默认 `200` |
| `MOM_OA_PERSONNEL_SYNC_MAX_PEOPLE` | `OaPersonnelSync__MaxPeople` | 可选，默认 `10000` |
| `MOM_OA_PERSONNEL_SYNC_TOTAL_TIMEOUT_SECONDS` | `OaPersonnelSync__TotalTimeoutSeconds` | 可选，默认 `90` |

已有 `MOM_LDAP_*` 变量继续提供共享目录和传输设置；流水线在 OA 登录或人员同步任一启用时传入。人员同步账号使用上表的独立变量，不恢复原 OA 登录已移除的 `MOM_LDAP_BIND_DN` / `MOM_LDAP_BIND_PASSWORD`。本次已核对静态映射，未连接 GitLab、LDAP 或执行发布。

共享的 `AuthenticationLdap` 配置需要有合法的 `Provider`、`BaseDn`、`Servers` 和属性名。默认工号及目录账号属性均为 `sAMAccountName`，稳定身份属性必须为 `objectGUID`；MOM 账号使用工号，按字符串保留字母和前导零。目录账号另存为 OA 元数据。保持既有 `Provider` 与属性口径，不能为同步随机修改身份来源。`EmployeeLookupAttribute` 是单人登录查询配置，批量同步不使用它。

支持 `Ldaps`、`StartTls`，保留平台证书验证。当前环境使用已批准的 Plain/389 传输时，沿用 `AuthenticationLdap:AllowPlainText=true` 的显式设置；未设置时同步拒绝执行。服务账号仅用于同步，不能替代员工的 OA 密码认证。

## 范围及完整性

- 固定从配置的 `BaseDn` 向下搜索 AD `person` / `user` 对象，并且要求部门字段以 `,BU17` 结尾（BU 值来自服务端配置）。不以账号所在 OU 判断业务归属，不接收客户端指定的服务器、BaseDn 或过滤器。
- `PageSize` 允许 1～1000，`MaxPeople` 允许 1～50000，`TotalTimeoutSeconds` 允许 5～180 秒；默认只同步 BU17。总预算包含排队、连接、全部分页及主备切换。
- 每页必须包含分页响应控制，且最终 cookie 为空才认定读取完整；结果超限、目录返回截断、referral 或不完整分页时整次读取失败，不把半份人员数据交给入库用例。
- 连接失败时可从备用节点重新完整查询，已读的部分结果丢弃。服务账号凭据被明确拒绝后立即停止，不向备用节点重复尝试同一错误密码。
- 缺少有效 objectGUID、账号或工号的记录不伪造身份，由应用层计入无效记录；目录启用状态缺失时保留 `Unknown`。同步账号数量不能直接视为 HR 在职人数。

新增账号默认 `Pending`，仅目录 `Enabled` 时启用，不生成本地密码、不分配角色或 SBU，同批记录 `Provisioned` 建档审计。已有账号不重复创建或改变认证授权；只有已绑定且目录来源、GUID、工号完全相符、尚未保存组织资料的账号，才允许首次补充 OA 元数据。首次补资料使用 `OaSyncedAtUtc` 并发令牌防止争用覆盖，冲突返回 409，保留 `SecurityVersion`。

未绑定预开通账号、仍占用登录名的本地账号、已删除 OA 账号和身份冲突账号保持原状。已删除本地账号若此前已释放标准化登录名，原名可创建独立 OA 用户，不继承旧权限；同步不会主动释放尚未释放的账号名。完整去重及计数规则、13 个新增可空字段和权限见 [同步与组织视图](oa-personnel.md)。

配置缺失或不合法时同步接口返回受控不可用提示，应用启动不连接 LDAP。直接写入用户表版本需要先应用 `20261111081842_AddOaPersonnelToUsers` 迁移；2026-11-11 已经用户授权应用到 `SAJET` 测试库，并核验迁移历史、13 个 OA 字段、索引及约束完整。本次未操作正式库，正式发布仍需由负责人应用该迁移。尚未执行真实 LDAP 同步，部署后仍需验证目录权限、分页、目标网络及本人 OA 登录。服务账号读取成功不能替代员工本人密码认证。
