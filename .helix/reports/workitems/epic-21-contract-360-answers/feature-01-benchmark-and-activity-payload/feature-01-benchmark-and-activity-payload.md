---
id: feature-01
type: feature
parent: epic-21
wave: w17
status: active
extends: epic-02 F03
---

# feature-01-benchmark-and-activity-payload — the two placeholder arrays get shapes and data

## Slice

`backend/src/Raffa.Api/ContractsEndpointExtensions.cs:353` and `:362` emit
`benchmark = Array.Empty<object>()` and `activity = Array.Empty<object>()` as
unconditional literals, and the comment at `:349-352` admits they are "R3/R4
placeholders". The API only re-serializes a domain result that is **also**
hardcoded empty at its one construction site
(`backend/src/Raffa.Documents.Contracts/Application/Contract360QueryService.cs:201`,
empties at `:211` and `:213`), and both row types are **memberless**
(`Contract360Result.cs:216` and `:223` — `public sealed record
Contract360BenchmarkEntry;`), so even if populated they could carry no data.

This feature gives both records members and fills them **by host composition in
`Raffa.Api`**: the benchmark from `IBenchmarkService` using a **resolved
(supplier name, geography) key** that `feature-03` will reuse, and the activity
from a **new, contract-scoped, allow-listed** audit query method. It also
publishes the shared key resolver the rest of the epic depends on.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | contract360-benchmark-and-activity | w17 |

## Extends

**epic-02 F03** (`feature-03-portfolio-contract-360`) — the 360 payload built
there reserved both members for R3/R4. **epic-04 F01**
(`feature-01-benchmark-service`) — `IBenchmarkService`, registered at
`backend/src/Raffa.Benchmark/ServiceCollectionExtensions.cs:60`, whose
market-feed adapter is already the default active one
(`backend/src/Raffa.Market/ServiceCollectionExtensions.cs:190-194`) and has
never been called with a complete key.

## Architecture decisions in force

- **ADR-001 w17 clause 3** — "Activity" is the **provenance timeline of
  already-persisted events** (upload, processing completed, validated,
  reprocessed, contract correction, renewal action, negotiation tick, savings
  outcome), each carrying when · actor (`system:<component>` for pipeline
  events) · what changed, and **never the value of an extracted fact**. Out:
  supplier outreach, notes, anything needing a new table. If the projection does
  not fit, **remove the member and delete the record type** — an unconditional
  `[]` with no decision is not acceptable. `benchmark` has no such option,
  because `feature-03` consumes it.
- **ADR-024 w17 clauses 10–11** — `Contract360ActivityEntry(OccurredAt, Action,
  ActorLabel)`, each member from a **bounded** audit column; and
  `Contract360BenchmarkEntry(Metric, Status, Position?, AdapterName?,
  SampleSize?, AsOf?)` with the abstain carried **as an entry**
  (`Status = insufficient_data`), never as `[]` — `[]` is today's defect and
  cannot be told apart from "never wired".
- **ADR-002 w17 clause 3** — host composition in `Raffa.Api`, the way
  `PortfolioEndpointExtensions.cs:28-189` already joins `supplierName`. **One
  resolution, two consumers**: the same `IBenchmarkService` call and the same
  resolved key as `feature-03`. Resolving twice and getting two answers on one
  screen is the defect.
- **ADR-011 w17 clause 22** — five conditions make this a *different read*
  rather than the same read with a filter: (1) **contract-scoped, never
  tenant-wide** — no `?tenantId=`, no "all activity" mode, no pagination over
  the trail; (2) a **closed allow-list of action constants, default-deny** — an
  audit action added later is invisible until explicitly added, and a blocklist
  is refused; (3) **names, never values**; (4) **no actor identifier beyond what
  the member list already shows** — `system:<component>` verbatim, a human event
  as the display name, **never a raw subject GUID**; (5) `/api/audit` keeps its
  ladder unchanged and this projection does not become a second route into it.
- **ADR-011 w17 clause 25** — the existing reader **cannot be narrowed into
  this**: `AuditQueryService.GetEventsAsync` (`:68-69`) is tenant-wide, capped
  at 200 rows (`:66`, `:80`) and has **no** contract, resource or action
  filter. A **new** method is required, written as **query predicates** — the
  obvious in-memory filter of the existing feed satisfies none of the five
  conditions and looks exactly like the feature.
- **ADR-009 w17 clause 5** — this projection lands on the **audit** DbContext,
  whose `AuditDbContextOptions.Configure` third argument is **also** optional
  (`:24-27`). The store fails **closed** (`audit.sql:20`, `:54-56`), and a
  closed failure **is** the unconditional `[]` that three seats refused
  separately — one defect, two names. Ruled onto `AuditQueryService`, which
  opens its own scope (`:75`).
- **ADR-012 w17 §45** — **no web consumer and no client task** this wave: the
  members reach the generated client and nothing renders them, which keeps
  `contract360ViewModel.ts` at three contenders. *absent ≠ empty* travels to
  W18.

## Target repo

`raffa-backend` — `backend/src/Raffa.Api`,
`backend/src/Raffa.Documents.Contracts/Application/Contract360Result.cs`,
`backend/src/Raffa.Audit`, plus `web/openapi` only in phase 2's contract task
(`E22/F01/US01/T01`), never here.
