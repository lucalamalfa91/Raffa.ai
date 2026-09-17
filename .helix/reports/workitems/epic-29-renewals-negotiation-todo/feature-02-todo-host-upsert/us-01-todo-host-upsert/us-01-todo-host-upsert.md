---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-todo-host-upsert — Ask upserts ranked TODOs before answering

## Story
As the **Ask engine**, I want the host to upsert all ranked points before the
answer call, so the Renewals deep-link is true and a repeat ask is idempotent.

## Acceptance criteria
- [ ] AC-1 the host upserts all ranked points after rank and before the answer call (`persistTodos=true`).
- [ ] AC-2 re-ask adds no duplicate Open and preserves Done.
- [ ] AC-3 locked-for-2026 rows never receive a 2026-save TODO.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-028/ADR-024 w19 (cl. 21)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-01 (todo-entity-api) | the upsert writes the entity; ranker (epic-31) supplies points |

## Architecture decisions in force
- ADR-028/ADR-024 w19 (cl. 21) — host upsert before answer; persist-all.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | todo-host-upsert | M | phase-4 |

## Council decisions carried into this story
- upsert after rank before answer; idempotent by point_key; locked rows get no 2026-save TODO.

## Open questions
- none
