"""Unit tests for scripts/keyvault_scope_grants_check.py (task E01/F02/US04/T02).

Same shape as tests/test_entra_keyvault_provision_scan.py: pure-parser unit
tests against hand-written fixtures, `check_*` against a deliberately-good
synthetic fixture tree and one deliberately-broken tree per failure mode
this scan exists to catch, then two end-to-end proofs against this actual
working tree (`run_all_checks()` directly, and the script itself as a
subprocess -- the same invocation this task's own definition of done
relies on).

The "good" containerapps main.tf is assembled from named sub-block
constants (HEADER_BLOCK, API_APP_BLOCK, WORKER_APP_BLOCK, IDENTITY_BLOCK)
so a broken fixture can remove/replace one whole, brace-balanced chunk at
a time via a plain string `.replace()` -- never an index slice that could
leave a dangling unmatched brace behind for the brace-counting extractor
to trip over.

Run:
    python tests/test_keyvault_scope_grants_check.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import keyvault_scope_grants_check as ksc  # noqa: E402

GOOD_IDENTITY_OUTPUTS_TF = (
    'output "workload_principal_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.principal_id\n"
    "}\n"
    "\n"
    'output "workload_identity_id" {\n'
    "  value = azurerm_user_assigned_identity.workload.id\n"
    "}\n"
)

GOOD_KEYVAULT_MAIN_TF = (
    'resource "azurerm_key_vault" "this" {\n'
    '  name                       = "kv-raffa-${var.environment}"\n'
    "  rbac_authorization_enabled = true\n"
    "}\n"
    "\n"
    'resource "azurerm_role_assignment" "workload_secrets_user" {\n'
    "  scope                            = azurerm_key_vault.this.id\n"
    '  role_definition_name             = "Key Vault Secrets User"\n'
    "  principal_id                     = var.workload_principal_id\n"
    "  skip_service_principal_aad_check = true\n"
    "}\n"
)

GOOD_KEYVAULT_VARIABLES_TF = (
    'variable "environment" {\n'
    "  type = string\n"
    "}\n"
    "\n"
    'variable "workload_principal_id" {\n'
    "  type = string\n"
    "}\n"
)

GOOD_CONTAINERAPPS_VARIABLES_TF = (
    'variable "environment" {\n'
    "  type = string\n"
    "}\n"
    "\n"
    'variable "workload_identity_id" {\n'
    '  description = "identity resource id"\n'
    "  type        = string\n"
    "}\n"
)

IDENTITY_BLOCK = (
    "  identity {\n"
    '    type         = "UserAssigned"\n'
    "    identity_ids = [var.workload_identity_id]\n"
    "  }\n"
)

HEADER_BLOCK = (
    'resource "azurerm_container_app_environment" "this" {\n'
    '  name = "cae-raffa-${var.environment}"\n'
    "}\n"
    "\n"
)

API_APP_BLOCK = (
    'resource "azurerm_container_app" "api" {\n'
    '  name = "ca-raffa-${var.environment}-api"\n'
    "\n" + IDENTITY_BLOCK + "\n"
    "  template {\n"
    "    min_replicas = 0\n"
    "  }\n"
    "}\n"
    "\n"
)

WORKER_APP_BLOCK = (
    'resource "azurerm_container_app" "worker" {\n'
    '  name = "ca-raffa-${var.environment}-worker"\n'
    "\n" + IDENTITY_BLOCK + "\n"
    "  template {\n"
    "    min_replicas = 0\n"
    "  }\n"
    "}\n"
)

GOOD_CONTAINERAPPS_MAIN_TF = HEADER_BLOCK + API_APP_BLOCK + WORKER_APP_BLOCK

GOOD_ENV_MAIN_TF = (
    'module "identity" {\n'
    '  source = "../../modules/identity"\n'
    "}\n"
    "\n"
    'module "keyvault" {\n'
    '  source = "../../modules/keyvault"\n'
    "\n"
    "  workload_principal_id = module.identity.workload_principal_id\n"
    "}\n"
    "\n"
    'module "containerapps" {\n'
    '  source = "../../modules/containerapps"\n'
    "\n"
    "  workload_identity_id = module.identity.workload_identity_id\n"
    "}\n"
)


def _write_fixture_tree(root: Path, **overrides) -> tuple:
    infra = root / "infra"
    identity_dir = infra / "modules" / "identity"
    keyvault_dir = infra / "modules" / "keyvault"
    containerapps_dir = infra / "modules" / "containerapps"
    environments_root = infra / "environments"
    identity_dir.mkdir(parents=True, exist_ok=True)
    keyvault_dir.mkdir(parents=True, exist_ok=True)
    containerapps_dir.mkdir(parents=True, exist_ok=True)

    (identity_dir / "outputs.tf").write_text(
        overrides.get("identity_outputs") or GOOD_IDENTITY_OUTPUTS_TF, encoding="utf-8"
    )

    (keyvault_dir / "main.tf").write_text(overrides.get("keyvault_main") or GOOD_KEYVAULT_MAIN_TF, encoding="utf-8")
    (keyvault_dir / "variables.tf").write_text(
        overrides.get("keyvault_variables") or GOOD_KEYVAULT_VARIABLES_TF, encoding="utf-8"
    )

    (containerapps_dir / "main.tf").write_text(
        overrides.get("containerapps_main") or GOOD_CONTAINERAPPS_MAIN_TF, encoding="utf-8"
    )
    (containerapps_dir / "variables.tf").write_text(
        overrides.get("containerapps_variables") or GOOD_CONTAINERAPPS_VARIABLES_TF, encoding="utf-8"
    )

    env_main_overrides = overrides.get("env_main_overrides") or {}
    for env in ksc.ENVS:
        env_dir = environments_root / env
        env_dir.mkdir(parents=True, exist_ok=True)
        (env_dir / "main.tf").write_text(env_main_overrides.get(env, GOOD_ENV_MAIN_TF), encoding="utf-8")

    return identity_dir, keyvault_dir, containerapps_dir, environments_root, root, infra


class SanityCheckGoodFixtureIsWellFormedTests(unittest.TestCase):
    """The composed GOOD_CONTAINERAPPS_MAIN_TF must itself be brace-balanced --
    guards the fixture-composition helpers above, not production code."""

    def test_brace_balance(self) -> None:
        self.assertEqual(GOOD_CONTAINERAPPS_MAIN_TF.count("{"), GOOD_CONTAINERAPPS_MAIN_TF.count("}"))


class GoodFixtureTreeTests(unittest.TestCase):
    """Every check_* must pass against a deliberately correct fixture tree."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        (
            self.identity_dir,
            self.keyvault_dir,
            self.containerapps_dir,
            self.environments_root,
            self.repo_root,
            self.infra_root,
        ) = _write_fixture_tree(Path(self._tmp.name))

    def test_all_checks_pass(self) -> None:
        for name, (passed, detail) in ksc.run_all_checks(
            self.identity_dir,
            self.keyvault_dir,
            self.containerapps_dir,
            self.environments_root,
            self.repo_root,
            self.infra_root,
        ):
            self.assertTrue(passed, f"{name}: {detail}")


