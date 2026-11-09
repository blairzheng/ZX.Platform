#!/usr/bin/env python3
"""Generate history-table compatibility SQL for DBA review; never connect to Oracle."""

import argparse
from pathlib import Path

from oracle_history_sql import history_compatibility_sql


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', type=Path)
    parser.add_argument('schema')
    parser.add_argument('--expected-migration')
    args = parser.parse_args()
    try:
        sql = history_compatibility_sql(args.schema.strip().upper(), args.expected_migration)
    except ValueError as error:
        parser.error(str(error))
    args.target.parent.mkdir(parents=True, exist_ok=True)
    args.target.write_text('WHENEVER SQLERROR EXIT SQL.SQLCODE\nSET DEFINE OFF\n\n' + sql,
                           encoding='utf-8')
    print(f'Generated {args.target}; no database connection or execution.')


if __name__ == '__main__':
    main()
