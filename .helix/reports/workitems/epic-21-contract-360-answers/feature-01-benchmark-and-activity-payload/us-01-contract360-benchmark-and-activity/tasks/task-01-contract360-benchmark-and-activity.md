---
id: E21/F01/US01/T01
type: task
story: us-01-contract360-benchmark-and-activity
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-contract360-benchmark-and-activity — fill both 360 arrays by host composition

## Context

**Closes: NW-20.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-20** row
(product-owner, software-architect, security-architect and the software-architect
round-2 close), plus the ruling on **OQ-w17-sa-03** and the geography half of
**OQ-w17-004**.

ADRs in force: **ADR-001** w17 clause 3; **ADR-024** w17 clauses 10–11;
**ADR-002** w17 clause 3; **ADR-011** w17 clauses 22 and 25; **ADR-009** w17
clause 5; **ADR-012** w17 §45.

Already on `main`, so **not** work to redo: `IBenchmarkService`
(`backend/src/Raffa.Benchmark/IBenchmarkService.cs:28`, one method at `:44-45`),
registered at `backend/src/Raffa.Benchmark/ServiceCollectionExtensions.cs:60`;
`MarketFeedBenchmarkAdapter` as the **default active** adapter
(`backend/src/Raffa.Market/Benchmark/MarketFeedBenchmarkAdapter.cs:61`, made
default at `backend/src/Raffa.Market/ServiceCollectionExtensions.cs:190-194`),
with a real `market_record` read (`:177-191`), a real abstain path (`:282`,
taken at `:163`) and `MinimumViableSampleSize = 5` (`:141`);
`ISupplierNameLookup`
(`backend/src/Raffa.SharedKernel/Suppliers/ISupplierNameLookup.cs:10`),
registered at
`backend/src/Raffa.Suppliers.Products/Infrastructure/ServiceCollectionExtensions.cs:41`
and **already injected into the 360 handler**
(`backend/src/Raffa.Api/ContractsEndpointExtensions.cs:156`);
`WorkspaceTenant.Country`
(`backend/src/Raffa.Identity.Workspace/Domain/WorkspaceTenant.cs:46`).

⚠ This task publishes the **shared benchmark key resolver** that
`E21/F02/US01/T01` and `E21/F03/US01/T01` both consume in phase 2. Resolving the
key twice and getting two answers on one screen is the defect this centralises
away.

## Coding objective

In `raffa-backend`, give both 360 placeholder records members and fill them from
the host.

1. **Give the records members.** In
   `backend/src/Raffa.Documents.Contracts/Application/Contract360Result.cs`,
   replace the memberless `Contract360BenchmarkEntry` (`:216`) and
   `Contract360ActivityEntry` (`:223`) with:
   - `Contract360BenchmarkEntry(string Metric, string Status, string? Position,
     string? AdapterName, int? SampleSize, DateOnly? AsOf)`
   - `Contract360ActivityEntry(DateTimeOffset OccurredAt, string Action,
     string ActorLabel)`
   Update **four** anchors in this file so it stops describing the members as
   empty placeholders **and stops promising a tab**: the **file-level docstring
   at `:9-12`**, which still quotes spec §8.2's tab list verbatim (*"… Documents,
   Benchmark, Renewal, **Activity**."*); the Benchmark/Activity paragraph at
   `:24-27`; and the two entry doc comments at `:208-215` and `:218-222`.
   ⚠ `:9-12` is **not** optional tidying. ADR-020 w17 §22 grants this task the
   free sweep *because* it is already opening this file, and names **`:12` and
   `:219` rewritten in the same edit** — "left alone, the tree keeps promising a
   tab in the very file W18 reads first". Leave the `Contract360Result` member
   order (`:39` `Benchmark`, `:41` `Activity`) unchanged.
2. **The shared key resolver — new file.** Add
   `backend/src/Raffa.Api/BenchmarkKeyResolution.cs`: a host-level service that,
   given a tenant and a contract, resolves the **(supplier name, geography)**
   pair — supplier name via `ISupplierNameLookup`, geography from the
   **workspace country** (`WorkspaceTenant.Country`, ISO 3166-1 alpha-2) — and
   returns either a complete key or an explicit "key incomplete" result.
   ⚠ `BenchmarkQuery`'s `Supplier` and `Geography` are **non-nullable**
   (`backend/src/Raffa.Benchmark/Contracts/BenchmarkQuery.cs:22-30`), so an
   incomplete key must be a *result*, never a `null!`. Register it in
   `backend/src/Raffa.Api/Program.cs`'s composition the way the other host
   services are, and follow the
   `backend/src/Raffa.Api/PortfolioEndpointExtensions.cs:154-189` convention of
   one place per host that calls the lookup.
