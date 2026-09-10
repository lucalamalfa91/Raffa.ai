---
id: E02/F01/US01/T01
type: task
story: us-01-ai-gateway-classification
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-ai-gateway-roles — 01 Ai Gateway Roles

## Coding objective
Implement IAiGateway classify/extract/embed/answer + config-selected IDs.

## Parent story AC covered
- See parent story `us-01-ai-gateway-classification` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `ai-gateway-roles` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-004, ADR-011.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `ai-gateway-roles`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | ai-gateway-roles behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F01/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-01-extraction-pipeline/us-01-ai-gateway-classification/tasks/task-01-ai-gateway-roles.md
  produces: [ai-gateway-roles]
  depends_on: [deployable-worker]
  effort: M
  layer: backend
  status: live
```
