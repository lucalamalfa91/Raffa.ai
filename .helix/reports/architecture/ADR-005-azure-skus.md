# ADR-005 — Azure services and SKUs for `dev` and `demo`

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: cloud-architect (owner), software-architect, security-architect, delivery-manager
- **Locked citations**: Cloud — Microsoft Azure; Environments — two isolated envs `dev`/`demo`, no production; Cost — free tiers / cheapest SKUs that satisfy the spec; IaC — HCP Terraform; AI — Microsoft Foundry via AI Gateway; Auth/secrets — Entra ID OIDC + Key Vault.

## Context and problem statement

Contigo V1 must run two isolated Azure environments (`dev` and `demo`) that satisfy the product topology intent (spec §5.1): a modular-monolith API, a background worker, a relational store with vectors/search, object storage, and a queue, plus secrets, identity, and the Foundry AI path. The brief (§4) mandates the cheapest/free SKU that still supports the product and forbids idle-expensive resources, production HA, and any shared PostgreSQL or document storage between the two environments. Every named service needs a concrete SKU so the infra cost researcher can price it at retail.

## Decision drivers

- **Cost** is the top driver: free tier where it exists, cheapest paid SKU otherwise, scale-to-zero / stop-start where the platform allows.
- **Isolation**: `dev` and `demo` get identical architecture but separate resource groups, data, identities, and Key Vaults — no shared store.
- **Product sufficiency**: the chosen SKUs must actually support HTTPS app host, async worker, PostgreSQL + pgvector, durable object storage, a queue, and V1 OCR (ADR-017) for full scanned/image contracts.

## Considered options

1. **Azure Container Apps + Azure Database for PostgreSQL Flexible Server + Storage Account + Service Bus** — serverless compute, serverless DB option, cheap blob/queue.
2. **Azure Kubernetes Service (AKS) + managed Postgres** — production-leaning, idle-expensive, overkill for no-prod V1.
3. **Azure App Service (Linux) + WebJobs** — Linux Basic plan for API+worker, but no first-class scale-to-zero compute for a separate worker.

## Decision outcome

**Chosen: Option 1** — Azure Container Apps (consumption) for both API and worker, Azure Database for PostgreSQL Flexible Server (Burstable, smallest tier) with pgvector, a single Storage Account (Blob + Queue) per environment, Azure Service Bus (Standard) for durable queue messaging, Azure Key Vault (Standard, no HSM), and Entra ID (Free) for OIDC. Each environment is a distinct Resource Group; `dev` and `demo` never share a Postgres, Storage Account, or Service Bus namespace.

### Consequences

- **Good**: consumption-based compute scales to zero when idle (zero bill at rest); single managed Postgres with pgvector and row-level isolation satisfies the relational store + vector requirement; blob+queue and Service Bus round out the topology; everything stays on free/cheapest tiers.
- **Bad**: consumption billing is metered by vCPU-seconds and requests, so a runaway worker can still cost money; Container Apps consumption cold-start latency is acceptable for `dev`/`demo` but not instant.
- **Neutral**: hosting the queue-dependent worker as a separate Container App (rather than WebJobs) adds one more deployment unit but keeps the worker independently scalable and stoppable.

## Concrete services and SKUs

