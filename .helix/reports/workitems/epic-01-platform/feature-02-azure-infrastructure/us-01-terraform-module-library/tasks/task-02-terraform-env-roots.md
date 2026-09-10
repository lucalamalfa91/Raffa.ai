---
id: E01/F02/US01/T02
type: task
story: us-01-terraform-module-library
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-terraform-env-roots — 02 Terraform Env Roots

## Coding objective
Create the two env roots dev/demo with backend.tf pointing at HCP workspaces.

## Parent story AC covered
- See parent story `us-01-terraform-module-library` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `terraform-env-roots` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-007, ADR-005, ADR-006.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `terraform-env-roots`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | terraform-env-roots behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F02/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-01-terraform-module-library/tasks/task-02-terraform-env-roots.md
  produces: [terraform-env-roots]
  depends_on: [terraform-module-library]
  effort: M
  layer: backend
  status: live
```
