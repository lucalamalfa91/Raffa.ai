---
id: E01/F03/US03/T02
type: task
story: us-03-promotion-dev-demo
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-demo-environment-reviewers — 02 Demo Environment Reviewers

## Coding objective
Document and lock the demo GitHub Environment required reviewers.

## Parent story AC covered
- See parent story `us-03-promotion-dev-demo` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `demo-reviewers` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `demo-reviewers`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | demo-reviewers behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F03/US03/T02
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-03-promotion-dev-demo/tasks/task-02-demo-environment-reviewers.md
  produces: [demo-reviewers]
  depends_on: [demo-promotion]
  effort: S
  layer: backend
  status: live
```
