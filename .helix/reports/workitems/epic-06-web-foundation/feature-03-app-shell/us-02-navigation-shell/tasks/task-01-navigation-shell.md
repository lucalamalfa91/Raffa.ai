---
id: E06/F03/US02/T01
type: task
story: us-02-navigation-shell
wave: 6
status: live
target_repo: raffa-web
---

# task-01-navigation-shell — Left-rail shell + role guards + global Ask

## Coding objective
Implement the 224px left rail, admin/procurement route guards, and the global
Ask bar scaffold.

## Parent story AC covered
- AC-1 (rail items), AC-2 (role guards), AC-3 (global Ask bar).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/components/shell/ | rail + guards |
| workspace/raffa-web/src/components/ask-bar/ | global Ask |
| inputs/design/prototypes/ia.md | route map + roles (cite) |
| inputs/design/prototypes/day1-demo.html | shell reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/ia.md`, `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-018 (roles are a permission gate, not an IA fork), ADR-019 (shell + global Ask).

## Definition of done
- [ ] Shell renders with role-gated Workspace & members on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | role guard hides admin item for Procurement | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E06/F03/US02/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-03-app-shell/us-02-navigation-shell/tasks/task-01-navigation-shell.md
  produces: [web-app-shell]
  depends_on: [web-design-system, web-signin-workspace]
  effort: M
  layer: web
  status: live
```
