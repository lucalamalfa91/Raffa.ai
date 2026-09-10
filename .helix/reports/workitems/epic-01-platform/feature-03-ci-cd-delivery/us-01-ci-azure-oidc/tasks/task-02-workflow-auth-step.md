---
id: E01/F03/US01/T02
type: task
story: us-01-ci-azure-oidc
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-workflow-auth-step — 02 Workflow Auth Step

## Coding objective
Author a reusable azure/login OIDC step (no secret, only client/tenant/sub).

## Parent story AC covered
- See parent story `us-01-ci-azure-oidc` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `ci-workflow-auth` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-015.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `ci-workflow-auth`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | ci-workflow-auth behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F03/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-01-ci-azure-oidc/tasks/task-02-workflow-auth-step.md
  produces: [ci-workflow-auth]
  depends_on: [ci-azure-auth]
  effort: S
  layer: backend
  status: live
```
