---
id: E06/F04/US01/T01
type: task
story: us-01-workspace-members-invite
wave: 6
status: live
target_repo: contigo-web
---

# task-01-workspace-members-invite — Members & roles + invite UI

## Coding objective
Implement the members table + invite form with admin/procurement gate.

## Parent story AC covered
- AC-1 (members table), AC-2 (invite pane + roles), AC-3 (non-admin state).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/contigo-web/src/routes/workspace/members/ | members UI |
| inputs/design/prototypes/ia.md | roles (cite) |
| inputs/design/prototypes/screens.md | screen 2 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (2), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-018 (admin vs procurement gate), ADR-020 (screen 2).
- **Recovery**: skipped on e06 (`wave/E06-F04-US01-T01` was an alias of the
  shell commit). Re-queued on slice `e07`. `/workspace/members` is still a
  `ScaffoldScreen` placeholder. Implement the real table + invite form.
- **Layout**: depend on `web-layout-mockup` (E06/F06). Do not ship this
  screen inside the leftover `max-width: 40rem` scaffold.

## Definition of done
- [ ] Invite + role gate works in browser; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | non-admin sees request-access, not invite | workspace/contigo-web/tests |

## Wave-spec entry
```yaml
- id: E06/F04/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-04-workspace-members-ui/us-01-workspace-members-invite/tasks/task-01-workspace-members-invite.md
  produces: [web-members-ui]
  depends_on: [web-app-shell]
  effort: M
  layer: web
  status: live
```
