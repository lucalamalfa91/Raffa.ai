---
id: E26/F01/US01/T01
type: task
story: us-01-openapi-sweep
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-openapi-sweep — Sweep stale OpenAPI/client gap prose (NW-30)

## Coding objective
Sweep the two stale claims in the hand-authored contract without changing a
shape: (a) `web/openapi/raffa-api.v1.json` — remove/adjust any prose implying
`GET /api/audit` is absent (it is CLOSED-ON-MAIN via w16 NW-08,
`raffa-api.v1.json:6486`, `Program.cs:377`) and record the conversations
`requestBody` as a documented generator limitation (the generator parses only
`responses`; bodies are hand-written in `client.ts`, as noted at
`raffa-api.v1.json:6`, conversations `:5339`, `:5680`); (b)
`web/src/api/client.ts` — sweep the same stale prose; confirm the hand-written
conversation bodies are present and correct (no re-wrap, no new method).
This is a documentation sweep only; no endpoint, wrapper or schema change.

## Parent story AC covered
- AC-1 OpenAPI no longer claims audit absent; records requestBody limitation.
- AC-2 client prose swept of stale claims.
- AC-3 no new wrapper / shape change.

## Files to create or modify
| Path | Change |
|------|--------|
| web/openapi/raffa-api.v1.json | sweep stale audit/requestBody prose |
| web/src/api/client.ts | sweep stale prose; confirm hand-written bodies |

## Context the implementer needs

**Closes: NW-30**

- **Architecture decisions in force**: ADR-012 / ADR-026 (the requestBody is a documented generator limitation).
- **Do not touch**: schema.ts (no shape change); any endpoint.

## Definition of done
- [ ] `grep` over `web/openapi/raffa-api.v1.json` and `web/src/api/client.ts` returns no stale "no `GET /api/audit`" claim
- [ ] `dotnet build backend/Raffa.slnx` exits 0 (no code change expected; sanity)
- [ ] `npm run typecheck` exits 0 (client prose change is comment-only)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| (grep) | no stale prose survives | `web/openapi/raffa-api.v1.json`, `web/src/api/client.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E26/F01/US01/T01
  prompt: reports/workitems/epic-26-contract-and-ops-residuals/feature-01-openapi-sweep/us-01-openapi-sweep/tasks/task-01-openapi-sweep.md
  produces: [openapi-swept]
  depends_on: []
  effort: S
  layer: backend
  status: live
```
