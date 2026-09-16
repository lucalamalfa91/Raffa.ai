---
id: feature-01
type: feature
parent: epic-26
wave: w18
status: active
---

# feature-01-openapi-sweep — Hand-authored OpenAPI/client prose sweep (NW-30)

## Slice
Sweep the stale prose in `raffa-api.v1.json` and `client.ts` that claimed no
`GET /api/audit` and no conversations `requestBody` exist: both halves are
CLOSED-ON-MAIN. A prose sweep, no new wrapper and no contract shape change.

## User stories
| ID | Title | Wave |
|----|------|------|
| us-01 | openapi-sweep | w18 |

## Architecture decisions in force
- ADR-012 / ADR-026 — the conversations requestBody is a documented generator limitation; bodies are hand-written in `client.ts`.

## Target repo
`raffa-backend` + `raffa-web` (mixed — docs + client prose)
