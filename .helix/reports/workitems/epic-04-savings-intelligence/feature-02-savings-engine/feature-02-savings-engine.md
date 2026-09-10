---
id: feature-02
type: feature
parent: epic-04
wave: R3
status: active
---

# feature-02-savings-engine — Normalize + compare + opportunity

## Slice

Normalize current unit price, compare against benchmark percentiles, compute
percentile/target/saving deterministically, and persist a trackable
SavingsOpportunity with status, owner, and realized outcome.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Price normalization + percentile comparison | R3 |
| us-02 | SavingsOpportunity workflow + realized outcome | R3 |

## Architecture decisions in force

- ADR-002 (Savings context)
- ADR-003 (PostgreSQL)
- ADR-001 (fixture adapter, deterministic math)

## Target repo

`raffa-backend`
