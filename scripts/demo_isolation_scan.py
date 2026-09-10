#!/usr/bin/env python3
"""Cross-environment isolation scan for `demo` vs `dev` (task E01/F02/US03/T02).

Parent story `us-03-demo-environment` AC-3: "`demo` has its own resource group
and HCP `raffa-demo` state; no shared Postgres/Storage/Service Bus with
`dev`." Task `task-01-demo-environment-provision` instantiated the `demo` root
and explicitly deferred the automated proof to this task (see the header
comments on `infra/environments/demo/main.tf` and `.../demo/outputs.tf`,
which name this task by id). This script is that proof.

Scope: this task owns the *cross-environment isolation* claim only -- it does
not re-verify everything `scripts/terraform_env_roots_scan.py` (task
E01/F02/US01/T02) already covers (module wiring completeness, version-pin
parity, location pin, ...). It is deliberately self-contained (its own small
HCL-slicing helpers, no import of the sibling script) so it can be read,
run, and reviewed on its own.

Checks, all read-only, no network, no `terraform` binary, no Azure/HCP
credentials required:

  1. both env roots (`dev`, `demo`) and the three datastore modules
     (`postgres`, `storage`, `servicebus`) have the files this scan reads.
  2. each root's `locals.environment` resolves to that root's own directory
     name (`dev` -> "dev", `demo` -> "demo") -- see NOTE below.
  3. `azurerm_resource_group.this.name` resolves to a different concrete
     string for `dev` and `demo` (ADR-016: distinct resource groups).
  4. each root's `backend.tf` points at the same HCP Terraform organization
     but a different workspace (`raffa-dev` vs `raffa-demo`) -- ADR-016:
     distinct remote state, never shared.
  5. for each of postgres/storage/servicebus: the env root's `module` block
     passes its OWN `azurerm_resource_group.this.name` and OWN
     `local.environment` -- not a literal, not the other environment's value.
  6. for each of postgres/storage/servicebus: the module's Azure resource name
     is parameterized by `${var.environment}`, and substituting each root's
     own resolved environment string yields two different identifiers for
     `dev` and `demo` -- i.e. they can never collide (ADR-005: never shared).
  7. neither root reads the other's state via `terraform_remote_state`, and
     neither root's source hardcodes the other environment's resource group
     name.

NOTE on `locals.environment` resolution: `infra/environments/demo/main.tf`
sets `locals { environment = "demo" }` (a literal), but
`infra/environments/dev/main.tf` sets `locals { environment = var.environment
}` (promoted off a literal by task E01/F02/US02/T01, after
`terraform_env_roots_scan.py` was written against the literal-only shape).
`resolve_root_environment` below therefore resolves *either* shape: a literal
directly, or a `var.<name>` reference via that variable's own `default` in
the root's `variables.tf` (dev's `variable "environment"` defaults to "dev"
and its `validation` block forbids any other value). This is why this task
does not reuse `terraform_env_roots_scan.find_locals_environment` (literal-only)
-- against the current repo it returns `None` for the `dev` root (visible by
running `python scripts/terraform_env_roots_scan.py`, which fails the "dev
environment + tags" check for this exact reason). That pre-existing gap
belongs to task E01/F02/US01/T02's file, is out of this task's "Files to
create or modify", and is left untouched; this script simply does not share
its blind spot.

Usage:
    python scripts/demo_isolation_scan.py

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
MODULES_ROOT = INFRA_ROOT / "modules"

ENVS = ("dev", "demo")

# AC-3's explicit list: "no shared Postgres/Storage/Service Bus with dev".
# {module name under infra/modules/: the one Azure resource type in that
# module whose `name` is the thing that must never collide}.
DATASTORE_MODULES = {
    "postgres": "azurerm_postgresql_flexible_server",
    "storage": "azurerm_storage_account",
    "servicebus": "azurerm_servicebus_namespace",
}


# ---------------------------------------------------------------------------
# Small brace-balanced HCL block extraction -- deliberately not a full HCL
# parser, just enough to pull named blocks/attributes out of the fixed shapes
# this repo's env roots and modules use. Self-contained on purpose (see
# module docstring); does not import scripts/terraform_env_roots_scan.py.
# ---------------------------------------------------------------------------

def _strip_line_comments(text: str) -> str:
    """Drop every line whose stripped content starts with '#' or '//'.

    Several files this scan reads (e.g. infra/environments/demo/main.tf,
    infra/environments/demo/outputs.tf) carry long `#`-comment headers that
    mention *this task's own id* and words like "dev"/"rg-raffa-dev" as
    prose. Left unstripped, that text would be indistinguishable from a real
    cross-environment reference to check_no_cross_environment_coupling.
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


