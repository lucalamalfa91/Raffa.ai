---
id: us-01
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-01-bar-scope — The 360 bar's chips and typed query are scoped to the open contract

## Story
As a **user** on Contract 360, I want the notice chip (and a typed notice
query) to open a conversation bound to that contract, so Q2 does not abstain
"which supplier".

## Acceptance criteria
- [ ] AC-1 from `/contracts/:contractId`, the bar navigates `/ask?scope=<id>` (query string), reusing w18 parse.
- [ ] AC-2 the notice chip uses the open contract's real supplier name (no hard-coded "this supplier" when a contract is open).
- [ ] AC-3 Portfolio / Ask-home chips stay unscoped.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-012 (cl. 49) / ADR-020 (37.1)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (engine-scope) | the engine must consume the id the bar now passes |

## Architecture decisions in force
- ADR-012 (cl. 49) — `?scope=` reuse, no new nav-state field.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | bar-scope | S | phase-2 |

## Council decisions carried into this story
- bar submits `?scope=<id>`; the visible binding is NW-78's chip, not new origin chrome.

## Open questions
- none
