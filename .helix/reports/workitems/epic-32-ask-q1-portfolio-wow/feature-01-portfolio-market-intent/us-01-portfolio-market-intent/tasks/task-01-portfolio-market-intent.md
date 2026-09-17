---
id: E32/F01/US01/T01
type: task
story: us-01-portfolio-market-intent
wave: w19
status: queued
target_repo: raffa-backend
---

# task-01-portfolio-market-intent — Q1 answered in Ask, not Quote check (NW-86)

## Coding objective
In `AskCopilotService.BuildInDomainReplyAsync`, remove the QuoteRoute
short-circuit for the `PortfolioMarketPosition` sentence (the "That's a job for
Quote check" reply); answer Q1 in Ask (also when scoped — lock 4). Empty above-
market (data existed) → honest sentence + Portfolio action; empty portfolio →
upload invite (R-ASK-10). Keep single-supplier "is X above market?" on
`MarketCompare`. Queued (head of W20).

## Parent story AC covered
- AC-1..AC-3.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | remove QuoteRoute short-circuit for Q1 |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 13/19); lock 6.
- **Do not touch**: the planner (epic-27); `CapabilityCatalog` QuoteCheck (still new-proposal).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0 (Q1 is `answer`, not QuoteRoute)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | Q1 answered in Ask | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none
