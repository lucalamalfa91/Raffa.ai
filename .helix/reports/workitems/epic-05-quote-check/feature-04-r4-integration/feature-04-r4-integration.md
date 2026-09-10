---
id: feature-04
type: feature
parent: epic-05
wave: R4
status: active
---

# feature-04-r4-integration — R4 Quote Check integration (Day-1 path)

## Slice

Single-task integration story depending on every R4 leaf artifact, proving the
customer Day-1 path (spec §20) on `demo`: upload quote → line items → market
assessment → target range → negotiation strategy.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | final-integration | R4 |

## Architecture decisions in force

- ADR-001, ADR-002, ADR-003, ADR-009, ADR-016.

## Target repo

`raffa-backend` (integration test) + `raffa-web` (smoke)
