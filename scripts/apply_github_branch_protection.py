#!/usr/bin/env python3
"""Apply and verify `main`-branch protection on lucalamalfa91/raffa.

Task E01/F01/US01/T01 (parent story AC-3, ADR-014): `main` must require a
pull request, disallow direct pushes and force-pushes, and require status
checks to pass. This script makes that a reproducible API call instead of a
manual console gesture: it PUTs the desired protection state, then GETs it
back and reports what is actually set.

Idempotent: the PUT always sends the full desired state, so re-running is
safe and converges rather than layering on prior runs.

Settings and the reasoning behind each one:

- `required_pull_request_reviews` present -> a pull request is mandatory to
  reach `main`; a plain `git push origin main` is rejected. This is the
  literal "no direct push" requirement.
- `required_approving_review_count: 0` -> the PR gate exists, but merging
  does not block on an approval. Raffa's `main` has exactly one account
  (lucalamalfa91) with write access and no standing second reviewer;
  combined with `enforce_admins: true` below, a required count >= 1 would
  make every merge to `main` permanently unapprovable -- nobody could ever
  satisfy it, including the owner on their own PR, with no admin bypass to
  fall back on. That directly contradicts ADR-014's own stated consequence
  that the flow must be "deterministic for Claude Code (branch -> PR ->
  merge -> tag)". A hard-blocked trunk is not deterministic, it is bricked.
  0 keeps the PR requirement (and the ability to request/leave reviews)
  without turning it into a deadlock nobody can clear. This mirrors the
  same call already made, for the same reason, in
  .helix/scripts/apply_github_branch_protection.py.
- `enforce_admins: true` -> "no direct push" holds for the repo owner too;
  there is no admin bypass of the PR requirement.
- `required_status_checks` present with `contexts: []` -> the status-check
  gate is switched on now, even though no CI workflow exists yet at this
  task's scope (T01, R0). An empty context list means no named check
  currently blocks merging; a later CI-setup task extends
  REQUIRED_STATUS_CHECK_CONTEXTS with the concrete per-folder job names so
  they gate the merge for real.
- `allow_force_pushes` / `allow_deletions: false` -> `main` cannot be
  rewritten or removed by anyone, admin included.

Owner/repo resolve from RAFFA_GITHUB_OWNER / RAFFA_GITHUB_REPO
(defaults lucalamalfa91 / raffa). RAFFA_GITHUB_ORG is accepted as an
alias for the owner. Authenticates via whatever `gh` already has configured
-- this script never handles a token itself, so there is nothing here to
leak.

Usage:
    python scripts/apply_github_branch_protection.py
    python scripts/apply_github_branch_protection.py --check-only

--check-only skips the PUT and only reports the current protection state
(useful in CI to detect drift without re-applying).

Exit 0 only if, after the run, `main` matches every required field below.
Non-zero otherwise, with the gap named.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys

OWNER_ENV = "RAFFA_GITHUB_OWNER"
ORG_ENV = "RAFFA_GITHUB_ORG"
REPO_ENV = "RAFFA_GITHUB_REPO"
DEFAULT_OWNER = "lucalamalfa91"
DEFAULT_REPO = "raffa"
BRANCH = "main"

# No CI workflow exists yet at T01 (R0). Later CI-setup tasks add the
# concrete per-folder job names here (e.g. "backend-build", "web-build") so
# specific checks gate the merge, not merely the category.
REQUIRED_STATUS_CHECK_CONTEXTS: list[str] = []

REQUIRED_APPROVING_REVIEW_COUNT = 0  # see module docstring for why 0, not >=1

DESIRED_PROTECTION = {
    "required_status_checks": {
        "strict": False,
        "contexts": REQUIRED_STATUS_CHECK_CONTEXTS,
    },
    "enforce_admins": True,
    "required_pull_request_reviews": {
        "required_approving_review_count": REQUIRED_APPROVING_REVIEW_COUNT,
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


def _gh(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            ["gh", *args], text=True, capture_output=True, input=input_text, timeout=30
        )
    except FileNotFoundError as exc:
        raise RuntimeError("gh CLI not found on PATH") from exc
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("gh CLI timed out") from exc


def apply_protection(owner: str, repo: str) -> tuple[bool, str]:
    try:
        proc = _gh(
            "api",
            "--method", "PUT",
            f"repos/{owner}/{repo}/branches/{BRANCH}/protection",
            "-H", "Accept: application/vnd.github+json",
            "-H", "Content-Type: application/json",
            "--input", "-",
            input_text=json.dumps(DESIRED_PROTECTION),
        )
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"PUT branch protection failed (exit {proc.returncode}): {detail}"
    return True, f"applied branch protection to {owner}/{repo}@{BRANCH}"


def get_protection(owner: str, repo: str) -> tuple[bool, dict | str]:
    try:
        proc = _gh("api", f"repos/{owner}/{repo}/branches/{BRANCH}/protection")
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"GET branch protection failed (exit {proc.returncode}): {detail}"
    try:
        return True, json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        return False, f"could not parse protection response: {exc}"


def _enabled(value: object) -> bool:
    """GET responses nest most booleans as {"enabled": bool}; PUT payloads
    use a plain bool. Accept either shape."""
    if isinstance(value, dict):
        return bool(value.get("enabled"))
    return bool(value)


def describe_gaps(state: dict) -> list[str]:
    gaps: list[str] = []

    rsc = state.get("required_status_checks")
    if not rsc:
        gaps.append("required_status_checks is not set (status checks not required)")

    rpr = state.get("required_pull_request_reviews")
    if not rpr:
        gaps.append("required_pull_request_reviews is not set (no PR required -- direct push possible)")
    elif int(rpr.get("required_approving_review_count", -1)) != REQUIRED_APPROVING_REVIEW_COUNT:
        gaps.append(
            "required_approving_review_count="
            f"{rpr.get('required_approving_review_count')!r}, want {REQUIRED_APPROVING_REVIEW_COUNT!r}"
        )

    if not _enabled(state.get("enforce_admins")):
        gaps.append("enforce_admins is not enabled (admins could bypass the PR requirement)")

    if _enabled(state.get("allow_force_pushes")):
        gaps.append("allow_force_pushes is enabled")

    if _enabled(state.get("allow_deletions")):
        gaps.append("allow_deletions is enabled")

    return gaps


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="report the current protection state; do not call PUT",
    )
    args = parser.parse_args()

    owner = _owner()
    repo = _repo()
    print(f"[apply_github_branch_protection] owner={owner} repo={repo} check_only={args.check_only}")

    if not args.check_only:
        applied, detail = apply_protection(owner, repo)
        if not applied:
            print(f"[FAIL] apply branch protection: {detail}", file=sys.stderr)
            return 1
        print(f"[PASS] {detail}")

    ok, state = get_protection(owner, repo)
    if not ok:
        print(f"[FAIL] fetch branch protection: {state}", file=sys.stderr)
        return 1
    assert isinstance(state, dict)

    gaps = describe_gaps(state)
    if gaps:
        print(f"[FAIL] {owner}/{repo}@{BRANCH} protection incomplete: {'; '.join(gaps)}", file=sys.stderr)
        return 1

    rpr = state["required_pull_request_reviews"]
    rsc = state["required_status_checks"]
    print(
        f"[PASS] {owner}/{repo}@{BRANCH} is protected: "
        f"PR required (approvals>={rpr['required_approving_review_count']}), "
        f"status checks required (contexts={rsc.get('contexts', [])}), "
        f"force_pushes_allowed={_enabled(state.get('allow_force_pushes'))}, "
        f"enforce_admins={_enabled(state.get('enforce_admins'))}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
