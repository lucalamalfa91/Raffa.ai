---
id: feature-04
type: feature
parent: epic-31
wave: w19
status: active
---

# feature-04-w19-final-integration — Wave w19 integration + acceptance runbook

## Slice
Single final-integration task: build/test backend, type-check/lint/test web,
run the non-live golden Q1/Q2/Q3 cases, sweep READMEs, and write
`docs/waves/w19-acceptance.md` (per item, the manual `dev` check for the three
wow questions).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | w19-final-integration | w19 |

## Architecture decisions in force
- ADR-016 — the acceptance walk is recorded, never dispatched; promotion is the operator's HITL gate.

## Target repo
`raffa-backend` + `raffa-web` (integration + acceptance doc)
