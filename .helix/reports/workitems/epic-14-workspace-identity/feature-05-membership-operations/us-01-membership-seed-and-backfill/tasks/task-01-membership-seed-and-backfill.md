---
id: E14/F05/US01/T01
type: task
story: us-01-membership-seed-and-backfill
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-membership-seed-and-backfill — Admin membership in the demo fixture seed, the schema guard line, and the operator backfill workflow

## Context

**Closes: NW-01, NW-02, NW-03** (the operator half each of them needs to survive
promotion). Decision row: `reports/architecture/waves/w14.md`, row **NW-58**,
delivery-manager half — "the promotion blocker this seat owns, and nobody else
would catch". ADRs in force: **ADR-022** (the fixture tenant), **ADR-021**
(schema before seed), **ADR-016 w14 footer** (seeds and backfills are data-plane
acts and are never promoted), **ADR-015 — `none`** (no new identity, federated
credential or stored secret), **ADR-025 §2.3 / ADR-026 implication 9** (no
"claim this workspace" endpoint; backfill is an operator job).

Once `GET /api/workspaces` lists by membership, the ADR-022 fixture tenant on
`demo` — seeded by SQL (`backend/scripts/demo-fixture-seed.sql:60-67`, run by
`.github/workflows/seed-demo-fixture.yml:132-135`), never by
`POST /api/workspaces` — can never receive a membership row from the new code
path. A repo-wide grep for `workspace_membership|workspace_user` across every
`*.sql`/`*.yml`/`*.yaml` returns exactly one file: the migration that creates
the tables. **Nothing anywhere seeds a membership row.** The same holds for every
workspace already created on `dev`.

**`deps: []`, phase 1** — deliberately, and this corrects the delivery-manager's
own lane plan. There is **no schema dependency**: `workspace_user` and
`workspace_membership` already exist. This is the wave's only deliverable a
fan-out task cannot verify, so it should exist as early as possible.

- **Architecture decisions in force**: ADR-022, ADR-021, ADR-016 w14 footer,
  ADR-015 (`none`).
- **Do not touch**: `.github/workflows/backend.yml`, `web.yml`, `infra.yml`,
  `demo-promote.yml`, `demo-config-check.yml` or any other workflow —
  **w14 changes exactly two workflow files** and `E14/F06/US01/T01` asserts it;
  `infra/**` (zero Terraform delta, both environments); anything under
  `backend/src` or `web/src`; `Migrations/**`.

## Coding objective

**1. Extend `backend/scripts/demo-fixture-seed.sql`.** Add an idempotent Admin
`workspace_user` + `workspace_membership` for the fixture tenant
`00000000-0000-0000-0000-000000000001`, following the file's own conventions,
which are stricter than "append an INSERT":
- The header (`:33-43`) is a **registry of fixed well-known ids**
  (`…0001` tenant, `…0002` contract, `…0011/12/13` opportunities). The two new
  rows take the next free documented ids — `…0003` (`workspace_user`) and
  `…0004` (`workspace_membership`) — and are **added to that registry comment**.
  **Never `gen_random_uuid()`.**
- `ON CONFLICT (id) DO NOTHING`, inside the existing `BEGIN;` (`:55`) /
  `COMMIT;` (`:148`).
- **RLS stays on.** The script sets `app.tenant_id` at `:53` and **never**
  touches a role, GRANT or policy — its own AC-4 note at `:48-52`. The two
  inserts satisfy each table's `tenant_isolation` `WITH CHECK` the ordinary way.
- The Admin role id must be the `workspace_role` row for `Admin` **in that
  tenant**, selected by name inside the same transaction — do not invent a role
  id, and do not assume one. If the tenant has no `workspace_role` rows (the
  fixture tenant was seeded before the role catalogue existed), insert the Admin
  role row too, with its own documented fixed id `…0005`.
- **The email**: take an optional `psql` variable and fall back to a documented
  placeholder, e.g.
  `\if :{?demo_admin_email} \else \set demo_admin_email 'demo-admin@raffa.invalid' \endif`,
  then use `:'demo_admin_email'` lower-cased. Say in the comment that the
  placeholder grants nobody real access and that the operator sets
  `DEMO_ADMIN_EMAIL` per environment before the first post-w14 seed
  (OQ-w14-dec-001).

**2. One line in `.github/workflows/seed-demo-fixture.yml`.** Its guard
(`:123-130`) exists precisely to give "an honest failure instead of a confusing
'relation does not exist'" (`:120-122`). Add `workspace_membership` to the
`table_name IN (…)` list and raise the `-lt 3` threshold to match, or seeding a
`demo` whose schema predates w14 fails obscurely mid-script. Pass
`-v demo_admin_email="${{ vars.DEMO_ADMIN_EMAIL }}"` to the `psql` call at
`:132-135` **only when the variable is set**, so the script's own default holds
otherwise. **No other change to this file.**

**3. New `.github/workflows/backfill-workspace-membership.yml`**, modelled
line-for-line on `seed-demo-fixture.yml`:
- `workflow_dispatch` with `target_environment` as a **typed `choice`**
  (`dev` | `demo`, the `:15-22` shape) plus `workflow_call`; and a required
  `pairs` input: newline-separated `<workspace id>,<admin email>` pairs supplied
  by the operator at dispatch time.
