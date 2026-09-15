---
id: us-01
type: user-story
parent: feature-03
wave: w16
status: active
---

# us-01-step-ticks-api — The negotiation steps I tick are a tenant row, not a tab

## Story

As a **Procurement member**, I want the negotiation steps I tick on Contract 360
to be stored server-side per contract, so that **the tracker still shows my
progress after a reload, on another device, and for the colleague I hand the
contract to**.

## Acceptance criteria

- [ ] AC-1 `GET /api/contracts/{id}/negotiation-steps` returns the ticked step
  **keys** for that contract; an untouched contract returns an empty set, not
  `404`.
- [ ] AC-2 `PUT /api/contracts/{id}/negotiation-steps` writes the **whole set**
  idempotently: sending the same set twice leaves the same rows, and a key absent
  from the body is unticked (its row is deleted).
- [ ] AC-3 An unknown step name in the body returns `400` and writes nothing.
- [ ] AC-4 A non-GUID contract id returns `400`; an unknown contract returns
  `404`; no validated token returns `401`; a non-member returns `404`.
- [ ] AC-5 **A second tenant's ticks never appear**: the same contract id under
  another tenant returns zero rows and no `500`.
- [ ] AC-6 The four accepted step keys are exactly the four canonical names of
  `inputs/design/prototypes/raffa-v2/screens-v2.md:106-108`; a fifth is rejected.
- [ ] AC-7 Ticks are **per contract**: contract Y is unaffected by contract X.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-028 §D3, ADR-003 w16 clause 1, ADR-009 w16 clause 2,
  ADR-021 w16, ADR-002 w16 clause 1
- [ ] the RLS policy ships **in the same migration as the table**, and
  `TenantRlsMigrationCheckTests`'s discovered table count goes up
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none in this wave | it creates its own table, entity, service and routes; `ContractsEndpointExtensions.cs` has no other w16 writer |

## Architecture decisions in force

- **ADR-028 §D3** — the owner module, both routes following
  `GET /api/contracts/{id}/corrections` **verbatim**, the table, the unique key,
  the closed enum **stored by name**, the whole-set idempotent `PUT`, unknown
  name `400`, missing name unticked, **the rendered label stays client-side**.
- **ADR-003** w16 clause 1 — `id`, `tenant_id`, `contract_id`, `step varchar(60)`,
  `ticked_at timestamptz`; unique `(tenant_id, contract_id, step)`; **the row's
  presence is the tick — there is no `ticked` boolean**; **no FK across the
  module boundary** (the `RenewalAction.ContractId` treatment).
- **ADR-009** w16 clause 2 — `tenant_id` not null and indexed, `ENABLE` **+
  `FORCE`**, `tenant_isolation` with **both `USING` and `WITH CHECK`**, same
  migration. Branch **2a** is taken deliberately: the entity subclasses
  `TenantScopedEntity` **in `DocumentsContractsDbContext`**, so the existing
  CI guard covers it with no hand-written RLS test — and the **proof** is the
  check's table count.
- **ADR-021** w16 — one migration, regenerating **one** byte-compared script; no
  `backend.yml` diff (any `backend.yml` change fails the final-integration check).
- **ADR-001** w16 clause 3 — four canonical named steps, key not index, per
  contract not per cycle.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | step-ticks-api | L | phase-1 |

## Council decisions carried into this story

> **ADR-009 w16 clause 2b, the trap if branch 2a is not taken**: a hand-written
> per-table RLS test "creates its own unprivileged Postgres role — Testcontainers
> hands you a superuser, and RLS constrains neither a superuser nor a table
> owner, so the obvious test is green and worthless."

> **ADR-003 w16 clause 1**: "The row's presence **is** the tick — there is no
> `ticked` boolean. An untick deletes the row, so the whole-set `PUT` is
> idempotent with no nullable third state and no 'false' rows."

> **ADR-001 w16 clause 3**: the row stores the **key**; "a row must not freeze a
> fact that later changes" — two of the four rendered labels are parameterized
> with the supplier name and the cancellation deadline.

## Open questions

- **OQ-w16-006** — ruled in both halves: product (ADR-001 w16 clause 3) and
  module (ADR-002 w16 clause 1 + ADR-028 §D3). Nothing left open.
