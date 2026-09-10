---
id: E09/F02/US01/T01
type: task
story: us-01-ci-apply
wave: 9
status: live
target_repo: raffa-infra
---

# task-01-terraform-connstrings — Savings + Quotes on Container Apps

## Coding objective

In `infra/modules/containerapps`, add env vars
`ConnectionStrings__Savings` and `ConnectionStrings__Quotes` (secret
`pg-cs`) on the API, and on the worker if it already mounts Renewals.
Do **not** `terraform apply` from the laptop. Point the operator at HCP
`raffa-dev` / `raffa-demo` VCS apply.

## Parent story AC covered

- AC-1

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/infra/modules/containerapps/main.tf | env blocks |

## Context the implementer needs

- **Architecture decisions in force**: ADR-007, ADR-011, ADR-021.

## Definition of done

- [ ] `terraform -chdir=infra/environments/dev validate` (backend=false) exits 0;
      env blocks present in module source.

## Wave-spec entry

```yaml
- {id: E09/F02/US01/T01, prompt: reports/workitems/epic-09-schema-apply/feature-02-apply-on-deploy/us-01-ci-apply/tasks/task-01-terraform-connstrings.md, produces: [schema-connstrings], depends_on: [schema-scripts], effort: M, layer: backend, status: live}
```
