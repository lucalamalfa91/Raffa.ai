---
id: E24/F01/US02/T01
type: task
story: us-02-portfolio-category-web
wave: w18
status: live
target_repo: raffa-web
---

# task-01-portfolio-category-web — Portfolio category filter control

## Coding objective
Add a category filter control to the Portfolio screen (`web/src/routes/contracts/`)
that issues `?category=` on `GET /api/contracts` via the `getPortfolio` wrapper,
kept as a router query parameter (never a client store), sourced from the real
supplier categories the host resolves. Clearing the filter re-loads the full
portfolio. Cite the design oracle `inputs/design/prototypes/raffa-v2/screens-v2.md:114-121`
(§6 "Type" column stays the document type; the category filter is a list
restriction, not a column rename) and anchor the control in the existing
Portfolio filter area.

## Parent story AC covered
- AC-1 category filter control issues `?category=`.
- AC-2 filter is a server query parameter, never a client store.
- AC-3 clearing re-loads the full portfolio.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/contracts/portfolio/PortfolioFilterControl.tsx | new: category filter control |
| web/src/routes/contracts/portfolio/index.tsx | wire the control to the `?category=` query param |
| web/src/routes/contracts/portfolio/portfolioViewModel.ts | carry the category filter state as a query param |

## Context the implementer needs

**Closes: NW-23**

- **Architecture decisions in force**: ADR-020 (control, never a store); ADR-018 (`/contracts` route).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md:114-121`.
- **Do not touch**: the backend category join (phase 1); the Savings screen.

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving the control issues `?category=` and clearing re-loads the full list

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the filter control issues `?category=` and clears to full list | `web/src/routes/contracts/portfolio/*.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E24/F01/US02/T01
  prompt: reports/workitems/epic-24-portfolio-and-savings-filters/feature-01-portfolio-category-filter/us-02-portfolio-category-web/tasks/task-01-portfolio-category-web.md
  produces: [portfolio-category-control]
  depends_on: [portfolio-category-filter]
  effort: S
  layer: frontend
  status: live
```
