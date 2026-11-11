# 内置菜单越南语翻译清单

[返回项目首页](../README.md) · [业务开发步骤](02-business.md)

状态：**初稿，待熟悉越南语的同事校译**。本文件列出内置菜单的稳定 `Code` 与三语内容，供复核与按环境维护。

## 1. 用途与边界

- 表中 `Code` 是内置菜单的稳定标识，见 `Services/Identity/ZX.Platform.Identity.Infrastructure/Persistence/DefaultMenus.cs`。菜单编码在界面不可修改，用它定位数据不会误伤自定义菜单。
- 新增数据库由 `DefaultMenus.AddMissing` 按本表写入；**已存在的环境不会自动更新**，需要 DBA 执行 `deploy/database/maintain-vietnamese-menu-translations.sql`（只填充空的 `TITLE_VI`，保留已有翻译，可重复执行）。
- 内置菜单在管理界面为只读，翻译只能通过上述脚本或数据导入维护；EF 迁移只负责加列，不改菜单和授权数据。
- 本文件只覆盖内置菜单。自定义菜单由管理员在菜单管理界面维护；`LAUNCHER_DESCRIPTION_VI`（宫格说明）内置项默认留空，展示时回退到语言包文案。

## 2. 目录与页面

| Code | 中文 | English | Tiếng Việt |
| --- | --- | --- | --- |
| `system` | 系统管理 | System | Quản trị hệ thống |
| `access` | 权限管理 | Access control | Quản lý quyền |
| `users` | 用户管理 | Users | Quản lý người dùng |
| `sbus` | SBU管理 | SBU management | Quản lý SBU |
| `roles` | 角色管理 | Roles | Quản lý vai trò |
| `menus` | 菜单管理 | Menus | Quản lý menu |

## 3. 按钮权限

越南语与 `AccessMessages.vi-VN.resx` 的 `Access.Permission.*` 保持一致，同一权限在权限树和权限下拉里不会出现两种说法。

| Code | 中文 | English | Tiếng Việt |
| --- | --- | --- | --- |
| `permission_identity_user_read` | 查看用户 | View users | Xem người dùng |
| `permission_identity_user_create` | 新增用户 | Create users | Thêm người dùng |
| `permission_identity_user_rename` | 修改他人名称 | Rename other users | Sửa tên người khác |
| `permission_identity_user_status` | 启用或禁用用户 | Enable or disable users | Kích hoạt hoặc vô hiệu hóa người dùng |
| `permission_identity_user_delete` | 删除用户 | Delete users | Xóa người dùng |
| `permission_identity_user_restore` | 恢复用户 | Restore users | Khôi phục người dùng |
| `permission_identity_user_sbus` | 分配用户SBU | Assign user SBUs | Gán SBU cho người dùng |
| `permission_identity_user_roles` | 分配用户角色 | Assign user roles | Gán vai trò cho người dùng |
| `permission_identity_user_forceLogout` | 强制用户下线 | Force users offline | Buộc người dùng đăng xuất |
| `permission_identity_user_approve` | 审批OA账号 | Approve OA accounts | Phê duyệt kích hoạt tài khoản OA |
| `permission_identity_sbu_read` | 查看SBU | View SBUs | Xem SBU |
| `permission_identity_sbu_create` | 新增SBU | Create SBUs | Thêm SBU |
| `permission_identity_sbu_rename` | 修改SBU名称 | Rename SBUs | Sửa tên SBU |
| `permission_identity_sbu_status` | 启用或禁用SBU | Enable or disable SBUs | Kích hoạt hoặc vô hiệu hóa SBU |
| `permission_identity_sbu_delete` | 删除SBU | Delete SBUs | Xóa SBU |
| `permission_identity_sbu_restore` | 恢复SBU | Restore SBUs | Khôi phục SBU |
| `permission_identity_role_read` | 查看角色 | View roles | Xem vai trò |
| `permission_identity_role_create` | 新增角色 | Create roles | Thêm vai trò |
| `permission_identity_role_update` | 修改角色 | Edit roles | Sửa thông tin vai trò |
| `permission_identity_role_status` | 启用或禁用角色 | Enable or disable roles | Kích hoạt hoặc vô hiệu hóa vai trò |
| `permission_identity_role_delete` | 删除角色 | Delete roles | Xóa vai trò |
| `permission_identity_role_permissions` | 分配角色权限 | Assign role permissions | Gán quyền cho vai trò |
| `permission_identity_menu_read` | 查看菜单 | View menus | Xem menu |
| `permission_identity_menu_create` | 新增菜单 | Create menus | Thêm menu |
| `permission_identity_menu_update` | 修改菜单 | Edit menus | Sửa menu |
| `permission_identity_menu_status` | 启用或禁用菜单 | Enable or disable menus | Kích hoạt hoặc vô hiệu hóa menu |
| `permission_identity_menu_delete` | 删除菜单 | Delete menus | Xóa menu |

## 4. 虚拟节点

权限树中的两个虚拟节点由代码生成，不落库，翻译在 `MenuService.GetPermissionTreeAsync` 中维护：

| 节点 | 中文 | English | Tiếng Việt |
| --- | --- | --- | --- |
| `__view_*` | 查看页面 | View page | Xem trang |
| `__unassigned` | 未归类权限 | Unassigned permissions | Quyền chưa phân loại |

`inventory.stocktake.read`、`ai.chat.use`、`integration.goflow.open` 目前没有内置菜单行：若未挂到任何菜单，会出现在“未归类权限”下并直接显示权限码，不需要菜单翻译；一旦为它们建立自定义菜单，越南语标题在界面维护。

## 5. 维护规则

1. 新增或改名内置菜单时，同步修改 `DefaultMenus.cs` 与本文件，以及在 `maintain-vietnamese-menu-translations.sql` 增加或调整对应的 `UPDATE`；三处内容必须一致。
2. 权限类节点的越南语必须与 `AccessMessages.vi-VN.resx` 的 `Access.Permission.{code}` 相同；分组类目对应 `Access.Group.{segment}`。
3. 脚本只填空列、不覆盖已有翻译；需要改已有译文时，由负责人确认后单独安排，不写进常规维护脚本。
4. 展示回退顺序为“越南语 → 英文 → 中文”，回退只影响显示，不写回字段；清空越南语字段在界面上是显式操作。
