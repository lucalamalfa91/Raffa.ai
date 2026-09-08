---
id: E11/F05/US01/T01
type: task
story: us-01-portfolio-screen
wave: 11
status: live
target_repo: contigo-web
---

# task-01-portfolio-mockup — Portfolio screen vs day1-demo.html

## Coding objective

Extract the Portfolio screen from `inputs/design/prototypes/day1-demo.html`
(screens.md §4). Copy attention-strip, filter chips, table header type, and
`.row-critical` treatments into `web/src/routes/contracts/`. Do not invent
a card layout. Do not change API mapping.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/contracts/contracts.css` | match export |
| `web/src/routes/contracts/*.tsx` | markup only if missing vs export |
| `web/tests/routes/contracts/` | attention strip + critical row class |

## Context the implementer needs
- Gap G-PORT. ADR-019 semantic mapping. **Do not touch** other route CSS.

## Definition of done
- [ ] `npm test` in `web/` — Portfolio tests pass.
- [ ] Attention strip and critical-row rules exist and match export values.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | strip + row tint | `web/tests/routes/contracts/` |

## Open questions blocking this task
- none
