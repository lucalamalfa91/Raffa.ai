#!/usr/bin/env python3
"""Structural verification for the Foundry / Azure AI Services wiring
(task E01/F02/US05/T02 foundry-connection-verify, extended by task
E10/F02/US01/T01 and by the 2026-09-09 ADR-004/ADR-008/ADR-017 amendments).

Parent story `us-05-foundry-account` AC-1..AC-4 (one shared pay-as-you-go
AI services account; two projects `contigo-dev`/`contigo-demo`; per-project
Document Intelligence connection). Task E01/F02/US05/T01 recorded that shape
as AI_SERVICES_ACCOUNT_NAME / AI_RESOURCE_GROUP_NAME / FOUNDRY_PROJECTS in
scripts/bootstrap_hcp_org.py and structurally asserted it is complete and
internally consistent (check_foundry_account_recorded). This script proves
the rest of the chain, never a re-decision of what T01 owns:

  1. Region -- ADR-006 pins both `dev` and `demo` to the same region
     ("North Europe" / `northeurope`), the region every model deployment
     SKU was verified in. check_region_pinned_to_westeurope() (legacy
     name) re-asserts both environment roots are still pinned there via
     scripts/terraform_env_roots_scan.check_location_pin().
  2. Connection ids -- build_foundry_connections() derives one
     deterministic id per project from T01's recorded constants and the
     check_connection_* functions prove each is well-formed, unique per
     environment, and resolves to the single ADR-008 account.
  3. Identity material -- ADR-011: the AI Gateway authenticates with the
     environment's own workload identity, never a key.
     check_workload_identity_output_well_formed() proves
     infra/modules/identity/outputs.tf exports it exactly once.
  4. The Terraform layer (ADR-008 amendment 2026-09-09: the account, the
     per-environment projects, the model deployments and the RBAC are
     Terraform-managed; there is no hub and no portal step):
       * infra/modules/foundry creates the shared resource group + account
         (kind AIServices, S0, custom subdomain, no local auth, project
         management on) in exactly ONE root and attaches by name in the
         other -- never `terraform_remote_state`, never a second account,
         never the hub-based `azurerm_ai_foundry`;
       * every root creates its own project, its own deployments (named
         `<model>-<env>`, `NoAutoUpgrade`) and its own two workload role
         assignments (Cognitive Services User for Document Intelligence,
         Cognitive Services OpenAI User for inference), all gated on the
         account being created or attached;
       * the endpoint and the AiGateway__Models__* map are published only
         behind `local.publish` (account enabled AND `publish_endpoint`),
         and the endpoint is read from the resource, never derived from a
         string -- commit a750746's invariant, kept structural;
       * infra/modules/containerapps carries the three static AiGateway__*
         env vars plus one `dynamic "env"` block over
         `var.ai_gateway_model_env` on BOTH the api and worker apps;
       * both env roots wire `module.foundry` and `module.containerapps`
         accordingly, declare `ai_gateway_wired`, bind exactly the four
         chat/embedding roles to declared deployments with allowed SKUs,
         and no longer declare the retired `foundry_ai_services_resource_id`
         escape hatch.

Like every other Foundry/HCP check in this repo, none of this is a live
Azure or HCP Terraform API call: the checks are read-only, no network, no
`terraform` binary, no Azure/HCP credentials required. Whether the
resources exist live is HCP Terraform's apply, not this script.

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

# ADR-017 AC-4: the two Document Intelligence prebuilt models the account
# serves. `prebuilt-read` is the one the ocr role binds (ADR-017 amendment
# 2026-09-09); `prebuilt-layout` stays available on the account.
DOCUMENT_INTELLIGENCE_MODELS = ("prebuilt-read", "prebuilt-layout")

FILES_TO_SECRET_SCAN_RELATIVE = (
    "modules/identity/outputs.tf",
    "modules/foundry/main.tf",
    "modules/foundry/variables.tf",
    "modules/foundry/outputs.tf",
    "environments/dev/variables.tf",
    "environments/demo/variables.tf",
)

MODULES_ROOT = INFRA_ROOT / "modules"
CONTAINERAPPS_MAIN_TF = MODULES_ROOT / "containerapps" / "main.tf"
CONTAINERAPPS_VARIABLES_TF = MODULES_ROOT / "containerapps" / "variables.tf"
FOUNDRY_MAIN_TF = MODULES_ROOT / "foundry" / "main.tf"
FOUNDRY_OUTPUTS_TF = MODULES_ROOT / "foundry" / "outputs.tf"

# The three non-secret env vars both the api and worker Container Apps
# carry statically, and the module variable each must read (never a
# secret_name -- ADR-011: an endpoint URL and two names are not secrets).
REQUIRED_AI_GATEWAY_ENV_VARS = {
    "AiGateway__Endpoint": "var.ai_gateway_endpoint",
    "AiGateway__ProjectName": "var.ai_gateway_project_name",
    "AiGateway__DocumentIntelligenceConnection": "var.ai_gateway_document_intelligence_connection",
}

# The per-role model map (modules/foundry `model_env` output) reaches both
# Container Apps through one `dynamic "env"` block over this variable.
MODEL_ENV_VARIABLE = "ai_gateway_model_env"

# infra/environments/{dev,demo}/variables.tf's two-phase wiring switch and
# demo's attach switch (ADR-008 amendment 2026-09-09).
AI_GATEWAY_WIRED_VARIABLE = "ai_gateway_wired"
DEMO_ATTACH_VARIABLE = "ai_account_attached"
RETIRED_ENV_ROOT_VARIABLE = "foundry_ai_services_resource_id"

# ADR-004 amendment 2026-09-09: the four chat/embedding roles every root
# must bind to a declared deployment; the fifth role (ocr) is fixed inside
# the module.
MODEL_ROLES = ("classify", "extract", "embed", "answer")
OCR_MODEL = ("prebuilt-read", "2024-11-30")
ALLOWED_DEPLOYMENT_SKUS = ("DataZoneStandard", "GlobalStandard")

WORKLOAD_ROLE_ASSIGNMENTS = {
    "workload_ai_services_user": "Cognitive Services User",
    "workload_ai_services_openai_user": "Cognitive Services OpenAI User",
}

# ADR-008 amendment 2026-09-09: account-native projects, one account.
FORBIDDEN_FOUNDRY_RESOURCE_TYPES = (
    "azurerm_ai_foundry",
    "azurerm_ai_services",
    "azurerm_cognitive_account_connection",
)

REQUIRED_MODEL_ENV_KEYS = tuple(
    f"AiGateway__Models__{role.title()}__{suffix}"
    for role in (*MODEL_ROLES, "ocr")
    for suffix in ("ModelId", "ModelVersion")
)


def _normalize_region(value: str) -> str:
    """"North Europe" and "northeurope" must compare equal: Terraform's
    azurerm provider accepts the human-readable display name, while
    Foundry/ARM region slugs are lowercase-no-space. Same region, two
    spellings -- this is the one place that equivalence is asserted."""
    return re.sub(r"\s+", "", value).lower()


# ---------------------------------------------------------------------------
# Derive connection ids from T01's own recorded shape -- never a second,
# hand-typed source of truth for account/project/connection-name.
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
    """Re-runs task T01's own check as part of this proof: the connection
    ids below are only meaningful if the recorded account/project shape is
    still complete and consistent."""
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
    if OCR_MODEL[0] not in recorded:
        return False, f"OCR_MODEL={OCR_MODEL!r} is not one of the recorded Document Intelligence models"
    return True, f"Document Intelligence models recorded: {sorted(recorded)} (ADR-017 AC-4); ocr role binds {OCR_MODEL[0]}"


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
# Small brace-balanced HCL helpers, same technique as
# scripts/terraform_env_roots_scan.py / scripts/demo_isolation_scan.py.
# `_find_all_blocks` is needed because `azurerm_container_app.api`/`.worker`
# each nest *several* same-named `env { ... }` blocks.
# ---------------------------------------------------------------------------

def _find_all_blocks(text: str, header_pattern: str) -> list:
    """Return the balanced inner text of every `<header_pattern> {...}` block,
    in source order."""
    return [_extract_block(text, m.end() - 1) for m in re.finditer(header_pattern + r"\s*{", text)]


def _block_after(text: str, header_pattern: str) -> str | None:
    """Balanced inner text of the first `<header_pattern> {...}` block, or None."""
    m = re.search(header_pattern + r"\s*{", text)
    return _extract_block(text, m.end() - 1) if m else None


def _attr(body: str, name: str) -> str | None:
    """RHS (whitespace-trimmed, single token) of `name = <rhs>` inside a block body."""
    m = re.search(rf"(?<![\w.]){re.escape(name)}\s*=\s*(\S+)", body)
    return m.group(1) if m else None


def _attr_line(body: str, name: str) -> str | None:
    """Whole RHS of `name = ...` up to end of line (for expressions with spaces)."""
    m = re.search(rf"(?<![\w.]){re.escape(name)}\s*=\s*(.+)", body)
    return m.group(1).strip() if m else None


def _quoted(body: str, name: str) -> str | None:
    m = re.search(rf'(?<![\w.]){re.escape(name)}\s*=\s*"([^"]*)"', body)
    return m.group(1) if m else None


def _read_stripped(path: Path) -> str:
    return _strip_line_comments(path.read_text(encoding="utf-8"))


def _env_vars_in_resource(main_tf_text: str, resource_type: str, resource_name: str) -> dict:
    """{env_var_name: {"value": rhs_or_None, "secret_name": rhs_or_None}} for
    every `env { name = "X" ... }` block anywhere inside one named resource
    (i.e. across every `container { }` block that resource's own `template`
    declares). A `dynamic "env" { ... }` block is not an `env {` header and
    is deliberately invisible here (see check_containerapps_model_env_dynamic_block)."""
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
    """modules/containerapps' api and worker Container Apps both carry the
    three static AiGateway__* env vars, each wired to its matching module
    variable as a plain `value` -- never `secret_name` (ADR-011: an endpoint
    URL and two names are not secrets, unlike the ConnectionStrings__* env
    vars alongside them)."""
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


def check_containerapps_model_env_dynamic_block(path: Path = CONTAINERAPPS_MAIN_TF) -> tuple:
    """ADR-004 amendment 2026-09-09: the per-role model map reaches BOTH
    Container Apps through exactly one `dynamic "env"` block over
    var.ai_gateway_model_env (name = env.key, value = env.value). An empty
    map emits nothing, so a fixture-gateway environment never carries a
    model id it does not call."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    problems = []
    for resource_name in ("api", "worker"):
        resource_body = tfr._find_block(text, rf'resource\s+"azurerm_container_app"\s+"{resource_name}"')
        if resource_body is None:
            problems.append(f'{resource_name}: no resource "azurerm_container_app" "{resource_name}"')
            continue
        blocks = _find_all_blocks(resource_body, r'dynamic\s+"env"')
        if len(blocks) != 1:
            problems.append(f'{resource_name}: expected exactly one dynamic "env" block, found {len(blocks)}')
            continue
        body = blocks[0]
        if _attr(body, "for_each") != f"var.{MODEL_ENV_VARIABLE}":
            problems.append(
                f'{resource_name}: dynamic "env" for_each={_attr(body, "for_each")!r}, '
                f"expected 'var.{MODEL_ENV_VARIABLE}'"
            )
        content = _block_after(body, r"content")
        if content is None:
            problems.append(f'{resource_name}: dynamic "env" has no content block')
            continue
        if _attr(content, "name") != "env.key" or _attr(content, "value") != "env.value":
            problems.append(
                f'{resource_name}: dynamic "env" content must set name = env.key and value = env.value '
                f"(found name={_attr(content, 'name')!r}, value={_attr(content, 'value')!r})"
            )
    if problems:
        return False, "; ".join(problems)
    return True, (
        f'api and worker each carry exactly one dynamic "env" block over var.{MODEL_ENV_VARIABLE} '
        "(name = env.key, value = env.value)"
    )


