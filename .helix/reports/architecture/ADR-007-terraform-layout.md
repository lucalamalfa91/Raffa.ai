# ADR-007 — Terraform module layout, remote state, and no secrets in source

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: cloud-architect (owner), delivery-manager, security-architect
- **Locked citations**: IaC — HCP Terraform, infra code in `infra/` folder of the monorepo; Environments — `dev`+`demo` isolated; Secrets — Key Vault, no secrets in code or Terraform source; tagging `project=raffa`, `env=dev|demo`.

## Context and problem statement

Brief §6 requires HCP Terraform in `infra/`, applying **both** `dev` and `demo`, with **remote state per environment**, no state in git, and no secrets in Terraform source. The council must define the module layout so the two environments share structure (identical architecture per brief §4) but never share state or store, and so secrets reach apps only at runtime via Key Vault + managed identity.

## Decision drivers

- **DRY with enforced isolation**: one reusable module set, instantiated twice (`dev` and `demo`) with separate backend config so nothing crosses environments.
- **Remote state per env**: separate HCP Terraform workspaces (or separate backend keys) with independent state.
- **No secrets in source**: all references are Key Vault scoped; Terraform writes no secret material; apps read via managed identity at runtime.

## Considered options

1. **Reusable modules + two thin environment roots (`environments/dev` and `environments/demo`)** — one module library, two instantiation points, separate state.
2. **Flat per-environment folders with duplicated resources** — copy-paste between `dev` and `demo`.
3. **A single shared root with a `var.environment` toggling everything** — one state, one backend.

## Decision outcome

**Chosen: Option 1** — a reusable module library plus two thin environment roots, with remote state per environment (separate HCP Terraform workspaces), because it honors DRY and the brief's "apply both environments" while guaranteeing `dev` and `demo` never share state, backend, or store.

### Consequences

- **Good**: a fix to a module propagates to both envs; each env's state/backend is isolated and independently lockable; matches brief §6 exactly.
- **Bad**: two backends/workspaces mean two `terraform plan/apply` targets and two state files to reason about; slightly more ceremony than a single root.
- **Neutral**: module boundaries (network, identity, data, compute, ai) are a design choice that adds files but keeps intent explicit.

## Module layout (under `infra/`)

```
infra/
  modules/
    network/          # VNet, subnets, private endpoints (if used)
    identity/         # Entra app registrations, user-assigned managed identities
    postgres/         # Azure Database for PostgreSQL Flexible Server + pgvector + RLS wiring
    storage/          # Storage Account (blob + queue) per env
    servicebus/       # Service Bus Standard namespace (topics) per env
    containerapps/    # Container Apps Environment + API app + worker app
    keyvault/         # Key Vault (Standard) per env + access policies
    acr/              # Azure Container Registry (Basic) per env
    monitor/          # Log Analytics workspace + data cap
  environments/
    dev/
      main.tf         # instantiates modules with env=dev
      backend.tf      # remote state -> HCP workspace "raffa-dev"
      variables.tf
      outputs.tf
    demo/
      main.tf         # instantiates modules with env=demo
      backend.tf      # remote state -> HCP workspace "raffa-demo"
      variables.tf
      outputs.tf
  versions.tf         # provider + Terraform version pins
  provider.tf         # azurerm (and azuread) providers
```

- **Remote state**: HCP Terraform — two workspaces, `raffa-dev` and `raffa-demo` (or a single workspace with two `backend "remote"` `key` values). State is never in git.
- **Providers**: `hashicorp/azurerm` (primary), `hashicorp/azuread` (identity/Entra), plus `hashicorp/random` if suffixing is needed. Provider versions pinned in `versions.tf`.
- **Tagging**: every module applies `project = "raffa"` and `env = var.environment` (set to `dev` or `demo`) so the cost researcher can filter.
- **No secrets**: Terraform only references Key Vault, Entra, and managed identities. It never emits a connection string, SAS token, or certificate secret into state as plaintext-visible app secret; apps use managed identity to read Key Vault at runtime.

## Pros and cons of the options

### Option 1 — modules + two env roots
- Good: DRY, isolated state/backend, matches brief §6.
- Bad: two workspaces to operate.

### Option 2 — duplicated per-env folders
- Good: obvious isolation.
- Bad: drift between `dev` and `demo`; every change copied twice; violates DRY intent.

### Option 3 — single root + `var.environment`
- Good: one state.
- Bad: one backend = `dev` and `demo` share state and backend, which the brief's "remote state per environment" forbids; isolation is only logical, not physical.

## Implications for the decomposition

- Every infra task targets a module in `infra/modules/` and is instantiated through `environments/{dev,demo}/main.tf`.
- Backend config is per-environment; a task must not point `dev` and `demo` at the same HCP Terraform workspace/state key.
- Secrets are never written into Terraform source or state as app-readable secrets; use Key Vault + managed identity (see security-architect ADR for Key Vault + RAG isolation).
- Tagging is mandatory (`project=raffa`, `env=dev|demo`) at every resource creation.

## Assumptions

- HCP Terraform supports two workshops/workspaces (or two backend `key` values) for the `raffa` repo.
- `azurerm` and `azuread` providers are used; exact provider minor versions are pinned at implementation time in the target region.
