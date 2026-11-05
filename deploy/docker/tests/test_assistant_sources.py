"""Check exported assistant catalogs using temporary sources, without real services."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location(
    'assistant_sources', ROOT / 'scripts/export-assistant-sources.py')
EXPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORTER)


class AssistantSourceChecks(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        # macOS may return /var/... while /var itself is a symlink.
        self.root = Path(temporary.name).resolve()
        self.backend = self.root / 'ZX.Platform'
        self.frontend = self.root / 'ZX.front.vue'
        self.output = self.backend / 'AI/Knowledge/project-sources.json'
        self.page = self.frontend / 'src/views/inventory/stocktake/index.vue'
        self.write(self.frontend / 'src/router/menuPages.ts',
                   "export const pages = [import('@/views/inventory/stocktake/index.vue'), "
                   "import('@/views/demo/index.vue')];\n")
        self.write(self.page, '<template>盘点业务页面</template>\n')
        self.write(self.page.parent / 'components/Filters.vue', '<template>筛选</template>\n')
        self.write(self.frontend / 'src/api/inventory/stocktake.ts', 'export const report = {};\n')
        self.write(self.frontend / 'src/locales/zh-CN/inventoryStocktake.ts',
                   "export default { title: '一键盘点' };\n")
        self.write(self.backend / 'Services/Identity/ZX.Platform.Identity.Application/Inventory/ReportService.cs',
                   'public class ReportService {}\n')
        self.write(self.backend / 'Services/Identity/ZX.Platform.Identity.Domain/Entities/Item.cs',
                   'public class Item {}\n')
        self.write(self.backend / 'Services/Identity/ZX.Platform.Identity.Api/Controllers/InventoryStocktakeController.cs',
                   'public class InventoryStocktakeController {}\n')

    @staticmethod
    def write(path, content):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content if isinstance(content, bytes) else content.encode('utf-8'))

    def export(self):
        count, size = EXPORTER.export(self.backend, self.frontend, self.output)
        encoded = self.output.read_bytes()
        catalog = json.loads(encoded)
        self.assertEqual(count, len(catalog['documents']))
        self.assertEqual(size, len(encoded))
        return catalog

    def test_exports_registered_business_sources_and_excludes_unapproved_sources(self):
        excluded = [
            self.frontend / 'src/views/demo/index.vue',
            self.frontend / 'src/views/unregistered/index.vue',
            self.page.parent / 'ai/Internal.vue',
            self.frontend / 'src/api/ai/chat.ts',
            self.frontend / 'src/api/identity/auth.ts',
            self.frontend / 'src/locales/zh-CN/aiChat.ts',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Application/AI/Internal.cs',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Application/Authentication/Internal.cs',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Application/Inventory/SecretConfig.cs',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Domain/Entities/UserSecret.cs',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Api/Controllers/AuthController.cs',
            self.backend / 'Services/Identity/ZX.Platform.Identity.Api/appsettings.json',
        ]
        for path in excluded:
            self.write(path, 'EXCLUDED_SOURCE_MUST_NOT_REACH_MODEL\n')

        catalog = self.export()
        documents = {document['path']: document for document in catalog['documents']}
        self.assertEqual(catalog['schemaVersion'], 1)
        self.assertTrue(catalog['exportedAtUtc'].endswith('+00:00'))
        self.assertIn('frontend/src/views/inventory/stocktake/index.vue', documents)
        self.assertIn('frontend/src/views/inventory/stocktake/components/Filters.vue', documents)
        self.assertIn('frontend/src/api/inventory/stocktake.ts', documents)
        self.assertIn('frontend/src/locales/zh-CN/inventoryStocktake.ts', documents)
        self.assertIn('backend/Services/Identity/ZX.Platform.Identity.Application/Inventory/ReportService.cs', documents)
        self.assertIn('backend/Services/Identity/ZX.Platform.Identity.Domain/Entities/Item.cs', documents)
        self.assertIn('backend/Services/Identity/ZX.Platform.Identity.Api/Controllers/InventoryStocktakeController.cs', documents)
        self.assertEqual(list(documents), sorted(documents))
        self.assertNotIn('EXCLUDED_SOURCE_MUST_NOT_REACH_MODEL', self.output.read_text(encoding='utf-8'))

    def test_redaction_preserves_citation_lines_and_hashes_normalized_text(self):
        source = (
            '<script>\r\n'
            "const caption = '盘点😀';\r\n"
            "const password = 'fixture-private-password';\r\n"
            'const config = { api_key: "fixture-api-key" };\r\n'
            'const opaque = "gpustack_fixtureonly_1234567890123456";\r\n'
            'const header = "Bearer\r\nabcdefghijklmnopqrstuvwxyz";\r\n'
            'const after = "line-after-redaction";\r\n'
            '</script>\r\n'
        )
        self.write(self.page, b'\xef\xbb\xbf' + source.encode('utf-8'))
        catalog = self.export()
        document = next(item for item in catalog['documents']
                        if item['path'] == 'frontend/src/views/inventory/stocktake/index.vue')
        text = document['text']
        normalized = source.replace('\r\n', '\n')
        for sensitive in ('fixture-private-password', 'fixture-api-key',
                          'gpustack_fixtureonly_1234567890123456', 'abcdefghijklmnopqrstuvwxyz'):
            self.assertNotIn(sensitive, text)
        self.assertIn('[REDACTED]', text)
        self.assertIn('盘点😀', text)
        self.assertNotIn('\ufeff', text)
        self.assertNotIn('\r', text)
        self.assertEqual(text.count('\n'), normalized.count('\n'))
        self.assertEqual(text.splitlines().index('const after = "line-after-redaction";'),
                         normalized.splitlines().index('const after = "line-after-redaction";'))
        self.assertEqual(document['sha256'], hashlib.sha256(text.encode('utf-8')).hexdigest())

    def test_private_key_rejection_preserves_last_successful_catalog(self):
        self.export()
        previous = self.output.read_bytes()
        self.write(self.page, '-----BEGIN PRIVATE KEY-----\nfixture\n-----END PRIVATE KEY-----\n')
        with self.assertRaises(ValueError):
            self.export()
        self.assertEqual(self.output.read_bytes(), previous)

    def test_size_and_document_limits_preserve_last_successful_catalog(self):
        self.export()
        previous = self.output.read_bytes()
        for limit in ('MAX_FILE_BYTES', 'MAX_CATALOG_BYTES', 'MAX_DOCUMENTS'):
            with self.subTest(limit=limit), patch.object(EXPORTER, limit, 1):
                with self.assertRaises(ValueError):
                    self.export()
                self.assertEqual(self.output.read_bytes(), previous)

    def test_source_symlink_rejection_preserves_catalog_and_does_not_read_target(self):
        self.export()
        previous = self.output.read_bytes()
        outside = self.root / 'outside.vue'
        self.write(outside, 'EXTERNAL_SOURCE_MUST_NOT_REACH_MODEL\n')
        link = self.page.parent / 'components/Escape.vue'
        try:
            link.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest('Creating symlinks is unavailable on this platform.')
        with self.assertRaises(ValueError):
            self.export()
        self.assertEqual(self.output.read_bytes(), previous)
        self.assertNotIn(b'EXTERNAL_SOURCE_MUST_NOT_REACH_MODEL', previous)

    def test_failed_replacement_preserves_catalog_and_cleans_temporary_file(self):
        self.export()
        previous = self.output.read_bytes()
        self.write(self.page, '<template>更新后的业务页面</template>\n')
        with patch.object(EXPORTER.os, 'replace', side_effect=OSError('fixture disk failure')):
            with self.assertRaises(OSError):
                self.export()
        self.assertEqual(self.output.read_bytes(), previous)
        self.assertEqual(list(self.output.parent.glob('*.tmp')), [])

    @unittest.skipUnless(os.name == 'posix', 'POSIX file mode check')
    def test_catalog_can_be_read_by_non_owner_container_user(self):
        self.export()
        mode = stat.S_IMODE(self.output.stat().st_mode)
        self.assertTrue(mode & stat.S_IROTH, 'Docker COPY must not leave a root-only catalog.')


if __name__ == '__main__':
    unittest.main()
