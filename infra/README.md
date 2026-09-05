# Contigo infrastructure

Terraform for the Azure `dev` and `demo` environments. Honours ADR-007
(reusable modules + two thin environment roots, remote state per env, no
secrets in source), ADR-005 (SKUs), ADR-006 (North Europe), and ADR-011
(Key Vault + workload identity).

**HCP Terraform owns apply.** Both workspaces are VCS-connected to this
repo (`trigger-prefixes: infra/`). A CLI `terraform apply` from GitHub
Actions is rejected on a VCS-connected workspace; `.github/workflows/infra.yml`
therefore plans (and on merge only points at the HCP UI). Confirm the
CURRENT run in HCP, do not apply from the laptop against those workspaces.

## Layout (ADR-007)

```
infra/
  modules/
    network/          # VNet, subnets
    identity/         # Entra app registrations + user-assigned workload identity
    postgres/         # PostgreSQL Flexible Server + pgvector (VECTOR extension)
    storage/          # Storage Account (blob + queue)
    servicebus/       # Service Bus Standard namespace
    containerapps/    # Container Apps Environment + API app + worker app
    keyvault/         # Key Vault + workload identity grant
    acr/              # Azure Container Registry Basic, admin_enabled = false
    monitor/          # Log Analytics (Pay-As-You-Go, daily cap)
    staticwebapp/     # Azure Static Web Apps Free (web SPA; region West US 2)
  environments/
    dev/              # thin root; HCP workspace contigo-dev
    demo/             # thin root; HCP workspace contigo-demo
  versions.tf         # Terraform + provider pins (mirrored in each env root)
  provider.tf         # azurerm / azuread (also mirrored — Terraform has no include)
```

Each environment root instantiates the same modules into
`rg-contigo-<env>` in **North Europe** (Static Web Apps excepted — see
below). `var.environment` is locked per root (`dev` cannot become `demo`).
Tagging is `project=contigo`, `env=dev|demo`.

## Remote state

| Env | HCP org | Workspace | Working directory |
|-----|---------|-----------|-------------------|
| `dev` | `contigo-platform` | `contigo-dev` | `infra/environments/dev` |
| `demo` | `contigo-platform` | `contigo-demo` | `infra/environments/demo` |

State is never in git. Provider pins: Terraform `>= 1.8.0, < 2.0.0`,
`hashicorp/azurerm ~> 4.0`, `hashicorp/azuread ~> 3.0`,
`hashicorp/random ~> 3.6`. Keep `versions.tf` and both env-root
`terraform {}` blocks in lockstep (`scripts/terraform_env_roots_scan.py`).

## Identities — do not mix

| Role | Azure app | Used by |
|------|-----------|---------|
| HCP Terraform → Azure | `contigo-hcp-dev` | HCP workspace **Environment** vars `ARM_*` (not Terraform variables) |
| GitHub Actions OIDC | `contigo-sp-dev` / `contigo-sp-demo` | GitHub Environment `dev` / `demo` vars `AZURE_CLIENT_ID` / `TENANT_ID` / `SUBSCRIPTION_ID` |

The HCP service principal needs Contributor + User Access Administrator on
the subscription (role assignments) and Cloud Application Administrator in
Entra (app registrations). The GitHub deploy principal needs Reader on the
subscription (else `No subscriptions found`) and Contributor on that env's
resource group. Federated credential subjects are immutable and environment-
scoped (`repo:lucalamalfa91@…/contigo@…:environment:dev`).

West Europe is `locationineligible` for this tenant — both envs stay in
North Europe (ADR-006).

## Commands

CI already runs fmt + validate on every `infra/**` PR, and `terraform plan`
against the matching HCP workspace when `TF_API_TOKEN` is present.

```bash
# from repo root — syntax only, no backend
terraform -chdir=infra fmt -check -recursive
terraform -chdir=infra/environments/dev init -backend=false -input=false
terraform -chdir=infra/environments/dev validate
```

Do **not** `terraform apply` from a local CLI against the VCS-connected
workspaces. Merge to `main` (or start a run in the HCP UI). `contigo-demo`
does not auto-plan from GHA on push to `main` (`infra.yml` plans **dev**
only on that path); the first demo apply is a HCP UI **New run** or a
`workflow_call` from `.github/workflows/demo-promote.yml`.

## Resource names (stable, no random suffix except ACR)

| Kind | Name |
|------|------|
| Resource group | `rg-contigo-<env>` |
| Postgres | `psql-contigo-<env>` (SKU `B_Standard_B1ms`; `lifecycle.ignore_changes = [zone]`) |
| Container Apps Environment | `cae-contigo-<env>` |
| API / worker apps | `ca-contigo-<env>-api` / `-worker` |
| Workload identity | `id-contigo-<env>-workload` (tag `oidcPublicClientId` = public-client app id; `web.yml` reads it over ARM) |
| ACR | `acrcontigo<env><6-char suffix>` (suffix is state-held) |
| Static Web App | `swa-contigo-<env>` (Free SKU; resource location **West US 2**) |

Container Apps boot from the placeholder image
`mcr.microsoft.com/k8se/quickstart:latest` until the backend deploy job
pushes `contigo-api:<sha>` / `contigo-worker:<sha>` and updates the apps.
`lifecycle.ignore_changes` on the container image keeps a later apply from
reverting a live revision to that placeholder. Ingress target port is `8080`.
The API ingress CORS origin is this environment's Static Web App
(`https://<swa-host>`).

Connection strings (Postgres `contigo_<env>`, Storage) are written to this
environment's Key Vault and referenced from the Container Apps as
`secret { key_vault_secret_id }` using the workload identity (ADR-011).
They are never literals in `.tf` source and are not re-exported from the
env-root outputs.

Postgres allows Azure services (`0.0.0.0-0.0.0.0`) so Container Apps in
this subscription can reach the public endpoint. Private-endpoint wiring
through `modules/network` is later work.

## Known gaps

- **AcrPull is in Terraform.** `modules/acr` grants this env's workload
  identity `AcrPull` on that env's registry only. `modules/containerapps`
  attaches `registry { server, identity }` so API/worker pull without an
  admin password. Confirm the HCP VCS apply on `contigo-dev` /
  `contigo-demo` before the next `az containerapp update`, or pulls 401.
- **Out-of-band Postgres / AcrPull objects** (database `contigo_<env>`,
  firewall `AllowAzureServices`, live AcrPull assignment) are adopted
  via `import {}` blocks in each env root (`environments/<env>/imports.tf`).
  Leave them after the first successful apply; they become no-ops once
  the addresses are in state.
- **Static Web Apps region.** `Microsoft.Web/staticSites` is not offered in
  North Europe; West Europe is ineligible on this tenant. The module
  defaults to West US 2. Static assets are a global CDN; that region only
  hosts managed Functions / staging, which we disable.
- **GHA `terraform plan` on push to `main`** is redundant with the HCP VCS
  run. Ignore/discard the CLI plan; the VCS run is authoritative.
