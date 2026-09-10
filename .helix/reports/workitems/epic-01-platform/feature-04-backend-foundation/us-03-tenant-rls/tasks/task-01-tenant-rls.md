---
id: E01/F04/US03/T01
type: task
story: us-03-tenant-rls
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-tenant-rls — Add RLS + tenant claim + migration check

## Coding objective

Implement tenant isolation per ADR-009: ensure every tenant-scoped table has a
non-null, indexed `tenant_id`; add a `FORCE ROW LEVEL SECURITY` policy per table
keyed to `current_setting('app.tenant_id', true)`; and add a data-access layer
interceptor that runs `SET app.tenant_id = <id>` once per request/worker-job and
resets it on connection return — never operating under `BYPASSRLS`. Add a CI
migration check that rejects any tenant-scoped table lacking an RLS policy. Worker
jobs must carry `tenant_id` from the queue message and set the same claim.

## Parent story AC covered

- AC-1 (tenant_id on every table)
- AC-2 (RLS policy per table)
- AC-3 (per-connection claim, no BYPASSRLS)
- AC-4 (CI migration check)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-backend/src/Raffa.SharedKernel/Tenancy/*.cs | tenant-aware connection interceptor |
| workspace/raffa-backend/src/*/Infrastructure/*rls*.cs | per-table RLS policy SQL |
| workspace/raffa-backend/tests/Raffa.Tenancy/*.cs | cross-tenant isolation test |

## Context the implementer needs

- **Architecture decisions in force**: ADR-009 (RLS backstop, explicit tenant_id, no BYPASSRLS).
- **Do not touch**: object-storage tenant prefixing (feature-06).

## Definition of done

- [ ] Cross-tenant read test fails (rows invisible); migration check runs and passes in CI.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | tenant A cannot read tenant B rows | `tests/Raffa.Tenancy` |

## Open questions blocking this task

- OQ-sec-001 — assumption in force: RLS available on chosen SKU.

## Wave-spec entry

```yaml
- id: E01/F04/US03/T01
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-03-tenant-rls/tasks/task-01-tenant-rls.md
  produces: [tenant-rls]
  depends_on: [postgres-schema]
  effort: L
  layer: backend
  status: live
```
