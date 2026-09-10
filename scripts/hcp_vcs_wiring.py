#!/usr/bin/env python3
"""Wire the two HCP Terraform workspaces to raffa's VCS, then assert
remote state only.

Task E01/F01/US02/T02 (parent story `us-02-hcp-terraform-workspaces`;
ADR-007 remote state per environment; ADR-014 trunk-based git flow, single
`main` mainline). Task E01/F01/US02/T01's `bootstrap_hcp_org.py` already
contains the one mutating call that can attach VCS wiring: an idempotent
GET-then-POST/PATCH that sets `vcs-repo` on `raffa-dev`/`raffa-demo`
whenever the org has an HCP Terraform oauth-client configured. Connecting
GitHub to HCP Terraform in the first place is an interactive, human-driven
OAuth-authorize or GitHub-App-install step that no API token can complete
headlessly (T01's docstring; still true live as of 2026-09-02 -- see
`python scripts/bootstrap_hcp_org.py --check-only`, which reports zero
oauth-clients under `raffa-platform`). Re-implementing that same
POST/PATCH here would duplicate T01's HTTP plumbing for the exact same
effect, so "wire" for this script means exactly what T01's own docstring
names this task as the trigger for: **re-run T01's idempotent script** (so
a since-completed OAuth connection gets attached on this pass without
recreating either workspace), then add the two assertions this task is
actually scoped to make -- which T01 deliberately treats as non-fatal INFO,
not a gate:

  1. VCS wiring is either correct or honestly pending, never silently
     wrong. If `vcs-repo` is attached, its identifier/branch, whether
     file-triggers are scoped, and the trigger prefix must match the locked
     repo (`lucalamalfa91/raffa`), ADR-014's single `main` mainline, and
     the `infra/` prefix (so only infra changes trigger a plan/apply, not
     every change in the monorepo). If no oauth-client exists yet, that is
     PENDING: expected, non-fatal, matches live reality. A `vcs-repo`
     pointed at the wrong repo/branch/prefix is a hard FAIL -- an active
     misconfiguration must never pass silently.
  2. Remote state only. Two parts:
       a. execution-mode must be "remote" for both workspaces. This is the
          setting `bootstrap_hcp_org.py` (T01) already establishes for both
          workspaces as ADR-007's chosen operating model ("HCP Terraform
          itself runs plan/apply and owns state") -- note this is *not*
          about where the state file is stored (an HCP Terraform workspace
          always stores state centrally regardless of execution mode);
          it is asserted here because it is also a precondition for the
          VCS-triggered runs this task wires up to fire at all -- a
          workspace's VCS trigger only drives a run that the workspace
          itself executes, which requires execution-mode "remote" (or
          "agent"). A silent regression to "local" would mean pushes under
          infra/ stop triggering anything, defeating the point of wiring
          the connection.
       b. no tfstate-shaped path is tracked in git -- the literal, load-
          bearing meaning of "remote, not in git" (AC-3). Re-asserted here,
          standalone, as this task's own definition-of-done-provable check,
          using the same predicate as T01's `check_no_state_in_git`.

Auth/org resolution mirrors `bootstrap_hcp_org.py` exactly (same env vars,
same defaults) so an operator configures credentials once for both scripts:
`TFE_TOKEN` / `HCP_TERRAFORM_TOKEN`, `TFE_ADDRESS` / `HCP_TERRAFORM_ADDRESS`,
`RAFFA_TFC_ORG` (default `raffa-platform`), `RAFFA_GITHUB_OWNER` /
`RAFFA_GITHUB_REPO` (default `lucalamalfa91/raffa`).

Usage:
    python scripts/hcp_vcs_wiring.py
    python scripts/hcp_vcs_wiring.py --check-only

--check-only never re-runs bootstrap_hcp_org.py and this script makes no
mutating API call of its own either (it only ever GETs) -- it just skips the
wiring attempt and reports the live state as-is. Useful as a CI drift check
that needs no write scope.

Exit 0 only if both `raffa-dev` and `raffa-demo` are remote-state-only
(execution-mode=remote, no tracked tfstate anywhere in the repo) AND each
workspace's VCS wiring is either correctly wired or honestly pending (never
mismatched). Non-zero otherwise, with the gap named on stdout/stderr.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

TOKEN_ENV_PRIMARY = "TFE_TOKEN"
TOKEN_ENV_ALIAS = "HCP_TERRAFORM_TOKEN"
ADDRESS_ENV_PRIMARY = "TFE_ADDRESS"
ADDRESS_ENV_ALIAS = "HCP_TERRAFORM_ADDRESS"
DEFAULT_ADDRESS = "https://app.terraform.io"

ORG_ENV = "RAFFA_TFC_ORG"
DEFAULT_ORG = "raffa-platform"  # see scripts/bootstrap_hcp_org.py -- "raffa" was unavailable

GITHUB_OWNER_ENV = "RAFFA_GITHUB_OWNER"
GITHUB_REPO_ENV = "RAFFA_GITHUB_REPO"
DEFAULT_GITHUB_OWNER = "lucalamalfa91"
DEFAULT_GITHUB_REPO = "raffa"

REPO_ROOT = Path(__file__).resolve().parent.parent
BOOTSTRAP_SCRIPT = REPO_ROOT / "scripts" / "bootstrap_hcp_org.py"

# ADR-007 workspace names; ADR-014 is the one mainline branch every
# workspace must track; "infra/" is the module tree that must trigger a
# plan/apply (and, symmetrically, the only tree that should).
WORKSPACE_NAMES: tuple[str, ...] = ("raffa-dev", "raffa-demo")
EXPECTED_BRANCH = "main"
EXPECTED_TRIGGER_PREFIX = "infra/"

JSON_API_HEADERS = {
    "Content-Type": "application/vnd.api+json",
    "Accept": "application/vnd.api+json",
}


def _token() -> str | None:
    return (
        (os.environ.get(TOKEN_ENV_PRIMARY) or "").strip()
        or (os.environ.get(TOKEN_ENV_ALIAS) or "").strip()
        or None
    )


def _address() -> str:
    return (
        (os.environ.get(ADDRESS_ENV_PRIMARY) or "").strip()
        or (os.environ.get(ADDRESS_ENV_ALIAS) or "").strip()
        or DEFAULT_ADDRESS
    )


def _org() -> str:
    return (os.environ.get(ORG_ENV) or "").strip() or DEFAULT_ORG


def _github_identifier() -> str:
    owner = (os.environ.get(GITHUB_OWNER_ENV) or "").strip() or DEFAULT_GITHUB_OWNER
    repo = (os.environ.get(GITHUB_REPO_ENV) or "").strip() or DEFAULT_GITHUB_REPO
    return f"{owner}/{repo}"


class ApiError(RuntimeError):
    pass


def _get(path: str, token: str) -> tuple[int, dict]:
    url = f"{_address()}/api/v2{path}"
    req = urllib.request.Request(
        url, method="GET", headers={**JSON_API_HEADERS, "Authorization": f"Bearer {token}"}
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        try:
            return exc.code, (json.loads(raw) if raw else {})
        except json.JSONDecodeError:
            return exc.code, {"errors": [{"detail": raw.decode(errors="replace")}]}
    except urllib.error.URLError as exc:
        raise ApiError(f"could not reach {_address()}: {exc.reason}") from exc
    except TimeoutError as exc:
        raise ApiError(f"request to {_address()} timed out: {exc}") from exc


def _error_detail(payload: dict) -> str:
    errors = payload.get("errors") or []
    if not errors:
        return json.dumps(payload)[:300]
    return "; ".join(str(e.get("detail") or e.get("title") or e) for e in errors)


def fetch_workspace_attributes(token: str, org: str, name: str) -> tuple[bool, dict | str]:
    """GET a workspace's live attributes. Returns (ok, attrs) or (False, error-detail)."""
    try:
        status, payload = _get(f"/organizations/{org}/workspaces/{name}", token)
    except ApiError as exc:
        return False, str(exc)
    if status != 200:
        return False, f"GET workspace {name} failed ({status}): {_error_detail(payload)}"
    return True, payload.get("data", {}).get("attributes", {})


