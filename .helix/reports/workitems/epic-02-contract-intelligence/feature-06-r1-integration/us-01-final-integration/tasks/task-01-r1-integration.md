---
id: E02/F06/US01/T01
type: task
story: us-01-final-integration
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-r1-integration — 01 R1 Integration

## Coding objective
Prove R1 end-to-end: upload->extract->portfolio->360->Ask Raffa->correction.

## Parent story AC covered
- See parent story `us-01-final-integration` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `r1-integration` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003, ADR-004, ADR-009, ADR-011, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `r1-integration`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | r1-integration behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F06/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-06-r1-integration/us-01-final-integration/tasks/task-01-r1-integration.md
  produces: [r1-integration]
  depends_on: [hybrid-ocr, contract-evidence-schema, tenant-retrieval, portfolio-filters, contract-360, deterministic-queries, abstain-guard, correction-audit]
  effort: L
  layer: backend
  status: live
```
