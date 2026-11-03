"""Check release baselines and the DBA handoff without Oracle or Docker."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile


ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location('release_package', ROOT / 'scripts/package-release.py')
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)
BASE = '20261102150741_WidenApprovalAuditSnapshot'
TARGET = '20261103090000_AllowPendingLdapLogin'


class ReleasePackageChecks(unittest.TestCase):
    def test_rejects_unknown_or_newer_production_version(self):
        for migration in ('20261102111111_Unknown', '20990101000000_Future'):
            with self.subTest(migration=migration), self.assertRaises(ValueError):
                PACKAGE.select_pending([BASE, TARGET], migration)

    def test_rejects_sql_and_path_input(self):
        for release, migration, schema in (
            ('../release', BASE, 'SAJET'),
            ('latest', BASE, 'SAJET'),
            ('release', BASE + "'; DROP TABLE USERS;--", 'SAJET'),
            ('release', BASE, 'SAJET;DROP'),
        ):
            with self.subTest(release=release, schema=schema), self.assertRaises(ValueError):
                PACKAGE.validate_inputs(release, migration, schema)

    def test_same_version_produces_no_upgrade_script(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(PACKAGE, 'ef') as ef:
            directory = Path(temporary) / 'database'
            metadata = PACKAGE.package_database(
                directory, release='test-release', from_migration=TARGET,
                schema='SAJET', migrations=[BASE, TARGET], commit='abc123')
            self.assertFalse(metadata['requiresDatabaseUpgrade'])
            self.assertFalse((directory / 'upgrade.sql').exists())
            self.assertTrue((directory / 'NO_DATABASE_CHANGE.txt').exists())
            ef.assert_not_called()

    def test_upgrade_delivers_only_target_schema_with_version_guard(self):
        def generate_sql(*args, **kwargs):
            path = Path(args[args.index('--output') + 1])
            path.write_text(
                'ALTER TABLE "ZX_IDENTITY"."ZX_IDENTITY_USER" ADD "EXAMPLE" NUMBER\n/\n'
                'INSERT INTO "ZX_IDENTITY"."ZX_IDENTITY_MIGRATIONS" '
                f'("MigrationId", "ProductVersion") VALUES (N\'{TARGET}\', N\'10.0.12\')\n/\n',
                encoding='utf-8')

        with tempfile.TemporaryDirectory() as temporary, patch.object(PACKAGE, 'ef', generate_sql):
            directory = Path(temporary) / 'database'
            metadata = PACKAGE.package_database(
                directory, release='test-release', from_migration=BASE,
                schema='SAJET', migrations=[BASE, TARGET], commit='abc123')
            sql = (directory / 'upgrade.sql').read_text()
            self.assertIn('WHENEVER SQLERROR EXIT SQL.SQLCODE', sql)
            self.assertIn('SET DEFINE OFF', sql)
            self.assertIn('"SAJET"."ZX_IDENTITY_USER"', sql)
            self.assertNotIn('"ZX_IDENTITY".', sql)
            self.assertIn(f"v_last <> '{BASE}'", sql)
            self.assertIn(f"N'{TARGET}'", sql)
            self.assertFalse((directory / 'upgrade.generated.sql').exists())
            self.assertEqual(metadata['pendingMigrations'], [TARGET])
            self.assertEqual(metadata['upgradeSqlSha256'], PACKAGE.sha256(directory / 'upgrade.sql'))
            self.assertEqual(json.loads((directory / 'database.json').read_text()), metadata)

    def test_release_handoff_uses_delivered_image_archive_digest(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            release = 'test-images'
            image_name = f'mom-images-{release}.tar.gz'

            def offline_ef(*args, **kwargs):
                if args[:2] == ('migrations', 'list'):
                    return json.dumps([{'id': BASE}, {'id': TARGET}])
                return None

            def offline_run(command, **kwargs):
                if command == ['git', 'rev-parse', 'HEAD']:
                    return 'abc123\n'
                self.assertEqual(command, ['bash', 'scripts/publish-images.sh', '--package-only', release])
                archive = root / 'output' / image_name
                archive.write_bytes(b'test archive bytes')
                PACKAGE.write_checksum(archive)
                return None

            with patch.object(PACKAGE, 'ROOT', root), \
                    patch.object(PACKAGE, 'ef', offline_ef), \
                    patch.object(PACKAGE, 'run', offline_run), \
                    patch.dict(PACKAGE.os.environ, {'GITHUB_STEP_SUMMARY': ''}), \
                    patch.object(PACKAGE.sys, 'argv', ['package-release.py', '--release', release,
                                                      '--from-migration', TARGET]):
                PACKAGE.main()

            directory = root / 'output/mom-release' / release
            metadata = json.loads((directory / 'release.json').read_text())
            self.assertEqual(metadata['images']['sha256'], PACKAGE.sha256(directory / image_name))
            self.assertIn(f'MOM_PACKAGE_SHA256={metadata["images"]["sha256"]}',
                          (directory / 'README.md').read_text())
            self.assertFalse((root / 'output' / image_name).exists())
            with zipfile.ZipFile(directory / f'mom-database-{release}.zip') as archive:
                self.assertIn('NO_DATABASE_CHANGE.txt', archive.namelist())
                self.assertNotIn('upgrade.sql', archive.namelist())


if __name__ == '__main__':
    unittest.main()
