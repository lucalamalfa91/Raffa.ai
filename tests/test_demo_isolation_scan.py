"""Unit tests for scripts/demo_isolation_scan.py (task E01/F02/US03/T02).

Covers: the pure HCL-slicing/resolution helpers against synthetic fixtures
(including both `locals.environment` shapes actually present in this repo --
a literal for `demo`, a `var.environment` reference for `dev`), the `check_*`
functions against a deliberately-good fixture tree and one deliberately-broken
tree per failure mode this scan exists to catch, and finally two end-to-end
proofs against this actual working tree: running the `check_*` functions
directly against the real `infra/` tree, and running
`scripts/demo_isolation_scan.py` as a subprocess -- the same invocation this
task's own definition of done relies on.

Run:
    python tests/test_demo_isolation_scan.py -v
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import demo_isolation_scan as dis  # noqa: E402

# ---------------------------------------------------------------------------
# Fixture builders -- shaped like the real repo, but with every field a test
# can override so a broken fixture only differs in the one thing it's
# testing.
# ---------------------------------------------------------------------------

GOOD_POSTGRES_MAIN_TF = '''resource "random_password" "administrator" {
  length = 32
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                = "psql-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
}
'''

GOOD_STORAGE_MAIN_TF = '''resource "random_string" "suffix" {
  length = 6
}

resource "azurerm_storage_account" "this" {
  name                = "straffa${var.environment}${random_string.suffix.result}"
  location            = var.location
  resource_group_name = var.resource_group_name
}
'''

GOOD_SERVICEBUS_MAIN_TF = '''resource "azurerm_servicebus_namespace" "this" {
  name                = "sbns-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
}
'''

GOOD_MODULE_MAIN_TF_BY_NAME = {
    "postgres": GOOD_POSTGRES_MAIN_TF,
    "storage": GOOD_STORAGE_MAIN_TF,
    "servicebus": GOOD_SERVICEBUS_MAIN_TF,
}

DATASTORE_MODULE_NAMES = ("postgres", "storage", "servicebus")


def _module_block(
    name: str,
    *,
    environment_expr: str = "local.environment",
    resource_group_name_expr: str = "azurerm_resource_group.this.name",
) -> str:
    return f'''
module "{name}" {{
  source = "../../modules/{name}"

  environment         = {environment_expr}
  location            = var.location
  resource_group_name = {resource_group_name_expr}
}}
'''


def _good_main_tf(
    env: str,
    *,
    environment_expr: str | None = None,
    rg_name_template: str | None = None,
    modules=DATASTORE_MODULE_NAMES,
    module_field_overrides: dict | None = None,
) -> str:
    """`environment_expr`: raw RHS for `locals.environment`. Defaults to a
    literal `"<env>"` (demo's real shape); pass "var.environment" for dev's
    real shape (paired with a variables.tf `environment` default)."""
    expr = environment_expr if environment_expr is not None else f'"{env}"'
    rg_name = rg_name_template if rg_name_template is not None else "rg-raffa-${local.environment}"
    overrides = module_field_overrides or {}
    module_blocks = "".join(_module_block(m, **overrides.get(m, {})) for m in modules)
    return f'''locals {{
  environment = {expr}
}}

resource "azurerm_resource_group" "this" {{
  name     = "{rg_name}"
  location = var.location

  tags = {{
    project = "raffa"
    env     = local.environment
  }}
}}
{module_blocks}'''


def _good_backend_tf(workspace: str, organization: str = "raffa-platform") -> str:
    return f'''terraform {{
  cloud {{
    organization = "{organization}"

    workspaces {{
      name = "{workspace}"
    }}
  }}
}}
'''


def _good_variables_tf(*, environment_default: str | None = None) -> str:
    body = 'variable "location" {\n  type    = string\n  default = "North Europe"\n}\n'
    if environment_default is not None:
        body += f'''
variable "environment" {{
  type    = string
  default = "{environment_default}"

  validation {{
    condition     = var.environment == "{environment_default}"
    error_message = "must be {environment_default}"
  }}
}}
'''
    return body


def _write_env_root(infra_root: Path, env: str, *, main_tf: str, backend_tf: str, variables_tf: str) -> None:
    env_dir = infra_root / "environments" / env
    env_dir.mkdir(parents=True, exist_ok=True)
    (env_dir / "main.tf").write_text(main_tf)
    (env_dir / "backend.tf").write_text(backend_tf)
    (env_dir / "variables.tf").write_text(variables_tf)
    (env_dir / "outputs.tf").write_text(
        'output "resource_group_name" {\n  value = azurerm_resource_group.this.name\n}\n'
    )


def _write_modules(infra_root: Path, module_main_tf_by_name: dict) -> None:
    for name, text in module_main_tf_by_name.items():
        module_dir = infra_root / "modules" / name
        module_dir.mkdir(parents=True, exist_ok=True)
        (module_dir / "main.tf").write_text(text)


def _build_tree(
    tmp_root: Path,
    *,
    dev_main_tf: str | None = None,
    demo_main_tf: str | None = None,
    dev_backend_tf: str | None = None,
    demo_backend_tf: str | None = None,
    dev_variables_tf: str | None = None,
    demo_variables_tf: str | None = None,
    module_overrides: dict | None = None,
) -> tuple:
    """Write a fixture tree shaped like the real `infra/`; every argument
    left `None` gets the real repo's own shape (dev: `var.environment`
    defaulted "dev"; demo: literal `"demo"`). Returns (environments_root,
    modules_root)."""
    infra_root = tmp_root / "infra"
    _write_env_root(
        infra_root,
        "dev",
        main_tf=dev_main_tf if dev_main_tf is not None else _good_main_tf("dev", environment_expr="var.environment"),
        backend_tf=dev_backend_tf if dev_backend_tf is not None else _good_backend_tf("raffa-dev"),
        variables_tf=dev_variables_tf if dev_variables_tf is not None else _good_variables_tf(environment_default="dev"),
    )
    _write_env_root(
        infra_root,
        "demo",
        main_tf=demo_main_tf if demo_main_tf is not None else _good_main_tf("demo"),
        backend_tf=demo_backend_tf if demo_backend_tf is not None else _good_backend_tf("raffa-demo"),
        variables_tf=demo_variables_tf if demo_variables_tf is not None else _good_variables_tf(),
    )
    modules = dict(GOOD_MODULE_MAIN_TF_BY_NAME)
    if module_overrides:
        modules.update(module_overrides)
    _write_modules(infra_root, modules)
    return infra_root / "environments", infra_root / "modules"


# ---------------------------------------------------------------------------
# Pure helper unit tests
# ---------------------------------------------------------------------------

class StripLineCommentsTests(unittest.TestCase):
    def test_drops_hash_and_slash_comments_keeps_code(self) -> None:
        text = '# a comment mentioning rg-raffa-dev\ncode = "kept"\n// another\nmore = 1\n'
        stripped = dis._strip_line_comments(text)
        self.assertNotIn("rg-raffa-dev", stripped)
        self.assertIn('code = "kept"', stripped)
        self.assertIn("more = 1", stripped)


class UnquoteTests(unittest.TestCase):
    def test_unquotes_string_literal(self) -> None:
        self.assertEqual(dis.unquote('"demo"'), "demo")

    def test_non_string_returns_none(self) -> None:
        self.assertIsNone(dis.unquote("var.environment"))

    def test_none_input_returns_none(self) -> None:
        self.assertIsNone(dis.unquote(None))


class FindAttrTests(unittest.TestCase):
    def test_finds_simple_assignment(self) -> None:
        block = '  environment = local.environment\n  location = var.location\n'
        self.assertEqual(dis.find_attr(block, "environment"), "local.environment")

    def test_missing_attr_returns_none(self) -> None:
        self.assertIsNone(dis.find_attr("location = var.location\n", "environment"))

    def test_none_block_returns_none(self) -> None:
        self.assertIsNone(dis.find_attr(None, "environment"))

    def test_does_not_partial_match_longer_attr_name(self) -> None:
        # "environment_id" must not satisfy a lookup for "environment".
        block = "  environment_id = \"x\"\n"
        self.assertIsNone(dis.find_attr(block, "environment"))


class ResolveRootEnvironmentTests(unittest.TestCase):
    """The crux of this task: dev's real `main.tf` sets `locals.environment
    = var.environment` (not a literal) -- resolve_root_environment must
    resolve *both* shapes actually present in this repo."""

    def test_resolves_literal_shape(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(Path(tmp))
            self.assertEqual(dis.resolve_root_environment("demo", envs), "demo")

    def test_resolves_var_reference_shape_via_default(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(Path(tmp))
            self.assertEqual(dis.resolve_root_environment("dev", envs), "dev")

    def test_var_reference_without_default_resolves_to_none(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(
                Path(tmp),
                dev_variables_tf=_good_variables_tf(),  # no "environment" variable at all
            )
            self.assertIsNone(dis.resolve_root_environment("dev", envs))

    def test_missing_locals_block_resolves_to_none(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(Path(tmp), dev_main_tf="# no locals block here\n")
            self.assertIsNone(dis.resolve_root_environment("dev", envs))

    def test_missing_root_resolves_to_none(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(Path(tmp))
            self.assertIsNone(dis.resolve_root_environment("staging", envs))


class ParseBackendWorkspaceTests(unittest.TestCase):
    def test_extracts_organization_and_workspace(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            envs, _modules = _build_tree(Path(tmp))
            parsed = dis.parse_backend_workspace("dev", envs)
            self.assertEqual(parsed, {"organization": "raffa-platform", "workspace": "raffa-dev"})

    def test_missing_backend_file_returns_none_pair(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            envs, _modules = _build_tree(root)
            (envs / "dev" / "backend.tf").unlink()
            self.assertEqual(dis.parse_backend_workspace("dev", envs), {"organization": None, "workspace": None})


class DatastoreNameTemplateTests(unittest.TestCase):
    def test_finds_name_template(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            _envs, modules = _build_tree(Path(tmp))
            template = dis.datastore_name_template("postgres", "azurerm_postgresql_flexible_server", modules)
            self.assertEqual(template, "psql-raffa-${var.environment}")

    def test_missing_module_file_returns_none(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            _envs, modules = _build_tree(Path(tmp))
            self.assertIsNone(dis.datastore_name_template("nonexistent", "azurerm_storage_account", modules))


# ---------------------------------------------------------------------------
# check_* against a deliberately-correct fixture tree
# ---------------------------------------------------------------------------

class GoodFixtureTreeTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.environments_root, self.modules_root = _build_tree(Path(self._tmp.name))

    def test_all_checks_pass(self) -> None:
        for name, (passed, detail) in dis.run_all_checks(self.environments_root, self.modules_root):
            self.assertTrue(passed, f"{name}: {detail}")


# ---------------------------------------------------------------------------
# check_* against one deliberately-broken fixture tree per failure mode
# ---------------------------------------------------------------------------

class BrokenFixtureTreeTests(unittest.TestCase):
    def _tree(self, **kwargs) -> tuple:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        return _build_tree(Path(tmp.name), **kwargs)

    def test_missing_module_file_fails_required_files(self) -> None:
        envs, modules = self._tree()
        (modules / "postgres" / "main.tf").unlink()
        passed, detail = dis.check_required_files_present(envs, modules)
        self.assertFalse(passed, detail)
        self.assertIn("postgres", detail)

    def test_missing_env_root_file_fails_required_files(self) -> None:
        envs, modules = self._tree()
        (envs / "demo" / "outputs.tf").unlink()
        passed, detail = dis.check_required_files_present(envs, modules)
        self.assertFalse(passed, detail)
        self.assertIn("demo/outputs.tf", detail)

    def test_dev_environment_default_mismatch_fails_identity(self) -> None:
        envs, _modules = self._tree(dev_variables_tf=_good_variables_tf(environment_default="production"))
        passed, detail = dis.check_root_environment_identity("dev", envs)
        self.assertFalse(passed, detail)

    def test_shared_resource_group_name_fails_distinct_rg(self) -> None:
        # demo's resource group resource hardcodes dev's own concrete name,
        # even though demo's `locals.environment` still correctly says "demo".
        envs, _modules = self._tree(demo_main_tf=_good_main_tf("demo", rg_name_template="rg-raffa-dev"))
        passed, detail = dis.check_distinct_resource_groups(envs)
        self.assertFalse(passed, detail)
        self.assertIn("same name", detail)

    def test_shared_workspace_fails_distinct_remote_state(self) -> None:
        envs, _modules = self._tree(demo_backend_tf=_good_backend_tf("raffa-dev"))
        passed, detail = dis.check_distinct_remote_state(envs)
        self.assertFalse(passed, detail)
        self.assertIn("same", detail)

    def test_different_organization_fails_distinct_remote_state(self) -> None:
        envs, _modules = self._tree(demo_backend_tf=_good_backend_tf("raffa-demo", organization="some-other-org"))
        passed, detail = dis.check_distinct_remote_state(envs)
        self.assertFalse(passed, detail)
        self.assertIn("organization", detail)

    def test_module_hardcoded_resource_group_fails_own_scope(self) -> None:
        bad_demo_main = _good_main_tf(
            "demo", module_field_overrides={"postgres": {"resource_group_name_expr": '"rg-raffa-dev"'}}
        )
        envs, _modules = self._tree(demo_main_tf=bad_demo_main)
        passed, detail = dis.check_module_own_scope("demo", "postgres", envs)
        self.assertFalse(passed, detail)
        self.assertIn("resource_group_name", detail)

    def test_module_hardcoded_environment_fails_own_scope(self) -> None:
        bad_dev_main = _good_main_tf(
            "dev",
            environment_expr="var.environment",
            module_field_overrides={"storage": {"environment_expr": '"demo"'}},
        )
        envs, _modules = self._tree(dev_main_tf=bad_dev_main)
        passed, detail = dis.check_module_own_scope("dev", "storage", envs)
        self.assertFalse(passed, detail)
        self.assertIn("environment", detail)

    def test_missing_module_block_fails_own_scope(self) -> None:
        envs, _modules = self._tree(demo_main_tf=_good_main_tf("demo", modules=("storage", "servicebus")))
        passed, detail = dis.check_module_own_scope("demo", "postgres", envs)
        self.assertFalse(passed, detail)
        self.assertIn("not found", detail)

    def test_unparameterized_name_fails_datastore_isolation(self) -> None:
        envs, modules = self._tree(
            module_overrides={
                "postgres": GOOD_POSTGRES_MAIN_TF.replace(
                    '"psql-raffa-${var.environment}"', '"psql-raffa-shared"'
                ),
            }
        )
        passed, detail = dis.check_datastore_isolation(
            "postgres", "azurerm_postgresql_flexible_server", envs, modules
        )
        self.assertFalse(passed, detail)
        self.assertIn("interpolate", detail)

    def test_demo_copied_from_dev_without_updating_environment_fails_datastore_isolation(self) -> None:
        # The exact real-world mistake this task exists to catch: demo's root
        # copy-pasted from dev and left the environment literal as "dev".
        envs, modules = self._tree(demo_main_tf=_good_main_tf("dev"))
        passed, detail = dis.check_datastore_isolation(
            "servicebus", "azurerm_servicebus_namespace", envs, modules
        )
        self.assertFalse(passed, detail)

    def test_remote_state_read_fails_cross_environment_coupling(self) -> None:
        bad_demo_main = (
            'data "terraform_remote_state" "dev" {\n  backend = "remote"\n}\n\n' + _good_main_tf("demo")
        )
        envs, _modules = self._tree(demo_main_tf=bad_demo_main)
        passed, detail = dis.check_no_cross_environment_coupling("demo", envs)
        self.assertFalse(passed, detail)
        self.assertIn("terraform_remote_state", detail)

    def test_hardcoded_other_env_resource_group_fails_cross_environment_coupling(self) -> None:
        # A live (non-comment) reference to dev's resource group name inside
        # demo's own root -- e.g. a leftover local nobody meant to keep.
        bad_demo_main = _good_main_tf("demo") + '\nlocals {\n  leftover_note = "rg-raffa-dev"\n}\n'
        envs, _modules = self._tree(demo_main_tf=bad_demo_main)
        passed, detail = dis.check_no_cross_environment_coupling("demo", envs)
        self.assertFalse(passed, detail)
        self.assertIn("rg-raffa-dev", detail)

    def test_comment_only_mention_of_other_env_does_not_fail_coupling_check(self) -> None:
        # Regression guard, mirroring the real repo's shape: demo/main.tf's
        # own header comments say "rg-raffa-dev" as prose. That must not
        # be flagged -- only a live reference would be.
        commented_demo_main = (
            "# see dev's own rg-raffa-dev for comparison\n" + _good_main_tf("demo")
        )
        envs, _modules = self._tree(demo_main_tf=commented_demo_main)
        passed, detail = dis.check_no_cross_environment_coupling("demo", envs)
        self.assertTrue(passed, detail)


# ---------------------------------------------------------------------------
# End-to-end proofs against the real working tree
# ---------------------------------------------------------------------------

class RealRepoStructuralScanTests(unittest.TestCase):
    """Same invocation this task's own definition of done relies on."""

    def test_all_checks_pass_against_the_real_repo(self) -> None:
        for name, (passed, detail) in dis.run_all_checks():
            self.assertTrue(passed, f"{name}: {detail}")


class MainEntryPointTests(unittest.TestCase):
    def test_main_passes_against_the_real_repo(self) -> None:
        """End-to-end: running the actual script against this real working
        tree exits 0 -- the same invocation the task's definition of done
        and any future CI status check both rely on."""
        proc = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts" / "demo_isolation_scan.py")],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)


if __name__ == "__main__":
    unittest.main()
