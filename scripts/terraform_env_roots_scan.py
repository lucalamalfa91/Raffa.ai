#!/usr/bin/env python3
"""Structural scan for the Terraform dev/demo environment roots (task E01/F02/US01/T02).

Parent story `us-01-terraform-module-library` AC-4: `infra/environments/dev/`
and `infra/environments/demo/` exist with separate `backend.tf` pointing at
distinct HCP Terraform workspaces (`contigo-dev` / `contigo-demo`), and the
story-wide definition of done ("AC-1..AC-4 verified by `terraform fmt -check`
and a structural scan") names a structural scan as the repeatable proof --
this is that scan.

Scope: this task (`terraform-env-roots`) owns the two thin environment roots
under `infra/environments/`. It depends on task T01's `terraform-module-library`
(the modules under `infra/modules/`), which already carries its own
structural coverage; this script does not re-verify module *internals*
(e.g. per-resource tagging inside `infra/modules/*/main.tf`) -- it verifies
what a correct env root must show at its own layer: which modules it wires,
from which source path, with which environment identity, tags, region, and
version pins.

Checks, all read-only, no network, no `terraform` binary required:

  1. both env roots (`dev`, `demo`) exist with the required root files
     (main.tf, backend.tf, variables.tf, outputs.tf).
  2. each backend.tf's `terraform { cloud { ... } }` block points at the
     `contigo-platform` HCP Terraform organization and at the workspace
     name that belongs to that environment (`contigo-dev` / `contigo-demo`).
  3. dev and demo do not point at the same HCP Terraform workspace (ADR-007:
     the two environments never share state).
  4. each main.tf wires all required modules (AC-1's list plus staticwebapp),
     each from its `../../modules/<name>` source path (ADR-007 module layout).
  5. each main.tf's `locals.environment` resolves to its own directory name
     -- either a literal (`environment = "demo"`) or a variable reference
     (`environment = var.environment`, resolved via that root's own
     variables.tf default; dev uses this form as of task E01/F02/US02/T01)
     -- and the root's `azurerm_resource_group.this` is tagged
     `project = "contigo"` / `env = local.environment` (AC-3's tagging rule,
     applied at the root level).
  6. each variables.tf pins `location` to "North Europe" (ADR-006).
  7. each root's own embedded `terraform{}` block (required_version + the
     three provider pins) matches the shared `infra/versions.tf` exactly --
     Terraform has no cross-directory include, so infra/versions.tf's
     comment demands the copies be kept in lockstep by hand; this check
     catches drift.

Usage:
    python scripts/terraform_env_roots_scan.py

Exit 0 if every check passes. Non-zero otherwise, with a PASS/FAIL line per
check on stdout (failures also echoed to stderr).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
INFRA_ROOT = REPO_ROOT / "infra"
ENVIRONMENTS_ROOT = INFRA_ROOT / "environments"

REQUIRED_ENV_ROOT_FILES = ("main.tf", "backend.tf", "variables.tf", "outputs.tf")

# AC-1's module list, exact order (order is not itself load-bearing, only membership).
REQUIRED_MODULES = (
    "network",
    "identity",
    "postgres",
    "storage",
    "servicebus",
    "containerapps",
    "keyvault",
    "acr",
    "monitor",
    "staticwebapp",
    # ADR-008 amendment 2026-09-09: the shared AI services account, this
    # environment's Foundry project, deployments and grants.
    "foundry",
)
REQUIRED_PROVIDERS = ("azurerm", "azuread", "random")

EXPECTED_ORGANIZATION = "contigo-platform"
EXPECTED_WORKSPACE_BY_ENV = {"dev": "contigo-dev", "demo": "contigo-demo"}
EXPECTED_LOCATION_DEFAULT = "North Europe"


# ---------------------------------------------------------------------------
# Small brace-balanced HCL block extraction -- deliberately not a full HCL
# parser, just enough to pull named blocks out of the fixed shapes ADR-007
# specifies for these files.
# ---------------------------------------------------------------------------

def _strip_line_comments(text: str) -> str:
    """Drop every line whose stripped content starts with '#' or '//'.

    Every .tf file in this repo (see infra/environments/*/main.tf) uses
    full-line `#` comments, including ones that mention block syntax as
    prose -- e.g. "...the terraform{}/provider{} blocks below...". Left
    unstripped, that literal `terraform{` substring is indistinguishable
    from a real block header to a regex search and is matched first
    (producing a bogus empty block). Comments must be removed before any
    `_find_block`/regex lookup runs against raw file text.
    """
    kept = []
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith("#") or stripped.startswith("//"):
            continue
        kept.append(line)
    return "\n".join(kept)


def _extract_block(text: str, open_brace_idx: int) -> str:
    """Return the text strictly between the '{' at open_brace_idx and its matching '}'."""
    depth = 0
    for i in range(open_brace_idx, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[open_brace_idx + 1 : i]
    raise ValueError(f"unbalanced braces from index {open_brace_idx}")


def _find_block(text: str, header_pattern: str) -> str | None:
    """Find `<header_pattern>{`, return its balanced inner text, or None if absent."""
    m = re.search(header_pattern + r"\s*{", text)
    if not m:
        return None
    return _extract_block(text, m.end() - 1)


def parse_cloud_backend(backend_tf_text: str) -> dict:
    """Extract {"organization": ..., "workspace": ...} from a `terraform { cloud { ... } }` block."""
    tf_body = _find_block(_strip_line_comments(backend_tf_text), r"terraform")
    cloud_body = _find_block(tf_body, r"cloud") if tf_body is not None else None
    if cloud_body is None:
        return {"organization": None, "workspace": None}
    org_m = re.search(r'organization\s*=\s*"([^"]+)"', cloud_body)
    ws_body = _find_block(cloud_body, r"workspaces")
    name_m = re.search(r'name\s*=\s*"([^"]+)"', ws_body) if ws_body is not None else None
    return {
        "organization": org_m.group(1) if org_m else None,
        "workspace": name_m.group(1) if name_m else None,
    }


def find_module_blocks(main_tf_text: str) -> dict:
    """Return {module_name: source_path_or_None} for every `module "X" { ... }` block."""
    main_tf_text = _strip_line_comments(main_tf_text)
    blocks: dict = {}
    for m in re.finditer(r'module\s+"([A-Za-z0-9_-]+)"\s*{', main_tf_text):
        body = _extract_block(main_tf_text, m.end() - 1)
        source_m = re.search(r'source\s*=\s*"([^"]+)"', body)
        blocks[m.group(1)] = source_m.group(1) if source_m else None
    return blocks


def find_locals_environment(main_tf_text: str) -> str | None:
    body = _find_block(_strip_line_comments(main_tf_text), r"locals")
    if body is None:
        return None
    m = re.search(r'environment\s*=\s*"([^"]+)"', body)
    return m.group(1) if m else None


def find_locals_environment_expr(main_tf_text: str) -> str | None:
    """Return the raw, unparsed `environment = <expr>` right-hand side inside `locals { ... }`."""
    body = _find_block(_strip_line_comments(main_tf_text), r"locals")
    if body is None:
        return None
    m = re.search(r'environment\s*=\s*(\S+)', body)
    return m.group(1) if m else None


def resolve_environment_value(main_tf_text: str, variables_tf_text: str) -> str | None:
    """Resolve `locals.environment` to its literal string value.

    Handles both shapes seen across the two env roots: a literal
    (`environment = "demo"` in demo/main.tf) and a variable reference
    (`environment = var.environment` in dev/main.tf -- promoted from a
    literal by task E01/F02/US02/T01 so the value comes from
    variables.tf's declared+validated default rather than a hardcoded
    local; see that task's commit message). A bare `var.<name>`
    reference resolves via this same root's
    `find_variable_default(variables_tf_text, "<name>")`.
    """
    expr = find_locals_environment_expr(main_tf_text)
    if expr is None:
        return None
    literal_m = re.match(r'^"([^"]+)"$', expr)
    if literal_m:
        return literal_m.group(1)
    var_m = re.match(r'^var\.(\w+)$', expr)
    if var_m:
        return find_variable_default(variables_tf_text, var_m.group(1))
    return None


def find_resource_group_tags(main_tf_text: str) -> dict:
    """Return the raw {key: value_source_text} tags on resource "azurerm_resource_group" "this"."""
    body = _find_block(_strip_line_comments(main_tf_text), r'resource\s+"azurerm_resource_group"\s+"this"\s*')
    if body is None:
        return {}
    tags_body = _find_block(body, r"tags\s*=")
    if tags_body is None:
        return {}
    return dict(re.findall(r'(\w+)\s*=\s*"?([^"\n]+?)"?\s*\n', tags_body + "\n"))


def parse_version_pins(text: str) -> dict:
    """Extract {"required_version": ..., "<provider>": {"source": ..., "version": ...}, ...}."""
    body = _find_block(_strip_line_comments(text), r"terraform")
    if body is None:
        return {}
    result: dict = {}
    rv_m = re.search(r'required_version\s*=\s*"([^"]+)"', body)
    result["required_version"] = rv_m.group(1) if rv_m else None
    rp_body = _find_block(body, r"required_providers")
    if rp_body is not None:
        for prov_m in re.finditer(r"(\w+)\s*=\s*{", rp_body):
            prov_body = _extract_block(rp_body, prov_m.end() - 1)
            src_m = re.search(r'source\s*=\s*"([^"]+)"', prov_body)
            ver_m = re.search(r'version\s*=\s*"([^"]+)"', prov_body)
            result[prov_m.group(1)] = {
                "source": src_m.group(1) if src_m else None,
                "version": ver_m.group(1) if ver_m else None,
            }
    return result


def find_variable_default(variables_tf_text: str, variable_name: str) -> str | None:
    body = _find_block(_strip_line_comments(variables_tf_text), rf'variable\s+"{re.escape(variable_name)}"\s*')
    if body is None:
        return None
    m = re.search(r'default\s*=\s*"([^"]+)"', body)
    return m.group(1) if m else None


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring scripts/repo_secret_scan.py
# ---------------------------------------------------------------------------

def check_env_roots_present(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    missing = [e for e in EXPECTED_WORKSPACE_BY_ENV if not (environments_root / e).is_dir()]
    if missing:
        return False, f"missing env root(s) under {environments_root}: {', '.join(missing)}"
    return True, f"both env roots present under {environments_root}: {', '.join(EXPECTED_WORKSPACE_BY_ENV)}"


def check_env_root_files(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    env_dir = environments_root / env
    if not env_dir.is_dir():
        return False, f"{env}/ does not exist"
    missing = [f for f in REQUIRED_ENV_ROOT_FILES if not (env_dir / f).is_file()]
    if missing:
        return False, f"{env}/ missing files: {', '.join(missing)}"
    return True, f"{env}/ has all required files: {', '.join(REQUIRED_ENV_ROOT_FILES)}"


def check_backend_isolation(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    backend_path = environments_root / env / "backend.tf"
    if not backend_path.is_file():
        return False, f"{env}/backend.tf does not exist"
    parsed = parse_cloud_backend(backend_path.read_text(encoding="utf-8"))
    expected_ws = EXPECTED_WORKSPACE_BY_ENV[env]
    if parsed["organization"] != EXPECTED_ORGANIZATION:
        return False, (
            f"{env}/backend.tf organization={parsed['organization']!r}, expected {EXPECTED_ORGANIZATION!r}"
        )
    if parsed["workspace"] != expected_ws:
        return False, f"{env}/backend.tf workspace={parsed['workspace']!r}, expected {expected_ws!r}"
    return True, f"{env}/backend.tf -> organization={parsed['organization']}, workspace={parsed['workspace']}"


def check_no_shared_workspace(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    names = set()
    for env in EXPECTED_WORKSPACE_BY_ENV:
        backend_path = environments_root / env / "backend.tf"
        if not backend_path.is_file():
            return False, f"{env}/backend.tf does not exist"
        parsed = parse_cloud_backend(backend_path.read_text(encoding="utf-8"))
        if parsed["workspace"] is None:
            return False, f"{env}/backend.tf has no workspace name"
        names.add(parsed["workspace"])
    if len(names) != len(EXPECTED_WORKSPACE_BY_ENV):
        return False, f"dev and demo point at the same HCP Terraform workspace: {names}"
    return True, f"dev and demo use distinct workspaces: {sorted(names)}"


def check_module_wiring(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    blocks = find_module_blocks(main_path.read_text(encoding="utf-8"))
    missing = [m for m in REQUIRED_MODULES if m not in blocks]
    if missing:
        return False, f"{env}/main.tf missing module block(s): {', '.join(missing)}"
    bad_source = [m for m in REQUIRED_MODULES if blocks[m] != f"../../modules/{m}"]
    if bad_source:
        detail = ", ".join(f"{m} source={blocks[m]!r}" for m in bad_source)
        return False, f"{env}/main.tf module source mismatch: {detail}"
    return True, f"{env}/main.tf wires all {len(REQUIRED_MODULES)} required modules from ../../modules/*"


def check_environment_and_tags(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    main_path = environments_root / env / "main.tf"
    variables_path = environments_root / env / "variables.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    text = main_path.read_text(encoding="utf-8")
    variables_text = variables_path.read_text(encoding="utf-8") if variables_path.is_file() else ""
    local_env = resolve_environment_value(text, variables_text)
    if local_env != env:
        return False, f"{env}/main.tf locals.environment resolves to {local_env!r}, expected {env!r}"
    tags = find_resource_group_tags(text)
    if tags.get("project") != "contigo" or tags.get("env") != "local.environment":
        return False, (
            f"{env}/main.tf azurerm_resource_group.this tags={tags!r}, "
            'expected project="contigo" and env=local.environment'
        )
    return True, (
        f"{env}/main.tf locals.environment resolves to {env!r}; "
        "resource group tagged project=contigo, env=local.environment"
    )


def check_location_pin(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    variables_path = environments_root / env / "variables.tf"
    if not variables_path.is_file():
        return False, f"{env}/variables.tf does not exist"
    value = find_variable_default(variables_path.read_text(encoding="utf-8"), "location")
    if value != EXPECTED_LOCATION_DEFAULT:
        return False, f"{env}/variables.tf location default={value!r}, expected {EXPECTED_LOCATION_DEFAULT!r}"
    return True, f"{env}/variables.tf location default == {EXPECTED_LOCATION_DEFAULT!r}"


def check_version_pin_parity(
    env: str, environments_root: Path = ENVIRONMENTS_ROOT, infra_root: Path = INFRA_ROOT
) -> tuple:
    main_path = environments_root / env / "main.tf"
    versions_path = infra_root / "versions.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    if not versions_path.is_file():
        return False, "infra/versions.tf does not exist"
    root_pins = parse_version_pins(main_path.read_text(encoding="utf-8"))
    shared_pins = parse_version_pins(versions_path.read_text(encoding="utf-8"))
    mismatches = []
    if root_pins.get("required_version") != shared_pins.get("required_version"):
        mismatches.append(
            f"required_version root={root_pins.get('required_version')!r} "
            f"vs infra/versions.tf={shared_pins.get('required_version')!r}"
        )
    for provider in REQUIRED_PROVIDERS:
        if root_pins.get(provider) != shared_pins.get(provider):
            mismatches.append(
                f"{provider} root={root_pins.get(provider)!r} vs infra/versions.tf={shared_pins.get(provider)!r}"
            )
    if mismatches:
        return False, f"{env}/main.tf drifted from infra/versions.tf: " + "; ".join(mismatches)
    return True, f"{env}/main.tf terraform{{}} pins match infra/versions.tf exactly"


def run_all_checks(environments_root: Path = ENVIRONMENTS_ROOT, infra_root: Path = INFRA_ROOT) -> list:
    checks = [("env roots present", check_env_roots_present(environments_root))]
    for env in EXPECTED_WORKSPACE_BY_ENV:
        checks.append((f"{env} root files", check_env_root_files(env, environments_root)))
        checks.append((f"{env} backend isolation", check_backend_isolation(env, environments_root)))
        checks.append((f"{env} module wiring", check_module_wiring(env, environments_root)))
        checks.append((f"{env} environment + tags", check_environment_and_tags(env, environments_root)))
        checks.append((f"{env} location pin", check_location_pin(env, environments_root)))
        checks.append(
            (f"{env} version pin parity", check_version_pin_parity(env, environments_root, infra_root))
        )
    checks.append(("dev/demo workspace isolation", check_no_shared_workspace(environments_root)))
    return checks


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print("[terraform_env_roots_scan] PASS: dev/demo env roots satisfy AC-4 (ADR-007/ADR-005/ADR-006)")
        return 0
    print("[terraform_env_roots_scan] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
