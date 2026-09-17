---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-engine-scope — The engine resolves deixis to the scoped contract

## Story
As the **Ask engine**, I want the conversation's `scopeContractId` threaded
into `AskAsync` so "this supplier / questo contratto" resolves to that id and
an unseen id refuses without leaking the pack.

## Acceptance criteria
- [ ] AC-1 `AskAsync` accepts and uses `Conversation.ScopeContractId`; deixis resolves to it.
- [ ] AC-2 a scoped id wins over a same-supplier portfolio hit; `RoutingContext.ContractId` = that id.
- [ ] AC-3 an unseen id returns 404 / N11 refusal, no pack leak.
- [ ] AC-4 a `PortfolioMarketPosition` question still ranks the workspace portfolio even when scoped (lock 4).

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 12); lock 4
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `Conversation.ScopeContractId` is already persisted and resolved; the call site drops it today |

## Architecture decisions in force
- ADR-024 w19 (cl. 12) — one engine, scoped entry; lock 4 exception.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | engine-scope | M | phase-1 |

## Council decisions carried into this story
- Scope is consumed in the engine, not re-derived from the URL on resume; lock 4 keeps Q1 portfolio-wide.

## Open questions
- none