def find_resource_block(text: str, resource_type: str, name: str = "this") -> str | None:
    pattern = rf'resource\s+"{re.escape(resource_type)}"\s+"{re.escape(name)}"\s*'
    return _find_block(_strip_line_comments(text), pattern)


def find_module_block(text: str, module_name: str) -> str | None:
    pattern = rf'module\s+"{re.escape(module_name)}"\s*'
    return _find_block(_strip_line_comments(text), pattern)


def find_locals_block(text: str) -> str | None:
    return _find_block(_strip_line_comments(text), r"locals")


def find_variable_block(text: str, var_name: str) -> str | None:
    pattern = rf'variable\s+"{re.escape(var_name)}"\s*'
    return _find_block(_strip_line_comments(text), pattern)


def find_attr(block_text: str | None, attr: str) -> str | None:
    """Return the raw (still-quoted-if-a-string) RHS text of `attr = <RHS>`."""
    if block_text is None:
        return None
    m = re.search(rf"^\s*{re.escape(attr)}\s*=\s*(.+?)\s*$", block_text, re.MULTILINE)
    return m.group(1) if m else None


def unquote(raw: str | None) -> str | None:
    if raw is None:
        return None
    m = re.fullmatch(r'"(.*)"', raw)
    return m.group(1) if m else None


# ---------------------------------------------------------------------------
# Domain helpers
# ---------------------------------------------------------------------------