# ---------------------------------------------------------------------------
# Pure assertions -- no network. Unit-tested directly against synthetic
# workspace-attribute fixtures shaped like the HCP Terraform API response
# (see tests/test_hcp_vcs_wiring.py).
# ---------------------------------------------------------------------------


def assert_remote_execution_mode(attrs: dict) -> tuple[bool, str]:
    """ADR-007 operating model + precondition for VCS-triggered runs."""
    mode = attrs.get("execution-mode")
    if mode == "remote":
        return True, "execution-mode=remote (HCP Terraform runs plan/apply and owns the run)"
    return False, (
        f"execution-mode={mode!r}, want 'remote' -- ADR-007's operating model, and required "
        "for a VCS trigger to actually fire a run"
    )


def classify_vcs_wiring(
    attrs: dict,
    expected_identifier: str,
    expected_branch: str = EXPECTED_BRANCH,
    expected_trigger_prefix: str = EXPECTED_TRIGGER_PREFIX,
) -> tuple[str, str]:
    """Classify a workspace's VCS wiring. Returns (status, detail).

    status is one of:
      - "wired"      vcs-repo attached and matches repo/branch/trigger scope.
      - "pending"    no vcs-repo yet -- connecting GitHub to HCP Terraform is
                     an interactive, human-driven step no API token can
                     complete. Not a failure.
      - "mismatched" vcs-repo attached but wrong repo/branch/scope -- an
                     active misconfiguration, always a failure.
    """
    vcs_repo = attrs.get("vcs-repo")
    if not vcs_repo:
        return "pending", (
            "no vcs-repo attached yet -- connecting GitHub to HCP Terraform needs a "
            "human-driven OAuth/GitHub-App step no API token can complete"
        )

    identifier = vcs_repo.get("identifier")
    branch = vcs_repo.get("branch")
    file_triggers_enabled = attrs.get("file-triggers-enabled")
    trigger_prefixes = attrs.get("trigger-prefixes") or []

    problems: list[str] = []
    if identifier != expected_identifier:
        problems.append(f"identifier={identifier!r}, want {expected_identifier!r}")
    if branch != expected_branch:
        problems.append(f"branch={branch!r}, want {expected_branch!r} (ADR-014 single mainline)")
    if not file_triggers_enabled:
        problems.append(
            "file-triggers-enabled is not true -- every repo change would trigger a run, "
            "not just infra/"
        )
    elif expected_trigger_prefix not in trigger_prefixes:
        problems.append(
            f"trigger-prefixes={trigger_prefixes!r} missing {expected_trigger_prefix!r}"
        )

    if problems:
        return "mismatched", "; ".join(problems)
    return "wired", (
        f"vcs-repo={identifier!r} branch={branch!r} trigger-prefixes={trigger_prefixes!r}"
    )


