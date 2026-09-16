"""Read-only access to the product clone (parent of this artifact).

Native Helix file tools refuse `..` and stay inside `.helix`. Passata 1 still
has to audit `backend/`, `web/`, `infra/`, `.github/`, `docs/`, `scripts/`.
These callables are the declared escape: paths are rooted at the product clone
and cannot write, and cannot enter `.helix` (process files use native tools).
"""

from __future__ import annotations

import json
import re
from pathlib import Path

_PLUGIN_FILE = Path(__file__).resolve()
_ARTIFACT_DIR = _PLUGIN_FILE.parents[1]  # .helix
_REPO_ROOT = _PLUGIN_FILE.parents[2]  # product clone

_SKIP_DIR_NAMES = {
    ".git",
    "node_modules",
    "__pycache__",
    ".venv",
    "bin",
    "obj",
    "dist",
    "coverage",
    ".helix",
}

_TEXT_SUFFIXES = {
    ".cs",
    ".ts",
    ".tsx",
    ".js",
    ".jsx",
    ".json",
    ".md",
    ".yml",
    ".yaml",
    ".sql",
    ".tf",
    ".csproj",
    ".sln",
    ".props",
    ".targets",
    ".css",
    ".html",
    ".xml",
    ".bicep",
    ".ps1",
    ".sh",
    ".py",
    ".toml",
    ".editorconfig",
    ".gitignore",
    ".dockerignore",
    ".txt",
}

_MAX_FILE_BYTES = 1_000_000


def _include_ok(hit: Path, include: str) -> bool:
    if not include:
        return True
    name = hit.name
    if include.startswith("*."):
        return hit.suffix.lower() == include[1:].lower()
    return include in name or hit.match(include)


def _normalize(path: str) -> str:
    raw = (path or "").strip().replace("\\", "/")
    while raw.startswith("./"):
        raw = raw[2:]
    while raw.startswith("../"):
        raw = raw[3:]
    if raw.startswith("/"):
        raw = raw.lstrip("/")
    return raw


def _resolve(path: str) -> Path | str:
    rel = _normalize(path)
    if not rel or rel in {".", ".."}:
        return "refused: empty or parent path"
    candidate = (_REPO_ROOT / rel).resolve()
    try:
        candidate.relative_to(_REPO_ROOT)
    except ValueError:
        return f"refused: {path} escapes the product clone"
    try:
        candidate.relative_to(_ARTIFACT_DIR)
        return f"refused: {path} is under .helix — use native read_file/grep/glob"
    except ValueError:
        pass
    return candidate


def read_product(path: str, offset: int = 0, limit: int = 100_000) -> str:
    """Read a product-clone file (backend/, web/, infra/, .github/, docs/, scripts/).

    Paths may be `backend/src/...` or the older `../backend/src/...`. Process
    files under `.helix/` are refused — use native `read_file` for those.

    Args:
        path: file relative to the product clone root.
        offset: zero-based character offset.
        limit: maximum characters returned.
    """
    target = _resolve(path)
    if isinstance(target, str):
        return target
    if not target.is_file():
        return f"not found: {path}"
    if target.stat().st_size > _MAX_FILE_BYTES:
        return f"too large: {path} ({target.stat().st_size} bytes)"
    text = target.read_text(encoding="utf-8", errors="replace")
    if not isinstance(offset, int) or isinstance(offset, bool) or offset < 0:
        return "error: offset must be a non-negative integer"
    if not isinstance(limit, int) or isinstance(limit, bool) or limit <= 0:
        return "error: limit must be a positive integer"
    total = len(text)
    if offset > total:
        return f"error: offset {offset} exceeds file length {total}"
    page = text[offset : offset + limit]
    next_offset = offset + len(page)
    complete = next_offset >= total
    metadata = {
        "path": _normalize(path),
        "offset": offset,
        "limit": limit,
        "returned_chars": len(page),
        "total_chars": total,
        "complete": complete,
        "next_offset": None if complete else next_offset,
    }
    return f"[READ_PRODUCT_PAGE {json.dumps(metadata, ensure_ascii=False)}]\n{page}"


def glob_product(pattern: str, path: str = "", max_results: int = 200) -> str:
    """List product-clone files matching a glob pattern.

    Args:
        pattern: glob, `**` allowed (e.g. `**/*.cs`, `**/Portfolio*.cs`).
        path: optional subdirectory under the product clone to search from.
        max_results: cap (default 200).
    """
    root: Path
    if path:
        resolved = _resolve(path)
        if isinstance(resolved, str):
            return resolved
        if not resolved.is_dir():
            return f"not a directory: {path}"
        root = resolved
    else:
        root = _REPO_ROOT
    if not isinstance(max_results, int) or isinstance(max_results, bool) or max_results <= 0:
        return "error: max_results must be a positive integer"
    matches: list[str] = []
    for hit in root.glob(pattern):
        if not hit.is_file():
            continue
        if any(part in _SKIP_DIR_NAMES for part in hit.parts):
            continue
        try:
            hit.relative_to(_ARTIFACT_DIR)
            continue
        except ValueError:
            pass
        try:
            rel = hit.resolve().relative_to(_REPO_ROOT).as_posix()
        except ValueError:
            continue
        matches.append(rel)
        if len(matches) >= max_results:
            break
    matches.sort()
    if not matches:
        return f"(no matches for {pattern!r} under {path or '.'})"
    return "\n".join(matches)


def grep_product(
    pattern: str,
    path: str = "",
    include: str = "",
    ignore_case: bool = False,
    max_matches: int = 200,
) -> str:
    """Regex search over product-clone file contents. Returns `path:line: text`.

    Args:
        pattern: Python regular expression.
        path: file or directory under the product clone (empty = whole clone,
            still skipping .helix and junk dirs).
        include: optional glob of filenames to keep (e.g. `*.cs`, `*.tsx`).
        ignore_case: case-insensitive search.
        max_matches: cap (default 200).
    """
    flags = re.IGNORECASE if ignore_case else 0
    try:
        rx = re.compile(pattern, flags)
    except re.error as exc:
        return f"error: invalid regex: {exc}"
    if not isinstance(max_matches, int) or isinstance(max_matches, bool) or max_matches <= 0:
        return "error: max_matches must be a positive integer"

    start: Path
    if path:
        resolved = _resolve(path)
        if isinstance(resolved, str):
            return resolved
        start = resolved
    else:
        start = _REPO_ROOT

    files: list[Path] = []
    if start.is_file():
        files = [start]
    elif start.is_dir():
        glob_pat = f"**/{include}" if include else "**/*"
        for hit in start.glob(glob_pat):
            if not hit.is_file():
                continue
            if any(part in _SKIP_DIR_NAMES for part in hit.parts):
                continue
            try:
                hit.relative_to(_ARTIFACT_DIR)
                continue
            except ValueError:
                pass
            if hit.suffix.lower() not in _TEXT_SUFFIXES and hit.name not in {
                "Dockerfile",
                "Makefile",
                "LICENSE",
            }:
                continue
            files.append(hit)
    else:
        return f"not found: {path}"

    rows: list[str] = []
    for file in files:
        try:
            if file.stat().st_size > _MAX_FILE_BYTES:
                continue
            text = file.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        try:
            rel = file.resolve().relative_to(_REPO_ROOT).as_posix()
        except ValueError:
            continue
        for i, line in enumerate(text.splitlines(), start=1):
            if rx.search(line):
                rows.append(f"{rel}:{i}: {line}")
                if len(rows) >= max_matches:
                    return "\n".join(rows)
    if not rows:
        return f"(no matches for {pattern!r})"
    return "\n".join(rows)
