---
id: E24/F01/US01/T01
type: task
story: us-01-portfolio-category-backend
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-portfolio-category-backend — `?category=` portfolio filter via host join on `Supplier.Category`

## Coding objective
Extend the portfolio filter with `?category=`: in
`backend/src/Raffa.Documents.Contracts/Application/PortfolioFilter.cs` add an
optional `Category` member (replacing the stale "Category deliberately not a
member" comment, which is now false — `Supplier.Category` exists at
`backend/src/Raffa.Suppliers.Products/Domain/Supplier.cs:36`); parse
`?category=` in `PortfolioEndpointExtensions.TryParseFilter`
(`backend/src/Raffa.Api/PortfolioEndpointExtensions.cs`); and resolve the filter
in the host by joining supplier category in `Raffa.Api` — the one project
allowed to reference both modules (ADR-002), the same "join the name on here"
shape `supplierName` already uses (`ResolveSupplierNamesAsync`,
`:133-146`). A `category` with no matching supplier returns an empty list,
never a fabricated value. Declare `?category=` in
`web/openapi/raffa-api.v1.json` and regenerate `web/src/api/generated/schema.ts`
+ the hand-written `web/src/api/client.ts` `getPortfolio` request type
(single-writer: this is the client-wrapper writer in this phase).

## Parent story AC covered
- AC-1 `?category=<c>` restricts to contracts whose supplier has that category.
- AC-2 blank/absent `category` = full portfolio.
- AC-3 an unmatched category returns empty, never fabricated.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Documents.Contracts/Application/PortfolioFilter.cs | add optional `Category`; update the stale deferral comment |
| backend/src/Raffa.Api/PortfolioEndpointExtensions.cs | parse `?category=`; join supplier category in the host |
| backend/src/Raffa.Suppliers.Products/Application/SupplierCategoryLookup.cs | new: batched category lookup (or extend `ISupplierNameLookup` consumer) |
| web/openapi/raffa-api.v1.json | document `?category=` |
| web/src/api/generated/schema.ts | regenerate |
| web/src/api/client.ts | `getPortfolio` accepts `category` |

## Context the implementer needs

**Closes: NW-23**

- **Architecture decisions in force**: ADR-002 (host composition; `DependencyDirectionTests.cs:63` keeps `Raffa.Documents.Contracts` at `[SharedKernel, AiGateway]` — the join must be in `Raffa.Api`); ADR-020 (query-param control).
- **Do not touch**: `Raffa.Documents.Contracts` domain (no category there); the migration (no new column — `Supplier.Category` exists).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test proving `?category=` filters by supplier category and an unmatched category returns empty
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests` exits 0 (direction test still passes)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | `?category=` restricts the list via host join | `backend/tests/Raffa.Api.Tests` |
| unit | `PortfolioFilter` parses `category` and defaults to `None` | `backend/tests/Raffa.Documents.Contracts.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E24/F01/US01/T01
  prompt: reports/workitems/epic-24-portfolio-and-savings-filters/feature-01-portfolio-category-filter/us-01-portfolio-category-backend/tasks/task-01-portfolio-category-backend.md
  produces: [portfolio-category-filter]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
