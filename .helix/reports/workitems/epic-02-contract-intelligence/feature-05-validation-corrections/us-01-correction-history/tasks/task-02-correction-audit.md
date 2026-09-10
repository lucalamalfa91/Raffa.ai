---
id: E02/F05/US01/T02
type: task
story: us-01-correction-history
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-correction-audit — 02 Correction Audit

## Coding objective
Emit audit event on correction; correction history query.

## Parent story AC covered
- See parent story `us-01-correction-history` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `correction-audit` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-011.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `correction-audit`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | correction-audit behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F05/US01/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-05-validation-corrections/us-01-correction-history/tasks/task-02-correction-audit.md
  produces: [correction-audit]
  depends_on: [correction-history, audit-abstraction]
  effort: S
  layer: backend
  status: live
```
