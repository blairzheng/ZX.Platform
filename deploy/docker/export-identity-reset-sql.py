#!/usr/bin/env python3
"""Generate a guarded reset of MOM Identity tables; never connect to a database."""
import argparse
import re
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('target', type=Path)
parser.add_argument('schema')
parser.add_argument('login_user', nargs='?')
parser.add_argument('service_name', nargs='?')
parser.add_argument('--session-agnostic', action='store_true')
parser.add_argument('--expected-migration')
parser.add_argument('--expected-table-count', type=int)
parser.add_argument('--expected-tables')
args = parser.parse_args()
target = args.target
schema = args.schema.strip().upper()
login = args.login_user.strip().upper() if args.login_user else None
service = args.service_name.strip().upper() if args.service_name else None
if not re.fullmatch(r'[A-Z][A-Z0-9_]{0,127}', schema) or schema in {'SYS', 'SYSTEM'}:
    raise SystemExit('Invalid application schema.')
if not args.session_agnostic and (not login or not service):
    raise SystemExit('LOGIN_USER and SERVICE_NAME are required unless --session-agnostic is set.')
if login and not re.fullmatch(r'[A-Z][A-Z0-9_$#]{0,127}', login):
    raise SystemExit('Invalid login.')
if service and not re.fullmatch(r'[A-Z][A-Z0-9_$#.-]{0,127}', service):
    raise SystemExit('Invalid service.')
if args.expected_migration and not re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', args.expected_migration):
    raise SystemExit('Invalid expected migration ID.')
if args.expected_table_count is not None and args.expected_table_count < 1:
    raise SystemExit('Expected table count must be positive.')
expected_tables = [name.strip().upper() for name in args.expected_tables.split(',')] if args.expected_tables else []
if expected_tables and (len(expected_tables) != len(set(expected_tables)) or
                        any(not re.fullmatch(r'ZX_IDENTITY_[A-Z0-9_]+', name) for name in expected_tables)):
    raise SystemExit('Invalid expected table names.')
if args.session_agnostic and (not args.expected_migration or not expected_tables):
    raise SystemExit('Session-agnostic reset requires expected migration and exact table names.')
tables = [
    'ZX_IDENTITY_USER_ROLE', 'ZX_IDENTITY_USER_SBU', 'ZX_IDENTITY_USER_FACTORY',
    'ZX_IDENTITY_ROLE_PERMISSION',
    'ZX_IDENTITY_MENU', 'ZX_IDENTITY_PERMISSION', 'ZX_IDENTITY_ROLE',
    'ZX_IDENTITY_USER', 'ZX_IDENTITY_SBU', 'ZX_IDENTITY_FACTORY',
    'ZX_IDENTITY_MIGRATIONS',
]
if expected_tables and not set(expected_tables).issubset(tables):
    raise SystemExit('Expected tables must be in the Identity reset allowlist.')
if expected_tables and args.expected_table_count is not None and args.expected_table_count != len(expected_tables):
    raise SystemExit('Expected table count does not match expected table names.')
sql = f'''-- DESTRUCTIVE: reset only the 11 listed MOM Identity tables in {schema}.
-- Requires explicit execution after stopping every Identity instance using this schema.
-- DDL commits implicitly. No database users or unrelated tables are deleted.
DECLARE
    v_count INTEGER;
BEGIN
'''
if args.session_agnostic:
    sql = sql.replace('-- DDL commits implicitly.',
                      '-- DBA must verify the connected service and login before execution.\n-- DDL commits implicitly.')
else:
    sql += f'''    IF UPPER(REGEXP_SUBSTR(SYS_CONTEXT('USERENV', 'SERVICE_NAME'), '^[^.]+')) <> '{service}'
       OR SYS_CONTEXT('USERENV', 'SESSION_USER') <> '{login}' THEN
        RAISE_APPLICATION_ERROR(-20001, 'Wrong service or login for Identity reset.');
    END IF;
'''
if args.expected_migration:
    sql += f'''    SELECT COUNT(*) INTO v_count FROM "{schema}"."ZX_IDENTITY_MIGRATIONS"
    WHERE "MigrationId" = '{args.expected_migration}';
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20002, 'Expected Identity migration was not found.');
    END IF;
    SELECT COUNT(*) INTO v_count FROM "{schema}"."ZX_IDENTITY_MIGRATIONS";
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20003, 'Unexpected Identity migration history.');
    END IF;
'''
if args.expected_table_count is not None:
    sql += f'''    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
    WHERE OWNER = '{schema}' AND TABLE_NAME LIKE 'ZX_IDENTITY%';
    IF v_count <> {args.expected_table_count} THEN
        RAISE_APPLICATION_ERROR(-20004, 'Unexpected number of Identity tables.');
    END IF;
'''
if expected_tables:
    names = ', '.join(f"'{name}'" for name in sorted(expected_tables))
    sql += f'''    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
    WHERE OWNER = '{schema}' AND TABLE_NAME IN ({names});
    IF v_count <> {len(expected_tables)} THEN
        RAISE_APPLICATION_ERROR(-20005, 'Identity table names differ from the reviewed list.');
    END IF;
    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
    WHERE OWNER = '{schema}' AND TABLE_NAME LIKE 'ZX_IDENTITY%';
    IF v_count <> {len(expected_tables)} THEN
        RAISE_APPLICATION_ERROR(-20006, 'Additional Identity tables were found.');
    END IF;
'''
for table in tables:
    sql += f'''    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
    WHERE OWNER = '{schema}' AND TABLE_NAME = '{table}';
    IF v_count > 0 THEN
        EXECUTE IMMEDIATE 'DROP TABLE "{schema}"."{table}" PURGE';
    END IF;
'''
sql += 'END;\n/\n'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(sql, encoding='utf-8')
print(f'Generated {target}; not executed. Drops only {len(tables)} named MOM tables.')
