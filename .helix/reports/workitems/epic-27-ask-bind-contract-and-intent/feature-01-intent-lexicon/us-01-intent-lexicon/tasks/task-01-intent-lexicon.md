---
id: E27/F01/US01/T01
type: task
story: us-01-intent-lexicon
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-intent-lexicon — Planner lexicon for the three demo phrasings (NW-79)

## Coding objective
In `backend/src/Raffa.Chat/Domain/AskIntent.cs` add `AskIntent.PortfolioMarketPosition`
(the unscoped/scoped "which of MY contracts are poorly positioned / where can I
save in 2026" intent, answered in Ask — ADR-024 w19 cl. 13 / lock 6). In
`backend/src/Raffa.Chat/Application/Planning/IntentPlanner.cs`: (a) check a
portfolio-market pattern (`mal posizionat*`/`poorly positioned`/`above market`
/`too expensive` + `quali contratti`/`which contracts` + optional
`risparm*`/`2026`) **before** `BenchmarkPattern` → route to `PortfolioMarketPosition`
(even when scoped — lock 4); (b) move the `SavingsPattern` `risparm*` check ahead
of `mercato`/`BenchmarkPattern` so `risparm*` reaches portfolio; (c) add a notice
pattern (`notice|preavviso|disdetta|disdetta period|cancellation deadline`) so
notice plans structured (NW-91) instead of `Clause`; (d) widen
`RenewalStrategyPattern` with `contrattare|contratta\w*|rinnovo|rinnov\w*|punti`
so Q3 hits `RenewalStrategy`. Keep the pure/synchronous no-LLM contract
(Appendix C rule 6).

## Parent story AC covered
- AC-1 Q1 → `PortfolioMarketPosition`.
- AC-2 Q2 notice → structured.
- AC-3 Q3 → `RenewalStrategy`.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Chat/Domain/AskIntent.cs | add `PortfolioMarketPosition` |
| backend/src/Raffa.Chat/Application/Planning/IntentPlanner.cs | reorder + add portfolio-market/notice/renewal-strategy patterns |
| backend/tests/Raffa.Chat.Tests/IntentPlannerTests.cs | rows for the three screenshot sentences (IT+EN) |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 13); lock 4/6.
- **Do not touch**: the `answer` role (still no tools); `AskCopilotService` routing (target intents land in later tasks).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests` exits 0, with `IntentPlannerTests` rows asserting the three sentences' intents and that none falls to unscoped `Clause`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | three screenshot sentences route correctly | `backend/tests/Raffa.Chat.Tests/IntentPlannerTests.cs` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E27/F01/US01/T01
  prompt: reports/workitems/epic-27-ask-bind-contract-and-intent/feature-01-intent-lexicon/us-01-intent-lexicon/tasks/task-01-intent-lexicon.md
  produces: [planner-lexicon]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
