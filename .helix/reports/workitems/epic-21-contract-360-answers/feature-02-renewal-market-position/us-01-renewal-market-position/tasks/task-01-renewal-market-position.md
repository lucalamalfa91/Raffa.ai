---
id: E21/F02/US01/T01
type: task
story: us-01-renewal-market-position
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-renewal-market-position — resolve the band in the host, keep the builder pure

## Context

**Closes: NW-22.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-22** row
(product-owner and software-architect halves), plus the geography half of
**OQ-w17-004**.

ADRs in force: **ADR-001** w17 clause 4; **ADR-002** w17 clause 2 (and `none` on
the allow-list — already permitted, and the reason is now recorded);
**ADR-024** w17 clause 6.

Already on `main`, so **not** work to redo: `marketPosition` is **already on the
wire** (`backend/src/Raffa.Api/RenewalsEndpointExtensions.cs:287`,
`web/openapi/raffa-api.v1.json:3310`) and **already consumed** by Contract 360
(`web/src/routes/contracts/contract360/contract360ViewModel.ts:175`). Filling
the null is therefore a **backend-only** change: no contract edit, no web edit.
`IBenchmarkService` and the default-active market-feed adapter are registered
and working; the shared **(supplier name, geography)** resolver lands in phase 1
as `E21/F01/US01/T01`'s deliverable.

## Coding objective

In `raffa-backend`, give the renewal insight a real market position **without
touching the builder's purity**.

1. **Resolve in the host.** In
   `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs`, where the candidates
   are assembled before `pipelineBuilder.Build(candidates)` (`:151`), resolve the
   market band **per candidate** using phase 1's shared resolver plus
   `IBenchmarkService.GetBenchmarkAsync`. The builder is already injected at
   `:118`; do **not** give it a new dependency.
2. **Carry it on the DTO.** Extend
   `backend/src/Raffa.Renewals/Application/RenewalDashboardCandidate.cs` (`:43-49`
   — `ContractId`, `SupplierId`, `EndDate`, `AutoRenewal`, `AnnualSpend`,
   `CancellationDeadline`) with the resolved band as a small value: the
   representative position string, the adapter name, the sample size and the
   as-of date, **or** an abstention marker. This DTO exists **precisely so the
   builder never sees a real `Contract`** (`:5-17`), and the same fence is
   restated in `RenewalPipelineBuilder.cs:16-20`.
