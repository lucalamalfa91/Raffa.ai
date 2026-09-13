---
id: us-01
type: user-story
parent: feature-05
wave: w14
status: active
---

# us-01-membership-seed-and-backfill — every tenant that already exists gets an Admin membership row

## Story

As the **operator**, I want the `demo` fixture tenant and every workspace that
already exists on `dev` to carry an Admin `workspace_membership` row, so that
promoting w14 does not make every existing workspace vanish for the person who
created it and offer "create a workspace" on the stakeholder-facing environment.

## Acceptance criteria

- [ ] AC-1 `backend/scripts/demo-fixture-seed.sql` inserts an Admin
      `workspace_user` + `workspace_membership` for the fixture tenant
      `00000000-0000-0000-0000-000000000001`, with the file's own conventions:
      **documented fixed ids** (`…0003`, `…0004` are next free) — **never**
      `gen_random_uuid()` — and `ON CONFLICT (id) DO NOTHING` inside the existing
      `BEGIN;` / `COMMIT;` (`:55`, `:148`).
- [ ] AC-2 Running the seed twice inserts nothing new and never errors; RLS
      stays on (the script sets `app.tenant_id` at `:53` and **never** touches a
      role, GRANT or policy — its own AC-4 note, `:48-52`).
- [ ] AC-3 `seed-demo-fixture.yml`'s schema guard (`:123-130`) also requires
      `workspace_membership`, so seeding a `demo` whose schema predates w14 fails
      **honestly** instead of obscurely mid-script.
- [ ] AC-4 A new `.github/workflows/backfill-workspace-membership.yml` writes an
      idempotent Admin membership for operator-supplied `(workspace id, admin
      email)` pairs against a chosen environment, and **fails if it inserted and
      matched zero rows** — a backfill that silently does nothing is the failure
      mode worth catching.
- [ ] AC-5 `git diff --stat origin/main -- .github/workflows` shows **exactly
      two** files after this story: the new one and the one-line guard change.
- [ ] AC-6 No new Azure resource, no Terraform change, no new Key Vault secret,
      no new federated credential, no new runtime config key.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | `deps: []`, phase 1 — and this is a **deliberate correction** to the delivery-manager's own lane plan, which had it at phase 3 behind the schema task. It has **no schema dependency at all**: `workspace_user` and `workspace_membership` already exist (`identity-workspace.sql` is the migration that *creates* them) so the seed rows and the guard line need nothing from `workspace-schema`. Putting the operator path in phase 1 is the de-risking move — it is the wave's only deliverable a fan-out task cannot verify |

## Architecture decisions in force

- **ADR-016 (w14 footer)** — seeds and backfills are **data-plane acts and are
  never promoted** (the body's isolation rule, `:79-80`). The backfill is run per
  environment, by a human, after that environment's schema is applied.
- **ADR-015 — `none`.** The per-env deploy SP `raffa-sp-<env>` already holds Key
  Vault Secrets User (`seed-demo-fixture.yml:60-64`, verbatim). *Inherited
  caveat, not a new one*: that role is granted **out of band, not by Terraform**
  (`backend.yml:251`, `infra/README.md` "Known gaps"), so this job fails exactly
  where `backend.yml` and `seed-demo-fixture.yml` already would.
- **ADR-025 §2.3 / ADR-026 implication 9** — no "claim this workspace"
  endpoint. `CreateWorkspaceAsync` never recorded a creator, so no column, log or
  audit row names one and no tenant can be attributed retroactively by code.
  Pairs come from the operator at HITL and the act is audited as
  `workspace.membership.backfilled` with the operator as actor.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Membership rows in the demo fixture seed, the guard line, and the backfill workflow | M | phase-1 |

## Council decisions carried into this story

- `demo-fixture-seed.sql`'s header (`:33-43`) is a **registry of fixed
  well-known ids** (`…0001` tenant, `…0002` contract, `…0011/12/13`
  opportunities). The two new rows extend that registry and are documented in it.
- `backfill-workspace-membership.yml` is modelled **line-for-line** on
  `seed-demo-fixture.yml`: `workflow_dispatch` with `target_environment` as a
  typed `choice` (`:15-22`) plus `workflow_call`;
  `environment: ${{ inputs.target_environment }}` so `demo` inherits its required
  reviewers (`:49-53`); OIDC via `./.github/actions/azure-login` (`:65-70`); Key
  Vault `postgres-connection` (`:86-118`); a precondition guard in the `:120-130`
  style; an idempotent `psql` insert; and a closing verification that **fails if
  zero** rather than merely printing (`:141-149`).
- `seed-demo-fixture.yml`'s guard exists precisely to give "an honest failure
  instead of a confusing 'relation does not exist'" (`:120-122`). One line.

## Open questions

- **OQ-w14-dec-001** (opened by this decomposition) — which email address the
  seeded `demo` Admin membership binds to. **Assumption in force**: the SQL
  takes an optional `psql` variable `demo_admin_email` and falls back to a
  documented placeholder that grants nobody real access; `seed-demo-fixture.yml`
  passes the `DEMO_ADMIN_EMAIL` GitHub Environment variable when it is set. The
  operator sets it once per environment before the first post-w14 seed. Recorded
  in `reports/open-questions.md` and in `reports/audit/w14-hitl.md`.
