# Raffa infrastructure

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
    foundry/          # shared Azure AI Services account + per-env Foundry project, model deployments, RBAC (ADR-004/008/017)
  environments/
    dev/              # thin root; HCP workspace raffa-dev
    demo/             # thin root; HCP workspace raffa-demo
  versions.tf         # Terraform + provider pins (mirrored in each env root)
  provider.tf         # azurerm / azuread (also mirrored — Terraform has no include)
```

Each environment root instantiates the same modules into
`rg-raffa-<env>` in **North Europe** (Static Web Apps excepted — see
below). `var.environment` is locked per root (`dev` cannot become `demo`).
Tagging is `project=raffa`, `env=dev|demo`. The one deliberate exception
is the shared AI services account (ADR-008): the `dev` root creates it in
`rg-raffa-ai` (tags `env=shared`) and the `demo` root attaches to it —
see "AI Gateway / Foundry + Document Intelligence" below.

## Remote state

| Env | HCP org | Workspace | Working directory |
|-----|---------|-----------|-------------------|
| `dev` | `raffa-platform` | `raffa-dev` | `infra/environments/dev` |
| `demo` | `raffa-platform` | `raffa-demo` | `infra/environments/demo` |

State is never in git. Provider pins: Terraform `>= 1.8.0, < 2.0.0`,
`hashicorp/azurerm ~> 4.0`, `hashicorp/azuread ~> 3.0`,
`hashicorp/random ~> 3.6`. Keep `versions.tf` and both env-root
`terraform {}` blocks in lockstep (`scripts/terraform_env_roots_scan.py`).

## Identities — do not mix

| Role | Azure app | Used by |
|------|-----------|---------|
| HCP Terraform → Azure | `raffa-hcp-dev` | HCP workspace **Environment** vars `ARM_*` (not Terraform variables) |
| GitHub Actions OIDC | `raffa-sp-dev` / `raffa-sp-demo` | GitHub Environment `dev` / `demo` vars `AZURE_CLIENT_ID` / `TENANT_ID` / `SUBSCRIPTION_ID` |

The HCP service principal needs Contributor + User Access Administrator on
the subscription (role assignments; since 2026-09-09 also the shared
`rg-raffa-ai` resource group and the data-plane grants on `aisvc-raffa`)
and Cloud Application Administrator in Entra (app registrations). The GitHub deploy principal needs Reader on the
subscription (else `No subscriptions found`) and Contributor on that env's
resource group. Federated credential subjects are immutable and environment-
scoped (`repo:lucalamalfa91@…/raffa@…:environment:dev`).

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
workspaces. Merge to `main` (or start a run in the HCP UI). `raffa-demo`
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
swa-raffa-demo --resource-group rg-raffa-demo --query
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
| Resource group | `rg-raffa-<env>` |
| Postgres | `psql-raffa-<env>` (SKU `B_Standard_B1ms`; `lifecycle.ignore_changes = [zone]`) |
| Container Apps Environment | `cae-raffa-<env>` |
| API / worker apps | `ca-raffa-<env>-api` / `-worker` |
| Workload identity | `id-raffa-<env>-workload` (tag `oidcPublicClientId` = public-client app id; `web.yml` reads it over ARM) |
| ACR | `acrraffa<env><6-char suffix>` (suffix is state-held) |
| Static Web App | `swa-raffa-<env>` (Free SKU; resource location **West US 2**) |
| Shared AI resource group | `rg-raffa-ai` (tags `env=shared`; owned by the `dev` root, ADR-008) |
| AI services account | `aisvc-raffa` (kind `AIServices`, S0, custom subdomain `aisvc-raffa`, keys disabled) |
| Foundry project | `raffa-<env>` (account-native sub-resource, no hub) |
| Model deployments | `<model>-<env>` (e.g. `gpt-5.4-nano-dev`, `text-embedding-3-small-dev`, `gpt-5.4-demo`) |

Container Apps boot from the placeholder image
`mcr.microsoft.com/k8se/quickstart:latest` until the backend deploy job
pushes `raffa-api:<sha>` / `raffa-worker:<sha>` and updates the apps.
`lifecycle.ignore_changes` on the container image keeps a later apply from
reverting a live revision to that placeholder. Ingress target port is `8080`.
The API ingress CORS origin is this environment's Static Web App
(`https://<swa-host>`).

Connection strings (Postgres `raffa_<env>`, Storage) are written to this
environment's Key Vault and referenced from the Container Apps as
`secret { key_vault_secret_id }` using the workload identity (ADR-011).
They are never literals in `.tf` source and are not re-exported from the
env-root outputs.

Postgres allows Azure services (`0.0.0.0-0.0.0.0`) so Container Apps in
this subscription can reach the public endpoint. Private-endpoint wiring
through `modules/network` is later work.

