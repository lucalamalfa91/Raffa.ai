---
id: feature-02
type: feature
parent: epic-05
wave: R4
status: active
---

# feature-02-quote-assessment — Benchmark match → market position

## Slice

Match normalized line items to the Benchmark Service, flag above/in-line/below
market, and produce a market assessment with recommended target range and potential
saving (deterministic math, no LLM for money).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Benchmark matching + market assessment | R4 |

## Architecture decisions in force

- ADR-002 (Quotes context → Benchmark interface)
- ADR-001 (fixture adapter)
- ADR-003 (PostgreSQL)

## Target repo

`raffa-backend`