def resolve_root_environment(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> str | None:
    """Resolve the concrete string an env root's `locals.environment` carries.

    Handles both shapes present in this repo: a literal (`"demo"`) or a
    `var.<name>` reference resolved via that variable's own `default` (`dev`).
    See the module docstring's NOTE for why this does not assume a literal.
    """
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return None
    locals_block = find_locals_block(main_path.read_text(encoding="utf-8"))
    raw = find_attr(locals_block, "environment")
    if raw is None:
        return None
    literal = unquote(raw)
    if literal is not None:
        return literal
    var_m = re.fullmatch(r"var\.([A-Za-z0-9_]+)", raw)
    if not var_m:
        return None
    variables_path = environments_root / env / "variables.tf"
    if not variables_path.is_file():
        return None
    var_block = find_variable_block(variables_path.read_text(encoding="utf-8"), var_m.group(1))
    return unquote(find_attr(var_block, "default"))


def resource_group_name_template(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> str | None:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return None
    block = find_resource_block(main_path.read_text(encoding="utf-8"), "azurerm_resource_group", "this")
    return unquote(find_attr(block, "name"))


def parse_backend_workspace(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> dict:
    backend_path = environments_root / env / "backend.tf"
    if not backend_path.is_file():
        return {"organization": None, "workspace": None}
    text = _strip_line_comments(backend_path.read_text(encoding="utf-8"))
    tf_body = _find_block(text, r"terraform")
    cloud_body = _find_block(tf_body, r"cloud") if tf_body is not None else None
    if cloud_body is None:
        return {"organization": None, "workspace": None}
    organization = unquote(find_attr(cloud_body, "organization"))
    ws_body = _find_block(cloud_body, r"workspaces")
    workspace = unquote(find_attr(ws_body, "name")) if ws_body is not None else None
    return {"organization": organization, "workspace": workspace}


def find_module_arg(
    env: str, module_name: str, arg: str, environments_root: Path = ENVIRONMENTS_ROOT
) -> str | None:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return None
    block = find_module_block(main_path.read_text(encoding="utf-8"), module_name)
    return find_attr(block, arg)


def datastore_name_template(
    module_name: str, resource_type: str, modules_root: Path = MODULES_ROOT
) -> str | None:
    module_main_path = modules_root / module_name / "main.tf"
    if not module_main_path.is_file():
        return None
    block = find_resource_block(module_main_path.read_text(encoding="utf-8"), resource_type, "this")
    return unquote(find_attr(block, "name"))


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring scripts/repo_secret_scan.py
# and scripts/terraform_env_roots_scan.py.
# ---------------------------------------------------------------------------

def check_required_files_present(
    environments_root: Path = ENVIRONMENTS_ROOT, modules_root: Path = MODULES_ROOT
) -> tuple:
    missing = []
    for env in ENVS:
        for f in ("main.tf", "backend.tf", "variables.tf", "outputs.tf"):
            if not (environments_root / env / f).is_file():
                missing.append(f"{env}/{f}")
    for module_name in DATASTORE_MODULES:
        if not (modules_root / module_name / "main.tf").is_file():
            missing.append(f"modules/{module_name}/main.tf")
    if missing:
        return False, f"missing required file(s): {', '.join(missing)}"
    return True, "both env roots and all datastore modules have their main.tf present"


def check_root_environment_identity(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    resolved = resolve_root_environment(env, environments_root)
    if resolved != env:
        return False, f"{env}/main.tf locals.environment resolves to {resolved!r}, expected {env!r}"
    return True, f"{env}/main.tf locals.environment resolves to {resolved!r}"


def check_distinct_resource_groups(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    resolved_names = {}
    for env in ENVS:
        template = resource_group_name_template(env, environments_root)
        if template is None:
            return False, f"{env}/main.tf: azurerm_resource_group.this has no name"
        env_value = resolve_root_environment(env, environments_root)
        if env_value != env:
            return False, f"{env}/main.tf locals.environment resolves to {env_value!r}, expected {env!r}"
        resolved_names[env] = template.replace("${local.environment}", env_value)
    if resolved_names["dev"] == resolved_names["demo"]:
        return False, f"dev and demo resource groups are the same name: {resolved_names['dev']!r}"
    return True, f"distinct resource groups -- dev={resolved_names['dev']!r}, demo={resolved_names['demo']!r}"


def check_distinct_remote_state(environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    parsed = {env: parse_backend_workspace(env, environments_root) for env in ENVS}
    for env in ENVS:
        if parsed[env]["organization"] is None or parsed[env]["workspace"] is None:
            return False, (
                f"{env}/backend.tf: could not parse terraform.cloud.{{organization,workspaces.name}}"
            )
    organizations = {parsed[env]["organization"] for env in ENVS}
    if len(organizations) != 1:
        return False, f"dev and demo point at different HCP Terraform organizations: {parsed}"
    workspaces = {parsed[env]["workspace"] for env in ENVS}
    if len(workspaces) != len(ENVS):
        return False, f"dev and demo share the same HCP Terraform workspace: {parsed}"
    return True, (
        f"dev and demo share organization {next(iter(organizations))!r} but use distinct workspaces: "
        f"dev={parsed['dev']['workspace']!r}, demo={parsed['demo']['workspace']!r}"
    )


def check_module_own_scope(
    env: str, module_name: str, environments_root: Path = ENVIRONMENTS_ROOT
) -> tuple:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    block = find_module_block(main_path.read_text(encoding="utf-8"), module_name)
    if block is None:
        return False, f"{env}/main.tf: module {module_name!r} block not found"
    rg_ref = find_attr(block, "resource_group_name")
    env_ref = find_attr(block, "environment")
    problems = []
    if rg_ref != "azurerm_resource_group.this.name":
        problems.append(f"resource_group_name={rg_ref!r} (expected this root's own azurerm_resource_group.this.name)")
    if env_ref != "local.environment":
        problems.append(f"environment={env_ref!r} (expected this root's own local.environment)")
    if problems:
        return False, f"{env}/main.tf module {module_name!r} not scoped to its own root: " + "; ".join(problems)
    return True, f"{env}/main.tf module {module_name!r} uses its own resource group and environment"


def check_datastore_isolation(
    module_name: str,
    resource_type: str,
    environments_root: Path = ENVIRONMENTS_ROOT,
    modules_root: Path = MODULES_ROOT,
) -> tuple:
    template = datastore_name_template(module_name, resource_type, modules_root)
    if template is None:
        return False, f"modules/{module_name}/main.tf: resource {resource_type!r} \"this\" has no name"
    if "${var.environment}" not in template:
        return False, (
            f"modules/{module_name}/main.tf {resource_type}.this.name={template!r} does not interpolate "
            "${var.environment}; dev and demo could produce the same Azure resource name"
        )
    resolved = {}
    for env in ENVS:
        env_value = resolve_root_environment(env, environments_root)
        if env_value != env:
            return False, f"{env}/main.tf locals.environment resolves to {env_value!r}, expected {env!r}"
        resolved[env] = template.replace("${var.environment}", env_value)
    if resolved["dev"] == resolved["demo"]:
        return False, f"modules/{module_name}: dev and demo would produce the same name {resolved['dev']!r}"
    return True, (
        f"modules/{module_name} {resource_type}.this.name={template!r} never collides -- "
        f"dev={resolved['dev']!r}, demo={resolved['demo']!r}"
    )


def check_no_cross_environment_coupling(
    env: str, environments_root: Path = ENVIRONMENTS_ROOT
) -> tuple:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    text = _strip_line_comments(main_path.read_text(encoding="utf-8"))
    if "terraform_remote_state" in text:
        return False, f"{env}/main.tf reads another workspace's state via terraform_remote_state"
    other = "demo" if env == "dev" else "dev"
    other_rg_template = resource_group_name_template(other, environments_root)
    if other_rg_template is not None:
        other_env_value = resolve_root_environment(other, environments_root)
        if other_env_value:
            other_rg_name = other_rg_template.replace("${local.environment}", other_env_value)
            if other_rg_name and other_rg_name in text:
                return False, f"{env}/main.tf references the {other} resource group name {other_rg_name!r}"
    return True, f"{env}/main.tf has no terraform_remote_state read and no reference to {other}'s resource group"


def run_all_checks(
    environments_root: Path = ENVIRONMENTS_ROOT, modules_root: Path = MODULES_ROOT
) -> list:
    checks = [("required files present", check_required_files_present(environments_root, modules_root))]
    for env in ENVS:
        checks.append((f"{env} environment identity", check_root_environment_identity(env, environments_root)))
    checks.append(("distinct resource groups", check_distinct_resource_groups(environments_root)))
    checks.append(("distinct remote state", check_distinct_remote_state(environments_root)))
    for module_name, resource_type in DATASTORE_MODULES.items():
        for env in ENVS:
            checks.append(
                (f"{env} {module_name} own scope", check_module_own_scope(env, module_name, environments_root))
            )
        checks.append(
            (f"{module_name} isolation", check_datastore_isolation(module_name, resource_type, environments_root, modules_root))
        )
    for env in ENVS:
        checks.append(
            (f"{env} no cross-environment coupling", check_no_cross_environment_coupling(env, environments_root))
        )
    return checks


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print(
            "[demo_isolation_scan] PASS: demo is isolated from dev -- distinct resource group, "
            "distinct HCP state, no shared Postgres/Storage/Service Bus (ADR-005, ADR-016)"
        )
        return 0
    print("[demo_isolation_scan] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
