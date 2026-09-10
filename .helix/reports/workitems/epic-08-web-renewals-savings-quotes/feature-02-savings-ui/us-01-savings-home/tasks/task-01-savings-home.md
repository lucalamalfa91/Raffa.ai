---
id: E08/F02/US01/T01
type: task
story: us-01-savings-home
wave: 8
status: live
target_repo: raffa-web
---

# task-01-savings-home — Savings KPIs + opportunities UI

## Coding objective
Implement the Home screen: 6 savings KPI cells + opportunities table.

## Parent story AC covered
- AC-1 (6 KPIs), AC-2 (opportunities table), AC-3 (rows + stale-labelled error state).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/home/ | Home UI |
| inputs/design/prototypes/screens.md | screen 9 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (9), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 9), ADR-019 (stale-labelled KPI state).

## Definition of done
- [ ] Home renders KPIs + opportunities on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | benchmark-unreachable → KPIs stale-labelled | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E08/F02/US01/T01
  prompt: reports/workitems/epic-08-web-renewals-savings-quotes/feature-02-savings-ui/us-01-savings-home/tasks/task-01-savings-home.md
  produces: [web-savings-ui]
  depends_on: [web-app-shell, web-renewal-ui]
  effort: M
  layer: web
  status: live
```
