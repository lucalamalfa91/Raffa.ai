---
id: E06/F01/US01/T01
type: task
story: us-01-typescript-client-regen
wave: 6
status: live
target_repo: raffa-web
---

# task-01-typescript-client-regen — Regen TS client from web/openapi

## Coding objective
Regenerate the single TypeScript API client from `web/openapi/` so it reflects
the post-E02–E05 contract.

## Parent story AC covered
- AC-1 (regen from `web/openapi/`), AC-2 (no divergent DTOs), AC-3 (build succeeds).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/api/ | regenerated client types |
| workspace/raffa-web/openapi/ | contract source (read) |

## Context the implementer needs
- **Claude Design**: not applicable (this is a chore, not a screen).
- **Architecture decisions in force**: ADR-012 (one generated TS client), ADR-014 (config-not-code).
- **Do not touch**: `wave-spec.execution.yaml`, `slices/e01.yaml`–`e05.yaml`, `slice.current.yaml`.

## Definition of done
- [ ] `npm run build` exits 0; generated client type-checks against E02–E05 endpoints.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| build | regenerated client compiles | workspace/raffa-web |

## Wave-spec entry
```yaml
- id: E06/F01/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-01-typescript-client-regen/us-01-typescript-client-regen/tasks/task-01-typescript-client-regen.md
  produces: [web-api-client-current]
  depends_on: []
  effort: S
  layer: web
  status: live
```
