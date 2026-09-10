---
id: E02/F01/US01/T02
type: task
story: us-01-ai-gateway-classification
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-ai-gateway-logging — 02 Ai Gateway Logging

## Coding objective
Log model/version/prompt/timestamp/input-hash; no-training config.

## Parent story AC covered
- See parent story `us-01-ai-gateway-classification` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `ai-gateway-logging` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-011, ADR-004.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `ai-gateway-logging`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | ai-gateway-logging behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F01/US01/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-01-extraction-pipeline/us-01-ai-gateway-classification/tasks/task-02-ai-gateway-logging.md
  produces: [ai-gateway-logging]
  depends_on: [ai-gateway-roles]
  effort: S
  layer: backend
  status: live
```
