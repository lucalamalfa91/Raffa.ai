---
id: us-01
type: user-story
parent: feature-04
wave: w19
status: active
---

# us-01-todo-web — The Renewals pane shows and ticks the negotiation TODOs

## Story
As a **user** on Renewals, I want to see the negotiation TODOs Ask wrote and
tick them done, so the strategy points are actionable.

## Acceptance criteria
- [ ] AC-1 the insight pane renders the read-back TODO list from `GET /api/renewals/{id}/negotiation-todos`.
- [ ] AC-2 a Mark-done tick PUTs the todo (Procurement/Admin); Open/Done tags render.
- [ ] AC-3 the client never invents a point; Done ticks survive a repeat ask.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-012 (cl. 52) / ADR-020 (41)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-01 (todo-entity-api) + feature-03 (renewals-select) | read-back + tick endpoint, and the row selection |

## Architecture decisions in force
- ADR-012 (cl. 52) / ADR-020 (41) — `.table` sub-surface, Mark-done, Open/Done tags.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | todo-web | M | phase-4 |

## Council decisions carried into this story
- read-back + tick; never invent a point; Done preserved on re-ask (server).

## Open questions
- none
