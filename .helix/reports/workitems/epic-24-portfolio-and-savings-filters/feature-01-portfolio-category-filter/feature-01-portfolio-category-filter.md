---
id: feature-01
type: feature
parent: epic-24
wave: w18
status: active
---

# feature-01-portfolio-category-filter — Portfolio supplier-category filter

## Slice
Add a supplier-category filter to the portfolio list: the backend extends the
filter to accept `?category=`, resolving it by joining `Supplier.Category` in
`Raffa.Api` (matching how `supplierName` already joins), and the web adds the
filter control as a query parameter — never a client store.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | portfolio-category-backend | w18 |
| us-02 | portfolio-category-web | w18 |

## Architecture decisions in force
- ADR-002 — the category join happens in `Raffa.Api` (the one project allowed to reference both modules).
- ADR-020 — filter is a query parameter, never a client store.

## Target repo
`raffa-backend` + `raffa-web` (mixed)
