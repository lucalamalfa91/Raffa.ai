---
id: epic-26
type: epic
wave: w18
status: active
extends: [epic-13 F05, epic-01 F03, epic-10]
---

# epic-26-contract-and-ops-residuals — OpenAPI + ops + e2e residuals

## Business capability
Sweep the hand-authored OpenAPI/client residual (NW-30) and reconcile the
pre-V2 Day-1 Playwright spec against the V2 shell (NW-50). The two ops walks
(NW-40 HCP env vars + Foundry RBAC; NW-41 deployed API serving seeded
`market_record` rows) are runbook steps in this wave's final-integration task,
not code.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/w18-todo.md §2 | NW-30, NW-40, NW-41, NW-50 |
| ADR-012 / ADR-026 | hand-authored OpenAPI + generated client |
| ADR-016 / ADR-005 | promotion, HCP apply, environment variables |
| web/e2e/day1.spec.ts | pre-V2 spec vs V2 shell |

## Features
| ID | Title | Wave |
|----|------|------|
| feature-01 | openapi-sweep | w18 |
| feature-02 | e2e-reconcile | w18 |

## Success looks like
`raffa-api.v1.json` + `client.ts` carry no stale "no `GET /api/audit` / no
conversations requestBody" prose (both halves are CLOSED-ON-MAIN, recorded);
`web/e2e/day1.spec.ts` matches the real V2 shell with no unexplained skip; and
the two ops walks are recorded in `docs/waves/w18-acceptance.md`.

## Architecture decisions in force
- ADR-012 / ADR-026 — the conversations requestBody is a documented generator limitation; bodies are hand-written in `client.ts`.
- ADR-016 — the e2e spec runs under vitest, never CI; a walk needs live `demo`.
- ADR-005 / ADR-016 — the HCP/Foundry/market walks are operator steps, not Terraform changes.

## Out of scope
- No new Terraform, SKU or env key (NW-40/41 are confirm-only walks).
- No CI workflow change (ADR-016: Playwright is a runbook spec, not CI).
