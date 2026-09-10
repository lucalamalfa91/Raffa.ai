---
id: E01/F03/US01/T01
type: task
story: us-01-ci-azure-oidc
wave: R0
status: live
target_repo: raffa-infra
# requires: [azure_subscription]
---

# task-01-ci-azure-oidc — Create 2 service principals + OIDC federation + workflow auth

## Coding objective

Create two least-privilege service principals `raffa-sp-dev` and
`raffa-sp-demo` (ADR-015) with role assignments scoped to the `dev` and `demo`
resource groups respectively. Configure GitHub → Entra OIDC federated credentials
with subject claims pinned to `repo:lucalamalfa91/raffa:*` (and the `demo` environment
claim for `demo`). Author a reusable GitHub Actions auth step using
`azure/login` with only `client-id`, `tenant-id`, `subscription-id` — never a
client secret or `AZURE_CREDENTIALS`. Record the federation config as Terraform
output so it is reproducible.

## Parent story AC covered

- AC-1 (two SPs, least privilege)
- AC-2 (OIDC federation, no stored secret)
- AC-3 (workflow non-secret fields only)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/modules/identity/main.tf | SPs + federation subject claims |
| workspace/raffa-infra/modules/identity/outputs.tf | sp client-id/tenant-id/subscription |
| workspace/raffa-infra/.github/actions/azure-login/action.yml | OIDC `azure/login` step |

## Context the implementer needs

- **Architecture decisions in force**: ADR-015 (OIDC federation, per-env SP, no secret).
- **Do not touch**: `demo` promotion workflow (us-03).

## Definition of done

- [ ] `azure/login` via federation succeeds for both envs without a stored secret.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| workflow | federation login for dev/demo, no secret fields | `.github/actions/azure-login` |

## Open questions blocking this task

- OQ-DM-003 — assumption in force: federation permitted; else short-lived SP cert in Key Vault.

## Wave-spec entry

```yaml
- id: E01/F03/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-01-ci-azure-oidc/tasks/task-01-ci-azure-oidc.md
  produces: [ci-azure-auth]
  depends_on: [entra-registrations]
  effort: L
  layer: backend
  status: live
```