def check_containerapps_model_env_variable(path: Path = CONTAINERAPPS_VARIABLES_TF) -> tuple:
    """modules/containerapps declares var.ai_gateway_model_env as a required
    map(string) -- no default, so a root can never forget to pass the
    module.foundry map."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    body = _block_after(text, rf'variable\s+"{MODEL_ENV_VARIABLE}"')
    if body is None:
        return False, f'modules/containerapps/variables.tf has no variable "{MODEL_ENV_VARIABLE}"'
    problems = []
    type_value = _attr_line(body, "type")
    if type_value is None or re.sub(r"\s+", "", type_value) != "map(string)":
        problems.append(f"type={type_value!r}, expected 'map(string)'")
    if re.search(r"(?<![\w.])default\s*=", body):
        problems.append("declares a default (must be required so every root passes module.foundry.model_env)")
    if problems:
        return False, f'variable "{MODEL_ENV_VARIABLE}": ' + "; ".join(problems)
    return True, f'modules/containerapps declares variable "{MODEL_ENV_VARIABLE}" as a required map(string)'


def check_foundry_workload_role_assignments_gated(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """ADR-008/ADR-011: modules/foundry grants the workload identity BOTH
    data-plane roles (Cognitive Services User for Document Intelligence,
    Cognitive Services OpenAI User for inference) on the shared account,
    each gated on the account being created or attached
    (`local.account_enabled`) -- never an unconditional resource, which
    would fail every plan of a root that has not attached yet."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    problems = []
    for resource_name, expected_role in WORKLOAD_ROLE_ASSIGNMENTS.items():
        body = tfr._find_block(text, rf'resource\s+"azurerm_role_assignment"\s+"{resource_name}"')
        if body is None:
            problems.append(
                f'modules/foundry/main.tf has no resource "azurerm_role_assignment" "{resource_name}" '
                f'("{expected_role}")'
            )
            continue
        count_expr = _attr_line(body, "count")
        if not count_expr or "account_enabled" not in count_expr:
            problems.append(f"{resource_name}: count={count_expr!r} does not gate on local.account_enabled")
        role = _quoted(body, "role_definition_name")
        if role != expected_role:
            problems.append(f'{resource_name}: role_definition_name={role!r}, expected "{expected_role}"')
        scope = _attr(body, "scope")
        if scope != "local.ai_services_account_id":
            problems.append(f"{resource_name}: scope={scope!r}, expected 'local.ai_services_account_id'")
        principal = _attr(body, "principal_id")
        if principal != "var.workload_principal_id":
            problems.append(f"{resource_name}: principal_id={principal!r}, expected 'var.workload_principal_id'")
        if _attr(body, "skip_service_principal_aad_check") != "true":
            problems.append(f"{resource_name}: skip_service_principal_aad_check must be true (AAD replication lag)")
    if problems:
        return False, "; ".join(problems)
    return True, (
        "modules/foundry/main.tf grants "
        + " and ".join(f'"{r}"' for r in WORKLOAD_ROLE_ASSIGNMENTS.values())
        + " on local.ai_services_account_id to var.workload_principal_id, each gated on local.account_enabled (count)"
    )


