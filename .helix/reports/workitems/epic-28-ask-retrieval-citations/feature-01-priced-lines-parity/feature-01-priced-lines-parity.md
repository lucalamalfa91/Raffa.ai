---
id: feature-01
type: feature
parent: epic-28
wave: w19
status: active
---

# feature-01-priced-lines-parity — Ask uses benchmarked priced-lines (NW-82)

## Slice
Ask's strategy/market-compare pack uses async `ToPricedLines` +
`BenchmarkKeyResolution` (supplier, workspace country) so the numbers match
`GET /api/contracts/{id}/strategy`; a missing band becomes "insufficient market
data", never an invented percentile.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | priced-lines-parity | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 16) — same numbers as 360 `/strategy`; provenance on every market number.

## Target repo
`raffa-backend`
