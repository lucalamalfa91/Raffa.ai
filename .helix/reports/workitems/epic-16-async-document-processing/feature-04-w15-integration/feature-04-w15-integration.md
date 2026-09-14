---
id: feature-04
type: feature
parent: epic-16
wave: w15
status: active
---

# feature-04-w15-integration — w15 integration and acceptance runbook

## Slice

The wave's proof. One task that builds and tests both trees end to end, sweeps
the READMEs whose public surface changed, and writes
`docs/waves/w15-acceptance.md` — the operator's runbook of what to click on
`dev` and what to expect, per item, taken from the acceptance criteria. Until
this task passes, the wave PR is not green and the wave is not done.

It also records **W15-A1**, the wave base proof, exactly as `E14/F06/US01/T01`
recorded W14-A1: the base is an operator act at HITL with no fan-out task, so the
only place its result can live is here.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The wave builds, tests and has a runbook the operator can walk | w15 |

## Architecture decisions in force

- **ADR-016** (w15 clauses 17, 19, 22–24) — the acceptance doc and its known-gaps table belong to this task; the post-deploy revision-state assertion; the promotion sequence; the dead-letter queue as a standing condition to be read.
- **ADR-014** (w15 clauses 1–7) — W15-A1's six points, including the zero product-tree delta and the pre-wave token check.
- **ADR-025 §J.9 / ADR-016 clause 20** — N3b is a numbered step walked by hand.
- **`readme-hygiene`** — README sweeps are standing implementer scope; this task closes the ones this wave's public surface changed.

## Target repo

mixed — it verifies `backend/`, `web/` and `infra/`, and writes under `docs/`.