def check_foundry_operator_role_assignments(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """Human operators (live probes, Foundry playground) get the same two
    roles through one for_each resource over local.operator_grants, which
    is derived from var.ai_operator_principal_ids and is empty while the
    account is disabled."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    body = tfr._find_block(text, r'resource\s+"azurerm_role_assignment"\s+"operator"')
    if body is None:
        return False, 'modules/foundry/main.tf has no resource "azurerm_role_assignment" "operator"'
    problems = []
    if _attr(body, "for_each") != "local.operator_grants":
        problems.append(f"operator: for_each={_attr(body, 'for_each')!r}, expected 'local.operator_grants'")
    if _attr(body, "role_definition_name") != "each.value.role":
        problems.append("operator: role_definition_name must be each.value.role")
    if _attr(body, "principal_id") != "each.value.principal_id":
        problems.append("operator: principal_id must be each.value.principal_id")
    if _attr(body, "scope") != "local.ai_services_account_id":
        problems.append(f"operator: scope={_attr(body, 'scope')!r}, expected 'local.ai_services_account_id'")
    grants = _block_after(text, r"operator_grants\s*=") or ""
    if "var.ai_operator_principal_ids" not in grants:
        problems.append("local.operator_grants is not derived from var.ai_operator_principal_ids")
    if "if local.account_enabled" not in grants:
        problems.append("local.operator_grants is not filtered by `if local.account_enabled`")
    if problems:
        return False, "; ".join(problems)
    return True, (
        'modules/foundry/main.tf grants the two data-plane roles to each var.ai_operator_principal_ids '
        "entry via for_each = local.operator_grants (empty while the account is disabled)"
    )


def check_ai_services_account_name_matches_terraform(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """modules/foundry/main.tf's `local.ai_services_account_name` and
    `local.ai_resource_group_name` string literals MUST stay in lockstep
    with scripts/bootstrap_hcp_org.py's AI_SERVICES_ACCOUNT_NAME /
    AI_RESOURCE_GROUP_NAME -- Terraform has no way to import a Python
    constant, so two hand-typed copies are asserted equal here."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    problems = []
    m = re.search(r"ai_services_account_name\s*=\s*\"([^\"]+)\"", text)
    if not m:
        problems.append("modules/foundry/main.tf has no local.ai_services_account_name string literal")
    elif m.group(1) != hcp.AI_SERVICES_ACCOUNT_NAME:
        problems.append(
            f"modules/foundry local.ai_services_account_name={m.group(1)!r} != "
            f"scripts/bootstrap_hcp_org.py AI_SERVICES_ACCOUNT_NAME={hcp.AI_SERVICES_ACCOUNT_NAME!r}"
        )
    rg = re.search(r"ai_resource_group_name\s*=\s*\"([^\"]+)\"", text)
    if not rg:
        problems.append("modules/foundry/main.tf has no local.ai_resource_group_name string literal")
    elif rg.group(1) != hcp.AI_RESOURCE_GROUP_NAME:
        problems.append(
            f"modules/foundry local.ai_resource_group_name={rg.group(1)!r} != "
            f"scripts/bootstrap_hcp_org.py AI_RESOURCE_GROUP_NAME={hcp.AI_RESOURCE_GROUP_NAME!r}"
        )
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"modules/foundry locals match AI_SERVICES_ACCOUNT_NAME={hcp.AI_SERVICES_ACCOUNT_NAME!r} and "
        f"AI_RESOURCE_GROUP_NAME={hcp.AI_RESOURCE_GROUP_NAME!r}"
    )


