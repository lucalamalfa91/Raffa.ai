---
id: feature-01
type: feature
parent: epic-03
wave: R2
status: active
---

# feature-01-renewal-engine — Deterministic renewal dates + priority score

## Slice

Compute renewal dates and cancellation deadlines from validated structured data
(deterministic arithmetic, never the LLM), generate renewal opportunities, and
compute an explainable priority score with component breakdown.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Deterministic date + deadline computation | R2 |
| us-02 | Priority score (explainable components) | R2 |

## Architecture decisions in force

- ADR-002 (Renewals context)
- ADR-003 (PostgreSQL)

## Target repo

`raffa-backend`
