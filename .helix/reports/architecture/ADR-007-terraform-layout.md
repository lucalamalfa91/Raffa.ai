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

## Amendment (2026-09-15, wave w17 — a second principal input on one module, and the README's role inventory joins the "two files, one edit" rule)

**Items served**: NW-73. **Owner**: cloud-architect. Clauses continue at **5**
(w15 ended at §4). Option 1 is unchanged and not re-opened. **No module is
created or removed — the set stays twelve** (w15 §1) — **no module boundary
moves, remote state per environment is untouched, and no secret enters Terraform
source.** This is the smallest possible layout delta: one existing module gains
one input.

### 5. `modules/servicebus` gains a **second** principal input, under the same per-root isolation rule as the first

w15 §2 created the `identity → servicebus` edge with a required
`workload_principal_id`. w17 adds the **CI** edge to the same module: a required
**`ci_deploy_principal_id`**, consumed by the single role assignment of the
ADR-005 w17 footer §15.

- **The shape is the one `modules/keyvault` already uses** — that module takes
  both `workload_principal_id` and `ci_deploy_principal_id`
  (`modules/keyvault/variables.tf:34`) and grants each its own scoped role. This
  is not a new pattern; it is the second instance of an existing one, which is
  why no layout decision moves.
- **The name is `ci_deploy_principal_id`, matching `modules/keyvault`.** This
  seat's lane draft proposed `ci_publisher_principal_id` and **yields**: one
  identity with two spellings across two modules is how a role inventory goes
  stale, and §6 below is what that costs.
- **Required, not optional**, and **both roots are wired in the same change** —
  `environments/dev/main.tf:106` and `environments/demo/main.tf:128` each gain one
  line, fed from the `data.azuread_service_principal.ci_deploy` each root
  **already** declares (`dev:223-225` → `:236`, `demo:245-247` → `:258`). No new
  data source, no new literal client id. The reason a dev-only wiring is refused
  is delivery-manager's and is recorded in the ADR-005 w17 footer §17:
  `infra.yml` validates only the changed root's path filter, so `demo` would break
  at its next promotion rather than in this PR.
- **The variable's description carries the per-root isolation rule verbatim** from
  the sibling it copies (`modules/servicebus/variables.tf:22-30`): each root
  passes **its own** environment's principal, never the other's. That comment is
  the module's standing guard against a cross-environment grant, and a second
  principal doubles the number of places it can be got wrong.

### 6. `infra/README.md` carries a **role inventory** that this change falsifies — and it rides the same PR

w15 §1 established that this ADR's layout block and `infra/README.md`'s parallel
tree are **two files, one edit**, because they had already drifted apart in
opposite directions. w17 finds a **second kind of drift in the same file**, and it
is not a layout tree:

- **`infra/README.md:331-336`** — "**CI deploy principal Key Vault grant is in
  Terraform** … `modules/keyvault` grants it `Key Vault Secrets User` on that
  vault only". After §5 this is **incomplete**: the principal also holds a
  topic-scoped `Azure Service Bus Data Sender`.
- **`infra/README.md:417-421`** — "**The two new V2 operator workflows need no new
  Azure grant.** … reuse the per-environment OIDC deploy principal
  (`raffa-sp-<env>`) that already holds `Key Vault Secrets User`". After §5 this
  sentence is **false in general**, and pointedly so: it is a claim about *operator
  workflows*, and NW-73's console workflow is an operator workflow that **does**
  need a new grant.

**Both paragraphs are updated in the same task as the module**, because
`infra/README.md` is **inside `infra/**`** and therefore inside the infrastructure
PR — it does not leak into the wave PR. Recorded as a rule rather than a one-off:
**a change to the set of roles a principal holds updates that file's role
inventory in the same change**, exactly as a new module updates its layout tree.
A stale inventory is worse than a stale tree, because the tree is checked against
the directory listing every time someone opens it, while the inventory is a claim
nothing re-derives.

### 7. What does not change, including one implication a task must **not** try to satisfy

- **Remote state per environment** — untouched. The role assignment is created in
  each environment's own workspace against that environment's own principal and
  its own topic; nothing is shared across `dev` and `demo`.
- **No secrets in Terraform source** — upheld and **strengthened**. The Service
  Bus data plane stays **RBAC with no secret at all**: §5 adds an object id
  (already resolved from a data source in both roots today), not a credential. No
  Key Vault secret, no `azurerm_servicebus_*_authorization_rule`, no SAS key.
