---
id: us-01
type: user-story
parent: feature-01
wave: w17
status: active
---

# us-01-contract360-benchmark-and-activity — the 360 carries a market entry and a provenance timeline

## Story

As a **procurement lead**, I want Contract 360's benchmark and activity data to
carry real content — a market position or an explicit abstention, and a
timeline of what actually happened to this contract — so that two tabs stop
being arrays that can never be non-empty.

## Acceptance criteria

- [ ] AC-1 `GET /api/contracts/{id}` returns a `benchmark` array with at least
  one entry for a contract whose supplier name and workspace country both
  resolve. The entry carries `metric`, `status` and — when the adapter answers —
  `position`, `adapterName`, `sampleSize` and `asOf`.
- [ ] AC-2 When the adapter **abstains** (fewer than its minimum viable sample),
  `benchmark` carries **one entry** with `status: "insufficient_data"`. It is
  **never** `[]`, because `[]` cannot be told apart from "never wired".
- [ ] AC-3 `GET /api/contracts/{id}` returns an `activity` array of
  `{ occurredAt, action, actorLabel }` for that contract only, ordered most
  recent first.
- [ ] AC-4 Only actions on the **closed allow-list** appear. An audit action
  that exists in the trail but is not on the list is **absent** from the
  response.
- [ ] AC-5 No entry carries an extracted value, and **no `detail` field appears
  on the wire at all**.
- [ ] AC-6 `actorLabel` is `system:<component>` verbatim for pipeline events and
  a display name for human events. **No raw subject GUID appears**, and an
  unresolvable actor renders as a removed member.
- [ ] AC-7 A **non-Admin** member of the workspace gets the contract's
  `activity` **and** still receives **403** from `GET /api/audit`.
- [ ] AC-8 Another tenant's events never appear for the same contract id, and a
  cross-tenant request for the contract still 404s.
- [ ] AC-9 The activity query is **contract-scoped**: there is no `?tenantId=`
  parameter, no "all activity" mode and no pagination over the trail.
- [ ] AC-10 Both record types have members, and neither
  `Contract360QueryService` nor any file in `Raffa.Documents.Contracts`
  references the audit or benchmark modules.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | phase 1. The benchmark service, its default-active market-feed adapter and `ISupplierNameLookup` are all already on `main` and registered |

## Architecture decisions in force

- **ADR-001** w17 clause 3 — what "Activity" means for V1; remove the member
  rather than ship an unconditional `[]`.
- **ADR-024** w17 clauses 10–11 — the two record shapes, and the abstain as an
  **entry**.
- **ADR-002** w17 clause 3 — host composition in `Raffa.Api`; **one resolution,
  two consumers**.
- **ADR-011** w17 clauses 22, 25 — the five conditions; a **new** query method
  with **query predicates**, never an in-memory filter of the existing feed.
- **ADR-009** w17 clause 5 — the audit DbContext's optional third argument; the
  projection must not fail closed into an empty array.
- **ADR-012** w17 §45 — no web consumer, no client task.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | contract360-benchmark-and-activity | L | phase-1 |

## Council decisions carried into this story

- **Both members land and their shapes are named**, because "a permitted member
  with no named shape is still a guess" (software-architect, round 2):
  `Contract360ActivityEntry(OccurredAt, Action, ActorLabel)` — three members,
  each from a **bounded** audit column (`Timestamp`; `Action` from the closed
  allow-list, `varchar(100)`,
  `backend/src/Raffa.Audit/Infrastructure/Configurations/AuditEventConfiguration.cs:21`;
  `Actor` `varchar(200)` `:20`, resolved to a **display label**) — which is
  exactly ADR-001 clause 3's *when · actor · what changed*, because that
  clause's own list is a list of **action names**.
- ⚠ **`AuditEvent.Detail` is never projected.** It is `text` with **deliberately
  no `HasMaxLength`** — "unbounded free-form context" (`:24`) — and writers
  **already** interpolate contract-derived dates into it on a `Contract`
  resource (`RenewalAlertService.cs:254-255`, `:264-265`;
  `RenewalThresholdScheduler.cs:148-149`). So the obvious implementation —
  project the one human-readable column — breaks *names, never values* **on day
  one**, and the table is append-only, so it would be **permanent**. Not even a
  per-action exception for `document.validated`, whose detail happens to hold
  only field names: whitelisting a free-text column breaks the first time a
  writer adds context, in a task that never mentions security.
- **The widening is verified, not suspected**: `GET /api/audit` is
  **live-`Admin`-only** (ADR-011 w16 clause 14) while `GET /api/contracts/{id}`
  is gated by `ICallerContext.ResolveTenantAsync`
  (`backend/src/Raffa.Api/ContractsEndpointExtensions.cs:165`) — **membership,
  with no Admin check anywhere in that file**. A naive projection moves an
  Admin-only read onto an **any-member** surface.
- **The benchmark key**: geography is the **workspace country**
  (`backend/src/Raffa.Identity.Workspace/Domain/WorkspaceTenant.cs:46`,
  `HasMaxLength(2)`, ISO 3166-1 alpha-2), labelled representative; the supplier
  name half is already solved by `ISupplierNameLookup`, which the 360 handler
  **already injects** (`ContractsEndpointExtensions.cs:156`).
- **No new table, no migration, no new environment key.**
  `ConnectionStrings__Audit` is already bound on the API
  (`infra/modules/containerapps/main.tf:90`) and the Worker (`:354`).

## Open questions

- **OQ-w17-sa-03** (may a 360 tab project the audit trail) — **ruled permitted
  as a contract-scoped provenance projection and refused as an audit reader**,
  under the five conditions of ADR-011 w17 clause 22. This is the re-review
  ADR-011 clause 14c reserved.
- **OQ-w17-004** (where geography comes from) — **ruled**: the workspace
  country, resolved **in the host**, labelled representative. ADR-001 w17
  clause 4, ADR-024 w17 clause 7.
- ⚠ **Recorded, not blocking** (ADR-020 w17 §22): four oracles still promise the
  Activity **tab**, one of them being `Contract360Result.cs:9-12` — the file
  this story edits. The **export** is owed to W18 and blocks nothing, because
  this wave ships no web consumer.
