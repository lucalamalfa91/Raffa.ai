---
id: us-01
type: user-story
parent: feature-01
wave: w18
status: active
---

# us-01-openapi-sweep — The hand-authored OpenAPI/client carry no stale gap prose

## Story
As the **API consumer**, I want the hand-authored OpenAPI and client to carry
no stale "no audit / no requestBody" claims, so the contract does not lie about
what is closed.

## Acceptance criteria
- [ ] AC-1 `web/openapi/raffa-api.v1.json` no longer claims `GET /api/audit` is absent (it is CLOSED-ON-MAIN) and records the conversations requestBody as a documented generator limitation.
- [ ] AC-2 `client.ts` prose is swept of the same stale claims; hand-written bodies are confirmed, not re-wrapped.
- [ ] AC-3 no new wrapper or contract shape change (a sweep, not a feature).

## Definition of done
- [ ] every AC above is verified by at least one test/command named in a task
- [ ] honours ADR-012 / ADR-026
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | both halves are already closed on main; only the prose is stale |

## Architecture decisions in force
- ADR-012 / ADR-026 — the requestBody limitation is documented, never re-implemented.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | openapi-sweep | S | phase-4 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Sweep, not a decision: ADR-012 already decided the shape; seats record `none`.

## Open questions
- none
