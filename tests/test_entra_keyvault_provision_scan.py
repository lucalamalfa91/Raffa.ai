"""Unit tests for scripts/entra_keyvault_provision_scan.py (task E01/F02/US04/T01).

Same shape as tests/test_dev_outputs_verify.py: pure-parser unit tests
against hand-written fixtures, `check_*` against a deliberately-good
synthetic fixture tree and one deliberately-broken tree per failure mode
this scan exists to catch, then two end-to-end proofs against this actual
working tree (`run_all_checks()` directly, and the script itself as a
subprocess -- the same invocation this task's own definition of done
relies on).

The "good" identity main.tf is assembled from named sub-block constants
(API_APPLICATION_BLOCK, PUBLIC_CLIENT_APPLICATION_BLOCK, ...) so a broken
fixture can remove/replace one whole, brace-balanced chunk at a time via a
plain string `.replace()` -- never an index slice that could leave a
dangling unmatched brace behind for the brace-counting extractor to trip
over.

Run:
    python tests/test_entra_keyvault_provision_scan.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import entra_keyvault_provision_scan as eks  # noqa: E402

HEADER_BLOCK = (
    'data "azuread_client_config" "current" {}\n'
    "\n"
    "locals {\n"
    "  tags = {\n"
    '    project = "raffa"\n'
    "    env     = var.environment\n"
    "  }\n"
    "\n"
    '  web_redirect_uri = var.web_redirect_uri\n'
    "}\n"
    "\n"
    'resource "azurerm_user_assigned_identity" "workload" {\n'
    '  name                = "id-raffa-${var.environment}-workload"\n'
    "  location            = var.location\n"
    "  resource_group_name = var.resource_group_name\n"
    "\n"
    "  tags = local.tags\n"
    "}\n"
    "\n"
    'resource "random_uuid" "scope_read" {}\n'
    'resource "random_uuid" "scope_write" {}\n'
    "\n"
)

READ_SCOPE_BLOCK = (
    "    oauth2_permission_scope {\n"
    "      id                         = random_uuid.scope_read.result\n"
    '      value                      = "Raffa.Read"\n'
    '      type                       = "User"\n'
    "      enabled                    = true\n"
    '      admin_consent_description  = "Allow the app to read the signed-in user\'s Raffa data."\n'
    '      admin_consent_display_name = "Read Raffa data"\n'
    "    }\n"
)

WRITE_SCOPE_BLOCK = (
    "    oauth2_permission_scope {\n"
    "      id                         = random_uuid.scope_write.result\n"
    '      value                      = "Raffa.Write"\n'
    '      type                       = "User"\n'
    "      enabled                    = true\n"
    '      admin_consent_description  = "Allow the app to write the signed-in user\'s Raffa data."\n'
    '      admin_consent_display_name = "Write Raffa data"\n'
    "    }\n"
)

API_APPLICATION_BLOCK = (
    'resource "azuread_application" "api" {\n'
    '  display_name     = "raffa-${var.environment}-api"\n'
    '  sign_in_audience = "AzureADMyOrg"\n'
    '  identifier_uris  = ["api://raffa-${var.environment}-api"]\n'
    "\n"
    "  api {\n"
    "    requested_access_token_version = 2\n"
    "\n" + READ_SCOPE_BLOCK + "\n" + WRITE_SCOPE_BLOCK + "  }\n"
    "\n"
    '  tags = ["project:raffa", "env:${var.environment}"]\n'
    "}\n"
    "\n"
    'resource "azuread_service_principal" "api" {\n'
    "  client_id = azuread_application.api.client_id\n"
    "}\n"
    "\n"
)

SPA_BLOCK = "  single_page_application {\n    redirect_uris = [local.web_redirect_uri]\n  }\n"
NATIVE_PUBLIC_CLIENT_BLOCK = '  public_client {\n    redirect_uris = ["raffa://callback"]\n  }\n'

REQUIRED_RESOURCE_ACCESS_BLOCK = (
    "  required_resource_access {\n"
    "    resource_app_id = azuread_application.api.client_id\n"
    "\n"
    "    resource_access {\n"
    '      id   = azuread_application.api.oauth2_permission_scope_ids["Raffa.Read"]\n'
    '      type = "Scope"\n'
    "    }\n"
    "\n"
    "    resource_access {\n"
    '      id   = azuread_application.api.oauth2_permission_scope_ids["Raffa.Write"]\n'
    '      type = "Scope"\n'
    "    }\n"
    "  }\n"
)

PUBLIC_CLIENT_APPLICATION_BLOCK = (
    'resource "azuread_application" "public_client" {\n'
    '  display_name     = "raffa-${var.environment}-public-client"\n'
    '  sign_in_audience = "AzureADMyOrg"\n'
    "\n" + SPA_BLOCK + "\n" + NATIVE_PUBLIC_CLIENT_BLOCK + "\n" + REQUIRED_RESOURCE_ACCESS_BLOCK + "\n"
    '  tags = ["project:raffa", "env:${var.environment}"]\n'
    "}\n"
    "\n"
    'resource "azuread_service_principal" "public_client" {\n'
    "  client_id = azuread_application.public_client.client_id\n"
    "}\n"
    "\n"
)

PRE_AUTHORIZED_BLOCK = (
    'resource "azuread_application_pre_authorized" "public_client" {\n'
    "  application_id       = azuread_application.api.id\n"
    "  authorized_client_id = azuread_application.public_client.client_id\n"
    "\n"
    "  permission_ids = [\n"
    '    azuread_application.api.oauth2_permission_scope_ids["Raffa.Read"],\n'
    '    azuread_application.api.oauth2_permission_scope_ids["Raffa.Write"],\n'
    "  ]\n"
    "}\n"
)

GOOD_IDENTITY_MAIN_TF = HEADER_BLOCK + API_APPLICATION_BLOCK + PUBLIC_CLIENT_APPLICATION_BLOCK + PRE_AUTHORIZED_BLOCK

GOOD_KEYVAULT_MAIN_TF = (
    'data "azurerm_client_config" "current" {}\n'
    "\n"
    "locals {\n"
    "  tags = {\n"
    '    project = "raffa"\n'
    "    env     = var.environment\n"
    "  }\n"
    "}\n"
    "\n"
    'resource "random_string" "suffix" {\n'
    "  length  = 6\n"
    "  special = false\n"
    "}\n"
    "\n"
    'resource "azurerm_key_vault" "this" {\n'
    '  name                       = "kv-raffa-${var.environment}-${random_string.suffix.result}"\n'
    "  location                   = var.location\n"
    "  resource_group_name        = var.resource_group_name\n"
    "  tenant_id                  = data.azurerm_client_config.current.tenant_id\n"
    '  sku_name                   = "standard"\n'
    "  rbac_authorization_enabled = true\n"
    "\n"
    "  tags = local.tags\n"
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
    '  description = "principal id"\n'
    "  type        = string\n"
    "}\n"
)


def _good_identity_outputs_tf() -> str:
    return "\n".join(
        f'output "{name}" {{\n  value = {expr}\n}}\n' for name, expr in eks.EXPECTED_IDENTITY_OUTPUTS.items()
    )


def _good_env_main_tf() -> str:
    return (
        'module "identity" {\n'
        '  source = "../../modules/identity"\n'
        "}\n"
        "\n"
        'module "keyvault" {\n'
        '  source = "../../modules/keyvault"\n'
        "\n"
        "  environment            = local.environment\n"
        "  resource_group_name    = azurerm_resource_group.this.name\n"
        "  workload_principal_id = module.identity.workload_principal_id\n"
        "}\n"
    )


def _write_fixture_tree(root: Path, **overrides) -> tuple:
    infra = root / "infra"
    identity_dir = infra / "modules" / "identity"
    keyvault_dir = infra / "modules" / "keyvault"
    environments_root = infra / "environments"
    identity_dir.mkdir(parents=True, exist_ok=True)
    keyvault_dir.mkdir(parents=True, exist_ok=True)

    (identity_dir / "main.tf").write_text(overrides.get("identity_main") or GOOD_IDENTITY_MAIN_TF, encoding="utf-8")
    (identity_dir / "outputs.tf").write_text(
        overrides.get("identity_outputs") or _good_identity_outputs_tf(), encoding="utf-8"
    )
    (identity_dir / "variables.tf").write_text('variable "environment" {\n  type = string\n}\n', encoding="utf-8")

    (keyvault_dir / "main.tf").write_text(overrides.get("keyvault_main") or GOOD_KEYVAULT_MAIN_TF, encoding="utf-8")
    (keyvault_dir / "variables.tf").write_text(
        overrides.get("keyvault_variables") or GOOD_KEYVAULT_VARIABLES_TF, encoding="utf-8"
    )
    (keyvault_dir / "outputs.tf").write_text('output "id" {\n  value = azurerm_key_vault.this.id\n}\n', encoding="utf-8")

    env_main_overrides = overrides.get("env_main_overrides") or {}
    for env in eks.ENVS:
        env_dir = environments_root / env
        env_dir.mkdir(parents=True, exist_ok=True)
        (env_dir / "main.tf").write_text(env_main_overrides.get(env, _good_env_main_tf()), encoding="utf-8")

    return identity_dir, keyvault_dir, environments_root, root, infra


class SanityCheckGoodFixtureIsWellFormedTests(unittest.TestCase):
    """The composed GOOD_IDENTITY_MAIN_TF must itself be brace-balanced --
    guards the fixture-composition helpers above, not production code."""

    def test_brace_balance(self) -> None:
        self.assertEqual(GOOD_IDENTITY_MAIN_TF.count("{"), GOOD_IDENTITY_MAIN_TF.count("}"))


class FindResourceNamesOfTypeTests(unittest.TestCase):
    def test_finds_all_names(self) -> None:
        text = 'resource "azuread_application" "api" {\n}\nresource "azuread_application" "public_client" {\n}\n'
        self.assertEqual(eks.find_resource_names_of_type(text, "azuread_application"), {"api", "public_client"})

    def test_does_not_bleed_into_longer_type_name(self) -> None:
        # "azuread_application" must not match "azuread_application_pre_authorized".
        text = 'resource "azuread_application_pre_authorized" "public_client" {\n}\n'
        self.assertEqual(eks.find_resource_names_of_type(text, "azuread_application"), set())
        self.assertEqual(
            eks.find_resource_names_of_type(text, "azuread_application_pre_authorized"), {"public_client"}
        )

    def test_comment_only_mention_is_not_found(self) -> None:
        text = '# resource "azuread_application" "fake" {}\nresource "azuread_application" "api" {\n}\n'
        self.assertEqual(eks.find_resource_names_of_type(text, "azuread_application"), {"api"})


class FindAllBlocksTests(unittest.TestCase):
    def test_finds_every_occurrence_in_order(self) -> None:
        text = 'oauth2_permission_scope {\n  value = "a"\n}\noauth2_permission_scope {\n  value = "b"\n}\n'
        bodies = eks.find_all_blocks(text, r"oauth2_permission_scope")
        self.assertEqual(len(bodies), 2)
        self.assertIn('value = "a"', bodies[0])
        self.assertIn('value = "b"', bodies[1])


class GoodFixtureTreeTests(unittest.TestCase):
    """Every check_* must pass against a deliberately correct fixture tree."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.identity_dir, self.keyvault_dir, self.environments_root, self.repo_root, self.infra_root = (
            _write_fixture_tree(Path(self._tmp.name))
        )

    def test_all_checks_pass(self) -> None:
        for name, (passed, detail) in eks.run_all_checks(
            self.identity_dir, self.keyvault_dir, self.environments_root, self.repo_root, self.infra_root
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

    def test_only_one_app_registration_fails(self) -> None:
        bad = HEADER_BLOCK + API_APPLICATION_BLOCK + PRE_AUTHORIZED_BLOCK.replace(
            "azuread_application.public_client.client_id", "azuread_application.api.client_id"
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_two_app_registrations(identity_dir)
        self.assertFalse(passed, detail)

    def test_missing_scope_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace('value                      = "Raffa.Write"', 'value                      = "Raffa.Delete"')
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_api_scopes(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("Raffa.Write", detail)

    def test_scope_wrong_type_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(READ_SCOPE_BLOCK, READ_SCOPE_BLOCK.replace('"User"', '"Admin"', 1))
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_api_scopes(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("Raffa.Read", detail)

    def test_scope_not_enabled_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(WRITE_SCOPE_BLOCK, WRITE_SCOPE_BLOCK.replace("enabled                    = true", "enabled                    = false"))
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_api_scopes(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("Raffa.Write", detail)

    def test_api_password_block_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            API_APPLICATION_BLOCK,
            API_APPLICATION_BLOCK.replace(
                '  tags = ["project:raffa", "env:${var.environment}"]\n}\n',
                '  password {\n    display_name = "oops"\n  }\n\n'
                '  tags = ["project:raffa", "env:${var.environment}"]\n}\n',
                1,
            ),
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_api_no_secret(identity_dir)
        self.assertFalse(passed, detail)

    def test_public_client_missing_native_redirect_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(NATIVE_PUBLIC_CLIENT_BLOCK + "\n", "")
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_public_client_pkce(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("public_client {}", detail)

    def test_public_client_wrong_native_uri_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            NATIVE_PUBLIC_CLIENT_BLOCK,
            NATIVE_PUBLIC_CLIENT_BLOCK.replace("raffa://callback", "https://example.com/callback"),
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_public_client_pkce(identity_dir)
        self.assertFalse(passed, detail)

    def test_public_client_password_block_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            PUBLIC_CLIENT_APPLICATION_BLOCK,
            PUBLIC_CLIENT_APPLICATION_BLOCK.replace(
                '  tags = ["project:raffa", "env:${var.environment}"]\n}\n',
                '  password {\n    display_name = "oops"\n  }\n\n'
                '  tags = ["project:raffa", "env:${var.environment}"]\n}\n',
                1,
            ),
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_public_client_pkce(identity_dir)
        self.assertFalse(passed, detail)

    def test_missing_required_resource_access_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(REQUIRED_RESOURCE_ACCESS_BLOCK + "\n", "")
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_required_resource_access(identity_dir)
        self.assertFalse(passed, detail)

    def test_required_resource_access_wrong_resource_app_id_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            "resource_app_id = azuread_application.api.client_id",
            "resource_app_id = azuread_application.public_client.client_id",
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_required_resource_access(identity_dir)
        self.assertFalse(passed, detail)

    def test_pre_authorization_missing_permission_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            '    azuread_application.api.oauth2_permission_scope_ids["Raffa.Write"],\n', ""
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_pre_authorization(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("Raffa.Write", detail)

    def test_pre_authorization_wrong_application_id_fails(self) -> None:
        bad = GOOD_IDENTITY_MAIN_TF.replace(
            "  application_id       = azuread_application.api.id\n",
            "  application_id       = azuread_application.public_client.id\n",
        )
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_pre_authorization(identity_dir)
        self.assertFalse(passed, detail)

    def test_missing_pre_authorization_resource_fails(self) -> None:
        bad = HEADER_BLOCK + API_APPLICATION_BLOCK + PUBLIC_CLIENT_APPLICATION_BLOCK
        identity_dir, *_ = self._tree(identity_main=bad)
        passed, detail = eks.check_pre_authorization(identity_dir)
        self.assertFalse(passed, detail)

    def test_missing_identity_output_fails(self) -> None:
        bad = _good_identity_outputs_tf().replace('output "issuer"', 'output "issuer_renamed_by_mistake"')
        identity_dir, *_ = self._tree(identity_outputs=bad)
        passed, detail = eks.check_identity_outputs(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("issuer", detail)

    def test_identity_output_wrong_value_fails(self) -> None:
        bad = _good_identity_outputs_tf().replace(
            "azurerm_user_assigned_identity.workload.principal_id", "azurerm_user_assigned_identity.workload.id"
        )
        identity_dir, *_ = self._tree(identity_outputs=bad)
        passed, detail = eks.check_identity_outputs(identity_dir)
        self.assertFalse(passed, detail)
        self.assertIn("workload_principal_id", detail)

    def test_two_key_vaults_in_one_module_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF + '\nresource "azurerm_key_vault" "extra" {\n  name = "kv-oops"\n}\n'
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = eks.check_one_key_vault(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_role_assignment_scoped_to_wrong_vault_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF.replace(
            "scope                            = azurerm_key_vault.this.id",
            "scope                            = var.other_vault_id",
        )
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = eks.check_keyvault_role_assignment(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_role_assignment_wrong_role_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF.replace("Key Vault Secrets User", "Key Vault Administrator")
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = eks.check_keyvault_role_assignment(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_role_assignment_hardcoded_principal_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF.replace(
            "principal_id                     = var.workload_principal_id",
            'principal_id                     = "11111111-1111-1111-1111-111111111111"',
        )
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = eks.check_keyvault_role_assignment(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_missing_role_assignment_fails(self) -> None:
        bad = GOOD_KEYVAULT_MAIN_TF.split('resource "azurerm_role_assignment"')[0]
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_main=bad)
        passed, detail = eks.check_keyvault_role_assignment(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_variable_with_default_fails(self) -> None:
        bad = GOOD_KEYVAULT_VARIABLES_TF.replace("  type        = string\n}\n", "  type        = string\n  default     = null\n}\n")
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_variables=bad)
        passed, detail = eks.check_keyvault_variable(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_missing_variable_fails(self) -> None:
        bad = 'variable "environment" {\n  type = string\n}\n'
        _identity_dir, keyvault_dir, *_ = self._tree(keyvault_variables=bad)
        passed, detail = eks.check_keyvault_variable(keyvault_dir)
        self.assertFalse(passed, detail)

    def test_env_root_hardcoded_principal_fails(self) -> None:
        bad_dev = _good_env_main_tf().replace(
            "workload_principal_id = module.identity.workload_principal_id",
            'workload_principal_id = "11111111-1111-1111-1111-111111111111"',
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = eks.check_env_root_wires_own_identity("dev", environments_root)
        self.assertFalse(passed, detail)

    def test_env_root_cross_env_reference_fails(self) -> None:
        # A hypothetical "module.dev_identity" reference in demo/main.tf (never
        # its own module.identity) must fail -- simulates a copy/paste that
        # crossed the isolation boundary.
        bad_demo = _good_env_main_tf().replace(
            "workload_principal_id = module.identity.workload_principal_id",
            "workload_principal_id = module.dev_identity.workload_principal_id",
        )
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"demo": bad_demo})
        passed, detail = eks.check_env_root_wires_own_identity("demo", environments_root)
        self.assertFalse(passed, detail)

    def test_env_root_missing_keyvault_module_fails(self) -> None:
        bad_dev = 'module "identity" {\n  source = "../../modules/identity"\n}\n'
        *_rest, environments_root, _repo_root, _infra_root = self._tree(env_main_overrides={"dev": bad_dev})
        passed, detail = eks.check_env_root_wires_own_identity("dev", environments_root)
        self.assertFalse(passed, detail)

    def test_secret_literal_fails(self) -> None:
        bad = GOOD_KEYVAULT_VARIABLES_TF + "\n# AccountKey=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ12==\n"
        _identity_dir, _keyvault_dir, _environments_root, repo_root, infra_root = self._tree(keyvault_variables=bad)
        passed, detail = eks.check_no_secret_literals(repo_root, infra_root)
        self.assertFalse(passed, detail)


class RealRepoScanTests(unittest.TestCase):
    """Same invocation this task's own definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in eks.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "entra_keyvault_provision_scan.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
