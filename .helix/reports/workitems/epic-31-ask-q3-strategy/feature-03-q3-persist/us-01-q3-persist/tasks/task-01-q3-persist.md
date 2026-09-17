---
id: E31/F03/US01/T01
type: task
story: us-01-q3-persist
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-q3-persist — Q3 persist-all + server-injected Renewals deep-link (NW-97)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs`, complete the Q3 answer: after
the ranker, upsert **all** ranked points (`persistTodos=true`, uncapped — via the
epic-29 host-upsert path) **before** the answer call; build the `answer` markdown
as the **top 3** points (topic · current → target · why) + citations (tenant
pages, calc points, market notes, a raffa Renewals card), and inject a
**server-side** `Navigate` action `/renewals?select={guid}` via
`CapabilityRouting.BuildHref` (never model `actionKeys`). A repeat ask adds no
duplicate Open and preserves Done (server idempotency). The web renders the
injected action verbatim (no new work beyond the existing `ActionRow`).

## Parent story AC covered
- AC-1 persist-all.
- AC-2 top-3 + citations + injected action.
- AC-3 re-ask idempotent.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | Q3 final answer: rank → upsert → top-3 + injected action |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 21) / ADR-028.
- **Do not touch**: `CopilotReplyBuilder` (answer path keeps actions); the TODO entity (epic-29).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0: Q3 answer has ≤3 points + injected `/renewals?select=` action; GET todos returns the full list; re-ask idempotent

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | top-3 + injected action + idempotent re-ask | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E31/F03/US01/T01
  prompt: reports/workitems/epic-31-ask-q3-strategy/feature-03-q3-persist/us-01-q3-persist/tasks/task-01-q3-persist.md
  produces: [q3-persist]
  depends_on: [point-ranker, todo-host-upsert, citation-ids]
  effort: M
  layer: backend
  status: live
```
