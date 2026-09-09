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
    foundry/          # Foundry / Document Intelligence connection info + RBAC (ADR-008/ADR-017)
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

## Promotion to `demo` (ADR-016) — runbook

`git tag demo-v<N> <sha-on-main> && git push origin demo-v<N>` triggers
`.github/workflows/demo-promote.yml`, which reuses `infra.yml` /
`backend.yml` / `web.yml` with `target_environment: demo` and pauses each
of their deploy/apply jobs for approval on the `demo` GitHub Environment
(required reviewers, `scripts/apply_demo_environment_reviewers.py`). There
is no `workflow_dispatch` trigger by design (ADR-016 rejected manual
dispatch — a tag is the immutable "what was promoted" record).

After approval, confirm the deployed `demo` Static Web App is serving the
right `config.json` (demo API + Entra public client, never `dev`'s or
localhost):

```bash
python scripts/check_demo_swa_config.py --host <swa-host> --environment demo
```

`<swa-host>` is printed by the `promote-web` job's own "Project deployed
to `https://<host>`" line, or `az staticwebapp show --name
swa-contigo-demo --resource-group rg-contigo-demo --query
defaultHostname`. The same check has an on-demand CI wrapper,
`.github/workflows/demo-config-check.yml` (`workflow_dispatch`, no Azure
credential needed — `config.json` is a public static asset).

Full step-by-step runbook, plus the recorded evidence from the first three
`demo-v*` promotions (`demo-v1`/`demo-v3` succeeded; the mechanism is
already proven live, not just described here):
`.helix/reports/execution/demo-v-promotion-runbook.md`.

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

## AI Gateway / Foundry + Document Intelligence (ADR-004, ADR-008, ADR-017)

**Inventory (task E10/F02/US01/T01):** before this task, `ca-contigo-*-api`
/ `-worker`'s env list was connection-strings-only (Postgres, Storage) on
both `dev` and `demo` -- no Foundry project or Document Intelligence
setting existed anywhere under `infra/modules` (confirmed by grepping
`infra/` for `foundry|cognitive|DocumentIntelligence|OpenAI`, which
matched only a forward-looking comment). This task added `modules/foundry`
and three non-secret env vars on both Container Apps:

| Env var | Config key | Value |
|---|---|---|
| `AiGateway__Endpoint` | `AiGateway:Endpoint` | `https://aisvc-contigo.cognitiveservices.azure.com/` (deterministic; ADR-008's single shared account) |
| `AiGateway__ProjectName` | `AiGateway:ProjectName` | `contigo-dev` / `contigo-demo` (ADR-008 per-env Foundry project) |
| `AiGateway__DocumentIntelligenceConnection` | `AiGateway:DocumentIntelligenceConnection` | `conn-docint-contigo-dev` / `-demo` (ADR-017 per-project connection) |

None are Key Vault secrets: an endpoint URL and two names carry no key
(ADR-011). `scripts/foundry_connection_verify.py` proves both this
Terraform and `scripts/bootstrap_hcp_org.py`'s recorded shape still agree.

