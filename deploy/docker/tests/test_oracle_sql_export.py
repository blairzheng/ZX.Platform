"""Check offline SQL guards without connecting to Oracle."""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
OLD = '20261026114558_InitialSajet19IdentitySchema'
BASE = '20261027000000_InitialIdentitySchema'
TARGET = '20261028084818_ReplaceFactoriesWithSbus'
OLD_TABLES = ','.join([
    'ZX_IDENTITY_FACTORY', 'ZX_IDENTITY_MENU', 'ZX_IDENTITY_MIGRATIONS',
    'ZX_IDENTITY_PERMISSION', 'ZX_IDENTITY_ROLE', 'ZX_IDENTITY_ROLE_PERMISSION',
    'ZX_IDENTITY_USER', 'ZX_IDENTITY_USER_FACTORY', 'ZX_IDENTITY_USER_ROLE',
])


class OracleSqlExportChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)

    def run_export(self, script, *args):
        return subprocess.run(
            [sys.executable, str(ROOT / script), *map(str, args)],
            capture_output=True, text=True, check=False,
        )

    def test_reset_requires_exact_state_without_session_values(self):
        target = self.directory / 'reset.sql'
        result = self.run_export(
            'export-identity-reset-sql.py', target, 'SAJET', '--session-agnostic',
            '--expected-migration', OLD, '--expected-tables', OLD_TABLES,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        sql = target.read_text()
        self.assertIn(OLD, sql)
        self.assertIn('v_count <> 9', sql)
        self.assertIn('Identity table names differ from the reviewed list.', sql)
        self.assertIn('DROP TABLE "SAJET"."ZX_IDENTITY_USER" PURGE', sql)
        self.assertNotIn('DBA_LOGIN', sql)
        self.assertNotIn('DB_SERVICE', sql)

    def test_reset_rejects_unchecked_session_agnostic_mode(self):
        result = self.run_export('export-identity-reset-sql.py', self.directory / 'reset.sql',
                                 'SAJET', '--session-agnostic')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.directory / 'reset.sql').exists())

    def test_default_reset_still_checks_login_and_service(self):
        target = self.directory / 'reset.sql'
        result = self.run_export('export-identity-reset-sql.py', target, 'SAJET', 'DBA_LOGIN', 'PROD_SERVICE')
        self.assertEqual(result.returncode, 0, result.stderr)
        sql = target.read_text()
        self.assertIn("SESSION_USER') <> 'DBA_LOGIN'", sql)
        self.assertIn("<> 'PROD_SERVICE'", sql)

    def test_upgrade_keeps_version_guard_in_session_agnostic_mode(self):
        source = self.directory / 'source.sql'
        source.write_text(
            f'INSERT INTO "ZX_IDENTITY"."ZX_IDENTITY_MIGRATIONS" '
            f'("MigrationId", "ProductVersion") VALUES (N\'{TARGET}\', N\'10.0.12\')\n/\n'
        )
        target = self.directory / 'upgrade.sql'
        result = self.run_export(
            'export-oracle19-sql.py', source, target, 'SAJET', '--session-agnostic',
            '--from-migration', BASE, '--to-migration', TARGET,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        sql = target.read_text()
        self.assertIn(f"v_last <> '{BASE}'", sql)
        self.assertIn(f"N'{TARGET}'", sql)
        self.assertNotIn('DBA_LOGIN', sql)
        self.assertNotIn('DB_SERVICE', sql)

    def test_default_upgrade_still_checks_login_and_service(self):
        source = self.directory / 'source.sql'
        source.write_text(f"INSERT INTO \"ZX_IDENTITY\".\"ZX_IDENTITY_MIGRATIONS\" VALUES (N'{TARGET}')\n/\n")
        target = self.directory / 'upgrade.sql'
        result = self.run_export(
            'export-oracle19-sql.py', source, target, 'SAJET', 'DBA_LOGIN', 'PROD_SERVICE',
            '--from-migration', BASE, '--to-migration', TARGET,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        sql = target.read_text()
        self.assertIn("SESSION_USER') <> 'DBA_LOGIN'", sql)
        self.assertIn("<> 'PROD_SERVICE'", sql)


if __name__ == '__main__':
    unittest.main()
