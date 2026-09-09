#!/usr/bin/env python3
"""Bootstrap the HCP Terraform organization and per-environment workspaces.

Task E01/F01/US02/T01 (parent story `us-02-hcp-terraform-workspaces`,
AC-1..AC-3; ADR-007 remote state per environment -- reusable modules + two
thin environment roots, remote state per env, no secrets in source; ADR-006
region `northeurope` is a data-plane concern only and never touches where
Terraform state lives). This script turns "an HCP Terraform org with two
independently-stated workspaces exists" into a reproducible, idempotent API
call instead of a one-off console click -- the same way
scripts/verify_github_repos.py and scripts/apply_github_branch_protection.py
did for the GitHub side in E01/F01/US01/T01.

What it does, in order:

  1. organization -- GET the org; if absent, POST to create it (needs an
     owner email, see CONTIGO_TFC_ORG_EMAIL below). AC-1.
  2. workspaces    -- GET, then POST or PATCH `contigo-dev` and
     `contigo-demo` so each converges to: execution-mode=remote (HCP
     Terraform itself runs plan/apply and owns state) and its own
     working-directory under infra/environments/{dev,demo} (ADR-007
     layout, created by a later feature-02 task). Every HCP Terraform
     workspace stores its state in HCP by construction -- there is no
     local-file or in-repo backend option -- so two separate workspaces
     *is* independent remote state per environment. AC-2. Each workspace
     also gets `project:contigo` / `env:{dev,demo}` organizational tags
     for filtering, applied via `POST .../relationships/tags` -- the
     `tag-names` convenience attribute on the workspace resource itself
     is accepted by this account's API without error but silently has no
     effect (verified live 2026-09-02: PATCH with tag-names echoed back
     `[]`); the relationship endpoint is the one that actually persists.
     Non-fatal if it fails: it is metadata, not one of AC-1..AC-3.
  3. VCS wiring    -- best-effort, never a hard gate. If the organization
     already has a VCS provider connected (an `oauth-client`, e.g. a
     linked GitHub App or legacy OAuth connection), this script attaches
     `vcs-repo` (owner/repo from CONTIGO_GITHUB_OWNER/CONTIGO_GITHUB_REPO,
     default lucalamalfa91/contigo) plus file-triggers-enabled=true and
     trigger-prefixes=["infra/"], so a change anywhere under infra/
     (shared modules or either environment root) triggers that
     workspace's plan/apply -- the parent task's stated goal. Verified
     live against this org on 2026-09-02
     (GET /organizations/contigo-platform/oauth-clients -> zero results):
     today there is no VCS provider connected, because connecting GitHub
     to HCP Terraform is an interactive, human-driven step (an OAuth
     authorize redirect, or installing HCP Terraform's GitHub App) that no
     API token can complete headlessly -- github.com has no
     `oauth-token-string`-style shortcut the way some self-hosted VCS
     providers do. Until that one-time connection exists, both workspaces
     are created/kept as API/CLI-driven (no vcs-repo) and this script says
     so plainly on every run instead of claiming a wiring it did not do.
     Re-running this script once the connection exists (task
     E01/F01/US02/T02 `hcp-vcs-wiring`, or an operator) wires vcs-repo on
     the next pass without recreating either workspace. Symmetrically,
     while no oauth-client is configured this script never sends a
     `vcs-repo` key at all (not even null), so it can never rip out a
     connection T02 or an operator already made by hand.
  4. no state in git -- `git ls-files` scanned for tfstate-shaped paths.
     Belt-and-braces local check that needs no API call at all: the root
     .gitignore from E01/F01/US01/T01 already excludes .terraform/,
     *.tfstate and *.tfstate.*, so this should always pass; it exists so a
     regression is caught here too, not only trusted to .gitignore. AC-3.
  5. Foundry account (ADR-008, ADR-017) -- task E01/F02/US05/T01, shape
     amended 2026-09-09. Records, as documentation plus a structural
     assertion (AI_RESOURCE_GROUP_NAME / AI_SERVICES_ACCOUNT_NAME /
     FOUNDRY_PROJECTS below), the one shared Azure AI Services account in
     its own resource group + two account-native Foundry projects
     (`contigo-dev`, `contigo-demo`) + a per-project Document Intelligence
     connection name. Since 2026-09-09 those resources are created by
     Terraform (infra/modules/foundry: the dev root owns the account, demo
     attaches to it) -- there is no hub and no portal step any more. This
     script still never calls Azure: the constants are the recorded shape
     scripts/foundry_connection_verify.py holds the Terraform module to
     (account name, resource group, projects, connection names).
     check_foundry_account_recorded() only proves the recorded shape is
     complete and internally consistent (one account, one resource group,
     exactly the two named projects, a Document Intelligence connection
     name per project).

Auth: `TFE_TOKEN` (the standard env var Terraform CLI and the `tfe`
provider read) or `HCP_TERRAFORM_TOKEN` as a Contigo-side alias. Address
defaults to https://app.terraform.io (`TFE_ADDRESS` / `HCP_TERRAFORM_ADDRESS`
to override, e.g. for Terraform Enterprise). Organization defaults to
`contigo-platform` (`CONTIGO_TFC_ORG` to override) -- `contigo` itself is
*not* the org slug: that name was already taken in app.terraform.io's
global namespace, so the org actually backing this product is
`contigo-platform` (confirmed live via GET /api/v2/organizations against
the configured token). Do not "fix" this back to a bare `contigo`; that
org is not the one wired to this account or token.

This script never handles a GitHub token or credential: VCS wiring only
reads whichever oauth-client HCP Terraform already has configured; it
never creates or stores one itself. It likewise never handles an Azure
subscription credential: the Foundry section below (point 5) is local,
recorded config -- not a call to any Azure API.

Usage:
    python scripts/bootstrap_hcp_org.py
    python scripts/bootstrap_hcp_org.py --check-only

--check-only makes no mutating API call (no organization or workspace
create/patch) and only reports current state -- useful for CI drift
checks. The Foundry check (point 5) runs regardless of --check-only: it
never makes a network call in the first place.

Exit 0 only if the organization exists, both workspaces exist (created or
already present), no tfstate-shaped path is tracked in git, and the
Foundry account's recorded shape (point 5) is complete and consistent.
"Not-yet-VCS-wired" is reported on stdout but is deliberately not a gate:
making it one would make this script permanently unable to succeed until a
human completes a step no script can perform (see point 3 above). The
Foundry resources' *existence* is not gated either (they are Terraform's,
applied through HCP) -- what this script can and does always assert is
that the recorded shape describing them is complete.
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

ORG_ENV = "CONTIGO_TFC_ORG"
DEFAULT_ORG = "contigo-platform"  # verified live 2026-09-02; "contigo" was unavailable
ORG_EMAIL_ENV = "CONTIGO_TFC_ORG_EMAIL"  # only needed if the org must be created fresh

GITHUB_OWNER_ENV = "CONTIGO_GITHUB_OWNER"
GITHUB_REPO_ENV = "CONTIGO_GITHUB_REPO"
DEFAULT_GITHUB_OWNER = "lucalamalfa91"
DEFAULT_GITHUB_REPO = "contigo"

REPO_ROOT = Path(__file__).resolve().parent.parent

# ADR-007 module layout: infra/environments/{dev,demo}. Names are locked by
# the task/ADR, not environment-configurable, the same way BRANCH="main" is
# a constant (not an env var) in apply_github_branch_protection.py.
WORKSPACES: tuple[dict, ...] = (
    {"name": "contigo-dev", "env": "dev", "working_directory": "infra/environments/dev"},
    {"name": "contigo-demo", "env": "demo", "working_directory": "infra/environments/demo"},
)

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


def _api(method: str, path: str, token: str, body: dict | None = None) -> tuple[int, dict]:
    url = f"{_address()}/api/v2{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        url,
        method=method,
        data=data,
        headers={**JSON_API_HEADERS, "Authorization": f"Bearer {token}"},
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


# ---------------------------------------------------------------------------
# Organization (AC-1)
# ---------------------------------------------------------------------------


def ensure_organization(token: str, org: str, check_only: bool) -> tuple[bool, str]:
    try:
        status, payload = _api("GET", f"/organizations/{org}", token)
    except ApiError as exc:
        return False, str(exc)

    if status == 200:
        attrs = payload.get("data", {}).get("attributes", {})
        return True, f"{org} exists (email={attrs.get('email')!r})"
    if status != 404:
        return False, f"GET /organizations/{org} failed ({status}): {_error_detail(payload)}"

    if check_only:
        return False, f"{org} does not exist (--check-only, not creating)"

    email = (os.environ.get(ORG_EMAIL_ENV) or "").strip()
    if not email:
        return False, (
            f"{org} does not exist and ${ORG_EMAIL_ENV} is not set -- cannot create an "
            "HCP Terraform organization without an owner email"
        )
    try:
        status, payload = _api(
            "POST",
            "/organizations",
            token,
            {"data": {"type": "organizations", "attributes": {"name": org, "email": email}}},
        )
    except ApiError as exc:
        return False, str(exc)
    if status not in (200, 201):
        return False, f"POST /organizations failed ({status}): {_error_detail(payload)}"
    return True, f"{org} created"


# ---------------------------------------------------------------------------
# Workspaces (AC-2) + best-effort VCS wiring
# ---------------------------------------------------------------------------


def get_oauth_token_id(token: str, org: str) -> str | None:
    try:
        status, payload = _api("GET", f"/organizations/{org}/oauth-clients", token)
    except ApiError:
        return None
    if status != 200:
        return None
    for client in payload.get("data", []):
        tokens = (client.get("relationships", {}).get("oauth-tokens") or {}).get("data") or []
        if tokens:
            return tokens[0]["id"]
    return None


def _workspace_tags(spec: dict) -> list[str]:
    return ["project:contigo", f"env:{spec['env']}"]


def _workspace_attributes(spec: dict, oauth_token_id: str | None) -> dict:
    attrs: dict = {
        "name": spec["name"],
        "execution-mode": "remote",
        "working-directory": spec["working_directory"],
        "auto-apply": False,
    }
    if oauth_token_id:
        attrs["vcs-repo"] = {
            "identifier": _github_identifier(),
            "oauth-token-id": oauth_token_id,
            "branch": "main",
        }
        attrs["file-triggers-enabled"] = True
        attrs["trigger-prefixes"] = ["infra/"]
    return attrs


def ensure_workspace(
    token: str, org: str, spec: dict, oauth_token_id: str | None, check_only: bool
) -> tuple[bool, str, bool, str | None]:
    """Returns (ok, detail, vcs_wired, workspace_id)."""
    name = spec["name"]
    try:
        status, payload = _api("GET", f"/organizations/{org}/workspaces/{name}", token)
    except ApiError as exc:
        return False, str(exc), False, None
    exists = status == 200
    if status not in (200, 404):
        return False, f"GET workspace {name} failed ({status}): {_error_detail(payload)}", False, None

    attrs = _workspace_attributes(spec, oauth_token_id)

    if check_only:
        if not exists:
            return False, f"{name} does not exist (--check-only, not creating)", False, None
        live_attrs = payload.get("data", {}).get("attributes", {})
        ws_id = payload["data"]["id"]
        return True, f"{name} exists (id={ws_id})", bool(live_attrs.get("vcs-repo")), ws_id

    try:
        if not exists:
            status, payload = _api(
                "POST",
                f"/organizations/{org}/workspaces",
                token,
                {"data": {"type": "workspaces", "attributes": attrs}},
            )
            if status not in (200, 201):
                return False, f"POST workspace {name} failed ({status}): {_error_detail(payload)}", False, None
            created_attrs = payload.get("data", {}).get("attributes", {})
            ws_id = payload["data"]["id"]
            return True, f"{name} created (id={ws_id})", bool(created_attrs.get("vcs-repo")), ws_id

        ws_id = payload["data"]["id"]
        status, payload = _api(
            "PATCH",
            f"/organizations/{org}/workspaces/{name}",
            token,
            {"data": {"type": "workspaces", "id": ws_id, "attributes": attrs}},
        )
        if status != 200:
            return False, f"PATCH workspace {name} failed ({status}): {_error_detail(payload)}", False, ws_id
        patched_attrs = payload.get("data", {}).get("attributes", {})
        return True, f"{name} up to date (id={ws_id})", bool(patched_attrs.get("vcs-repo")), ws_id
    except ApiError as exc:
        return False, str(exc), False, None


def apply_workspace_tags(token: str, ws_id: str, spec: dict) -> tuple[bool, str]:
    """Best-effort: bind project/env tags via the tags relationship endpoint.

    The plain `tag-names` attribute on the workspace resource is accepted
    by this account's API without error but verified live (2026-09-02) to
    have no effect; `POST .../relationships/tags` is the endpoint that
    actually persists tags, and re-posting an already-bound tag is a safe
    204 no-op (confirmed live), so this is safe to call on every run.
    """
    tags = _workspace_tags(spec)
    try:
        status, payload = _api(
            "POST",
            f"/workspaces/{ws_id}/relationships/tags",
            token,
            {"data": [{"type": "tags", "attributes": {"name": t}} for t in tags]},
        )
    except ApiError as exc:
        return False, str(exc)
    if status not in (204, 200, 201):
        return False, f"POST relationships/tags failed ({status}): {_error_detail(payload)}"
    return True, f"tags {tags} bound to {ws_id}"


# ---------------------------------------------------------------------------
# No state in git (AC-3) -- local, no API dependency
# ---------------------------------------------------------------------------


def check_no_state_in_git() -> tuple[bool, str]:
    proc = subprocess.run(
        ["git", "ls-files"], cwd=REPO_ROOT, text=True, capture_output=True, timeout=30
    )
    if proc.returncode != 0:
        return False, f"git ls-files failed (exit {proc.returncode}): {proc.stderr.strip()}"
    hits = [
        line
        for line in proc.stdout.splitlines()
        if line and (".tfstate" in Path(line).name or ".terraform" in Path(line).parts)
    ]
    if hits:
        return False, f"tracked tfstate-shaped path(s): {', '.join(hits)}"
    return True, "no tfstate-shaped path is tracked in git"


# ---------------------------------------------------------------------------
# Foundry account (ADR-008, ADR-017) -- the recorded shape Terraform
# (infra/modules/foundry) is held to by scripts/foundry_connection_verify.py
# ---------------------------------------------------------------------------

# ADR-008 (amended 2026-09-09): one shared pay-as-you-go Azure AI Services
# account (kind AIServices -- Azure OpenAI + Document Intelligence on one
# endpoint) in its own resource group, two account-native Foundry projects
# (`contigo-dev`, `contigo-demo`) -- never a second account/subscription,
# no hub. ADR-017 (amended the same day): Document Intelligence
# `prebuilt-read` is native to that *same* account; the per-project
# "connection" below is an informational value the backend sends as a
# request header, not a resource. Task E01/F02/US05/T01 recorded the
# original shape; the 2026-09-09 amendment made Terraform its owner: the
# dev root creates the resource group and the account, every root creates
# its own project, model deployments (named <model>-<env>) and role
# assignments, demo attaches to the account by name.
#
# This script still never calls Azure. These constants are the one
# recorded copy of the names infra/modules/foundry/main.tf's locals must
# match (scripts/foundry_connection_verify.py asserts both sides agree);
# the managed identity that authenticates the AI Gateway to the account
# (ADR-011) is infra/modules/identity's `workload_identity_id` /
# `workload_principal_id` outputs.
#
# AI_SERVICES_ACCOUNT_NAME deliberately carries no dev/demo suffix -- it
# is the one shared account ADR-008 requires; isolation between
# environments is by distinct Foundry project, per-environment deployment
# names and per-environment RBAC principals, never a second account.
AI_RESOURCE_GROUP_NAME = "rg-contigo-ai"
AI_SERVICES_ACCOUNT_NAME = "aisvc-contigo"

FOUNDRY_PROJECTS: tuple[dict, ...] = (
    {
        "project": "contigo-dev",
        "env": "dev",
        # ADR-017 AC-4: one Document Intelligence connection per project,
        # both pointing at the same AI_SERVICES_ACCOUNT_NAME account.
        "document_intelligence_connection": "conn-docint-contigo-dev",
    },
    {
        "project": "contigo-demo",
        "env": "demo",
        "document_intelligence_connection": "conn-docint-contigo-demo",
    },
)


def check_foundry_account_recorded() -> tuple[bool, str]:
    """Local, structural proof of the ADR-008/ADR-017 recorded shape.

    Not a live Azure API call -- see the module docstring point 5 and the
    comment above AI_RESOURCE_GROUP_NAME. This only asserts the constants
    above still describe exactly: one shared resource group, one shared AI
    services account, the two ADR-008 projects (`contigo-dev`,
    `contigo-demo`, no more, no fewer), and a non-empty Document
    Intelligence connection name recorded for each project -- i.e. that
    the recorded shape has not silently drifted (e.g. someone adding a
    second account, which ADR-008 forbids).
    """
    if not AI_RESOURCE_GROUP_NAME.strip():
        return False, "no shared AI resource group name recorded"
    if not AI_SERVICES_ACCOUNT_NAME.strip():
        return False, "no Foundry AI services account name recorded"

    expected_projects = {"contigo-dev", "contigo-demo"}
    recorded_projects = {p["project"] for p in FOUNDRY_PROJECTS}
    if recorded_projects != expected_projects:
        return (
            False,
            f"expected projects {sorted(expected_projects)}, recorded {sorted(recorded_projects)}",
        )

    missing_conn = [
        p["project"]
        for p in FOUNDRY_PROJECTS
        if not p.get("document_intelligence_connection", "").strip()
    ]
    if missing_conn:
        return False, f"no Document Intelligence connection recorded for: {', '.join(missing_conn)}"

    connections = [p["document_intelligence_connection"] for p in FOUNDRY_PROJECTS]
    return True, (
        f"rg={AI_RESOURCE_GROUP_NAME} account={AI_SERVICES_ACCOUNT_NAME} "
        f"projects={sorted(recorded_projects)} document_intelligence_connections={connections}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="report current state; make no mutating API call",
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
    print(f"[bootstrap_hcp_org] org={org} address={_address()} check_only={args.check_only}")

    ok_org, detail_org = ensure_organization(token, org, args.check_only)
    print(f"[{'PASS' if ok_org else 'FAIL'}] organization: {detail_org}")
    if not ok_org:
        print("[bootstrap_hcp_org] FAIL: see above", file=sys.stderr)
        return 1

    oauth_token_id = get_oauth_token_id(token, org)

    all_ws_ok = True
    ws_names: list[str] = []
    for spec in WORKSPACES:
        ok, detail, _wired, ws_id = ensure_workspace(token, org, spec, oauth_token_id, args.check_only)
        print(f"[{'PASS' if ok else 'FAIL'}] workspace {spec['name']}: {detail}")
        all_ws_ok = all_ws_ok and ok
        if ok:
            ws_names.append(spec["name"])
        if ok and ws_id and not args.check_only:
            tags_ok, tags_detail = apply_workspace_tags(token, ws_id, spec)
            print(f"[{'PASS' if tags_ok else 'WARN'}] workspace {spec['name']} tags: {tags_detail}")

    ok_git, detail_git = check_no_state_in_git()
    print(f"[{'PASS' if ok_git else 'FAIL'}] no state in git: {detail_git}")

    if oauth_token_id:
        print(f"[INFO] VCS: oauth-client present in {org}; vcs-repo wiring attempted for both workspaces")
    else:
        print(
            f"[INFO] VCS: no oauth-client configured in {org} yet -- connecting GitHub to HCP "
            "Terraform is an interactive, human-driven step (OAuth authorize or GitHub App "
            "install) that no API token can complete; both workspaces are API/CLI-driven for "
            "now. Re-run this script after the connection exists (task E01/F01/US02/T02) to "
            "wire vcs-repo without recreating either workspace."
        )

    ok_foundry, detail_foundry = check_foundry_account_recorded()
    print(
        f"[{'PASS' if ok_foundry else 'FAIL'}] foundry account "
        f"(recorded shape, ADR-008/ADR-017): {detail_foundry}"
    )
    print(
        "[INFO] Foundry: the shared AI services account, the per-environment projects, model "
        "deployments and role assignments are Terraform-managed since 2026-09-09 "
        "(infra/modules/foundry; the dev root owns the account, demo attaches to it) and are "
        "applied through HCP Terraform like everything else under infra/. The check above only "
        "proves the recorded shape is complete and internally consistent -- "
        "scripts/foundry_connection_verify.py holds the Terraform module to it. The workload "
        "managed identity that authenticates to the account (ADR-011) is "
        "infra/modules/identity's workload_identity_id/workload_principal_id outputs."
    )

    if not (ok_org and all_ws_ok and ok_git and ok_foundry):
        print("[bootstrap_hcp_org] FAIL: see above", file=sys.stderr)
        return 1

    print(
        f"[bootstrap_hcp_org] PASS: workspaces ready under {org}: {', '.join(ws_names)}; "
        f"foundry account recorded: {detail_foundry}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
