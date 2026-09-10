"""Unit tests for src/ci_workflow_auth.py (task E01/F03/US01/T02, produces
`ci-workflow-auth`).

RealRepoScanTests is the named test that proves the produced artifact:
it runs every check against the actual on-disk
`.github/actions/azure-login/action.yml` and `modules/identity/outputs.tf`
(T01, `ci-azure-auth`) and asserts every one passes.
"""

from __future__ import annotations

import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from textwrap import dedent

TESTS_ROOT = Path(__file__).resolve().parent
REPO_ROOT = TESTS_ROOT.parent
SRC_ROOT = REPO_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

import ci_workflow_auth as cwa  # noqa: E402


VALID_ACTION_YAML = dedent(
    """\
    # ------------------------------------------------------------------
    # Composite action: Azure OIDC Login
    #
    #   permissions:
    #     id-token: write          # required for OIDC token request
    #     contents: read
    #
    # No client secret, no AZURE_CREDENTIALS (ADR-015 AC-2, AC-3).
    # ------------------------------------------------------------------

    name: 'Azure OIDC Login'
    description: >-
      Authenticate GitHub Actions to Azure via OIDC federated credentials.

    inputs:
      client-id:
        description: 'Entra AD application (client) ID of the service principal.'
        required: true
      tenant-id:
        description: 'Entra ID tenant ID.'
        required: true
      subscription-id:
        description: 'Azure subscription ID.'
        required: true

    runs:
      using: 'composite'
      steps:
        - name: Azure Login via OIDC federation
          uses: azure/login@v2
          with:
            client-id: ${{ inputs.client-id }}
            tenant-id: ${{ inputs.tenant-id }}
            subscription-id: ${{ inputs.subscription-id }}
    """
)

VALID_IDENTITY_OUTPUTS_TF = dedent(
    """\
    output "client_id" {
      value = azuread_application.deploy.client_id
    }

    output "tenant_id" {
      value = data.azuread_client_config.current.tenant_id
    }

    output "subscription_id" {
      value = data.azurerm_subscription.current.subscription_id
    }

    output "service_principal_object_id" {
      value = azuread_service_principal.deploy.object_id
    }
    """
)


