---
id: E01/F04/US02/T01
type: task
story: us-02-relational-store
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-ef-core-pgvector — Wire EF Core/npgsql + pgvector + initial migrations

## Coding objective

Configure EF Core with the `Npgsql.EntityFrameworkCore.PostgreSQL` provider and
enable the `pgvector` extension so a `vector` column type is usable for embeddings
(ADR-003). Scaffold the `DbContext` for the Documents/Contracts context (Document,
DocumentVersion, ExtractionJob, Contract, ContractVersion, Clause, Obligation,
Risk, CorrectionHistory, Embedding) and generate initial code-first migrations.
Store vectors next to relational facts in the one Postgres store; deterministic
results are persisted by domain code, never LLM-computed truth (App C #1, #6).

## Parent story AC covered

- AC-1 (EF Core npgsql + pgvector `vector` type)
- AC-2 (code-first migrations only)
- AC-3 (embedding table in Documents/Contracts, deterministic persistence)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-backend/src/Raffa.Documents.Contracts/Infrastructure/*.cs | DbContext + entity config |
| workspace/raffa-backend/src/Raffa.Documents.Contracts/Migrations/*.cs | initial migration |

## Context the implementer needs

- **Architecture decisions in force**: ADR-003 (Postgres + pgvector, EF Core/npgsql, migrations).
- **Do not touch**: RLS wiring (us-03) — but `tenant_id` columns are added here per ADR-009.

## Definition of done

- [ ] `dotnet ef migrations add Initial` + `database update` succeed; a `vector` column is usable.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | migrations apply + pgvector type works | `Migrations`, local Postgres |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F04/US02/T01
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-02-relational-store/tasks/task-01-ef-core-pgvector.md
  produces: [postgres-schema]
  depends_on: [dotnet-solution]
  effort: L
  layer: backend
  status: live
```
