---
id: E27/F02/US01/T01
type: task
story: us-01-engine-scope
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-engine-scope — Thread `scopeContractId` into `AskAsync` (NW-76)

## Coding objective
Extend `AskCopilotService.AskAsync` (`backend/src/Raffa.Api/AskCopilotService.cs:125`,
currently `(tenantId, question, recentTurns, userId, cancellationToken)`) with an
optional `scopeContractId` parameter. In the call site
`ConversationsEndpointExtensions.AskAndAppendAsync` (~`:295`) load
`conversation.ScopeContractId` and pass it. In `BuildInDomainReplyAsync`, when a
scope id is present and visible to the caller: (a) resolve deixis
(`this supplier`/`this contract`/"questo contratto") to that id rather than
`ExtractSupplierCandidate`; (b) make a scoped id win over a same-name portfolio
`FirstOrDefault`; (c) set `RoutingContext.ContractId` to that id for follow-ups;
(d) an unseen id → 404 / N11 refusal without assembling a pack. **Exception
(lock 4):** a `PortfolioMarketPosition` question still ranks the workspace
portfolio even when scoped. No wire change (`scopeContractId` is already on
`CreateConversationRequest`).

## Parent story AC covered
- AC-1 `AskAsync` uses the scope id; deixis resolves to it.
- AC-2 scoped id wins; `RoutingContext.ContractId` = that id.
- AC-3 unseen id → 404/N11.
- AC-4 portfolio-market question stays portfolio-wide (lock 4).

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | add `scopeContractId` to `AskAsync`; resolve deixis/scoped id |
| backend/src/Raffa.Api/ConversationsEndpointExtensions.cs | pass `conversation.ScopeContractId` into `AskAsync` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 12) + lock 4.
- **Do not touch**: `Raffa.Chat` allow-list (scope already a domain field); `CreateConversationRequest`.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test that a scoped `AskAsync` resolves deixis to the scope id, a scoped id wins over a same-name hit, and an unseen id returns N11

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | scoped deixis + scoped-id-wins + unseen-id refusal | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E27/F02/US01/T01
  prompt: reports/workitems/epic-27-ask-bind-contract-and-intent/feature-02-engine-scope/us-01-engine-scope/tasks/task-01-engine-scope.md
  produces: [engine-scope]
  depends_on: [planner-lexicon]
  effort: M
  layer: backend
  status: live
```
