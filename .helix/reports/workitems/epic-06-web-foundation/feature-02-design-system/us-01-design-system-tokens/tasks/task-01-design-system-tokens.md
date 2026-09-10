---
id: E06/F02/US01/T01
type: task
story: us-01-design-system-tokens
wave: 6
status: live
target_repo: raffa-web
---

# task-01-design-system-tokens — Port design system tokens + components

## Coding objective
Port the Modernist design system into `web/` as the shared token sheet + component
catalogue, consuming `styles.css` values verbatim.

## Parent story AC covered
- AC-1 (tokens/components from design-system.md), AC-2 (semantic mappings), AC-3 (accessibility).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/styles/ | token sheet + component classes |
| inputs/design/prototypes/design-system.md | source (read, cite) |
| inputs/design/prototypes/day1-demo.html | executable reference (read, cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/design-system.md` + `day1-demo.html`; prefer `/design-sync` where enabled, else the markdown dump is authoritative.
- **Architecture decisions in force**: ADR-019 (adopt export verbatim, do not fork), ADR-012.

## Definition of done
- [ ] `npm run build` exits 0; token sheet committed and used by later screens.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| build | shared sheet compiles | workspace/raffa-web |

## Wave-spec entry
```yaml
- id: E06/F02/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-02-design-system/us-01-design-system-tokens/tasks/task-01-design-system-tokens.md
  produces: [web-design-system]
  depends_on: []
  effort: M
  layer: web
  status: live
```
