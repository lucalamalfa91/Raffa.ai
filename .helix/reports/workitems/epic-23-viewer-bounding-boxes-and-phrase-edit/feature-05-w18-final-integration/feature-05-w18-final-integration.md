---
id: feature-05
type: feature
parent: epic-23
wave: w18
status: active
---

# feature-05-w18-final-integration — Wave w18 final integration + acceptance runbook

## Slice
The single-story, single-task wave integration that builds/tests the backend,
type-checks/lints/tests the web, runs the non-live e2e, sweeps the READMEs whose
public surface changed, and writes `docs/waves/w18-acceptance.md` (per item, the
manual `dev` check) — including the two ops walk steps (NW-40 HCP env + Foundry
RBAC; NW-41 deployed API serves seeded `market_record`).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | w18-final-integration | w18 |

## Architecture decisions in force
- ADR-016 — promotion is a HITL gate, never a `depends_on`; the acceptance walk is the operator's.
- ADR-005 — the HCP/Foundry/market walks are confirm-only, no Terraform change.

## Target repo
`raffa-backend` (integration + acceptance doc; walks touch no code)
