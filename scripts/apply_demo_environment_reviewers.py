#!/usr/bin/env python3
"""Create/lock the `demo` GitHub Environment's required reviewers.

Task E01/F03/US03/T02 (parent story `us-03-promotion-dev-demo`; ADR-016:
promotion of a release from `dev` to `demo` is gated by a `demo` GitHub
Environment with required reviewers). Story AC-2 is "A `demo` GitHub
Environment with required reviewers gates the deploy" -- this script is
what actually makes that true on the live repo, the same way
`scripts/apply_github_branch_protection.py` (E01/F01/US01/T01) makes `main`
branch protection true rather than merely described in a workflow file.

`.github/workflows/demo-promote.yml`'s `demo-promotion` job (and the
`apply`/`deploy` jobs it reuses from `infra.yml`/`backend.yml`/`web.yml` via
`target_environment: demo`) already run `environment: demo` -- naming an
environment in workflow YAML is enough for GitHub to run the job under it,
but a *bare* environment has no protection rules. GitHub only exposes
required reviewers via repo Settings, the Environments REST API, or
Terraform (see `demo-promote.yml`'s own header comment) -- never via
workflow YAML. This script is that REST-API call.

Required reviewers, per OQ-DM-002 / this story's "Council decisions carried
into this story": product-owner + security-architect. Both council seats
resolve, today, to the same single real GitHub account with write access to
`lucalamalfa91/raffa`: `lucalamalfa91`. This is not a scope-reduction of
OQ-DM-002 -- it is the same fact already established (and accepted) for
this exact repo by `apply_github_branch_protection.py`'s own docstring:
"Raffa's main has exactly one account (lucalamalfa91) with write access
and no standing second reviewer." GitHub Environment reviewers must be a
real user or team `id`, not a role label, and GitHub Teams exist only
inside Organizations -- `lucalamalfa91/raffa` is a personal-account repo,
so a Team reviewer is not constructible here either. The environment is
therefore locked to the one account that actually holds each of those two
seats today; adding a second, distinct reviewer is a later task for the day
a second real account is granted either seat (tracked by OQ-DM-002, not
redecided here). `RAFFA_DEMO_ENVIRONMENT_REVIEWERS` (below) is the seam
for that day -- it takes a comma-separated list of GitHub logins, not a
hardcoded single account.

`prevent_self_review` is left `False` (GitHub's own default) and set
explicitly so the reason is on record: with a single real reviewer account,
`True` would make every `demo-v*` promotion permanently unapprovable by the
same person who is also the only one who can push the tag -- an identical
deadlock to the one `apply_github_branch_protection.py` already avoided by
choosing `required_approving_review_count: 0` over `>= 1`.

No `deployment_branch_policy` restriction is set (sent as `None`, i.e.
unrestricted): `demo-promote.yml`'s own trigger (`on.push.tags: demo-v*`)
plus its `verify-tag-on-main` job already restrict *what* can reach this
environment (ADR-016, ADR-014). Environment deployment-branch policies only
match branch/tag name patterns, not "must be an ancestor of main", so
duplicating the restriction here would be a second, looser copy of the same
rule -- out of this task's "demo-reviewers" scope.

Idempotent: the PUT always sends the full desired state, so re-running is
safe and converges rather than layering on prior runs (same contract as
`apply_github_branch_protection.py`).

Verified live against `lucalamalfa91/raffa` on 2026-09-03 (`gh api
repos/lucalamalfa91/raffa/environments/demo`, before this script
existed): GitHub's GET response nests each reviewer's id under
`protection_rules[].reviewers[].reviewer.id` (a different shape from the
PUT body's flat `reviewers[].id`), and `prevent_self_review` lives on the
`required_reviewers` protection rule itself, not on the environment object
-- e.g.:

    {
      "name": "demo",
      "protection_rules": [
        {
          "type": "required_reviewers",
          "prevent_self_review": false,
          "reviewers": [
            {"type": "User", "reviewer": {"login": "lucalamalfa91", "id": 57912352}}
          ]
        }
      ],
      "deployment_branch_policy": null
    }

`extract_required_reviewers`/`describe_gaps` below are written against this
verified shape, not the general docs example.

Owner/repo resolve from RAFFA_GITHUB_OWNER / RAFFA_GITHUB_REPO
(defaults lucalamalfa91 / raffa); RAFFA_GITHUB_ORG is accepted as an
alias for the owner -- same env vars, same defaults, as every other
scripts/*.py in this repo that talks to GitHub. Reviewer logins resolve
from RAFFA_DEMO_ENVIRONMENT_REVIEWERS (comma-separated GitHub logins,
default "lucalamalfa91"). Authenticates via whatever `gh` already has
configured -- this script never handles a token itself, so there is
nothing here to leak.

Usage:
    python scripts/apply_demo_environment_reviewers.py
    python scripts/apply_demo_environment_reviewers.py --check-only

--check-only skips the PUT and only reports the current environment state
(useful in CI to detect drift without re-applying).

Exit 0 only if, after the run, the `demo` environment exists and its
`required_reviewers` protection rule lists exactly the resolved reviewer
account(s) with `prevent_self_review: false`. Non-zero otherwise, with the
gap named.
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
REVIEWERS_ENV = "RAFFA_DEMO_ENVIRONMENT_REVIEWERS"
DEFAULT_OWNER = "lucalamalfa91"
DEFAULT_REPO = "raffa"
# OQ-DM-002: product-owner + security-architect both resolve, today, to this
# one real account -- see module docstring.
DEFAULT_REVIEWER_LOGINS: tuple[str, ...] = ("lucalamalfa91",)
ENVIRONMENT_NAME = "demo"


def _owner() -> str:
    return (
        (os.environ.get(OWNER_ENV) or "").strip()
        or (os.environ.get(ORG_ENV) or "").strip()
        or DEFAULT_OWNER
    )


def _repo() -> str:
    return (os.environ.get(REPO_ENV) or "").strip() or DEFAULT_REPO


def _reviewer_logins() -> list[str]:
    raw = (os.environ.get(REVIEWERS_ENV) or "").strip()
    if not raw:
        return list(DEFAULT_REVIEWER_LOGINS)
    logins = [login.strip() for login in raw.split(",") if login.strip()]
    return logins or list(DEFAULT_REVIEWER_LOGINS)


def _gh(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            ["gh", *args], text=True, capture_output=True, input=input_text, timeout=30
        )
    except FileNotFoundError as exc:
        raise RuntimeError("gh CLI not found on PATH") from exc
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError("gh CLI timed out") from exc


def resolve_user_id(login: str) -> tuple[bool, int | str]:
    """GET the numeric account id for a GitHub login (Environment reviewers
    are addressed by id, never by login)."""
    try:
        proc = _gh("api", f"users/{login}")
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"GET users/{login} failed (exit {proc.returncode}): {detail}"
    try:
        payload = json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        return False, f"could not parse user {login!r} response: {exc}"
    user_id = payload.get("id")
    if not isinstance(user_id, int):
        return False, f"user {login!r} response has no numeric id: {payload!r}"
    return True, user_id


def resolve_reviewer_ids(logins: list[str]) -> tuple[bool, list[int] | str]:
    ids: list[int] = []
    for login in logins:
        ok, result = resolve_user_id(login)
        if not ok:
            return False, f"resolve {login!r}: {result}"
        assert isinstance(result, int)
        ids.append(result)
    return True, ids


def apply_environment(owner: str, repo: str, desired: dict) -> tuple[bool, str]:
    try:
        proc = _gh(
            "api",
            "--method", "PUT",
            f"repos/{owner}/{repo}/environments/{ENVIRONMENT_NAME}",
            "-H", "Accept: application/vnd.github+json",
            "-H", "Content-Type: application/json",
            "--input", "-",
            input_text=json.dumps(desired),
        )
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"PUT environment {ENVIRONMENT_NAME!r} failed (exit {proc.returncode}): {detail}"
    return True, f"applied {ENVIRONMENT_NAME!r} environment reviewers on {owner}/{repo}"


def get_environment(owner: str, repo: str) -> tuple[bool, dict | str]:
    try:
        proc = _gh("api", f"repos/{owner}/{repo}/environments/{ENVIRONMENT_NAME}")
    except RuntimeError as exc:
        return False, str(exc)
    if proc.returncode != 0:
        detail = proc.stderr.strip() or proc.stdout.strip()
        return False, f"GET environment {ENVIRONMENT_NAME!r} failed (exit {proc.returncode}): {detail}"
    try:
        return True, json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        return False, f"could not parse environment response: {exc}"


# ---------------------------------------------------------------------------
# Pure logic -- no network. Unit-tested directly against synthetic (and one
# live-captured) environment-response fixtures shaped like the real GitHub
# API response (see tests/test_apply_demo_environment_reviewers.py).
# ---------------------------------------------------------------------------


def build_desired_state(user_ids: list[int]) -> dict:
    """The PUT body. See module docstring for why `prevent_self_review` is
    explicitly `False` and `deployment_branch_policy` is explicitly
    unrestricted (`None`)."""
    return {
        "prevent_self_review": False,
        "reviewers": [{"type": "User", "id": uid} for uid in user_ids],
        "deployment_branch_policy": None,
    }


def find_required_reviewers_rule(state: dict) -> dict | None:
    for rule in state.get("protection_rules") or []:
        if rule.get("type") == "required_reviewers":
            return rule
    return None


def extract_required_reviewers(state: dict) -> list[tuple[str | None, int | None]]:
    """(type, id) pairs for every reviewer entry in the live
    `required_reviewers` protection rule, e.g. [("User", 57912352)]. GET
    nests each entry's id under reviewers[].reviewer.id -- see module
    docstring for the verified live shape this is written against."""
    rule = find_required_reviewers_rule(state)
    if rule is None:
        return []
    entries: list[tuple[str | None, int | None]] = []
    for entry in rule.get("reviewers") or []:
        reviewer_type = entry.get("type")
        reviewer_id = (entry.get("reviewer") or {}).get("id")
        entries.append((reviewer_type, reviewer_id))
    return entries


def describe_gaps(state: dict, expected_user_ids: list[int]) -> list[str]:
    gaps: list[str] = []

    rule = find_required_reviewers_rule(state)
    if rule is None:
        gaps.append(
            f"no required_reviewers protection rule is set on the {state.get('name')!r} "
            "environment (no human gate -- AC-2 unmet)"
        )
        return gaps

    expected_entries = {("User", uid) for uid in expected_user_ids}
    actual_entries = set(extract_required_reviewers(state))
    if actual_entries != expected_entries:
        missing = expected_entries - actual_entries
        extra = actual_entries - expected_entries
        detail_parts = []
        if missing:
            detail_parts.append(f"missing {sorted(missing)}")
        if extra:
            detail_parts.append(f"unexpected {sorted(extra)}")
        gaps.append(
            f"required reviewers {sorted(actual_entries)} != expected "
            f"{sorted(expected_entries)} ({'; '.join(detail_parts)})"
        )

    prevent_self_review = rule.get("prevent_self_review")
    if prevent_self_review is not False:
        gaps.append(
            f"prevent_self_review={prevent_self_review!r}, want False -- True would deadlock "
            "the single-reviewer-account promotion gate (see module docstring)"
        )

    return gaps


# ---------------------------------------------------------------------------
# Orchestration -- network + subprocess, not unit tested (mirrors
# apply_github_branch_protection.py / hcp_vcs_wiring.py: proven live, since
# it needs a real `gh` auth session against the real repo).
# ---------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="report the current demo environment state; do not call PUT",
    )
    args = parser.parse_args()

    owner = _owner()
    repo = _repo()
    logins = _reviewer_logins()
    print(
        f"[apply_demo_environment_reviewers] owner={owner} repo={repo} "
        f"environment={ENVIRONMENT_NAME} reviewers={logins} check_only={args.check_only}"
    )

    resolved_ok, resolved = resolve_reviewer_ids(logins)
    if not resolved_ok:
        print(f"[FAIL] resolve reviewer id(s): {resolved}", file=sys.stderr)
        return 1
    assert isinstance(resolved, list)
    expected_ids = resolved

    if not args.check_only:
        desired = build_desired_state(expected_ids)
        applied, detail = apply_environment(owner, repo, desired)
        if not applied:
            print(f"[FAIL] apply {ENVIRONMENT_NAME} environment reviewers: {detail}", file=sys.stderr)
            return 1
        print(f"[PASS] {detail}")

    ok, state = get_environment(owner, repo)
    if not ok:
        print(f"[FAIL] fetch {ENVIRONMENT_NAME} environment: {state}", file=sys.stderr)
        return 1
    assert isinstance(state, dict)

    gaps = describe_gaps(state, expected_ids)
    if gaps:
        print(
            f"[FAIL] {owner}/{repo} environment {ENVIRONMENT_NAME!r} reviewers incomplete: "
            f"{'; '.join(gaps)}",
            file=sys.stderr,
        )
        return 1

    rule = find_required_reviewers_rule(state) or {}
    actual_logins = sorted(
        (entry.get("reviewer") or {}).get("login")
        for entry in (rule.get("reviewers") or [])
        if entry.get("type") == "User" and (entry.get("reviewer") or {}).get("login")
    )
    print(
        f"[PASS] {owner}/{repo} environment {ENVIRONMENT_NAME!r} requires review from "
        f"{actual_logins} (prevent_self_review={rule.get('prevent_self_review')!r})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
