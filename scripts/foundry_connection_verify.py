#!/usr/bin/env python3
"""Structural verification for Foundry connection readiness (task
E01/F02/US05/T02, foundry-connection-verify).

Parent story `us-05-foundry-account` AC-1..AC-4 (one hub; two projects
`contigo-dev`/`contigo-demo`; one pay-as-you-go AI services account;
per-project Document Intelligence connection). Task E01/F02/US05/T01
(already merged into this branch) recorded that shape as
FOUNDRY_HUB_NAME/AI_SERVICES_ACCOUNT_NAME/FOUNDRY_PROJECTS in
scripts/bootstrap_hcp_org.py and structurally asserted it is complete and
internally consistent (check_foundry_account_recorded). This task's own
coding objective ("Verify Foundry hub/projects availability in
northeurope; record connection ids") is narrower and additive -- never a
re-decision of what T01 already owns:

  1. Region -- ADR-006 pins both `dev` and `demo` to the same region
     ("North Europe" / `northeurope`), and ADR-008's own Assumptions section
     names that as the region Foundry + Document Intelligence must be
     available in. check_region_pinned_to_westeurope() re-asserts (not
     just assumes) that both environment roots are still pinned there --
     via scripts/terraform_env_roots_scan.check_location_pin(), the same
     check task E01/F02/US01/T02 already wrote -- and that the canonical
     Foundry region slug this task records (`northeurope`) is the same
     region. One region, one source of truth (infra/environments/*/
     variables.tf), never a second literal that could silently drift.
  2. Connection ids -- T01 recorded a Document Intelligence connection
     *name* per project (e.g. `conn-docint-contigo-dev`) but never a
     structured connection *id* tying hub/account/project/connection name
     together. build_foundry_connections() derives one deterministically
     from T01's own recorded constants (never a second, hand-typed copy),
     and the check_connection_* functions below prove each is
     well-formed, unique per project/environment, and still resolves to
     the single ADR-008 AI services account -- no second account, no
     cross-environment collision.
  3. Identity material -- ADR-008: "The AI Gateway reads Foundry endpoint
     + credentials via managed identity/Key Vault; no model key in
     Terraform source or app code." The managed identity that later
     authenticates each connection above is
     infra/modules/identity/outputs.tf's `workload_identity_id` output
     (tasks E01/F02/US04/T02 and E01/F02/US05/T01).
     check_workload_identity_output_well_formed() proves that output
     exists exactly once and resolves to the real resource attribute.
     This is deliberately stricter than the sibling scans: both
     keyvault_scope_grants_check.py and entra_keyvault_provision_scan.py
     collect outputs into a {name: value} dict, so a *duplicate* block
     name is invisible to a plain membership check (the last match wins
     silently). This script counts block *headers* via regex first, so a
     repeat of the exact drift this task found on this branch --
     modules/identity/outputs.tf briefly held two textually-overlapping
     `output "workload_identity_id"` blocks after a phase-barrier merge
     between this task and E01/F02/US04/T02 -- fails loudly instead of
     passing by accident.

Like every other Foundry/HCP check in this repo, none of this is a live
Azure or HCP Terraform API call: Azure AI Foundry hub/project/connection
creation is an interactive Azure Portal step (ADR-008), and this harness
has no Azure subscription credential in scope. "Availability" here means
what scripts/bootstrap_hcp_org.py's own Foundry section already means --
the recorded shape is complete, internally consistent, and pinned to the
same region as the rest of the infrastructure -- not a live capacity
check against Azure.

Task E10/F02/US01/T01 (foundry-ocr-ca) extends this same script rather
than adding a sibling one -- its own task text names this file directly
("Identity module already documents Foundry connection verify... Prefer
completing that path"). Before that task, everything above proved the
*recorded shape* (T01/T02's structural constants); nothing proved the
Terraform layer actually consumed it. Four more checks close that gap:
`infra/modules/foundry` exists and both derives its
`local.ai_services_account_name` from the same literal as this file's own
AI_SERVICES_ACCOUNT_NAME, and gates its `azurerm_role_assignment` on
`var.ai_services_resource_id` rather than an unconditional resource that
would fail every apply against an account the ADR-008 Portal step has not
created yet; `infra/modules/containerapps` wires the three non-secret
AiGateway__* env vars (AC-3) onto both the api and worker Container Apps;
and both `infra/environments/{dev,demo}` instantiate `module.foundry` and
declare the `foundry_ai_services_resource_id` variable an operator sets
once that Portal step is done.

Checks, all read-only, no network, no `terraform` binary, no Azure/HCP
credentials required.

Usage:
    python scripts/foundry_connection_verify.py

Exit 0 if every check passes. Non-zero otherwise, with a PASS/FAIL line
per check on stdout (failures also echoed to stderr).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

SCRIPTS_ROOT = Path(__file__).resolve().parent
if str(SCRIPTS_ROOT) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_ROOT))

from terraform_env_roots_scan import _extract_block, _strip_line_comments  # noqa: E402
import terraform_env_roots_scan as tfr  # noqa: E402
import bootstrap_hcp_org as hcp  # noqa: E402
import repo_secret_scan as secret_scan  # noqa: E402

REPO_ROOT = SCRIPTS_ROOT.parent
INFRA_ROOT = REPO_ROOT / "infra"
IDENTITY_DIR = INFRA_ROOT / "modules" / "identity"
ENVIRONMENTS_ROOT = INFRA_ROOT / "environments"

ENVS = ("dev", "demo")

# ADR-006: both environments share one region; ADR-008's Assumptions
# section names it as the region Foundry + AI services + Document
# Intelligence must be confirmed available in. "northeurope" is the
# canonical Azure region slug for the Terraform-pinned "North Europe"
# (infra/environments/{dev,demo}/variables.tf) -- see _normalize_region
# below for why both spellings must compare equal.
FOUNDRY_REGION = "northeurope"

# ADR-017 AC-4: the exact two Document Intelligence prebuilt models this
# story's per-project connections must serve. Named literally here
# because T01's own recorded shape (FOUNDRY_PROJECTS in
# bootstrap_hcp_org.py) only carries the connection *name*, never the
# model ids the connection is for.
DOCUMENT_INTELLIGENCE_MODELS = ("prebuilt-read", "prebuilt-layout")

FILES_TO_SECRET_SCAN_RELATIVE = ("modules/identity/outputs.tf",)

# Task E10/F02/US01/T01 additions below -- see the module docstring's own
# paragraph naming this task.
MODULES_ROOT = INFRA_ROOT / "modules"
CONTAINERAPPS_MAIN_TF = MODULES_ROOT / "containerapps" / "main.tf"
FOUNDRY_MAIN_TF = MODULES_ROOT / "foundry" / "main.tf"

# The three non-secret env vars this task adds to both the api and worker
# Container Apps, and the module variable each must read (never a
# secret_name -- ADR-011: an endpoint URL and two names are not secrets).
REQUIRED_AI_GATEWAY_ENV_VARS = {
    "AiGateway__Endpoint": "var.ai_gateway_endpoint",
    "AiGateway__ProjectName": "var.ai_gateway_project_name",
    "AiGateway__DocumentIntelligenceConnection": "var.ai_gateway_document_intelligence_connection",
}

# infra/environments/{dev,demo}/variables.tf's own operator-facing hook for
# the ADR-008 Portal step's resulting resource id (set as an HCP Terraform
# workspace variable of the same name).
FOUNDRY_ENV_ROOT_VARIABLE = "foundry_ai_services_resource_id"


def _normalize_region(value: str) -> str:
    """"North Europe" and "northeurope" must compare equal: Terraform's
    azurerm provider accepts the human-readable display name, while
    Foundry/ARM region slugs are lowercase-no-space. Same region, two
    spellings -- this is the one place that equivalence is asserted."""
    return re.sub(r"\s+", "", value).lower()


# ---------------------------------------------------------------------------
# Derive connection ids from T01's own recorded shape -- never a second,
# hand-typed source of truth for hub/account/project/connection-name.
# ---------------------------------------------------------------------------

def build_foundry_connections(projects=None) -> tuple[dict, ...]:
    projects = projects if projects is not None else hcp.FOUNDRY_PROJECTS
    return tuple(
        {
            "project": p["project"],
            "env": p["env"],
            "document_intelligence_connection": p["document_intelligence_connection"],
            "connection_id": (
                f"{hcp.AI_SERVICES_ACCOUNT_NAME}/projects/{p['project']}"
                f"/connections/{p['document_intelligence_connection']}"
            ),
            "region": FOUNDRY_REGION,
        }
        for p in projects
    )


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring the sibling scan
# scripts (scripts/keyvault_scope_grants_check.py and friends). `projects`
# lets tests exercise a synthetic FOUNDRY_PROJECTS-shaped fixture without
# touching bootstrap_hcp_org's real module constant.
# ---------------------------------------------------------------------------

def check_foundry_account_shape_still_recorded() -> tuple:
    """Re-runs task T01's own check as part of *this* task's proof: this
    task's connection ids are only meaningful if T01's hub/project/account
    shape is still complete and consistent -- never assumed just because a
    different task's test file currently passes."""
    return hcp.check_foundry_account_recorded()


