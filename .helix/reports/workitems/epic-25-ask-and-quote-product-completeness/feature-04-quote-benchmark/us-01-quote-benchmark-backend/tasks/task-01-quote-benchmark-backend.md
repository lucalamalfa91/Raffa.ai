---
id: E25/F04/US01/T01
type: task
story: us-01-quote-benchmark-backend
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-quote-benchmark-backend — Quote check: market benchmark + durable history

## Coding objective
Re-focus Quote check's assessment on the market-benchmark job (not a worksheet)
and make its history durable server state. In `Raffa.Quotes` + `Raffa.Api`
(`QuotesEndpointExtensions.cs`, `backend/src/Raffa.Api/QuotesEndpointExtensions.cs`),
ensure the assessment returns a market-benchmark position with an honest
`insufficient_benchmark_data` first-of-type cold start (fixture-adapter fenced
— never a paid external API, ADR-001 §1.2), and expose a read-back of prior
quote benchmarks so history is server state (ADR-028), never a client store.
Keep every request (no "send to Home" that drops history): the existing
"See it in Savings →" CTA stays but never replaces the benchmark result.
Declare any new read in `web/openapi/raffa-api.v1.json` and regenerate
`web/src/api/generated/schema.ts` + the hand-written `web/src/api/client.ts`
wrapper (single-writer: this phase's client wrapper writer).

## Parent story AC covered
- AC-1 benchmark position with honest cold start, never fabricated.
- AC-2 quote history read back from server state.
- AC-3 every request kept.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Quotes/Application/QuoteBenchmarkHistoryService.cs | new: durable benchmark history read |
| backend/src/Raffa.Api/QuotesEndpointExtensions.cs | expose benchmark-first assessment + history read |
| web/openapi/raffa-api.v1.json | document the history read |
| web/src/api/generated/schema.ts | regenerate |
| web/src/api/client.ts | `getQuoteBenchmarkHistory` wrapper |

## Context the implementer needs

**Closes: NW-57**

- **Architecture decisions in force**: ADR-024 (quote flow), ADR-028 (history is server state), ADR-001 (fixture-adapter fence, no paid API).
- **Do not touch**: the Savings KPI (realized money is a later item); `client.ts` beyond the new wrapper.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test proving a cold start returns `insufficient_benchmark_data` and a prior quote's benchmark reads back

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | cold start → honest insufficient_data; history reads back | `backend/tests/Raffa.Quotes.Tests` / `Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F04/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-04-quote-benchmark/us-01-quote-benchmark-backend/tasks/task-01-quote-benchmark-backend.md
  produces: [quote-benchmark-history]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
