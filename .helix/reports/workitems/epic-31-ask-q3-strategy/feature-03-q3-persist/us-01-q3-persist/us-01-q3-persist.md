---
id: us-01
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-01-q3-persist — Q3 writes all TODOs and deep-links to Renewals

## Story
As a **user**, I want the Q3 answer to persist all ranked points and offer a
Renewals deep-link, so clicking it shows the full list; a repeat ask adds no
duplicate.

## Acceptance criteria
- [ ] AC-1 after rank, upsert all ranked todos (`persistTodos=true`), not capped at 3.
- [ ] AC-2 the `answer` carries top-3 markdown + citations + a server-injected `/renewals?select={guid}` action.
- [ ] AC-3 re-ask: no duplicate Open, Done preserved (server idempotency).

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 21) / ADR-028
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (point-ranker) + epic-29 todo (entity+host upsert+select) | rank → upsert → deep-link |

## Architecture decisions in force
- ADR-024 w19 (cl. 21) / ADR-028 — persist-all + server-injected action.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | q3-persist | M | phase-4 |

## Council decisions carried into this story
- top-3 chat, persist-all uncapped; server-injected action, never model `actionKeys`; honesty line "N more points saved in Renewals."

## Open questions
- none