def check_region_pinned_to_westeurope(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    if _normalize_region(FOUNDRY_REGION) != "northeurope":
        return False, f"FOUNDRY_REGION={FOUNDRY_REGION!r} does not normalize to 'northeurope'"
    problems = []
    for env in ENVS:
        passed, detail = tfr.check_location_pin(env, environments_root)
        if not passed:
            problems.append(detail)
            continue
        if _normalize_region(tfr.EXPECTED_LOCATION_DEFAULT) != _normalize_region(FOUNDRY_REGION):
            problems.append(
                f"{env}: infra location default {tfr.EXPECTED_LOCATION_DEFAULT!r} does not "
                f"match Foundry region {FOUNDRY_REGION!r}"
            )
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"dev and demo are both pinned to {tfr.EXPECTED_LOCATION_DEFAULT!r}, the same region "
        f"this task records for Foundry ({FOUNDRY_REGION!r}, ADR-006/ADR-008)"
    )


def check_connection_ids_well_formed(projects=None) -> tuple:
    connections = build_foundry_connections(projects)
    problems = []
    for c in connections:
        cid = c["connection_id"]
        if not cid.strip():
            problems.append(f"{c['project']}: empty connection_id")
            continue
        if not c["document_intelligence_connection"].strip():
            problems.append(f"{c['project']}: empty document_intelligence_connection name")
            continue
        if hcp.AI_SERVICES_ACCOUNT_NAME not in cid:
            problems.append(
                f"{c['project']}: connection_id {cid!r} does not reference "
                f"{hcp.AI_SERVICES_ACCOUNT_NAME!r}"
            )
        if c["project"] not in cid:
            problems.append(f"{c['project']}: connection_id {cid!r} does not reference its own project name")
        if c["document_intelligence_connection"] not in cid:
            problems.append(
                f"{c['project']}: connection_id {cid!r} does not reference "
                f"{c['document_intelligence_connection']!r}"
            )
    if problems:
        return False, "; ".join(problems)
    return True, f"{len(connections)} connection id(s) well-formed: {[c['connection_id'] for c in connections]}"


