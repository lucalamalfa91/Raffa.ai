---
id: E01/F07/US01/T02
type: task
story: us-01-web-oidc-shell
wave: R0
status: live
target_repo: raffa-web
---

# task-02-web-api-client — 02 Web Api Client

## Coding objective
Generate TS API client from OpenAPI; wire /health.

## Parent story AC covered
- See parent story `us-01-web-oidc-shell` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/ | implementation for `web-api-client` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `web-api-client`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | web-api-client behaviour | workspace/raffa-web/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F07/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-07-web-client/us-01-web-oidc-shell/tasks/task-02-web-api-client.md
  produces: [web-api-client]
  depends_on: [web-client]
  effort: S
  layer: frontend
  status: live
```
