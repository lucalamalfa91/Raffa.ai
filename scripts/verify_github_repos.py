#!/usr/bin/env python3
"""Verify the Raffa product remote: identity, folder layout, no secrets.

Task E01/F01/US01/T01 (parent story AC-1, AC-2, AC-4). The product remote
already exists at https://github.com/lucalamalfa91/raffa under the
**lucalamalfa91 user account** (not a GitHub organization) and is the single
public monorepo (ADR-014) -- not four separate remotes. This script turns
that shape into a checkable fact instead of a one-off console read:

  1. repo identity  -- owner (user, not org), name, visibility, description,
                        default branch. GitHub REST API via `gh api`,
                        read-only.
  2. folder layout  -- infra/, backend/, web/, mobile/, .helix/ present at
                        the repo root. Local filesystem, not the GitHub
                        contents API: an implementer's task branch is not
                        pushed by the implementer (Helix's stop hook owns
                        the PR to origin/main), so newly-added folders exist
                        on disk here before they exist on the remote.
  3. secret scan    -- no connection-string, key, SAS-token, or PAT shaped
                        string in any git-tracked file (AC-4). Scans the
                        working tree's tracked files, not just staged diffs.

Read-only end to end: no mutating GitHub API call, no write to the working
tree, no git push.

Owner/repo resolve from RAFFA_GITHUB_OWNER / RAFFA_GITHUB_REPO
(defaults lucalamalfa91 / raffa). RAFFA_GITHUB_ORG is accepted as an
alias for the owner -- kept for parity with the equivalent process-side
check in .helix/scripts/verify_github_repos.py. Authenticates via whatever
`gh` already has configured; this script never handles a token itself.

Usage:
    python scripts/verify_github_repos.py

Exit 0 only if all three checks pass. Non-zero otherwise, with a per-check
PASS/FAIL line on stdout (the failing ones also echoed to stderr).
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

OWNER_ENV = "RAFFA_GITHUB_OWNER"
ORG_ENV = "RAFFA_GITHUB_ORG"
REPO_ENV = "RAFFA_GITHUB_REPO"
DEFAULT_OWNER = "lucalamalfa91"
DEFAULT_REPO = "raffa"
EXPECTED_DESCRIPTION = "Raffa platform"
EXPECTED_DEFAULT_BRANCH = "main"

DOMAIN_FOLDERS = ("infra", "backend", "web", "mobile", ".helix")

REPO_ROOT = Path(__file__).resolve().parent.parent

# Coarse-but-cheap shapes for the secret material the task names (connection
# string, key, SAS token, PAT). False positives are an eyeball away; false
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

# This file necessarily contains the patterns above as literal text, so
# scanning it would self-match every rule. Nothing else is excluded: the
# whole point of AC-4 is that no tracked file gets a free pass.
SECRET_SCAN_SELF_EXCLUDE = "scripts/verify_github_repos.py"
SECRET_SCAN_EXCLUDE_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip",
    ".woff", ".woff2", ".ttf", ".eot", ".lock",
}


def _owner() -> str:
    return (
        (os.environ.get(OWNER_ENV) or "").strip()
        or (os.environ.get(ORG_ENV) or "").strip()
        or DEFAULT_OWNER
    )


def _repo() -> str:
    return (os.environ.get(REPO_ENV) or "").strip() or DEFAULT_REPO


def _gh(*args: str) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(["gh", *args], text=True, capture_output=True, timeout=30)
    except FileNotFoundError as exc:
        raise RuntimeError("gh CLI not found on PATH") from exc
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("gh CLI timed out") from exc


def _run_git(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=REPO_ROOT, text=True, capture_output=True, timeout=30)


def check_repo_identity(owner: str, repo: str) -> tuple[bool, str]:
    try:
        proc = _gh("api", f"repos/{owner}/{repo}")
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"gh api repos/{owner}/{repo} failed (exit {proc.returncode}): {detail}"
    try:
        data = json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        return False, f"could not parse gh api response: {exc}"

    want_full_name = f"{owner}/{repo}"
    problems: list[str] = []
    if data.get("full_name") != want_full_name:
        problems.append(f"full_name={data.get('full_name')!r}, want {want_full_name!r}")
    if (data.get("owner") or {}).get("type") == "Organization":
        problems.append(
            "owner is a GitHub Organization; AC-1 requires the lucalamalfa91 "
            "user account, not a Raffa org"
        )
    if bool(data.get("private")):
        problems.append("repo is private; AC-2 requires public")
    if (data.get("description") or "") != EXPECTED_DESCRIPTION:
        problems.append(f"description={data.get('description')!r}, want {EXPECTED_DESCRIPTION!r}")
    if data.get("default_branch") != EXPECTED_DEFAULT_BRANCH:
        problems.append(f"default_branch={data.get('default_branch')!r}, want {EXPECTED_DEFAULT_BRANCH!r}")

    if problems:
        return False, "; ".join(problems)
    return True, (
        f"{want_full_name}: public, description={EXPECTED_DESCRIPTION!r}, "
        f"default_branch={EXPECTED_DEFAULT_BRANCH!r}"
    )


def check_domain_folders() -> tuple[bool, str]:
    missing = [f for f in DOMAIN_FOLDERS if not (REPO_ROOT / f).is_dir()]
    if missing:
        return False, f"missing at repo root ({REPO_ROOT}): {', '.join(missing)}"
    return True, f"present at repo root: {', '.join(DOMAIN_FOLDERS)}"


def check_no_committed_secrets() -> tuple[bool, str]:
    proc = _run_git("ls-files")
    if proc.returncode != 0:
        return False, f"git ls-files failed (exit {proc.returncode}): {proc.stderr.strip()}"
    files = [line for line in proc.stdout.splitlines() if line]

    hits: list[str] = []
    for rel_path in files:
        if rel_path == SECRET_SCAN_SELF_EXCLUDE:
            continue
        if Path(rel_path).suffix.lower() in SECRET_SCAN_EXCLUDE_SUFFIXES:
            continue
        abs_path = REPO_ROOT / rel_path
        try:
            text = abs_path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        for label, pattern in SECRET_PATTERNS:
            match = pattern.search(text)
            if match:
                line_no = text.count("\n", 0, match.start()) + 1
                hits.append(f"{rel_path}:{line_no} looks like {label}")

    if hits:
        return False, "; ".join(hits)
    return True, f"{len(files)} tracked files scanned, no secret-shaped strings found"


def main() -> int:
    owner = _owner()
    repo = _repo()

    checks = [
        ("repo identity", check_repo_identity(owner, repo)),
        ("domain folders", check_domain_folders()),
        ("secret scan", check_no_committed_secrets()),
    ]

    ok = True
    for name, (passed, detail) in checks:
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print(f"[verify_github_repos] PASS: {owner}/{repo} matches the required shape")
        return 0
    print("[verify_github_repos] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
