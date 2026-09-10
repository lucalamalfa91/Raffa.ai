---
id: E01/F02/US02/T01
type: task
story: us-02-dev-environment
wave: R0
status: live
target_repo: raffa-infra
# requires: [azure_subscription]
# requires: [hcp_terraform]
---

# task-01-dev-environment-provision — Instantiate `dev` modules (env=dev) in westeurope

## Coding objective

Instantiate the module library for the `dev` environment in West Europe: Container
Apps Environment (consumption, workload profile, min instances = 0) plus two
Container Apps (API and worker), PostgreSQL Flexible Server Burstable `Standard_B1ms`
with the `pgvector` extension enabled, a Storage Account (GPv2 LRS, blob + queue), a
Service Bus Standard namespace, a Key Vault Standard, an Azure Container Registry
(Basic), and a Log Analytics workspace with a data cap (ADR-005). Tag every resource
`project=raffa`, `env=dev`, and pin `location = "West Europe"` (ADR-006). Remote
state writes to HCP `raffa-dev` (ADR-007). No secret in source.

## Parent story AC covered

- AC-1 (full `dev` service set incl. pgvector + scale-to-zero + Log Analytics cap)
- AC-2 (tags + region)
- AC-3 (dev resource group + HCP `raffa-dev` state)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/environments/dev/main.tf | wire module instances with env=dev |
| workspace/raffa-infra/environments/dev/variables.tf | dev variables (location, env) |
| workspace/raffa-infra/environments/dev/outputs.tf | dev outputs (resource ids, endpoints) |

## Context the implementer needs

- **Architecture decisions in force**: ADR-005 (SKUs), ADR-006 (`westeurope`), ADR-007 (env root), ADR-003 (`pgvector` enabled).
- **Do not touch**: `demo` env root; no data-plane replication.

## Definition of done

- [ ] `terraform plan` for `environments/dev` exits 0 and lists the required resources.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| plan | dev resources + tags + pgvector + min0 present | `environments/dev` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F02/US02/T01
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-02-dev-environment/tasks/task-01-dev-environment-provision.md
  produces: [azure-dev-environment]
  depends_on: [terraform-module-library]
  effort: L
  layer: backend
  status: live
```