def check_connection_ids_unique_and_isolated(projects=None) -> tuple:
    connections = build_foundry_connections(projects)
    ids = [c["connection_id"] for c in connections]
    if len(set(ids)) != len(ids):
        return False, f"connection ids are not all unique: {ids}"
    envs = [c["env"] for c in connections]
    if len(set(envs)) != len(envs):
        return False, f"connection envs are not all unique: {envs}"
    return True, f"{len(connections)} connection id(s) unique, one per environment ({sorted(envs)})"


def check_connections_share_single_account(projects=None) -> tuple:
    connections = build_foundry_connections(projects)
    accounts = {c["connection_id"].split("/projects/")[0] for c in connections}
    if accounts != {hcp.AI_SERVICES_ACCOUNT_NAME}:
        return False, (
            f"connection ids reference account(s) {sorted(accounts)}, expected only "
            f"{hcp.AI_SERVICES_ACCOUNT_NAME!r} (ADR-008: never a second account)"
        )
    return True, f"all connections resolve to the single ADR-008 AI services account {hcp.AI_SERVICES_ACCOUNT_NAME!r}"


def check_document_intelligence_models_recorded() -> tuple:
    expected = {"prebuilt-read", "prebuilt-layout"}
    recorded = set(DOCUMENT_INTELLIGENCE_MODELS)
    if recorded != expected:
        return False, f"DOCUMENT_INTELLIGENCE_MODELS={sorted(recorded)}, expected {sorted(expected)} (ADR-017 AC-4)"
    return True, f"Document Intelligence models recorded: {sorted(recorded)} (ADR-017 AC-4)"


