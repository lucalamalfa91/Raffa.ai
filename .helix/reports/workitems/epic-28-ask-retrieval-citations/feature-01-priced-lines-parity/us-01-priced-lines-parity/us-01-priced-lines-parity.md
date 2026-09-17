---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-priced-lines-parity — Ask strategy numbers equal the 360 `/strategy`

## Story
As the **Ask engine**, I want the named-supplier strategy/market-compare pack
to use the same benchmarked priced-lines as `/strategy`, so Ask and 360 never
drift.

## Acceptance criteria
- [ ] AC-1 Ask composition calls async `ToPricedLines` + `BenchmarkKeyResolution` (supplier, workspace country).
- [ ] AC-2 the numbers match `GET /api/contracts/{id}/strategy` opening/range/walk-away when bands exist.
- [ ] AC-3 a missing band narrates "insufficient market data", never a fabricated percentile, with provenance.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 16)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `InsightsEndpointExtensions.ToPricedLines` (async) and `BenchmarkKeyResolution` already exist |

## Architecture decisions in force
- ADR-024 w19 (cl. 16) — async priced-lines + workspace country key.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | priced-lines-parity | M | phase-1 |

## Council decisions carried into this story
- async `ToPricedLines` + `BenchmarkKeyResolution`; missing band = "insufficient market data"; provenance on every number.

## Open questions
- none