class BrokenFixtureTreeTests(unittest.TestCase):
    """One deliberately-broken fixture tree per failure mode this scan must catch.

    Every mutation below removes or swaps one whole named block constant
    (never an index slice), so the result stays brace-balanced.
    """

    def _tree(self, **overrides) -> tuple:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        return _write_fixture_tree(Path(tmp.name), **overrides)

    # -- containerapps variable -------------------------------------------------

    def test_missing_containerapps_variable_fails(self) -> None:
        bad = 'variable "environment" {\n  type = string\n}\n'
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_variables=bad)
        passed, detail = ksc.check_containerapps_identity_variable(containerapps_dir)
        self.assertFalse(passed, detail)

    def test_containerapps_variable_with_default_fails(self) -> None:
        bad = GOOD_CONTAINERAPPS_VARIABLES_TF.replace(
            "  type        = string\n}\n", "  type        = string\n  default     = null\n}\n"
        )
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_variables=bad)
        passed, detail = ksc.check_containerapps_identity_variable(containerapps_dir)
        self.assertFalse(passed, detail)

    # -- containerapps identity assignment --------------------------------------

    def test_missing_identity_block_on_api_fails(self) -> None:
        bad = HEADER_BLOCK + API_APP_BLOCK.replace(IDENTITY_BLOCK + "\n", "") + WORKER_APP_BLOCK
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_main=bad)
        passed, detail = ksc.check_containerapps_identity_assignment(containerapps_dir)
        self.assertFalse(passed, detail)
        self.assertIn("api", detail)

    def test_missing_identity_block_on_worker_fails(self) -> None:
        bad = HEADER_BLOCK + API_APP_BLOCK + WORKER_APP_BLOCK.replace(IDENTITY_BLOCK + "\n", "")
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_main=bad)
        passed, detail = ksc.check_containerapps_identity_assignment(containerapps_dir)
        self.assertFalse(passed, detail)
        self.assertIn("worker", detail)

    def test_wrong_identity_type_fails(self) -> None:
        bad = GOOD_CONTAINERAPPS_MAIN_TF.replace('"UserAssigned"', '"SystemAssigned"', 1)
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_main=bad)
        passed, detail = ksc.check_containerapps_identity_assignment(containerapps_dir)
        self.assertFalse(passed, detail)

    def test_hardcoded_identity_ids_fails(self) -> None:
        bad = GOOD_CONTAINERAPPS_MAIN_TF.replace(
            "identity_ids = [var.workload_identity_id]",
            'identity_ids = ["/subscriptions/x/resourceGroups/y/providers/Microsoft.ManagedIdentity/userAssignedIdentities/hardcoded"]',
            1,
        )
        _identity_dir, _keyvault_dir, containerapps_dir, *_ = self._tree(containerapps_main=bad)
        passed, detail = ksc.check_containerapps_identity_assignment(containerapps_dir)
        self.assertFalse(passed, detail)

    # -- identity module outputs -------------------------------------------------

    def test_missing_identity_output_fails(self) -> None:
        bad = GOOD_IDENTITY_OUTPUTS_TF.split('output "workload_identity_id"')[0]
        identity_dir, *_ = self._tree(identity_outputs=bad)
        passed, detail = ksc.check_identity_module_exposes_identity_id(identity_dir)
        self.assertFalse(passed, detail)

    def test_identity_output_wrong_value_fails(self) -> None:
        bad = GOOD_IDENTITY_OUTPUTS_TF.replace(
            "azurerm_user_assigned_identity.workload.id",
            "azurerm_user_assigned_identity.workload.principal_id",
        )
        identity_dir, *_ = self._tree(identity_outputs=bad)
        passed, detail = ksc.check_identity_module_exposes_identity_id(identity_dir)
        self.assertFalse(passed, detail)

    # -- env root wiring to containerapps -----------------------------------------

    def test_env_root_missing_containerapps_attribute_fails(self) -> None:
        bad_dev = GOOD_ENV_MAIN_TF.replace("  workload_identity_id = module.identity.workload_identity_id\n", "")
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = ksc.check_env_root_wires_containerapps_identity("dev", environments_root)
        self.assertFalse(passed, detail)

    def test_env_root_hardcoded_containerapps_identity_fails(self) -> None:
        bad_dev = GOOD_ENV_MAIN_TF.replace(
            "workload_identity_id = module.identity.workload_identity_id",
            'workload_identity_id = "11111111-1111-1111-1111-111111111111"',
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = ksc.check_env_root_wires_containerapps_identity("dev", environments_root)
        self.assertFalse(passed, detail)

    def test_env_root_cross_env_containerapps_reference_fails(self) -> None:
        # A hypothetical "module.other_identity" reference in demo/main.tf
        # (never its own module.identity) must fail -- simulates a
        # copy/paste that crossed the isolation boundary.
        bad_demo = GOOD_ENV_MAIN_TF.replace(
            "workload_identity_id = module.identity.workload_identity_id",
            "workload_identity_id = module.other_identity.workload_identity_id",
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"demo": bad_demo})
        passed, detail = ksc.check_env_root_wires_containerapps_identity("demo", environments_root)
        self.assertFalse(passed, detail)

    def test_env_root_missing_containerapps_module_fails(self) -> None:
        bad_dev = 'module "identity" {\n  source = "../../modules/identity"\n}\n'
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = ksc.check_env_root_wires_containerapps_identity("dev", environments_root)
        self.assertFalse(passed, detail)

    # -- grant + assignment share one identity instance ---------------------------

    def test_grant_and_assignment_mismatched_instances_fails(self) -> None:
        bad_dev = GOOD_ENV_MAIN_TF.replace(
            "workload_identity_id = module.identity.workload_identity_id",
            "workload_identity_id = module.other.workload_identity_id",
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = ksc.check_grant_and_assignment_share_identity_instance("dev", environments_root)
        self.assertFalse(passed, detail)

    def test_grant_and_assignment_same_wrong_instance_fails(self) -> None:
        bad_dev = GOOD_ENV_MAIN_TF.replace(
            "workload_principal_id = module.identity.workload_principal_id",
            "workload_principal_id = module.other.workload_principal_id",
        ).replace(
            "workload_identity_id = module.identity.workload_identity_id",
            "workload_identity_id = module.other.workload_identity_id",
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = ksc.check_grant_and_assignment_share_identity_instance("dev", environments_root)
        self.assertFalse(passed, detail)

    # -- keyvault grant re-check + hygiene ----------------------------------------

    def test_role_assignment_wrong_role_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF.replace("Key Vault Secrets User", "Key Vault Administrator")
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = ksc.check_keyvault_grant_still_scoped_to_own_vault(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_access_policy_block_present_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF + '\naccess_policy {\n  tenant_id = "x"\n}\n'
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = ksc.check_no_access_policy_block(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_secret_literal_fails(self) -> None:
        bad = GOOD_CONTAINERAPPS_VARIABLES_TF + "\n# AccountKey=" + ("Z" * 40) + "==\n"
        *_rest, repo_root, infra_root = self._tree(containerapps_variables=bad)
        passed, detail = ksc.check_no_secret_literals(repo_root, infra_root)
        self.assertFalse(passed, detail)


class RealRepoScanTests(unittest.TestCase):
    """Same invocation this task's own definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in ksc.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "keyvault_scope_grants_check.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