def check_workload_identity_output_well_formed(identity_dir: Path = IDENTITY_DIR) -> tuple:
    """ADR-008/ADR-011: the connection material each Foundry connection
    above will authenticate with is this environment's own
    workload_identity_id Terraform output. Counts block *headers*, not
    dict keys, so a duplicate/malformed second `output
    "workload_identity_id"` block -- invisible to a plain {name: value}
    membership check -- is still caught here (see module docstring)."""
    path = identity_dir / "outputs.tf"
    if not path.is_file():
        return False, "modules/identity/outputs.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    headers = list(re.finditer(r'output\s+"workload_identity_id"\s*{', text))
    if not headers:
        return False, 'modules/identity/outputs.tf has no output "workload_identity_id"'
    if len(headers) > 1:
        return False, (
            f'modules/identity/outputs.tf declares output "workload_identity_id" '
            f"{len(headers)} times, expected exactly 1"
        )
    body = _extract_block(text, headers[0].end() - 1)
    value_m = re.search(r"value\s*=\s*(\S+)", body)
    value = value_m.group(1) if value_m else None
    if value != "azurerm_user_assigned_identity.workload.id":
        return False, (
            f'output "workload_identity_id" value={value!r}, expected '
            "'azurerm_user_assigned_identity.workload.id'"
        )
    return True, (
        'modules/identity/outputs.tf declares exactly one output "workload_identity_id" = '
        "azurerm_user_assigned_identity.workload.id"
    )


# ---------------------------------------------------------------------------
# Task E10/F02/US01/T01: prove the Terraform *layer* consumes the recorded
# shape above -- the four checks the module docstring names. Small
# brace-balanced HCL helpers, same technique as
# scripts/terraform_env_roots_scan.py / scripts/demo_isolation_scan.py;
# `_find_all_blocks` is the one addition neither sibling script needed --
# `azurerm_container_app.api`/`.worker` each nest *several* same-named
# `env { ... }` blocks (one per env var), and `_find_block` (imported
# above) only ever returns the first match.
# ---------------------------------------------------------------------------

def _find_all_blocks(text: str, header_pattern: str) -> list:
    """Return the balanced inner text of every `<header_pattern> {...}` block,
    in source order (as opposed to `_find_block`, which returns only the
    first)."""
    return [_extract_block(text, m.end() - 1) for m in re.finditer(header_pattern + r"\s*{", text)]


def _env_vars_in_resource(main_tf_text: str, resource_type: str, resource_name: str) -> dict:
    """{env_var_name: {"value": rhs_or_None, "secret_name": rhs_or_None}} for
    every `env { name = "X" ... }` block anywhere inside one named resource
    (i.e. across every `container { }` block that resource's own `template`
    declares)."""
    text = _strip_line_comments(main_tf_text)
    resource_body = tfr._find_block(
        text, rf'resource\s+"{re.escape(resource_type)}"\s+"{re.escape(resource_name)}"'
    )
    if resource_body is None:
        return {}
    result: dict = {}
    for env_body in _find_all_blocks(resource_body, r"env"):
        name_m = re.search(r'name\s*=\s*"([^"]+)"', env_body)
        if not name_m:
            continue
        value_m = re.search(r"value\s*=\s*(\S+)", env_body)
        secret_m = re.search(r"secret_name\s*=\s*(\S+)", env_body)
        result[name_m.group(1)] = {
            "value": value_m.group(1) if value_m else None,
            "secret_name": secret_m.group(1) if secret_m else None,
        }
    return result


