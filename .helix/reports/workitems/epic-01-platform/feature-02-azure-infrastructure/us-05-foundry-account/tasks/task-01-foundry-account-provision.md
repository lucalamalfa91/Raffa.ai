---
id: E01/F02/US05/T01
type: task
story: us-05-foundry-account
wave: R0
status: live
target_repo: contigo-infra
# requires: [azure_subscription]
---

# task-01-foundry-account-provision — Provision Foundry hub + 2 projects + one AI services account

> Superseded 2026-09-09 by task-03: the account, the account-native projects,
> the model deployments and the RBAC are created by Terraform
> (`infra/modules/foundry`, ADR-008 amendment); there is no hub and no portal
> step. Kept for history.

## Coding objective

Provision the Azure AI Foundry account shape from ADR-008: a single Azure AI Foundry
**hub**, two **projects** `contigo-dev` and `contigo-demo` under the hub, and a single
pay-as-you-go Azure AI services account backing inference for both (no second AI
services subscription). Model deployments are recorded per project and are not
managed by Terraform in V1 — this task provisions the account/hub/project control
plane, Document Intelligence S0 (`prebuilt-read` / `prebuilt-layout`) on that same
account (ADR-017), and the managed-identity/keyvault connection material the AI Gateway will use
(ADR-008, ADR-011). Confirm Foundry + PAYG AI services + Document Intelligence availability in `westeurope`.

## Parent story AC covered

- AC-1 (single hub)
- AC-2 (two projects `contigo-dev`/`contigo-demo`)
- AC-3 (single PAYG AI services account)
- AC-4 (Document Intelligence S0 on that account, per-project connections)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/contigo-infra/modules/identity/outputs.tf | add Foundry managed-identity output |
| scripts/bootstrap_hcp_org.py | note/assert Foundry account (portal-recorded) as documentation |

## Context the implementer needs

- **Architecture decisions in force**: ADR-008 (one hub, two projects, one PAYG account); ADR-006 (`westeurope` availability check); ADR-017 (Document Intelligence S0 on the same account).
- **Do not touch**: chat/embed model IDs (ADR-004/CQ-008, later). OCR models are the Document Intelligence prebuilts named in ADR-017 (`prebuilt-read`, `prebuilt-layout`).

## Definition of done

- [ ] Bootstrap records hub + `contigo-dev` + `contigo-demo` + one AI services account id + Document Intelligence endpoint/connection per project.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| script | hub + 2 projects + one account present | `scripts/bootstrap_hcp_org.py` |

## Open questions blocking this task

- OQ-sec-002 (no-training endpoint) — assumption in force: gateway selects no-training endpoint; final model IDs later (ADR-004).

## Wave-spec entry

```yaml
- id: E01/F02/US05/T01
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-05-foundry-account/tasks/task-01-foundry-account-provision.md
  produces: [foundry-account]
  depends_on: [entra-registrations]
  effort: M
  layer: backend
  status: live
```
