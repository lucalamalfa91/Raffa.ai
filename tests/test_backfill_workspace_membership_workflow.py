"""Static structural checks for `.github/workflows/backfill-workspace-membership.yml`
and the one-line schema guard it requires in `.github/workflows/seed-demo-fixture.yml`
(task E14/F05/US01/T01, membership-seed-and-backfill).

Like every other verification task in this repo (see
`scripts/ci_path_filters_verify.py`'s own module docstring), there is no
GitHub Actions runner available in this harness, so "the workflow behaves
correctly" is proven here as "the workflow is structurally correct" --
declares the typed `choice` + `workflow_call` inputs, the `environment:`
gate, the OIDC login action, the schema precondition guard, and a
fail-if-zero (not merely-print) closing verification. This is a small,
targeted text/regex check for this repo's own GitHub Actions YAML shape,
deliberately dependency-free (stdlib only, no PyYAML) -- matching
`ci_path_filters_verify.py`'s own "not a general-purpose YAML parser"
convention -- rather than a full parse.

The DoD's other two proofs are *not* pytest's job and are not duplicated
here: running `demo-fixture-seed.sql` twice against a scratch database
(documented in the task log), and `git diff --name-only origin/main --
.github/workflows` listing exactly two files (a shell command, not a static
text property of one file).

Run:
    python tests/test_backfill_workspace_membership_workflow.py -v
"""

from __future__ import annotations

import re
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
WORKFLOWS_DIR = REPO_ROOT / ".github" / "workflows"
BACKFILL_WORKFLOW = WORKFLOWS_DIR / "backfill-workspace-membership.yml"
SEED_WORKFLOW = WORKFLOWS_DIR / "seed-demo-fixture.yml"


def _read(path: Path) -> str:
    if not path.is_file():
        raise AssertionError(f"{path} does not exist")
    return path.read_text(encoding="utf-8")


def _block_between(text: str, start_pattern: str, end_pattern: str | None) -> str:
    """The text from the first match of `start_pattern` up to (but not
    including) the next match of `end_pattern` at or after it -- or to the
    end of the string if `end_pattern` is None or not found. A small helper
    to scope an assertion to one YAML section (e.g. `workflow_dispatch:`
    only, not `workflow_call:` too) without a full parser."""
    start = re.search(start_pattern, text)
    if start is None:
        raise AssertionError(f"pattern {start_pattern!r} not found")
    tail = text[start.end():]
    if end_pattern is None:
        return tail
    end = re.search(end_pattern, tail)
    return tail if end is None else tail[: end.start()]


# ---------------------------------------------------------------------------
# backfill-workspace-membership.yml
# ---------------------------------------------------------------------------