3. **Fill the fields, keep the purity.** In
   `backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs`, replace
   the hardcoded nulls at `:91-92` so `MarketPosition` carries the band and
   `AnnualUpliftPercent` is filled **only where the band supports it**. Sweep the
   stale comment at `:89-90` ("Benchmark/Savings fields are always null this
   wave"). ⚠ The constructor stays
   `RenewalPipelineBuilder(RenewalEngine renewalEngine, IClock clock)` (`:22`)
   and the class doc comment's purity claim (`:6-21`, especially `:10`) stays
   **true**: no database call, no HTTP call, no LLM call (Appendix C rule 6).
4. **The two honest shapes, and nothing else.** A representative position
   (above / in line with / below market) **carrying adapter, sample size and
   as-of date**, or the explicit string **"insufficient market data"** when the
   adapter abstains (below `MinimumViableSampleSize = 5`,
   `backend/src/Raffa.Market/Benchmark/MarketFeedBenchmarkAdapter.cs:141`, abstain
   at `:282`) or the key is incomplete. Never a percentile alone, never the word
   "market" unqualified, never "Not determined".
5. **`PotentialSavingsRange` stays null** unless the band yields one. An honest
   null is not a defect and must not be filled with a guess.
6. **Do not reach `Raffa.Market` from the module.** The allow-list permits
   `Raffa.Renewals → [SharedKernel, Raffa.Benchmark]`, so the module may depend
   on the **port** `IBenchmarkService`
   (`backend/src/Raffa.Benchmark/IBenchmarkService.cs:28`); the **adapter** lives
   in `Raffa.Market`, which is **not** allow-listed. In practice the module
   needs neither: the host resolves and passes the answer in.

## Parent story AC covered

- AC-1 For a candidate whose key resolves and whose adapter answers, `marketPosition` is **non-null** and reads as a **representative** position — not a bare percentile, not "market" unqualified.
- AC-2 The claim carries its **adapter name, sample size and as-of date**.
- AC-3 When the adapter abstains or the key is incomplete, it says **"insufficient market data"** — never "Not determined", never a fabricated number.
- AC-4 `RenewalPipelineBuilder` remains **pure and synchronous**; its unit tests need **no new fake service**.
- AC-5 `Raffa.Renewals` references **no** type from `Raffa.Market`; `DependencyDirectionTests` stays green with its allow-list unchanged.
- AC-6 `PotentialSavingsRange` stays `null` unless the band yields one, and the "always null this wave" comment is gone.
- AC-7 `git diff --stat origin/main -- web/` is **empty**.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` | modify — resolve the band per candidate before `Build(candidates)` and carry it on the DTO |
| `backend/src/Raffa.Renewals/Application/RenewalDashboardCandidate.cs` | modify — carry the resolved band (position, adapter, sample size, as-of) or an abstention marker |
| `backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs` | modify — fill `MarketPosition` and, where supported, `AnnualUpliftPercent`; sweep the stale comment at `:89-90`; constructor unchanged |
| `backend/tests/Raffa.Renewals.Tests/RenewalPipelineBuilderTests.cs` | modify — a supplied band fills the field; an abstention yields "insufficient market data"; **no fake service is introduced** |
| `backend/tests/Raffa.Api.Tests/RenewalsEndpointTests.cs` | modify — the endpoint resolves the band and emits a non-null `marketPosition`; an abstaining adapter emits the abstention string |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-001 w17 clause 4** — the two honest shapes. The band must be **labelled representative**: it comes from the existing fixture / market-feed adapter at the R3/R4 gate, and **a fixture figure is never presented as live market truth**.
  - **ADR-002 w17 clause 2** — ⚠ **the intake's "inject and wire" framing is wrong and must not be followed.** Injecting `IBenchmarkService` into the builder would break the purity the type documents at `:10` and would be the exact "fix" this clause is written to forbid. Permission was never the blocker; a **resolved key** was.
  - **ADR-024 w17 clause 6** — the wire shape does not move.
- **Why this is not a contract change**: `RenewalInsightRecommendations`
  (`backend/src/Raffa.Renewals/Application/RenewalPipelineItem.cs:75-80`) already
  declares `string? MarketPosition` at `:79`; the endpoint already emits it
  (`RenewalsEndpointExtensions.cs:287`); the OpenAPI already declares it
  (`web/openapi/raffa-api.v1.json:3310`); and Contract 360 already reads it
  (`contract360ViewModel.ts:175`). Today that read is **dead code** because the
  value is always null — which is precisely the defect.
- **Do not touch**: `web/openapi/raffa-api.v1.json`,
  `web/src/api/generated/schema.ts`, `web/src/api/client.ts` or anything under
  `web/src` — this item has no web half; `backend/src/Raffa.Api/ContractsEndpointExtensions.cs`
  and `Contract360Result.cs` (phase 1's, `E21/F01/US01/T01`);
  `backend/src/Raffa.Insights/**` and `InsightsEndpointExtensions.cs` (that is
  `E21/F03/US01/T01`'s file, this same phase — **do not open it**);
  `backend/src/Raffa.Market/**` and `backend/src/Raffa.Benchmark/**` (the
  adapters answer as they are).
- ⚠ **Renewals test fixtures already assert `marketPosition: null`** in four
  places (`web/tests/api/client.test.ts:902`,
  `web/tests/routes/renewals/renewalPipelineViewModel.test.ts:50`,
  `web/tests/routes/renewals/RenewalsRoute.test.tsx:91`,
  `web/tests/routes/contracts/contract360/Contract360Route.test.tsx:236`). Those
  are **web fixtures for a nullable field and stay valid** — a nullable field
  that can now be non-null does not invalidate a null fixture. **Do not edit
  them**; `web/` is out of scope for this task.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Renewals.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0
- [ ] `grep -n "RenewalPipelineBuilder(" backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs` still shows exactly `(RenewalEngine renewalEngine, IClock clock)`
- [ ] `grep -rn "IBenchmarkService\|Raffa.Market" backend/src/Raffa.Renewals/` returns nothing
- [ ] `grep -n "always null this wave" backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs` returns nothing
- [ ] `grep -n "MarketPosition: null" backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs` returns nothing
- [ ] `grep -n "Not determined" backend/src/Raffa.Renewals/ backend/src/Raffa.Api/RenewalsEndpointExtensions.cs -r` returns nothing
- [ ] `git diff --stat origin/main -- web/` is empty

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | a candidate carrying a resolved band produces a non-null `MarketPosition` with adapter, sample size and as-of; a candidate carrying an abstention produces "insufficient market data"; **the test file introduces no fake benchmark service** | `backend/tests/Raffa.Renewals.Tests/RenewalPipelineBuilderTests.cs` |
| unit | `PotentialSavingsRange` stays null when the band yields none | `backend/tests/Raffa.Renewals.Tests/RenewalPipelineBuilderTests.cs` |
| integration | the renewals endpoint resolves the band per candidate and emits it; an abstaining adapter emits the abstention string, not null | `backend/tests/Raffa.Api.Tests/RenewalsEndpointTests.cs` |
| architecture | `Raffa.Renewals`' allow-list is unchanged and the module still references no `Raffa.Market` type | `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` |

## Open questions blocking this task

- **none blocking.** OQ-w17-004's geography half is ruled (the workspace
  country, resolved in the host, labelled representative).

## Wave-spec entry

```yaml
- id: E21/F02/US01/T01
  prompt: reports/workitems/epic-21-contract-360-answers/feature-02-renewal-market-position/us-01-renewal-market-position/tasks/task-01-renewal-market-position.md
  produces: [renewal-market-position]
  depends_on: [benchmark-key-resolver]
  effort: M
  layer: backend
  status: live
```
