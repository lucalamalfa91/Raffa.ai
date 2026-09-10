---
id: E02/F04/US02/T02
type: task
story: us-02-rag-citations
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-abstain-guard — 02 Abstain Guard

## Coding objective
No-fabrication guard returning cannot-determine; auth-before-retrieval.

## Parent story AC covered
- See parent story `us-02-rag-citations` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `abstain-guard` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-004.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `abstain-guard`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | abstain-guard behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F04/US02/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-02-rag-citations/tasks/task-02-abstain-guard.md
  produces: [abstain-guard]
  depends_on: [rag-citations]
  effort: M
  layer: backend
  status: live
```
