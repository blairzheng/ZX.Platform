-- MOM Oracle 19c / SAJET: UPGRADE.
-- Run the whole block once after verifying the connected service and login.
-- Review this generated script before execution; the connected service and login must be checked by the DBA.
-- DDL commits implicitly; errors can leave partial changes.
-- On error stop and inspect; do not drop existing objects or rerun automatically.
DECLARE
    v_last VARCHAR2(150);
BEGIN
    SELECT MAX("MigrationId") INTO v_last FROM "SAJET"."ZX_IDENTITY_MIGRATIONS";
    IF v_last IS NULL OR v_last <> '20261102144318_AddUserApprovalAudit' THEN
        RAISE_APPLICATION_ERROR(-20003, 'Unexpected current migration; stop this upgrade.');
    END IF;

    -- 审计快照存逗号分隔的 32 位 GUID：61 个 GUID 拼接后为 2012 字符，
    -- VARCHAR2(2000) 会让一次合法的多角色/多 SBU 审批因为写审计而整笔失败。
    -- Oracle 不允许把字符列直接 MODIFY 成 CLOB（ORA-22858），这里改为
    -- “新增 CLOB 列 -> 复制原值 -> 删除旧列 -> 改名”，复制而不是截断，既有审计证据保持原样。
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" ADD ("ROLE_IDS_CLOB" CLOB)~';
    EXECUTE IMMEDIATE q'~UPDATE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" SET "ROLE_IDS_CLOB" = TO_CLOB("ROLE_IDS")~';
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" DROP COLUMN "ROLE_IDS"~';
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" RENAME COLUMN "ROLE_IDS_CLOB" TO "ROLE_IDS"~';

    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" ADD ("SBU_IDS_CLOB" CLOB)~';
    EXECUTE IMMEDIATE q'~UPDATE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" SET "SBU_IDS_CLOB" = TO_CLOB("SBU_IDS")~';
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" DROP COLUMN "SBU_IDS"~';
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER_APPROVAL_AUDIT" RENAME COLUMN "SBU_IDS_CLOB" TO "SBU_IDS"~';

    INSERT INTO "SAJET"."ZX_IDENTITY_MIGRATIONS" ("MigrationId", "ProductVersion")
    VALUES (N'20261102150741_WidenApprovalAuditSnapshot', N'10.0.12');
    COMMIT;
END;
/
