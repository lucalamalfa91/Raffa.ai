---
id: E10/F01/US01/T01
type: task
story: us-01-seed-demo-db
wave: 10
status: live
target_repo: contigo-backend
---

# task-01-seed-fixture — Seed fixture benchmark + savings on Azure Postgres

## Coding objective

Add a repeatable, checked-in seed (dotnet tool, SQL, or CI job) that writes
ADR-001 fixture benchmark rows and at least one savings opportunity onto
Flexible Server `contigo_demo` after e09 schema apply. Optional same path
for `contigo_dev`. Use Key Vault `postgres-connection`. Do not open the
laptop firewall. Do not call `Database.MigrateAsync()`. Do not generate EF
scripts (e09). Do not build web screens (e06–e08).

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/contigo-backend/ (seed project or Scripts/) | new seed |
| .github/workflows/ (optional callable) | run seed against demo/dev |

## Context the implementer needs

- **Architecture decisions in force**: ADR-001, ADR-009, ADR-021, ADR-022.
- Fixture adapter already exists in-process (`FixtureBenchmarkAdapter`).
  This task persists rows the Day-1 savings UI can read.
- RLS: seed as a role that can insert the demo tenant; API identity stays
  the non-bypass app role.

## Definition of done

- [ ] After e09, running the seed against `contigo_demo` makes
      `GET /api/savings` (with the demo tenant header) return at least one
      fixture-backed opportunity.

## Wave-spec entry

```yaml
- {id: E10/F01/US01/T01, prompt: reports/workitems/epic-10-demo-readiness/feature-01-fixture-seed/us-01-seed-demo-db/tasks/task-01-seed-fixture.md, produces: [demo-fixture-seed], depends_on: [], effort: L, layer: backend, status: live}
```
