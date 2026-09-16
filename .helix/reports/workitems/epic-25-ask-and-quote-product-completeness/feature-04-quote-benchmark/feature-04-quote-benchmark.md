---
id: feature-04
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-04-quote-benchmark — Quote check: market benchmark + history (NW-57)

## Slice
Quote check behaves as a market-benchmark job with a first-of-type cold start
and a history, not a manual savings worksheet. The backend exposes benchmark
history + a benchmark-first shape (fixture-adapter fenced per ADR-001); the web
renders the benchmark-first result and keeps every request (no "send to Home").

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | quote-benchmark-backend | w18 |
| us-02 | quote-benchmark-web | w18 |

## Architecture decisions in force
- ADR-024 (quote flow), ADR-028 (history is server state), ADR-001 (fixture fence, no paid API).

## Target repo
`raffa-backend` + `raffa-web` (mixed)