| Concern | Service | SKU / tier (per env) | Notes |
| --- | --- | --- | --- |
| API + worker host | Azure Container Apps | **Consumption profile (no SKU — usage metered)**, 0.25 vCPU / 0.5 GiB default per replica, min instances = 0 | API and worker are two separate Container Apps in the env; scale-to-zero at idle. |
| Ingress / TLS | Container Apps Environment | **Consumption-only workload profile** | Provides HTTPS endpoint for API (and can front web if needed). |
| Relational store | Azure Database for PostgreSQL — Flexible Server | **Burstable, Standard_B1ms (1 vCPU, 2 GiB)**, private access | `pgvector` extension enabled per-server; single server per env, RLS/`tenant_id` for isolation. |
| Object storage | Azure Storage Account | **General Purpose v2, Blob (LRS) hot**, Block Blob | Contract documents; per-env account, never shared. |
| Queue (simple) | Azure Storage Account | **Queue storage** (within same GPv2 account) | Inbox/dead-letter for lightweight jobs; no extra charge beyond the storage account. |
| Queue (durable) | Azure Service Bus | **Standard tier** (1 namespace per env) | Topics/queues for extraction events; Standard gives topics + sessions; Basic omits topics. |
| Secrets | Azure Key Vault | **Standard tier** (no Premium/HSM) | Per-env secrets; apps read at runtime via managed identity. |
| Identity | Microsoft Entra ID | **Free tier** (app registrations, users/groups) | OIDC / SSO-ready; no premium P1/P2 licenses. |
| Monitoring | Azure Monitor / Log Analytics | **Log Analytics Workspace, Pay-As-You-Go with a data cap** (e.g. 1 GB/day) | Billing is per-GB; a daily cap prevents idle-log runaway. |
| Container registry | Azure Container Registry | **Basic tier** (per env is redundant → **one** Basic registry shared across envs is rejected to preserve isolation; use one Basic per env) | Each env pulls from its own registry namespace; Basic supports geo-less, 10 GiB, `data endpoints = none`. |
| OCR / document layout | Azure AI Document Intelligence (on the ADR-008 AI services account) | **S0 / pay-per-page** — `prebuilt-read` + `prebuilt-layout` | In V1 (ADR-017). No idle SKU. F0 page caps are insufficient for the 100-contract Day-1 path. Per-env endpoint via Foundry projects `contigo-dev` / `contigo-demo`. |

> **Shared-vs-isolated note**: one ACR per environment is chosen strictly to honor the isolation rule for any deployment-time secrets/pull identity, but ACR is a publish surface, not a data store. The infra cost researcher may note ACR Basic is metered on storage+pull bandwidth; a single ACR with per-env repositories is an acceptable cost-optimization the council can ratify later. Default here: one ACR Basic per env.

### Scale-to-zero / stop-start

- Container Apps consumption: **min replicas = 0** for both API and worker → zero compute cost at idle.
- PostgreSQL Flexible Server: cannot scale-to-zero; use **Burstable** (the cheapest paid option). Optional `starts_on`/maintenance automation is a later task; not relied on here.
- Key Vault, Entra ID, Service Bus Standard, ACR Basic, Storage Account: fixed but minimal monthly cost; none are metered-idle-expensive in practice (Service Bus Standard and ACR Basic are the only non-trivial fixed lines).

## Pros and cons of the options

### Option 1 — Container Apps + Flexible Server + Storage + Service Bus
- Good: scale-to-zero compute; serverless; cheapest viable Postgres with pgvector; native queue + topic support; fits the spec topology exactly.
- Bad: consumption metering is variable; two fixed monthly lines (Service Bus Standard, ACR Basic).

### Option 2 — AKS + managed Postgres
- Good: full control, future-proof.
- Bad: always-on control plane charges even at idle; violates "no production HA / idle-expensive" spirit for a no-prod V1.

### Option 3 — App Service Linux + WebJobs
- Good: simple Linux Basic plan.
- Bad: no clean separate worker scale-to-zero; Basic plan is always-on; WebJobs couples worker to the web host.

## Implications for the decomposition

- Every Terraform task must tag resources `project=contigo` and `env=dev|demo`.
- `dev` and `demo` each get their own Remote State, Resource Group, Postgres Flexible Server, Storage Account, Service Bus namespace, Key Vault, and ACR.
- Any task touching the queue must target Service Bus Standard (topics) plus the Storage Queue for simple inbox where cheap; do not introduce a second queue product.
- Any task wiring extraction MUST provision Document Intelligence S0 (`prebuilt-read` / `prebuilt-layout`) on the existing AI services account (ADR-008, ADR-017). Do not add a second AI subscription or an idle-expensive OCR cluster. Do not ship native-PDF-only extraction as the V1 path.
- Any task touching the DB must use the Burstable Flexible Server with `pgvector` and rely on `tenant_id` + RLS for isolation (see security-architect ADR).
- Do not introduce AKS, App Service, or a non-relational system-of-record.

## Assumptions

- Container Apps consumption is available in the target region (see ADR-region) and supports min-instances=0 for both apps.
- PostgreSQL Flexible Server Burstable supports the `pgvector` extension and RLS at no premium.
- Service Bus Standard topics are needed for extraction events; Basic (queues only) is the fallback if topics are not required in the initial slice.

## Amendment (2026-09-09, AI services rows)