def evaluate_workspace(name: str, attrs: dict, expected_identifier: str) -> tuple[bool, list[str]]:
    """Run both per-workspace assertions. Returns (ok, printable detail lines)."""
    lines: list[str] = []

    state_ok, state_detail = assert_remote_execution_mode(attrs)
    lines.append(f"[{'PASS' if state_ok else 'FAIL'}] {name} remote-execution-mode: {state_detail}")

    vcs_status, vcs_detail = classify_vcs_wiring(attrs, expected_identifier)
    vcs_ok = vcs_status != "mismatched"
    vcs_label = {"wired": "PASS", "pending": "WARN", "mismatched": "FAIL"}[vcs_status]
    lines.append(f"[{vcs_label}] {name} vcs-wiring ({vcs_status}): {vcs_detail}")

    return (state_ok and vcs_ok), lines


def list_tracked_files(repo_root: Path) -> list[str]:
    proc = subprocess.run(
        ["git", "ls-files"], cwd=repo_root, text=True, capture_output=True, timeout=30
    )
    if proc.returncode != 0:
        raise RuntimeError(f"git ls-files failed (exit {proc.returncode}): {proc.stderr.strip()}")
    return [line for line in proc.stdout.splitlines() if line]


def find_tracked_tfstate_paths(repo_root: Path) -> list[str]:
    """Same tfstate-shaped-path predicate as bootstrap_hcp_org.py's
    check_no_state_in_git, re-asserted here as this task's own, standalone,
    definition-of-done-provable half of "remote state only"."""
    return [
        line
        for line in list_tracked_files(repo_root)
        if line and (".tfstate" in Path(line).name or ".terraform" in Path(line).parts)
    ]


