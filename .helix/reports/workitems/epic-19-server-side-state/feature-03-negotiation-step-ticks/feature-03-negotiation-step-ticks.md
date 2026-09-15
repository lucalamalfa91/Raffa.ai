---
id: feature-03
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-07 F02, epic-02 F03
---

# feature-03-negotiation-step-ticks — Contract 360's four negotiation steps are server state

## Slice

`web/src/routes/contracts/contract360/negotiationStepsStore.ts` keeps four
booleans per contract under `sessionStorage["raffa.contract360.steps.<id>"]` as a
**positional, unnamed `boolean[4]`** — it stores no step names, and two of the
four rendered labels are parameterized with the supplier name and the
cancellation deadline (`contract360ViewModel.ts:211-218`). Nothing server-side
exists: no entity, no table, no route. This feature creates the one new tenant
table of the wave, `contract_negotiation_step`, owned by
`Raffa.Documents.Contracts` (`Raffa.Renewals` is structurally impossible — its
ADR-002 allow-list is `[SharedKernel, Benchmark]`), and publishes
`GET` / `PUT /api/contracts/{id}/negotiation-steps` following
`GET /api/contracts/{id}/corrections` verbatim.

The wire carries **step keys only**, from the four names the design oracle fixes;
the rendered label stays client-side.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | step-ticks-api | w16 |

The web half of NW-13 is **feature-07** (combined with NW-11: both retire a store
inside `contract360/index.tsx` and both touch `handleUndo`), and the contract
publication is **feature-06**.

## Architecture decisions in force

- **ADR-028 §D3** — owner module, the two routes, the table, the unique key, the
  closed enum stored **by name**, the row's presence **is** the tick, a whole-set
  idempotent `PUT`, an unknown step name is a `400`, a missing one is unticked.
- **ADR-028 §D4** — Undo is **two idempotent writes**, ticks `PUT` **first**,
  then the action `POST`; recovery from a partial failure is a **re-read**, never
  a compensating write.
- **ADR-003** w16 clause 1 — columns `id`, `tenant_id`, `contract_id`,
  `step varchar(60)`, `ticked_at timestamptz`; unique `(tenant_id, contract_id, step)`;
  **no `ticked` boolean**; no FK across the module boundary.
- **ADR-009** w16 clause 2 — `tenant_id` not null and indexed, `ENABLE` **+
  `FORCE`**, a `tenant_isolation` policy with **both `USING` and `WITH CHECK`**,
  all in the table's own migration. The entity **subclasses `TenantScopedEntity`
  in `DocumentsContractsDbContext`** so `TenantRlsMigrationCheckTests` covers it
  for free — and the proof of coverage is that the check's table count goes up.
- **ADR-021** w16 — one migration, regenerating
  `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql`;
  **no `backend.yml` array moves** (the module is already listed in both arrays).
- **ADR-001** w16 clause 3 — the four canonical named steps, keyed not indexed,
  label client-side, **per contract** not per renewal cycle.
- **ADR-002** w16 clause 1 — `Raffa.Documents.Contracts` owns it.

## Target repo

`raffa-backend`
