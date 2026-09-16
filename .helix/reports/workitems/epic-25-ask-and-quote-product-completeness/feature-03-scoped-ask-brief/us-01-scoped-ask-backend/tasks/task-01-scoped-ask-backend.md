---
id: E25/F03/US01/T01
type: task
story: us-01-scoped-ask-backend
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-scoped-ask-backend — Thread the conversation scope into the Ask planner/gate

## Coding objective
Thread the conversation's persisted `ScopeContractId` into the Ask engine so the
scoped entry is about that contract, not a generic gate. In
`backend/src/Raffa.Api/AskCopilotService.cs` add an optional `scopeContractId`
parameter to `AskAsync`; pass it to `IntentPlanner.Plan` (via the existing
`namedSupplier`/scope path) and to `DomainGate` resolution so the R-ASK-10 gate
resolves the scope id **before** deciding, and the scoped turn's pack
(`BuildClausePackAsync` / `BuildStructuredFactPackAsync` / `BuildMarketComparePackAsync`
and siblings) scopes to that contract's supplier. Read the scope from the
conversation at the call site (`ConversationEndpointExtensions` /
`ChatEndpointExtensions` pass `Conversation.ScopeContractId`, already persisted
at `backend/src/Raffa.Chat/Domain/Conversations/Conversation.cs:50` and resolved
by `ConversationService.GetAsync`). No wire change: `scopeContractId` is already
on `CreateConversationRequest`.

## Parent story AC covered
- AC-1 `AskAsync` threads `ScopeContractId` into the planner.
- AC-2 the gate resolves scope before the R-ASK-10 check.
- AC-3 the scoped turn's citations are scoped to that supplier.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | add `scopeContractId` to `AskAsync`; scope the planner/pack |
| backend/src/Raffa.Api/ConversationsEndpointExtensions.cs | pass the conversation's `ScopeContractId` into `AskAsync` |

## Context the implementer needs

**Closes: NW-56**

- **Architecture decisions in force**: ADR-024 (one engine, scoped entry; gate resolves scope before R-ASK-10); ADR-002 (composition stays in `Raffa.Api`).
- **Do not touch**: `Raffa.Chat` module's allow-list (the scope is already a domain field); the web (phase 3).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test proving a scoped `AskAsync` call scopes the pack to the named supplier and a scoped-entry turn does not hit the generic gate

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | a scoped `AskAsync` scopes the pack/gate to the contract's supplier | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F03/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-03-scoped-ask-brief/us-01-scoped-ask-backend/tasks/task-01-scoped-ask-backend.md
  produces: [scoped-ask-planner]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
