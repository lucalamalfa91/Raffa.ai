#!/usr/bin/env python3
"""Stand-alone secret scan + five-folder layout check for lucalamalfa91/raffa.

Task E01/F01/US01/T02 (parent story `us-01-github-org-repo-protection`,
AC-2 folder layout + AC-4 no committed secrets). This is split out of
`scripts/verify_github_repos.py` (task T01) on purpose: T01's script also
asserts repo *identity* (owner/visibility/description) via `gh api`, which
needs network access and an authenticated `gh` session. This script needs
neither -- it only reads the local working tree and shells out to
`git ls-files`, so it can run anywhere this repo is checked out, including
as a CI status check with no GitHub token in scope (ADR-014 notes
`REQUIRED_STATUS_CHECK_CONTEXTS` in `apply_github_branch_protection.py` is
empty only until a job like this one exists to name).

Two checks, both read-only, neither touches the network or the working tree:

  1. folder layout -- infra/, backend/, web/, mobile/, .helix/ present at
     the repo root (ADR-014: one monorepo, these five folders, not four
     remotes).
  2. secret scan    -- no connection-string, key, SAS-token, or PAT shaped
     string in any git-tracked file (parent story AC-4). Scans the working
     tree's tracked files (via `git ls-files`), so it also catches secrets
     staged but not yet committed.

Usage:
    python scripts/repo_secret_scan.py

Exit 0 if both checks pass. Non-zero otherwise, with a PASS/FAIL line per
check on stdout (failures also echoed to stderr).
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

DOMAIN_FOLDERS = ("infra", "backend", "web", "mobile", ".helix")

# Same coarse-but-cheap shapes as scripts/verify_github_repos.py (AC-4 is
# shared between both scripts). False positives are an eyeball away; false
# negatives are not -- patterns lean wide on purpose.
SECRET_PATTERNS = (
    ("Azure Storage account key", re.compile(r"(?i)AccountKey=[A-Za-z0-9+/]{20,}={0,2}")),
    ("connection-string password", re.compile(r"(?i)\bPassword=[^;\s\"'<>]{8,}")),
    ("Azure SAS token", re.compile(r"(?i)[?&]sig=[A-Za-z0-9%]{20,}")),
    ("GitHub token", re.compile(r"gh[pousr]_[A-Za-z0-9]{20,}")),
    ("GitHub fine-grained PAT", re.compile(r"github_pat_[A-Za-z0-9_]{20,}")),
    ("AWS access key id", re.compile(r"AKIA[0-9A-Z]{16}")),
    ("private key block", re.compile(r"-----BEGIN (RSA|EC|OPENSSH|PGP|DSA) PRIVATE KEY-----")),
    (
        "inline api-key/secret/token assignment",
        re.compile(
            r"(?i)\b(api[_-]?key|client[_-]?secret|access[_-]?token)\b\s*[:=]\s*"
            r"[\"'][A-Za-z0-9/+_.=-]{16,}[\"']"
        ),
    ),
)

# Files that necessarily contain the patterns above as literal text and get
# a deliberate, documented pass instead of a false FAIL:
#   - this script defines the patterns themselves as regex source;
#   - its test file plants fake-but-pattern-shaped values as fixtures to
#     prove detection works (see tests/test_repo_secret_scan.py);
#   - task E01/F02/US04/T01's test file does the same thing to prove its
#     own scripts/entra_keyvault_provision_scan.py's no-secret-literal
#     check (which reuses find_secret_matches from this module) actually
#     detects a planted secret (see tests/test_entra_keyvault_provision_scan.py);
#   - task E01/F02/US04/T02's test file does the same thing for its own
#     scripts/keyvault_scope_grants_check.py no-secret-literal check (see
#     tests/test_keyvault_scope_grants_check.py).
# Nothing else is excluded by name: the whole point of AC-4 is that no
# other tracked file gets a free pass.
SECRET_SCAN_SELF_EXCLUDE = {
    "scripts/repo_secret_scan.py",
    "tests/test_repo_secret_scan.py",
    "tests/test_entra_keyvault_provision_scan.py",
    "tests/test_keyvault_scope_grants_check.py",
}
SECRET_SCAN_EXCLUDE_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip",
    ".woff", ".woff2", ".ttf", ".eot", ".lock",
}


def find_missing_domain_folders(repo_root: Path) -> list[str]:
    """Return the DOMAIN_FOLDERS entries not present as a directory under repo_root."""
    return [f for f in DOMAIN_FOLDERS if not (repo_root / f).is_dir()]


def find_secret_matches(rel_path: str, text: str) -> list[str]:
    """Return one 'path:line looks like <label>' string per pattern found in text."""
    hits: list[str] = []
    for label, pattern in SECRET_PATTERNS:
        match = pattern.search(text)
        if match:
            line_no = text.count("\n", 0, match.start()) + 1
            hits.append(f"{rel_path}:{line_no} looks like {label}")
    return hits


def list_tracked_files(repo_root: Path) -> list[str]:
    proc = subprocess.run(
        ["git", "ls-files"], cwd=repo_root, text=True, capture_output=True, timeout=30
    )
    if proc.returncode != 0:
        raise RuntimeError(f"git ls-files failed (exit {proc.returncode}): {proc.stderr.strip()}")
    return [line for line in proc.stdout.splitlines() if line]


def scan_tracked_files_for_secrets(repo_root: Path) -> tuple[list[str], int]:
    """Scan every git-tracked file under repo_root. Return (hits, files_scanned)."""
    files = list_tracked_files(repo_root)
    hits: list[str] = []
    scanned = 0
    for rel_path in files:
        if rel_path in SECRET_SCAN_SELF_EXCLUDE:
            continue
        if Path(rel_path).suffix.lower() in SECRET_SCAN_EXCLUDE_SUFFIXES:
            continue
        abs_path = repo_root / rel_path
        try:
            text = abs_path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        scanned += 1
        hits.extend(find_secret_matches(rel_path, text))
    return hits, scanned


def check_domain_folders(repo_root: Path = REPO_ROOT) -> tuple[bool, str]:
    missing = find_missing_domain_folders(repo_root)
    if missing:
        return False, f"missing at repo root ({repo_root}): {', '.join(missing)}"
    return True, f"present at repo root: {', '.join(DOMAIN_FOLDERS)}"


def check_no_committed_secrets(repo_root: Path = REPO_ROOT) -> tuple[bool, str]:
    try:
        hits, scanned = scan_tracked_files_for_secrets(repo_root)
    except RuntimeError as exc:
        return False, str(exc)
    if hits:
        return False, "; ".join(hits)
    return True, f"{scanned} tracked files scanned, no secret-shaped strings found"


def main() -> int:
    checks = [
        ("domain folders", check_domain_folders()),
        ("secret scan", check_no_committed_secrets()),
    ]

    ok = True
    for name, (passed, detail) in checks:
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print("[repo_secret_scan] PASS: five-folder layout present, no committed secrets")
        return 0
    print("[repo_secret_scan] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
