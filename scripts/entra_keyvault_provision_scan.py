#!/usr/bin/env python3
"""Structural scan for Entra app registrations + Key Vault grants
(task E01/F02/US04/T01, entra-keyvault-provision).

Parent story `us-04-entra-keyvault` AC-1..AC-3 and this task's own
definition of done ("terraform plan shows 4 Entra registrations + 2 Key
Vaults + scoped access policies, no secret literal") need a repeatable,
credential-free proof. A real `terraform plan` needs a live HCP Terraform
login plus an authenticated azurerm/azuread provider -- this task's own
frontmatter comments out `requires: [azure_subscription]` and
`requires: [hcp_terraform]`, and neither is available in this harness.
`terraform validate` (run separately against both env roots, see this
task's turn) already proves the HCL is syntactically and referentially
sound; this script is the standing, repeatable proof of the *shape* a
`terraform plan` would otherwise show:

  - modules/identity declares exactly two Entra app registrations ("api",
    "public_client") -- each environment root instantiates the module once
    (dev, demo; membership already covered by
    scripts/terraform_env_roots_scan.py), so 2 x 2 = four registrations.
  - the "api" registration exposes exactly Raffa.Read/Raffa.Write as
    enabled, delegated ("User") scopes.
  - the "public_client" registration is PKCE-only: a single_page_application
    redirect (web, ADR-012) wired from `local.web_redirect_uri`, and a
    public_client redirect of exactly "raffa://callback" (native,
    ADR-013) -- and neither application ever declares a `password {}`
    block (ADR-010/ADR-011: no client secret).
  - the public client declares required_resource_access for both scopes,
    and the API pre-authorizes it for both (azuread_application_pre_authorized)
    so the Authorization Code + PKCE flow never needs admin consent.
  - modules/identity/outputs.tf exposes the non-secret values a later task
    needs: both client ids, the API's audience (App ID URI), the OIDC
    issuer, and the managed identity's principal id.
  - modules/keyvault declares exactly one Key Vault ("this") -- one per
    environment root, so 1 x 2 = two vaults -- and grants it via
    `azurerm_role_assignment` (RBAC "Key Vault Secrets User", the role
    equivalent of the legacy access-policy get+list ADR-011 calls for,
    required because this vault has `rbac_authorization_enabled = true`)
    to a `var.workload_principal_id` input -- never a literal.
  - both infra/environments/{dev,demo}/main.tf wire that input from their
    OWN `module.identity.workload_principal_id` -- never a literal, never
    the other environment's -- so the grant can never cross dev/demo.
  - none of the files this task touches contain a secret-shaped literal
    (reuses scripts/repo_secret_scan.py's patterns).

Checks, all read-only, no network, no `terraform` binary, no Azure/Entra
credentials required.

Usage:
    python scripts/entra_keyvault_provision_scan.py

Exit 0 if every check passes. Non-zero otherwise, with a PASS/FAIL line per
check on stdout (failures also echoed to stderr).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

SCRIPTS_ROOT = Path(__file__).resolve().parent
if str(SCRIPTS_ROOT) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_ROOT))

from terraform_env_roots_scan import _extract_block, _find_block, _strip_line_comments  # noqa: E402
import repo_secret_scan as secret_scan  # noqa: E402

REPO_ROOT = SCRIPTS_ROOT.parent
INFRA_ROOT = REPO_ROOT / "infra"
MODULES_ROOT = INFRA_ROOT / "modules"
ENVIRONMENTS_ROOT = INFRA_ROOT / "environments"
IDENTITY_DIR = MODULES_ROOT / "identity"
KEYVAULT_DIR = MODULES_ROOT / "keyvault"

ENVS = ("dev", "demo")

# scope value -> expected raw `id = <expr>` right-hand side inside its
# oauth2_permission_scope block.
EXPECTED_SCOPES = {
    "Raffa.Read": "random_uuid.scope_read.result",
    "Raffa.Write": "random_uuid.scope_write.result",
}

EXPECTED_IDENTITY_OUTPUTS = {
    "public_client_id": "azuread_application.public_client.client_id",
    "api_client_id": "azuread_application.api.client_id",
    "api_identifier_uri": "one(azuread_application.api.identifier_uris)",
    "issuer": '"https://login.microsoftonline.com/${data.azuread_client_config.current.tenant_id}/v2.0"',
    "workload_principal_id": "azurerm_user_assigned_identity.workload.principal_id",
}

FILES_TO_SECRET_SCAN_RELATIVE = (
    "modules/identity/main.tf",
    "modules/identity/outputs.tf",
    "modules/identity/variables.tf",
    "modules/keyvault/main.tf",
    "modules/keyvault/variables.tf",
    "modules/keyvault/outputs.tf",
    "environments/dev/main.tf",
    "environments/demo/main.tf",
)


# ---------------------------------------------------------------------------
# Small parsing helpers this scan needs beyond terraform_env_roots_scan's
# brace-balanced primitives (imported above): finding every block of a
# repeated resource type, and a raw single-line `attr = <RHS>` lookup
# (same shape as scripts/demo_isolation_scan.py's own find_attr, duplicated
# rather than imported since that script is deliberately self-contained).
# Multi-line list attributes (e.g. `permission_ids = [\n ...\n]`) are
# deliberately NOT extracted this way -- checks that need one search for
# the expected element text as a substring of the whole block instead.
# ---------------------------------------------------------------------------

def find_attr(block_text, attr):
    if block_text is None:
        return None
    m = re.search(rf"^\s*{re.escape(attr)}\s*=\s*(.+?)\s*$", block_text, re.MULTILINE)
    return m.group(1) if m else None


def find_resource_block(text, resource_type, name):
    pattern = rf'resource\s+"{re.escape(resource_type)}"\s+"{re.escape(name)}"\s*'
    return _find_block(_strip_line_comments(text), pattern)


def find_resource_names_of_type(text, resource_type):
    """Return the set of resource names for every `resource "<resource_type>" "<name>" {` block."""
    text = _strip_line_comments(text)
    pattern = rf'resource\s+"{re.escape(resource_type)}"\s+"([A-Za-z0-9_-]+)"\s*{{'
    return {m.group(1) for m in re.finditer(pattern, text)}


def find_all_blocks(text, header_pattern):
    """Return every balanced block body matching `<header_pattern>\\s*{`, in source order."""
    bodies = []
    for m in re.finditer(header_pattern + r"\s*{", text):
        bodies.append(_extract_block(text, m.end() - 1))
    return bodies


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring the sibling scan
# scripts (scripts/terraform_env_roots_scan.py, scripts/demo_isolation_scan.py,
# scripts/dev_outputs_verify.py). Root paths are parameters defaulting to the
# real repo layout so tests can point them at a synthetic fixture tree.
# ---------------------------------------------------------------------------

def check_module_files_present(identity_dir: Path = IDENTITY_DIR, keyvault_dir: Path = KEYVAULT_DIR) -> tuple:
    missing = []
    for base, names in (
        (identity_dir, ("main.tf", "outputs.tf", "variables.tf")),
        (keyvault_dir, ("main.tf", "outputs.tf", "variables.tf")),
    ):
        for name in names:
            if not (base / name).is_file():
                missing.append(f"{base.name}/{name}")
    if missing:
        return False, f"missing file(s): {', '.join(missing)}"
    return True, "modules/identity and modules/keyvault have main.tf/outputs.tf/variables.tf"


def check_two_app_registrations(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    names = find_resource_names_of_type(path.read_text(encoding="utf-8"), "azuread_application")
    if names != {"api", "public_client"}:
        return False, (
            f"modules/identity/main.tf azuread_application resources = {sorted(names)}, "
            "expected ['api', 'public_client']"
        )
    return True, (
        "modules/identity/main.tf declares exactly 2 azuread_application resources "
        "(api, public_client) -- x2 env roots (dev, demo) = 4 registrations total"
    )


def check_api_scopes(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    app_body = find_resource_block(text, "azuread_application", "api")
    if app_body is None:
        return False, 'modules/identity/main.tf has no resource "azuread_application" "api"'
    api_block = _find_block(app_body, r"\bapi\b")
    if api_block is None:
        return False, "azuread_application.api has no nested api {} block"
    scope_bodies = find_all_blocks(api_block, r"oauth2_permission_scope")
    found = {}
    for body in scope_bodies:
        raw_value = find_attr(body, "value")
        value = raw_value.strip('"') if raw_value else raw_value
        found[value] = {
            "id": find_attr(body, "id"),
            "type": find_attr(body, "type"),
            "enabled": find_attr(body, "enabled"),
        }
    missing = [v for v in EXPECTED_SCOPES if v not in found]
    if missing:
        return False, f"azuread_application.api api{{}} missing oauth2_permission_scope value(s): {', '.join(missing)}"
    problems = []
    for value, expected_id_expr in EXPECTED_SCOPES.items():
        scope = found[value]
        if scope["id"] != expected_id_expr:
            problems.append(f"{value} id={scope['id']!r} expected {expected_id_expr!r}")
        if scope["type"] != '"User"':
            problems.append(f"{value} type={scope['type']!r} expected '\"User\"'")
        if scope["enabled"] != "true":
            problems.append(f"{value} enabled={scope['enabled']!r} expected 'true'")
    if problems:
        return False, "; ".join(problems)
    return True, f"azuread_application.api exposes exactly {sorted(EXPECTED_SCOPES)} as enabled delegated (User) scopes"


def check_public_client_pkce(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    body = find_resource_block(text, "azuread_application", "public_client")
    if body is None:
        return False, 'modules/identity/main.tf has no resource "azuread_application" "public_client"'
    if re.search(r"\bpassword\s*{", body):
        return False, "azuread_application.public_client declares a password {} block (client secret) -- ADR-010 forbids one"
    spa_body = _find_block(body, r"single_page_application")
    if spa_body is None:
        return False, "azuread_application.public_client has no single_page_application {} block (web redirect)"
    spa_uris = find_attr(spa_body, "redirect_uris")
    if spa_uris != "[local.web_redirect_uri]":
        return False, f"single_page_application.redirect_uris={spa_uris!r}, expected '[local.web_redirect_uri]'"
    native_body = _find_block(body, r"\bpublic_client\b")
    if native_body is None:
        return False, "azuread_application.public_client has no public_client {} block (native redirect)"
    native_uris = find_attr(native_body, "redirect_uris")
    if native_uris is None or "raffa://callback" not in native_uris:
        return False, f'public_client{{}}.redirect_uris={native_uris!r}, expected to contain "raffa://callback"'
    return True, (
        "azuread_application.public_client has single_page_application(local.web_redirect_uri) + "
        'public_client(raffa://callback) redirects, no password {} block'
    )


def check_required_resource_access(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    body = find_resource_block(text, "azuread_application", "public_client")
    if body is None:
        return False, 'modules/identity/main.tf has no resource "azuread_application" "public_client"'
    rra_body = _find_block(body, r"required_resource_access")
    if rra_body is None:
        return False, "azuread_application.public_client has no required_resource_access {} block"
    resource_app_id = find_attr(rra_body, "resource_app_id")
    if resource_app_id != "azuread_application.api.client_id":
        return False, (
            f"required_resource_access.resource_app_id={resource_app_id!r}, "
            "expected 'azuread_application.api.client_id'"
        )
    missing = [
        value
        for value in EXPECTED_SCOPES
        if f'azuread_application.api.oauth2_permission_scope_ids["{value}"]' not in rra_body
    ]
    if missing:
        return False, f"required_resource_access missing resource_access for scope(s): {', '.join(missing)}"
    return True, "azuread_application.public_client declares required_resource_access for both API scopes"


def check_api_no_secret(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    body = find_resource_block(text, "azuread_application", "api")
    if body is None:
        return False, 'modules/identity/main.tf has no resource "azuread_application" "api"'
    if re.search(r"\bpassword\s*{", body):
        return False, "azuread_application.api declares a password {} block (client secret) -- ADR-011 forbids one"
    return True, "azuread_application.api has no password {} block"


def check_pre_authorization(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "main.tf"
    if not path.is_file():
        return False, "modules/identity/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    names = find_resource_names_of_type(text, "azuread_application_pre_authorized")
    if not names:
        return False, "modules/identity/main.tf has no azuread_application_pre_authorized resource"
    problems = []
    for name in names:
        body = find_resource_block(text, "azuread_application_pre_authorized", name)
        app_id = find_attr(body, "application_id")
        client_id = find_attr(body, "authorized_client_id")
        if app_id != "azuread_application.api.id":
            problems.append(f"{name}.application_id={app_id!r}, expected 'azuread_application.api.id'")
        if client_id != "azuread_application.public_client.client_id":
            problems.append(
                f"{name}.authorized_client_id={client_id!r}, expected 'azuread_application.public_client.client_id'"
            )
        for value in EXPECTED_SCOPES:
            needle = f'azuread_application.api.oauth2_permission_scope_ids["{value}"]'
            if needle not in body:
                problems.append(f"{name}.permission_ids missing {needle}")
    if problems:
        return False, "; ".join(problems)
    return True, "azuread_application_pre_authorized pre-authorizes the public client for both scopes on the api application"


def check_identity_outputs(identity_dir: Path = IDENTITY_DIR) -> tuple:
    path = identity_dir / "outputs.tf"
    if not path.is_file():
        return False, "modules/identity/outputs.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    found = {}
    for m in re.finditer(r'output\s+"([A-Za-z0-9_-]+)"\s*{', text):
        body = _extract_block(text, m.end() - 1)
        found[m.group(1)] = find_attr(body, "value")
    missing = [name for name in EXPECTED_IDENTITY_OUTPUTS if name not in found]
    if missing:
        return False, f"modules/identity/outputs.tf missing output(s): {', '.join(missing)}"
    mismatched = [name for name, expr in EXPECTED_IDENTITY_OUTPUTS.items() if found.get(name) != expr]
    if mismatched:
        detail = ", ".join(
            f"{name} value={found.get(name)!r} expected {EXPECTED_IDENTITY_OUTPUTS[name]!r}" for name in mismatched
        )
        return False, f"modules/identity/outputs.tf output value mismatch: {detail}"
    return True, f"modules/identity/outputs.tf declares all {len(EXPECTED_IDENTITY_OUTPUTS)} required outputs"


def check_one_key_vault(keyvault_dir: Path = KEYVAULT_DIR) -> tuple:
    path = keyvault_dir / "main.tf"
    if not path.is_file():
        return False, "modules/keyvault/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    names = find_resource_names_of_type(text, "azurerm_key_vault")
    if names != {"this"}:
        return False, f"modules/keyvault/main.tf azurerm_key_vault resources = {sorted(names)}, expected ['this']"
    body = find_resource_block(text, "azurerm_key_vault", "this")
    if find_attr(body, "rbac_authorization_enabled") != "true":
        return False, "modules/keyvault/main.tf azurerm_key_vault.this rbac_authorization_enabled != true"
    return True, (
        "modules/keyvault/main.tf declares exactly 1 azurerm_key_vault (rbac_authorization_enabled=true) "
        "-- x2 env roots (dev, demo) = 2 vaults total"
    )


def check_keyvault_role_assignment(keyvault_dir: Path = KEYVAULT_DIR) -> tuple:
    path = keyvault_dir / "main.tf"
    if not path.is_file():
        return False, "modules/keyvault/main.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    names = find_resource_names_of_type(text, "azurerm_role_assignment")
    if "workload_secrets_user" not in names:
        return False, 'modules/keyvault/main.tf has no azurerm_role_assignment.workload_secrets_user'
    problems = []
    for name in names:
        body = find_resource_block(text, "azurerm_role_assignment", name)
        scope = find_attr(body, "scope")
        if scope != "azurerm_key_vault.this.id":
            problems.append(f"{name}.scope={scope!r}, expected 'azurerm_key_vault.this.id' (this module's own vault only)")
        # Additional assignments (e.g. deployer Secrets Officer so apply can
        # write connection-string secrets) are allowed, but every grant must
        # stay on this vault. The workload grant itself is the T01 contract.
        if name == "workload_secrets_user":
            role = find_attr(body, "role_definition_name")
            principal = find_attr(body, "principal_id")
            if role != '"Key Vault Secrets User"':
                problems.append(f"{name}.role_definition_name={role!r}, expected '\"Key Vault Secrets User\"'")
            if principal != "var.workload_principal_id":
                problems.append(f"{name}.principal_id={principal!r}, expected 'var.workload_principal_id' (never a literal)")
    if problems:
        return False, "; ".join(problems)
    return True, (
        "modules/keyvault/main.tf grants var.workload_principal_id 'Key Vault Secrets User' "
        "scoped to this module's own azurerm_key_vault.this only"
    )


def check_keyvault_variable(keyvault_dir: Path = KEYVAULT_DIR) -> tuple:
    path = keyvault_dir / "variables.tf"
    if not path.is_file():
        return False, "modules/keyvault/variables.tf does not exist"
    text = _strip_line_comments(path.read_text(encoding="utf-8"))
    body = _find_block(text, r'variable\s+"workload_principal_id"\s*')
    if body is None:
        return False, 'modules/keyvault/variables.tf has no variable "workload_principal_id"'
    var_type = find_attr(body, "type")
    if var_type != "string":
        return False, f'variable "workload_principal_id" type={var_type!r}, expected "string"'
    if find_attr(body, "default") is not None:
        return False, 'variable "workload_principal_id" has a default -- it must be required so every caller wires it explicitly'
    return True, 'modules/keyvault/variables.tf declares a required variable "workload_principal_id" (string, no default)'


def check_env_root_wires_own_identity(env: str, environments_root: Path = ENVIRONMENTS_ROOT) -> tuple:
    main_path = environments_root / env / "main.tf"
    if not main_path.is_file():
        return False, f"{env}/main.tf does not exist"
    text = _strip_line_comments(main_path.read_text(encoding="utf-8"))
    block = _find_block(text, r'module\s+"keyvault"\s*')
    if block is None:
        return False, f'{env}/main.tf has no module "keyvault" block'
    value = find_attr(block, "workload_principal_id")
    if value != "module.identity.workload_principal_id":
        return False, (
            f'{env}/main.tf module "keyvault".workload_principal_id={value!r}, '
            "expected 'module.identity.workload_principal_id' (this root's own identity module instance)"
        )
    return True, f'{env}/main.tf module "keyvault" is granted this root\'s own module.identity.workload_principal_id'


def check_no_secret_literals(repo_root: Path = REPO_ROOT, infra_root: Path = INFRA_ROOT) -> tuple:
    hits = []
    scanned = 0
    for rel in FILES_TO_SECRET_SCAN_RELATIVE:
        path = infra_root / rel
        if not path.is_file():
            return False, f"infra/{rel} does not exist"
        try:
            display = str(path.relative_to(repo_root)).replace("\\", "/")
        except ValueError:
            display = f"infra/{rel}"
        text = path.read_text(encoding="utf-8", errors="ignore")
        scanned += 1
        hits.extend(secret_scan.find_secret_matches(display, text))
    if hits:
        return False, "; ".join(hits)
    return True, f"{scanned} file(s) scanned (identity + keyvault modules, dev/demo env roots), no secret-shaped strings found"


def run_all_checks(
    identity_dir: Path = IDENTITY_DIR,
    keyvault_dir: Path = KEYVAULT_DIR,
    environments_root: Path = ENVIRONMENTS_ROOT,
    repo_root: Path = REPO_ROOT,
    infra_root: Path = INFRA_ROOT,
) -> list:
    checks = [
        ("module files present", check_module_files_present(identity_dir, keyvault_dir)),
        ("two Entra app registrations", check_two_app_registrations(identity_dir)),
        ("api scopes", check_api_scopes(identity_dir)),
        ("public client PKCE + no secret", check_public_client_pkce(identity_dir)),
        ("public client required_resource_access", check_required_resource_access(identity_dir)),
        ("api no secret", check_api_no_secret(identity_dir)),
        ("public client pre-authorized", check_pre_authorization(identity_dir)),
        ("identity outputs", check_identity_outputs(identity_dir)),
        ("one Key Vault per env root", check_one_key_vault(keyvault_dir)),
        ("Key Vault role assignment", check_keyvault_role_assignment(keyvault_dir)),
        ("keyvault workload_principal_id variable", check_keyvault_variable(keyvault_dir)),
    ]
    for env in ENVS:
        checks.append(
            (f"{env} wires its own identity's principal id", check_env_root_wires_own_identity(env, environments_root))
        )
    checks.append(("no secret literals", check_no_secret_literals(repo_root, infra_root)))
    return checks


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print(
            "[entra_keyvault_provision_scan] PASS: 4 Entra registrations (2 per env x dev/demo), "
            "2 Key Vaults with per-env RBAC grants, no secret literal (ADR-010, ADR-011)"
        )
        return 0
    print("[entra_keyvault_provision_scan] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
