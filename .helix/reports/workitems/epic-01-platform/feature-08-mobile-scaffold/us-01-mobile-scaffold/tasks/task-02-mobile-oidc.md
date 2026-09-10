---
id: E01/F08/US01/T02
type: task
story: us-01-mobile-scaffold
wave: R0
status: live
target_repo: raffa-mobile
---

# task-02-mobile-oidc — 02 Mobile Oidc

## Coding objective
Configure OIDC PKCE vs Entra with native redirect scheme.

## Parent story AC covered
- See parent story `us-01-mobile-scaffold` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-mobile/src/ | implementation for `mobile-oidc` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-013, ADR-010.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `mobile-oidc`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | mobile-oidc behaviour | workspace/raffa-mobile/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F08/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-08-mobile-scaffold/us-01-mobile-scaffold/tasks/task-02-mobile-oidc.md
  produces: [mobile-oidc]
  depends_on: [mobile-scaffold]
  effort: S
  layer: frontend
  status: live
```
