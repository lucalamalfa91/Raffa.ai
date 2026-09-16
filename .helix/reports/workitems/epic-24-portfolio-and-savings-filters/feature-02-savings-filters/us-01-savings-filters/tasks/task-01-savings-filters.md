---
id: E24/F02/US01/T01
type: task
story: us-01-savings-filters
wave: w18
status: live
target_repo: raffa-web
---

# task-01-savings-filters — Savings opportunities supplier/status/currency filters

## Coding objective
Add supplier / status / currency filter controls to the Savings opportunities
list (`web/src/routes/savings/index.tsx` + `savingsViewModel.ts` +
`OpportunitiesTable.tsx`), operating on the already-loaded
`getSavingsOpportunities` rows (supplier via the `getPortfolio` name index,
status `Identified`/`InProgress`/`Realized`, currency from
`buildOpportunityRows`). Filtering is pure client-side view state over the
server data — never a client store, never a new domain concept, and "Estimate"
stays a sort/numeric column, not a filter. Clearing restores the full list.
Cite the design oracle `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137`
(§8) and anchor the controls above the opportunities table.

## Parent story AC covered
- AC-1 supplier / status / currency filter controls.
- AC-2 council's filter set, no invented category, Estimate is a sort.
- AC-3 clearing restores full list; no storage write.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/savings/savingsFilters.ts | new: pure filter predicates (supplier/status/currency) |
| web/src/routes/savings/index.tsx | render the filter controls over `rows` |
| web/src/routes/savings/savingsViewModel.ts | add a `filterOpportunityRows` pure helper |

## Context the implementer needs

**Closes: NW-25**

- **Architecture decisions in force**: ADR-020 (presentation only, no client store).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137`.
- **Do not touch**: `client.ts` / `raffa-api.v1.json` (no wire change); the Portfolio screen.

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving each filter restricts the rows and clearing restores the full list

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | supplier/status/currency filters restrict rows; clear restores | `web/src/routes/savings/savingsFilters.test.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E24/F02/US01/T01
  prompt: reports/workitems/epic-24-portfolio-and-savings-filters/feature-02-savings-filters/us-01-savings-filters/tasks/task-01-savings-filters.md
  produces: [savings-filters]
  depends_on: []
  effort: S
  layer: frontend
  status: live
```
