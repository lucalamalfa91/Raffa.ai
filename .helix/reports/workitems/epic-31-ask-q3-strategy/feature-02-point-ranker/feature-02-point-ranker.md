---
id: feature-02
type: feature
parent: epic-31
wave: w19
status: active
---

# feature-02-point-ranker — `NegotiationPointRanker`: grounded points only (NW-96)

## Slice
A pure calculator in `Raffa.Insights` emits only grounded points
(topic/rank/current/target/whyItMatters/citationKeys/strength), ordered
above-band price → uncapped/high liability → auto-renew+short notice → SLA/
credits → term/volume → payment terms; chat top-3, persist-all via the shared
`BuildNegotiationPointsPackAsync`.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | point-ranker | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 23) — grounded-only; order; chat top-3 / persist-all.

## Target repo
`raffa-backend`
