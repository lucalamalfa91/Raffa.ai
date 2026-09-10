---
id: E01/F06/US02/T02
type: task
story: us-02-audit-baseline
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-audit-query — 02 Audit Query

## Coding objective
Implement authorized GET /api/audit with tenant scoping.

## Parent story AC covered
- See parent story `us-02-audit-baseline` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `audit-query` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `audit-query`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | audit-query behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F06/US02/T02
  prompt: reports/workitems/epic-01-platform/feature-06-document-ingestion/us-02-audit-baseline/tasks/task-02-audit-query.md
  produces: [audit-query]
  depends_on: [audit-abstraction]
  effort: S
  layer: backend
  status: live
```