3. **Fill `benchmark` in the host.** In
   `backend/src/Raffa.Api/ContractsEndpointExtensions.cs`, replace the
   unconditional `benchmark = Array.Empty<object>()` (`:353`) with a projection
   of real entries, resolved in `GetContract360Async` (`:151`) via the new
   resolver plus `IBenchmarkService.GetBenchmarkAsync`. ⚠ **When the adapter
   abstains, emit one entry with `Status = "insufficient_data"`** — never `[]`.
   When the **key** is incomplete, emit one entry with the same abstain status
   and no `Position`. Delete the "R3/R4 placeholders" comment at `:349-352`.
4. **A new, contract-scoped audit query — this is where the five conditions are
   implemented or lost.** In
   `backend/src/Raffa.Audit/Infrastructure/AuditQueryService.cs`, add a method
   beside `GetEventsAsync` (`:68-69`) that takes the tenant, the contract's
   resource id and the **allow-list of action constants**, and filters **as
   query predicates**:
   - `ResourceType`/`ResourceId` equal to the contract — **contract-scoped**,
     never tenant-wide, no `?tenantId=`, no "all activity" mode, no pagination;
   - `Action` **in** a closed, default-deny allow-list declared in this task;
   - ordered by `OccurredAt` descending, with its own row cap.
   ⚠ **Do not** call `GetEventsAsync` and filter in memory: it is tenant-wide
   (`:78`), capped at 200 (`:66`, `:80`) and unfiltered, so an in-memory filter
   satisfies **none** of conditions 1–2 while looking exactly like the feature.
   Add the method to `IAuditQueryService` (`:27`, `:37-38`).
   ⚠ Keep it inside `AuditQueryService`, which **opens its own scope** (`:75`)
   and documents the ordering a host composition would rediscover (`:71-74`):
   `AuditDbContextOptions.Configure`'s third argument is **optional** (`:24-27`),
   so a host-built context silently returns **zero rows** and the tab paints the
   very empty array three seats refused.
5. **The allow-list.** ⚠ There is **no central registry** — audit action
   constants are declared **per service**. Declare the closed list explicitly in
   this task, referencing the existing constants by name, drawn from ADR-001 w17
   clause 3's own list: `document.validated`
   (`backend/src/Raffa.Documents.Contracts/Application/DocumentValidationService.cs:58`),
   `document.reprocessed` (`DocumentReprocessService.cs:51`),
   `document.prioritised` (`DocumentPriorityService.cs:36`),
   `document.rejected` (`Admission/DocumentAdmissionGate.cs:70`),
   `contract.corrected` (`ContractCorrectionService.cs:105`),
   `contract.negotiation_steps_set` (`NegotiationStepService.cs:61`),
   `renewal.action_updated`
   (`backend/src/Raffa.Renewals/Application/RenewalActionService.cs:42`),
   `savings_opportunity.realized`
   (`backend/src/Raffa.Savings/Application/SavingsOpportunityService.cs:87`).
   **Default-deny**: an action added later is invisible until explicitly added.
   A blocklist is refused — with one, every future action leaks by default in a
   task that never mentions security.
6. **Fill `activity` in the host.** Replace the unconditional
   `activity = Array.Empty<object>()` (`:362`) with the projection. Resolve
   `Actor` to a **display label**: `system:<component>` verbatim for pipeline
   events, the display name the member list already discloses for human events,
   **never a raw subject GUID**; an unresolvable actor renders as a removed
   member. ⚠ **Never project `AuditEvent.Detail`**, and do not add a `detail`
   field to the wire — not even for one action.
7. **Leave the module alone.** `Contract360QueryService.cs:201`'s construction
   site keeps passing empty arrays for these two members; the **host**
   overwrites them when it shapes the response (`ToContract360Response`, `:220`),
   the way `supplierName` is already joined in
   `PortfolioEndpointExtensions.cs:92-106`. `Raffa.Documents.Contracts` is
   fenced to `[SharedKernel, AiGateway]` by
   `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs:63` (the
   entry itself; `:60` is the `AllowedReferences` dictionary declaration), so it
   may reference neither `Raffa.Audit` nor `Raffa.Benchmark`.

