---
id: E01/F02/US02/T02
type: task
story: us-02-dev-environment
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-dev-outputs-verify — 02 Dev Outputs Verify

## Coding objective
Verify dev Terraform outputs expose resource ids/endpoints and tags applied.

## Parent story AC covered
- See parent story `us-02-dev-environment` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `dev-outputs-verified` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-005, ADR-006.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `dev-outputs-verified`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | dev-outputs-verified behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F02/US02/T02
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-02-dev-environment/tasks/task-02-dev-outputs-verify.md
  produces: [dev-outputs-verified]
  depends_on: [azure-dev-environment]
  effort: S
  layer: backend
  status: live
```
