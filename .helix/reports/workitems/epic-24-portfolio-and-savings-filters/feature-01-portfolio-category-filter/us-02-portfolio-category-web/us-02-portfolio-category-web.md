---
id: us-02
type: user-story
parent: feature-01
wave: w18
status: active
---

# us-02-portfolio-category-web — Portfolio category filter control

## Story
As an **operator**, I want a category filter control on the Portfolio screen so
that I can filter the list without touching the data.

## Acceptance criteria
- [ ] AC-1 the Portfolio screen shows a category filter control that issues `?category=` on `GET /api/contracts`.
- [ ] AC-2 the filter reads back from the server (query parameter), never a client store.
- [ ] AC-3 clearing the filter re-loads the full portfolio.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-020 (control, never a store)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (portfolio-category-backend) | the control issues the query the backend now accepts |

## Architecture decisions in force
- ADR-020 — filter as a query parameter, never a client store.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | portfolio-category-web | S | phase-2 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- The filter vocabulary is read from the real supplier categories, never a hard-coded enum.

## Open questions
- none
