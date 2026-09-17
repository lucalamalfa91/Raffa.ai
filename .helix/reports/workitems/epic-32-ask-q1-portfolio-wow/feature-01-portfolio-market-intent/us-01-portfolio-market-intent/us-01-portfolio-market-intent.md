---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-portfolio-market-intent — Q1 is answered in Ask, never routed to Quote check

## Story
As the **Ask engine**, I want Q1 answered in Ask (not Quote check), so
portfolio mal-position is a savings answer, not a routing dead-end.

## Acceptance criteria
- [ ] AC-1 the screenshot Q1 → `PortfolioMarketPosition` answered in Ask (also when scoped).
- [ ] AC-2 single-supplier "is X above market?" stays `MarketCompare`.
- [ ] AC-3 empty-above-market is an honest sentence + Portfolio action; empty portfolio → upload invite.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 13/19) + lock 6
- [ ] queued — not in the w19 wave file (overflow, head of W20)

## Dependencies
| Depends on | Why |
|------------|-----|
| epic-27 (planner lexicon) | the intent routes first |

## Architecture decisions in force
- ADR-024 w19 (cl. 13/19) — R-SYS-02 narrowed (lock 6).

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | portfolio-market-intent | M | queued |

## Council decisions carried into this story
- Q1 answered in Ask; Quote check = new market proposal only.

## Open questions
- none
