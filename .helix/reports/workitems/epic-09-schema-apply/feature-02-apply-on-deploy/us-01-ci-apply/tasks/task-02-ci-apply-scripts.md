---
id: E09/F02/US01/T02
type: task
story: us-01-ci-apply
wave: 9
status: live
target_repo: contigo-infra
---

# task-02-ci-apply-scripts — Apply SQL on deploy

## Coding objective

After both `az containerapp update` steps in `.github/workflows/backend.yml`,
apply the six idempotent scripts in ADR-021 order against the env Postgres
using Key Vault `postgres-connection` (OIDC already on the job). Prove
`contigo_<env>` has the app tables (or fail the job). Reuse on demo via
existing `workflow_call`. No Swagger. No `MigrateAsync` in the API.

## Parent story AC covered

- AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/contigo-infra/.github/workflows/backend.yml | apply step |
| workspace/contigo-infra/scripts/ | optional apply helper |

## Context the implementer needs

- **Architecture decisions in force**: ADR-014, ADR-015, ADR-016, ADR-021.
- Firewall today is AllowAzureServices only.

## Definition of done

- [ ] A `dev` deploy run applies scripts; a named check lists expected
      tables (or `__EFMigrationsHistory` rows) on `contigo_dev`.

## Wave-spec entry

```yaml
- {id: E09/F02/US01/T02, prompt: reports/workitems/epic-09-schema-apply/feature-02-apply-on-deploy/us-01-ci-apply/tasks/task-02-ci-apply-scripts.md, produces: [schema-applied], depends_on: [schema-connstrings], effort: L, layer: backend, status: live}
```
