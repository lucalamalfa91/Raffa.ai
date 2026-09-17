---
id: feature-05
type: feature
parent: epic-32
wave: w19
status: active
---

# feature-05-q1-follow-up-todos — Q1 follow-up "contrattare su X" writes TODOs (NW-90)

## Slice
A Q1 follow-up ("su quali punti posso contrattare sul supplier X") resolves X
(NW-80), narrates the top 3 grounded points, persists **all** to TODOs
(`persistTodos=true`), and injects `/renewals?select={id}`; locked rows never
get a 2026-save TODO. Queued (W20).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | q1-follow-up-todos | w20 |

## Architecture decisions in force
- ADR-024 w19 (cl. 21) + lock 7/9.

## Target repo
`raffa-backend`