def assert_state_not_in_git(repo_root: Path = REPO_ROOT) -> tuple[bool, str]:
    try:
        hits = find_tracked_tfstate_paths(repo_root)
    except RuntimeError as exc:
        return False, str(exc)
    if hits:
        return False, f"tracked tfstate-shaped path(s): {', '.join(hits)}"
    return True, "no tfstate-shaped path is tracked in git"


# ---------------------------------------------------------------------------
# Orchestration -- network + subprocess, not unit tested (mirrors
# bootstrap_hcp_org.py's own main(), which is likewise proven live rather
# than unit tested).
# ---------------------------------------------------------------------------


def run_bootstrap_wiring_attempt() -> tuple[bool, str]:
    """Re-run T01's idempotent bootstrap script so a since-completed VCS
    OAuth connection gets attached to both workspaces on this pass, without
    duplicating its POST/PATCH logic here."""
    if not BOOTSTRAP_SCRIPT.exists():
        return False, f"{BOOTSTRAP_SCRIPT} not found"
    proc = subprocess.run(
        [sys.executable, str(BOOTSTRAP_SCRIPT)],
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        timeout=60,
    )
    detail = (proc.stdout + proc.stderr).strip()
    return proc.returncode == 0, detail


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="skip re-running bootstrap_hcp_org.py; only GET the live workspaces and assert",
    )
    args = parser.parse_args()

    token = _token()
    if not token:
        print(
            f"[FAIL] no HCP Terraform API token found in ${TOKEN_ENV_PRIMARY} or ${TOKEN_ENV_ALIAS}",
            file=sys.stderr,
        )
        return 1

    org = _org()
    identifier = _github_identifier()
    print(f"[hcp_vcs_wiring] org={org} address={_address()} check_only={args.check_only}")

    if not args.check_only:
        wire_ok, wire_detail = run_bootstrap_wiring_attempt()
        print(f"[{'PASS' if wire_ok else 'FAIL'}] wiring attempt (bootstrap_hcp_org.py re-run):")
        for line in wire_detail.splitlines():
            print(f"    {line}")
        if not wire_ok:
            print("[hcp_vcs_wiring] FAIL: see above", file=sys.stderr)
            return 1

    all_ok = True
    for name in WORKSPACE_NAMES:
        ok, attrs = fetch_workspace_attributes(token, org, name)
        if not ok:
            print(f"[FAIL] {name}: {attrs}")
            all_ok = False
            continue
        assert isinstance(attrs, dict)
        ws_ok, lines = evaluate_workspace(name, attrs, identifier)
        for line in lines:
            print(line)
        all_ok = all_ok and ws_ok

    git_ok, git_detail = assert_state_not_in_git()
    print(f"[{'PASS' if git_ok else 'FAIL'}] state not in git: {git_detail}")
    all_ok = all_ok and git_ok

    if all_ok:
        print(
            "[hcp_vcs_wiring] PASS: raffa-dev + raffa-demo are remote-state-only "
            "(remote execution mode, no tracked tfstate); VCS wiring is wired or honestly pending"
        )
        return 0
    print("[hcp_vcs_wiring] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
