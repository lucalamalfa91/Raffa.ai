---
id: epic-04
type: epic
wave: R3
status: active
---

# epic-04-savings-intelligence — Savings Intelligence (R3)

## Business capability

Introduce the Benchmark Service interface with a replaceable **fixture adapter** (no
paid external market API for the first `demo`), normalize current unit prices,
compare against P25/P50/P75 with confidence + provenance, and surface a savings
dashboard plus a trackable SavingsOpportunity workflow — so that **Raffa quantifies
credible savings opportunities** (spec §16 R3).

## Product coverage

| Source | Item |
|--------|------|
| spec §16 | R3 — Savings (definition of success) |
| spec §4.3 | KPIs, normalized price comparison, percentile, target, workflow |
| spec §10 | benchmark service boundary, internal contract, matching, trust |
| spec §17.1 | current price, P25/P50/P75, percentile, target, saving, confidence |
| ADR-002 | Benchmark + Savings bounded contexts |
| ADR-003 | PostgreSQL (savings/benchmark entities) |
| ADR-004 | (no Foundry call — deterministic math only) |
| ADR-009 | tenancy / RLS |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | benchmark-service (interface + fixture adapter) | R3 |
| feature-02 | savings-engine (normalize + compare + opportunity) | R3 |
| feature-03 | savings-dashboard (KPIs + opportunity workflow) | R3 |
| feature-04 | r3-integration | R3 |

## Success looks like

On `demo`: a matched contract shows its current unit price against a fixture-provided
P25/P50/P75 with confidence and provenance, the system computes a target range and
saving, and a SavingsOpportunity can be created, owned, and marked realized — all
using the internal fixture adapter, never a paid provider.

## Architecture decisions in force

- ADR-001 (fixture adapter, no paid API for first demo), ADR-002, ADR-003, ADR-009.

## Out of scope

- Provider-specific benchmark adapters (only the fixture adapter for the first demo).
- Quote check / negotiation strategy (R4).