Rows added to "Concrete services and SKUs"; the OCR row above is subsumed.
All are created by Terraform (`infra/modules/foundry`, ADR-008 amendment of
the same date).

| Concern | Service | SKU / tier | Notes |
| --- | --- | --- | --- |
| AI account (shared) | Azure AI Services, kind `AIServices` | **S0, pay-as-you-go**, no idle charge | ONE account `aisvc-contigo` in its own `rg-contigo-ai` (tags `env=shared`) -- the single deliberate exception to "one of everything per env", per ADR-008. Keys disabled; Entra-only auth. |
| Foundry projects | `Microsoft.CognitiveServices/accounts/projects` | no charge | `contigo-dev`, `contigo-demo` (account-native, no hub). |
| Chat / extraction models | Model deployments on the account | **DataZoneStandard** (EU) or **GlobalStandard**, per-1K-token, capacity in K TPM per deployment | dev: gpt-5.4-nano; demo: gpt-5.4 + gpt-5.4-nano. Provisioned SKUs rejected (fixed cost). |
| Embeddings | Model deployments | **GlobalStandard**, per-1K-token | dev: text-embedding-3-small; demo: text-embedding-3-large (dimensions forced to 1536). |
| OCR | Document Intelligence on the same account | **S0 pay-per-page**, `prebuilt-read` | No deployment resource; billed per page on the shared account. |

Shared-vs-isolated note, second line: the AI account is shared by decision
(ADR-008); isolation is per project, per-environment deployment names and
per-environment RBAC principals.

## Amendment (2026-09-10, wave w14 — mail transport: decided, not applied)

**Items served**: NW-58. **Terraform delta in wave w14: none.** This footer
records a decision so that the wave which ships invitation mail applies it
instead of re-litigating it. No w14 task follows from this section.

### What w14 actually spends

**Nothing.** No new Azure resource, no new SKU, no Terraform change, no new
runtime configuration key, no new Key Vault secret, in either environment.
The invitation lifecycle (NW-58) ships behind `IInvitationMailer` with the
default `NullInvitationMailer` (ADR-026 §D6): `mailDelivered: false` on the
201 makes "the UI must not say sent unless the mail left" a **server fact**
with no infrastructure at all. The product-owner's ADR-001 w14 footer defers
the transport (P1, not a §1.2 non-goal); this seat concurs — see "Why the
decision is recorded now but not applied" below.

Two contract properties are what keep the delta at zero, and both are
normative for the decomposition:

1. **`acceptUrl` on the invite 201 is a site-relative path in w14**, e.g.
   `/invite/accept?token=…`, resolved by the SPA against its own origin. It
   must **not** be an absolute URL. The API has no notion of the web origin:
   `var.spa_host_name` reaches `modules/containerapps` but is consumed
   **only** by the ingress CORS block (`modules/containerapps/main.tf:189`),
   and a grep for `SpaHostName|WebBaseUrl|BaseUrl|AcceptUrl` over
   `backend/src` returns zero files. An absolute URL therefore requires a new
   `Invitations__AcceptUrlBase` env var on the API Container App — and
   `backend.yml` deploys with `az containerapp update --image` **only**
   (`:198-201`, `:205-208`), never `--set-env-vars`, so that env var can only
   arrive through Terraform plus an HCP apply a human confirms in the UI
   (`infra.yml:104-135`). That is a human-gated blocker in the middle of the
   wave, bought for nothing. **The absolute accept URL is a property of the
   mail transport, not of the invitation** — a mailer has no browser to
   resolve a relative path against; a copy button does.
   `new URL(acceptUrl, window.location.origin)` accepts both shapes, so the
   client needs **no change** when the transport wave makes it absolute.
2. **No server-held invitation key in w14.** ADR-026 §D4's token is a CSPRNG
   secret stored only as a SHA-256 hash, verified by lookup against a row that
   must exist anyway (single-use, expiry, revocation). An HMAC/signed token
   would buy stateless verification this design cannot use, and would cost a
   Key Vault secret, a `random_bytes` resource, an env var (hence Terraform
   plus an HCP apply), a per-environment rotation story, and the property that
   **rotating the key invalidates every outstanding invitation**. This seat's
   own lane draft proposed `Invitations__TokenSigningKey`; it is **withdrawn**.
   Constraint on ADR-025, stated narrowly: whatever token strength the
   security-architect specifies, it must not require a server-held key in w14,
   or the wave reacquires a Terraform change and an operator-confirmed apply.

