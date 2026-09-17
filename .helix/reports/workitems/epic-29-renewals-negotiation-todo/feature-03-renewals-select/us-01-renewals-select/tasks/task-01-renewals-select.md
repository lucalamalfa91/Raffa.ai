---
id: E29/F03/US01/T01
type: task
story: us-01-renewals-select
wave: w19
status: live
target_repo: raffa-web
---

# task-01-renewals-select — Renewals honours `?select=` (NW-84)

## Coding objective
In `web/src/routes/renewals/index.tsx` (`useState` selection today, never reads
the query), initialise selection from `useSearchParams().get("select")`: a
matching listed `contractId` becomes the selected row (showing its insight
pane — and TODOs once feature-04 lands). An invalid or other-tenant id falls
back to the current default (top-priority row) without a 500 or a leak. Keep the
existing auto-select default for the no-query case.

## Parent story AC covered
- AC-1 `?select=` selects the matching row + insight.
- AC-2 invalid id falls back to default, no 500.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/renewals/index.tsx | read `?select=` and initialise selection |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012 (cl. 51).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §7.
- **Do not touch**: `CapabilityRouting.BuildHref` (already emits `?select=`); the TODO list (feature-04).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test that `?select=<guid>` selects the row and an invalid id falls back without error

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `?select=` selection + invalid fallback | `web/src/routes/renewals/index.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E29/F03/US01/T01
  prompt: reports/workitems/epic-29-renewals-negotiation-todo/feature-03-renewals-select/us-01-renewals-select/tasks/task-01-renewals-select.md
  produces: [renewals-select]
  depends_on: []
  effort: S
  layer: frontend
  status: live
```