def check_containerapps_ai_gateway_env_wired(path: Path = CONTAINERAPPS_MAIN_TF) -> tuple:
    """AC-2/AC-3: modules/containerapps' api and worker Container Apps both
    carry the three AiGateway__* env vars this task adds, each wired to its
    matching module variable as a plain `value` -- never `secret_name`
    (ADR-011: an endpoint URL and two names are not secrets, unlike the
    ConnectionStrings__* env vars alongside them)."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = path.read_text(encoding="utf-8")
    problems = []
    for resource_name in ("api", "worker"):
        env_vars = _env_vars_in_resource(text, "azurerm_container_app", resource_name)
        for var_name, expected_value in REQUIRED_AI_GATEWAY_ENV_VARS.items():
            entry = env_vars.get(var_name)
            if entry is None:
                problems.append(f'{resource_name}: missing env "{var_name}"')
                continue
            if entry["secret_name"] is not None:
                problems.append(
                    f'{resource_name}: env "{var_name}" uses secret_name={entry["secret_name"]!r} '
                    "(expected a plain value -- it is not a secret)"
                )
            if entry["value"] != expected_value:
                problems.append(
                    f'{resource_name}: env "{var_name}" value={entry["value"]!r}, expected {expected_value!r}'
                )
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"api and worker both carry all {len(REQUIRED_AI_GATEWAY_ENV_VARS)} AiGateway__* env var(s) as a "
        "plain (non-secret) value, wired to their matching module variable"
    )


def check_foundry_role_assignment_conditional(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """ADR-008/ADR-011: modules/foundry grants the workload identity
    "Cognitive Services User" on the shared AI services account, gated on
    `var.ai_services_resource_id` -- never an unconditional resource, which
    would fail every apply against an account the ADR-008 Portal step has
    not created yet on either environment."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    body = tfr._find_block(text, r'resource\s+"azurerm_role_assignment"\s+"workload_ai_services_user"')
    if body is None:
        return False, 'modules/foundry/main.tf has no resource "azurerm_role_assignment" "workload_ai_services_user"'

    problems = []
    count_m = re.search(r"count\s*=\s*(.+)", body)
    count_expr = count_m.group(1).strip() if count_m else None
    if not count_expr or "ai_services_resource_id" not in count_expr:
        problems.append(f"count={count_expr!r} does not gate on var.ai_services_resource_id")

    role_m = re.search(r'role_definition_name\s*=\s*"([^"]+)"', body)
    role = role_m.group(1) if role_m else None
    if role != "Cognitive Services User":
        problems.append(f'role_definition_name={role!r}, expected "Cognitive Services User"')

    scope_m = re.search(r"scope\s*=\s*(\S+)", body)
    scope = scope_m.group(1) if scope_m else None
    if scope != "var.ai_services_resource_id":
        problems.append(f"scope={scope!r}, expected 'var.ai_services_resource_id'")

    principal_m = re.search(r"principal_id\s*=\s*(\S+)", body)
    principal = principal_m.group(1) if principal_m else None
    if principal != "var.workload_principal_id":
        problems.append(f"principal_id={principal!r}, expected 'var.workload_principal_id'")

    if problems:
        return False, "; ".join(problems)
    return True, (
        'modules/foundry/main.tf grants "Cognitive Services User" on var.ai_services_resource_id to '
        "var.workload_principal_id, gated on var.ai_services_resource_id != \"\""
    )