class ParseActionYamlTests(unittest.TestCase):
    def test_parses_inputs_using_step_and_with(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        self.assertEqual(set(parsed["inputs"]), {"client-id", "tenant-id", "subscription-id"})
        self.assertEqual(parsed["using"], "composite")
        self.assertEqual(parsed["step_count"], 1)
        self.assertEqual(parsed["uses"], "azure/login@v2")
        self.assertEqual(
            parsed["with"],
            {
                "client-id": "${{ inputs.client-id }}",
                "tenant-id": "${{ inputs.tenant-id }}",
                "subscription-id": "${{ inputs.subscription-id }}",
            },
        )

    def test_required_flag_captured_per_input(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        for name in ("client-id", "tenant-id", "subscription-id"):
            self.assertEqual(parsed["inputs"][name]["required"], "true")

    def test_header_comment_text_does_not_leak_into_parsed_inputs(self):
        # The header comment literally contains the words "client secret"
        # and "AZURE_CREDENTIALS" -- parsing must not mistake that prose
        # for structural YAML.
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        self.assertNotIn("permissions", parsed["inputs"])
        self.assertEqual(len(parsed["inputs"]), 3)


class CheckActionFileExistsTests(unittest.TestCase):
    def test_pass_when_file_present(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "action.yml"
            path.write_text(VALID_ACTION_YAML, encoding="utf-8")
            passed, detail = cwa.check_action_file_exists(path)
            self.assertTrue(passed, detail)

    def test_fails_when_file_missing(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "does-not-exist.yml"
            passed, detail = cwa.check_action_file_exists(path)
            self.assertFalse(passed)
            self.assertIn("does not exist", detail)


class CheckCompositeSingleAzureLoginStepTests(unittest.TestCase):
    def test_pass_on_valid(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        passed, detail = cwa.check_composite_single_azure_login_step(parsed)
        self.assertTrue(passed, detail)

    def test_fails_when_not_composite(self):
        bad = VALID_ACTION_YAML.replace("using: 'composite'", "using: 'node20'")
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_composite_single_azure_login_step(parsed)
        self.assertFalse(passed)
        self.assertIn("node20", detail)

    def test_fails_when_action_not_pinned_to_a_version_tag(self):
        bad = VALID_ACTION_YAML.replace("uses: azure/login@v2", "uses: azure/login@main")
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_composite_single_azure_login_step(parsed)
        self.assertFalse(passed)
        self.assertIn("azure/login@main", detail)

    def test_fails_when_a_second_step_is_added(self):
        bad = VALID_ACTION_YAML.replace(
            "        subscription-id: ${{ inputs.subscription-id }}\n",
            "        subscription-id: ${{ inputs.subscription-id }}\n"
            "    - name: extra step\n"
            "      run: echo hi\n"
            "      shell: bash\n",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_composite_single_azure_login_step(parsed)
        self.assertFalse(passed)
        self.assertIn("2 step", detail)


class CheckInputsExactlyNonSecretThreeTests(unittest.TestCase):
    def test_pass_on_valid(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        passed, detail = cwa.check_inputs_exactly_non_secret_three(parsed)
        self.assertTrue(passed, detail)

    def test_fails_when_a_secret_input_is_added(self):
        bad = VALID_ACTION_YAML.replace(
            "  subscription-id:\n",
            "  client-secret:\n"
            "    description: 'nope'\n"
            "    required: false\n"
            "  subscription-id:\n",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_inputs_exactly_non_secret_three(parsed)
        self.assertFalse(passed)
        self.assertIn("client-secret", detail)

    def test_fails_when_an_input_is_missing(self):
        bad = VALID_ACTION_YAML.replace(
            "  subscription-id:\n"
            "    description: 'Azure subscription ID.'\n"
            "    required: true\n",
            "",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_inputs_exactly_non_secret_three(parsed)
        self.assertFalse(passed)
        self.assertIn("subscription-id", detail)


class CheckInputsAllRequiredTests(unittest.TestCase):
    def test_pass_on_valid(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        passed, detail = cwa.check_inputs_all_required(parsed)
        self.assertTrue(passed, detail)

    def test_fails_when_an_input_is_optional(self):
        bad = VALID_ACTION_YAML.replace(
            "  tenant-id:\n    description: 'Entra ID tenant ID.'\n    required: true\n",
            "  tenant-id:\n    description: 'Entra ID tenant ID.'\n    required: false\n",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_inputs_all_required(parsed)
        self.assertFalse(passed)
        self.assertIn("tenant-id", detail)


class CheckStepWiresInputsWithoutDriftTests(unittest.TestCase):
    def test_pass_on_valid(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        passed, detail = cwa.check_step_wires_inputs_without_drift(parsed)
        self.assertTrue(passed, detail)

    def test_fails_when_with_value_drifts_from_its_own_input(self):
        bad = VALID_ACTION_YAML.replace(
            "tenant-id: ${{ inputs.tenant-id }}",
            "tenant-id: ${{ inputs.client-id }}",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_step_wires_inputs_without_drift(parsed)
        self.assertFalse(passed)
        self.assertIn("tenant-id", detail)

    def test_fails_when_step_hardcodes_an_undeclared_secret_field(self):
        bad = VALID_ACTION_YAML.replace(
            "        subscription-id: ${{ inputs.subscription-id }}\n",
            "        subscription-id: ${{ inputs.subscription-id }}\n"
            "        creds: ${{ secrets.AZURE_CREDENTIALS }}\n",
        )
        parsed = cwa.parse_action_yaml(bad)
        passed, detail = cwa.check_step_wires_inputs_without_drift(parsed)
        self.assertFalse(passed)
        self.assertIn("creds", detail)


class CheckNoSecretMaterialTests(unittest.TestCase):
    def test_pass_on_valid_ignores_header_comment_prose(self):
        # VALID_ACTION_YAML's own header comment says "No client secret,
        # no AZURE_CREDENTIALS" -- that prose must not trip the scan.
        passed, detail = cwa.check_no_secret_material(VALID_ACTION_YAML)
        self.assertTrue(passed, detail)

    def test_fails_on_creds_field(self):
        bad = VALID_ACTION_YAML.replace(
            "        subscription-id: ${{ inputs.subscription-id }}\n",
            "        subscription-id: ${{ inputs.subscription-id }}\n"
            "        creds: ${{ secrets.AZURE_CREDENTIALS }}\n",
        )
        passed, detail = cwa.check_no_secret_material(bad)
        self.assertFalse(passed)
        self.assertIn("creds:", detail)

    def test_fails_on_secrets_context_usage(self):
        bad = VALID_ACTION_YAML.replace(
            "        subscription-id: ${{ inputs.subscription-id }}\n",
            "        subscription-id: ${{ inputs.subscription-id }}\n"
            "        client-secret: ${{ secrets.SOMETHING }}\n",
        )
        passed, detail = cwa.check_no_secret_material(bad)
        self.assertFalse(passed)
        self.assertTrue("secrets." in detail or "client-secret" in detail)


class CheckDocumentsIdTokenPermissionTests(unittest.TestCase):
    def test_pass_on_valid(self):
        passed, detail = cwa.check_documents_id_token_permission(VALID_ACTION_YAML)
        self.assertTrue(passed, detail)

    def test_fails_when_permission_note_is_removed(self):
        bad = VALID_ACTION_YAML.replace("#     id-token: write          # required for OIDC token request\n", "")
        passed, detail = cwa.check_documents_id_token_permission(bad)
        self.assertFalse(passed)
        self.assertIn("id-token: write", detail)


class CheckWiredToIdentityModuleOutputsTests(unittest.TestCase):
    def test_pass_on_valid(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        with tempfile.TemporaryDirectory() as tmp:
            outputs_path = Path(tmp) / "outputs.tf"
            outputs_path.write_text(VALID_IDENTITY_OUTPUTS_TF, encoding="utf-8")
            passed, detail = cwa.check_wired_to_identity_module_outputs(parsed, outputs_path)
            self.assertTrue(passed, detail)

    def test_fails_when_outputs_tf_missing(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        with tempfile.TemporaryDirectory() as tmp:
            outputs_path = Path(tmp) / "does-not-exist.tf"
            passed, detail = cwa.check_wired_to_identity_module_outputs(parsed, outputs_path)
            self.assertFalse(passed)
            self.assertIn("does not exist", detail)

    def test_fails_when_a_required_terraform_output_is_missing(self):
        parsed = cwa.parse_action_yaml(VALID_ACTION_YAML)
        missing_subscription = VALID_IDENTITY_OUTPUTS_TF.replace(
            'output "subscription_id" {\n'
            "  value = data.azurerm_subscription.current.subscription_id\n"
            "}\n\n",
            "",
        )
        with tempfile.TemporaryDirectory() as tmp:
            outputs_path = Path(tmp) / "outputs.tf"
            outputs_path.write_text(missing_subscription, encoding="utf-8")
            passed, detail = cwa.check_wired_to_identity_module_outputs(parsed, outputs_path)
            self.assertFalse(passed)
            self.assertIn("subscription_id", detail)

    def test_fails_when_action_input_was_renamed_away_from_a_terraform_output(self):
        renamed = VALID_ACTION_YAML.replace("tenant-id", "tenantid")
        parsed = cwa.parse_action_yaml(renamed)
        with tempfile.TemporaryDirectory() as tmp:
            outputs_path = Path(tmp) / "outputs.tf"
            outputs_path.write_text(VALID_IDENTITY_OUTPUTS_TF, encoding="utf-8")
            passed, detail = cwa.check_wired_to_identity_module_outputs(parsed, outputs_path)
            self.assertFalse(passed)
            self.assertIn("tenant-id", detail)


class RunAllChecksAndMainRealRepoTests(unittest.TestCase):
    """The named test proving the produced artifact `ci-workflow-auth`:
    every check against the *actual* repo files T01 (`ci-azure-auth`)
    left on disk must pass."""

    def test_run_all_checks_passes_against_the_real_repo_files(self):
        results = cwa.run_all_checks()
        self.assertGreaterEqual(len(results), 7)
        for name, (passed, detail) in results:
            self.assertTrue(passed, f"{name}: {detail}")

    def test_main_exits_zero_and_reports_the_artifact_name(self):
        buf = StringIO()
        with redirect_stdout(buf):
            code = cwa.main()
        self.assertEqual(code, 0)
        output = buf.getvalue()
        self.assertIn("ci-workflow-auth", output)
        self.assertNotIn("FAIL", output)

    def test_real_action_file_lives_at_the_task_prescribed_path(self):
        self.assertTrue(cwa.ACTION_PATH.is_file())
        self.assertEqual(
            cwa.ACTION_PATH,
            REPO_ROOT / ".github" / "actions" / "azure-login" / "action.yml",
        )


if __name__ == "__main__":
    unittest.main()
