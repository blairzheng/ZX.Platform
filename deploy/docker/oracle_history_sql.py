"""Render the one-time Identity history-table compatibility DDL; never execute SQL."""

import re


def history_compatibility_sql(schema, expected_migration=None, *, login_user=None, service_name=None):
    """Accept either history naming convention, then converge to the current one.

    All shape and optional version checks run before the first DDL statement.
    Each rename is independent so a DBA can resume a partially completed conversion.
    """
    if not re.fullmatch(r'[A-Z][A-Z0-9_]{0,127}', schema) or schema in {'SYS', 'SYSTEM'}:
        raise ValueError('Invalid application schema.')
    if expected_migration is not None and not re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', expected_migration):
        raise ValueError('Invalid expected migration ID.')
    if (login_user is None) != (service_name is None):
        raise ValueError('Both login user and service name are required for a session check.')
    if login_user and not re.fullmatch(r'[A-Z][A-Z0-9_$#]{0,127}', login_user):
        raise ValueError('Invalid Oracle login identifier.')
    if service_name and not re.fullmatch(r'[A-Z][A-Z0-9_$#.-]{0,127}', service_name):
        raise ValueError('Invalid Oracle service identifier.')

    sql = f'''-- Identity history compatibility for {schema}; review and execute explicitly.
-- Existing migration IDs and product versions are preserved.
-- CREATE_TIME on existing history records denotes conversion time, not their original execution time.
-- Oracle DDL commits implicitly. Stop Identity instances before executing.
DECLARE
    v_count INTEGER;
    v_old_id INTEGER;
    v_new_id INTEGER;
    v_old_version INTEGER;
    v_new_version INTEGER;
    v_create_time INTEGER;
    v_data_type VARCHAR2(128);
    v_nullable VARCHAR2(1);
    v_default VARCHAR2(32767);
    v_last VARCHAR2(150);
BEGIN
'''
    if login_user:
        sql += f'''    IF UPPER(REGEXP_SUBSTR(SYS_CONTEXT('USERENV', 'SERVICE_NAME'), '^[^.]+')) <> '{service_name}'
       OR SYS_CONTEXT('USERENV', 'SESSION_USER') <> '{login_user}' THEN
        RAISE_APPLICATION_ERROR(-20011, 'Wrong service or login for history conversion.');
    END IF;
'''
    sql += f'''    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
    WHERE OWNER = '{schema}' AND TABLE_NAME = 'ZX_IDENTITY_MIGRATIONS';
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20012, 'History table is absent or inaccessible; stop.');
    END IF;
    SELECT COUNT(CASE WHEN COLUMN_NAME = 'MigrationId' THEN 1 END),
           COUNT(CASE WHEN COLUMN_NAME = 'MIGRATION_ID' THEN 1 END),
           COUNT(CASE WHEN COLUMN_NAME = 'ProductVersion' THEN 1 END),
           COUNT(CASE WHEN COLUMN_NAME = 'PRODUCT_VERSION' THEN 1 END),
           COUNT(CASE WHEN COLUMN_NAME = 'CREATE_TIME' THEN 1 END),
           COUNT(*)
    INTO v_old_id, v_new_id, v_old_version, v_new_version, v_create_time, v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = '{schema}' AND TABLE_NAME = 'ZX_IDENTITY_MIGRATIONS';
    IF v_old_id + v_new_id <> 1 OR v_old_version + v_new_version <> 1
       OR v_count <> 2 + v_create_time THEN
        RAISE_APPLICATION_ERROR(-20013, 'Unexpected or ambiguous history columns; stop.');
    END IF;
    IF v_create_time = 1 THEN
        SELECT DATA_TYPE, NULLABLE, DATA_DEFAULT INTO v_data_type, v_nullable, v_default
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = '{schema}' AND TABLE_NAME = 'ZX_IDENTITY_MIGRATIONS'
          AND COLUMN_NAME = 'CREATE_TIME';
        v_default := REPLACE(REPLACE(REPLACE(UPPER(TRIM(v_default)), CHR(10), ''), CHR(13), ''), ' ', '');
        IF v_data_type <> 'DATE' OR v_nullable <> 'N' OR v_default IS NULL
           OR v_default NOT IN ('SYSDATE', '(SYSDATE)') THEN
            RAISE_APPLICATION_ERROR(-20014, 'Unexpected history CREATE_TIME definition; stop.');
        END IF;
    END IF;
'''
    if expected_migration:
        sql += f'''    IF v_new_id = 1 THEN
        EXECUTE IMMEDIATE 'SELECT MAX("MIGRATION_ID") FROM "{schema}"."ZX_IDENTITY_MIGRATIONS"' INTO v_last;
    ELSE
        EXECUTE IMMEDIATE 'SELECT MAX("MigrationId") FROM "{schema}"."ZX_IDENTITY_MIGRATIONS"' INTO v_last;
    END IF;
    IF v_last IS NULL OR v_last <> '{expected_migration}' THEN
        RAISE_APPLICATION_ERROR(-20015, 'Unexpected current migration; stop history conversion.');
    END IF;
'''
    sql += f'''    -- Every preflight above must pass before the first implicit DDL commit.
    IF v_old_id = 1 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE "{schema}"."ZX_IDENTITY_MIGRATIONS" RENAME COLUMN "MigrationId" TO "MIGRATION_ID"';
    END IF;
    IF v_old_version = 1 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE "{schema}"."ZX_IDENTITY_MIGRATIONS" RENAME COLUMN "ProductVersion" TO "PRODUCT_VERSION"';
    END IF;
    IF v_create_time = 0 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE "{schema}"."ZX_IDENTITY_MIGRATIONS" ADD ("CREATE_TIME" DATE DEFAULT SYSDATE NOT NULL)';
    END IF;
END;
/
'''
    return sql
