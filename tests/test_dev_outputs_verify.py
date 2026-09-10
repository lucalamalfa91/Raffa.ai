"""Unit tests for scripts/dev_outputs_verify.py (task E01/F02/US02/T02).

Covers the pure HCL output/tag-extraction helpers against hand-written
fixtures (including a couple of comment-prose regressions in the same
spirit as tests/test_terraform_env_roots.py), the `check_*` functions
against both a deliberately-good and several deliberately-broken
synthetic fixture trees (one per failure mode this verification exists to
catch), and finally two end-to-end proofs against this actual working
tree: running the `check_*` functions directly against the real `infra/`
tree, and running `scripts/dev_outputs_verify.py` as a subprocess -- the
same invocation this task's own definition of done relies on.

Run:
    python tests/test_dev_outputs_verify.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import dev_outputs_verify as dov  # noqa: E402

GOOD_ROOT_MAIN_TF = (
    'resource "azurerm_resource_group" "this" {\n'
    '  name     = "rg-raffa-dev"\n'
    "  location = var.location\n"
    "\n"
    "  tags = {\n"
    '    project = "raffa"\n'
    "    env     = local.environment\n"
    "  }\n"
    "}\n"
)


def _good_root_outputs_tf() -> str:
    return "\n".join(
        f'output "{name}" {{\n  value = {expr}\n}}\n' for name, expr in dov.EXPECTED_ROOT_OUTPUTS.items()
    )


def _good_module_outputs_tf(module: str) -> str:
    return "\n".join(
        f'output "{name}" {{\n  value = {expr}\n}}\n'
        for name, expr in dov.EXPECTED_MODULE_OUTPUTS[module].items()
    )


def _good_module_main_tf(module: str) -> str:
    resources = "".join(
        f'\nresource "{rtype}" "{rname}" {{\n  name = "x-{rname}"\n\n  tags = local.tags\n}}\n'
        for rtype, rname in dov.TAGGED_RESOURCES_BY_MODULE[module]
    )
    return (
        "locals {\n"
        "  tags = {\n"
        '    project = "raffa"\n'
        "    env     = var.environment\n"
        "  }\n"
        "}\n"
        f"{resources}"
    )


def _write_fixture_tree(root: Path, **overrides) -> tuple:
    infra = root / "infra"
    modules_root = infra / "modules"
    dev_root = infra / "environments" / "dev"
    dev_root.mkdir(parents=True, exist_ok=True)
    modules_root.mkdir(parents=True, exist_ok=True)

    (dev_root / "main.tf").write_text(overrides.get("root_main_text") or GOOD_ROOT_MAIN_TF, encoding="utf-8")
    (dev_root / "outputs.tf").write_text(
        overrides.get("root_outputs_text") or _good_root_outputs_tf(), encoding="utf-8"
    )

    module_main_overrides = overrides.get("module_main_overrides") or {}
    module_outputs_overrides = overrides.get("module_outputs_overrides") or {}
    for module in dov.EXPECTED_MODULE_OUTPUTS:
        mdir = modules_root / module
        mdir.mkdir(parents=True, exist_ok=True)
        (mdir / "main.tf").write_text(
            module_main_overrides.get(module, _good_module_main_tf(module)), encoding="utf-8"
        )
        (mdir / "outputs.tf").write_text(
            module_outputs_overrides.get(module, _good_module_outputs_tf(module)), encoding="utf-8"
        )
    return modules_root, dev_root


class FindOutputBlocksTests(unittest.TestCase):
    def test_finds_value_expression(self) -> None:
        text = 'output "id" {\n  description = "x"\n  value = module.postgres.id\n}\n'
        self.assertEqual(dov.find_output_blocks(text), {"id": "module.postgres.id"})

    def test_multiple_outputs_do_not_bleed_into_each_other(self) -> None:
        text = 'output "a" {\n  value = 1\n}\n\noutput "b" {\n  value = 2\n}\n'
        self.assertEqual(dov.find_output_blocks(text), {"a": "1", "b": "2"})

    def test_missing_value_is_none(self) -> None:
        text = 'output "id" {\n  description = "x"\n}\n'
        self.assertIsNone(dov.find_output_blocks(text)["id"])

    def test_output_only_mentioned_in_a_comment_is_not_found(self) -> None:
        # Regression, same shape as test_terraform_env_roots.py's module
        # comment case: a full-line comment that mentions an output block
        # as prose must not be picked up as a real output.
        text = '# a later task will add output "fake" { value = 1 }\noutput "real" {\n  value = 2\n}\n'
        blocks = dov.find_output_blocks(text)
        self.assertEqual(blocks, {"real": "2"})
        self.assertNotIn("fake", blocks)


class FindLocalsTagsTests(unittest.TestCase):
    def test_finds_project_and_env(self) -> None:
        text = 'locals {\n  tags = {\n    project = "raffa"\n    env     = var.environment\n  }\n}\n'
        self.assertEqual(dov.find_locals_tags(text), {"project": "raffa", "env": "var.environment"})

    def test_missing_locals_returns_empty_dict(self) -> None:
        self.assertEqual(dov.find_locals_tags("# nothing here\n"), {})


class FindResourceTagsRefTests(unittest.TestCase):
    def test_finds_local_tags_reference(self) -> None:
        text = 'resource "azurerm_key_vault" "this" {\n  name = "x"\n\n  tags = local.tags\n}\n'
        self.assertEqual(dov.find_resource_tags_ref(text, "azurerm_key_vault", "this"), "local.tags")

    def test_missing_resource_returns_none(self) -> None:
        self.assertIsNone(dov.find_resource_tags_ref("# nothing here\n", "azurerm_key_vault", "this"))

    def test_two_resources_of_the_same_type_do_not_bleed_into_each_other(self) -> None:
        text = (
            'resource "azurerm_container_app" "api" {\n  tags = local.tags\n}\n\n'
            'resource "azurerm_container_app" "worker" {\n  tags = "not_local_tags"\n}\n'
        )
        self.assertEqual(dov.find_resource_tags_ref(text, "azurerm_container_app", "api"), "local.tags")
        self.assertEqual(
            dov.find_resource_tags_ref(text, "azurerm_container_app", "worker"), '"not_local_tags"'
        )


class GoodFixtureTreeTests(unittest.TestCase):
    """Every check_* must pass against a deliberately correct fixture tree."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.modules_root, self.dev_root = _write_fixture_tree(Path(self._tmp.name))

    def test_all_checks_pass(self) -> None:
        for name, (passed, detail) in dov.run_all_checks(self.dev_root, self.modules_root):
            self.assertTrue(passed, f"{name}: {detail}")


