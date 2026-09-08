---
id: F07
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-07-insights — Criticality score, contract-level levers, strategy pack

## Slice

The deterministic numbers Ask narrates for strategies (HITL decision D6):
a **criticality score** 0–100 per validated contract with explained
components (renewal urgency from the existing priority score, risk
severity, spend weight, savings potential, open critical facts; weights are
configuration), **contract-level negotiation levers** by generalizing
`NegotiationStrategyCalculator` from quote lines to a *priced line* input
(opening / range / walk-away + levers with evidence), and a **strategy pack
builder** (When you must move → Where you can push → Targets → Next steps)
that also feeds `GET /api/contracts/{id}/strategy` and
`GET /api/insights/criticality` so the UI and Ask show the same numbers
(`inputs/requirements.md` R-STR-01…03, R-PORT-01…03, R-CMP-01).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Strategies rest on calculators, not on a model's guess | 13 |

## Architecture decisions in force

- ADR-024 — module map (`Contigo.Insights` → `[SharedKernel, Benchmark]`), deterministic strategies
- ADR-001 (amended) — benchmark bands from the mock feed, labelled representative
- spec §9.2, §10.4, §12.1, Appendix C rules 6 and 10

## Target repo

`contigo-backend`
