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

## Amendment (2026-09-13, wave w15 — the module list is reconciled with the tree, and two edges become real)

**Items served**: NW-27, NW-67, NW-68, NW-05. **Owner**: cloud-architect. This
is ADR-007's first amendment. Option 1 is unchanged and not re-opened: a
reusable module library plus two thin environment roots, **remote state per
environment** (HCP workspaces `raffa-dev` / `raffa-demo`), **no state in git**,
**no secrets in Terraform source**, and mandatory `project=raffa` /
`env=dev|demo` tagging. Every implication at `:84-87` stands verbatim.

### 1. The layout block at `:36-61` is two waves stale — nine modules, not eleven

The block above lists **nine** modules. The tree carries **eleven**: the same
nine plus **`staticwebapp/`** (Azure Static Web Apps Free, region West US 2) and
**`foundry/`** (shared AI Services account + per-env project + model deployments
+ RBAC, ADR-004/008/017). Both landed **without an ADR-007 footer**, which is
why this correction is recorded before the w15 addition rather than after it:
publishing "nine → ten" would re-publish a list that has been wrong for two
waves and hand the decomposer a layout that does not match the tree it must
edit.

**With w15's addition the module set is twelve:**

```
infra/modules/
  network/  identity/  postgres/  storage/  servicebus/  containerapps/
  keyvault/  acr/  monitor/  staticwebapp/  foundry/  communication/   # new in w15
```

**`infra/modules/communication/`** (new, NW-68) holds the four
`azurerm_communication_*` / `azurerm_email_communication_*` resources of the
ADR-005 w14 footer, one set per environment, never shared. It exports
`connection_string` (**`sensitive = true`**) and `sender_address`.

**A second file, and it is the one an engineer actually opens.**
`infra/README.md:18-29` carries a **parallel layout tree** that already lists all
eleven directories correctly. It gains `communication/` in the **same task** as
the module itself. **Two files, one edit — not one.** Recorded because this
ADR's block and that README have already drifted apart once, in opposite
directions.

### 2. Two module edges that the layout implies and the tree does not have

- **`servicebus → containerapps`.** `modules/servicebus`'s three outputs (`id`,
  `name`, `fqdn`) have **one consumer in the whole repo**
  (`environments/dev/outputs.tf:77`), and `modules/containerapps/variables.tf`
  declares **no Service Bus input at all**. w15 creates the edge: the
  containerapps module gains `servicebus_namespace_name`, `servicebus_fqdn`,
  `servicebus_topic_name`, `servicebus_subscription_name` (plus
  `worker_max_replicas` / `api_max_replicas`), wired in both roots from
  `module.servicebus.*`. **No plan-time unknown is introduced**:
  `modules/servicebus/outputs.tf:19` composes `fqdn` from the namespace **name**,
  which is the literal `sbns-raffa-${var.environment}` and not a post-apply
  attribute, so the reviewer sees the real string in the w15 plan instead of
  `(known after apply)`.
- **`identity → servicebus`.** `modules/servicebus` gains a required
  `workload_principal_id` input and creates the two **topic-scoped** role
  assignments of the ADR-005 w15 footer §2. This is the shape `modules/foundry`
  and `modules/keyvault` already use.

`modules/identity` additionally gains a Microsoft Graph **data source**, a
`count`-gated `azuread_app_role_assignment` and a `tenant_id` output (NW-67,
NW-05) — all **inside** the existing module, so no boundary moves.
`modules/keyvault` gains one secret and one output. `modules/containerapps`
gains the `acs-cs` and worker `st-cs` handles. The new dependency chain
`communication → keyvault → containerapps` is acyclic and is created **by
reference**, so `module "communication"` may sit anywhere in a root: Terraform
orders by reference, not by file position — which is why `module "keyvault"`
already sits *after* `module "containerapps"` in both roots and works.

### 3. What does not change

- **Remote state per environment** — untouched. No w15 resource is shared
  between `dev` and `demo`; the one pre-existing exception (`aisvc-raffa`,
  ADR-008) is not widened, and mail is explicitly **one resource set per
  environment** because it has no fixed cost to amortise.
- **No secrets in source** — upheld and strengthened. The Service Bus data plane
  is **RBAC with no secret at all**. The one secret w15 adds (`acs-connection`)
  follows the `postgres-connection` / `storage-connection` path byte for byte:
  Terraform references Key Vault, the app reads at runtime through the workload
  identity, and **no secret value is written into Terraform source**. The
  conditional KEDA fallback of the ADR-005 w15 footer §2, if taken, is
  `listen`-only and is referenced **only** by `custom_scale_rule.authentication`,
  never by an `env {}` block and never by application code.
- **Tagging** — `project = "raffa"` and `env = var.environment` on every taggable
  w15 resource, including all four ACS types.

### 4. Implication for the decomposition

**One Terraform PR touches all of `infra/**` in this wave** — six modules
(`servicebus`, `containerapps`, `communication`, `keyvault`, `identity`, plus
`infra/README.md`) and both environment roots. That is not a preference: it is
what `scripts/check_single_writer.py` requires, and splitting it would serialise
three PRs against one VCS-connected workspace while buying three separate human
confirmations. **The four `azurerm_communication_*` spellings and the
`custom_scale_rule` authentication shape are proved by `terraform fmt -check
-recursive` + `terraform validate` in the `infra.yml` PR job — never asserted
from an ADR footer**, per the ADR-005 w14 prerequisite at `:211-219`.
