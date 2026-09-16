---
id: feature-02
type: feature
parent: epic-21
wave: w17
status: active
extends: epic-03 F01/F03
---

# feature-02-renewal-market-position — the renewal insight carries a real market band

## Slice

`backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs:91-92`
constructs `new RenewalInsightRecommendations(action, explanation,
AnnualUpliftPercent: null, MarketPosition: null, PotentialSavingsRange: null)`
with all three benchmark fields hardcoded `null` and a comment at `:89-90`
saying so. Only `action` and `explanation` are computed. This feature fills
`MarketPosition` (and `AnnualUpliftPercent` where the band supports it) with a
**representative** market position carrying its adapter, sample size and as-of
date — or an explicit "insufficient market data".

`marketPosition` is **already on the wire**
(`backend/src/Raffa.Api/RenewalsEndpointExtensions.cs:287`,
`web/openapi/raffa-api.v1.json:3310`) and **already consumed** by Contract 360
(`web/src/routes/contracts/contract360/contract360ViewModel.ts:175`), so filling
the null needs **no contract change and no web change**: it makes `lever` real
at a call site that has been reading a constant since the screen shipped.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | renewal-market-position | w17 |

## Extends

**epic-03 F01/F03** (`feature-01-renewal-engine`,
`feature-03-renewal-dashboard`) — the builder and the insight card built there
reserved these fields. **epic-04 F01** (`feature-01-benchmark-service`) — the
adapter that answers.

## Architecture decisions in force

- **ADR-001 w17 clause 4** — a market claim has exactly **two** honest shapes: a
  **representative** position (above / in line with / below market) carrying
  **adapter, sample size and as-of date**, or an explicit **"insufficient market
  data"**. Never a percentile alone, never "market" unqualified, never "Not
  determined".
- **ADR-002 w17 clause 2** — ⚠ **this corrects the intake's framing ("inject and
  wire").** **Do not inject `IBenchmarkService` into `RenewalPipelineBuilder`**:
  the type's own doc comment states it is *"Pure and synchronous — no database
  call, no HTTP call, no LLM call (Appendix C rule 6)"*
  (`RenewalPipelineBuilder.cs:10`, block `:6-21`), while the service is async and
  adapter-backed (`MarketFeedBenchmarkAdapter.cs:177-191` reads
  `market_record`). **Permission was never the blocker — a resolved key was**:
  `DependencyDirectionTests.cs`'s allow-list already permits
  `Raffa.Renewals → [SharedKernel, Raffa.Benchmark]`, and the **port**
  `IBenchmarkService` does live in `Raffa.Benchmark` — while the **adapter**
  lives in `Raffa.Market`, which is **not** allow-listed. So the module may
  depend on the port; **only the host may compose the adapter.** The purity rule
  is written into the ADR footer so a later task does not "fix" this by
  injection.
- **ADR-024 w17 clause 6** — the wire shape is unchanged; `MarketPosition` is a
  `string?` on `RenewalInsightRecommendations`
  (`backend/src/Raffa.Renewals/Application/RenewalPipelineItem.cs:79`) and stays
  one. `PotentialSavingsRange` stays null unless the band yields one — **an
  honest null is not a defect**.

## Target repo

`raffa-backend` — `backend/src/Raffa.Renewals`, `backend/src/Raffa.Api`. **No
`web/` change and no contract change.**