**Identity (RBAC), not yet live.** `modules/foundry` also grants the
workload identity `Cognitive Services User` on the shared `aisvc-contigo`
AI services account -- but only when `var.foundry_ai_services_resource_id`
(this env root's own variable, default `""`) is set. Azure AI Foundry
hub/project/account creation is an interactive Azure Portal step V1 keeps
outside the Terraform module surface (ADR-008); nobody has performed it
yet on either `dev` or `demo`. **Operator follow-up**, once that Portal
step is done: record the AI services account's ARM resource id as the
`foundry_ai_services_resource_id` HCP Terraform workspace variable on
**both** `contigo-dev` and `contigo-demo` (same account, set in each
workspace), then let HCP apply. Until then the role assignment simply does
not exist yet -- it is not a failed apply, `terraform plan`/`apply` still
succeed with it absent.

**What this does NOT close:** no live `IAiGateway` implementation exists
in `backend/src/Contigo.AiGateway` yet -- only `Fixtures/FixtureAiGateway.cs`
is registered (see that module's `ServiceCollectionExtensions.cs`). Adding
these env vars removes the "env vars were absent" blocker only; a future
backend task still has to read them and bind a real Foundry-backed
gateway. Per ADR-022, an operator may accept fixture AI for a `demo` dry
run in the meantime -- this Terraform still exists so `demo` is not
*permanently* fixture-only.

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
  the addresses are in state. `modules/acr` ignores in-place changes on
  the imported AcrPull assignment — ARM rejects `azurerm_role_assignment`
  updates (`doesn't support update`).
- **Static Web Apps region.** `Microsoft.Web/staticSites` is not offered in
  North Europe; West Europe is ineligible on this tenant. The module
  defaults to West US 2. Static assets are a global CDN; that region only
  hosts managed Functions / staging, which we disable.
- **GHA `terraform plan` on push to `main`** is redundant with the HCP VCS
  run. Ignore/discard the CLI plan; the VCS run is authoritative.
- **CI deploy principal Key Vault grant is in Terraform.** `backend.yml`
  reads `postgres-connection` as `contigo-sp-<env>`. Each env root looks
  that SP up by the GitHub Environment `AZURE_CLIENT_ID` (display name
  is not unique in this tenant) and `modules/keyvault` grants it
  `Key Vault Secrets User` on that vault only
  (`azurerm_role_assignment.ci_secrets_user`). Confirm the HCP VCS apply
  before re-running the backend deploy job.
- **Foundry account is still portal-only (ADR-008), so its RBAC grant is
  conditional.** `modules/foundry` derives the AI Gateway endpoint/project/
  connection names unconditionally (pure string derivation) but skips the
  `Cognitive Services User` role assignment until an operator sets
  `foundry_ai_services_resource_id` on both HCP workspaces. See "AI
  Gateway / Foundry + Document Intelligence" above for the operator
  follow-up step.

## Known gaps — Ask Contigo V2 (epic-13, ADR-024)

Recorded by task E13/F11/US01/T01 (`v2-integration`) while wiring the V2
operator jobs and acceptance runbook. Terraform changes are outside that task's
file scope; these are findings, not fixes.

- **`ConnectionStrings__Suppliers` is missing from the API Container App —
  blocks the first V2 deploy.** `backend/src/Contigo.Api/Program.cs` reads
  `ConnectionStrings:Suppliers` and throws at startup without it (task
  E13/F06/US01/T01 wired `AddSuppliersProductsModule`), but
  `modules/containerapps/main.tf` only injects `IdentityWorkspace`,
  `DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Chat` and
  `Storage`. Add the same `secret_name = "pg-cs"` env block its neighbours
  already use — Suppliers is a separate schema on the same server (ADR-003),
  not a separate database. This is the identical shape as the
  `ConnectionStrings__Savings` / `__Quotes` gap that module's own comment
  already records for an earlier wave.
- **`ConnectionStrings__Market` is intentionally absent, and that has a
  consequence.** `Contigo.Api/Program.cs` calls `AddMarketModule()` with no
  connection string, so the API keeps the module's in-memory mock projection
  and never reads the `market_record` / `market_embedding` rows
  `.github/workflows/seed-market-intelligence.yml` writes. Ingesting the feed
  is still the right pre-step (it is what R-MKT-03 specifies and what a live
  provider will feed), but "the deployed API serves the seeded corpus" is not
  yet true. Wiring it is an API composition change plus one env block here.
- **The two new V2 operator workflows need no new Azure grant.**
  `seed-market-intelligence.yml` and `reprocess-tenant-documents.yml` reuse the
  per-environment OIDC deploy principal (`contigo-sp-<env>`) that already holds
  `Key Vault Secrets User` on that vault via
  `modules/keyvault`'s `azurerm_role_assignment.ci_secrets_user`, and they read
  the same `postgres-connection` secret `backend.yml` and
  `seed-demo-fixture.yml` already read. No secret was added, so no
  `modules/keyvault` change is required for them.
- **`reprocess-tenant-documents.yml` depends on public API ingress.** It calls
  `POST /api/documents/{id}/reprocess` on `ca-contigo-<env>-api` from a GitHub
  runner, resolving the FQDN exactly as `web.yml` already does for the SPA's
  `config.json`. If `modules/containerapps` ever moves the API behind a private
  endpoint or an IP allow-list, that workflow needs a `backend/scripts/`
  database-side helper instead — its own header comment records why the API was
  the right tool while ingress stays public.
