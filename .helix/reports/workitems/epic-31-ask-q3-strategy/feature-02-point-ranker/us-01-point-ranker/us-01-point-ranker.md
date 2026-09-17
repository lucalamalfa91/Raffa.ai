---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-point-ranker — The ranker emits only grounded points, in order

## Story
As the **Ask engine**, I want a ranker that emits only grounded negotiation
points (never a generic 7-lever dump), so the chat narration is honest and the
top 3 are real.

## Acceptance criteria
- [ ] AC-1 each point carries topic/rank/current/target/whyItMatters/citationKeys/strength, and is emitted only if grounded (stored fact, clause, band, or peer chunk).
- [ ] AC-2 order: above-band price → uncapped/high liability → auto-renew+short notice → SLA/credits → term/volume → payment terms.
- [ ] AC-3 chat cap top 3; persist-all via `BuildNegotiationPointsPackAsync(contractId, includeRenewalUrgency, persistTodos)`.

## Definition of done
- [ ] every AC verified by `NegotiationPointRankerTests`
- [ ] honours ADR-024 w19 (cl. 23)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| epic-28 priced-lines (NW-82) | the above-band price rank needs the benchmarked line |

## Architecture decisions in force
- ADR-024 w19 (cl. 23) — grounded-only; Insights stays `[SharedKernel, Benchmark]`.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | point-ranker | L | phase-3 |

## Council decisions carried into this story
- grounded-only; top-3 chat / persist-all; host maps DTOs into `StrategyInputs` (Insights fenced).

## Open questions
- none
