---
id: E31/F01/US01/T01
type: task
story: us-01-q3-route
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-q3-route — Q3 plans `RenewalStrategy` for the named supplier (NW-95)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs`
(`BuildInDomainReplyAsync`/`BuildRenewalStrategyPackAsync`), make the named-
supplier `contrattare su X`/`rinnovo` question (epic-27 lexicon) build a
`RenewalStrategy` pack for the resolved AsterCloud contract: corpora `calc`
(strategy/ranked points) + `tenant` (clause evidence via the epic-28 scoped
RAG) + `market` (benchmarked priced-lines via epic-28) + one `raffa` Renewals
item. A missing deadline becomes a **named point** ("end date unknown"), not an
abstain; the reply is `kind=answer` ranked.

## Parent story AC covered
- AC-1 Q3 → `RenewalStrategy` named.
- AC-2 calc+tenant+market+raffa corpora.
- AC-3 missing deadline = named point; `kind=answer`.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | Q3 `RenewalStrategy` pack assembly |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 23).
- **Do not touch**: `Raffa.Insights` allow-list; the ranker (feature-02, same phase? no — ordered).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a golden Q3 producing `kind=answer` ranked points, not `Abstain`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | Q3 → ranked `answer` | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E31/F01/US01/T01
  prompt: reports/workitems/epic-31-ask-q3-strategy/feature-01-q3-route/us-01-q3-route/tasks/task-01-q3-route.md
  produces: [q3-route]
  depends_on: [supplier-resolution, priced-lines-parity, rag-contract-filter]
  effort: M
  layer: backend
  status: live
```