def check_ai_services_account_name_matches_terraform(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """modules/foundry/main.tf's `local.ai_services_account_name` string
    literal MUST stay in lockstep with this file's own (T01-derived)
    AI_SERVICES_ACCOUNT_NAME -- two hand-typed copies of the same name is
    exactly the drift check_workload_identity_output_well_formed already
    guards against elsewhere in this file, for the identical reason
    (Terraform has no way to import a Python constant)."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    m = re.search(r"ai_services_account_name\s*=\s*\"([^\"]+)\"", text)
    if not m:
        return False, "modules/foundry/main.tf has no local.ai_services_account_name string literal"
    if m.group(1) != hcp.AI_SERVICES_ACCOUNT_NAME:
        return False, (
            f"modules/foundry local.ai_services_account_name={m.group(1)!r} != "
            f"scripts/bootstrap_hcp_org.py AI_SERVICES_ACCOUNT_NAME={hcp.AI_SERVICES_ACCOUNT_NAME!r}"
        )
    return True, f"modules/foundry local.ai_services_account_name matches AI_SERVICES_ACCOUNT_NAME={hcp.AI_SERVICES_ACCOUNT_NAME!r}"


def check_env_roots_wire_foundry_module(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    """AC-2: both dev and demo env roots instantiate `module.foundry` (from
    `../../modules/foundry`, the same convention every sibling module in
    this env root already uses -- scripts/terraform_env_roots_scan.py's own
    REQUIRED_MODULES membership check does not yet know this module's name,
    since AC-1 of that task's own story predates this one) and declare the
    FOUNDRY_ENV_ROOT_VARIABLE an operator sets once the ADR-008 Portal step
    is done."""
    problems = []
    for env in ENVS:
        main_path = environments_root / env / "main.tf"
        variables_path = environments_root / env / "variables.tf"
        if not main_path.is_file():
            problems.append(f"{env}/main.tf does not exist")
            continue
        blocks = tfr.find_module_blocks(main_path.read_text(encoding="utf-8"))
        source = blocks.get("foundry")
        if source != "../../modules/foundry":
            problems.append(f'{env}/main.tf module "foundry" source={source!r}, expected "../../modules/foundry"')
        if not variables_path.is_file():
            problems.append(f"{env}/variables.tf does not exist")
            continue
        var_text = _strip_line_comments(variables_path.read_text(encoding="utf-8"))
        if not re.search(rf'variable\s+"{FOUNDRY_ENV_ROOT_VARIABLE}"\s*{{', var_text):
            problems.append(f'{env}/variables.tf has no variable "{FOUNDRY_ENV_ROOT_VARIABLE}"')
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"dev and demo both wire module.foundry from ../../modules/foundry and declare "
        f"variable {FOUNDRY_ENV_ROOT_VARIABLE!r}"
    )


def check_no_secret_literals(repo_root: Path = REPO_ROOT, infra_root: Path = INFRA_ROOT) -> tuple:
    hits = []
    scanned = 0
    for rel in FILES_TO_SECRET_SCAN_RELATIVE:
        path = infra_root / rel
        if not path.is_file():
            return False, f"infra/{rel} does not exist"
        try:
            display = str(path.relative_to(repo_root)).replace("\\", "/")
        except ValueError:
            display = f"infra/{rel}"
        text = path.read_text(encoding="utf-8", errors="ignore")
        scanned += 1
        hits.extend(secret_scan.find_secret_matches(display, text))
    if hits:
        return False, "; ".join(hits)
    return True, f"{scanned} file(s) scanned (identity outputs), no secret-shaped strings found"


def run_all_checks(
    identity_dir: Path = IDENTITY_DIR,
    environments_root: Path = ENVIRONMENTS_ROOT,
    repo_root: Path = REPO_ROOT,
    infra_root: Path = INFRA_ROOT,
    projects=None,
    containerapps_main_tf: Path = CONTAINERAPPS_MAIN_TF,
    foundry_main_tf: Path = FOUNDRY_MAIN_TF,
) -> list:
    return [
        ("foundry account shape still recorded", check_foundry_account_shape_still_recorded()),
        ("region pinned to northeurope", check_region_pinned_to_westeurope(environments_root)),
        ("connection ids well-formed", check_connection_ids_well_formed(projects)),
        ("connection ids unique + isolated per env", check_connection_ids_unique_and_isolated(projects)),
        ("connections share the single ADR-008 account", check_connections_share_single_account(projects)),
        ("Document Intelligence models recorded", check_document_intelligence_models_recorded()),
        ("workload_identity_id output well-formed", check_workload_identity_output_well_formed(identity_dir)),
        # Task E10/F02/US01/T01: the Terraform layer actually consumes the
        # recorded shape above (see the module docstring's own paragraph).
        ("containerapps AI Gateway env vars wired", check_containerapps_ai_gateway_env_wired(containerapps_main_tf)),
        ("foundry role assignment conditional on account id", check_foundry_role_assignment_conditional(foundry_main_tf)),
        ("AI services account name matches Terraform", check_ai_services_account_name_matches_terraform(foundry_main_tf)),
        ("dev/demo env roots wire module.foundry", check_env_roots_wire_foundry_module(environments_root)),
        ("no secret literals", check_no_secret_literals(repo_root, infra_root)),
    ]


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    connections = build_foundry_connections()
    print(
        "[INFO] recorded Foundry connection ids: "
        + ", ".join(f"{c['project']}={c['connection_id']}" for c in connections)
    )

    if ok:
        print(
            "[foundry_connection_verify] PASS: Foundry hub/projects pinned to "
            f"{FOUNDRY_REGION} (ADR-006/ADR-008); connection ids recorded per project"
        )
        return 0
    print("[foundry_connection_verify] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
