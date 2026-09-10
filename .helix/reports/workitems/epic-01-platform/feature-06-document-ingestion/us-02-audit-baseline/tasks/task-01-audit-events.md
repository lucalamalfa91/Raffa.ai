---
id: E01/F06/US02/T01
type: task
story: us-02-audit-baseline
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-audit-events — 01 Audit Events

## Coding objective
Implement append-only audit abstraction every module writes to.

## Parent story AC covered
- See parent story `us-02-audit-baseline` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `audit-abstraction` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `audit-abstraction`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | audit-abstraction behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F06/US02/T01
  prompt: reports/workitems/epic-01-platform/feature-06-document-ingestion/us-02-audit-baseline/tasks/task-01-audit-events.md
  produces: [audit-abstraction]
  depends_on: [workspace-roles]
  effort: M
  layer: backend
  status: live
```
