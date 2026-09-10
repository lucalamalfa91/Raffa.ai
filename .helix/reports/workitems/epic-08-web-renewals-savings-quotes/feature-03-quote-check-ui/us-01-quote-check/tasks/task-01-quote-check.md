---
id: E08/F03/US01/T01
type: task
story: us-01-quote-check
wave: 8
status: live
target_repo: raffa-web
---

# task-01-quote-check — Quote check stepper + negotiation + outcome UI

## Coding objective
Implement the quote-check stepper (Extract → Assessment → Target → Negotiation)
and outcome form.

## Parent story AC covered
- AC-1 (stepper), AC-2 (extract + SKU map + recalculate), AC-3 (assessment/target), AC-4 (negotiation + outcome).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/quotes/:id/ | quote check UI |
| inputs/design/prototypes/screens.md | screen 10 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (10), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 10), ADR-019 (provenance card; blocked assessment until SKU resolved).

## Definition of done
- [ ] Quote stepper + outcome → Home realized renders on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | unmatched SKU blocks assessment until mapped | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E08/F03/US01/T01
  prompt: reports/workitems/epic-08-web-renewals-savings-quotes/feature-03-quote-check-ui/us-01-quote-check/tasks/task-01-quote-check.md
  produces: [web-quote-ui]
  depends_on: [web-app-shell]
  effort: L
  layer: web
  status: live
```
