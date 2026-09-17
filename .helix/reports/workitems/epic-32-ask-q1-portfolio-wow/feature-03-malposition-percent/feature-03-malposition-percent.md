---
id: feature-03
type: feature
parent: epic-32
wave: w19
status: active
---

# feature-03-malposition-percent — Pure mal-position % calculator (NW-88)

## Slice
A pure calculator computes `linePct = (unitPrice − media)/media × 100` (media =
P50), contract % = spend-weighted avg of positive linePcts; omitted for
insufficient data; `actionableIn2026` flag from stored facts. Queued (W20).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | malposition-percent | w20 |

## Architecture decisions in force
- ADR-024 w19 (cl. 19) + lock 1/3.

## Target repo
`raffa-backend`
