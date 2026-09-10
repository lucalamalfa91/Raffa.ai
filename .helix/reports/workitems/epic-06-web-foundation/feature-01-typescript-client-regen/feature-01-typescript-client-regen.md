---
id: feature-01
type: feature
parent: epic-06
wave: 6
status: active
---

# feature-01-typescript-client-regen — TypeScript client regen (chore)

## Slice

Regenerate the single TypeScript API client from `web/openapi/` so it reflects
the OpenAPI contract after E02–E05 backend growth. Never hand-write divergent
DTOs.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Regenerate TS API client | 6 |

## Architecture decisions in force

- ADR-012 (one generated TS client), ADR-014 (config-not-code).

## Target repo

`raffa-web`
