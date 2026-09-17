---
id: E28/F01/US01/T01
type: task
story: us-01-priced-lines-parity
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-priced-lines-parity — Ask uses benchmarked priced-lines (NW-82)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs`, replace the **sync**
`InsightsEndpointExtensions.ToPricedLines` call in `BuildRenewalStrategyPackAsync`
(and align `BuildMarketComparePackAsync`) with the **async** overload backed by
`IBenchmarkService` + `BenchmarkKeyResolution` (supplier name, workspace
country), so the Ask strategy/market-compare numbers match
`GET /api/contracts/{id}/strategy` (which already uses the async path). A
missing band produces an "insufficient market data" snippet with a provenance
label (representative · adapter · n · as-of), never a fabricated percentile.
Keep `Raffa.Insights` fenced — map clause/risk/commercial snapshots into
`StrategyInputs` DTOs in `Raffa.Api`.

## Parent story AC covered
- AC-1 async `ToPricedLines` + `BenchmarkKeyResolution`.
- AC-2 numbers match `/strategy` where bands exist.
- AC-3 missing band = "insufficient market data" + provenance.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | async priced-lines + benchmark key in strategy/market-compare packs |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 16).
- **Do not touch**: `InsightsEndpointExtensions.ToPricedLines` (reuse); `Raffa.Insights` allow-list.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test that the Ask pack opening/range/walk-away equal the `/strategy` values for a band, and a missing band narrates "insufficient market data"

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | Ask pack numbers == `/strategy` | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E28/F01/US01/T01
  prompt: reports/workitems/epic-28-ask-retrieval-citations/feature-01-priced-lines-parity/us-01-priced-lines-parity/tasks/task-01-priced-lines-parity.md
  produces: [priced-lines-parity]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