class BrokenFixtureTreeTests(unittest.TestCase):
    """One deliberately-broken fixture tree per failure mode this verification must catch."""

    def _tree(self, **overrides) -> tuple:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        return _write_fixture_tree(Path(tmp.name), **overrides)

    def test_missing_module_outputs_file_fails(self) -> None:
        modules_root, _dev_root = self._tree()
        (modules_root / "keyvault" / "outputs.tf").unlink()
        passed, detail = dov.check_module_outputs("keyvault", modules_root)
        self.assertFalse(passed, detail)
        self.assertIn("does not exist", detail)

    def test_module_output_wrong_value_fails(self) -> None:
        bad = _good_module_outputs_tf("acr").replace(
            "azurerm_container_registry.this.login_server", "azurerm_container_registry.this.name"
        )
        modules_root, _dev_root = self._tree(module_outputs_overrides={"acr": bad})
        passed, detail = dov.check_module_outputs("acr", modules_root)
        self.assertFalse(passed, detail)
        self.assertIn("login_server", detail)

    def test_module_resource_missing_tags_fails(self) -> None:
        bad = _good_module_main_tf("storage").replace("\n\n  tags = local.tags\n", "\n")
        modules_root, _dev_root = self._tree(module_main_overrides={"storage": bad})
        passed, detail = dov.check_module_resource_tags("storage", modules_root)
        self.assertFalse(passed, detail)
        self.assertIn("azurerm_storage_account.this", detail)

    def test_module_locals_tags_hardcoded_env_fails(self) -> None:
        bad = _good_module_main_tf("monitor").replace('env     = var.environment', 'env     = "dev"')
        modules_root, _dev_root = self._tree(module_main_overrides={"monitor": bad})
        passed, detail = dov.check_module_resource_tags("monitor", modules_root)
        self.assertFalse(passed, detail)

    def test_root_outputs_missing_key_fails(self) -> None:
        bad = _good_root_outputs_tf().replace('output "postgres_fqdn"', 'output "renamed_by_mistake"')
        _modules_root, dev_root = self._tree(root_outputs_text=bad)
        passed, detail = dov.check_root_outputs(dev_root)
        self.assertFalse(passed, detail)
        self.assertIn("postgres_fqdn", detail)

    def test_root_outputs_wrong_value_fails(self) -> None:
        bad = _good_root_outputs_tf().replace("module.keyvault.vault_uri", "module.keyvault.id")
        _modules_root, dev_root = self._tree(root_outputs_text=bad)
        passed, detail = dov.check_root_outputs(dev_root)
        self.assertFalse(passed, detail)
        self.assertIn("key_vault_uri", detail)

    def test_root_resource_group_missing_tags_fails(self) -> None:
        bad = GOOD_ROOT_MAIN_TF.replace(
            '\n  tags = {\n    project = "raffa"\n    env     = local.environment\n  }\n', "\n"
        )
        _modules_root, dev_root = self._tree(root_main_text=bad)
        passed, detail = dov.check_root_resource_group_tags(dev_root)
        self.assertFalse(passed, detail)


class RealRepoOutputsVerifyTests(unittest.TestCase):
    """Same invocation this task's own definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in dov.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        """End-to-end: running the actual script against this real working
        tree exits 0 -- the same invocation the task's definition of done
        and any future CI status check both rely on."""
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "dev_outputs_verify.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