## AI Gateway / Foundry + Document Intelligence (ADR-004, ADR-008, ADR-017)

Since 2026-09-09 (ADR-004/ADR-008/ADR-017 amendments) `modules/foundry`
**creates** the AI resources; there is no hub and no portal step:

- **Ownership.** The `dev` root (`create_shared_account = true`) creates the
  shared resource group `rg-raffa-ai` and the single Azure AI Services
  account `aisvc-raffa` (kind `AIServices`: Azure OpenAI + Document
  Intelligence on one endpoint, `https://aisvc-raffa.cognitiveservices.azure.com/`;
  S0; custom subdomain; `project_management_enabled`; system identity;
  `local_auth_enabled = false` — no key exists anywhere, ADR-011). The
  `demo` root attaches to that account by name once `ai_account_attached =
  true` (a `data "azurerm_cognitive_account"` lookup inside the module —
  never `terraform_remote_state`, never the other environment's resource
  group). Never a second account (ADR-008).
- **Per environment**, every root creates its own Foundry project
  (`raffa-<env>`, an account-native sub-resource), its own model
  deployments (named `<model>-<env>`, pinned versions, `NoAutoUpgrade`) and
  its own role assignments: the workload identity gets `Cognitive Services
  User` (Document Intelligence) and `Cognitive Services OpenAI User`
  (inference); the object ids in `ai_operator_principal_ids` get the same
  two roles for live probes and the Foundry playground (Owner carries no
  data-plane rights). List an operator id in one root only.

| Role | dev deployment | demo deployment |
|---|---|---|
| classify | `gpt-5.4-nano-dev` (gpt-5.4-nano 2026-03-17, DataZoneStandard, 300K TPM) | `gpt-5.4-nano-demo` (200K TPM) |
| extract, answer | `gpt-5.4-nano-dev` | `gpt-5.4-demo` (gpt-5.4 2026-03-05, DataZoneStandard, 200K TPM) |
| embed | `text-embedding-3-small-dev` (v1, GlobalStandard, 100K TPM) | `text-embedding-3-large-demo` (v1, GlobalStandard, 100K TPM; the backend forces `dimensions = 1536`) |
| ocr | Document Intelligence `prebuilt-read` 2024-11-30 (built in, no deployment) | same |

Every SKU/version was verified in `northeurope` for this subscription on
2026-09-09 (`az cognitiveservices model list -l northeurope`);
`gpt-4o-mini` / `gpt-4.1-*` exist there only as provisioned SKUs and are
rejected by the module's validation.

**Two-phase wiring (`ai_gateway_wired`, per root, default `false`).**
`Raffa.Api` binds `IAiGateway` to the Foundry client whenever
`AiGateway:Endpoint` is non-empty and to the fixture gateway otherwise, so
the endpoint and the model map are published only when the account is
created/attached **and** `ai_gateway_wired = true` (commit a750746's
invariant, now read from the account resource in
`modules/foundry/outputs.tf`). With the flag `false` the account, project
and deployments exist and can be probed while the apps keep the fixture;
flipping it to `true` by pull request publishes, on both Container Apps:

| Env var | Config key | Value |
|---|---|---|
| `AiGateway__Endpoint` | `AiGateway:Endpoint` | the account endpoint, or `""` while unwired |
| `AiGateway__ProjectName` | `AiGateway:ProjectName` | `raffa-dev` / `raffa-demo` |
| `AiGateway__DocumentIntelligenceConnection` | `AiGateway:DocumentIntelligenceConnection` | `conn-docint-raffa-dev` / `-demo` (informational header value) |
| `AiGateway__Models__{Classify,Extract,Embed,Answer,Ocr}__ModelId` | `AiGateway:Models:<Role>:ModelId` | the deployment names above (`prebuilt-read` for ocr); one `dynamic "env"` block over `var.ai_gateway_model_env`, absent while unwired |
| `AiGateway__Models__<Role>__ModelVersion` | `AiGateway:Models:<Role>:ModelVersion` | the pinned model version (`2024-11-30` for ocr) |
| `ai_gateway_extra_env` entries | any `AiGateway:*` knob | per-role settings settled by the live probe (e.g. `AiGateway__Models__Extract__ReasoningEffort`) |

None are Key Vault secrets (ADR-011). `scripts/foundry_connection_verify.py`
holds the module and both roots to this shape (single owner, gated
outputs, per-env deployment names, allowed SKUs, both roles, no hub) and
`scripts/bootstrap_hcp_org.py` records the names it compares against.

**Live probe (after the `dev` apply, before wiring).** Wait a few minutes
for RBAC propagation, then, as an operator listed in
`ai_operator_principal_ids`:

