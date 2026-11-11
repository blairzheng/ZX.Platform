-- MOM Oracle 19c / ZX_IDENTITY: UPGRADE (Vietnamese menu columns).
-- Adds the two optional Vietnamese columns used by the menu contract.
-- Run the whole block once after verifying the connected service and login, and after the schema
-- object owner below matches the target environment (the test database may use another owner).
-- Review this script before execution; the connected service and login must be checked by the DBA.
-- DDL commits implicitly; errors can leave partial changes.
-- On error stop and inspect; do not drop existing objects or rerun automatically.
-- This script changes table structure only. Built-in translation content is a separate delivery:
-- deploy/database/maintain-vietnamese-menu-translations.sql
DECLARE
    v_last VARCHAR2(150);
BEGIN
    SELECT MAX("MIGRATION_ID") INTO v_last FROM "ZX_IDENTITY"."ZX_IDENTITY_MIGRATIONS";
    IF v_last IS NULL OR v_last <> '20261109012525_AddDatabaseCreationTime' THEN
        RAISE_APPLICATION_ERROR(-20003, 'Unexpected current migration; stop this upgrade.');
    END IF;

    EXECUTE IMMEDIATE q'~ALTER TABLE "ZX_IDENTITY"."ZX_IDENTITY_MENU" ADD "TITLE_VI" NVARCHAR2(200)~';

    EXECUTE IMMEDIATE q'~ALTER TABLE "ZX_IDENTITY"."ZX_IDENTITY_MENU" ADD "LAUNCHER_DESCRIPTION_VI" NVARCHAR2(500)~';

    EXECUTE IMMEDIATE q'~INSERT INTO "ZX_IDENTITY"."ZX_IDENTITY_MIGRATIONS" ("MIGRATION_ID", "PRODUCT_VERSION")
VALUES (N'20261111000917_AddVietnameseMenuTranslations', N'10.0.12')~';
    COMMIT;
END;
/
