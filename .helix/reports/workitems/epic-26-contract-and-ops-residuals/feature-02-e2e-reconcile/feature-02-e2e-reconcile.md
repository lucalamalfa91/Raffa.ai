---
id: feature-02
type: feature
parent: epic-26
wave: w18
status: active
---

# feature-02-e2e-reconcile — Day-1 Playwright spec reconciled to the V2 shell (NW-50)

## Slice
Reconcile the pre-V2 `web/e2e/day1.spec.ts` to the V2 shell: remove the stale
Ask-is-home / pre-V2 assumptions, keep every step real, and run under vitest
(never CI) with no unexplained skip.

## User stories
| ID | Title | Wave |
|----|------|------|
| us-01 | e2e-reconcile | w18 |

## Architecture decisions in force
- ADR-016 — the Day-1 walk is a runbook spec, never CI; ADR-012 — the V2 shell is the target.

## Target repo
`raffa-web`
