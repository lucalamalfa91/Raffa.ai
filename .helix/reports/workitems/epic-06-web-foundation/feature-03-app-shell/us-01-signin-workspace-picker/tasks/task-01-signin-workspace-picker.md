---
id: E06/F03/US01/T01
type: task
story: us-01-signin-workspace-picker
wave: 6
status: live
target_repo: raffa-web
---

# task-01-signin-workspace-picker — Sign-in + workspace picker screen

## Coding objective
Implement sign-in (Entra) → workspace picker (list + Create new) with redirect state.

## Parent story AC covered
- AC-1 (workspace list + create), AC-2 (redirect spinner), AC-3 (real config.json).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/signin/ | sign-in + picker |
| workspace/raffa-web/public/config.json | env config (read, wire) |
| inputs/design/prototypes/ia.md | route `/signin` (cite) |
| inputs/design/prototypes/screens.md | screen 1 (read, cite) |
| inputs/design/prototypes/day1-demo.html | interactive reference (read, cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/ia.md`, `screens.md` (1), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-012 (OIDC PKCE), ADR-018 (/signin).

## Definition of done
- [ ] Sign-in → picker flow works in browser; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| e2e | sign-in → workspace list | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E06/F03/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-03-app-shell/us-01-signin-workspace-picker/tasks/task-01-signin-workspace-picker.md
  produces: [web-signin-workspace]
  depends_on: [web-api-client-current]
  effort: M
  layer: web
  status: live
```
