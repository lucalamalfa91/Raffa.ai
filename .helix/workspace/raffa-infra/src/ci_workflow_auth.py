#!/usr/bin/env python3
"""Structural verification for the reusable Azure OIDC workflow-auth step
(task E01/F03/US01/T02, `ci-workflow-auth`).

Parent story `us-01-ci-azure-oidc` AC-2 ("OIDC federation subject claims
pinned, no client secret stored") and AC-3 ("Workflow files contain only
client-id, tenant-id, subscription-id (no secret)"); ADR-015 (OIDC
federation, per-env SP, no stored secret). Task E01/F03/US01/T01 (already
merged into this branch, `ci-azure-auth`) authored the actual composite
action at `.github/actions/azure-login/action.yml` plus the
`modules/identity` Terraform that backs it. This task's own coding
objective ("Author a reusable azure/login OIDC step (no secret, only
client/tenant/sub)") is proof over that artifact, not a re-decision of
it: it structurally proves the composite action

  1. wraps a single `azure/login` composite step, pinned to a major
     version tag (never a floating branch ref);
  2. declares exactly the three non-secret inputs `client-id`,
     `tenant-id`, `subscription-id` -- no more, no less -- each
     `required: true` (AC-3);
  3. passes every declared input straight through to the `azure/login`
     step with no drift and no undeclared extra `with:` field (closing
     the gap where a fourth, secret field could be hard-coded into the
     step without ever being declared as an input);
  4. contains no secret-shaped material anywhere outside its own
     documentation comments (`client-secret`, `AZURE_CREDENTIALS`,
     `creds:`, `password`, `secrets.*`) -- ADR-015 "no client secret is
     ever stored" (AC-2);
  5. documents the `permissions: id-token: write` a calling workflow
     must grant for GitHub's OIDC provider to issue the token
     `azure/login` exchanges;
  6. stays wired to `modules/identity/outputs.tf` (`ci-azure-auth`,
     T01): every one of that module's three CI-facing non-secret outputs
     (`client_id`, `tenant_id`, `subscription_id`) has a same-named
     (kebab-case) input on this action, so a renamed Terraform output
     cannot silently orphan a workflow input.

Like every other verification task in this repo, this is a static/text
check: there is no Azure subscription or GitHub OIDC provider available
in this harness, so "the step succeeds via federation" is proven here as
"the step is structurally correct and secret-free," not as a live
token exchange.

This is a small, targeted parser for this repo's own composite-action
YAML shape (a flat `inputs:` mapping and a single-step `runs:` block) --
not a general-purpose YAML parser, and deliberately dependency-free
(stdlib only), matching the rest of `workspace/raffa-infra`.

Usage (from `workspace/raffa-infra/`):
    python src/ci_workflow_auth.py

Exit 0 if every check passes. Non-zero otherwise, with a PASS/FAIL line
per check on stdout (the failure summary is also echoed to stderr).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

SRC_ROOT = Path(__file__).resolve().parent
REPO_ROOT = SRC_ROOT.parent

ACTION_PATH = REPO_ROOT / ".github" / "actions" / "azure-login" / "action.yml"
IDENTITY_OUTPUTS_PATH = REPO_ROOT / "modules" / "identity" / "outputs.tf"

# ADR-015 AC-3: these three non-secret fields are the whole interface.
EXPECTED_INPUTS = ("client-id", "tenant-id", "subscription-id")

# ADR-015: the CI identity's Terraform outputs (T01, ci-azure-auth) that
# this action's inputs must stay wired to, one-for-one (snake_case ->
# kebab-case).
REQUIRED_IDENTITY_OUTPUTS = ("client_id", "tenant_id", "subscription_id")

_AZURE_LOGIN_USES_RE = re.compile(r"^azure/login@v\d+(?:\.\d+){0,2}$")

# Secret-shaped markers that must never appear outside a comment. Matched
# case-insensitively against the comment-stripped body.
SECRET_MARKERS = (
    "client-secret",
    "clientsecret",
    "azure_credentials",
    "creds:",
    "password",
    "secrets.",
)


# ---------------------------------------------------------------------------
# Minimal YAML block parsing -- see module docstring for scope/limits.
# ---------------------------------------------------------------------------

def _strip_comment_lines(text: str) -> str:
    """Drop whole-line YAML comments (lines whose stripped content starts
    with '#'). action.yml in this repo only ever uses full-line comments
    (its header/usage block), never trailing inline ones, so this avoids
    the harder "# inside a quoted string" ambiguity for no real benefit
    here."""
    return "\n".join(
        line for line in text.splitlines() if not line.strip().startswith("#")
    )


def _indent_of(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def _unquote(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
        return value[1:-1]
    return value


def _block_after(lines: list[str], key: str, base_indent: int) -> list[str]:
    """Lines strictly more indented than `base_indent`, starting right
    after a `<key>:` line found at exactly `base_indent`, up to (but not
    including) the next line at or below `base_indent`."""
    out: list[str] = []
    in_block = False
    for line in lines:
        if not in_block:
            if _indent_of(line) == base_indent and line.strip() == f"{key}:":
                in_block = True
            continue
        if line.strip() == "":
            out.append(line)
            continue
        if _indent_of(line) <= base_indent:
            break
        out.append(line)
    return out


def _parse_inputs_block(block_lines: list[str]) -> dict[str, dict[str, str]]:
    inputs: dict[str, dict[str, str]] = {}
    current: str | None = None
    for line in block_lines:
        if not line.strip():
            continue
        indent = _indent_of(line)
        stripped = line.strip()
        if indent == 2 and stripped.endswith(":"):
            current = stripped[:-1].strip()
            inputs[current] = {}
        elif indent >= 4 and current is not None and ":" in stripped:
            k, _, v = stripped.partition(":")
            inputs[current][k.strip()] = _unquote(v.strip())
    return inputs


def _parse_runs_block(lines: list[str]) -> dict:
    runs_block = _block_after(lines, "runs", 0)

    using = None
    for line in runs_block:
        s = line.strip()
        if s.startswith("using:"):
            using = _unquote(s.split(":", 1)[1].strip())
            break

    steps_block = _block_after(runs_block, "steps", 2)
    step_count = sum(1 for line in steps_block if line.strip().startswith("- "))

    uses = None
    with_indent = None
    for line in steps_block:
        s = line.strip()
        if uses is None and s.startswith("uses:"):
            uses = _unquote(s.split(":", 1)[1].strip())
        core = s[2:].strip() if s.startswith("- ") else s
        if with_indent is None and core == "with:":
            with_indent = _indent_of(line)

    with_map: dict[str, str] = {}
    if with_indent is not None:
        for line in _block_after(steps_block, "with", with_indent):
            s = line.strip()
            if s and ":" in s:
                k, _, v = s.partition(":")
                with_map[k.strip()] = _unquote(v.strip())

    return {"using": using, "step_count": step_count, "uses": uses, "with": with_map}


def parse_action_yaml(text: str) -> dict:
    """Parse the subset of a composite action.yml this task cares about:
    the `inputs:` mapping and the single-step `runs:` block. Returns a
    dict with keys `inputs`, `using`, `step_count`, `uses`, `with`."""
    lines = _strip_comment_lines(text).splitlines()
    inputs = _parse_inputs_block(_block_after(lines, "inputs", 0))
    runs = _parse_runs_block(lines)
    return {
        "inputs": inputs,
        "using": runs["using"],
        "step_count": runs["step_count"],
        "uses": runs["uses"],
        "with": runs["with"],
    }


# ---------------------------------------------------------------------------
# check_* -- each returns (passed, detail), mirroring the sibling
# structural-verification scripts in this repo.
# ---------------------------------------------------------------------------

def check_action_file_exists(path: Path = ACTION_PATH) -> tuple[bool, str]:
    if not path.is_file():
        return False, f"{path} does not exist"
    return True, f"{path} exists"


def check_composite_single_azure_login_step(parsed: dict) -> tuple[bool, str]:
    if parsed["using"] != "composite":
        return False, f"runs.using={parsed['using']!r}, expected 'composite'"
    if parsed["step_count"] != 1:
        return False, f"runs.steps has {parsed['step_count']} step(s), expected exactly 1"
    uses = parsed["uses"] or ""
    if not _AZURE_LOGIN_USES_RE.match(uses):
        return False, f"step uses={uses!r}, expected a pinned 'azure/login@vN' tag"
    return True, f"single composite step uses {uses!r}"


def check_inputs_exactly_non_secret_three(parsed: dict) -> tuple[bool, str]:
    names = set(parsed["inputs"])
    expected = set(EXPECTED_INPUTS)
    if names != expected:
        problems = []
        missing = expected - names
        extra = names - expected
        if missing:
            problems.append(f"missing {sorted(missing)}")
        if extra:
            problems.append(f"unexpected {sorted(extra)}")
        return False, "; ".join(problems)
    return True, f"inputs are exactly {sorted(expected)} (AC-3)"


def check_inputs_all_required(parsed: dict) -> tuple[bool, str]:
    not_required = [
        name
        for name in EXPECTED_INPUTS
        if name in parsed["inputs"] and parsed["inputs"][name].get("required") != "true"
    ]
    if not_required:
        return False, f"input(s) not required: true: {not_required}"
    return True, "client-id, tenant-id, subscription-id are all required: true"


def check_step_wires_inputs_without_drift(parsed: dict) -> tuple[bool, str]:
    with_map = parsed["with"]
    expected = set(EXPECTED_INPUTS)
    actual = set(with_map)
    if actual != expected:
        return False, f"step 'with:' keys are {sorted(actual)}, expected exactly {sorted(expected)}"
    problems = []
    for name in EXPECTED_INPUTS:
        want = f"${{{{ inputs.{name} }}}}"
        got = with_map.get(name)
        if got != want:
            problems.append(f"{name}: with-value {got!r} != expected {want!r}")
    if problems:
        return False, "; ".join(problems)
    return True, "each declared input is passed straight through to azure/login with no drift"


def check_no_secret_material(text: str) -> tuple[bool, str]:
    body = _strip_comment_lines(text).lower()
    hits = [marker for marker in SECRET_MARKERS if marker in body]
    if hits:
        return False, f"secret-shaped marker(s) found outside comments: {hits}"
    return True, "no secret-shaped field (client-secret/AZURE_CREDENTIALS/creds/password/secrets.*) found"


def check_documents_id_token_permission(text: str) -> tuple[bool, str]:
    if "permissions:" not in text:
        return False, "usage documentation does not mention a 'permissions:' block"
    if "id-token: write" not in text:
        return False, "usage documentation does not mention 'id-token: write'"
    return True, "documents the required 'permissions: id-token: write' for callers"


def check_wired_to_identity_module_outputs(
    parsed: dict, outputs_path: Path = IDENTITY_OUTPUTS_PATH
) -> tuple[bool, str]:
    if not outputs_path.is_file():
        return False, f"{outputs_path} does not exist"
    text = re.sub(r"#.*", "", outputs_path.read_text(encoding="utf-8"))
    declared = set(re.findall(r'output\s+"([a-zA-Z0-9_]+)"\s*{', text))
    missing_tf = set(REQUIRED_IDENTITY_OUTPUTS) - declared
    if missing_tf:
        return False, f"{outputs_path} is missing output(s) {sorted(missing_tf)} (ci-azure-auth, T01)"
    expected_inputs = {name.replace("_", "-") for name in REQUIRED_IDENTITY_OUTPUTS}
    missing_inputs = expected_inputs - set(parsed["inputs"])
    if missing_inputs:
        return False, (
            f"modules/identity/outputs.tf outputs {sorted(REQUIRED_IDENTITY_OUTPUTS)} but "
            f"action.yml inputs are missing {sorted(missing_inputs)} -- ci-workflow-auth has "
            "drifted from ci-azure-auth"
        )
    return True, (
        f"action.yml inputs {sorted(expected_inputs)} stay wired to modules/identity/outputs.tf's "
        f"{sorted(REQUIRED_IDENTITY_OUTPUTS)} (ci-azure-auth -> ci-workflow-auth)"
    )


def run_all_checks(
    action_path: Path = ACTION_PATH,
    outputs_path: Path = IDENTITY_OUTPUTS_PATH,
) -> list[tuple[str, tuple[bool, str]]]:
    exists = check_action_file_exists(action_path)
    results: list[tuple[str, tuple[bool, str]]] = [("azure-login/action.yml exists", exists)]
    if not exists[0]:
        return results

    text = action_path.read_text(encoding="utf-8")
    parsed = parse_action_yaml(text)
    results.extend(
        [
            ("single composite azure/login step, pinned version", check_composite_single_azure_login_step(parsed)),
            ("inputs are exactly the 3 non-secret fields (AC-3)", check_inputs_exactly_non_secret_three(parsed)),
            ("all 3 inputs are required", check_inputs_all_required(parsed)),
            ("step wires inputs through with no drift", check_step_wires_inputs_without_drift(parsed)),
            ("no secret material (ADR-015 AC-2/AC-3)", check_no_secret_material(text)),
            ("documents id-token: write permission", check_documents_id_token_permission(text)),
            (
                "wired to modules/identity/outputs.tf (ci-azure-auth)",
                check_wired_to_identity_module_outputs(parsed, outputs_path),
            ),
        ]
    )
    return results


def main() -> int:
    ok = True
    for name, (passed, detail) in run_all_checks():
        print(f"[{'PASS' if passed else 'FAIL'}] {name}: {detail}")
        ok = ok and passed

    if ok:
        print(
            "[ci_workflow_auth] PASS: reusable azure/login OIDC step (ci-workflow-auth) is "
            "secret-free, correctly wired, and stays consistent with ci-azure-auth"
        )
        return 0
    print("[ci_workflow_auth] FAIL: see above", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
