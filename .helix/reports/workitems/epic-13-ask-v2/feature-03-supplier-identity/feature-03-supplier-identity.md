---
id: F03
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-03-supplier-identity — Supplier entity, resolver, extraction, back-fill

## Slice

Today no supplier name exists anywhere (`Raffa.Suppliers.Products` is a
bare csproj, no `supplier` fact is extracted, `Contract.SupplierId` is never
set) so Ask cannot say "your Allianz contract" and Documents / Portfolio /
Renewals show guids or nothing. This feature adds the `Supplier` entity
(tenant-scoped, RLS, normalized name + aliases), the `ISupplierResolver`
port in SharedKernel called from the pipeline orchestration, the `supplier`
**critical** extracted fact (page, span, confidence; reviewed like any
critical field), and the back-fill of existing contracts through reprocess
(`inputs/requirements.md` R-SUP-01…04).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Contracts are named by supplier everywhere | 13 |

## Architecture decisions in force

- ADR-024 — supplier identity, module map (`Raffa.Suppliers.Products` → `[SharedKernel]`)
- ADR-002 — Documents may not reference Suppliers; the port lives in SharedKernel
- ADR-009 — RLS on the new table; ADR-021 — `suppliers.sql` applied by CI
- spec §7.3 — critical fields require stricter validation

## Target repo

`raffa-backend`
