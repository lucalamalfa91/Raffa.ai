#!/usr/bin/env python3
"""Structural verification for the dev environment root's Terraform outputs
(task E01/F02/US02/T02, dev-outputs-verify).

Parent story `us-02-dev-environment` AC-1 ("dev provisions: ... Container
Apps Environment ... PostgreSQL Flexible Server ... Storage Account ...
Service Bus ... Key Vault ... Container Registry ... Log Analytics")
and AC-2 ("All dev resources tagged project=contigo, env=dev,
location=North Europe") need a repeatable proof that the *outputs* of
`infra/environments/dev/` actually surface a resource id/endpoint per
service, and that the per-service resources underneath are tagged. Task
T01 (E01/F02/US02/T01, already merged) provisioned the dev root but left
this exact gap as a recorded, uncommitted-to-later-task comment in
`infra/environments/dev/outputs.tf`: none of the nine modules under
`infra/modules/` declared an `output` block, so the root had nothing to
re-export beyond its own resource group. This task closes that gap.

Scope: this script owns the dev environment root's outputs *and* the
seven modules ADR-005's Concrete-services-and-SKUs table names a resource
id/endpoint for (Postgres FQDN, Container Apps API ingress FQDN, Storage
account name, Service Bus namespace FQDN, Key Vault URI, ACR login
server, Log Analytics workspace id). `network` and `identity` are not
named in that table and are left untouched, matching this task's own
"do not touch unrelated wave artifacts" instruction.

Checks, all read-only, no network, no `terraform` binary required:

  1. `infra/environments/dev/main.tf`'s `azurerm_resource_group.this` is
     tagged `project = "contigo"` / `env = local.environment` (AC-2 at
     the root).
  2. each of the seven modules' `outputs.tf` exists and declares exactly
     the resource id/endpoint outputs ADR-005 calls for, each with a
     `value` expression that references the real resource attribute
     (not a copy/paste stand-in).
  3. each of those same modules' `main.tf`: `locals.tags` is
     `project = "contigo"` / `env = var.environment`, and every
     taggable resource in that module (the ones with a `tags` argument
     in the Azure provider schema -- sub-resources such as
     `azurerm_storage_queue`/`azurerm_subnet` do not have one) is tagged
     `tags = local.tags` (AC-2 at the module layer).
  4. `infra/environments/dev/outputs.tf` declares all required outputs
     -- the four task-T01 outputs (regression safety), the `tags`
     output, and one root-level output per module output added above --
     each with the exact `module.<name>.<attr>` (or root resource
     attribute) value expression expected.

Usage:
    python scripts/dev_outputs_verify.py

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

from terraform_env_roots_scan import _extract_block, _find_block, _strip_line_comments  # noqa: E402

REPO_ROOT = SCRIPTS_ROOT.parent
INFRA_ROOT = REPO_ROOT / "infra"
DEV_ROOT = INFRA_ROOT / "environments" / "dev"
MODULES_ROOT = INFRA_ROOT / "modules"

# module -> {output_name: expected `value = ...` expression, exact text}
EXPECTED_MODULE_OUTPUTS = {
    "postgres": {
        "id": "azurerm_postgresql_flexible_server.this.id",
        "name": "azurerm_postgresql_flexible_server.this.name",
        "fqdn": "azurerm_postgresql_flexible_server.this.fqdn",
    },
    "containerapps": {
        "container_app_environment_id": "azurerm_container_app_environment.this.id",
        "api_id": "azurerm_container_app.api.id",
        "api_fqdn": "azurerm_container_app.api.latest_revision_fqdn",
        "worker_id": "azurerm_container_app.worker.id",
    },
    "storage": {
        "id": "azurerm_storage_account.this.id",
        "name": "azurerm_storage_account.this.name",
        "primary_blob_endpoint": "azurerm_storage_account.this.primary_blob_endpoint",
        "primary_queue_endpoint": "azurerm_storage_account.this.primary_queue_endpoint",
    },
    "servicebus": {
        "id": "azurerm_servicebus_namespace.this.id",
        "name": "azurerm_servicebus_namespace.this.name",
        "fqdn": '"${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"',
    },
    "keyvault": {
        "id": "azurerm_key_vault.this.id",
        "vault_uri": "azurerm_key_vault.this.vault_uri",
    },
    "acr": {
        "id": "azurerm_container_registry.this.id",
        "login_server": "azurerm_container_registry.this.login_server",
    },
    "monitor": {
        "id": "azurerm_log_analytics_workspace.this.id",
        "workspace_id": "azurerm_log_analytics_workspace.this.workspace_id",
    },
    "staticwebapp": {
        "id": "azurerm_static_web_app.this.id",
        "name": "azurerm_static_web_app.this.name",
        "default_host_name": "azurerm_static_web_app.this.default_host_name",
    },
}

# root output name -> expected `value = ...` expression in
# infra/environments/dev/outputs.tf, exact text.
EXPECTED_ROOT_OUTPUTS = {
    "resource_group_name": "azurerm_resource_group.this.name",
    "resource_group_id": "azurerm_resource_group.this.id",
    "location": "var.location",
    "environment": "local.environment",
    "tags": "azurerm_resource_group.this.tags",
    "postgres_id": "module.postgres.id",
    "postgres_fqdn": "module.postgres.fqdn",
    "container_app_environment_id": "module.containerapps.container_app_environment_id",
    "api_fqdn": "module.containerapps.api_fqdn",
    "worker_id": "module.containerapps.worker_id",
    "storage_account_name": "module.storage.name",
    "storage_primary_blob_endpoint": "module.storage.primary_blob_endpoint",
    "storage_primary_queue_endpoint": "module.storage.primary_queue_endpoint",
    "servicebus_namespace_fqdn": "module.servicebus.fqdn",
    "key_vault_uri": "module.keyvault.vault_uri",
    "acr_login_server": "module.acr.login_server",
    "log_analytics_workspace_id": "module.monitor.workspace_id",
    "static_web_app_name": "module.staticwebapp.name",
    "static_web_app_hostname": "module.staticwebapp.default_host_name",
    # ADR-008 amendment 2026-09-09: the shared AI services account this root
    # owns, dev's Foundry project and its model deployments.
    "ai_resource_group_name": "module.foundry.ai_resource_group_name",
    "ai_services_account_id": "module.foundry.ai_services_account_id",
    "ai_services_account_endpoint": "module.foundry.ai_services_account_endpoint",
    "ai_services_endpoint": "module.foundry.ai_services_endpoint",
    "foundry_project_id": "module.foundry.foundry_project_id",
    "ai_model_deployment_names": "module.foundry.model_deployment_names",
}

# module -> [(resource_type, resource_name), ...] this task must prove is
# tagged `tags = local.tags` -- only resources whose Azure provider schema
# actually has a `tags` argument (sub-resources like azurerm_storage_queue
# or azurerm_subnet do not, and are intentionally absent from this list).
TAGGED_RESOURCES_BY_MODULE = {
    "postgres": [("azurerm_postgresql_flexible_server", "this")],
    "containerapps": [
        ("azurerm_container_app_environment", "this"),
        ("azurerm_container_app", "api"),
        ("azurerm_container_app", "worker"),
    ],
    "storage": [("azurerm_storage_account", "this")],
    "servicebus": [("azurerm_servicebus_namespace", "this")],
    "keyvault": [("azurerm_key_vault", "this")],
    "acr": [("azurerm_container_registry", "this")],
    "monitor": [("azurerm_log_analytics_workspace", "this")],
    "staticwebapp": [("azurerm_static_web_app", "this")],
}


# ---------------------------------------------------------------------------
# Parsing helpers built on top of terraform_env_roots_scan's brace-balanced
# block extraction -- this script does not re-implement that primitive.
# ---------------------------------------------------------------------------

def find_output_blocks(text: str) -> dict:
    """Return {output_name: value_expression_text} for every `output "X" { ... }` block."""
    text = _strip_line_comments(text)
    blocks: dict = {}
    for m in re.finditer(r'output\s+"([A-Za-z0-9_-]+)"\s*{', text):
        body = _extract_block(text, m.end() - 1)
        value_m = re.search(r'value\s*=\s*(.+)', body)
        blocks[m.group(1)] = value_m.group(1).strip() if value_m else None
    return blocks


def find_locals_tags(text: str) -> dict:
    """Return {"project": ..., "env": ...} parsed out of `locals { tags = { ... } }`."""
    body = _find_block(_strip_line_comments(text), r"locals")
    if body is None:
        return {}
    tags_body = _find_block(body, r"tags\s*=")
    if tags_body is None:
        return {}
    return dict(re.findall(r'(\w+)\s*=\s*"?([^"\n]+?)"?\s*\n', tags_body + "\n"))


def find_resource_tags_ref(text: str, resource_type: str, resource_name: str) -> str | None:
    """Return the raw `tags = <expr>` right-hand side text for `resource "<type>" "<name>"`, or None."""
    header = rf'resource\s+"{re.escape(resource_type)}"\s+"{re.escape(resource_name)}"\s*'
    body = _find_block(_strip_line_comments(text), header)
    if body is None:
        return None
    m = re.search(r'tags\s*=\s*(\S+)', body)
    return m.group(1) if m else None


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring scripts/terraform_env_roots_scan.py
# ---------------------------------------------------------------------------

def check_root_resource_group_tags(dev_root: Path = DEV_ROOT) -> tuple:
    main_path = dev_root / "main.tf"
    if not main_path.is_file():
        return False, "infra/environments/dev/main.tf does not exist"
    text = main_path.read_text(encoding="utf-8")
    body = _find_block(_strip_line_comments(text), r'resource\s+"azurerm_resource_group"\s+"this"\s*')
    if body is None:
        return False, "infra/environments/dev/main.tf has no azurerm_resource_group.this"
    tags_body = _find_block(body, r"tags\s*=")
    if tags_body is None:
        return False, "infra/environments/dev/main.tf azurerm_resource_group.this has no tags block"
    tags = dict(re.findall(r'(\w+)\s*=\s*"?([^"\n]+?)"?\s*\n', tags_body + "\n"))
    if tags.get("project") != "contigo" or tags.get("env") != "local.environment":
        return False, f"infra/environments/dev/main.tf azurerm_resource_group.this tags={tags!r}"
    return True, "infra/environments/dev/main.tf azurerm_resource_group.this tagged project=contigo, env=local.environment"


def check_module_outputs(module: str, modules_root: Path = MODULES_ROOT) -> tuple:
    outputs_path = modules_root / module / "outputs.tf"
    if not outputs_path.is_file():
        return False, f"infra/modules/{module}/outputs.tf does not exist"
    found = find_output_blocks(outputs_path.read_text(encoding="utf-8"))
    expected = EXPECTED_MODULE_OUTPUTS[module]
    missing = [name for name in expected if name not in found]
    if missing:
        return False, f"infra/modules/{module}/outputs.tf missing output(s): {', '.join(missing)}"
    mismatched = [name for name, expr in expected.items() if found.get(name) != expr]
    if mismatched:
        detail = ", ".join(f"{name} value={found.get(name)!r} expected {expected[name]!r}" for name in mismatched)
        return False, f"infra/modules/{module}/outputs.tf output value mismatch: {detail}"
    return True, f"infra/modules/{module}/outputs.tf declares {', '.join(sorted(expected))}"


def check_module_resource_tags(module: str, modules_root: Path = MODULES_ROOT) -> tuple:
    main_path = modules_root / module / "main.tf"
    if not main_path.is_file():
        return False, f"infra/modules/{module}/main.tf does not exist"
    text = main_path.read_text(encoding="utf-8")
    tags = find_locals_tags(text)
    if tags.get("project") != "contigo" or tags.get("env") != "var.environment":
        return False, (
            f"infra/modules/{module}/main.tf locals.tags={tags!r}, "
            'expected project="contigo" and env=var.environment'
        )
    untagged = []
    for resource_type, resource_name in TAGGED_RESOURCES_BY_MODULE[module]:
        ref = find_resource_tags_ref(text, resource_type, resource_name)
        if ref != "local.tags":
            untagged.append(f"{resource_type}.{resource_name} tags={ref!r}")
    if untagged:
        return False, f"infra/modules/{module}/main.tf resource(s) not tagged with local.tags: {', '.join(untagged)}"
    return True, (
        f"infra/modules/{module}/main.tf locals.tags=project=contigo/env=var.environment; "
        f"{len(TAGGED_RESOURCES_BY_MODULE[module])} resource(s) tagged with local.tags"
    )


def check_root_outputs(dev_root: Path = DEV_ROOT) -> tuple:
    outputs_path = dev_root / "outputs.tf"
    if not outputs_path.is_file():
        return False, "infra/environments/dev/outputs.tf does not exist"
    found = find_output_blocks(outputs_path.read_text(encoding="utf-8"))
    missing = [name for name in EXPECTED_ROOT_OUTPUTS if name not in found]
    if missing:
        return False, f"infra/environments/dev/outputs.tf missing output(s): {', '.join(missing)}"
    mismatched = [name for name, expr in EXPECTED_ROOT_OUTPUTS.items() if found.get(name) != expr]
    if mismatched:
        detail = ", ".join(
            f"{name} value={found.get(name)!r} expected {EXPECTED_ROOT_OUTPUTS[name]!r}" for name in mismatched
        )
        return False, f"infra/environments/dev/outputs.tf output value mismatch: {detail}"
    return True, f"infra/environments/dev/outputs.tf declares all {len(EXPECTED_ROOT_OUTPUTS)} required outputs"


def run_all_checks(dev_root: Path = DEV_ROOT, modules_root: Path = MODULES_ROOT) -> list:
    checks = [("dev root resource-group tags", check_root_resource_group_tags(dev_root))]
    for module in EXPECTED_MODULE_OUTPUTS:
        checks.append((f"{module} module outputs", check_module_outputs(module, modules_root)))
        checks.append((f"{module} module resource tags", check_module_resource_tags(module, modules_root)))
    checks.append(("dev root outputs", check_root_outputs(dev_root)))
    return checks


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print("[dev_outputs_verify] PASS: dev outputs expose resource ids/endpoints; tags applied (AC-1/AC-2, ADR-005)")
        return 0
    print("[dev_outputs_verify] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
