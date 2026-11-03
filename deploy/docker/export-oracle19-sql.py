#!/usr/bin/env python3
"""Wrap EF-generated forward SQL in one guarded Oracle PL/SQL statement."""
import argparse
import re
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('target', type=Path)
parser.add_argument('schema')
parser.add_argument('login_user', nargs='?')
parser.add_argument('service_name', nargs='?')
parser.add_argument('--session-agnostic', action='store_true')
parser.add_argument('--from-migration')
parser.add_argument('--to-migration')
args = parser.parse_args()
source, target = args.source, args.target
schema = args.schema.strip().upper()
login_user = args.login_user.strip().upper() if args.login_user else None
service_name = args.service_name.strip().upper() if args.service_name else None
upgrade = args.from_migration is not None or args.to_migration is not None
if upgrade:
    if not all(value and re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', value) for value in (args.from_migration, args.to_migration)):
        raise SystemExit('An upgrade requires both --from-migration and --to-migration with full migration IDs.')
    if args.from_migration >= args.to_migration:
        raise SystemExit('Upgrade target must be later than the source migration.')
if not re.fullmatch(r"[A-Z][A-Z0-9_]{0,127}", schema):
    raise SystemExit("Invalid Oracle schema identifier.")
if not args.session_agnostic and (not login_user or not service_name):
    raise SystemExit("LOGIN_USER and SERVICE_NAME are required unless --session-agnostic is set.")
if login_user and not re.fullmatch(r"[A-Z][A-Z0-9_$#]{0,127}", login_user):
    raise SystemExit("Invalid Oracle login identifier.")
if service_name and not re.fullmatch(r"[A-Z][A-Z0-9_$#.-]{0,127}", service_name):
    raise SystemExit("Invalid Oracle service identifier.")
sql = source.read_text(encoding="utf-8-sig")
# EF's Oracle script uses both quoted identifiers and PL/SQL owner literals.
sql = sql.replace('"ZX_IDENTITY"', f'"{schema}"').replace("'ZX_IDENTITY'", f"'{schema}'")
blocks = [block.strip() for block in re.split(r"(?m)^/\s*$", sql) if block.strip()]
objects = re.findall(rf'CREATE\s+(?:UNIQUE\s+)?(?:TABLE|INDEX)\s+"{re.escape(schema)}"\."([^"]+)"', sql)
if "~" in sql or (not upgrade and (len(set(objects)) != 19 or re.search(r"\b(DROP|TRUNCATE|GRANT|ALTER)\b", sql, re.I))):
    raise SystemExit("Unexpected initialization SQL: review before wrapping.")
if re.search(r"\bBOOLEAN\b|\bIS (?:FALSE|TRUE)\b", sql, re.I):
    raise SystemExit("Native Boolean SQL is not valid for Oracle 19c.")
names = ", ".join("'" + name + "'" for name in sorted(set(objects)))
mode = "UPGRADE" if upgrade else "FIRST INITIALIZATION ONLY"
header = f"""-- MOM Oracle 19c / {schema}: {mode}.
-- Run the whole block once after verifying the connected service and login.
-- Review this generated script before execution; schema, login and service are checked.
-- DDL commits implicitly; errors can leave partial changes.
-- On error stop and inspect; do not drop existing objects or rerun automatically.
DECLARE
    v_count INTEGER;
    v_last VARCHAR2(150);
BEGIN
"""
if args.session_agnostic:
    header = header.replace('schema, login and service are checked.',
                            'the connected service and login must be checked by the DBA.')
else:
    header += f"""    IF UPPER(REGEXP_SUBSTR(SYS_CONTEXT('USERENV', 'SERVICE_NAME'), '^[^.]+')) <> '{service_name}' THEN
        RAISE_APPLICATION_ERROR(-20001, 'Wrong service: expected {service_name}.');
    END IF;
    IF SYS_CONTEXT('USERENV', 'SESSION_USER') <> '{login_user}' THEN
        RAISE_APPLICATION_ERROR(-20002, 'Wrong login: expected {login_user}.');
    END IF;
"""
if upgrade:
    if args.to_migration not in sql:
        raise SystemExit('Target migration ID is absent from the generated SQL.')
    header += f"""    SELECT MAX("MigrationId") INTO v_last FROM "{schema}"."ZX_IDENTITY_MIGRATIONS";
    IF v_last IS NULL OR v_last <> '{args.from_migration}' THEN
        RAISE_APPLICATION_ERROR(-20003, 'Unexpected current migration; stop this upgrade.');
    END IF;
"""
else:
    header += f"""    SELECT COUNT(*) INTO v_count FROM ALL_OBJECTS
    WHERE OWNER = '{schema}' AND OBJECT_NAME IN ({names});
    IF v_count <> 0 THEN
        RAISE_APPLICATION_ERROR(-20003, 'MOM objects already exist; inspect before initializing.');
    END IF;
"""
statements = ["    EXECUTE IMMEDIATE q'~" + block + "~';" for block in blocks]
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(header + "\n\n".join(statements) + "\n    COMMIT;\nEND;\n/\n", encoding="utf-8")
print(f"Created {target}: {len(blocks)} statements, {len(objects)} objects; no database connection.")
