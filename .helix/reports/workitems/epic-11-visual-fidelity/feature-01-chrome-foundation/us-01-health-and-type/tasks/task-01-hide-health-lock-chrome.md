---
id: E11/F01/US01/T01
type: task
story: us-01-health-and-type
wave: 11
status: live
target_repo: contigo-web
---

# task-01-hide-health-lock-chrome — Hide the health probe; lock shared chrome

## Coding objective

The compiled prototype (`inputs/design/prototypes/day1-demo.html`) has no
`API: reachable` line. `web/src/App.tsx` still renders `{healthStatus}`
above SignInRoute and WorkspaceShellApp. Keep the `/health` fetch (E01 DoD)
but take it off the canvas: add `.visually-hidden` in `web/src/styles/base.css`
(clip/absolute, not `display:none` so the test id stays in the a11y tree if
tests query by test id). Put that class on the health `<p>`.

Align `.btn` padding with the prototype sign-in CTA:
`padding: 12px 14px` (inline on the export's `.btn.btn-primary.btn-block`).
Do that in `web/src/styles/components.css` only — do not touch signin.css.

## Parent story AC covered
- AC-1, AC-2, AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/App.tsx` | health `<p>` gets `.visually-hidden`; keep `data-testid="api-health-status"` |
| `web/src/styles/base.css` | `.visually-hidden` |
| `web/src/styles/components.css` | `.btn` padding 12px 14px |
| `web/tests/App.test.tsx` | still finds the test id; add that it is not visible (`toBeVisible` false or CSS clip) |

## Context the implementer needs
- **Architecture**: ADR-019. Gap G-HEALTH.
- **Do not touch**: `signin.css`, `SignInScreen.tsx`, `shell.css`, `ask-bar.css`,
  route screens. Those are later e11 features.
- **Do not** remove the health fetch.

## Definition of done
- [ ] `npm test` in `web/` — App health tests pass and assert the node is not visible.
- [ ] No visible `API:` string on the sign-in route in those tests.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | health probe still updates the test id; not visible | `web/tests/App.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E11/F01/US01/T01
  produces: [visual-chrome]
```
