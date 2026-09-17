---
id: feature-01
type: feature
parent: epic-27
wave: w19
status: active
---

# feature-01-intent-lexicon — Planner lexicon covers the demo phrasings (NW-79)

## Slice
`IntentPlanner` and `AskIntent` gain the `PortfolioMarketPosition` intent, the
`risparm*`-before-`mercato` reorder, notice/preavviso/disdetta structured
routing, and `contrattare`/`rinnovo` hitting `RenewalStrategyPattern`, locked
by a planner test table (IT+EN) for the three screenshot sentences.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | intent-lexicon | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 13) — the planner table covers the three screenshot sentences; no phrasing falls to unscoped Clause RAG when facts exist.

## Target repo
`raffa-backend`
