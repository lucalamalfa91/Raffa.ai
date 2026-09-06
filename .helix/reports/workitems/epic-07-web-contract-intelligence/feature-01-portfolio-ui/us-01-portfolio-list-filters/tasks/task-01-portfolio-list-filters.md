---
id: E07/F01/US01/T01
type: task
story: us-01-portfolio-list-filters
wave: 7
status: live
target_repo: contigo-web
---

# task-01-portfolio-list-filters — Portfolio + filters + attention strip UI

## Coding objective
Implement the portfolio table, filters, attention strip, and states.

## Parent story AC covered
- AC-1 (filters), AC-2 (attention strip), AC-3 (sort/tint), AC-4 (states).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/contigo-web/src/routes/contracts/ | portfolio UI |
| inputs/design/prototypes/screens.md | screen 4 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (4), `day1-demo.html`; prefer `/design-sync`.
- **Layout**: wait for `web-layout-mockup`. Match [artifact 9249d66d](https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58), not the leftover 40rem scaffold.
- **Architecture decisions in force**: ADR-020 (screen 4), ADR-019 (urgency in column one, not colour-only; table hover tint).

## Definition of done
- [ ] Portfolio renders with filters + states on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | filter + attention strip click | workspace/contigo-web/tests |

## Wave-spec entry
```yaml
- id: E07/F01/US01/T01
  prompt: reports/workitems/epic-07-web-contract-intelligence/feature-01-portfolio-ui/us-01-portfolio-list-filters/tasks/task-01-portfolio-list-filters.md
  produces: [web-portfolio]
  depends_on: [web-layout-mockup]
  effort: L
  layer: web
  status: live
```
