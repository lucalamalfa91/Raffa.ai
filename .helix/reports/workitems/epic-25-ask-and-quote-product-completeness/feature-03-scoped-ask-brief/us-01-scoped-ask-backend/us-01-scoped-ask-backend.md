---
id: us-01
type: user-story
parent: feature-03
wave: w18
status: active
---

# us-01-scoped-ask-backend — The Ask engine resolves the scoped contract before the gate

## Story
As the **Ask engine**, I want the persisted conversation `scopeContractId` to
scope the planner and the gate, so that a "Ask about it" turn is about that
contract, not a generic gate.

## Acceptance criteria
- [ ] AC-1 `AskAsync` threads the conversation's `ScopeContractId` into the planner (a named/known scope id scopes the turn to that contract).
- [ ] AC-2 the gate resolves the scope before the R-ASK-10 check, so a scoped entry can answer even when the tenant has ≥1 validated contract.
- [ ] AC-3 the scoped turn's citations/pack are scoped to that contract's supplier.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024 (one engine, scoped entry)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `Conversation.ScopeContractId` is already persisted (`Conversation.cs:50`) and resolved by `ConversationService` |

## Architecture decisions in force
- ADR-024 — the gate resolves the scope id before the R-ASK-10 check; one engine, scoped entry.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | scoped-ask-backend | M | phase-2 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Gate resolves the scope id before the R-ASK-10 check; one engine; the scoped turn briefs the named contract's supplier.

## Open questions
- none
