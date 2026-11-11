-- MOM Oracle 19c / ZX_IDENTITY: DATA MAINTENANCE (built-in Vietnamese menu titles).
-- Fills only the empty Vietnamese titles of built-in menus; existing translations are never overwritten,
-- so the script is safe to run more than once.
-- Oracle stores '' as NULL, so the IS NULL condition also covers empty strings.
-- It changes no hierarchy, path, component, permission, role or user grant, and it is not part of any
-- EF migration: run it per environment after the schema upgrade, and only for built-in menus.
-- Custom menus are maintained by administrators in the menu editor, not here.
-- Replace ZX_IDENTITY with the object owner of the target environment before running.
--
-- The same values seed new databases through Services/Identity/ZX.Platform.Identity.Infrastructure/
-- Persistence/DefaultMenus.cs. Update both places together when the built-in menu set changes.
BEGIN
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản trị hệ thống'
        WHERE "CODE" = N'system' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản lý quyền'
        WHERE "CODE" = N'access' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản lý người dùng'
        WHERE "CODE" = N'users' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản lý SBU'
        WHERE "CODE" = N'sbus' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản lý vai trò'
        WHERE "CODE" = N'roles' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Quản lý menu'
        WHERE "CODE" = N'menus' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;

    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xem người dùng'
        WHERE "CODE" = N'permission_identity_user_read' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Thêm người dùng'
        WHERE "CODE" = N'permission_identity_user_create' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Sửa tên người khác'
        WHERE "CODE" = N'permission_identity_user_rename' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Kích hoạt hoặc vô hiệu hóa người dùng'
        WHERE "CODE" = N'permission_identity_user_status' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xóa người dùng'
        WHERE "CODE" = N'permission_identity_user_delete' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Khôi phục người dùng'
        WHERE "CODE" = N'permission_identity_user_restore' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Gán SBU cho người dùng'
        WHERE "CODE" = N'permission_identity_user_sbus' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Gán vai trò cho người dùng'
        WHERE "CODE" = N'permission_identity_user_roles' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Buộc người dùng đăng xuất'
        WHERE "CODE" = N'permission_identity_user_forceLogout' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Phê duyệt kích hoạt tài khoản OA'
        WHERE "CODE" = N'permission_identity_user_approve' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;

    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xem SBU'
        WHERE "CODE" = N'permission_identity_sbu_read' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Thêm SBU'
        WHERE "CODE" = N'permission_identity_sbu_create' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Sửa tên SBU'
        WHERE "CODE" = N'permission_identity_sbu_rename' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Kích hoạt hoặc vô hiệu hóa SBU'
        WHERE "CODE" = N'permission_identity_sbu_status' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xóa SBU'
        WHERE "CODE" = N'permission_identity_sbu_delete' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Khôi phục SBU'
        WHERE "CODE" = N'permission_identity_sbu_restore' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;

    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xem vai trò'
        WHERE "CODE" = N'permission_identity_role_read' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Thêm vai trò'
        WHERE "CODE" = N'permission_identity_role_create' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Sửa thông tin vai trò'
        WHERE "CODE" = N'permission_identity_role_update' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Kích hoạt hoặc vô hiệu hóa vai trò'
        WHERE "CODE" = N'permission_identity_role_status' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xóa vai trò'
        WHERE "CODE" = N'permission_identity_role_delete' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Gán quyền cho vai trò'
        WHERE "CODE" = N'permission_identity_role_permissions' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;

    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xem menu'
        WHERE "CODE" = N'permission_identity_menu_read' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Thêm menu'
        WHERE "CODE" = N'permission_identity_menu_create' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Sửa menu'
        WHERE "CODE" = N'permission_identity_menu_update' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Kích hoạt hoặc vô hiệu hóa menu'
        WHERE "CODE" = N'permission_identity_menu_status' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;
    UPDATE "ZX_IDENTITY"."ZX_IDENTITY_MENU" SET "TITLE_VI" = N'Xóa menu'
        WHERE "CODE" = N'permission_identity_menu_delete' AND "IS_BUILT_IN" = 1 AND "TITLE_VI" IS NULL;

    COMMIT;
END;
/

-- Coverage check: built-in menus that still have no Vietnamese title (expect no rows).
SELECT "CODE", "TITLE", "TITLE_EN"
FROM "ZX_IDENTITY"."ZX_IDENTITY_MENU"
WHERE "IS_BUILT_IN" = 1 AND "IS_DELETED" = 0 AND "TITLE_VI" IS NULL
ORDER BY "CODE";