def check_foundry_account_resource_shape(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """ADR-008 amendment 2026-09-09: the shared account is created (in
    create mode only) as kind AIServices, S0, custom subdomain = account
    name, no local auth (ADR-011), project management on, system identity,
    shared tags; attached by name in the other mode; never through the
    hub-based azurerm_ai_foundry, the deprecated azurerm_ai_services, or a
    connection resource."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    problems = []

    for forbidden in FORBIDDEN_FOUNDRY_RESOURCE_TYPES:
        if re.search(rf'(resource|data)\s+"{re.escape(forbidden)}', text):
            problems.append(f'forbidden resource type "{forbidden}" is used (ADR-008: account-native projects, one account)')

    rg = tfr._find_block(text, r'resource\s+"azurerm_resource_group"\s+"ai"')
    if rg is None:
        problems.append('no resource "azurerm_resource_group" "ai"')
    else:
        if "create_shared_account" not in (_attr_line(rg, "count") or ""):
            problems.append("azurerm_resource_group.ai count must gate on var.create_shared_account")
        if _attr(rg, "name") != "local.ai_resource_group_name":
            problems.append(f"azurerm_resource_group.ai name={_attr(rg, 'name')!r}, expected 'local.ai_resource_group_name'")
        if _attr(rg, "tags") != "local.tags_shared":
            problems.append("azurerm_resource_group.ai must be tagged local.tags_shared")

    acct = tfr._find_block(text, r'resource\s+"azurerm_cognitive_account"\s+"this"')
    if acct is None:
        problems.append('no resource "azurerm_cognitive_account" "this"')
    else:
        if "create_shared_account" not in (_attr_line(acct, "count") or ""):
            problems.append("azurerm_cognitive_account.this count must gate on var.create_shared_account")
        expectations = {
            "kind": '"AIServices"',
            "sku_name": '"S0"',
            "custom_subdomain_name": "local.ai_services_account_name",
            "local_auth_enabled": "false",
            "project_management_enabled": "true",
            "name": "local.ai_services_account_name",
            "tags": "local.tags_shared",
        }
        for key, expected in expectations.items():
            if _attr(acct, key) != expected:
                problems.append(f"azurerm_cognitive_account.this {key}={_attr(acct, key)!r}, expected {expected}")
        identity = _block_after(acct, r"identity")
        if identity is None or _quoted(identity, "type") != "SystemAssigned":
            problems.append("azurerm_cognitive_account.this must carry identity { type = \"SystemAssigned\" }")

    data = tfr._find_block(text, r'data\s+"azurerm_cognitive_account"\s+"this"')
    if data is None:
        problems.append('no data "azurerm_cognitive_account" "this" (attach mode)')
    else:
        if "attach_shared_account" not in (_attr_line(data, "count") or ""):
            problems.append("data.azurerm_cognitive_account.this count must gate on var.attach_shared_account")
        if _attr(data, "name") != "local.ai_services_account_name":
            problems.append("data.azurerm_cognitive_account.this name must be local.ai_services_account_name")
        if _attr(data, "resource_group_name") != "local.ai_resource_group_name":
            problems.append("data.azurerm_cognitive_account.this resource_group_name must be local.ai_resource_group_name")

    tags_shared = _block_after(text, r"tags_shared\s*=")
    if tags_shared is None or _quoted(tags_shared, "env") != "shared":
        problems.append('local.tags_shared must carry env = "shared"')

    if problems:
        return False, "; ".join(problems)
    return True, (
        "modules/foundry creates the shared resource group + AIServices S0 account (custom subdomain, "
        "no local auth, project management on, system identity, env=shared tags) in create mode, "
        "attaches by name otherwise, and uses no hub/connection/deprecated resource type"
    )


def check_foundry_project_and_deployments_shape(path: Path = FOUNDRY_MAIN_TF) -> tuple:
    """ADR-008/ADR-004 amendments 2026-09-09: one account-native project
    per environment and one deployment per bound model, named
    <model>-<env> (no cross-root contention on the shared account), pinned
    to an explicit version with NoAutoUpgrade (reproducible audit log);
    the ocr role is fixed to Document Intelligence prebuilt-read."""
    if not path.is_file():
        return False, f"{path} does not exist"
    text = _read_stripped(path)
    problems = []

    project = tfr._find_block(text, r'resource\s+"azurerm_cognitive_account_project"\s+"this"')
    if project is None:
        problems.append('no resource "azurerm_cognitive_account_project" "this"')
    else:
        if "account_enabled" not in (_attr_line(project, "count") or ""):
            problems.append("azurerm_cognitive_account_project.this count must gate on local.account_enabled")
        if _attr(project, "name") != "local.foundry_project_name":
            problems.append("azurerm_cognitive_account_project.this name must be local.foundry_project_name")
        if _attr(project, "cognitive_account_id") != "local.ai_services_account_id":
            problems.append("azurerm_cognitive_account_project.this cognitive_account_id must be local.ai_services_account_id")

    deployment = tfr._find_block(text, r'resource\s+"azurerm_cognitive_deployment"\s+"model"')
    if deployment is None:
        problems.append('no resource "azurerm_cognitive_deployment" "model"')
    else:
        if _quoted(deployment, "name") != "${each.key}-${var.environment}":
            problems.append(
                f"azurerm_cognitive_deployment.model name={_quoted(deployment, 'name')!r}, "
                "expected \"${each.key}-${var.environment}\" (per-environment deployment names)"
            )
        if _attr(deployment, "cognitive_account_id") != "local.ai_services_account_id":
            problems.append("azurerm_cognitive_deployment.model cognitive_account_id must be local.ai_services_account_id")
        if _quoted(deployment, "version_upgrade_option") != "NoAutoUpgrade":
            problems.append("azurerm_cognitive_deployment.model version_upgrade_option must be \"NoAutoUpgrade\"")
        model = _block_after(deployment, r"model")
        if model is None:
            problems.append("azurerm_cognitive_deployment.model has no model block")
        else:
            if _quoted(model, "format") != "OpenAI":
                problems.append('deployment model.format must be "OpenAI"')
            if _attr(model, "name") != "each.key":
                problems.append("deployment model.name must be each.key")
            if _attr(model, "version") != "each.value.model_version":
                problems.append("deployment model.version must be each.value.model_version (pinned)")

    ocr_id = re.search(r'ocr_model_id\s*=\s*"([^"]+)"', text)
    ocr_version = re.search(r'ocr_model_version\s*=\s*"([^"]+)"', text)
    if (ocr_id.group(1) if ocr_id else None, ocr_version.group(1) if ocr_version else None) != OCR_MODEL:
        problems.append(f"local.ocr_model_id/ocr_model_version must equal {OCR_MODEL!r}")

    if problems:
        return False, "; ".join(problems)
    return True, (
        "modules/foundry creates one account-native project per environment and per-environment "
        "deployments named ${each.key}-${var.environment}, pinned (NoAutoUpgrade); ocr role = "
        f"{OCR_MODEL[0]} {OCR_MODEL[1]}"
    )


def check_foundry_outputs_gated(main_path: Path = FOUNDRY_MAIN_TF, outputs_path: Path = FOUNDRY_OUTPUTS_TF) -> tuple:
    """Commit a750746's invariant, kept structural: the endpoint the
    Container Apps receive is "" unless the account is created/attached AND
    var.publish_endpoint is true, and it is read from the account resource
    or data source (try(...)), never derived from a string; the model map
    is published behind the same gate and covers all five roles."""
    problems = []
    if not main_path.is_file():
        return False, f"{main_path} does not exist"
    if not outputs_path.is_file():
        return False, f"{outputs_path} does not exist"
    main_text = _read_stripped(main_path)
    outputs_text = _read_stripped(outputs_path)

    endpoint_out = _block_after(outputs_text, r'output\s+"ai_services_endpoint"')
    expected_endpoint = 'local.publish ? local.ai_services_endpoint : ""'
    endpoint_value = _attr_line(endpoint_out, "value") if endpoint_out else None
    if endpoint_value is None or re.sub(r"\s+", " ", endpoint_value) != expected_endpoint:
        problems.append(f'output "ai_services_endpoint" value={endpoint_value!r}, expected {expected_endpoint!r}')

    model_env_out = _block_after(outputs_text, r'output\s+"model_env"')
    if model_env_out is None or _attr(model_env_out, "value") != "local.model_env":
        problems.append('output "model_env" must exist with value = local.model_env')

    publish = _attr_line(main_text, "publish")
    if publish is None or re.sub(r"\s+", "", publish) != "local.account_enabled&&var.publish_endpoint":
        problems.append(f"local.publish={publish!r}, expected 'local.account_enabled && var.publish_endpoint'")

    endpoint_local = re.search(r"ai_services_endpoint\s*=\s*try\((.*?)\)\n", main_text, re.DOTALL)
    endpoint_src = endpoint_local.group(1) if endpoint_local else ""
    if (
        "azurerm_cognitive_account.this[0].endpoint" not in endpoint_src
        or "data.azurerm_cognitive_account.this[0].endpoint" not in endpoint_src
        or not re.search(r'""\s*,?\s*$', endpoint_src.strip())
    ):
        problems.append(
            "local.ai_services_endpoint must be try(azurerm_cognitive_account.this[0].endpoint, "
            'data.azurerm_cognitive_account.this[0].endpoint, "") -- read from the resource, never derived'
        )

    model_env_m = re.search(r"model_env\s*=\s*merge\((.*?)\n\s*\)\s*\n", main_text, re.DOTALL)
    model_env_src = model_env_m.group(1) if model_env_m else ""
    if "AiGateway__Models__${title(role)}__ModelId" not in model_env_src:
        problems.append("local.model_env must build AiGateway__Models__${title(role)}__ModelId keys")
    if "AiGateway__Models__${title(role)}__ModelVersion" not in model_env_src:
        problems.append("local.model_env must build AiGateway__Models__${title(role)}__ModelVersion keys")
    if model_env_src.count("if local.publish") < 2:
        problems.append("every local.model_env comprehension must carry `if local.publish`")
    if not re.search(r"ocr\s*=\s*{\s*id\s*=\s*local\.ocr_model_id\s*,\s*version\s*=\s*local\.ocr_model_version", main_text):
        problems.append("local.role_model must include the ocr role (id = local.ocr_model_id, version = local.ocr_model_version)")

    if problems:
        return False, "; ".join(problems)
    return True, (
        'ai_services_endpoint and model_env are published only behind local.publish '
        "(account enabled && var.publish_endpoint); the endpoint is read from the account resource/data source; "
        f"model_env covers {len(REQUIRED_MODEL_ENV_KEYS)} AiGateway__Models__* keys"
    )


def _env_root_foundry_module_body(main_path: Path) -> str | None:
    return _block_after(_read_stripped(main_path), r'module\s+"foundry"')


def _env_root_containerapps_module_body(main_path: Path) -> str | None:
    return _block_after(_read_stripped(main_path), r'module\s+"containerapps"')


def check_env_roots_wire_foundry_module(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    """Both env roots instantiate module.foundry from ../../modules/foundry
    with their own identity/location/environment, gate publication on
    var.ai_gateway_wired, pass module.foundry.model_env and
    .ai_services_endpoint into module.containerapps, and no longer declare
    the retired foundry_ai_services_resource_id escape hatch."""
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
        foundry = _env_root_foundry_module_body(main_path)
        if foundry is not None:
            expectations = {
                "publish_endpoint": f"var.{AI_GATEWAY_WIRED_VARIABLE}",
                "workload_principal_id": "module.identity.workload_principal_id",
                "location": "var.location",
                "environment": "local.environment",
            }
            for key, expected in expectations.items():
                if _attr(foundry, key) != expected:
                    problems.append(f'{env}/main.tf module "foundry" {key}={_attr(foundry, key)!r}, expected {expected!r}')
        containerapps = _env_root_containerapps_module_body(main_path)
        if containerapps is None:
            problems.append(f'{env}/main.tf has no module "containerapps"')
        else:
            if _attr(containerapps, MODEL_ENV_VARIABLE) != "module.foundry.model_env":
                problems.append(f'{env}/main.tf module "containerapps" must pass {MODEL_ENV_VARIABLE} = module.foundry.model_env')
            if _attr(containerapps, "ai_gateway_endpoint") != "module.foundry.ai_services_endpoint":
                problems.append(f'{env}/main.tf module "containerapps" must pass ai_gateway_endpoint = module.foundry.ai_services_endpoint')
        if not variables_path.is_file():
            problems.append(f"{env}/variables.tf does not exist")
            continue
        var_text = _read_stripped(variables_path)
        wired = _block_after(var_text, rf'variable\s+"{AI_GATEWAY_WIRED_VARIABLE}"')
        if wired is None:
            problems.append(f'{env}/variables.tf has no variable "{AI_GATEWAY_WIRED_VARIABLE}"')
        elif _attr(wired, "type") != "bool":
            problems.append(f'{env}/variables.tf variable "{AI_GATEWAY_WIRED_VARIABLE}" must be type = bool')
        if re.search(rf'variable\s+"{RETIRED_ENV_ROOT_VARIABLE}"\s*{{', var_text):
            problems.append(
                f'{env}/variables.tf still declares the retired variable "{RETIRED_ENV_ROOT_VARIABLE}" '
                "(the account is Terraform-managed; ADR-008 amendment 2026-09-09)"
            )
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"dev and demo both wire module.foundry from ../../modules/foundry (publish_endpoint = "
        f"var.{AI_GATEWAY_WIRED_VARIABLE}, own identity), pass model_env/endpoint into module.containerapps, "
        f"and declare variable {AI_GATEWAY_WIRED_VARIABLE!r} (bool)"
    )


def check_single_shared_account_owner(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    """ADR-008: never a second account. Exactly one root (dev) sets
    create_shared_account = true; demo sets it false and attaches through
    var.ai_account_attached (declared as a bool in its variables.tf)."""
    problems = []
    creators = []
    for env in ENVS:
        main_path = environments_root / env / "main.tf"
        if not main_path.is_file():
            problems.append(f"{env}/main.tf does not exist")
            continue
        foundry = _env_root_foundry_module_body(main_path)
        if foundry is None:
            problems.append(f'{env}/main.tf has no module "foundry"')
            continue
        create = _attr(foundry, "create_shared_account")
        attach = _attr(foundry, "attach_shared_account")
        if create == "true":
            creators.append(env)
        elif create != "false":
            problems.append(f"{env}: create_shared_account={create!r}, expected a literal true/false")
        if env == "dev":
            if attach != "false":
                problems.append(f"dev: attach_shared_account={attach!r}, expected false (dev creates, never attaches)")
        else:
            if attach != f"var.{DEMO_ATTACH_VARIABLE}":
                problems.append(f"{env}: attach_shared_account={attach!r}, expected 'var.{DEMO_ATTACH_VARIABLE}'")
            variables_path = environments_root / env / "variables.tf"
            var_text = _read_stripped(variables_path) if variables_path.is_file() else ""
            attach_var = _block_after(var_text, rf'variable\s+"{DEMO_ATTACH_VARIABLE}"')
            if attach_var is None:
                problems.append(f'{env}/variables.tf has no variable "{DEMO_ATTACH_VARIABLE}"')
            elif _attr(attach_var, "type") != "bool":
                problems.append(f'{env}/variables.tf variable "{DEMO_ATTACH_VARIABLE}" must be type = bool')
    if creators != ["dev"]:
        problems.append(f"roots with create_shared_account = true: {creators}, expected exactly ['dev'] (ADR-008: one account)")
    if problems:
        return False, "; ".join(problems)
    return True, (
        f"exactly one root (dev) creates the shared account; demo attaches via var.{DEMO_ATTACH_VARIABLE} "
        "(ADR-008: never a second account)"
    )


def env_root_model_bindings(environments_root: Path = ENVIRONMENTS_ROOT) -> dict:
    """{env: {"roles": {role: model}, "deployments": {model: {"model_version", "sku_name", "capacity"}}}}
    parsed from each root's module "foundry" block."""
    result: dict = {}
    for env in ENVS:
        main_path = environments_root / env / "main.tf"
        if not main_path.is_file():
            continue
        foundry = _env_root_foundry_module_body(main_path)
        if foundry is None:
            continue
        roles_body = _block_after(foundry, r"model_roles\s*=") or ""
        roles = {m.group(1): m.group(2) for m in re.finditer(r'(\w+)\s*=\s*"([^"]+)"', roles_body)}
        deployments_body = _block_after(foundry, r"model_deployments\s*=") or ""
        deployments = {}
        for m in re.finditer(r'"([^"]+)"\s*=\s*{([^}]*)}', deployments_body):
            inner = m.group(2)
            deployments[m.group(1)] = {
                "model_version": _quoted(inner, "model_version"),
                "sku_name": _quoted(inner, "sku_name"),
                "capacity": _attr(inner, "capacity"),
            }
        result[env] = {"roles": roles, "deployments": deployments}
    return result


def check_env_roots_model_roles_complete(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    """ADR-004 amendment 2026-09-09: every root binds exactly the four
    chat/embedding roles, each to a deployment it declares, every
    deployment pinned to an explicit version on an allowed pay-per-token
    SKU (provisioned SKUs carry a fixed cost -- ADR-005)."""
    problems = []
    bindings = env_root_model_bindings(environments_root)
    for env in ENVS:
        if env not in bindings:
            problems.append(f'{env}: no module "foundry" block to parse')
            continue
        roles = bindings[env]["roles"]
        deployments = bindings[env]["deployments"]
        if set(roles) != set(MODEL_ROLES):
            problems.append(f"{env}: model_roles keys {sorted(roles)}, expected {sorted(MODEL_ROLES)}")
        if not deployments:
            problems.append(f"{env}: no model_deployments declared")
        for role, key in roles.items():
            if key not in deployments:
                problems.append(f"{env}: role {role!r} points at {key!r}, which is not a declared deployment")
        for key, d in deployments.items():
            if not d["model_version"]:
                problems.append(f"{env}: deployment {key!r} has no pinned model_version")
            if d["sku_name"] not in ALLOWED_DEPLOYMENT_SKUS:
                problems.append(
                    f"{env}: deployment {key!r} sku_name={d['sku_name']!r}, expected one of {list(ALLOWED_DEPLOYMENT_SKUS)}"
                )
            if d["capacity"] is None:
                problems.append(f"{env}: deployment {key!r} has no capacity")
    if problems:
        return False, "; ".join(problems)
    summary = "; ".join(
        f"{env}: " + ", ".join(f"{role}={key}-{env}" for role, key in sorted(b["roles"].items()))
        for env, b in bindings.items()
    )
    return True, f"every root binds exactly {list(MODEL_ROLES)} to pinned deployments on allowed SKUs ({summary})"


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
    return True, f"{scanned} file(s) scanned (identity outputs, foundry module, env root variables), no secret-shaped strings found"


def run_all_checks(
    identity_dir: Path = IDENTITY_DIR,
    environments_root: Path = ENVIRONMENTS_ROOT,
    repo_root: Path = REPO_ROOT,
    infra_root: Path = INFRA_ROOT,
    projects=None,
    containerapps_main_tf: Path = CONTAINERAPPS_MAIN_TF,
    containerapps_variables_tf: Path = CONTAINERAPPS_VARIABLES_TF,
    foundry_main_tf: Path = FOUNDRY_MAIN_TF,
    foundry_outputs_tf: Path = FOUNDRY_OUTPUTS_TF,
) -> list:
    return [
        ("foundry account shape still recorded", check_foundry_account_shape_still_recorded()),
        ("region pinned to northeurope", check_region_pinned_to_westeurope(environments_root)),
        ("connection ids well-formed", check_connection_ids_well_formed(projects)),
        ("connection ids unique + isolated per env", check_connection_ids_unique_and_isolated(projects)),
        ("connections share the single ADR-008 account", check_connections_share_single_account(projects)),
        ("Document Intelligence models recorded", check_document_intelligence_models_recorded()),
        ("workload_identity_id output well-formed", check_workload_identity_output_well_formed(identity_dir)),
        ("containerapps AI Gateway env vars wired", check_containerapps_ai_gateway_env_wired(containerapps_main_tf)),
        ("containerapps model env dynamic block", check_containerapps_model_env_dynamic_block(containerapps_main_tf)),
        ("containerapps model env variable", check_containerapps_model_env_variable(containerapps_variables_tf)),
        ("foundry workload role assignments gated", check_foundry_workload_role_assignments_gated(foundry_main_tf)),
        ("foundry operator role assignments", check_foundry_operator_role_assignments(foundry_main_tf)),
        ("AI services account name matches Terraform", check_ai_services_account_name_matches_terraform(foundry_main_tf)),
        ("foundry account resource shape", check_foundry_account_resource_shape(foundry_main_tf)),
        ("foundry project + deployments shape", check_foundry_project_and_deployments_shape(foundry_main_tf)),
        ("foundry outputs gated (a750746 invariant)", check_foundry_outputs_gated(foundry_main_tf, foundry_outputs_tf)),
        ("dev/demo env roots wire module.foundry", check_env_roots_wire_foundry_module(environments_root)),
        ("single shared account owner", check_single_shared_account_owner(environments_root)),
        ("env roots bind every model role", check_env_roots_model_roles_complete(environments_root)),
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
    for env, b in env_root_model_bindings().items():
        print(
            f"[INFO] {env} model deployments: "
            + ", ".join(f"{role}={key}-{env}" for role, key in sorted(b["roles"].items()))
            + f", ocr={OCR_MODEL[0]}"
        )

    if ok:
        print(
            "[foundry_connection_verify] PASS: Foundry account/projects/deployments Terraform-managed in "
            f"{FOUNDRY_REGION} (ADR-006/ADR-008); connection ids recorded per project"
        )
        return 0
    print("[foundry_connection_verify] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
