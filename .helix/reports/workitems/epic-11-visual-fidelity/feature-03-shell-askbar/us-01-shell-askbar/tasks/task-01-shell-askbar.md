---
id: E11/F03/US01/T01
type: task
story: us-01-shell-askbar
wave: 11
status: live
target_repo: raffa-web
---

# task-01-shell-askbar — Shell + Ask bar vs day1-demo.html

## Coding objective

Search `inputs/design/prototypes/day1-demo.html` for the 224px rail and the
`showAskBar` block. Copy padding, type, badge, and Ask-bar strip values into
`web/src/components/shell/shell.css` and `web/src/components/ask-bar/ask-bar.css`.
Do not ship the prototype-only dark stepper.

Known already-correct structure: `.shell-layout { grid-template-columns: 224px 1fr }`.
Verify item padding (9px 16px), kicker 11px uppercase, workspace name 15px/800,
Ask bar `padding: 12px 32px`, mark 14×14 accent, input 16px heading-weight.

Markup changes only if the export has nodes this app lacks (e.g. Ask mark
size). Do not rebuild nav items or routes.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/components/shell/shell.css` | match export |
| `web/src/components/ask-bar/ask-bar.css` | match export |
| `web/src/components/shell/AppShell.tsx` | only if markup is missing vs export |
| `web/src/components/ask-bar/GlobalAskBar.tsx` | only if markup is missing vs export |
| `web/tests/components/shell/` | 224px grid; Ask mark present |

## Context the implementer needs
- Gaps G-SHELL, G-ASKBAR. ADR-019. design-system.md "App-level patterns".
- **Do not touch**: sign-in, `App.tsx`, route CSS for documents/portfolio/360/review.

## Definition of done
- [ ] `npm test` in `web/` — shell tests still pass; assert 224px track and Ask mark.
- [ ] No dark stepper strip in the product shell.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | rail + Ask bar chrome | `web/tests/components/shell/` |

## Open questions blocking this task
- none
