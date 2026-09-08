---
id: E13/F03/US01/T01
type: task
story: us-01-supplier-identity
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-supplier-entity — `Supplier` entity, `ISupplierResolver` port, migration + `suppliers.sql`, RLS

## Coding objective

Turn the bare `backend/src/Contigo.Suppliers.Products` into the supplier
module (`inputs/requirements.md` R-SUP-02): `Domain/Supplier` (tenant-
scoped: id, tenantId, name, normalizedName, aliases[], category?, country?,
createdAt, updatedAt; `TenantScopedEntity` copied from the Documents
pattern — this module may only reference SharedKernel), `Application/
SupplierNameNormalizer` (lower-case; strip legal suffixes Inc, Inc., Ltd,
Limited, GmbH, AG, SA, S.A., SpA, S.p.A., S.r.l., Srl, LLC, Corp,
Corporation, Co.; strip punctuation and collapse whitespace),
`Application/SupplierResolver : ISupplierResolver` (match by
`normalizedName` or alias within the tenant, else create; unique index
`(tenant_id, normalized_name)`), `Infrastructure/SuppliersDbContext`
(+ factory, configuration, `TenantRlsConnectionInterceptor` wiring like
`DocumentsContractsDbContext`), an EF migration creating `supplier` with
the RLS policy on `app.tenant_id`, the checked-in idempotent
`Migrations/Scripts/suppliers.sql` (ADR-021) with a
`SuppliersMigrationScriptTests`, and `AddSuppliersProductsModule(string
connectionString)` registering the DbContext + resolver. Declare the port
`ISupplierResolver` in `backend/src/Contigo.SharedKernel/Suppliers/`
(`Task<Result<SupplierRef>> ResolveAsync(TenantId, string rawName,
CancellationToken)` with `SupplierRef(EntityId Id, string Name)`) plus
`ISupplierNameLookup` (`GetNamesAsync(TenantId, IReadOnlyCollection<EntityId>)`
→ id → name map) so Documents, Renewals and the API can show names without
referencing this module. Add `suppliers.sql` to both fixed-order `SCRIPTS`
arrays in `.github/workflows/backend.yml` (this task is the phase-2
writer of that file). Do not edit `Program.cs` (F06/T01 wires the module
in phase 3) and do not touch the extraction pipeline (T02).

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Contigo.SharedKernel/Suppliers/ISupplierResolver.cs`, `ISupplierNameLookup.cs`, `SupplierRef.cs` | new ports |
| `backend/src/Contigo.Suppliers.Products/Domain/Supplier.cs`, `TenantScopedEntity.cs` | new |
| `backend/src/Contigo.Suppliers.Products/Application/SupplierNameNormalizer.cs`, `SupplierResolver.cs`, `SupplierNameLookup.cs` | new |
| `backend/src/Contigo.Suppliers.Products/Infrastructure/*` (DbContext, factory, options, configuration, `ServiceCollectionExtensions.cs`) | new |
| `backend/src/Contigo.Suppliers.Products/Migrations/*` + `Migrations/Scripts/suppliers.sql` | new |
| `backend/src/Contigo.Suppliers.Products/Contigo.Suppliers.Products.csproj` | EF / Npgsql packages |
| `backend/tests/Contigo.Suppliers.Products.Tests/*` | normalizer, resolver, migration script |
| `backend/tests/Contigo.IntegrationTests/SupplierCrossTenantIsolationTests.cs` | RLS: another tenant cannot read a supplier |
| `.github/workflows/backend.yml` | add `suppliers.sql` to both SCRIPTS arrays |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (supplier identity; module allow-list `[SharedKernel]`), ADR-002 (ports in SharedKernel; composition in `Contigo.Api`), ADR-009 (RLS), ADR-021 (checked-in idempotent SQL applied by CI).
- Gap G-SUPPLIER. Mirror `Contigo.Documents.Contracts/Infrastructure/*` and `Migrations/Scripts/documents-contracts.sql`; `backend/README.md` "Schema apply".
- **Do not touch**: `StagedExtractionService`, `DocumentProcessingPipeline` (T02), `Program.cs`, `Contigo.Chat`, `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.Suppliers.Products.Tests` exit 0 — "Salesforce, Inc." / "salesforce" / "SALESFORCE INC" normalize equal; resolver reuses the row on the second call; alias match; migration script test green
- [ ] `dotnet test backend/tests/Contigo.IntegrationTests --filter SupplierCrossTenantIsolationTests` exit 0 (when the Postgres fixture is available in CI) — cross-tenant read denied by RLS
- [ ] `grep -c "suppliers.sql" .github/workflows/backend.yml` = 2; `dotnet build backend/Contigo.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | normalization + resolution | `Contigo.Suppliers.Products.Tests/*` |
| unit | idempotent script matches the model | `SuppliersMigrationScriptTests.cs` |
| integration | RLS isolation | `Contigo.IntegrationTests/SupplierCrossTenantIsolationTests.cs` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F03/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-03-supplier-identity/us-01-supplier-identity/tasks/task-01-supplier-entity.md
  produces: [supplier-entity]
  depends_on: [v2-scaffold]
  effort: M
  layer: backend
  status: live
```
