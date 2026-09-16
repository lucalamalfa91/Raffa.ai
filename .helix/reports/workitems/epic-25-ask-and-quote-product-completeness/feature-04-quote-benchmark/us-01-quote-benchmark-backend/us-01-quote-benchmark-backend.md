---
id: us-01
type: user-story
parent: feature-04
wave: w18
status: active
---

# us-01-quote-benchmark-backend — Quote check returns a market benchmark with history

## Story
As the **Quote check service**, I want a quote's assessment to be a market
benchmark (with a first-of-type cold start) and a persisted history the client
can read back, so the job-to-be-done is a comparison, not a worksheet.

## Acceptance criteria
- [ ] AC-1 the assessment returns a market-benchmark position (fixture-adapter fenced) with an honest `insufficient_benchmark_data` cold start, never a fabricated number.
- [ ] AC-2 quote history is read back from server state (a prior quote's benchmark is durable).
- [ ] AC-3 every request is kept (no "send to Home" that drops history).

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024/ADR-028/ADR-001 (fixture fence)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | the quotes module/endpoint already exists (`QuotesEndpointExtensions.cs`, `Raffa.Quotes`) |

## Architecture decisions in force
- ADR-024 (quote flow), ADR-028 (history is server state), ADR-001 (fixture fence, no paid API).

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | quote-benchmark-backend | M | phase-2 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Job-to-be-done = market benchmark (not worksheet); history is server state; first-of-type cold start is an honest `insufficient_benchmark_data`.

## Open questions
- none
