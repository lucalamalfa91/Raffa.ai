---
id: E01/F02/US03/T01
type: task
story: us-03-demo-environment
wave: R0
status: live
target_repo: raffa-infra
# requires: [azure_subscription]
# requires: [hcp_terraform]
---

# task-01-demo-environment-provision — Instantiate `demo` modules (env=demo), isolated

## Coding objective

Instantiate the same module library for the `demo` environment in `westeurope`,
fully isolated from `dev`: its own resource group, Container Apps Environment +
API/worker apps (consumption, min 0), PostgreSQL Flexible Server `Standard_B1ms`
with `pgvector`, Storage Account (blob+queue), Service Bus Standard, Key Vault
Standard, ACR Basic, Log Analytics with data cap. Tag `project=raffa`,
`env=demo`, `location=West Europe` (ADR-005, ADR-006). Remote state →
HCP `raffa-demo` (ADR-007). Assert no shared Postgres / Storage / Service Bus
with `dev` (ADR-016 isolation, ADR-001).

## Parent story AC covered

- AC-1 (same service set)
- AC-2 (tags + region)
- AC-3 (isolated RG + `raffa-demo` state, no shared store)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/environments/demo/main.tf | wire module instances env=demo |
| workspace/raffa-infra/environments/demo/variables.tf | demo variables |
| workspace/raffa-infra/environments/demo/outputs.tf | demo outputs |

## Context the implementer needs

- **Architecture decisions in force**: ADR-005, ADR-006 (`westeurope`), ADR-007, ADR-003, ADR-016 (no data-plane sharing).
- **Do not touch**: `dev` env root.

## Definition of done

- [ ] `terraform plan` for `environments/demo` exits 0 and lists required resources.
- [ ] Isolation check: distinct resource group + `raffa-demo` state, no shared store ids.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| plan | demo resources + tags + isolation | `environments/demo` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F02/US03/T01
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-03-demo-environment/tasks/task-01-demo-environment-provision.md
  produces: [azure-demo-environment]
  depends_on: [terraform-module-library]
  effort: L
  layer: backend
  status: live
```
