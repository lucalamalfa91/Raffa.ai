---
id: us-02
type: user-story
parent: feature-04
wave: w18
status: active
---

# us-02-quote-benchmark-web — Quote check renders the benchmark, not a worksheet

## Story
As a **procurement user**, I want Quote check to show the market benchmark
position (and my history), so I can assess a proposal in minutes instead of
filling a manual worksheet.

## Acceptance criteria
- [ ] AC-1 the assessment result shows the market-benchmark position with an honest cold-start copy, never a fabricated number.
- [ ] AC-2 prior quote history is visible from server state.
- [ ] AC-3 the "See it in Savings →" CTA never replaces the benchmark result.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024/ADR-028
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (quote-benchmark-backend) | the screen renders the benchmark the backend now returns |

## Architecture decisions in force
- ADR-024 (quote flow), ADR-028 (history is server state).

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | quote-benchmark-web | M | phase-4 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Benchmark-first UX; history from server state; "See it in Savings →" is secondary, never replacing the benchmark.

## Open questions
- none
