---
id: us-01
type: user-story
parent: feature-02
wave: R0
status: active
---

# us-01-terraform-module-library — Terraform module library + version pins

## Story

As a **cloud engineer**, I want a reusable Terraform module library under `infra/modules/`
with pinned provider/Terraform versions and mandatory `project`/`env` tagging, so that
`dev` and `demo` share structure but never state.

## Acceptance criteria

- [ ] AC-1 `infra/modules/` contains `network identity postgres storage servicebus containerapps keyvault acr monitor`.
- [ ] AC-2 `versions.tf` pins `azurerm`, `azuread`, `random` providers and Terraform version.
- [ ] AC-3 Every module applies `project = "raffa"` and `env = var.environment`.
- [ ] AC-4 `infra/environments/dev/` and `infra/environments/demo/` exist with separate `backend.tf` (HCP workspaces `raffa-dev`/`raffa-demo`).

## Definition of done

- [ ] AC-1..AC-4 verified by `terraform fmt -check` and a structural scan.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (feature-01) | modules live in the monorepo `infra/` folder |
| us-02 (feature-01) | backend.tf points at HCP workspaces |

## Architecture decisions in force

- ADR-007 — module layout, two env roots, remote state, no secrets.
- ADR-005 — the service/SKU set the modules wrap.
- ADR-006 — `location = "West Europe"`.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Scaffold module library + version/provider pins + env roots | L | phase-2 |

## Council decisions carried into this story

Modules: network, identity, postgres, storage, servicebus, containerapps, keyvault, acr, monitor. Providers `hashicorp/azurerm` + `hashicorp/azuread` + `hashicorp/random`. Tagging `project=raffa`, `env=dev|demo`.

## Open questions

- none