```bash
EP=https://aisvc-raffa.cognitiveservices.azure.com
TOKEN=$(az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv)
# chat completions on the dev deployment (Azure OpenAI v1 surface)
curl -sS "$EP/openai/v1/chat/completions" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"model":"gpt-5.4-nano-dev","messages":[{"role":"user","content":"Reply with {\"ok\":true}"}],"max_completion_tokens":64,"response_format":{"type":"json_schema","json_schema":{"name":"probe","strict":true,"schema":{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}}}}'
# embeddings
curl -sS "$EP/openai/v1/embeddings" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"model":"text-embedding-3-small-dev","input":"probe"}' | jq '.data[0].embedding | length'
# Document Intelligence Read (202 + Operation-Location, then poll it)
curl -sS -D - -o /dev/null "$EP/documentintelligence/documentModels/prebuilt-read:analyze?_overload=analyzeDocument&api-version=2024-11-30" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"base64Source\":\"$(base64 -w0 sample.pdf)\"}"
```

A `401`/`403` right after the apply is RBAC propagation (retry later); a
`404 DeploymentNotFound` is a deployment name; a `400` naming a parameter
is a backend compatibility finding for the GPT-5.x request shape.

## Known gaps

- **AcrPull is in Terraform.** `modules/acr` grants this env's workload
  identity `AcrPull` on that env's registry only. `modules/containerapps`
  attaches `registry { server, identity }` so API/worker pull without an
  admin password. Confirm the HCP VCS apply on `raffa-dev` /
  `raffa-demo` before the next `az containerapp update`, or pulls 401.
- **Out-of-band Postgres / AcrPull objects** (database `raffa_<env>`,
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
  reads `postgres-connection` as `raffa-sp-<env>`. Each env root looks
  that SP up by the GitHub Environment `AZURE_CLIENT_ID` (display name
  is not unique in this tenant) and `modules/keyvault` grants it
  `Key Vault Secrets User` on that vault only
  (`azurerm_role_assignment.ci_secrets_user`). Confirm the HCP VCS apply
  before re-running the backend deploy job.
- **Foundry wiring is two-phase.** The account, projects, deployments and
  RBAC are Terraform-managed (`modules/foundry`); `AiGateway__Endpoint` and
  the `AiGateway__Models__*` env vars are published only where the root sets
  `ai_gateway_wired = true` (dev after the live probe, demo with the
  promotion that carries the live-Foundry backend). `demo` also needs
  `ai_account_attached = true` before it creates its project and
  deployments — and `dev` and `demo` applies that touch the shared account
  must not run at the same time (the account serialises deployment
  writes).
- **First-ever Cognitive Services account in the subscription.** If the
  create fails with `ResourceKindRequireAcceptTerms`, the owner accepts the
  Responsible AI terms once (`az cognitiveservices account create ... --yes`
  for `aisvc-raffa` in `rg-raffa-ai`) and the resources are adopted with
  `import {}` blocks in `environments/dev/imports.tf`, like the Postgres /
  AcrPull objects above.

## Known gaps — Ask Raffa V2 (epic-13, ADR-024)

Recorded by task E13/F11/US01/T01 (`v2-integration`) while wiring the V2
operator jobs and acceptance runbook. Terraform changes are outside that task's
file scope; these are findings, not fixes.

- **`ConnectionStrings__Suppliers` is missing from the API Container App —
  blocks the first V2 deploy.** `backend/src/Raffa.Api/Program.cs` reads
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
  consequence.** `Raffa.Api/Program.cs` calls `AddMarketModule()` with no
  connection string, so the API keeps the module's in-memory mock projection
  and never reads the `market_record` / `market_embedding` rows
  `.github/workflows/seed-market-intelligence.yml` writes. Ingesting the feed
  is still the right pre-step (it is what R-MKT-03 specifies and what a live
  provider will feed), but "the deployed API serves the seeded corpus" is not
  yet true. Wiring it is an API composition change plus one env block here.
- **The two new V2 operator workflows need no new Azure grant.**
  `seed-market-intelligence.yml` and `reprocess-tenant-documents.yml` reuse the
  per-environment OIDC deploy principal (`raffa-sp-<env>`) that already holds
  `Key Vault Secrets User` on that vault via
  `modules/keyvault`'s `azurerm_role_assignment.ci_secrets_user`, and they read
  the same `postgres-connection` secret `backend.yml` and
  `seed-demo-fixture.yml` already read. No secret was added, so no
  `modules/keyvault` change is required for them.
- **`reprocess-tenant-documents.yml` depends on public API ingress.** It calls
  `POST /api/documents/{id}/reprocess` on `ca-raffa-<env>-api` from a GitHub
  runner, resolving the FQDN exactly as `web.yml` already does for the SPA's
  `config.json`. If `modules/containerapps` ever moves the API behind a private
  endpoint or an IP allow-list, that workflow needs a `backend/scripts/`
  database-side helper instead — its own header comment records why the API was
  the right tool while ingress stays public.
