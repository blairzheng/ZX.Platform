#!/usr/bin/env python3
"""Export allowlisted business sources for the read-only platform assistant.

Runs with Python 3.10+ and the standard library, from any working directory.
Only reads source files and atomically replaces the catalog; no application,
model, database or other service is started or contacted.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile


MAX_FILE_BYTES = 512 * 1024
MAX_CATALOG_BYTES = 12 * 1024 * 1024
MAX_DOCUMENTS = 2000
SOURCE_SUFFIXES = {".cs", ".ts", ".vue", ".md"}
BLOCKED_PARTS = {
    ".git", ".agents", ".codex", "node_modules", "bin", "obj", "tests", "__tests__",
    "ai", "assistant", "authentication", "authorization", "security", "persistence",
    "migrations", "externaldata", "login", "examples", "example", "demo", "demos",
    "preview", "deploy", "scripts", "logs", "log", "secrets", "usersecrets",
}
BLOCKED_NAMES = re.compile(
    r"(?:appsettings|secret|credential|connectionstring|ldap|jwt|token|password|aichat|assistant|authcontroller|"
    r"weatherforecast|preview|mock|\.test\.|\.spec\.)", re.IGNORECASE
)
SENSITIVE_VALUE = re.compile(
    r"(?P<prefix>\b(?:password|passwd|pwd|api[_-]?key|client[_-]?secret|access[_-]?token|"
    r"refresh[_-]?token|connection[_-]?string)\b\s*[:=]\s*)(?P<quote>['\"])(?P<value>[^'\"\r\n]+)(?P=quote)",
    re.IGNORECASE,
)
SENSITIVE_TOKEN = re.compile(
    r"(?:gpustack_[A-Za-z0-9_-]{12,}|sk-[A-Za-z0-9_-]{16,}|"
    r"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|"
    r"Bearer\s+[A-Za-z0-9._-]{20,}|"
    r"(?:https?|redis|oracle|postgres(?:ql)?|mysql)://[^\s/'\"]+:[^\s/@'\"]+@)",
    re.IGNORECASE,
)
PRIVATE_KEY = re.compile(r"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----")
PAGE_IMPORT = re.compile(r"import\(['\"]@/(views/[A-Za-z0-9_/-]+/index\.vue)['\"]\)")


def reject_links(path: Path) -> None:
    if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):
        raise ValueError("Symbolic links are not allowed in source or catalog paths.")


def safe_path(path: Path, root: Path) -> Path:
    """Reject traversal and links before resolving, including linked parents."""
    if any(part in {".", ".."} for part in path.parts):
        raise ValueError("Relative traversal is not allowed in source areas.")
    absolute = path.absolute()
    root_absolute = root.absolute()
    reject_links(absolute)
    reject_links(root_absolute)
    if not absolute.is_relative_to(root_absolute):
        raise ValueError("Source path is outside its project root.")
    resolved = absolute.resolve(strict=True)
    if not resolved.is_relative_to(root_absolute.resolve(strict=True)):
        raise ValueError("Resolved source path is outside its project root.")
    return resolved


def allowed(relative: Path) -> bool:
    return (
        relative.suffix.lower() in SOURCE_SUFFIXES
        and relative.name.lower() not in {"agents.md", "skill.md"}
        and not any(part.lower() in BLOCKED_PARTS or part.startswith(".") for part in relative.parts)
        and BLOCKED_NAMES.search(relative.name) is None
    )


def raise_walk_error(error: OSError) -> None:
    # An unreadable source area must fail, rather than silently exporting a partial catalog.
    raise error


def collect_tree(directory: Path, root: Path) -> set[Path]:
    files: set[Path] = set()
    for current, directories, names in os.walk(
            safe_path(directory, root), followlinks=False, onerror=raise_walk_error):
        for name in directories + names:
            if (Path(current) / name).is_symlink():
                raise ValueError("Symbolic link found in an allowlisted source tree.")
        directories[:] = [
            name for name in directories
            if name.lower() not in BLOCKED_PARTS and not name.startswith(".")
        ]
        for name in names:
            candidate = Path(current) / name
            if allowed(candidate.relative_to(root)):
                files.add(safe_path(candidate, root))
    return files


def read_source(path: Path, root: Path, prefix: str) -> dict:
    source = safe_path(path, root)
    with source.open("rb") as stream:
        encoded = stream.read(MAX_FILE_BYTES + 1)
    if len(encoded) > MAX_FILE_BYTES:
        raise ValueError("An allowlisted source file exceeds the export size limit.")
    text = encoded.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
    if "\x00" in text or PRIVATE_KEY.search(text):
        raise ValueError("An allowlisted source file contains binary data or a private key.")
    # Keep line counts stable so excerpts still identify the original source.
    text = SENSITIVE_VALUE.sub(lambda match: match.group("prefix") + '"[REDACTED]"', text)
    text = SENSITIVE_TOKEN.sub(lambda match: "[REDACTED]" + "\n" * match.group().count("\n"), text)
    if SENSITIVE_TOKEN.search(text):
        raise ValueError("An allowlisted source file still contains sensitive data.")
    # The .NET reader measures UTF-16 code units, including astral characters as two.
    if len(text.encode("utf-16-le")) // 2 > MAX_FILE_BYTES:
        raise ValueError("Redacted source text exceeds the runtime document size limit.")
    catalog_path = f"{prefix}/{source.relative_to(root).as_posix()}"
    if (len(catalog_path.encode("utf-16-le")) // 2 > 400 or
            any(character in catalog_path for character in ("\\", ":")) or
            any(ord(character) < 32 or 127 <= ord(character) <= 159 for character in catalog_path)):
        raise ValueError("An exported source path is not supported by the runtime catalog.")
    return {
        "path": catalog_path,
        "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
        "text": text,
    }


def write_catalog(output: Path, encoded: bytes) -> None:
    output = output.absolute()
    reject_links(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    reject_links(output.parent)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
                prefix=f".{output.name}.", suffix=".tmp", dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(encoded)
        # NamedTemporaryFile starts at 0600; the published catalog must also be
        # readable by the non-root Identity container user after Docker COPY.
        temporary.chmod(0o644)
        reject_links(output)
        os.replace(temporary, output)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def export(backend: Path, frontend: Path, output: Path) -> tuple[int, int]:
    backend = safe_path(backend, backend)
    frontend = safe_path(frontend, frontend)
    registry_path = frontend / "src/router/menuPages.ts"
    registry = read_source(registry_path, frontend, "frontend")
    frontend_files = {registry_path}
    # Registered pages determine real modules. Re-export picks up new business pages.
    for module in PAGE_IMPORT.findall(registry["text"]):
        directory = frontend / "src" / Path(module).parent
        if allowed(directory.relative_to(frontend) / "index.vue"):
            frontend_files.update(collect_tree(directory, frontend))
    for relative in ("src/api", "src/locales/zh-CN", "src/locales/en-US",
                     "src/components/DataTable", "src/components/TableRowActions.vue"):
        candidate = frontend / relative
        if candidate.is_dir():
            frontend_files.update(collect_tree(candidate, frontend))
        elif candidate.is_file() and allowed(candidate.relative_to(frontend)):
            frontend_files.add(safe_path(candidate, frontend))
    frontend_files = {
        source for source in frontend_files
        if source.name not in {"auth.ts", "users.ts", "mom.ts", "aiChat.ts", "index.ts", "vue-i18n.d.ts"}
        or not source.relative_to(frontend).as_posix().startswith("src/locales/")
    }
    frontend_files.discard(frontend / "src/api/identity/auth.ts")

    application = safe_path(backend / "Services/Identity/ZX.Platform.Identity.Application", backend)
    backend_files: set[Path] = set()
    for directory in application.iterdir():
        if directory.is_symlink():
            raise ValueError("Symbolic link found in Application source areas.")
        if directory.is_dir() and directory.name.lower() not in BLOCKED_PARTS:
            backend_files.update(collect_tree(directory, backend))
    backend_files.update(collect_tree(backend / "Services/Identity/ZX.Platform.Identity.Domain/Entities", backend))
    backend_files.update(collect_tree(backend / "Services/Identity/ZX.Platform.Identity.Api/Controllers", backend))
    if not 0 < len(frontend_files) + len(backend_files) <= MAX_DOCUMENTS:
        raise ValueError("The source catalog is empty or has too many documents.")

    documents = [read_source(source, frontend, "frontend") for source in sorted(frontend_files)]
    documents.extend(read_source(source, backend, "backend") for source in sorted(backend_files))
    documents.sort(key=lambda source: source["path"])
    payload = {
        "schemaVersion": 1,
        "exportedAtUtc": datetime.now(timezone.utc).isoformat(),
        "documents": documents,
    }
    encoded = (json.dumps(payload, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    if len(encoded) > MAX_CATALOG_BYTES:
        raise ValueError("The generated source catalog exceeds its size limit.")
    write_catalog(output, encoded)
    return len(documents), len(encoded)


def main() -> None:
    default_backend = Path(__file__).absolute().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", type=Path, default=default_backend)
    parser.add_argument("--frontend", type=Path)
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    frontend = arguments.frontend or arguments.backend.absolute().parent / "ZX.front.vue"
    output = arguments.output or arguments.backend / "Services/Identity/ZX.Platform.Identity.Api/AI/Knowledge/project-sources.json"
    count, size = export(arguments.backend, frontend, output)
    print(f"Exported {count} business source files ({size} bytes).")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError) as error:
        # Do not echo source contents or encoding errors containing original input bytes.
        print(f"Source export failed ({type(error).__name__}); check source paths, links, encoding and size limits.", file=sys.stderr)
        sys.exit(1)
