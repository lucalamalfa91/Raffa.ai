---
id: feature-05
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-10 F01 (demo fixture seed), epic-01 F03 (CI workflows)
---

# feature-05-membership-operations — the operator path nothing else can deliver

## Slice

Extends **epic-10 F01**'s `demo` fixture seed and **epic-01 F03**'s workflow set
with the one w14 deliverable a fan-out task cannot verify and that blocks
promotion however the rest of the wave lands.

The delivery-manager's finding: once `GET /api/workspaces` lists by membership,
the ADR-022 fixture tenant on `demo` is seeded by **SQL**
(`backend/scripts/demo-fixture-seed.sql:60-67`, run by
`seed-demo-fixture.yml:132-135`), **not** by `POST /api/workspaces`, so it can
never receive a membership row from the new code path. Proven twice: the seed
inserts `workspace` + `contract` + three `savings_opportunity` rows and nothing
else, and a repo-wide grep for `workspace_membership|workspace_user` across every
`*.sql` / `*.yml` / `*.yaml` returns **exactly one file** —
`identity-workspace.sql`, the migration that *creates* the tables. **Nothing
anywhere seeds a membership row.** Promote w14 as-is and the demo user signs in,
`GET /api/workspaces` returns `[]`, and the stakeholder-facing environment offers
"create a workspace". The same holds for every workspace already created on
`dev`.

Two deliverables, both zero infra delta: extend the seed, and add one operator
workflow for the backfill.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Membership seed for the demo fixture, and a backfill workflow for existing tenants | w14 |

## Architecture decisions in force

- ADR-022 — the day-1 demo fixture tenant and its seed
- ADR-021 — schema applied by CI; the seed runs after it
- ADR-016 (w14 footer) — **seeds and backfills are data-plane acts and are never promoted**; w14 adds no per-environment key
- ADR-015 — `none`: the seed and backfill reuse `raffa-sp-<env>` and its existing Key Vault Secrets User role. No new OIDC subject claim, no new federated credential, no new stored secret
- ADR-025 §2.3 / ADR-026 implication 9 — a "claim this workspace" endpoint is refused as a tenant-takeover primitive; backfill is an operator job over `(workspace id, admin email)` pairs supplied at HITL

## Target repo

`raffa-backend` (this monorepo: `backend/scripts/`, `.github/workflows/`)
