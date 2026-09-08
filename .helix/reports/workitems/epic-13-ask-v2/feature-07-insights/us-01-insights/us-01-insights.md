---
id: us-01
type: user-story
parent: feature-07
wave: 13
status: active
---

# us-01-insights — Strategies rest on calculators, not on a model's guess

## Story

As **procurement**, I want the criticality ranking of my portfolio, the
negotiation targets for a contract and the strategy steps Contigo proposes
to be computed the same way every time and explained component by
component, so that Ask, Contract 360 and Renewals show the same numbers
and I can defend them in a negotiation.

## Acceptance criteria

- [ ] AC-1 `CriticalityScoreCalculator` returns 0–100 per contract with
      components (renewal urgency, risk severity, spend weight, savings
      potential, open critical facts), each with a score and an
      explanation; components sum to the total; same inputs → same
      ranking; weights come from configuration.
- [ ] AC-2 A contract whose critical facts are all weak is flagged
      "validate first" in its explanation and its criticality is raised, not
      hidden.
- [ ] AC-3 `NegotiationStrategyCalculator` accepts a *priced line*
      (quantity, unit price, term, benchmark band) so a contract line item
      yields opening target, acceptable range, walk-away and levers exactly
      as a quote line does; existing quote tests still pass unchanged.
- [ ] AC-4 `StrategyPackBuilder` composes, for one contract, the renewal
      engine result, priority components, insight-card recommendation,
      benchmark position per line, contract-level levers, market notes and
      open weak facts into a pack with citation keys; a passed deadline is
      stated as passed; no benchmark match → no targets, levers only,
      "insufficient market data".
- [ ] AC-5 `GET /api/insights/criticality` and
      `GET /api/contracts/{id}/strategy` return the same numbers the Ask
      pack narrates (shared builder, one source).

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-v2-foundation | `Contigo.Insights` project exists (T01) |

## Architecture decisions in force

- ADR-024 — deterministic strategies; `Contigo.Insights` → `[SharedKernel, Benchmark]`
- ADR-002 — Insights takes DTOs, never `Contract` / `Renewal` entities; composition in `Contigo.Api`
- ADR-001 (amended) — bands from the mock feed, labelled representative
- spec §9.2, §10.4, §12.1; Appendix C rules 6 and 10

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Criticality calculator, priced-line levers, strategy pack builder, Insights endpoints (unmapped) | L | phase-2 |

## Council decisions carried into this story

Criticality weights `Insights:Criticality:{RenewalUrgency=0.30,
RiskSeverity=0.20, SpendWeight=0.20, SavingsPotential=0.20,
OpenCriticalFacts=0.10}` (configuration, sum 1.0). Priced line record
`PricedLine(Sku?, Description, Quantity, UnitPrice, Currency, TermMonths,
Benchmark?)` shared by quotes and contracts. Strategy sections in this
order: When you must move → Where you can push → Targets → Next steps
(four steps mirroring the Contract 360 tracker: Notify · Request revised
pricing and licence mix · Counter with the market benchmark · Sign, or send
non-renewal notice — `contigo-v2/app.jsx` `stepDefs`).

## Open questions

- none
