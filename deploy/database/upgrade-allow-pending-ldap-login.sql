-- MOM Oracle 19c / SAJET. Stop all Identity writers and drain requests before running.
-- Upgrade from WidenApprovalAuditSnapshot; does not enable users or change any grants.
-- Oracle DDL commits implicitly. On error stop and inspect; do not rerun automatically.
WHENEVER SQLERROR EXIT SQL.SQLCODE
DECLARE
    v_last VARCHAR2(150);
BEGIN
    SELECT MAX("MigrationId") INTO v_last FROM "SAJET"."ZX_IDENTITY_MIGRATIONS";
    IF v_last IS NULL OR v_last <> '20261102150741_WidenApprovalAuditSnapshot' THEN
        RAISE_APPLICATION_ERROR(-20003, 'Unexpected current migration; stop this upgrade.');
    END IF;

    EXECUTE IMMEDIATE 'ALTER TABLE "SAJET"."ZX_IDENTITY_USER" DROP CONSTRAINT "CK_ZID_USER_LDAP_ENABLED"';
    EXECUTE IMMEDIATE q'~ALTER TABLE "SAJET"."ZX_IDENTITY_USER" ADD CONSTRAINT "CK_ZID_USER_LDAP_ENABLED" CHECK (AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS <> 'Rejected' OR IS_ENABLED = 0)~';

    INSERT INTO "SAJET"."ZX_IDENTITY_MIGRATIONS" ("MigrationId", "ProductVersion")
    VALUES (N'20261103090000_AllowPendingLdapLogin', N'10.0.12');
    COMMIT;
END;
/
