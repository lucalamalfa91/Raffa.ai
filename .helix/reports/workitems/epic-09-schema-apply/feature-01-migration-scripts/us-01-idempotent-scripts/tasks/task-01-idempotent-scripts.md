---
id: E09/F01/US01/T01
type: task
story: us-01-idempotent-scripts
wave: 9
status: live
target_repo: raffa-backend
---

# task-01-idempotent-scripts — Generate module SQL scripts + prove apply

## Coding objective

Generate and check in idempotent EF SQL for Identity.Workspace, Audit,
Renewals, Savings, Quotes (`dotnet ef migrations script --idempotent`).
Add tests modeled on `DocumentsContractsMigrationScriptTests`: each script
applies to a bare `pgvector/pgvector:pg16` via Npgsql (no `MigrateAsync`).
Fail CI if a script is missing or does not match a fresh generate.

Do **not** call `Database.MigrateAsync()` in `Raffa.Api`. Do **not** add
Swagger. Do **not** edit `slices/e01.yaml`–`e05.yaml` or
`slice.current.yaml`.

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-backend/src/Raffa.Identity.Workspace/Migrations/Scripts/ | new sql |
| workspace/raffa-backend/src/Raffa.Audit/Migrations/Scripts/ | new sql |
| workspace/raffa-backend/src/Raffa.Renewals/Migrations/Scripts/ | new sql |
| workspace/raffa-backend/src/Raffa.Savings/Migrations/Scripts/ | new sql |
| workspace/raffa-backend/src/Raffa.Quotes/Migrations/Scripts/ | new sql |
| workspace/raffa-backend/tests/ | script apply + stale-check tests |

## Context the implementer needs

- **Architecture decisions in force**: ADR-003, ADR-009, ADR-021.
- Pattern: `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql`
  and `DocumentsContractsMigrationScriptTests`.

## Definition of done

- [ ] `dotnet test` proves each new script applies on empty Postgres+pgvector
      and the stale-check fails if the file is deleted or truncated.

## Wave-spec entry

```yaml
- {id: E09/F01/US01/T01, prompt: reports/workitems/epic-09-schema-apply/feature-01-migration-scripts/us-01-idempotent-scripts/tasks/task-01-idempotent-scripts.md, produces: [schema-scripts], depends_on: [], effort: L, layer: backend, status: live}
```
