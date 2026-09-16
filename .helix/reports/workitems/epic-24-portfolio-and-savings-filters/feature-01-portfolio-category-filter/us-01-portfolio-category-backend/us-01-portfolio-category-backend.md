---
id: us-01
type: user-story
parent: feature-01
wave: w18
status: active
---

# us-01-portfolio-category-backend — Filter the portfolio by supplier category (host join)

## Story
As an **operator**, I want to restrict the portfolio list by supplier category,
so that I can see only the contracts from a certain type of supplier.

## Acceptance criteria
- [ ] AC-1 `GET /api/contracts?category=<c>` restricts the list to contracts whose supplier has that category (joined in `Raffa.Api`), returning 200 with only matching rows.
- [ ] AC-2 a blank/absent `category` leaves the full tenant-scoped portfolio (no regression to the existing filters).
- [ ] AC-3 a `category` the domain has no supplier for returns an empty list, never a fabricated value.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-002 (host join) and ADR-020
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | builds on the existing `Supplier.Category` and `ISupplierNameLookup`-style host join already on main |

## Architecture decisions in force
- ADR-002 — the join is host-side (`Raffa.Api`); `Raffa.Documents.Contracts` stays at `[SharedKernel, AiGateway]`.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | portfolio-category-backend | M | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Filter by supplier category joined in the host; §6 "Type" stays the document type; no fake enum ships.

## Open questions
- none (OQ-w17-007 resolved at the table: supplier category joined in host)
