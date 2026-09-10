---
id: E08/F01/US01/T01
type: task
story: us-01-renewal-pipeline
wave: 8
status: live
target_repo: raffa-web
---

# task-01-renewal-pipeline — Renewal pipeline + insight + action UI

## Coding objective
Implement the renewal threshold strip, priority table, insight card, and actions.

## Parent story AC covered
- AC-1 (threshold strip), AC-2 (table), AC-3 (insight + actions), AC-4 (deadline styling + states).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/renewals/ | renewal UI |
| inputs/design/prototypes/screens.md | screen 8 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (8), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 8), ADR-019 (deadline ≤45d accent-700 weight 600).

## Definition of done
- [ ] Renewal pipeline with action → opportunity renders on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | action creates opportunity link to Home | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E08/F01/US01/T01
  prompt: reports/workitems/epic-08-web-renewals-savings-quotes/feature-01-renewal-pipeline-ui/us-01-renewal-pipeline/tasks/task-01-renewal-pipeline.md
  produces: [web-renewal-ui]
  depends_on: [web-contract-360, web-app-shell]
  effort: L
  layer: web
  status: live
```
