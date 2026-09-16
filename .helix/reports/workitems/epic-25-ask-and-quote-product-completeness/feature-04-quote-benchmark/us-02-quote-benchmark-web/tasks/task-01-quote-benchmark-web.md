---
id: E25/F04/US02/T01
type: task
story: us-02-quote-benchmark-web
wave: w18
status: live
target_repo: raffa-web
---

# task-01-quote-benchmark-web — Quote check renders the benchmark + history

## Coding objective
In `web/src/routes/quotes/` (index + assessment/target steps), render the
market-benchmark position from the assessment (`recalculateQuoteAssessment`
response) with the honest cold-start copy for `insufficient_benchmark_data`;
show prior quote history read back from server state (the new
`getQuoteBenchmarkHistory`); and keep the "See it in Savings →" CTA secondary —
it never replaces the benchmark result. Cite the design oracle
`inputs/design/prototypes/raffa-v2/screens-v2.md` §10 and anchor the benchmark
result in the Assessment step.

## Parent story AC covered
- AC-1 benchmark position + honest cold-start copy.
- AC-2 prior history visible from server state.
- AC-3 "See it in Savings →" never replaces the benchmark.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/quotes/assessment/AssessmentResult.tsx | benchmark-first result + cold-start copy |
| web/src/routes/quotes/history/QuoteHistoryList.tsx | new: durable history list |
| web/src/routes/quotes/index.tsx | load + render history; keep CTA secondary |

## Context the implementer needs

**Closes: NW-57**

- **Architecture decisions in force**: ADR-024 (quote flow), ADR-028 (history is server state).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §10.
- **Do not touch**: `client.ts` (wrapper from phase 2); the Savings screen.

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving the benchmark result renders and a cold start shows honest copy, never a fabricated number

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | benchmark renders; cold start honest | `web/src/routes/quotes/assessment/*.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F04/US02/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-04-quote-benchmark/us-02-quote-benchmark-web/tasks/task-01-quote-benchmark-web.md
  produces: [quote-benchmark-ux]
  depends_on: [quote-benchmark-history]
  effort: M
  layer: frontend
  status: live
```