**No contract edit here.** `web/openapi/raffa-api.v1.json`'s `benchmark`
(`:2639`) and `activity` (`:2689`) entries — and their "Always empty this wave"
descriptions at `:2645` and `:2695` — are updated by the phase-2 contract owner,
`E22/F01/US01/T01`. **No web consumer and no client task** this wave
(ADR-012 w17 §45).

## Parent story AC covered

- AC-1 `GET /api/contracts/{id}` returns a `benchmark` array with at least one entry when supplier name and workspace country both resolve, carrying `metric`, `status` and — when the adapter answers — `position`, `adapterName`, `sampleSize`, `asOf`.
- AC-2 When the adapter abstains, `benchmark` carries **one entry** with `status: "insufficient_data"`, never `[]`.
- AC-3 `activity` is an array of `{ occurredAt, action, actorLabel }` for that contract only, most recent first.
- AC-4 Only allow-listed actions appear; a non-listed action in the trail is absent.
- AC-5 No entry carries an extracted value and **no `detail` field appears on the wire**.
- AC-6 `actorLabel` is `system:<component>` verbatim or a display name; **no raw subject GUID**; an unresolvable actor renders as a removed member.
- AC-7 A non-Admin member gets the contract's `activity` **and** still receives **403** from `GET /api/audit`.
- AC-8 Another tenant's events never appear for the same contract id.
- AC-9 The activity query is contract-scoped: no `?tenantId=`, no all-activity mode, no pagination.
- AC-10 Both records have members and `Raffa.Documents.Contracts` references neither the audit nor the benchmark module.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Documents.Contracts/Application/Contract360Result.cs` | modify — give both entry records members; correct the docstrings at **`:9-12`** (the spec §8.2 tab promise — ADR-020 w17 §22), `:24-27`, `:208-215` and `:218-222` |
| `backend/src/Raffa.Api/BenchmarkKeyResolution.cs` | **new** — the shared (supplier name, geography) resolver; an incomplete key is a result, never a null |
| `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` | modify — fill both members in the host; the abstain as an entry; delete the R3/R4 comment at `:349-352` |
| `backend/src/Raffa.Api/Program.cs` | modify — register the resolver only |
| `backend/src/Raffa.Audit/Infrastructure/AuditQueryService.cs` | modify — a new contract-scoped, allow-listed method written as query predicates, inside the service's own scope |
| `backend/tests/Raffa.Api.Tests/Contract360EndpointTests.cs` | modify — the benchmark entry, the abstain entry, the activity projection, the allow-list, no `detail`, no GUID |
| `backend/tests/Raffa.Api.Tests/DocumentAdminActionsAuthorizationTests.cs` | modify — the pinning test: a non-Admin sees the contract's activity and still gets 403 from `GET /api/audit` |
| `backend/tests/Raffa.Audit.Tests/ContractActivityProjectionTests.cs` | **new** — contract scoping, the default-deny allow-list, cross-tenant isolation, and that a host-built context would have returned zero rows |
| `backend/tests/Raffa.Documents.Contracts.Tests/Contract360QueryServiceTests.cs` | modify — the module still passes empty arrays and references neither module |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-001 w17 clause 3** — what Activity means for V1, and that **removing the member is preferable to an unconditional `[]`**. If the projection does not fit, delete the member and the record type; do not ship `[]` with no decision.
  - **ADR-024 w17 clause 11** — the two shapes, and the abstain **as an entry**: `[]` is today's defect and cannot be told apart from "never wired".
  - **ADR-011 w17 clause 22** — the five conditions, in full. Condition 3 (*names, never values*) has a **mechanism**, not just a rule: `Detail` is never projected.
  - **ADR-011 w17 clause 25** — a **new** method with **query predicates**; the existing reader cannot be narrowed into this.
  - **ADR-009 w17 clause 5** — the second DbContext's optional third argument. The store is sound and therefore fails **closed** — ⚠ and on a provenance tab **the closed failure *is* the unconditional `[]`** that product-owner, software-architect and security-architect each refused separately: **one defect, two names**.
  - **ADR-002 w17 clause 3** — host composition, with `PortfolioEndpointExtensions.cs` as the precedent; **one resolution, two consumers**.
  - **ADR-012 w17 §45** — the members land and **nothing renders them**; adding members is additive, so this lands **green and silent**. That is accepted deliberately this wave, with *absent ≠ empty* travelling to W18.
- **Not granted**: an actual audit **screen** stays Admin-only and still owes its
  own ADR-020 row and a fresh re-review. `/api/audit` keeps its ladder
  unchanged and this projection does not become a second route into it.
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:95-112`
  (screen 5). ⚠ Four oracles still promise the Activity **tab** — including
  `Contract360Result.cs:9-12`, the file this task edits, **and step 1 rewrites
  that one**: it is the only one of the four inside this task's reach, which is
  exactly why ADR-020 w17 §22 rides the sweep here instead of minting a writer.
  The remaining three are design exports this wave does not own; the **export**
  is owed to W18 and blocks nothing, because no web consumer ships.
