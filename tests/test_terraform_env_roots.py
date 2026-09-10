"""Unit tests for scripts/terraform_env_roots_scan.py (task E01/F02/US01/T02).

Covers the pure HCL-block-extraction/parsing functions against synthetic
fixtures shaped like the real files (no `terraform` binary, no network),
the `check_*` functions against both a deliberately-good and several
deliberately-broken fixture trees (one per failure mode this scan exists to
catch), and finally two end-to-end proofs against this actual working tree:
running the `check_*` functions directly against the real `infra/` tree, and
running `scripts/terraform_env_roots_scan.py` as a subprocess -- the same
invocation this task's own definition of done relies on.

Run:
    python tests/test_terraform_env_roots.py -v
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import terraform_env_roots_scan as tfr  # noqa: E402

GOOD_TERRAFORM_BLOCK = """terraform {
  required_version = ">= 1.8.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}
"""

GOOD_VARIABLES_TF = """variable "location" {
  description = "Azure region."
  type        = string
  default     = "North Europe"
}
"""


def _good_backend_tf(env: str) -> str:
    workspace = tfr.EXPECTED_WORKSPACE_BY_ENV[env]
    return f"""terraform {{
  cloud {{
    organization = "raffa-platform"

    workspaces {{
      name = "{workspace}"
    }}
  }}
}}
"""


def _good_main_tf(env: str, terraform_block: str = GOOD_TERRAFORM_BLOCK, modules=tfr.REQUIRED_MODULES) -> str:
    module_blocks = "".join(
        f'''
module "{m}" {{
  source = "../../modules/{m}"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}}
'''
        for m in modules
    )
    return f"""{terraform_block}
provider "azurerm" {{
  features {{}}
}}

provider "azuread" {{}}

locals {{
  environment = "{env}"
}}

resource "azurerm_resource_group" "this" {{
  name     = "rg-raffa-${{local.environment}}"
  location = var.location

  tags = {{
    project = "raffa"
    env     = local.environment
  }}
}}
{module_blocks}"""


def _write_fixture_tree(
    root: Path,
    *,
    main_tf_overrides: dict | None = None,
    backend_tf_overrides: dict | None = None,
    variables_tf_overrides: dict | None = None,
    versions_tf_text: str | None = None,
) -> None:
    infra = root / "infra"
    envs = infra / "environments"
    infra.mkdir(parents=True, exist_ok=True)
    (infra / "versions.tf").write_text(
        versions_tf_text if versions_tf_text is not None else GOOD_TERRAFORM_BLOCK
    )
    main_tf_overrides = main_tf_overrides or {}
    backend_tf_overrides = backend_tf_overrides or {}
    variables_tf_overrides = variables_tf_overrides or {}
    for env in ("dev", "demo"):
        env_dir = envs / env
        env_dir.mkdir(parents=True, exist_ok=True)
        (env_dir / "main.tf").write_text(main_tf_overrides.get(env, _good_main_tf(env)))
        (env_dir / "backend.tf").write_text(backend_tf_overrides.get(env, _good_backend_tf(env)))
        (env_dir / "variables.tf").write_text(variables_tf_overrides.get(env, GOOD_VARIABLES_TF))
        (env_dir / "outputs.tf").write_text(
            'output "resource_group_name" {\n  value = azurerm_resource_group.this.name\n}\n'
        )


class ParseCloudBackendTests(unittest.TestCase):
    def test_extracts_organization_and_workspace(self) -> None:
        parsed = tfr.parse_cloud_backend(_good_backend_tf("dev"))
        self.assertEqual(parsed, {"organization": "raffa-platform", "workspace": "raffa-dev"})

    def test_missing_cloud_block_returns_none(self) -> None:
        parsed = tfr.parse_cloud_backend("terraform {\n}\n")
        self.assertEqual(parsed, {"organization": None, "workspace": None})

    def test_missing_terraform_block_returns_none(self) -> None:
        parsed = tfr.parse_cloud_backend("# nothing here\n")
        self.assertEqual(parsed, {"organization": None, "workspace": None})


class FindModuleBlocksTests(unittest.TestCase):
    def test_finds_all_modules_with_source(self) -> None:
        blocks = tfr.find_module_blocks(_good_main_tf("dev"))
        self.assertEqual(set(blocks), set(tfr.REQUIRED_MODULES))
        for m in tfr.REQUIRED_MODULES:
            self.assertEqual(blocks[m], f"../../modules/{m}")

    def test_missing_source_is_none(self) -> None:
        blocks = tfr.find_module_blocks('module "network" {\n  environment = "dev"\n}\n')
        self.assertIsNone(blocks["network"])

    def test_two_modules_do_not_bleed_into_each_other(self) -> None:
        text = (
            'module "acr" {\n  source = "../../modules/acr"\n}\n\n'
            'module "monitor" {\n  source = "../../modules/monitor"\n}\n'
        )
        blocks = tfr.find_module_blocks(text)
        self.assertEqual(blocks, {"acr": "../../modules/acr", "monitor": "../../modules/monitor"})

    def test_module_only_mentioned_in_a_comment_is_not_wired(self) -> None:
        # Regression: this exact shape (a full-line comment that *mentions*
        # a module block as prose, with no real block anywhere else) fooled
        # the very first version of this scan into reporting "monitor" as
        # wired when only "network" actually was -- the comment line reads
        # as a complete, syntactically valid module block to a naive regex.
        text = (
            '# a later task will add module "monitor" { source = "../../modules/monitor" }\n'
            'module "network" {\n  source = "../../modules/network"\n}\n'
        )
        blocks = tfr.find_module_blocks(text)
        self.assertEqual(blocks, {"network": "../../modules/network"})
        self.assertNotIn("monitor", blocks)


class FindLocalsEnvironmentTests(unittest.TestCase):
    def test_finds_environment(self) -> None:
        self.assertEqual(tfr.find_locals_environment('locals {\n  environment = "dev"\n}\n'), "dev")

    def test_no_locals_block_returns_none(self) -> None:
        self.assertIsNone(tfr.find_locals_environment("# no locals here\n"))


class ResolveEnvironmentValueTests(unittest.TestCase):
    """Regression coverage for task E01/F02/US02/T02 (dev-outputs-verify).

    Task E01/F02/US02/T01 promoted dev/main.tf's `locals.environment` from
    a literal `"dev"` to `var.environment` (commit 822fe70, after this
    script -- commit 78a940c -- already existed), which silently broke
    `check_environment_and_tags("dev")` against the real repo: the old
    `find_locals_environment` only ever matched a quoted literal. These
    tests cover the fix (`resolve_environment_value`), which additionally
    resolves a bare `var.<name>` reference via that root's own
    variables.tf default.
    """

    def test_resolves_literal(self) -> None:
        main_tf = 'locals {\n  environment = "demo"\n}\n'
        self.assertEqual(tfr.resolve_environment_value(main_tf, ""), "demo")

    def test_resolves_matching_variable_reference(self) -> None:
        main_tf = "locals {\n  environment = var.environment\n}\n"
        variables_tf = GOOD_VARIABLES_TF.replace("location", "environment").replace("North Europe", "dev")
        self.assertEqual(tfr.resolve_environment_value(main_tf, variables_tf), "dev")

    def test_variable_reference_with_no_matching_default_returns_none(self) -> None:
        main_tf = "locals {\n  environment = var.environment\n}\n"
        self.assertIsNone(tfr.resolve_environment_value(main_tf, "# no variables here\n"))

    def test_no_locals_block_returns_none(self) -> None:
        self.assertIsNone(tfr.resolve_environment_value("# nothing here\n", ""))


class FindResourceGroupTagsTests(unittest.TestCase):
    def test_finds_project_and_env_tags(self) -> None:
        text = _good_main_tf("demo")
        tags = tfr.find_resource_group_tags(text)
        self.assertEqual(tags.get("project"), "raffa")
        self.assertEqual(tags.get("env"), "local.environment")

    def test_missing_resource_returns_empty_dict(self) -> None:
        self.assertEqual(tfr.find_resource_group_tags("# nothing here\n"), {})

    def test_missing_env_tag_omits_key(self) -> None:
        text = (
            'resource "azurerm_resource_group" "this" {\n'
            '  name = "rg-raffa-dev"\n\n'
            "  tags = {\n"
            '    project = "raffa"\n'
            "  }\n"
            "}\n"
        )
        tags = tfr.find_resource_group_tags(text)
        self.assertEqual(tags.get("project"), "raffa")
        self.assertNotIn("env", tags)


class ParseVersionPinsTests(unittest.TestCase):
    def test_parses_required_version_and_providers(self) -> None:
        pins = tfr.parse_version_pins(GOOD_TERRAFORM_BLOCK)
        self.assertEqual(pins["required_version"], ">= 1.8.0, < 2.0.0")
        self.assertEqual(pins["azurerm"], {"source": "hashicorp/azurerm", "version": "~> 4.0"})
        self.assertEqual(pins["azuread"], {"source": "hashicorp/azuread", "version": "~> 3.0"})
        self.assertEqual(pins["random"], {"source": "hashicorp/random", "version": "~> 3.6"})

    def test_missing_terraform_block_returns_empty_dict(self) -> None:
        self.assertEqual(tfr.parse_version_pins("# nothing here\n"), {})

    def test_ignores_terraform_shaped_prose_in_a_leading_comment(self) -> None:
        # Regression: this is (near-verbatim) the real comment header on
        # infra/environments/{dev,demo}/main.tf. "...terraform{}/provider{}
        # blocks below..." reads as `terraform{` with zero required
        # whitespace and briefly matched as the block header itself,
        # extracting an empty body and reporting every pin as None even
        # though the real `terraform { ... }` block a few lines down was
        # intact and correct. Caught by RealRepoStructuralScanTests against
        # the actual working tree, not by any hand-written fixture -- every
        # earlier fixture in this file used comment-free strings.
        text = (
            "# See infra/versions.tf and infra/provider.tf for why the\n"
            "# terraform{}/provider{} blocks below are duplicated here.\n"
        ) + GOOD_TERRAFORM_BLOCK
        pins = tfr.parse_version_pins(text)
        self.assertEqual(pins["required_version"], ">= 1.8.0, < 2.0.0")
        self.assertEqual(pins["azurerm"], {"source": "hashicorp/azurerm", "version": "~> 4.0"})


class FindVariableDefaultTests(unittest.TestCase):
    def test_finds_default(self) -> None:
        self.assertEqual(tfr.find_variable_default(GOOD_VARIABLES_TF, "location"), "North Europe")

    def test_missing_variable_returns_none(self) -> None:
        self.assertIsNone(tfr.find_variable_default(GOOD_VARIABLES_TF, "nonexistent"))


class GoodFixtureTreeTests(unittest.TestCase):
    """Every check_* must pass against a deliberately correct fixture tree."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        _write_fixture_tree(self.root)
        self.infra_root = self.root / "infra"
        self.envs_root = self.infra_root / "environments"

    def test_all_checks_pass(self) -> None:
        for name, (passed, detail) in tfr.run_all_checks(self.envs_root, self.infra_root):
            self.assertTrue(passed, f"{name}: {detail}")


class BrokenFixtureTreeTests(unittest.TestCase):
    """One deliberately-broken fixture tree per failure mode this scan must catch."""

    def _tree(self, **overrides) -> tuple:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        _write_fixture_tree(root, **overrides)
        return root / "infra", (root / "infra" / "environments")

    def test_missing_demo_root_fails_env_roots_present(self) -> None:
        infra_root, envs_root = self._tree()
        shutil.rmtree(envs_root / "demo")
        passed, detail = tfr.check_env_roots_present(envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("demo", detail)

    def test_missing_root_file_fails_root_files_check(self) -> None:
        infra_root, envs_root = self._tree()
        (envs_root / "dev" / "outputs.tf").unlink()
        passed, detail = tfr.check_env_root_files("dev", envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("outputs.tf", detail)

    def test_shared_workspace_fails_isolation_checks(self) -> None:
        # dev's backend.tf wrongly points at the demo workspace.
        infra_root, envs_root = self._tree(
            backend_tf_overrides={"dev": _good_backend_tf("demo")}
        )
        passed, detail = tfr.check_backend_isolation("dev", envs_root)
        self.assertFalse(passed, detail)
        passed, detail = tfr.check_no_shared_workspace(envs_root)
        self.assertFalse(passed, detail)

    def test_wrong_organization_fails_backend_isolation(self) -> None:
        bad_backend = _good_backend_tf("dev").replace("raffa-platform", "some-other-org")
        infra_root, envs_root = self._tree(backend_tf_overrides={"dev": bad_backend})
        passed, detail = tfr.check_backend_isolation("dev", envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("organization", detail)

    def test_missing_module_fails_module_wiring(self) -> None:
        modules_without_monitor = [m for m in tfr.REQUIRED_MODULES if m != "monitor"]
        infra_root, envs_root = self._tree(
            main_tf_overrides={"dev": _good_main_tf("dev", modules=modules_without_monitor)}
        )
        passed, detail = tfr.check_module_wiring("dev", envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("monitor", detail)

    def test_wrong_module_source_fails_module_wiring(self) -> None:
        bad_main = _good_main_tf("dev").replace(
            'source = "../../modules/monitor"', 'source = "../../modules/monitoring"'
        )
        infra_root, envs_root = self._tree(main_tf_overrides={"dev": bad_main})
        passed, detail = tfr.check_module_wiring("dev", envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("monitor", detail)

    def test_environment_mismatch_fails_environment_and_tags(self) -> None:
        # The "dev" directory's own main.tf claims to be "demo".
        infra_root, envs_root = self._tree(main_tf_overrides={"dev": _good_main_tf("demo")})
        passed, detail = tfr.check_environment_and_tags("dev", envs_root)
        self.assertFalse(passed, detail)

    def test_missing_env_tag_fails_environment_and_tags(self) -> None:
        bad_main = _good_main_tf("dev").replace("    env     = local.environment\n", "")
        infra_root, envs_root = self._tree(main_tf_overrides={"dev": bad_main})
        passed, detail = tfr.check_environment_and_tags("dev", envs_root)
        self.assertFalse(passed, detail)

    def test_wrong_location_default_fails_location_pin(self) -> None:
        bad_vars = GOOD_VARIABLES_TF.replace("North Europe", "West Europe")
        infra_root, envs_root = self._tree(variables_tf_overrides={"dev": bad_vars})
        passed, detail = tfr.check_location_pin("dev", envs_root)
        self.assertFalse(passed, detail)
        self.assertIn("West Europe", detail)

    def test_drifted_provider_version_fails_version_pin_parity(self) -> None:
        drifted_block = GOOD_TERRAFORM_BLOCK.replace('version = "~> 4.0"', 'version = "~> 5.0"')
        infra_root, envs_root = self._tree(
            main_tf_overrides={"dev": _good_main_tf("dev", terraform_block=drifted_block)}
        )
        passed, detail = tfr.check_version_pin_parity("dev", envs_root, infra_root)
        self.assertFalse(passed, detail)
        self.assertIn("azurerm", detail)


class RealRepoStructuralScanTests(unittest.TestCase):
    """Same invocation this task's own definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in tfr.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        """End-to-end: running the actual script against this real working
        tree exits 0 -- the same invocation the task's definition of done
        and any future CI status check both rely on."""
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "terraform_env_roots_scan.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
