---
id: E29/F04/US01/T01
type: task
story: us-01-todo-web
wave: w19
status: live
target_repo: raffa-web
---

# task-01-todo-web — Renewals pane renders + ticks the negotiation TODOs (NW-85)

## Coding objective
In `web/src/routes/renewals/InsightCard.tsx` (or a new
`NegotiationTodoList.tsx`), render the read-back negotiation TODO list from
`GET /api/renewals/{id}/negotiation-todos` as a `.table` sub-surface with
topic / current → target / why, Open/Done tags, and a Mark-done
`.btn-secondary` that PUTs the tick. Add the `getRenewalNegotiationTodos` + tick
wrappers to `web/src/api/client.ts` (single-writer: feature-01 documented the
routes; this task is the client writer in its phase). Never invent a point; Done
ticks survive a repeat ask (server idempotency). Cite `screens-v2.md` §7/§8 as
anchor.

## Parent story AC covered
- AC-1 read-back TODO list rendered.
- AC-2 Mark-done PUT tick + Open/Done tags.
- AC-3 never invent a point; Done survives re-ask.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/renewals/NegotiationTodoList.tsx | new: read-back list + tick |
| web/src/routes/renewals/InsightCard.tsx | embed the TODO list |
| web/src/api/client.ts | `getRenewalNegotiationTodos` + tick wrappers |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012 (cl. 52) / ADR-020 (41).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §7/§8.
- **Do not touch**: the entity/API (feature-01); `?select=` (feature-03).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test that the TODO list renders and a Mark-done tick PUTs

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | TODO list render + tick | `web/src/routes/renewals/NegotiationTodoList.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E29/F04/US01/T01
  prompt: reports/workitems/epic-29-renewals-negotiation-todo/feature-04-todo-web/us-01-todo-web/tasks/task-01-todo-web.md
  produces: [todo-web]
  depends_on: [negotiation-todo-api]
  effort: M
  layer: frontend
  status: live
```