- **Do not touch**: `web/openapi/raffa-api.v1.json` and
  `web/src/api/generated/schema.ts` (phase 2's contract owner); anything under
  `web/src` (no client task this wave);
  `AuditQueryService.GetEventsAsync` itself and the `/api/audit` route's
  authorization ladder; `CriticalityScoreCalculator.cs:35`'s `0.8` (a criticality
  input, out of scope per OQ-w17-sa-01); `InsightsEndpointExtensions.cs` (that
  is `E21/F03/US01/T01`'s file, phase 2).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Audit.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0 (the module's allow-list is unchanged)
- [ ] `grep -n "Array.Empty" backend/src/Raffa.Api/ContractsEndpointExtensions.cs` no longer returns the `benchmark` or `activity` lines
- [ ] `grep -rn "Detail" backend/src/Raffa.Api/ContractsEndpointExtensions.cs` returns nothing in the activity projection, and `grep -n "detail" backend/src/Raffa.Api/ContractsEndpointExtensions.cs` shows no wire field
- [ ] `grep -n "public sealed record Contract360BenchmarkEntry;\|public sealed record Contract360ActivityEntry;" backend/src/Raffa.Documents.Contracts/Application/Contract360Result.cs` returns nothing
- [ ] **the `:9-12` tab promise is gone, verifiably**: `grep -n "Tabs:" backend/src/Raffa.Documents.Contracts/Application/Contract360Result.cs` returns **nothing** (it returns **one hit today**, at `:9`, whose sentence ends "… Benchmark, Renewal, Activity." at `:11-12`). ADR-020 w17 §22 — `:12` and `:219` in the same edit
- [ ] `grep -rn "Raffa.Audit\|Raffa.Benchmark" backend/src/Raffa.Documents.Contracts/` returns nothing
- [ ] `grep -n "tenantId\|AllActivity\|skip\|take" backend/src/Raffa.Audit/Infrastructure/AuditQueryService.cs` shows no tenant-wide or paginated parameter on the **new** method
- [ ] `git diff --stat origin/main -- web/` is **empty**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | `benchmark` carries a real entry; an abstaining adapter yields one `insufficient_data` **entry**, not `[]`; an incomplete key yields the same rather than a crash | `backend/tests/Raffa.Api.Tests/Contract360EndpointTests.cs` |
| integration | `activity` is contract-scoped and most-recent-first; a non-allow-listed action is absent; no `detail` field and no raw GUID appear in the payload | `backend/tests/Raffa.Api.Tests/Contract360EndpointTests.cs` |
| integration | **the pinning test** — a non-Admin member reads the contract's activity **and** receives 403 from `GET /api/audit` | `backend/tests/Raffa.Api.Tests/DocumentAdminActionsAuthorizationTests.cs` |
| unit | the new query method filters by contract and by allow-list **in the query**; another tenant's events never appear; a context configured without the tenant argument returns zero rows and the test asserts that this path is not the one used | `backend/tests/Raffa.Audit.Tests/ContractActivityProjectionTests.cs` |
| unit | the module's construction site still passes empty arrays and references neither module | `backend/tests/Raffa.Documents.Contracts.Tests/Contract360QueryServiceTests.cs` |

## Open questions blocking this task

- **none blocking.** OQ-w17-sa-03 is ruled (permitted as a contract-scoped
  provenance projection, refused as an audit reader) and OQ-w17-004's geography
  half is ruled (the workspace country, resolved in the host).
- **Recorded**: `OpenWeakFacts`' 0.8 bar is **not** this task's and must not be
  rendered anywhere (OQ-w17-sa-01).

## Wave-spec entry

```yaml
- id: E21/F01/US01/T01
  prompt: reports/workitems/epic-21-contract-360-answers/feature-01-benchmark-and-activity-payload/us-01-contract360-benchmark-and-activity/tasks/task-01-contract360-benchmark-and-activity.md
  produces: [contract360-benchmark-activity, benchmark-key-resolver]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
