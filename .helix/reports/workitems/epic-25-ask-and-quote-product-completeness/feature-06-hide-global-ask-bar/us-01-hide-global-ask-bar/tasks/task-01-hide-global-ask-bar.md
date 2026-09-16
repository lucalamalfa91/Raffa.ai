---
id: E25/F06/US01/T01
type: task
story: us-01-hide-global-ask-bar
wave: w18
status: live
target_repo: raffa-web
---

# task-01-hide-global-ask-bar — Suppress the global Ask bar on Ask screens

## Coding objective
In `web/src/components/shell/AppShell.tsx` (which already calls `useLocation`
and renders `GlobalAskBar` at `:82`), suppress the global bar when the path is
`/ask` or `/ask/:id` (the route whose own composer replaces it), keep it
everywhere else, and leave ⌘K/Ctrl+K to focus the on-Ask composer (the `AskRoute`
input, or `GlobalAskBar`'s own shortcut where the bar still renders). A
route-scoped check, not a new context. Cite the IA anchor
`inputs/design/prototypes/raffa-v2/ia-v2.md` (§2 Ask bar + route map `/ask`,
`/ask/:id`).

## Parent story AC covered
- AC-1 suppressed on `/ask` + `/ask/:id`, kept elsewhere.
- AC-2 ⌘K focuses the composer on Ask.
- AC-3 bar still appears on non-Ask routes.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/components/shell/AppShell.tsx | suppress `GlobalAskBar` on `/ask` + `/ask/:id` via `useLocation` |

## Context the implementer needs

**Closes: NW-60**

- **Architecture decisions in force**: ADR-018 (the `/ask` route owns its composer); ADR-020 (no duplicate bar).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/ia-v2.md` §2.
- **Do not touch**: `GlobalAskBar.tsx` internals (it keeps its ⌘K behaviour for non-Ask routes); the role-gate feature (phase 1, same file) is in a different phase.

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving the bar is suppressed on `/ask` (and `/ask/:id`) and present on e.g. `/documents`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | bar suppressed on Ask, present elsewhere | `web/src/components/shell/AppShell.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F06/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-06-hide-global-ask-bar/us-01-hide-global-ask-bar/tasks/task-01-hide-global-ask-bar.md
  produces: [global-bar-suppressed]
  depends_on: []
  effort: S
  layer: frontend
  status: live
```