### The transport, when it ships

Chosen: **Azure Communication Services Email with an Azure Managed Domain**,
one resource per environment in that environment's own resource group. Rows
for "Concrete services and SKUs", to be added when applied:

| Concern | Service / resource type | Terraform resource | SKU / tier / meter |
| --- | --- | --- | --- |
| Communication resource | `Microsoft.Communication/CommunicationServices` | `azurerm_communication_service` | **No SKU.** `data_location = "Europe"`. Metered per message only; no idle charge. |
| Email service | `Microsoft.Communication/EmailServices` | `azurerm_email_communication_service` | **No SKU.** `data_location = "Europe"`. |
| Sender domain | `…/EmailServices/domains` | `azurerm_email_communication_service_domain` | `domain_management = "AzureManaged"` → sender `DoNotReply@<managed-domain>`. No charge. |
| Link | — | `azurerm_communication_service_email_domain_association` | No charge. |

Rejected, with reasons, so they are not re-proposed: **ACS + custom domain**
(needs DNS TXT/SPF/DKIM on a domain Terraform does not own, plus a manual
verification wait — kept as the `demo` deliverability follow-up);
**SendGrid** (third-party account and a long-lived API key for something
Azure does natively; no free tier); **Microsoft Graph `sendMail`** (needs a
licensed M365 mailbox — a per-seat cost, not an Azure meter — and the
tenant-wide `Mail.Send` application permission, narrowable only by an
out-of-band Exchange policy Terraform cannot own).

**One per environment, never shared.** ADR-008's shared `aisvc-contigo`
account is the single deliberate exception to one-of-everything-per-env
(`:95`) and it exists to amortise a fixed cost. Mail has **no fixed cost to
amortise**, so sharing would buy nothing and would blur the environment
boundary inside the recipient's inbox — a `demo` invite indistinguishable
from a `dev` one.

**Cost**: fixed **$0.00 per environment**. Metered per message only; retail
list to be confirmed by the infra cost researcher, order **$0.00025 per
email** plus **$0.00012 per MB**. At pilot volume this rounds to under
$0.01 per environment per month. **No new fixed monthly line in either
environment**, so the locked "nothing idle-expensive" rule holds with no
exception requested — ADR-005 `:56` still names Service Bus Standard and ACR
Basic as the only non-trivial fixed lines, and mail adds no third.

**Shape when applied** (so the transport wave inherits a design, not a blank
page): a new `infra/modules/communication` per ADR-007 Option 1, wired
`communication → keyvault → containerapps`; the ACS connection string as Key
Vault secret `acs-connection` → Container App secret handle `acs-cs` → env
var, read by the existing per-environment workload identity already granted
"Key Vault Secrets User" (`modules/keyvault/main.tf:48-53`) — the exact path
`postgres-connection`/`storage-connection` already take, so no new role
assignment and no cross-environment reach. Config keys on the **API app
only** (the worker neither issues nor sends invitations and must not receive
them "for symmetry"): `Invitations__Mail__Enabled`,
`Invitations__Mail__SenderAddress`, `Invitations__Mail__ConnectionString`,
and `Invitations__AcceptUrlBase` (which becomes **absolute** at that point,
from `var.spa_host_name`). All bind like `ConnectionStrings:Market`
(`Program.cs:122-128`, optional, no `?? throw`), never like the nine
fail-fast guards. Sending is gated per environment by
`var.invitation_mail_enabled`, mirroring `ai_gateway_wired` — which `dev` has
already flipped `false` → `true` (`environments/dev/variables.tf:26`) while
`demo` still sits at `false` (`environments/demo/variables.tf:23`), the
per-environment lifecycle already proven on this tenant.

**Prerequisites for that wave**: the four `azurerm_communication_*` resource
types and their attribute spellings must be read from the pinned
`azurerm ~> 4.0` schema and proved by `terraform validate` in the PR job —
they are named here as proposed, not asserted, and no identifier may be
copied out of this footer. `Invitations__Mail__SenderAddress` comes from the
module output, never a hand-composed string. Azure Managed Domain sends from
`…azurecomm.net`, which corporate mail filters treat harshly; `demo` is
stakeholder-facing, so a custom domain is a separate DNS-gated task and until
it lands the copyable link stays the guaranteed path.

