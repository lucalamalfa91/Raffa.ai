---
id: E11/F07/US01/T01
type: task
story: us-01-review-screen
wave: 11
status: live
target_repo: raffa-web
---

# task-01-review-mockup — Review screen vs day1-demo.html

## Coding objective

Extract Review / correction from `inputs/design/prototypes/day1-demo.html`
(screens.md §6). Copy header, progress line, 4-column list, and 340–400px
evidence pane into `web/src/routes/contracts/review/`. Do not change
decision/API behaviour.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/contracts/review/review.css` | match export |
| `web/src/routes/contracts/review/*.tsx` | markup only if missing vs export |
| `web/tests/routes/contracts/review/` | list + evidence pane layout |

## Context the implementer needs
- Gap G-REV. ADR-019. **Do not touch** other route CSS.

## Definition of done
- [ ] `npm test` in `web/` — Review tests pass.
- [ ] Evidence pane uses `--color-surface` and a 2px left rule (design-system.md).

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | pane + list | `web/tests/routes/contracts/review/` |

## Open questions blocking this task
- none
