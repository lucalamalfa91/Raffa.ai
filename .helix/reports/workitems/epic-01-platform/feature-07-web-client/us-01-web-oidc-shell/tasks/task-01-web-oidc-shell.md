---
id: E01/F07/US01/T01
type: task
story: us-01-web-oidc-shell
wave: R0
status: live
target_repo: raffa-web
---

# task-01-web-oidc-shell — 01 Web Oidc Shell

## Coding objective
Scaffold React+TS+Vite SPA with OIDC PKCE + config injection.

## Parent story AC covered
- See parent story `us-01-web-oidc-shell` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/ | implementation for `web-client` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012, ADR-010.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `web-client`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | web-client behaviour | workspace/raffa-web/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F07/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-07-web-client/us-01-web-oidc-shell/tasks/task-01-web-oidc-shell.md
  produces: [web-client]
  depends_on: [deployable-api, workspace-roles]
  effort: M
  layer: frontend
  status: live
```
