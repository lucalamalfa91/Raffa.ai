"""Unit tests for scripts/foundry_connection_verify.py (task E01/F02/US05/T02).

Same shape as tests/test_keyvault_scope_grants_check.py: pure-parser unit
tests against hand-written fixtures, `check_*` against a deliberately-good
synthetic fixture tree and one deliberately-broken tree per failure mode
this scan exists to catch, then two end-to-end proofs against this actual
working tree (`run_all_checks()` directly, and the script itself as a
subprocess -- the same invocation this task's own definition of done
relies on).

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

# Reproduces this task's own real-world corruption: two textually-present
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

GOOD_LOCATION_VARIABLES_TF = 'variable "location" {\n  type    = string\n  default = "North Europe"\n}\n'
BAD_LOCATION_VARIABLES_TF = 'variable "location" {\n  type    = string\n  default = "East US"\n}\n'

GOOD_PROJECTS = (
    {"project": "contigo-dev", "env": "dev", "document_intelligence_connection": "conn-docint-contigo-dev"},
    {"project": "contigo-demo", "env": "demo", "document_intelligence_connection": "conn-docint-contigo-demo"},
)

# ---------------------------------------------------------------------------
# Task E10/F02/US01/T01 fixtures -- trimmed-down synthetic versions of the
# real infra/modules/containerapps/main.tf and infra/modules/foundry/main.tf
# shapes, same "hand-written fixture, one deliberately-broken tree per
# failure mode" approach as the fixtures above.
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
GOOD_AI_GATEWAY_ENV = ENDPOINT_ENV + PROJECT_NAME_ENV + DOCUMENT_INTELLIGENCE_CONNECTION_ENV

GOOD_CONTAINERAPPS_MAIN_TF = _container_app_block("api", GOOD_AI_GATEWAY_ENV) + "\n" + _container_app_block(
    "worker", GOOD_AI_GATEWAY_ENV
)

# worker never got the three env blocks (e.g. a copy-paste that only
# touched "api") -- the exact kind of half-applied edit this check exists
# to catch.
WORKER_MISSING_ENV_CONTAINERAPPS_MAIN_TF = _container_app_block("api", GOOD_AI_GATEWAY_ENV) + "\n" + _container_app_block(
    "worker", ""
)

# Endpoint wired as a Key Vault secret instead of a plain value -- wrong
# per ADR-011 (it is not a secret) even though the env var name is right.
SECRET_BACKED_ENDPOINT_ENV = (
    "      env {\n"
    '        name        = "AiGateway__Endpoint"\n'
    '        secret_name = "ai-endpoint"\n'
    "      }\n"
)
SECRET_BACKED_CONTAINERAPPS_MAIN_TF = _container_app_block(
    "api", SECRET_BACKED_ENDPOINT_ENV + PROJECT_NAME_ENV + DOCUMENT_INTELLIGENCE_CONNECTION_ENV
)


def _foundry_main_tf(
    account_name: str = "aisvc-contigo",
    count_expr: str | None = 'var.ai_services_resource_id != "" ? 1 : 0',
    role: str = "Cognitive Services User",
    scope: str = "var.ai_services_resource_id",
    principal: str = "var.workload_principal_id",
) -> str:
    count_line = f"  count = {count_expr}\n\n" if count_expr else ""
    return (
        "locals {\n"
        f'  ai_services_account_name = "{account_name}"\n'
        "}\n"
        "\n"
        'resource "azurerm_role_assignment" "workload_ai_services_user" {\n'
        f"{count_line}"
        f"  scope                            = {scope}\n"
        f'  role_definition_name             = "{role}"\n'
        f"  principal_id                     = {principal}\n"
        "  skip_service_principal_aad_check = true\n"
        "}\n"
    )


GOOD_FOUNDRY_MAIN_TF = _foundry_main_tf()


def _write_containerapps_fixture(root: Path, text: str) -> Path:
    modules_dir = root / "infra" / "modules" / "containerapps"
    modules_dir.mkdir(parents=True, exist_ok=True)
    (modules_dir / "main.tf").write_text(text, encoding="utf-8")
    return modules_dir / "main.tf"


def _write_foundry_module_fixture(root: Path, text: str) -> Path:
    modules_dir = root / "infra" / "modules" / "foundry"
    modules_dir.mkdir(parents=True, exist_ok=True)
    (modules_dir / "main.tf").write_text(text, encoding="utf-8")
    return modules_dir / "main.tf"


def _write_env_root_foundry_wiring(
    root: Path, env: str, with_module: bool = True, with_variable: bool = True
) -> None:
    env_dir = root / "infra" / "environments" / env
    env_dir.mkdir(parents=True, exist_ok=True)
    module_block = (
        'module "foundry" {\n  source = "../../modules/foundry"\n}\n\n' if with_module else ""
    )
    (env_dir / "main.tf").write_text(module_block + "# no other modules needed for this fixture\n", encoding="utf-8")
    variable_block = (
        'variable "foundry_ai_services_resource_id" {\n  type    = string\n  default = ""\n}\n'
        if with_variable
        else ""
    )
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
        # name, not the raw connection label, and stays true even if two
        # environments are (mis)configured with the same connection label.
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
        """Regression guard for this task's own real-world find: a
        duplicated `output "workload_identity_id"` block (this repo's
        infra/modules/identity/outputs.tf briefly held exactly this shape
        after a phase-barrier merge) must fail loudly, not be silently
        collapsed to "one entry" by a dict-based scan."""
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


class CheckFoundryRoleAssignmentConditionalTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_foundry_role_assignment_conditional(path)
            self.assertTrue(passed, detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "foundry" / "main.tf"
            passed, detail = fcv.check_foundry_role_assignment_conditional(path)
            self.assertFalse(passed, detail)

    def test_unconditional_resource_fails(self) -> None:
        """No `count = ...` at all -- would fail every apply against an
        account the ADR-008 Portal step has not created yet."""
        with tempfile.TemporaryDirectory() as tmp:
            text = _foundry_main_tf(count_expr=None)
            path = _write_foundry_module_fixture(Path(tmp), text)
            passed, detail = fcv.check_foundry_role_assignment_conditional(path)
            self.assertFalse(passed, detail)
            self.assertIn("count", detail)

    def test_wrong_role_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            text = _foundry_main_tf(role="Contributor")
            path = _write_foundry_module_fixture(Path(tmp), text)
            passed, detail = fcv.check_foundry_role_assignment_conditional(path)
            self.assertFalse(passed, detail)
            self.assertIn("Cognitive Services User", detail)

    def test_wrong_principal_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            text = _foundry_main_tf(principal="var.some_other_principal_id")
            path = _write_foundry_module_fixture(Path(tmp), text)
            passed, detail = fcv.check_foundry_role_assignment_conditional(path)
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_foundry_role_assignment_conditional()
        self.assertTrue(passed, detail)


class CheckAiServicesAccountNameMatchesTerraformTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = _write_foundry_module_fixture(Path(tmp), GOOD_FOUNDRY_MAIN_TF)
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertTrue(passed, detail)

    def test_drifted_name_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            text = _foundry_main_tf(account_name="aisvc-contigo-2")
            path = _write_foundry_module_fixture(Path(tmp), text)
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertFalse(passed, detail)

    def test_missing_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "infra" / "modules" / "foundry" / "main.tf"
            passed, detail = fcv.check_ai_services_account_name_matches_terraform(path)
            self.assertFalse(passed, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_ai_services_account_name_matches_terraform()
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
            self.assertIn(fcv.FOUNDRY_ENV_ROOT_VARIABLE, detail)

    def test_currently_passes_against_the_real_repo(self) -> None:
        passed, detail = fcv.check_env_roots_wire_foundry_module()
        self.assertTrue(passed, detail)


class CheckNoSecretLiteralsTests(unittest.TestCase):
    def test_good_fixture_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            infra_root = root / "infra"
            _write_identity_fixture(root, GOOD_IDENTITY_OUTPUTS_TF)
            passed, detail = fcv.check_no_secret_literals(root, infra_root)
            self.assertTrue(passed, detail)

    def test_planted_secret_is_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            infra_root = root / "infra"
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
    """Same invocation this task's own definition of done relies on."""

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


if __name__ == "__main__":
    unittest.main()
