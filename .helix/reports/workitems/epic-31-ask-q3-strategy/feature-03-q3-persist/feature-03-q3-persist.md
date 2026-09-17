---
id: feature-03
type: feature
parent: epic-31
wave: w19
status: active
---

# feature-03-q3-persist — Q3 persist + Renewals deep-link (NW-97)

## Slice
After rank, upsert all ranked todos; the `answer` carries top-3 markdown +
citations + a server-injected `/renewals?select={guid}` action (never model
`actionKeys`). Re-ask is idempotent (no duplicate Open, Done preserved).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | q3-persist | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 21) + ADR-028 + ADR-012 (cl. 53/55) + ADR-020 (40).

## Target repo
`raffa-backend` + `raffa-web`
