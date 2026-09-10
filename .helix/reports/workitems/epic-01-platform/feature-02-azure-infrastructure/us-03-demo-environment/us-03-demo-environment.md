---
id: us-03
type: user-story
parent: feature-02
wave: R0
status: active
---

# us-03-demo-environment — Provision the `demo` Azure environment

## Story

As a **cloud engineer**, I want the `demo` environment instantiated identically to
`dev` but fully isolated (own resource group, data, identities, state), so that the
stakeholder-facing environment never shares a store with `dev`.

## Acceptance criteria

- [ ] AC-1 `demo` provisions the same service set as `dev` (Container Apps, Postgres+pgvector, Storage, Service Bus, Key Vault, ACR, Log Analytics).
- [ ] AC-2 All `demo` resources tagged `project=raffa`, `env=demo`, `location=West Europe`.
- [ ] AC-3 `demo` has its own resource group and HCP `raffa-demo` state; no shared Postgres/Storage/Service Bus with `dev`.

## Definition of done

- [ ] `terraform plan` for `environments/demo` exits 0; isolation asserted (distinct resource group + state).

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (feature-02) | same module library |

## Architecture decisions in force

- ADR-005, ADR-006, ADR-007, ADR-003.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Instantiate `demo` modules with env=demo (isolated) | L | phase-3 |

## Council decisions carried into this story

Identical architecture, separate RG/state/identities/data. Region `westeurope`.

## Open questions

- none
