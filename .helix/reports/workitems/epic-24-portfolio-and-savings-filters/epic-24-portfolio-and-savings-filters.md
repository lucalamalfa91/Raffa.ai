---
id: epic-24
type: epic
wave: w18
status: active
extends: [epic-02 F03, epic-04 F03, epic-07 F01, epic-08 F02]
---

# epic-24-portfolio-and-savings-filters — Portfolio and savings list filters

## Business capability
A Procurement operator can restrict what they are looking at: the Portfolio by
supplier **category** (joined in the host, matching how `supplierName` already
resolves), and the Savings opportunities list by supplier / status / currency.
Both are read-only restrictions of lists that already load server data — no new
domain concept is invented and nothing is stored client-side.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/w18-todo.md §1 | NW-23 (portfolio category), NW-25 (savings filters) |
| ADR-002 | supplier category joined in `Raffa.Api` (host composition) |
| ADR-020 | filter-as-control (no client store for a missing GET) |
| screens-v2.md §6 (114-121), §8 (132-137) | Portfolio "Type" stays doc type; savings filter set |

## Features
| ID | Title | Wave |
|----|------|------|
| feature-01 | portfolio-category-filter | w18 |
| feature-02 | savings-filters | w18 |

## Success looks like
On `dev`, the operator restricts the portfolio by supplier category and the
savings list by supplier/status/currency; the restrictions are query parameters,
never a client store, and a filter never fabricates a category the domain does
not have.

## Architecture decisions in force
- ADR-002 — the category join happens in `Raffa.Api` (the one project allowed to reference both modules).
- ADR-020 — the filter is a query parameter, never a client store standing in for a missing GET.

## Out of scope
- No new schema / migration: `Supplier.Category` already exists (`Supplier.cs:36`).
- No invented "category" for Savings (Estimate is a sort, not a filter).
- `screens-v2` §6 "Type" column stays the document type, not a spend category.
