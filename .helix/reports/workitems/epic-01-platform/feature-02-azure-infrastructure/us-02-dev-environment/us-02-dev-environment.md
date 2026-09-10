---
id: us-02
type: user-story
parent: feature-02
wave: R0
status: active
---

# us-02-dev-environment — Provision the `dev` Azure environment

## Story

As a **cloud engineer**, I want the `dev` environment instantiated from the modules
in `westeurope`, so that `dev` hosts the R0–R4 stack with isolated data, identities,
and resource group.

## Acceptance criteria

- [ ] AC-1 `dev` provisions: Container Apps Environment (consumption, min 0 replicas) for API + worker, PostgreSQL Flexible Server (Burstable Standard_B1ms) with `pgvector` enabled, Storage Account (blob+queue), Service Bus Standard, Key Vault Standard, Container Registry Basic, Log Analytics with data cap.
- [ ] AC-2 All `dev` resources tagged `project=raffa`, `env=dev`, `location=West Europe`.
- [ ] AC-3 `dev` has its own resource group and remote state in HCP `raffa-dev`.

## Definition of done

- [ ] `terraform apply -target`/`plan` for `environments/dev` exits 0 and resources are tagged correctly.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (feature-02) | instantiates the module library |

## Architecture decisions in force

- ADR-005 (SKUs), ADR-006 (westeurope), ADR-007 (env root + state), ADR-003 (pgvector).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Instantiate `dev` modules with env=dev + pgvector + tags | L | phase-3 |

## Council decisions carried into this story

Container Apps consumption (min 0), Postgres Flexible Server Burstable `Standard_B1ms` + `pgvector`, Storage GPv2 LRS, Service Bus Standard, Key Vault Standard, ACR Basic, Log Analytics w/ data cap. Region `westeurope`.

## Open questions

- none
