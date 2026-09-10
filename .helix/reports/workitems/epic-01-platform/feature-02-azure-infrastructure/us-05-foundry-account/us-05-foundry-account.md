---
id: us-05
type: user-story
parent: feature-02
wave: R0
status: active
---

# us-05-foundry-account — Azure AI Services account + two Foundry projects

## Story

As a **cloud engineer**, I want one shared Azure AI Services account with two
account-native Foundry projects (`raffa-dev`, `raffa-demo`) and per-environment
model deployments, so that `dev`/`demo` model content is logically isolated
without double billing.

## Acceptance criteria

- [ ] AC-1 A single Azure AI Services account `aisvc-raffa` (kind `AIServices`, `project_management_enabled`, keys disabled) exists in `rg-raffa-ai`; no hub (ADR-008 amendment 2026-09-09 superseded the hub).
- [ ] AC-2 Two Foundry projects `raffa-dev` and `raffa-demo` are created by Terraform as sub-resources of that account; model deployments are per environment (`<model>-<env>`, pinned versions).
- [ ] AC-3 A single pay-as-you-go Azure AI services account backs both (no second subscription).
- [ ] AC-4 Document Intelligence (`prebuilt-read`) is native to that account and bound as the `ocr` role; `conn-docint-raffa-<env>` is an informational value (ADR-017 amendment 2026-09-09).

## Definition of done

- [ ] Terraform (`infra/modules/foundry`) declares the account, both projects, the deployments and the RBAC; `scripts/foundry_connection_verify.py` proves the shape; usage attributable per project.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-04 (feature-02) | gateway authenticates to Foundry via managed identity/Key Vault |

## Architecture decisions in force

- ADR-008 (one PAYG AI services account, one project per environment; amended 2026-09-09: Terraform-managed, account-native projects, no hub).
- ADR-017 (OCR in V1: Document Intelligence on the same account; amended 2026-09-09: Read for every PDF and image).
- ADR-004 (amended 2026-09-09: confirmed per-environment model deployments).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Provision Foundry hub + two projects + one AI services account (superseded 2026-09-09 by task-03) | M | phase-4 |
| task-02 | Verify Foundry connection shape (`scripts/foundry_connection_verify.py`) | S | phase-4 |
| task-03 | Create the account, projects, deployments and RBAC in Terraform; two-phase wiring (`ai_gateway_wired`) | M | 2026-09-09 |

## Council decisions carried into this story

One hub, projects `raffa-dev`/`raffa-demo`, single PAYG Azure AI services account including Document Intelligence S0 for V1 OCR (ADR-017). Chat/embed model IDs confirmed later (ADR-004 / CQ-008).

## Open questions

- none