class WorkflowFileExistsTests(unittest.TestCase):
    def test_backfill_workflow_exists(self) -> None:
        self.assertTrue(BACKFILL_WORKFLOW.is_file(), f"{BACKFILL_WORKFLOW} does not exist")

    def test_workflow_name_matches_filename(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        pattern = re.compile(r"^name:\s*backfill-workspace-membership\s*$", re.MULTILINE)
        self.assertRegex(text, pattern, "workflow `name:` must match its filename")


class TypedChoiceAndWorkflowCallTests(unittest.TestCase):
    """AC-4 / coding objective point 3: `workflow_dispatch` with
    `target_environment` as a typed `choice` (the seed-demo-fixture.yml
    `:15-22` shape) plus `workflow_call`."""

    def setUp(self) -> None:
        self.text = _read(BACKFILL_WORKFLOW)
        self.dispatch_block = _block_between(self.text, r"\n  workflow_dispatch:\n", r"\n  workflow_call:\n")
        self.call_block = _block_between(self.text, r"\n  workflow_call:\n", r"\npermissions:\n")

    def test_dispatch_declares_target_environment_as_typed_choice(self) -> None:
        self.assertIn("target_environment:", self.dispatch_block)
        self.assertRegex(self.dispatch_block, r"type:\s*choice")
        self.assertRegex(self.dispatch_block, r"options:\s*\[dev,\s*demo\]")
        self.assertRegex(self.dispatch_block, r"required:\s*true")

    def test_workflow_call_also_declares_target_environment(self) -> None:
        self.assertIn("target_environment:", self.call_block)
        self.assertRegex(self.call_block, r"type:\s*string")

    def test_dispatch_declares_required_pairs_string_input(self) -> None:
        self.assertIn("pairs:", self.dispatch_block)
        pairs_block = _block_between(self.dispatch_block, r"\n      pairs:\n", None)
        self.assertRegex(pairs_block, r"required:\s*true")
        self.assertRegex(pairs_block, r"type:\s*string")

    def test_workflow_call_also_declares_pairs(self) -> None:
        self.assertIn("pairs:", self.call_block)


class EnvironmentGateTests(unittest.TestCase):
    """`environment: ${{ inputs.target_environment }}` so `demo` inherits
    its required reviewers (ADR-014/ADR-016), exactly like
    seed-demo-fixture.yml's own job."""

    def test_job_environment_keys_off_target_environment_input(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("environment: ${{ inputs.target_environment }}", text)

    def test_job_requests_id_token_write_for_oidc(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertRegex(text, r"id-token:\s*write")


class OidcLoginActionTests(unittest.TestCase):
    """ADR-015: OIDC via ./.github/actions/azure-login, the same non-secret
    client-id/tenant-id/subscription-id inputs every other deploy-shaped
    workflow in this repo uses. No new identity, no stored secret."""

    def test_uses_the_shared_azure_login_composite_action(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("uses: ./.github/actions/azure-login", text)
        self.assertIn("client-id: ${{ vars.AZURE_CLIENT_ID }}", text)
        self.assertIn("tenant-id: ${{ vars.AZURE_TENANT_ID }}", text)
        self.assertIn("subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}", text)

    def test_declares_no_azure_credentials_secret(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertNotIn("AZURE_CREDENTIALS", text)
        self.assertNotIn("client-secret", text)


class PreconditionGuardTests(unittest.TestCase):
    """A precondition guard in the seed-demo-fixture.yml `:120-130` style:
    workspace, workspace_user, workspace_membership and workspace_role must
    all exist, or the job fails with a named error instead of an obscure
    mid-script 'relation does not exist'."""

    def test_guard_checks_all_four_membership_tables_together(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        guard_match = re.search(
            r"table_name IN \(([^)]*)\)", text
        )
        self.assertIsNotNone(guard_match, "no 'table_name IN (...)' schema guard query found")
        table_list = guard_match.group(1)
        for table in ("workspace", "workspace_user", "workspace_membership", "workspace_role"):
            self.assertIn(f"'{table}'", table_list, f"guard does not check for {table!r}: {table_list!r}")

    def test_guard_fails_the_job_with_a_named_error(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        guard_step = _block_between(text, r"Confirm w14 schema is present[^\n]*\n", r"\n      - name:")
        self.assertIn("::error::", guard_step)
        self.assertIn("exit 1", guard_step)


class BackfillInsertTests(unittest.TestCase):
    """Coding objective point 3: per pair, one transaction scoped by
    `SET app.tenant_id`, `gen_random_uuid()` for the backfilled rows (no
    fixed id is available here), RLS never disabled, no role/GRANT/policy
    touched."""

    def test_scopes_by_tenant_id_per_pair(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("SET app.tenant_id = '${WORKSPACE_ID}';", text)

    def test_uses_gen_random_uuid_not_a_fixed_id(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("gen_random_uuid()", text)

    def test_never_touches_rls_role_grant_or_policy(self) -> None:
        # Scan non-comment lines only: the header prose *talks about* never
        # touching a role/GRANT/policy, which would otherwise false-positive
        # against a naive whole-file substring search.
        text = _read(BACKFILL_WORKFLOW)
        code_lines = [line for line in text.splitlines() if not line.strip().startswith("#")]
        code_text = "\n".join(code_lines)
        for forbidden in ("DISABLE ROW LEVEL", "BYPASSRLS", "GRANT ", "CREATE POLICY", "CREATE ROLE"):
            self.assertNotIn(forbidden, code_text, f"workflow must never touch (outside comments): {forbidden!r}")

    def test_looks_up_admin_role_by_name_not_a_fixed_id(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("wr.name = 'Admin'", text)


class FailIfZeroVerificationTests(unittest.TestCase):
    """A closing verification that fails if zero rather than merely
    printing (seed-demo-fixture.yml `:141-149` shape): every supplied
    (workspace id, email) must have a live Admin membership, or the job
    exits non-zero."""

    def setUp(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.verify_step = _block_between(text, r"Verify every pair now has a live Admin membership[^\n]*\n", None)

    def test_verification_step_exists(self) -> None:
        self.assertTrue(self.verify_step.strip(), "no closing verification step found")

    def test_verification_counts_live_admin_membership_per_pair(self) -> None:
        self.assertIn("workspace_membership", self.verify_step)
        self.assertIn("wr.name = 'Admin'", self.verify_step)

    def test_verification_fails_the_job_when_any_pair_is_missing(self) -> None:
        self.assertIn("MISSING", self.verify_step)
        self.assertIn("::error::", self.verify_step)
        self.assertIn("exit 1", self.verify_step)

    def test_verification_does_not_merely_print_on_success(self) -> None:
        # A print-only "check" (no exit path) would defeat the point --
        # there must be a real `-gt 0` (or equivalent) branch that exits.
        self.assertRegex(self.verify_step, r'if \[ "\$MISSING" -gt 0 \]; then')


class AuditLogTests(unittest.TestCase):
    """ADR-025 §G: the audited fact is `workspace.membership.backfilled`
    with the dispatching actor. The email itself must not appear in that
    line -- ADR-025 §G's own action table carries no email column."""

    def test_logs_the_adr025_audit_action_with_the_dispatching_actor(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertIn("workspace.membership.backfilled", text)
        self.assertIn("BACKFILL_ACTOR: ${{ github.actor }}", text)
        self.assertRegex(text, r"workspace\.membership\.backfilled actor=\$\{BACKFILL_ACTOR\}")

    def test_audit_line_itself_carries_no_email_variable(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        audit_line_match = re.search(r"^.*workspace\.membership\.backfilled actor=.*$", text, re.MULTILINE)
        self.assertIsNotNone(audit_line_match)
        self.assertNotIn("EMAIL", audit_line_match.group(0))


class NoNewAzureOrTerraformSurfaceTests(unittest.TestCase):
    """AC-6: no new Azure resource, Terraform change, Key Vault secret,
    federated credential, or runtime config key -- this workflow only reads
    the same postgres-connection secret every sibling workflow reads."""

    def test_reads_only_the_existing_postgres_connection_secret(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        secret_names = set(re.findall(r"--name\s+([A-Za-z0-9_-]+)", text))
        self.assertEqual(secret_names, {"postgres-connection"})

    def test_never_calls_terraform_or_az_containerapp_update(self) -> None:
        text = _read(BACKFILL_WORKFLOW)
        self.assertNotIn("terraform ", text)
        self.assertNotIn("az containerapp update", text)
        self.assertNotIn("set-env-vars", text)


# ---------------------------------------------------------------------------
# seed-demo-fixture.yml's guard line (AC-3)
# ---------------------------------------------------------------------------

class SeedFixtureGuardTests(unittest.TestCase):
    """AC-3: seed-demo-fixture.yml's schema guard also requires
    workspace_membership, so seeding a `demo` whose schema predates w14
    fails honestly instead of obscurely mid-script."""

    def test_seed_workflow_exists(self) -> None:
        self.assertTrue(SEED_WORKFLOW.is_file(), f"{SEED_WORKFLOW} does not exist")

    def test_guard_table_list_includes_workspace_membership(self) -> None:
        text = _read(SEED_WORKFLOW)
        guard_match = re.search(r"table_name IN \(([^)]*)\)", text)
        self.assertIsNotNone(guard_match, "no 'table_name IN (...)' schema guard query found")
        table_list = guard_match.group(1)
        self.assertIn("'workspace_membership'", table_list)

    def test_guard_threshold_matches_table_count(self) -> None:
        text = _read(SEED_WORKFLOW)
        guard_match = re.search(r"table_name IN \(([^)]*)\)", text)
        self.assertIsNotNone(guard_match)
        table_count = len([t for t in guard_match.group(1).split(",") if t.strip()])
        threshold_match = re.search(r'\[\s*"\$PRESENT"\s*-lt\s*(\d+)\s*\]', text)
        self.assertIsNotNone(threshold_match, "no '[ \"$PRESENT\" -lt N ]' threshold check found")
        self.assertEqual(
            int(threshold_match.group(1)),
            table_count,
            "the -lt threshold must equal the number of tables in the guard's table_name IN (...) list",
        )

    def test_demo_admin_email_is_passed_only_when_the_variable_is_set(self) -> None:
        text = _read(SEED_WORKFLOW)
        self.assertIn("DEMO_ADMIN_EMAIL: ${{ vars.DEMO_ADMIN_EMAIL }}", text)
        seed_step = _block_between(text, r"Seed fixture benchmark \+ savings data[^\n]*\n", None)
        self.assertIn('if [ -n "${DEMO_ADMIN_EMAIL}" ]; then', seed_step)
        self.assertIn("demo_admin_email=", seed_step)


class OnlyTwoWorkflowsTouchedByConventionTests(unittest.TestCase):
    """Static half of AC-5 (the other half -- `git diff --name-only
    origin/main -- .github/workflows` -- is a shell command in the task's
    own Definition of done, not reproducible inside pytest without a git
    history to diff against). This only proves the two files this task
    owns are internally consistent with each other, e.g. both cite the same
    guard table list shape."""

    def test_backfill_and_seed_guards_check_the_same_core_tables(self) -> None:
        backfill_text = _read(BACKFILL_WORKFLOW)
        seed_text = _read(SEED_WORKFLOW)
        for table in ("workspace", "workspace_membership"):
            self.assertIn(f"'{table}'", backfill_text)
            self.assertIn(f"'{table}'", seed_text)


if __name__ == "__main__":
    unittest.main()
