---
id: E11/F06/US01/T01
type: task
story: us-01-contract360-screen
wave: 11
status: live
target_repo: contigo-web
---

# task-01-contract360-mockup — Contract 360 vs day1-demo.html

## Coding objective

Extract Contract 360 from `inputs/design/prototypes/day1-demo.html`
(screens.md §5). Copy header, 6-cell fact row, tab bar, recommended-action
block, and fact tables into `web/src/routes/contracts/contract360/`. Do not
change which API fields render. Do not invent tabs the export does not have
if the app already lists the locked ADR-020 set — keep the ADR-020 tabs and
style them like the export.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/contracts/contract360/contract360.css` | match export |
| `web/src/routes/contracts/contract360/*.tsx` | markup only if missing vs export |
| `web/tests/routes/contracts/contract360/` | fact row + recommended-action present |

## Context the implementer needs
- Gap G-360. ADR-019/020. **Do not touch** other route CSS.

## Definition of done
- [ ] `npm test` in `web/` — Contract 360 tests pass.
- [ ] Recommended-action is a distinct block (`.card` or export equivalent).

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | header / fact row / recommendation | `web/tests/routes/contracts/contract360/` |

## Open questions blocking this task
- none