- **Tagging (`:87`, "mandatory at every resource creation") is not violated by a
  resource that cannot carry tags.** `azurerm_role_assignment` has **no `tags`
  argument**; neither of the two existing assignments carries one. Recorded so a
  task does not add a `tags` block that fails `validate`, and so a reviewer does
  not read its absence as a missed implication.

### 8. Implication for the decomposition

The w17 `infra/**` delta is **exactly five files**: `modules/servicebus/main.tf`,
`modules/servicebus/variables.tf`, `environments/dev/main.tf`,
`environments/demo/main.tf` and `infra/README.md` (§6). **One Terraform task, one
PR, ahead of the wave PR** — delivery-manager's two-PR shape (ADR-014 w15 clause
5) fires this wave because, unlike w16, the `infra/` delta is non-zero. `fmt
-check -recursive` and `validate` must be asserted on **both** roots, not only the
changed one: that is the inverse of w16 and it is what catches a dev-only wiring
before `demo`'s promotion does.

### 9. Correction (round 3) — `demo` does not apply "at its promotion"; both workspaces apply from the same merge

**Raised by delivery-manager** (round 2, finding (i)) against this seat's framing.
Verified first-hand before adopting, and the verification **changes the rule, not
just the wording** — including delivery-manager's own version of it.

- **No promotion applies anything.** `infra.yml`'s `apply` job is named
  *"terraform apply skipped"* and its **only step writes to `$GITHUB_STEP_SUMMARY`**
  (`:114-136`); it invokes no Terraform, deliberately — `:104-113` records that a
  CLI apply against a VCS-connected workspace is rejected. `demo-promote.yml` says
  the same in its own comment: the reused workflow "still plans (and records the VCS
  apply pointer); **it does not CLI-apply**" (`:123-127`), and `promote-backend` /
  `promote-web` merely `needs` a green **plan** (`:140`, `:151`).
- **The trigger is the merge, not the tag.** `scripts/hcp_vcs_wiring.py:104-106`
  wires **both** workspaces identically — `WORKSPACE_NAMES = ("raffa-dev",
  "raffa-demo")`, `EXPECTED_BRANCH = "main"`, `EXPECTED_TRIGGER_PREFIX = "infra/"` —
  and `:101-103` states the intent: "ADR-014 is the one mainline branch **every**
  workspace must track". A `demo-v*` tag plays **no part** in the infra apply.
- **Corrected rule, replacing this footer's `:223-224` and `:282` and ADR-005 w17
  §25 `:1189-1190`**: *one merge to `main` touching `infra/` queues a VCS run on
  **both** `raffa-dev` and `raffa-demo` at the same instant. `demo`'s infra moves on
  the merge, never on the promotion tag.*

**§5 and §8 are strengthened, not weakened.** "Both roots in the same PR" was
justified by "`demo` would break at its next promotion". A dev-only wiring is in
fact **worse** than that: `raffa-demo`'s run is queued **by the same merge**,
against a demo root missing the input, in a workspace nobody is watching. The
failure is **sooner and quieter**, not later. The decisions stand; their reason is
now the correct one.

⚠ **The `environment: demo` approval gates an echo.** `infra.yml:121` puts
`environment:` on the **apply** job — the job whose only step is the summary write.
ADR-016's required-reviewer ritual therefore pauses **nothing on the Terraform
plane**: HCP's run is triggered by the merge, outside GitHub Actions, where no
GitHub approval stands in front of it. This is **not** a proposal to move the gate
this wave — it is a correction to what the gate is believed to do, recorded so no
task is written on the belief that a human approves `demo`'s infra.

**OQ-w17-ca-05 (open, assumption in force).** Whether `raffa-demo` has
**auto-apply** enabled is **live HCP state and unreadable from this checkout** —
`hcp_vcs_wiring.py` asserts VCS wiring and execution-mode, **not** auto-apply
(`:33-46`). The branches differ materially: auto-apply **on** means `demo` infra
changes in the **same merge as `dev`**, under a running client pilot, with no
approval in front of it; **off** means the run sits queued and `demo` silently
lacks NW-73's role assignment while every workflow is green — delivery-manager's
finding. **Assumption in force: off**, because `infra.yml:109` instructs the
operator to "Confirm the VCS-triggered run in the HCP UI (or enable workspace
auto-apply)". **Owed as an operator read, not a task**: confirm both workspaces'
auto-apply setting before the `demo` promotion, and record it.

**For the decomposer**: the infra PR's merge is the apply event for **both**
environments, so NW-73's acceptance confirms **two** HCP runs (`raffa-dev` *and*
`raffa-demo`), not one. That is delivery-manager's "two applies before any
acceptance walk" (OQ-w17-dm-03), reached here from the workspace wiring rather
than from PR #118 — two routes, one requirement.
