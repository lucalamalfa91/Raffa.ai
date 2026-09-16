---
id: E21/F03/US01/T01
type: task
story: us-01-strategy-benchmark-wired
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-strategy-benchmark-wired — a real band in /strategy and the client method that calls it

## Context

**Closes: NW-62 (the server half).**

Decision row: `reports/architecture/waves/w17.md` — the **NW-62** row
(product-owner and software-architect halves, and the client-architect half's
loading rule), plus the rulings on **OQ-w17-005**, **OQ-w17-004** and
**OQ-w17-sa-01**.

ADRs in force: **ADR-001** w17 clause 4; **ADR-024** w17 clauses 7, 9 and A4;
**ADR-002** w17 clause 3; **ADR-012** w16 clause 25 and w17 clauses 35, 39.

Already on `main`, so **not** work to redo: the route
(`backend/src/Raffa.Api/InsightsEndpointExtensions.cs:64`), the handler (`:156`),
`StrategyPack` with both sections
(`backend/src/Raffa.Insights/Contracts/StrategyPack.cs:26-32`, `WhenYouMustMove`
at `:51-56`), and the operation in the generated client
(`web/src/api/generated/schema.ts:724`, path binding `:928-929`). What is
missing is a **resolved benchmark key** and a **hand-written client wrapper**.

⚠ **This task is the phase-2 writer of `web/src/api/client.ts`**
(ADR-012 w16 clause 25 — hand-written glue, one writer per phase).
`E22/F03/US01/T01` is its phase-3 writer. Do not open it in any other phase-2
task.

## Coding objective

In `raffa-backend`, give `/strategy` a real market band, and add the one client
method that can call it.

1. **Resolve the key in the host.** In
   `backend/src/Raffa.Api/InsightsEndpointExtensions.cs`, in
   `GetContractStrategyAsync` (`:156`, body `:164-204`), resolve the
   **(supplier name, geography)** pair with phase 1's shared resolver —
   geography from the **workspace country**
   (`backend/src/Raffa.Identity.Workspace/Domain/WorkspaceTenant.cs:46`, ISO
   3166-1 alpha-2) — and pass both into `StrategyInputs` via `ToStrategyInputs`
   (`:348`, construction `:364-374`), replacing `SupplierName: null` at `:366`.
   ⚠ The module cannot do this itself: `:26-37` records that `Raffa.Insights`'
   allow-list is exactly `[SharedKernel, Benchmark]`, so it cannot read the
   workspace. The **host** resolves; the module receives.
2. **Make `ToPricedLines` call the adapter.** In the same file, `ToPricedLines`
   (`:313`, doc `:305-312`) hardcodes `Benchmark: null` at `:325` (with
   `TermMonths: null` `:324` and `SampleSize: null` `:326`). With the key
   resolved, call `IBenchmarkService.GetBenchmarkAsync`
   (`backend/src/Raffa.Benchmark/IBenchmarkService.cs:44-45`) and fill the
   benchmark, the term and the sample size from the result. That file's own doc
   names this method as the place.
3. **The two honest shapes, and the label.** A **representative** position
   carrying adapter, sample size and as-of date, or the explicit **"insufficient
   market data"** when the adapter abstains
   (`backend/src/Raffa.Market/Benchmark/MarketFeedBenchmarkAdapter.cs:282`, taken
   at `:163`, `MinimumViableSampleSize = 5` at `:141`) or the key is incomplete.
   ⚠ **Never a fabricated number**, never a bare percentile, never "market"
   unqualified, never "Not determined". Delete or correct the comment at
   `:39-54` which states the key is unobtainable — it is obtainable now.
4. **One resolution per screen.** Use phase 1's resolver, the same one
   `E21/F01/US01/T01` used for the 360's `benchmark` member. ⚠ Resolving here
   independently would let one screen show **two answers for one question**
   (ADR-024 w17 clause 7).
5. **Leave `WhenYouMustMove` alone in shape.** It is already answerable from
   the persisted columns; it is null only when extraction missed the fact. Do
   not add a fallback that invents a date.
6. **Add the client wrapper — it does not exist.** In
   `web/src/api/client.ts`, add `getContractStrategy` following the file's own
   convention for a generated-types-backed wrapper: derive the response type
   from `paths["/api/contracts/{id}/strategy"]["get"]["responses"]` exactly as
   `getContractEvidence` derives its own at `:675-679`, declare it on the client
   interface beside `:1346`, and implement it beside `:2443`. ⚠ Verified on this
   checkout: the operation is in `schema.ts` but `getContractStrategy` appears
   **zero times** in `client.ts`, and every API call in this product goes
   through this glue — so "already generated" is not "already callable".
7. **Do not touch `OpenWeakFacts`.** `StrategyPack.cs:32` stays as it is, and no
   new consumer is added. It is filtered at `0.8`
   (`backend/src/Raffa.Insights/Criticality/CriticalityScoreCalculator.cs:35`)
   while Review moves to `0.90`; rendering it as a review statement would put
   **two definitions of "weak" for one contract** on two screens
   (OQ-w17-sa-01).

## Parent story AC covered

