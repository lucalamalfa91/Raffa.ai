---
id: E01/F02/US01/T01
type: task
story: us-01-terraform-module-library
wave: R0
status: live
target_repo: raffa-infra
---

# task-01-terraform-module-library — Scaffold modules + version pins + env roots

## Coding objective

Create the Terraform layout from ADR-007 under `infra/`: a `modules/` directory
with `network`, `identity`, `postgres`, `storage`, `servicebus`, `containerapps`,
`keyvault`, `acr`, `monitor`; a top-level `versions.tf` pinning `hashicorp/azurerm`,
`hashicorp/azuread`, `hashicorp/random` and the Terraform core version; and two thin
environment roots `infra/environments/dev` and `infra/environments/demo` each with
`main.tf`, `backend.tf` (remote → HCP workspace `raffa-dev`/`raffa-demo`),
`variables.tf`, `outputs.tf`. Every module resource must apply `project = "raffa"`
and `env = var.environment`. No secret may appear in Terraform source (ADR-007).

## Parent story AC covered

- AC-1 (nine modules present)
- AC-2 (versions pinned)
- AC-3 (tagging)
- AC-4 (two env roots + separate backend.tf)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/versions.tf | pin providers + Terraform version |
| workspace/raffa-infra/provider.tf | azurerm + azuread providers |
| workspace/raffa-infra/modules/*/main.tf | one module stub each (resource skeleton) |
| workspace/raffa-infra/modules/*/variables.tf | per-module vars incl. `environment` |
| workspace/raffa-infra/environments/dev/main.tf | instantiate modules env=dev |
| workspace/raffa-infra/environments/dev/backend.tf | remote → HCP `raffa-dev` |
| workspace/raffa-infra/environments/demo/main.tf | instantiate modules env=demo |
| workspace/raffa-infra/environments/demo/backend.tf | remote → HCP `raffa-demo` |

## Context the implementer needs

- **Architecture decisions in force**: ADR-007 (modules + two env roots + remote state, no secrets); ADR-005 (service set); ADR-006 (`location = "West Europe"`).
- **Do not touch**: application code under `backend/`, `web/`, `mobile/`.

## Definition of done

- [ ] `terraform fmt -check` in `infra/` exits 0.
- [ ] `terraform validate` exits 0 for both `environments/dev` and `environments/demo` (init against HCP backend).

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| structural | nine modules + two env roots + pins + tagging present | `infra/` tree |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F02/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-01-terraform-module-library/tasks/task-01-terraform-module-library.md
  produces: [terraform-module-library]
  depends_on: [github-org-repos, hcp-terraform-workspaces]
  effort: L
  layer: backend
  status: live
```