### Why the decision is recorded now but not applied

At w14 acceptance the two options are **observationally identical on `dev`**:
a transport landed behind `invitation_mail_enabled = false` cannot claim
delivery either, so it buys no acceptance while inserting an operator-confirmed
HCP apply between W14-01 and the wave's only §20 item. Recording the decision
costs one footer and no task; deferring the apply costs nothing and loses
nothing, because ADR-026 §D6 makes the transport a DI registration plus a
configuration binding rather than a redesign.

## Amendment (2026-09-10, wave w14 — the accept-link example in the footer above is corrected to the fragment)

**Items served**: NW-58. **No new decision, no Terraform delta, no new task.**
This footer converges one string in this seat's own w14 footer onto ADR-025
Rule C9 — a rule accepted at the same table, in answer to a question this seat
raised. Everything above stands unchanged: the ACS design, the SKUs, the module
shape, the secret path, the per-environment flag, the prerequisites, and the
zero-delta confirmation.

**1. The accept link is `/invite/accept#<token>`, never `/invite/accept?token=…`.**
The footer above illustrates the site-relative rule at `:126` with
`/invite/accept?token=…`. That **example** is superseded; the **rule** it
illustrates — site-relative, never absolute — is unchanged and still normative
for the decomposition.

This is a correction, not a preference. ADR-025 Rule C9 (`:254-265`) is accepted
and governs the link shape: *"The token must never appear in a URL path or query
string server-side and must never be logged. The accept link is
`/invite/accept#<token>` — site-relative per ADR-005's w14 footer, and a
**fragment**, which is never transmitted to any server."* Rule C9 cites **this
footer** as its authority for the site-relative half, in the same sentence that
forbids the query string this footer's example shows. The cited authority was
the outlier: five accepted artifacts already carry the fragment (ADR-012 `:151`,
ADR-016 `:106`, ADR-018 `:185`, ADR-025 `:257`, `INDEX.md:251`), and a `Grep` for
`\?token=` across `reports/architecture/**/*.md` finds it, outside the seven lane
drafts, in exactly two places — both written by this seat.

**2. Why this is a defect and not a typo: the consequence lands in this lane.**
It is the failure this seat raised as OQ-w14-cl-01 and did not own.
`staticwebapp.config.json` rewrites every non-asset path to `/index.html`, so the
**Static Web Apps platform logs the accept path**. A query-string token is logged
with it, rides the `Referer` header to every third-party asset the accept page
loads, and persists in browser history — a live, single-use invitation token in
platform logs, on infrastructure this seat owns. A fragment is never transmitted
to any server and costs nothing. An implementer copying the superseded example
would have produced precisely the exposure the question was raised to close.

**3. The zero-delta confirmation is unaffected and both its conditions still
hold.** A fragment is site-relative by construction, so condition 1 is satisfied
more strongly, not less; the token remains CSPRNG-and-hashed with no server-held
key, so condition 2 is untouched. `new URL(acceptUrl, window.location.origin)`
resolves either shape, so no client change follows and no follow-up web task is
created. **Cost delta remains $0.00 in both environments; no new Azure resource,
SKU, Terraform module, environment key, Key Vault secret or deploy-path change.**

**4. The decision record, disclosed rather than silent.**
`reports/architecture/waves/w14.md:20` reproduced the superseded example from
this seat's own NW-58 text, where it is marked "**normative, not preferences**".
The literal `?token=…` was corrected to `#<token>` in that one sentence. That is
the only edit this seat has made to the record after the close: no other byte,
no row, no vote, no verdict and no ADR action changed. It is recorded here so
the operator sees it at HITL, because the record is the file the decomposer
reads first.

**5. What is *not* corrected here, because it is not this seat's file.**
ADR-026 §D5 defines the 201 body containing `acceptUrl` but states **no shape**
for it, and §D6 `:255` calls it "the copyable `acceptUrl`" — a phrase that reads
as absolute, since a link pasted into another channel must be. The rule is
carried by ADR-025 Rule C9 and by this footer, so the wave is governed; but the
ADR that owns the endpoint contract is silent, and silence in the owning ADR is
where an implementer defaults to the obvious reading. Raised for
**software-architect** as a note, not a demand — one clause in §D5, no other
decision moves.
