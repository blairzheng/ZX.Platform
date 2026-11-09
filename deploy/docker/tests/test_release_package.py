"""Check release baselines and the DBA handoff without Oracle or Docker."""

import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
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
MODEL_SQL = 'CREATE TABLE "ZX_IDENTITY"."ZX_IDENTITY_USER" ("ID" RAW(16) NOT NULL);\n'


class ReleasePackageChecks(unittest.TestCase):
    def test_model_table_scope_is_exact_and_includes_future_business_tables(self):
        tables = PACKAGE.parse_model_tables(
            MODEL_SQL + MODEL_SQL +
            'CREATE TABLE "ZX_IDENTITY"."EQUIPMENT_INSPECTION" ("ID" RAW(16) NOT NULL);\n')
        self.assertEqual(tables, ['EQUIPMENT_INSPECTION', 'ZX_IDENTITY_MIGRATIONS', 'ZX_IDENTITY_USER'])

    def test_model_table_scope_rejects_missing_or_unexpected_identifiers(self):
        for sql in (None, '', 'SELECT 1 FROM DUAL;',
                    'CREATE TABLE "SAJET"."ZX_IDENTITY_USER" ("ID" RAW(16));',
                    'CREATE TABLE "ZX_IDENTITY"."MixedCase" ("ID" RAW(16));',
                    MODEL_SQL + 'CREATE TABLE "ZX_IDENTITY"."BAD;NAME" ("ID" RAW(16));'):
            with self.subTest(sql=sql), self.assertRaises(ValueError):
                PACKAGE.parse_model_tables(sql)

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
        model = MODEL_SQL + 'CREATE TABLE "ZX_IDENTITY"."EQUIPMENT_INSPECTION" ("ID" RAW(16));\n'
        with tempfile.TemporaryDirectory() as temporary, patch.object(PACKAGE, 'ef', return_value=model) as ef:
            directory = Path(temporary) / 'database'
            metadata = PACKAGE.package_database(
                directory, release='test-release', from_migration=TARGET,
                schema='SAJET', migrations=[BASE, TARGET], commit='abc123')
            self.assertFalse(metadata['requiresDatabaseUpgrade'])
            self.assertFalse((directory / 'upgrade.sql').exists())
            self.assertTrue((directory / 'NO_DATABASE_CHANGE.txt').exists())
            self.assertIn('"MIGRATION_ID", "PRODUCT_VERSION", "CREATE_TIME"',
                          (directory / 'verify.sql').read_text())
            self.assertIsNone(metadata['historyCompatibilitySqlSha256'])
            verify = (directory / 'verify.sql').read_text()
            self.assertIn("c.TABLE_NAME IN ('EQUIPMENT_INSPECTION', 'ZX_IDENTITY_MIGRATIONS', 'ZX_IDENTITY_USER')", verify)
            self.assertIn("t.TABLE_NAME IN ('EQUIPMENT_INSPECTION', 'ZX_IDENTITY_MIGRATIONS', 'ZX_IDENTITY_USER')", verify)
            self.assertNotIn('SUBSTR(', verify)
            self.assertNotIn('ZX_IDENTITY_USER_BAK', verify)
            ef.assert_called_once_with('dbcontext', 'script', capture=True)

    def test_upgrade_delivers_only_target_schema_with_version_guard(self):
        def generate_sql(*args, **kwargs):
            if args[:2] == ('dbcontext', 'script'):
                return MODEL_SQL
            path = Path(args[args.index('--output') + 1])
            path.write_text(
                'ALTER TABLE "ZX_IDENTITY"."ZX_IDENTITY_USER" ADD "EXAMPLE" NUMBER\n/\n'
                'CREATE INDEX "ZX_IDENTITY"."IX_EXAMPLE" ON "ZX_IDENTITY"."ZX_IDENTITY_USER" ("EXAMPLE")\n/\n'
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
            self.assertIn('CREATE INDEX "SAJET"."IX_EXAMPLE" ON "SAJET"."ZX_IDENTITY_USER"', sql)
            self.assertNotIn('"ZX_IDENTITY".', sql)
            self.assertIn('("MIGRATION_ID", "PRODUCT_VERSION") VALUES', sql)
            self.assertLess(sql.index('-- Identity history compatibility'), sql.index('-- MOM Oracle'))
            compatibility = (directory / 'history-compatibility.sql').read_text()
            self.assertIn('RENAME COLUMN "MigrationId" TO "MIGRATION_ID"', compatibility)
            self.assertIn('"CREATE_TIME" DATE DEFAULT SYSDATE NOT NULL', compatibility)
            self.assertEqual(metadata['historyCompatibilitySqlSha256'],
                             PACKAGE.sha256(directory / 'history-compatibility.sql'))
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
            template = root / 'scripts/templates/upload-images.sh'
            template.parent.mkdir(parents=True)
            shutil.copyfile(ROOT / 'scripts/templates/upload-images.sh', template)

            def offline_ef(*args, **kwargs):
                if args[:2] == ('migrations', 'list'):
                    return json.dumps([{'id': BASE}, {'id': TARGET}])
                if args[:2] == ('dbcontext', 'script'):
                    return MODEL_SQL
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
                    patch.object(PACKAGE, 'assistant_sources_metadata', return_value={
                        'documentCount': 2, 'frontendCommit': 'a' * 40}) as assistant_sources, \
                    patch.dict(PACKAGE.os.environ, {'GITHUB_STEP_SUMMARY': ''}), \
                    patch.object(PACKAGE.sys, 'argv', ['package-release.py', '--release', release,
                                                      '--from-migration', TARGET]):
                PACKAGE.main()

            directory = root / 'output/mom-release' / release
            metadata = json.loads((directory / 'release.json').read_text())
            assistant_sources.assert_called_once_with()
            self.assertEqual(metadata['assistantSources']['frontendCommit'], 'a' * 40)
            self.assertEqual(metadata['images']['sha256'], PACKAGE.sha256(directory / image_name))
            self.assertTrue((directory / 'upload.sh').is_file())
            self.assertIn(f'MOM_PACKAGE_SHA256={metadata["images"]["sha256"]}',
                          (directory / 'README.md').read_text())
            self.assertFalse((root / 'output' / image_name).exists())
            with zipfile.ZipFile(directory / f'mom-database-{release}.zip') as archive:
                self.assertIn('NO_DATABASE_CHANGE.txt', archive.namelist())
                self.assertNotIn('upgrade.sql', archive.namelist())

    def test_database_only_release_has_no_image_upload_entry(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            def offline_ef(*args, **kwargs):
                if args[:2] == ('migrations', 'list'):
                    return json.dumps([{'id': BASE}, {'id': TARGET}])
                if args[:2] == ('dbcontext', 'script'):
                    return MODEL_SQL
                return None

            with patch.object(PACKAGE, 'ROOT', root), \
                    patch.object(PACKAGE, 'ef', offline_ef), \
                    patch.object(PACKAGE, 'run', return_value='abc123\n') as run, \
                    patch.dict(PACKAGE.os.environ, {'GITHUB_STEP_SUMMARY': ''}), \
                    patch.object(PACKAGE.sys, 'argv', ['package-release.py', '--release', 'database-only',
                                                      '--from-migration', TARGET, '--database-only']):
                PACKAGE.main()

            directory = root / 'output/mom-release/database-only'
            metadata = json.loads((directory / 'release.json').read_text())
            self.assertIsNone(metadata['images'])
            self.assertIsNone(metadata['assistantSources'])
            self.assertEqual(metadata['database']['schema'], 'ZX_IDENTITY')
            self.assertFalse((directory / 'upload.sh').exists())
            run.assert_called_once_with(['git', 'rev-parse', 'HEAD'], capture=True)


class StandaloneUploadChecks(unittest.TestCase):
    release = 'offline-upload-check'
    token = 'offline-test-token'

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / 'extracted release with spaces'
        self.directory.mkdir()
        self.archive = self.directory / f'mom-images-{self.release}.tar.gz'
        with tarfile.open(self.archive, 'w:gz') as archive:
            content = b'offline image fixture'
            member = tarfile.TarInfo(f'mom-images-{self.release}/images.tar.gz')
            member.size = len(content)
            archive.addfile(member, io.BytesIO(content))
        self.digest = PACKAGE.sha256(self.archive)
        PACKAGE.write_upload_script(self.directory, release=self.release, digest=self.digest)

        self.capture = self.root / 'curl-call.json'
        fake_bin = self.root / 'bin'
        fake_bin.mkdir()
        curl = fake_bin / 'curl'
        curl.write_text(
            f'#!{sys.executable}\n'
            'import hashlib, json, os, sys\n'
            'from pathlib import Path\n'
            'args = sys.argv[1:]\n'
            "archive = Path(args[args.index('--upload-file') + 1])\n"
            "Path(os.environ['FAKE_CURL_CAPTURE']).write_text(json.dumps({\n"
            "    'args': args, 'headers': sys.stdin.read(),\n"
            "    'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),\n"
            '}))\n'
            "print(os.environ.get('FAKE_CURL_HTTP_STATUS', '201'), end='')\n"
            "sys.exit(int(os.environ.get('FAKE_CURL_EXIT', '0')))\n",
            encoding='utf-8')
        curl.chmod(0o700)
        self.environment = dict(os.environ, PATH=str(fake_bin) + os.pathsep + os.environ['PATH'],
                                MOM_UPLOAD_TOKEN=self.token, FAKE_CURL_CAPTURE=str(self.capture),
                                FAKE_CURL_HTTP_STATUS='201', FAKE_CURL_EXIT='0')
        self.environment.pop('BASH_ENV', None)

    def run_upload(self, **environment):
        return subprocess.run(
            ['bash', str(self.directory / 'upload.sh')], cwd=self.root,
            env=dict(self.environment, **environment), text=True, capture_output=True, timeout=15)

    def test_extracted_package_uploads_without_a_repository(self):
        result = self.run_upload()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        request = json.loads(self.capture.read_text())
        self.assertEqual(request['args'][0], '-q')
        self.assertEqual(request['headers'], f'DEPLOY-TOKEN: {self.token}\n')
        self.assertEqual(request['sha256'], self.digest)
        self.assertEqual(request['args'][request['args'].index('--upload-file') + 1],
                         str(self.archive))
        self.assertEqual(request['args'][-1],
                         'https://bu17gitlab.luxshare.com.cn/api/v4/projects/80/packages/generic/'
                         f'mom-images/{self.release}/{self.archive.name}')
        self.assertIn(f'MOM_RELEASE={self.release}', result.stdout)
        self.assertIn(f'MOM_PACKAGE_SHA256={self.digest}', result.stdout)
        self.assertIn('MOM_COMPONENT=backend', result.stdout)
        self.assertIn('MOM_ACTION=deploy', result.stdout)
        self.assertNotIn(self.token, result.stdout + result.stderr)
        self.assertNotIn(self.token, ' '.join(request['args']))

    def test_changed_archive_is_rejected_before_upload(self):
        with self.archive.open('ab') as archive:
            archive.write(b'changed after packaging')
        result = self.run_upload()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.capture.exists())
        self.assertNotIn('MOM_ACTION=deploy', result.stdout)
        self.assertNotIn(self.token, result.stdout + result.stderr)

    def test_redirect_or_transport_failure_has_no_deployment_handoff(self):
        for status, exit_code in [('302', '0'), ('500', '22')]:
            with self.subTest(status=status):
                self.capture.unlink(missing_ok=True)
                result = self.run_upload(FAKE_CURL_HTTP_STATUS=status, FAKE_CURL_EXIT=exit_code)
                self.assertNotEqual(result.returncode, 0)
                self.assertTrue(self.capture.exists())
                self.assertNotIn('MOM_COMPONENT=backend', result.stdout)
                self.assertNotIn('MOM_PACKAGE_SHA256=', result.stdout)
                self.assertNotIn(self.token, result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
