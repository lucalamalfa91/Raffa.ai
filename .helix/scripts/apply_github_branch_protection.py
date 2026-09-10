#!/usr/bin/env python3
"""Apply `main`-branch protection to lucalamalfa91/raffa, per ADR-014.

Require pull request, no direct push, status-checks machinery enabled —
task E01/F01/US01/T01 AC-3. Idempotent: the PUT sets the full desired state
every run, so re-running is safe.

Owner/repo resolve from RAFFA_GITHUB_OWNER / RAFFA_GITHUB_REPO
(defaults lucalamalfa91 / raffa). RAFFA_GITHUB_ORG is accepted as an
alias for the owner. Authenticates via whatever `gh` already has configured.

The product remote is **public**. Classic branch protection works on GitHub
Free for public repos.

Settings:

- required_pull_request_reviews present -> a PR is mandatory to reach
  `main` (no direct push). required_approving_review_count is 0 because
  no human/agent reviewer is guaranteed available for every merge — the
  PR gate must not deadlock on an approval nobody may be able to give.
- enforce_admins true -> "no direct push" has no admin bypass.
- required_status_checks present (contexts empty) -> the status-check gate
  exists and is enabled; no CI workflow has been created yet in this task's
  scope, so there are no named contexts to require yet.
- allow_force_pushes / allow_deletions false -> main cannot be rewritten or
  removed.

Usage:
  python scripts/apply_github_branch_protection.py
  python scripts/apply_github_branch_protection.py --verify-only
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess

OWNER_ENV = "RAFFA_GITHUB_OWNER"
ORG_ENV = "RAFFA_GITHUB_ORG"
REPO_ENV = "RAFFA_GITHUB_REPO"
DEFAULT_OWNER = "lucalamalfa91"
DEFAULT_REPO = "raffa"

PROTECTION = {
    "required_status_checks": {"strict": False, "contexts": []},
    "enforce_admins": True,
    "required_pull_request_reviews": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews": False,
        "require_code_owner_reviews": False,
        "require_last_push_approval": False,
    },
    "restrictions": None,
    "required_linear_history": False,
    "allow_force_pushes": False,
    "allow_deletions": False,
    "block_creations": False,
    "required_conversation_resolution": False,
    "lock_branch": False,
    "allow_fork_syncing": False,
}


def _owner() -> str:
    return (
        (os.environ.get(OWNER_ENV) or "").strip()
        or (os.environ.get(ORG_ENV) or "").strip()
        or DEFAULT_OWNER
    )


def _repo() -> str:
    return (os.environ.get(REPO_ENV) or "").strip() or DEFAULT_REPO


def _gh(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["gh", *args], text=True, capture_output=True, input=input_text)


def apply_one(owner: str, repo: str) -> bool:
    proc = _gh(
        "api",
        "--method", "PUT",
        f"repos/{owner}/{repo}/branches/main/protection",
        "-H", "Accept: application/vnd.github+json",
        "--input", "-",
        input_text=json.dumps(PROTECTION),
    )
    if proc.returncode != 0:
        print(f"ERROR   {owner}/{repo}: apply failed (exit {proc.returncode})")
        print(proc.stderr.strip())
        return False
    print(f"applied {owner}/{repo}: main branch protection set")
    return True


def verify_one(owner: str, repo: str) -> bool:
    proc = _gh("api", f"repos/{owner}/{repo}/branches/main/protection")
    if proc.returncode != 0:
        print(f"FAIL    {owner}/{repo}: no protection found (exit {proc.returncode})")
        print(proc.stderr.strip())
        return False
    data = json.loads(proc.stdout)
    pr_required = "required_pull_request_reviews" in data
    checks_enabled = "required_status_checks" in data
    no_force_push = not (data.get("allow_force_pushes") or {}).get("enabled", False)
    ok = pr_required and checks_enabled and no_force_push
    status = "OK" if ok else "FAIL"
    print(
        f"{status:6} {owner}/{repo}: require_pr={pr_required} status_checks={checks_enabled} "
        f"no_force_push={no_force_push}"
    )
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--verify-only",
        action="store_true",
        help="skip the PUT, only report the current protection state",
    )
    args = ap.parse_args()

    owner = _owner()
    repo = _repo()
    print(
        f"[apply_github_branch_protection] owner={owner} repo={repo} "
        f"verify_only={args.verify_only}"
    )

    ok = True
    if not args.verify_only:
        if not apply_one(owner, repo):
            ok = False
    if ok and not verify_one(owner, repo):
        ok = False

    if ok:
        print(
            f"[apply_github_branch_protection] PASS: {owner}/{repo} requires a PR "
            "on main (no direct push) with status checks enabled"
        )
        return 0
    print("[apply_github_branch_protection] FAIL: see above")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
