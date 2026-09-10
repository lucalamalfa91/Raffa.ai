---
id: E01/F02/US05/T02
type: task
story: us-05-foundry-account
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-foundry-connection-verify — 02 Foundry Connection Verify

## Coding objective
Verify Foundry hub/projects availability in westeurope; record connection ids.

## Parent story AC covered
- See parent story `us-05-foundry-account` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `foundry-connection` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-008, ADR-006.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `foundry-connection`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | foundry-connection behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F02/US05/T02
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-05-foundry-account/tasks/task-02-foundry-connection-verify.md
  produces: [foundry-connection]
  depends_on: [foundry-account]
  effort: S
  layer: backend
  status: live
```
