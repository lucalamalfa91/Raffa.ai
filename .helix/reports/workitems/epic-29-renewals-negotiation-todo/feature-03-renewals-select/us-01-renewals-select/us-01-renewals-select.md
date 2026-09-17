---
id: us-01
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-01-renewals-select — `/renewals?select=` opens the right row

## Story
As a **user** following a chat deep-link, I want `/renewals?select={id}` to
select that row (and its TODO pane), so the strategy points land on the right
contract.

## Acceptance criteria
- [ ] AC-1 `useSearchParams().get("select")` selects the matching listed id and shows its insight pane.
- [ ] AC-2 an invalid/other-tenant id falls back to the default top row, never a 500.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-012 (cl. 51)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `GET /api/renewals` already returns `contractId` per row; only the query read is missing |

## Architecture decisions in force
- ADR-012 (cl. 51) — read `?select=`, fall back on invalid.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | renewals-select | S | phase-2 |

## Council decisions carried into this story
- `?select=` selects; invalid falls back to the top-priority row, no leak, no 500.

## Open questions
- none
