---
id: feature-01
type: feature
parent: epic-32
wave: w19
status: active
---

# feature-01-portfolio-market-intent — Unscoped portfolio market → Ask, not Quote check (NW-86)

## Slice
The planner's `PortfolioMarketPosition` intent (epic-27) removes the Q1
QuoteRoute short-circuit; single-supplier "is X above market?" stays
`MarketCompare`; empty-above-market is an honest sentence + Portfolio action.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | portfolio-market-intent | w20 |

## Architecture decisions in force
- ADR-024 w19 (cl. 13/19) + lock 6 (R-SYS-02 narrowed).

## Target repo
`raffa-backend`
