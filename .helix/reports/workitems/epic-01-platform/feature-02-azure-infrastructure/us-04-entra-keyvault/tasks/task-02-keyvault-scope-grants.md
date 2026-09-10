---
id: E01/F02/US04/T02
type: task
story: us-04-entra-keyvault
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-keyvault-scope-grants — 02 Keyvault Scope Grants

## Coding objective
Grant each env API/worker managed identity only its own Key Vault.

## Parent story AC covered
- See parent story `us-04-entra-keyvault` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `keyvault-scope-grants` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-010, ADR-011.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `keyvault-scope-grants`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | keyvault-scope-grants behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F02/US04/T02
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-04-entra-keyvault/tasks/task-02-keyvault-scope-grants.md
  produces: [keyvault-scope-grants]
  depends_on: [entra-registrations, keyvaults]
  effort: M
  layer: backend
  status: live
```