- `environment: ${{ inputs.target_environment }}` so `demo` inherits its
  required reviewers (`:49-53`).
- OIDC via `./.github/actions/azure-login` (`:65-70`); Key Vault
  `postgres-connection` (`:86-118`). **No new identity and no new federated
  credential** — the per-env deploy SP already holds Key Vault Secrets User
  (`:60-64`, verbatim).
- A **precondition guard** in the `:120-130` style: `workspace`,
  `workspace_user`, `workspace_membership` and `workspace_role` must all exist.
- For each pair, inside one transaction per workspace and with
  `SET app.tenant_id = '<workspace id>'`: insert the `workspace_user` if absent
  (fixed id is not available here — use `gen_random_uuid()` for backfilled rows,
  which is correct because these are real tenants, not fixtures) and the Admin
  `workspace_membership` if absent, `ON CONFLICT DO NOTHING`. **Never** disable
  RLS, and never touch a role, GRANT or policy.
- A closing verification that **fails if zero** rather than merely printing
  (`:141-149`): after the run, every supplied `(workspace id, email)` must have
  a live Admin membership; if any does not, `::error::` and exit 1. A backfill
  that silently inserts nothing is the failure mode worth catching.
- Echo the audited fact in the log as `workspace.membership.backfilled` with the
  dispatching actor, per ADR-025 §G. Do **not** write the email into any other
  sink.

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4, AC-5, AC-6

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/scripts/demo-fixture-seed.sql` | Admin `workspace_user` (`…0003`) + `workspace_membership` (`…0004`) (+ the Admin `workspace_role` `…0005` if absent); the id registry comment; the `demo_admin_email` variable |
| `.github/workflows/seed-demo-fixture.yml` | the guard gains `workspace_membership`; pass `demo_admin_email` when `vars.DEMO_ADMIN_EMAIL` is set |
| `.github/workflows/backfill-workspace-membership.yml` | new — the operator backfill for existing `dev` / `demo` tenants |
| `tests/test_backfill_workspace_membership_workflow.py` | new — a static check that the new workflow declares the typed `choice`, the `environment:`, the OIDC action, the guard and the fail-if-zero verification |

## Context the implementer needs

- The demo tenant id is `00000000-0000-0000-0000-000000000001` and its display
  name is `'Raffa Demo'` (`demo-fixture-seed.sql:63`).
- The repo root already carries Python workflow checks with pytest
  (`scripts/check_demo_swa_config.py` + `tests/test_check_demo_swa_config.py`) —
  follow that shape. `python`, not `python3`.
- **Do not** add a Terraform resource, a Key Vault secret, an app setting or a
  container-app env var. `backend.yml` deploys with
  `az containerapp update --image` **only** (`:196-208`); a value pushed with
  `--set-env-vars` is reverted by the next HCP apply. That shortcut is named as
  **drift** in ADR-016's w14 footer.
- **Do not touch**: any other workflow file, `infra/**`, `backend/src/**`,
  `web/**`, `Migrations/**`.

## Definition of done

- [ ] `psql -v ON_ERROR_STOP=1 -f backend/scripts/demo-fixture-seed.sql` run
      **twice** against a scratch database with the w14 schema applied exits 0
      both times and inserts nothing the second time
- [ ] `python -m pytest tests/test_backfill_workspace_membership_workflow.py`
      exits 0
- [ ] `git diff --name-only origin/main -- .github/workflows` lists **exactly
      two** files: `backfill-workspace-membership.yml` and
      `seed-demo-fixture.yml`
- [ ] `rg -n "ALTER TABLE|DISABLE ROW LEVEL|GRANT|CREATE POLICY|BYPASSRLS" backend/scripts/demo-fixture-seed.sql`
      returns nothing
- [ ] `git diff --stat origin/main -- infra` is empty

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| script (manual/CI) | the seed is idempotent and RLS-respecting; a second run inserts nothing | run `demo-fixture-seed.sql` twice, documented in the task log |
| unit (python) | the new workflow has the typed `choice`, the `environment:`, `azure-login`, the precondition guard and a fail-if-zero verification | `tests/test_backfill_workspace_membership_workflow.py` |
| static | exactly two workflow files differ from `origin/main` | the DoD command above |

## Open questions blocking this task

- **OQ-w14-dec-001** — which email the seeded `demo` Admin membership binds to.
  **Assumption in force**: an optional `psql` variable `demo_admin_email` with a
  documented placeholder default (`demo-admin@raffa.invalid`), supplied by the
  `DEMO_ADMIN_EMAIL` GitHub Environment variable when the operator sets it. Not
  blocking: the placeholder is inert and the backfill workflow is the path for
  real addresses.

## Wave-spec entry
```yaml
- id: E14/F05/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-05-membership-operations/us-01-membership-seed-and-backfill/tasks/task-01-membership-seed-and-backfill.md
  produces: [membership-seed-and-backfill]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
