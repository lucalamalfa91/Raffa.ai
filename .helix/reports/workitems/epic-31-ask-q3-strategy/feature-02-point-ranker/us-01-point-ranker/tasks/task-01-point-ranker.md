---
id: E31/F02/US01/T01
type: task
story: us-01-point-ranker
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-point-ranker — `NegotiationPointRanker` grounded points only (NW-96)

## Coding objective
Add `NegotiationPointRanker` to `Raffa.Insights` (`[SharedKernel, Benchmark]`
allow-list) producing grounded negotiation points
(`topic`/`rank`/`current`/`target`/`whyItMatters`/`citationKeys`/`strength`),
emitting a point **only** when grounded (a stored fact, clause, benchmark band,
or peer chunk) — never the generic 7-lever dump (no "no utilization data yet").
Order: above-band price → uncapped/high liability → auto-renew+short notice →
SLA/credits → term/volume → payment terms. Add the shared
`BuildNegotiationPointsPackAsync(contractId, includeRenewalUrgency,
persistTodos)` host helper (chat top-3, persist-all), and map clause/risk/
commercial snapshots into `StrategyInputs` DTOs in `Raffa.Api` (Insights stays
fenced). Chat marks at most 3.

## Parent story AC covered
- AC-1 grounded-only points with the full field set.
- AC-2 ordering.
- AC-3 chat top-3 / persist-all via the shared helper.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Insights/Application/NegotiationPointRanker.cs | new: grounded-only ranker |
| backend/src/Raffa.Api/AskCopilotService.cs | `BuildNegotiationPointsPackAsync` host helper + DTO mapping |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 23) — grounded-only, top-3/persist-all.
- **Do not touch**: `Raffa.Insights` allow-list (no Documents reference); the TODO entity (epic-29).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Insights.Tests` exits 0, with `NegotiationPointRankerTests` proving above-band price outranks ungrounded payment-terms and the generic dump is not an answer

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | grounded-only + ordering | `backend/tests/Raffa.Insights.Tests/NegotiationPointRankerTests.cs` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E31/F02/US01/T01
  prompt: reports/workitems/epic-31-ask-q3-strategy/feature-02-point-ranker/us-01-point-ranker/tasks/task-01-point-ranker.md
  produces: [point-ranker]
  depends_on: [priced-lines-parity]
  effort: L
  layer: backend
  status: live
```
