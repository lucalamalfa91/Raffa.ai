---
id: E13/F07/US01/T01
type: task
story: us-01-insights
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-insights-calculators — Criticality, priced-line levers, strategy pack builder, Insights endpoints

## Coding objective

Fill `backend/src/Contigo.Insights` (scaffolded by F01/T01) with pure,
deterministic calculators fed by DTOs (`inputs/requirements.md`
R-PORT-01, R-STR-01/02, R-CMP-01). (1) `Criticality/CriticalityScoreCalculator`
(`Calculate(ContractCriticalityInputs)` → `CriticalityScore` 0–100 with
five `CriticalityComponent(Score, Weight, Explanation)`: renewal urgency
from a `PriorityScoreResult`-shaped input normalized to 0–1, risk severity
(max of `RiskSeverity`, Critical=1 / High=0.75 / Medium=0.5 / Low=0.25 /
none=0), spend weight (annual spend ÷ portfolio annual spend), savings
potential (above-band lines × spend or the savings opportunity range
midpoint ÷ spend, capped 1), open critical facts (count of critical
fields below 0.8 ÷ number of critical fields; explanation "validate
first" when > 0.5); weights from `InsightsOptions` (defaults 0.30 / 0.20 /
0.20 / 0.20 / 0.10, sum validated); `CalculateMany` returns the ranked
list). (2) Generalize `Contigo.Quotes/Application/Strategy/NegotiationStrategyCalculator`
to a shared input `PricedLine(Sku?, Description, Quantity, UnitPrice,
Currency, TermMonths, BenchmarkDistribution?, SampleSize?)` declared in
`Contigo.Insights/Contracts/` and referenced by Quotes (Quotes may
reference Insights? — no: Quotes' allow-list is `[SharedKernel,
Benchmark]`; therefore put `PricedLine` in `Contigo.Benchmark/Contracts/`
next to `BenchmarkDistribution`, where both Quotes and Insights can see
it, and keep the calculator's core in `Contigo.Insights/Negotiation/PricedLineNegotiationCalculator`
with Quotes' existing calculator delegating to it — if Quotes cannot
reference Insights, copy the pure core into Benchmark instead and have
both delegate; state which in the README). Existing quote strategy tests
must pass unchanged. (3) `Strategy/StrategyPackBuilder.Build(StrategyInputs)`
→ `StrategyPack` with sections **When you must move** (renewal date,
cancellation deadline, days left, passed-deadline flag), **Where you can
push** (levers with rationale + evidence keys), **Targets** (opening /
range / walk-away per priced line when a band exists, else "insufficient
market data"), **Next steps** (the four tracker steps with due hints,
`contigo-v2/app.jsx` `stepDefs`), plus `openWeakFacts[]` and citation
keys for every number (`fact:<contractId>:<field>`, `market:<recordId>`,
`calc:<name>`). (4) `Contigo.Api/InsightsEndpointExtensions.cs` with
`GET /api/insights/criticality` and `GET /api/contracts/{id}/strategy`
composing tenant data (portfolio facts, risks, renewals engine outputs,
savings opportunities, line items, benchmark via `IBenchmarkService`) into
the DTOs — **not mapped** in `Program.cs` here (F06/T01 maps it in phase
3); unit-test the composition with fakes. `AddInsightsModule()` registers
calculators + options.

## Parent story AC covered
- AC-1, AC-2, AC-3, AC-4, AC-5

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Contigo.Insights/Criticality/*`, `Negotiation/*`, `Strategy/*`, `Contracts/*`, `InsightsOptions.cs`, `ServiceCollectionExtensions.cs` | new |
| `backend/src/Contigo.Benchmark/Contracts/PricedLine.cs` | new shared input (only file touched in Benchmark) |
| `backend/src/Contigo.Quotes/Application/Strategy/NegotiationStrategyCalculator.cs` (+ `NegotiationStrategyService.cs` if signatures move) | delegate to the shared core |
| `backend/src/Contigo.Api/InsightsEndpointExtensions.cs` | new, unmapped |
| `backend/tests/Contigo.Insights.Tests/*` | calculators, builder, endpoint composition with fakes |
| `backend/tests/Contigo.Quotes.Tests/*` | unchanged expectations still green |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (deterministic strategies; Insights → `[SharedKernel, Benchmark]`), ADR-002 (DTOs, no entity references across modules; composition in `Contigo.Api`), Appendix C rules 6 and 10, spec §9.2, §10.4, §12.1.
- Gap G-STRATEGY, G-CRITICALITY, G-COMPARE. Inputs available today: `Contigo.Renewals.Application.{RenewalEngine,PriorityScoreCalculator,RenewalPipelineBuilder}`, `Contigo.Documents.Contracts.Application.{PortfolioQueryService,Contract360QueryService}`, `Contigo.Savings.Application.SavingsOpportunityService`, `IBenchmarkService`.
- **Do not touch**: `Program.cs` (F05/T02 owns it this phase), `Contigo.Chat`, `DependencyDirectionTests.cs` (F01/T01 added Insights), `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.Insights.Tests` exit 0 — components sum to total; same inputs → same order; all-weak contract flagged "validate first"; priced-line targets equal the quote calculator's for identical inputs; builder states a passed deadline; no band → no targets
- [ ] `dotnet test backend/tests/Contigo.Quotes.Tests` exit 0 unchanged
- [ ] `dotnet build backend/Contigo.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | criticality, levers, pack builder, endpoint composition | `Contigo.Insights.Tests/*` |
| unit | quotes unchanged | `Contigo.Quotes.Tests/*` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F07/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-07-insights/us-01-insights/tasks/task-01-insights-calculators.md
  produces: [insights-calculators]
  depends_on: [v2-scaffold]
  effort: L
  layer: backend
  status: live
```