- AC-1 `GET /api/contracts/{id}/strategy` returns `WhereYouCanPush` entries whose priced-line targets carry a **non-null** benchmark when supplier name and workspace country both resolve.
- AC-2 The band is **labelled representative** and carries adapter, sample size and as-of date; abstention or an incomplete key yields **"insufficient market data"**.
- AC-3 `WhenYouMustMove` continues to answer from the persisted columns and is unchanged in shape.
- AC-4 Geography is resolved **in the host**; `Raffa.Insights` references no workspace type and its allow-list is unchanged.
- AC-5 `web/src/api/client.ts` exposes `getContractStrategy`, typed from the generated `paths` the way `getContractEvidence` is.
- AC-6 `npx tsc --noEmit` and `npm test` exit 0 in `web/`.
- AC-7 `StrategyPack.OpenWeakFacts` is **unchanged** and gains no consumer.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/InsightsEndpointExtensions.cs` | modify — resolve the key in the host, pass it into `StrategyInputs`, make `ToPricedLines` call the adapter, correct the `:39-54` comment |
| `web/src/api/client.ts` | modify — add the `getContractStrategy` wrapper (type, interface member, implementation) following the `getContractEvidence` convention |
| `backend/tests/Raffa.Insights.Tests/InsightsEndpointCompositionTests.cs` | modify — the host resolves the key and the module stays fenced |
| `backend/tests/Raffa.Insights.Tests/StrategyPackBuilderTests.cs` | modify — a supplied band reaches the priced-line targets; an abstention yields the explicit insufficiency; `OpenWeakFacts` is untouched |
| `backend/tests/Raffa.Api.Tests/ContractStrategyEndpointTests.cs` | **new** — the endpoint's band, its abstain path, and cross-tenant isolation |
| `web/tests/api/client.test.ts` | modify — `getContractStrategy` calls the right path with the tenant header and surfaces a failure as a result, not a throw |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-024 w17 clause 9 / OQ-w17-005** — **consume the existing endpoint.** It already returns both sections and has no consumer; computing a second answer is the divergence ADR-012 forbids.
  - **ADR-024 w17 clause 7 / OQ-w17-004** — geography is the workspace country, resolved in the host; **no per-contract geography column this wave** (a migration the cap cannot afford); **one resolution per screen**.
  - **ADR-024 w17 clause A4 / OQ-w17-sa-01** — `OpenWeakFacts` is a criticality input, **not** a review decision. Do not render it and do not re-band it.
  - **ADR-001 w17 clause 4** — the R3/R4 gate: the band comes from the existing fixture / market-feed adapter and **a fixture figure is never presented as live market truth**. It must be **labelled representative**.
  - **ADR-012 w16 clause 25** — `client.ts` is hand-written glue and **one-writer-per-phase**; this task is its phase-2 writer.
- **Do not touch**: `web/openapi/raffa-api.v1.json` and
  `web/src/api/generated/schema.ts` — `E22/F01/US01/T01` owns them **in this same
  phase**; the strategy operation is already in both and needs no edit. Anything
  under `web/src/routes/` (that is `us-02`, phase 3).
  `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` and `Contract360Result.cs`
  (phase 1's). `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` and
  `backend/src/Raffa.Renewals/**` (`E21/F02/US01/T01`'s files, **this same
  phase** — do not open them). `CriticalityScoreCalculator.cs`.
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:97-101` — the
  Answers band is *Where you can save* (estimate + lever), *When you must move*
  (notice deadline, days left, auto-renews), *What to do*. The provenance
  vocabulary to reuse is `ia-v2.md:110`'s (`adapter A, n = 214`), established for
  Ask — **do not invent a second one for the same idea**. Rendering is `us-02`'s;
  this task must make sure the **data** for state (ii) is on the wire.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Insights.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0
- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npm test` exits 0
- [ ] `grep -n "Benchmark: null" backend/src/Raffa.Api/InsightsEndpointExtensions.cs` returns nothing
- [ ] `grep -c "getContractStrategy" web/src/api/client.ts` returns **3 or more** (type, interface member, implementation)
- [ ] `grep -rn "Identity.Workspace\|WorkspaceTenant" backend/src/Raffa.Insights/` returns nothing
- [ ] `grep -n "Not determined" backend/src/Raffa.Api/InsightsEndpointExtensions.cs` returns nothing
- [ ] `git diff --stat origin/main -- web/openapi web/src/api/generated` is **empty** for this task's commits

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the host resolves supplier name and geography and the module never reads the workspace; the allow-list is unchanged | `backend/tests/Raffa.Insights.Tests/InsightsEndpointCompositionTests.cs` |
| unit | a supplied band reaches the priced-line targets with adapter, sample size and as-of; an abstention yields the explicit insufficiency string; `OpenWeakFacts` is byte-identical | `backend/tests/Raffa.Insights.Tests/StrategyPackBuilderTests.cs` |
| integration | `GET /api/contracts/{id}/strategy` returns a non-null benchmark for a resolvable contract, the abstention string otherwise, and 404s across tenants | `backend/tests/Raffa.Api.Tests/ContractStrategyEndpointTests.cs` |
| unit | the client wrapper calls `/api/contracts/{id}/strategy` with the tenant header and returns a result object on failure rather than throwing | `web/tests/api/client.test.ts` |

## Open questions blocking this task

- **none blocking.** OQ-w17-005, OQ-w17-004 and OQ-w17-sa-01 are all ruled.

## Wave-spec entry

```yaml
- id: E21/F03/US01/T01
  prompt: reports/workitems/epic-21-contract-360-answers/feature-03-answers-band/us-01-strategy-benchmark-wired/tasks/task-01-strategy-benchmark-wired.md
  produces: [strategy-benchmark-wired]
  depends_on: [benchmark-key-resolver]
  effort: M
  layer: backend
  status: live
```
