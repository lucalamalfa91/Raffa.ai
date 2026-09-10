---
id: E07/F02/US01/T01
type: task
story: us-01-contract-360
wave: 7
status: live
target_repo: raffa-web
---

# task-01-contract-360 — Contract 360 header + tabs UI

## Coding objective
Implement Contract 360 header, fact row, recommendation block, and 10 tabs with
facts/AI separation.

## Parent story AC covered
- AC-1 (header), AC-2 (fact row), AC-3 (overview), AC-4 (10 tabs, facts/AI).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/contracts/:id/ | Contract 360 UI |
| inputs/design/prototypes/screens.md | screen 5 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (5), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 5), ADR-019 (facts/AI never mixed).

## Definition of done
- [ ] Contract 360 renders with 10 tabs on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | recommendation block separated from deterministic facts | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E07/F02/US01/T01
  prompt: reports/workitems/epic-07-web-contract-intelligence/feature-02-contract-360-ui/us-01-contract-360/tasks/task-01-contract-360.md
  produces: [web-contract-360]
  depends_on: [web-portfolio, web-api-client-current]
  effort: L
  layer: web
  status: live
```
