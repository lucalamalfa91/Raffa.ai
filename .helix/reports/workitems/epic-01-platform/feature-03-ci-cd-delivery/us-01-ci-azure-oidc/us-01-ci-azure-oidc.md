---
id: us-01
type: user-story
parent: feature-03
wave: R0
status: active
---

# us-01-ci-azure-oidc — CI → Azure OIDC federation + service principals

## Story

As a **delivery engineer**, I want GitHub Actions to authenticate to Azure via OIDC
federated credentials with one least-privilege service principal per env, so that no
client secret is ever stored in GitHub or Terraform source.

## Acceptance criteria

- [ ] AC-1 Two service principals `raffa-sp-dev` and `raffa-sp-demo` exist, least-privilege to their own resource group.
- [ ] AC-2 OIDC federation subject claims pinned (repo/branch/env), no client secret stored.
- [ ] AC-3 Workflow files contain only `client-id`, `tenant-id`, `subscription-id` (no secret).

## Definition of done

- [ ] `azure/login` succeeds via federation for both envs without `AZURE_CREDENTIALS`.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-04 (feature-02) | SPs scoped to env resource groups |

## Architecture decisions in force

- ADR-015 — OIDC federation, per-env SP, no stored secret.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Create 2 SPs + OIDC federation + workflow auth | L | phase-4 |

## Council decisions carried into this story

SPs `raffa-sp-dev`/`raffa-sp-demo`; subject claim `repo:lucalamalfa91/raffa:*` + env for `demo`; no `AZURE_CREDENTIALS`.

## Open questions

- OQ-DM-003 (federation permitted) — assumption in force: permitted; fallback short-lived SP cert in Key Vault.
