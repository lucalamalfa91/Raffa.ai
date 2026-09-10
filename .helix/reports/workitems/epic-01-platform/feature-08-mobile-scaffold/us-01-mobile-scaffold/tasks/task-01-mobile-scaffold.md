---
id: E01/F08/US01/T01
type: task
story: us-01-mobile-scaffold
wave: R0
status: live
target_repo: raffa-mobile
---

# task-01-mobile-scaffold — 01 Mobile Scaffold

## Coding objective
Scaffold React Native (Expo) + TypeScript app (non-blocking).

## Parent story AC covered
- See parent story `us-01-mobile-scaffold` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-mobile/src/ | implementation for `mobile-scaffold` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-013.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `mobile-scaffold`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | mobile-scaffold behaviour | workspace/raffa-mobile/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F08/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-08-mobile-scaffold/us-01-mobile-scaffold/tasks/task-01-mobile-scaffold.md
  produces: [mobile-scaffold]
  depends_on: [deployable-api]
  effort: M
  layer: frontend
  status: live
```
