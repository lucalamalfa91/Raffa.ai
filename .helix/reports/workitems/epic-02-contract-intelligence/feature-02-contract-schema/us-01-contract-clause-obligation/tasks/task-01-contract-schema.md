---
id: E02/F02/US01/T01
type: task
story: us-01-contract-clause-obligation
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-contract-schema — 01 Contract Schema

## Coding objective
Add Contract/LineItem/Clause/Obligation/Risk/CorrectionHistory + migrations.

## Parent story AC covered
- See parent story `us-01-contract-clause-obligation` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `contract-schema` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `contract-schema`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | contract-schema behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F02/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-02-contract-schema/us-01-contract-clause-obligation/tasks/task-01-contract-schema.md
  produces: [contract-schema]
  depends_on: [postgres-schema]
  effort: L
  layer: backend
  status: live
```
