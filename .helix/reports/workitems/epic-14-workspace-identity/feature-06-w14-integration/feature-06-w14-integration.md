---
id: feature-06
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-13 F11 (V2 wave-close integration)
---

# feature-06-w14-integration — the wave is not done until a human can run it on `dev`

## Slice

Extends **epic-13 F11**, the precedent for this wave-close duty
(`reports/execution/wave-close-e13.md:29`). One task, last phase, depending on
every leaf artifact: build and test both sides, assert the wave changed exactly
two workflow files and no Terraform, add the two-account invitation e2e
(authored and skipped with a named reason until the second Entra account
exists), and write the operator-runnable acceptance document for **deployed
`dev`** — the artefact that catches the class of defect e13's own integration
task found while writing the V2 runbook (a missing
`ConnectionStrings__Suppliers` that crashed the live API on boot,
`infra/modules/containerapps/main.tf:117-121`).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Final integration and the w14 acceptance runbook | w14 |

## Architecture decisions in force

- ADR-014 (w14 footer) — wave base and integration branch; the green-base proof; the no-unplanned-CI-YAML rule
- ADR-016 (w14 footer) — promotion ordering is a **HITL gate, never a `depends_on`**; seeds and backfills are data-plane acts and are never promoted; the wave does **not** tag itself
- ADR-025 §H — the test set the wave must carry
- ADR-024 / ADR-020 — the acceptance-doc shape (`docs/ask-v2-acceptance.md` is the precedent)

## Target repo

`raffa-backend` (this monorepo; the task touches `docs/`, `web/e2e/` and no
application source)
