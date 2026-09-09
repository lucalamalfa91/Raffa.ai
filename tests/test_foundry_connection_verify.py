"""Unit tests for scripts/foundry_connection_verify.py (task E01/F02/US05/T02,
extended for the 2026-09-09 ADR-004/ADR-008/ADR-017 amendments).

Same shape as tests/test_keyvault_scope_grants_check.py: pure-parser unit
tests against hand-written fixtures, `check_*` against a deliberately-good
synthetic fixture tree and one deliberately-broken tree per failure mode
this scan exists to catch, then two end-to-end proofs against this actual
working tree (`run_all_checks()` directly, and the script itself as a
subprocess -- the same invocation the definition of done relies on).

Run:
    python tests/test_foundry_connection_verify.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import foundry_connection_verify as fcv  # noqa: E402

GOOD_IDENTITY_OUTPUTS_TF = (
    'output "workload_principal_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.principal_id\n"
    "}\n"
    "\n"
    'output "workload_identity_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.id\n"
    "}\n"
)

# Reproduces a real-world corruption: two textually-present
# `output "workload_identity_id" {` headers, regardless of how the bodies
# nest -- the exact shape that a plain {name: value} dict-based scan
# (see module docstring) would silently collapse into "one entry" and pass.
DUPLICATE_IDENTITY_OUTPUTS_TF = (
    'output "workload_principal_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.principal_id\n"
    "}\n"
    "\n"
    'output "workload_identity_id" {\n'
    '  description = "first"\n'
    'output "workload_identity_id" {\n'
    '  description = "second"\n'
    "  value = azurerm_user_assigned_identity.workload.id\n"
    "}\n"
    "}\n"
)

MISSING_IDENTITY_OUTPUTS_TF = (
    'output "workload_principal_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.principal_id\n"
    "}\n"
)

WRONG_VALUE_IDENTITY_OUTPUTS_TF = (
    'output "workload_identity_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.principal_id\n"
    "}\n"
)

GOOD_PROJECTS = (
    {"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "conn-docint-contigo-dev"},
    {"project": "contigo-demo", "env": "demo", "document_intelligence_connection": "conn-docint-contigo-demo"},
)

# ---------------------------------------------------------------------------
# modules/containerapps fixtures -- trimmed-down synthetic versions of the
# real main.tf / variables.tf shapes.
# ---------------------------------------------------------------------------


def _container_app_block(resource_name: str, env_lines: str) -> str:
    return (
        f'resource "azurerm_container_app" "{resource_name}" {{\n'
        "  template {\n"
        "    container {\n"
        f"{env_lines}"
        "    }\n"
        "  }\n"
        "}\n"
    )


ENDPOINT_ENV = (
    "      env {\n"
    '        name  = "AiGateway__Endpoint"\n'
    "        value = var.ai_gateway_endpoint\n"
    "      }\n"
)
PROJECT_NAME_ENV = (
    "      env {\n"
    '        name  = "AiGateway__ProjectName"\n'
    "        value = var.ai_gateway_project_name\n"
    "      }\n"
)
DOCUMENT_INTELLIGENCE_CONNECTION_ENV = (
    "      env {\n"
    '        name  = "AiGateway__DocumentIntelligenceConnection"\n'
    "        value = var.ai_gateway_document_intelligence_connection\n"
    "      }\n"
)


def _dynamic_model_env(for_each: str = "var.ai_gateway_model_env", name: str = "env.key", value: str = "env.value") -> str:
    return (
        '      dynamic "env" {\n'
        f"        for_each = {for_each}\n"
        "\n"
        "        content {\n"
        f"          name  = {name}\n"
        f"          value = {value}\n"
        "        }\n"
        "      }\n"
    )


DYNAMIC_MODEL_ENV = _dynamic_model_env()
STATIC_AI_GATEWAY_ENV = ENDPOINT_ENV + PROJECT_NAME_ENV + DOCUMENT_INTELLIGENCE_CONNECTION_ENV
GOOD_AI_GATEWAY_ENV = STATIC_AI_GATEWAY_ENV + DYNAMIC_MODEL_ENV

GOOD_CONTAINERAPPS_MAIN_TF = _container_app_block("api", GOOD_AI_GATEWAY_ENV) + "\n" + _container_app_block(
    "worker", GOOD_AI_GATEWAY_ENV
)

# worker never got the env blocks (e.g. a copy-paste that only touched
# "api") -- the exact kind of half-applied edit these checks exist to catch.
WORKER_MISSING_ENV_CONTAINERAPPS_MAIN_TF = _container_app_block("api", GOOD_AI_GATEWAY_ENV) + "\n" + _container_app_block(
    "worker", ""
)
WORKER_MISSING_DYNAMIC_CONTAINERAPPS_MAIN_TF = _container_app_block("api", GOOD_AI_GATEWAY_ENV) + "\n" + _container_app_block(
    "worker", STATIC_AI_GATEWAY_ENV
)
WRONG_FOR_EACH_CONTAINERAPPS_MAIN_TF = _container_app_block(
    "api", STATIC_AI_GATEWAY_ENV + _dynamic_model_env(for_each="var.some_other_map")
) + "\n" + _container_app_block("worker", GOOD_AI_GATEWAY_ENV)

# Endpoint wired as a Key Vault secret instead of a plain value -- wrong
# per ADR-011 (it is not a secret) even though the env var name is right.
SECRET_BACKED_ENDPOINT_ENV = (
    "      env {\n"
    '        name        = "AiGateway__Endpoint"\n'
    '        secret_name = "ai-endpoint"\n'
    "      }\n"
)
SECRET_BACKED_CONTAINERAPPS_MAIN_TF = _container_app_block(
    "api", SECRET_BACKED_ENDPOINT_ENV + PROJECT_NAME_ENV + DOCUMENT_INTELLIGENCE_CONNECTION_ENV + DYNAMIC_MODEL_ENV
)


def _containerapps_variables_tf(with_model_env: bool = True, with_default: bool = False, type_expr: str = "map(string)") -> str:
    text = 'variable "ai_gateway_endpoint" {\n  type = string\n}\n\n'
    if with_model_env:
        default_line = "  default = {}\n" if with_default else ""
        text += f'variable "ai_gateway_model_env" {{\n  type = {type_expr}\n{default_line}}}\n'
    return text


GOOD_CONTAINERAPPS_VARIABLES_TF = _containerapps_variables_tf()


# ---------------------------------------------------------------------------
# modules/foundry fixtures -- the same shape as the real main.tf/outputs.tf,
# with every load-bearing token parameterised so each failure mode can be
# planted on its own.
# ---------------------------------------------------------------------------


def _foundry_main_tf(
    account_name: str = "aisvc-contigo",
    rg_name: str = "rg-contigo-ai",
    count_expr: str | None = "local.account_enabled ? 1 : 0",
    scope: str = "local.ai_services_account_id",
    principal: str = "var.workload_principal_id",
    role_user: str = "Cognitive Services User",
    role_openai: str = "Cognitive Services OpenAI User",
    with_openai_role: bool = True,
    kind: str = "AIServices",
    local_auth: str = "false",
    upgrade: str = "NoAutoUpgrade",
    deployment_name: str = "${each.key}-${var.environment}",
    account_count: str | None = "var.create_shared_account ? 1 : 0",
    publish_gate: bool = True,
    endpoint_local: str = (
        "try(\n"
        "    azurerm_cognitive_account.this[0].endpoint,\n"
        "    data.azurerm_cognitive_account.this[0].endpoint,\n"
        '    "",\n'
        "  )"
    ),
    with_operator: bool = True,
    operator_for_each: str = "local.operator_grants",
    operator_source: str = "var.ai_operator_principal_ids",
    ocr_model: tuple = ("prebuilt-read", "2024-11-30"),
    extra: str = "",
) -> str:
    count_line = f"  count = {count_expr}\n\n" if count_expr else ""
    account_count_line = f"  count = {account_count}\n\n" if account_count else ""
    publish_filter = " if local.publish" if publish_gate else ""
    openai_role = (
        'resource "azurerm_role_assignment" "workload_ai_services_openai_user" {\n'
        f"{count_line}"
        f"  scope                            = {scope}\n"
        f'  role_definition_name             = "{role_openai}"\n'
        f"  principal_id                     = {principal}\n"
        "  skip_service_principal_aad_check = true\n"
        "}\n\n"
        if with_openai_role
        else ""
    )
    operator = (
        'resource "azurerm_role_assignment" "operator" {\n'
        f"  for_each = {operator_for_each}\n\n"
        f"  scope                = {scope}\n"
        "  role_definition_name = each.value.role\n"
        "  principal_id         = each.value.principal_id\n"
        "}\n\n"
        if with_operator
        else ""
    )
    return (
        "locals {\n"
        f'  ai_services_account_name = "{account_name}"\n'
        f'  ai_resource_group_name   = "{rg_name}"\n'
        '  foundry_project_name             = "contigo-${var.environment}"\n'
        '  document_intelligence_connection = "conn-docint-contigo-${var.environment}"\n'
        f'  ocr_model_id      = "{ocr_model[0]}"\n'
        f'  ocr_model_version = "{ocr_model[1]}"\n'
        "  account_enabled = var.create_shared_account || var.attach_shared_account\n"
        "  publish         = local.account_enabled && var.publish_endpoint\n"
        "  ai_services_account_id = try(\n"
        "    azurerm_cognitive_account.this[0].id,\n"
        "    data.azurerm_cognitive_account.this[0].id,\n"
        '    "",\n'
        "  )\n"
        f"  ai_services_endpoint = {endpoint_local}\n"
        "  tags_shared = {\n"
        '    project = "contigo"\n'
        '    env     = "shared"\n'
        "  }\n"
        "  tags = {\n"
        '    project = "contigo"\n'
        "    env     = var.environment\n"
        "  }\n"
        '  data_plane_roles = ["Cognitive Services User", "Cognitive Services OpenAI User"]\n'
        "  operator_grants = {\n"
        f"    for pair in setproduct({operator_source}, local.data_plane_roles) :\n"
        '    "${pair[0]}-${pair[1]}" => {\n'
        "      principal_id = pair[0]\n"
        "      role         = pair[1]\n"
        "    }\n"
        "    if local.account_enabled\n"
        "  }\n"
        "  enabled_model_deployments = { for k, d in var.model_deployments : k => d if local.account_enabled }\n"
        "  role_model = merge(\n"
        "    {\n"
        "      for role, key in var.model_roles : role => {\n"
        "        id      = azurerm_cognitive_deployment.model[key].name\n"
        "        version = azurerm_cognitive_deployment.model[key].model[0].version\n"
        "      }\n"
        "      if local.account_enabled\n"
        "    },\n"
        "    {\n"
        "      for role, m in { ocr = { id = local.ocr_model_id, version = local.ocr_model_version } } : role => m\n"
        "      if local.account_enabled\n"
        "    },\n"
        "  )\n"
        "  model_env = merge(\n"
        '    { for role, m in local.role_model : "AiGateway__Models__${title(role)}__ModelId" => m.id'
        f"{publish_filter} }},\n"
        '    { for role, m in local.role_model : "AiGateway__Models__${title(role)}__ModelVersion" => m.version'
        f"{publish_filter} }},\n"
        f"    {{ for k, v in var.extra_gateway_env : k => v{publish_filter} }},\n"
        "  )\n"
        "}\n"
        "\n"
        'resource "azurerm_resource_group" "ai" {\n'
        f"{account_count_line}"
        "  name     = local.ai_resource_group_name\n"
        "  location = var.location\n\n"
        "  tags = local.tags_shared\n"
        "}\n"
        "\n"
        'resource "azurerm_cognitive_account" "this" {\n'
        f"{account_count_line}"
        "  name                  = local.ai_services_account_name\n"
        "  location              = var.location\n"
        "  resource_group_name   = azurerm_resource_group.ai[0].name\n"
        f'  kind                  = "{kind}"\n'
        '  sku_name              = "S0"\n'
        "  custom_subdomain_name = local.ai_services_account_name\n\n"
        "  project_management_enabled    = true\n"
        f"  local_auth_enabled            = {local_auth}\n"
        "  public_network_access_enabled = true\n\n"
        "  identity {\n"
        '    type = "SystemAssigned"\n'
        "  }\n\n"
        "  tags = local.tags_shared\n"
        "}\n"
        "\n"
        'data "azurerm_cognitive_account" "this" {\n'
        "  count = (var.attach_shared_account && !var.create_shared_account) ? 1 : 0\n\n"
        "  name                = local.ai_services_account_name\n"
        "  resource_group_name = local.ai_resource_group_name\n"
        "}\n"
        "\n"
        'resource "azurerm_cognitive_account_project" "this" {\n'
        "  count = local.account_enabled ? 1 : 0\n\n"
        "  name                 = local.foundry_project_name\n"
        "  cognitive_account_id = local.ai_services_account_id\n"
        "  location             = var.location\n\n"
        "  identity {\n"
        '    type = "SystemAssigned"\n'
        "  }\n\n"
        "  tags = local.tags\n"
        "}\n"
        "\n"
        'resource "azurerm_cognitive_deployment" "model" {\n'
        "  for_each = local.enabled_model_deployments\n\n"
        f'  name                 = "{deployment_name}"\n'
        "  cognitive_account_id = local.ai_services_account_id\n\n"
        "  model {\n"
        '    format  = "OpenAI"\n'
        "    name    = each.key\n"
        "    version = each.value.model_version\n"
        "  }\n\n"
        "  sku {\n"
        "    name     = each.value.sku_name\n"
        "    capacity = each.value.capacity\n"
        "  }\n\n"
        f'  version_upgrade_option = "{upgrade}"\n'
        "}\n"
        "\n"
        'resource "azurerm_role_assignment" "workload_ai_services_user" {\n'
        f"{count_line}"
        f"  scope                            = {scope}\n"
        f'  role_definition_name             = "{role_user}"\n'
        f"  principal_id                     = {principal}\n"
        "  skip_service_principal_aad_check = true\n"
        "}\n"
        "\n"
        f"{openai_role}"
        f"{operator}"
        f"{extra}"
    )


def _foundry_outputs_tf(
    endpoint_value: str = 'local.publish ? local.ai_services_endpoint : ""',
    model_env_value: str = "local.model_env",
    with_model_env: bool = True,
) -> str:
    model_env = (
        f'output "model_env" {{\n  value = {model_env_value}\n}}\n\n' if with_model_env else ""
    )
    return (
        f'output "ai_services_endpoint" {{\n  value = {endpoint_value}\n}}\n\n'
        f"{model_env}"
        'output "foundry_project_name" {\n  value = local.foundry_project_name\n}\n'
    )


GOOD_FOUNDRY_MAIN_TF = _foundry_main_tf()
GOOD_FOUNDRY_OUTPUTS_TF = _foundry_outputs_tf()


def _write_containerapps_fixture(root: Path, text: str, variables_text: str = GOOD_CONTAINERAPPS_VARIABLES_TF) -> Path:
    modules_dir = root / "infra" / "modules" / "containerapps"
    modules_dir.mkdir(parents=True, exist_ok=True)
    (modules_dir / "main.tf").write_text(text, encoding="utf-8")
    (modules_dir / "variables.tf").write_text(variables_text, encoding="utf-8")
    return modules_dir / "main.tf"


def _write_foundry_module_fixture(root: Path, text: str, outputs_text: str = GOOD_FOUNDRY_OUTPUTS_TF) -> Path:
    modules_dir = root / "infra" / "modules" / "foundry"
    modules_dir.mkdir(parents=True, exist_ok=True)
    (modules_dir / "main.tf").write_text(text, encoding="utf-8")
    (modules_dir / "outputs.tf").write_text(outputs_text, encoding="utf-8")
    return modules_dir / "main.tf"


DEV_DEPLOYMENTS = (
    '    "gpt-5.4-nano"           = { model_version = "2026-03-17", sku_name = "DataZoneStandard", capacity = 300 }\n'
    '    "text-embedding-3-small" = { model_version = "1", sku_name = "GlobalStandard", capacity = 100 }\n'
)
DEV_ROLES = (
    '    classify = "gpt-5.4-nano"\n'
    '    extract  = "gpt-5.4-nano"\n'
    '    answer   = "gpt-5.4-nano"\n'
    '    embed    = "text-embedding-3-small"\n'
)


def _write_env_root_foundry_wiring(
    root: Path,
    env: str,
    with_module: bool = True,
    with_variable: bool = True,
    create: str | None = None,
    attach: str | None = None,
    roles: str = DEV_ROLES,
    deployments: str = DEV_DEPLOYMENTS,
    with_containerapps: bool = True,
    with_attach_variable: bool = True,
    legacy_variable: bool = False,
    publish: str = "var.ai_gateway_wired",
) -> None:
    env_dir = root / "infra" / "environments" / env
    env_dir.mkdir(parents=True, exist_ok=True)
    create = create if create is not None else ("true" if env == "dev" else "false")
    attach = attach if attach is not None else ("false" if env == "dev" else "var.ai_account_attached")
    module_block = (
        'module "foundry" {\n'
        '  source = "../../modules/foundry"\n\n'
        "  environment           = local.environment\n"
        "  location              = var.location\n"
        "  workload_principal_id = module.identity.workload_principal_id\n\n"
        f"  create_shared_account = {create}\n"
        f"  attach_shared_account = {attach}\n"
        f"  publish_endpoint      = {publish}\n\n"
        "  model_deployments = {\n"
        f"{deployments}"
        "  }\n\n"
        "  model_roles = {\n"
        f"{roles}"
        "  }\n"
        "}\n\n"
        if with_module
        else ""
    )
    containerapps_block = (
        'module "containerapps" {\n'
        '  source = "../../modules/containerapps"\n\n'
        "  ai_gateway_endpoint  = module.foundry.ai_services_endpoint\n"
        "  ai_gateway_model_env = module.foundry.model_env\n"
        "}\n\n"
        if with_containerapps
        else ""
    )
    (env_dir / "main.tf").write_text(
        containerapps_block + module_block + "# no other modules needed for this fixture\n", encoding="utf-8"
    )
    variable_block = 'variable "ai_gateway_wired" {\n  type    = bool\n  default = false\n}\n' if with_variable else ""
    if env == "demo" and with_attach_variable:
        variable_block += 'variable "ai_account_attached" {\n  type    = bool\n  default = false\n}\n'
    if legacy_variable:
        variable_block += 'variable "foundry_ai_services_resource_id" {\n  type    = string\n  default = ""\n}\n'
    (env_dir / "variables.tf").write_text(variable_block, encoding="utf-8")


def _write_identity_fixture(root: Path, outputs_text: str) -> Path:
    identity_dir = root / "infra" / "modules" / "identity"
    identity_dir.mkdir(parents=True, exist_ok=True)
    (identity_dir / "outputs.tf").write_text(outputs_text, encoding="utf-8")
    return identity_dir


def _write_environments_fixture(root: Path, dev_location: str, demo_location: str) -> Path:
    environments_root = root / "infra" / "environments"
    for env, location in (("dev", dev_location), ("demo", demo_location)):
        env_dir = environments_root / env
        env_dir.mkdir(parents=True, exist_ok=True)
        (env_dir / "variables.tf").write_text(
            f'variable "location" {{\n  type    = string\n  default = "{location}"\n}}\n', encoding="utf-8"
        )
    return environments_root


class NormalizeRegionTests(unittest.TestCase):
    def test_strips_spaces_and_lowercases(self) -> None:
        self.assertEqual(fcv._normalize_region("North Europe"), "northeurope")
        self.assertEqual(fcv._normalize_region("northeurope"), "northeurope")
        self.assertEqual(fcv._normalize_region("  North   Europe "), "northeurope")
        self.assertEqual(fcv._normalize_region("West Europe"), "westeurope")

    def test_north_europe_and_northeurope_are_the_same_region(self) -> None:
        self.assertEqual(fcv._normalize_region("North Europe"), fcv._normalize_region(fcv.FOUNDRY_REGION))


class BuildFoundryConnectionsTests(unittest.TestCase):
    def test_default_uses_real_bootstrap_projects(self) -> None:
        connections = fcv.build_foundry_connections()
        self.assertEqual({c["project"] for c in connections}, {"contigo-dev", "contigo-demo"})
        for c in connections:
            self.assertEqual(c["region"], "northeurope")
            self.assertIn(fcv.hcp.AI_SERVICES_ACCOUNT_NAME, c["connection_id"])
            self.assertIn(c["project"], c["connection_id"])
            self.assertIn(c["document_intelligence_connection"], c["connection_id"])

    def test_custom_projects_are_respected(self) -> None:
        custom = (
            {"project": "x", "env": "dev", "document_intelligence_connection": "conn-x"},
        )
        connections = fcv.build_foundry_connections(custom)
        self.assertEqual(len(connections), 1)
        self.assertEqual(
            connections[0]["connection_id"], f"{fcv.hcp.AI_SERVICES_ACCOUNT_NAME}/projects/x/connections/conn-x"
        )

    def test_deterministic_across_calls(self) -> None:
        self.assertEqual(fcv.build_foundry_connections(GOOD_PROJECTS), fcv.build_foundry_connections(GOOD_PROJECTS))


class CheckFoundryAccountShapeStillRecordedTests(unittest.TestCase):
    def test_delegates_to_bootstrap_hcp_org(self) -> None:
        self.assertEqual(fcv.check_foundry_account_shape_still_recorded(), fcv.hcp.check_foundry_account_recorded())

    def test_currently_passes_against_the_real_module_constants(self) -> None:
        passed, detail = fcv.check_foundry_account_shape_still_recorded()
        self.assertTrue(passed, detail)
        self.assertIn("rg-contigo-ai", detail)


class CheckRegionPinnedToWesteuropeTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            environments_root = _write_environments_fixture(Path(tmp), "North Europe", "North Europe")
            passed, detail = fcv.check_region_pinned_to_westeurope(environments_root)
            self.assertTrue(passed, detail)

    def test_dev_drifted_to_another_region_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            environments_root = _write_environments_fixture(Path(tmp), "East US", "North Europe")
            passed, detail = fcv.check_region_pinned_to_westeurope(environments_root)
            self.assertFalse(passed, detail)

    def test_demo_drifted_to_another_region_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            environments_root = _write_environments_fixture(Path(tmp), "North Europe", "West Europe")
            passed, detail = fcv.check_region_pinned_to_westeurope(environments_root)
            self.assertFalse(passed, detail)

    def test_missing_variables_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            environments_root = Path(tmp) / "infra" / "environments"
            (environments_root / "dev").mkdir(parents=True)
            (environments_root / "demo").mkdir(parents=True)
            passed, detail = fcv.check_region_pinned_to_westeurope(environments_root)
            self.assertFalse(passed, detail)


class CheckConnectionIdsWellFormedTests(unittest.TestCase):
    def test_good_projects_pass(self) -> None:
        passed, detail = fcv.check_connection_ids_well_formed(GOOD_PROJECTS)
        self.assertTrue(passed, detail)

    def test_empty_connection_name_fails(self) -> None:
        bad = ({"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "   "},)
        passed, detail = fcv.check_connection_ids_well_formed(bad)
        self.assertFalse(passed, detail)

    def test_missing_key_raises_or_fails_loudly(self) -> None:
        bad = ({"project": "contigo-dev", "env": "dev"},)
        with self.assertRaises(KeyError):
            fcv.check_connection_ids_well_formed(bad)


class CheckConnectionIdsUniqueAndIsolatedTests(unittest.TestCase):
    def test_good_projects_pass(self) -> None:
        passed, detail = fcv.check_connection_ids_unique_and_isolated(GOOD_PROJECTS)
        self.assertTrue(passed, detail)

    def test_duplicate_connection_name_across_projects_fails(self) -> None:
        bad = (
            {"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "same-name"},
            {"project": "contigo-demo", "env": "demo", "document_intelligence_connection": "same-name"},
        )
        # different project segments still make the ids unique -- this
        # documents that connection_id uniqueness comes from the project
        # name, not the raw connection label.
        passed, detail = fcv.check_connection_ids_unique_and_isolated(bad)
        self.assertTrue(passed, detail)

    def test_duplicate_env_fails(self) -> None:
        bad = (
            {"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "conn-a"},
            {"project": "contigo-dev-2", "env": "dev", "document_intelligence_connection": "conn-b"},
        )
        passed, detail = fcv.check_connection_ids_unique_and_isolated(bad)
        self.assertFalse(passed, detail)

    def test_duplicate_project_fails(self) -> None:
        bad = (
            {"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "conn-a"},
            {"project": "contigo-dev", "env": "demo", "document_intelligence_connection": "conn-a"},
        )
        passed, detail = fcv.check_connection_ids_unique_and_isolated(bad)
        self.assertFalse(passed, detail)


class CheckConnectionsShareSingleAccountTests(unittest.TestCase):
    def test_good_projects_pass(self) -> None:
        passed, detail = fcv.check_connections_share_single_account(GOOD_PROJECTS)
        self.assertTrue(passed, detail)

    def test_all_connections_use_the_module_account_name(self) -> None:
        for c in fcv.build_foundry_connections(GOOD_PROJECTS):
            self.assertTrue(c["connection_id"].startswith(fcv.hcp.AI_SERVICES_ACCOUNT_NAME + "/projects/"))


class CheckDocumentIntelligenceModelsRecordedTests(unittest.TestCase):
    def test_passes_against_the_real_constant(self) -> None:
        passed, detail = fcv.check_document_intelligence_models_recorded()
        self.assertTrue(passed, detail)
        self.assertIn("prebuilt-read", detail)
        self.assertIn("prebuilt-layout", detail)

    def test_ocr_model_is_a_recorded_document_intelligence_model(self) -> None:
        self.assertIn(fcv.OCR_MODEL[0], fcv.DOCUMENT_INTELLIGENCE_MODELS)


class CheckWorkloadIdentityOutputWellFormedTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            identity_dir = _write_identity_fixture(Path(tmp), GOOD_IDENTITY_OUTPUTS_TF)
            passed, detail = fcv.check_workload_identity_output_well_formed(identity_dir)
            self.assertTrue(passed, detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            identity_dir = Path(tmp) / "infra" / "modules" / "identity"
            identity_dir.mkdir(parents=True)
            passed, detail = fcv.check_workload_identity_output_well_formed(identity_dir)
            self.assertFalse(passed, detail)

    def test_missing_output_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            identity_dir = _write_identity_fixture(Path(tmp), MISSING_IDENTITY_OUTPUTS_TF)
            passed, detail = fcv.check_workload_identity_output_well_formed(identity_dir)
            self.assertFalse(passed, detail)

    def test_wrong_value_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            identity_dir = _write_identity_fixture(Path(tmp), WRONG_VALUE_IDENTITY_OUTPUTS_TF)
            passed, detail = fcv.check_workload_identity_output_well_formed(identity_dir)
            self.assertFalse(passed, detail)

    def test_duplicate_block_fails(self) -> None:
        """A duplicated `output "workload_identity_id"` block must fail
        loudly, not be silently collapsed to "one entry" by a dict-based
        scan."""
        with tempfile.TemporaryDirectory() as tmp:
            identity_dir = _write_identity_fixture(Path(tmp), DUPLICATE_IDENTITY_OUTPUTS_TF)
            passed, detail = fcv.check_workload_identity_output_well_formed(identity_dir)
            self.assertFalse(passed, detail)
            self.assertIn("2 times", detail)


class CheckContainerappsAiGatewayEnvWiredTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_ai_gateway_env_wired(path)
            self.assertTrue(passed, detail)

    def test_dynamic_block_does_not_confuse_the_static_scan(self) -> None:
        """The `dynamic "env"` block must be invisible to the static env
        scan (its header is not `env {` and its content sets name = env.key,
        not a literal) -- the three static vars are still found exactly."""
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF)
            env_vars = fcv._env_vars_in_resource(path.read_text(encoding="utf-8"), "azurerm_container_app", "api")
            self.assertEqual(set(env_vars), set(fcv.REQUIRED_AI_GATEWAY_ENV_VARS))

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "containerapps" / "main.tf"
            passed, detail = fcv.check_containerapps_ai_gateway_env_wired(path)
            self.assertFalse(passed, detail)

    def test_worker_missing_env_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), WORKER_MISSING_ENV_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_ai_gateway_env_wired(path)
            self.assertFalse(passed, detail)
            self.assertIn("worker", detail)

    def test_secret_backed_env_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), SECRET_BACKED_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_ai_gateway_env_wired(path)
            self.assertFalse(passed, detail)
            self.assertIn("secret_name", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_containerapps_ai_gateway_env_wired()
        self.assertTrue(passed, detail)


class CheckContainerappsModelEnvDynamicBlockTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_model_env_dynamic_block(path)
            self.assertTrue(passed, detail)

    def test_worker_missing_dynamic_block_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), WORKER_MISSING_DYNAMIC_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_model_env_dynamic_block(path)
            self.assertFalse(passed, detail)
            self.assertIn("worker", detail)

    def test_wrong_for_each_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_containerapps_fixture(Path(tmp), WRONG_FOR_EACH_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_model_env_dynamic_block(path)
            self.assertFalse(passed, detail)
            self.assertIn("for_each", detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "containerapps" / "main.tf"
            passed, detail = fcv.check_containerapps_model_env_dynamic_block(path)
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_containerapps_model_env_dynamic_block()
        self.assertTrue(passed, detail)


class CheckContainerappsModelEnvVariableTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            main = _write_containerapps_fixture(Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF)
            passed, detail = fcv.check_containerapps_model_env_variable(main.parent / "variables.tf")
            self.assertTrue(passed, detail)

    def test_missing_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            main = _write_containerapps_fixture(
                Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF, _containerapps_variables_tf(with_model_env=False)
            )
            passed, detail = fcv.check_containerapps_model_env_variable(main.parent / "variables.tf")
            self.assertFalse(passed, detail)

    def test_variable_with_default_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            main = _write_containerapps_fixture(
                Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF, _containerapps_variables_tf(with_default=True)
            )
            passed, detail = fcv.check_containerapps_model_env_variable(main.parent / "variables.tf")
            self.assertFalse(passed, detail)
            self.assertIn("default", detail)

    def test_wrong_type_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            main = _write_containerapps_fixture(
                Path(tmp), GOOD_CONTAINERAPPS_MAIN_TF, _containerapps_variables_tf(type_expr="string")
            )
            passed, detail = fcv.check_containerapps_model_env_variable(main.parent / "variables.tf")
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_containerapps_model_env_variable()
        self.assertTrue(passed, detail)


class CheckFoundryWorkloadRoleAssignmentsGatedTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertTrue(passed, detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "foundry" / "main.tf"
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)

    def test_unconditional_resource_fails(self) -> None:
        """No `count = ...` at all -- would fail every plan of a root that
        has not attached to the account yet."""
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(count_expr=None))
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)
            self.assertIn("count", detail)

    def test_wrong_role_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(role_user="Contributor"))
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)
            self.assertIn("Cognitive Services User", detail)

    def test_missing_openai_role_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(with_openai_role=False))
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)
            self.assertIn("Cognitive Services OpenAI User", detail)

    def test_wrong_principal_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(principal="var.some_other_principal_id"))
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)
            self.assertIn("principal", detail)

    def test_scope_not_account_id_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(scope="var.ai_services_resource_id"))
            passed, detail = fcv.check_foundry_workload_role_assignments_gated(path)
            self.assertFalse(passed, detail)
            self.assertIn("scope", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_workload_role_assignments_gated()
        self.assertTrue(passed, detail)


class CheckFoundryOperatorRoleAssignmentsTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_operator_role_assignments(path)
            self.assertTrue(passed, detail)

    def test_missing_resource_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(with_operator=False))
            passed, detail = fcv.check_foundry_operator_role_assignments(path)
            self.assertFalse(passed, detail)

    def test_wrong_for_each_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(operator_for_each="toset(var.ai_operator_principal_ids)"))
            passed, detail = fcv.check_foundry_operator_role_assignments(path)
            self.assertFalse(passed, detail)
            self.assertIn("for_each", detail)

    def test_grants_not_from_operator_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(operator_source="var.some_other_list"))
            passed, detail = fcv.check_foundry_operator_role_assignments(path)
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_operator_role_assignments()
        self.assertTrue(passed, detail)


class CheckAiServicesAccountNameMatchesTerraformTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertTrue(passed, detail)

    def test_drifted_name_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(account_name="aisvc-contigo-2"))
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertFalse(passed, detail)

    def test_drifted_resource_group_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(rg_name="rg-contigo-dev"))
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertFalse(passed, detail)
            self.assertIn("ai_resource_group_name", detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "foundry" / "main.tf"
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_ai_services_account_name_matches_terraform()
        self.assertTrue(passed, detail)


class CheckFoundryAccountResourceShapeTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_account_resource_shape(path)
            self.assertTrue(passed, detail)

    def test_wrong_kind_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(kind="OpenAI"))
            passed, detail = fcv.check_foundry_account_resource_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("kind", detail)

    def test_local_auth_enabled_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(local_auth="true"))
            passed, detail = fcv.check_foundry_account_resource_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("local_auth_enabled", detail)

    def test_hub_resource_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            hub = 'resource "azurerm_ai_foundry" "hub" {\n  name = "hub-contigo"\n}\n'
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(extra=hub))
            passed, detail = fcv.check_foundry_account_resource_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("azurerm_ai_foundry", detail)

    def test_unconditional_account_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(account_count=None))
            passed, detail = fcv.check_foundry_account_resource_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("create_shared_account", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_account_resource_shape()
        self.assertTrue(passed, detail)


class CheckFoundryProjectAndDeploymentsShapeTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_project_and_deployments_shape(path)
            self.assertTrue(passed, detail)

    def test_auto_upgrade_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(upgrade="OnceNewDefaultVersionAvailable"))
            passed, detail = fcv.check_foundry_project_and_deployments_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("NoAutoUpgrade", detail)

    def test_deployment_name_without_environment_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(deployment_name="${each.key}"))
            passed, detail = fcv.check_foundry_project_and_deployments_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("per-environment", detail)

    def test_wrong_ocr_model_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(ocr_model=("prebuilt-layout", "2024-11-30")))
            passed, detail = fcv.check_foundry_project_and_deployments_shape(path)
            self.assertFalse(passed, detail)
            self.assertIn("ocr_model_id", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_project_and_deployments_shape()
        self.assertTrue(passed, detail)


class CheckFoundryOutputsGatedTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_outputs_gated(path, path.parent / "outputs.tf")
            self.assertTrue(passed, detail)

    def test_endpoint_output_not_gated_fails(self) -> None:
        """The a750746 regression: an endpoint published whether or not the
        account is wired made the dev API answer 500 to every upload."""
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(
                Path(tmp), GOOD_FOUNDRY_MAIN_TF, _foundry_outputs_tf(endpoint_value="local.ai_services_endpoint")
            )
            passed, detail = fcv.check_foundry_outputs_gated(path, path.parent / "outputs.tf")
            self.assertFalse(passed, detail)
            self.assertIn("ai_services_endpoint", detail)

    def test_derived_endpoint_string_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            derived = '"https://${local.ai_services_account_name}.cognitiveservices.azure.com/"'
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(endpoint_local=derived))
            passed, detail = fcv.check_foundry_outputs_gated(path, path.parent / "outputs.tf")
            self.assertFalse(passed, detail)
            self.assertIn("try(", detail)

    def test_model_env_not_gated_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), _foundry_main_tf(publish_gate=False))
            passed, detail = fcv.check_foundry_outputs_gated(path, path.parent / "outputs.tf")
            self.assertFalse(passed, detail)
            self.assertIn("if local.publish", detail)

    def test_missing_model_env_output_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF, _foundry_outputs_tf(with_model_env=False))
            passed, detail = fcv.check_foundry_outputs_gated(path, path.parent / "outputs.tf")
            self.assertFalse(passed, detail)
            self.assertIn("model_env", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_outputs_gated()
        self.assertTrue(passed, detail)


class CheckEnvRootsWireFoundryModuleTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for env in fcv.ENVS:
                _write_env_root_foundry_wiring(root, env)
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertTrue(passed, detail)

    def test_missing_module_block_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev", with_module=False)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("dev", detail)

    def test_missing_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev")
            _write_env_root_foundry_wiring(root, "demo", with_variable=False)
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn(fcv.AI_GATEWAY_WIRED_VARIABLE, detail)

    def test_retired_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev", legacy_variable=True)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn(fcv.RETIRED_ENV_ROOT_VARIABLE, detail)

    def test_publish_not_wired_to_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev", publish="true")
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("publish_endpoint", detail)

    def test_containerapps_without_model_env_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev", with_containerapps=False)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_wire_foundry_module(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("containerapps", detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_env_roots_wire_foundry_module()
        self.assertTrue(passed, detail)


class CheckSingleSharedAccountOwnerTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for env in fcv.ENVS:
                _write_env_root_foundry_wiring(root, env)
            passed, detail = fcv.check_single_shared_account_owner(root / "infra" / "environments")
            self.assertTrue(passed, detail)

    def test_both_roots_creating_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev")
            _write_env_root_foundry_wiring(root, "demo", create="true", attach="false")
            passed, detail = fcv.check_single_shared_account_owner(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("one account", detail)

    def test_nobody_creating_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev", create="false")
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_single_shared_account_owner(root / "infra" / "environments")
            self.assertFalse(passed, detail)

    def test_demo_without_attach_variable_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_env_root_foundry_wiring(root, "dev")
            _write_env_root_foundry_wiring(root, "demo", with_attach_variable=False)
            passed, detail = fcv.check_single_shared_account_owner(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn(fcv.DEMO_ATTACH_VARIABLE, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_single_shared_account_owner()
        self.assertTrue(passed, detail)


class CheckEnvRootsModelRolesCompleteTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for env in fcv.ENVS:
                _write_env_root_foundry_wiring(root, env)
            passed, detail = fcv.check_env_roots_model_roles_complete(root / "infra" / "environments")
            self.assertTrue(passed, detail)
            self.assertIn("classify=gpt-5.4-nano-dev", detail)

    def test_missing_role_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            roles = '    classify = "gpt-5.4-nano"\n    extract  = "gpt-5.4-nano"\n    embed    = "text-embedding-3-small"\n'
            _write_env_root_foundry_wiring(root, "dev", roles=roles)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_model_roles_complete(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("answer", detail)

    def test_role_pointing_at_unknown_deployment_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            roles = DEV_ROLES.replace('answer   = "gpt-5.4-nano"', 'answer   = "gpt-5.4"')
            _write_env_root_foundry_wiring(root, "dev", roles=roles)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_model_roles_complete(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("not a declared deployment", detail)

    def test_provisioned_sku_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            deployments = DEV_DEPLOYMENTS.replace('"DataZoneStandard"', '"GlobalProvisionedManaged"')
            _write_env_root_foundry_wiring(root, "dev", deployments=deployments)
            _write_env_root_foundry_wiring(root, "demo")
            passed, detail = fcv.check_env_roots_model_roles_complete(root / "infra" / "environments")
            self.assertFalse(passed, detail)
            self.assertIn("GlobalProvisionedManaged", detail)

    def test_env_root_model_bindings_parses_the_real_roots(self) -> None:
        bindings = fcv.env_root_model_bindings()
        self.assertEqual(set(bindings), set(fcv.ENVS))
        self.assertEqual(set(bindings["dev"]["roles"]), set(fcv.MODEL_ROLES))
        self.assertEqual(bindings["dev"]["roles"]["embed"], "text-embedding-3-small")
        self.assertEqual(bindings["demo"]["roles"]["extract"], "gpt-5.4")

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_env_roots_model_roles_complete()
        self.assertTrue(passed, detail)


class CheckNoSecretLiteralsTests(unittest.TestCase):
    def _write_good_tree(self, root: Path) -> Path:
        infra_root = root / "infra"
        _write_identity_fixture(root, GOOD_IDENTITY_OUTPUTS_TF)
        foundry_dir = infra_root / "modules" / "foundry"
        foundry_dir.mkdir(parents=True, exist_ok=True)
        (foundry_dir / "main.tf").write_text(GOOD_FOUNDRY_MAIN_TF, encoding="utf-8")
        (foundry_dir / "variables.tf").write_text('variable "environment" {\n  type = string\n}\n', encoding="utf-8")
        (foundry_dir / "outputs.tf").write_text(GOOD_FOUNDRY_OUTPUTS_TF, encoding="utf-8")
        for env in fcv.ENVS:
            _write_env_root_foundry_wiring(root, env)
        return infra_root

    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            infra_root = self._write_good_tree(root)
            passed, detail = fcv.check_no_secret_literals(root, infra_root)
            self.assertTrue(passed, detail)

    def test_planted_secret_is_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            infra_root = self._write_good_tree(root)
            bad = GOOD_IDENTITY_OUTPUTS_TF + "\n# AccountKey=" + ("Z" * 40) + "==\n"
            _write_identity_fixture(root, bad)
            passed, detail = fcv.check_no_secret_literals(root, infra_root)
            self.assertFalse(passed, detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            infra_root = root / "infra"
            passed, detail = fcv.check_no_secret_literals(root, infra_root)
            self.assertFalse(passed, detail)


class RealRepoScanTests(unittest.TestCase):
    """Same invocation the definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in fcv.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "foundry_connection_verify.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)
        self.assertIn("recorded Foundry connection ids", proc.stdout)
        self.assertIn("gpt-5.4-nano-dev", proc.stdout)


if __name__ == "__main__":
    unittest.main()
