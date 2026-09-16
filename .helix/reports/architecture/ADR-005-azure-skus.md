# ADR-005 — Azure services and SKUs for `dev` and `demo`

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: cloud-architect (owner), software-architect, security-architect, delivery-manager
- **Locked citations**: Cloud — Microsoft Azure; Environments — two isolated envs `dev`/`demo`, no production; Cost — free tiers / cheapest SKUs that satisfy the spec; IaC — HCP Terraform; AI — Microsoft Foundry via AI Gateway; Auth/secrets — Entra ID OIDC + Key Vault.

## Context and problem statement

Raffa V1 must run two isolated Azure environments (`dev` and `demo`) that satisfy the product topology intent (spec §5.1): a modular-monolith API, a background worker, a relational store with vectors/search, object storage, and a queue, plus secrets, identity, and the Foundry AI path. The brief (§4) mandates the cheapest/free SKU that still supports the product and forbids idle-expensive resources, production HA, and any shared PostgreSQL or document storage between the two environments. Every named service needs a concrete SKU so the infra cost researcher can price it at retail.

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
| OCR / document layout | Azure AI Document Intelligence (on the ADR-008 AI services account) | **S0 / pay-per-page** — `prebuilt-read` + `prebuilt-layout` | In V1 (ADR-017). No idle SKU. F0 page caps are insufficient for the 100-contract Day-1 path. Per-env endpoint via Foundry projects `raffa-dev` / `raffa-demo`. |

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

- Every Terraform task must tag resources `project=raffa` and `env=dev|demo`.
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
| AI account (shared) | Azure AI Services, kind `AIServices` | **S0, pay-as-you-go**, no idle charge | ONE account `aisvc-raffa` in its own `rg-raffa-ai` (tags `env=shared`) -- the single deliberate exception to "one of everything per env", per ADR-008. Keys disabled; Entra-only auth. |
| Foundry projects | `Microsoft.CognitiveServices/accounts/projects` | no charge | `raffa-dev`, `raffa-demo` (account-native, no hub). |
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

**One per environment, never shared.** ADR-008's shared `aisvc-raffa`
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

## Amendment (2026-09-13, wave w15 — Service Bus wired, ACS applied, and the JWT keys)

**Items served**: NW-27, NW-67, NW-68, NW-05. **Owner**: cloud-architect.
Everything above stands unchanged: every SKU, the scale-to-zero rule, the
shared-`aisvc-raffa` exception, the ACS design and its rejected alternatives,
the fragment accept link, and the two fixed monthly lines. This footer moves
Service Bus from **provisioned** to **wired**, the ACS rows from **decided** to
**applied**, and records the Azure delta of NW-05 and NW-67 so the wave's
cost claim is complete rather than silent on its largest items.

### 1. The w15 cost ruling, stated first

**New fixed monthly line in either environment: $0.00.** `:56` still names
**Service Bus Standard** and **ACR Basic** as the only two non-trivial fixed
lines, and w15 adds **no third**. Everything this wave provisions is either
already billed, SKU-less and metered per message, or a literal string on a
container app:

| Item | Azure delta | Fixed cost delta |
| --- | --- | --- |
| NW-27 | 1 Service Bus **subscription** (free), 2 topic-scoped role assignments, 1 secret handle, 1 scale rule, 2 `max_replicas` | **$0.00** |
| NW-68 | 4 **SKU-less** ACS resources, 1 Key Vault secret, 1 secret handle | **$0.00** |
| NW-67 | 1 `azuread_app_role_assignment` — no resource, no SKU, no meter | **$0.00** |
| NW-05 | **none** — four existing `modules/identity` outputs wired for the first time | **$0.00** |

**Wiring a resource the project already pays for is the cheapest capability in
this wave.** The Service Bus Standard namespace is already provisioned and
already billed in both environments (order **$0.0135/hour ≈ $9.86 per namespace
per month**, including 12.5 M operations); at pilot scale w15's traffic does not
approach the included allowance. Compute is a **relocation, not an addition** —
the same OCR / classify / extract / embed vCPU-seconds move off the API replica
onto the worker replica, both at `min_replicas = 0`. Retail rates for the cost
researcher, unchanged in kind from `:183-189`: **Azure Service Bus Standard**,
**Azure Container Apps consumption (vCPU-second / GiB-second, with the monthly
free grant)**, **Azure Communication Services Email** (order $0.00025/email +
$0.00012/MB), **Microsoft Entra External ID** (MAU free allowance).

### 2. NW-27 — Service Bus moves from provisioned to wired

`modules/servicebus` is 23 lines and creates exactly two resources: the
namespace (`:11-18`, `sbns-raffa-${var.environment}`, **`sku = "Standard"`**)
and `azurerm_servicebus_topic.extraction_events` (`:20-23`). There is **no
subscription and no authorization rule anywhere under `infra/`**, and
`modules/containerapps` references the namespace **not at all**. Three
independent gaps follow, and all three produce the *same* symptom — upload
returns 201 in under 2 s and no document ever progresses:

1. **A topic with zero subscriptions discards every message.** Service Bus
   accepts the publish and drops the message; the producer sees success. A
   producer shipped against today's Terraform would lose every document.
2. **The worker can never start.** `modules/containerapps/main.tf:231-232` sets
   the worker `min_replicas = 0` / `max_replicas = 1`, the app has **no
   ingress**, and a case-insensitive grep of the module for `scale_rule|azapi`
   returns **zero matches**. Nothing can raise its replica count.
3. **No Service Bus RBAC and no authorization rule exist**, so no identity and
   no credential can reach the data plane.

**SKU unchanged — Standard.** No tier change and no second queue product
(`:76`). **Add one subscription, keep the topic:**

| Setting | Value | Why |
| --- | --- | --- |
| name | `document-processing` on `extraction-events` | — |
| `max_delivery_count` | **`5`** | Bounded retry, then dead-letter. **Strictly greater than the application's `MaxAttempts`** — ADR-027's "the database owns the terminal state". The broker must never exhaust first, or a row stays non-terminal and A15-2 fails. The app-side number is software-architect's; the inequality is joint and normative. |
| `dead_lettering_on_message_expiration` | `true` | An expired message lands somewhere inspectable. |
| `default_message_ttl` | `P1D` | A document unprocessed for a day is a defect, not a backlog. |
| `lock_duration` | **`PT5M`** (the Service Bus maximum) | OCR + classify + extract + embed against live Foundry routinely exceeds it → the handler **must** renew; see the coupled key in §5. |
| sessions | **not enabled** | A session-enabled subscription serialises onto one ordered consumer and would silently cap the worker at one effective replica. |

**Exactly one subscription is a design constraint, not an accident** (ADR-027
§D12, reached independently from the code side): a second subscription would
process every document twice, which ADR-027 §D5's replace step would mask as
*lost work* rather than surface as an error.

**Topic + subscription, not a queue**, because it is purely additive (nothing is
destroyed or replaced), it preserves the accepted `:43` rationale for paying for
Standard, and KEDA's `azure-servicebus` scaler supports `topicName` +
`subscriptionName` natively — nothing is given up.

**Data-plane auth: RBAC, no secret.** Two `azurerm_role_assignment` in
`modules/servicebus`, scoped to the **topic** (never the namespace), principal
`var.workload_principal_id`: **`Azure Service Bus Data Sender`** and **`Azure
Service Bus Data Receiver`**. This is the path `modules/foundry` already takes
(ADR-008 `:107-110`), and it means **NW-27 adds no Key Vault secret at all**.
`RootManageSharedAccessKey` is rejected outright. Both roles sit on one
principal because there **is** one (`modules/identity/main.tf:36-46`, attached
to both apps); splitting into api- and worker-identities is a genuine
least-privilege improvement, is security-architect's call, and is **out of this
wave's budget** — it re-keys four role assignments per environment.

**Worker scaling.** `min_replicas = 0` **unchanged** (`:54`);
`max_replicas = var.worker_max_replicas` **default `3`** (from `1`); a
`custom_scale_rule` of `custom_rule_type = "azure-servicebus"` with metadata
`namespace` / `topicName` / `subscriptionName` / `messageCount = "5"`. API
`max_replicas = var.api_max_replicas` **default `3`** — not cosmetic: the web
uploads three files in flight, A15-1 drops **fifteen** and asserts p95 < 2 s,
and one 0.25-vCPU replica serialising fifteen blob writes is the likeliest way
that target fails once the pipeline itself is fixed. Scale-out on consumption is
paid only while used.

**Rejected: `min_replicas = 1` on the worker.** It is the obvious way to make a
queue consumer run and it is forbidden here. One always-on replica is
0.25 vCPU × 2.592 M s ≈ 648,000 vCPU-s and 0.5 GiB × 2.592 M s ≈ 1,296,000
GiB-s per month; net of the consumption free grant (180,000 vCPU-s / 360,000
GiB-s, already drawn on by the API) that is order **$14 per environment per
month, ~$28 across both** — a **new fixed monthly line**, exactly what the lock
forbids and what `:54-56` exists to prevent. **Recorded so it is not re-proposed
as a "simplification" when the scale rule proves fiddly.**

**Proved, not asserted** — the same discipline `:211-219` imposed on the ACS
names. How a Container Apps `custom_scale_rule` authenticates its queue-length
probe **must be read from the pinned `azurerm ~> 4.0` schema and proved by
`terraform validate` in the `infra.yml` PR job**; no identifier may be copied
out of this footer. Preference order: (1) **workload-identity auth on the scale
rule** if the pinned provider exposes it — zero secrets; (2) bounded fallback,
`azurerm_servicebus_namespace_authorization_rule` `keda-listen` with
`listen = true, send = false, manage = false` → Key Vault secret
**`servicebus-listen-connection`** → handle **`sb-listen-cs`** on the worker,
referenced **only** by `custom_scale_rule.authentication`, never by an `env {}`
block and never by application code; (3) **`min_replicas = 1` is not a
fallback** — the item returns to this seat.

### 3. NW-68 — the ACS rows move from decided to applied

The design at `:154-219` is adopted **unchanged and not re-opened**. New module
`infra/modules/communication`, one set per environment, never shared:

| Resource | Name | SKU | Region |
| --- | --- | --- | --- |
| `azurerm_communication_service` | `acs-raffa-<env>` | **none — metered per message** | global type; **no `location`**, `data_location = "Europe"` |
| `azurerm_email_communication_service` | `acsemail-raffa-<env>` | **none** | `data_location = "Europe"` |
| `azurerm_email_communication_service_domain` | Azure Managed Domain | `domain_management = "AzureManaged"` | — |
| `azurerm_communication_service_email_domain_association` | — | no charge | — |

`tags = { project = "raffa", env = var.environment }` on every taggable
resource. Outputs `connection_string` (**`sensitive = true`**) and
`sender_address` — the latter **from the module output, never hand-composed**
(`:215-216`). **Global-only is not a second region**: ADR-006's w14 footer
`:69-83` ruled this case in advance, so ADR-006 needs **no new footer**.

`modules/keyvault` gains `acs_connection_string` (sensitive),
`azurerm_key_vault_secret "acs_connection"` named **`acs-connection`** with the
**same `depends_on = [deployer_secrets_officer, workload_secrets_user]` pair**
as `postgres_connection` / `storage_connection` (`main.tf:86-89`) — that
dependency is not decoration, it is what stops the first apply racing RBAC
propagation on an `rbac_authorization_enabled` vault — and a versionless output.
`modules/containerapps` gains **one `secret {}` handle on the API app only**:
`acs-cs`. **No new role assignment**: the workload identity already holds
`Key Vault Secrets User` on its own vault and only that one
(`keyvault/main.tf:48-53`). ADR-011 gains a **secret, not a permission**.

**Ruling on security-architect's PROPOSE (S15-26): the connection string stands
for w15, and the `TokenCredential` form is recorded as the preferred later
migration.** They are right that ACS accepts an Entra `TokenCredential` and that
it would remove the secret entirely; they explicitly called either form
acceptable. The connection-string path is chosen because it is already proven
**twice** in this module (`pg-cs`, `st-cs`), it satisfies the lock verbatim
("secrets in Key Vault, never in source or bundle"), and swapping mid-wave adds
a role assignment on a resource type nobody here has probed **plus** an SDK auth
path with no coverage, against a wave already carrying three applies' worth of
Terraform. When it lands it is an **ADR-011 amendment and one role assignment**,
not a redesign — and it deletes `acs-connection`, the only secret w15 adds.

### 4. NW-67 and NW-05 — no Azure resource, and one operator act

**NW-67 adds no Azure resource, no SKU and no meter.** `modules/identity` gains
a `data "azuread_service_principal" "msgraph"`, a **`count`-gated**
`azuread_app_role_assignment` for **`User.Invite.All`** on the existing
per-environment workload identity, `var.guest_provisioning_enabled` (default
**`false`**), and a `tenant_id` output. **No secret, no app registration, no
client credential** — `DefaultAzureCredential` with the `AZURE_CLIENT_ID`
already on both apps. The `azuread` provider is already required and configured
in both roots — **no new provider**.

**The operator act, stated correctly, because the mechanism decides whether the
apply succeeds.** For a managed identity there is **no separate "Grant admin
consent" click** — the `azuread_app_role_assignment` **is** the consent. What is
genuinely one-time is upstream: the **identity that runs the HCP apply** must be
able to write app role assignments, and it cannot today. It needs Graph
`AppRoleAssignment.ReadWrite.All` + `Application.Read.All`, **or** the
**Privileged Role Administrator** / **Cloud Application Administrator**
directory role, granted by a tenant admin **before** the apply. That is an
**ADR-015** matter (delivery-manager's file): `modules/identity/main.tf:41-42`'s
"the deploy job does not need Microsoft Graph (ADR-015)" stops being true for
the **apply plane** at that moment. The narrower **Guest Inviter** directory
role instead of the tenant-wide Graph app role is **security-architect's ruling**
— a one-resource swap here either way.

**The gate is what lets all three applies ride one PR.** With
`guest_provisioning_enabled = false` by default, a missing directory permission
leaves `count = 0`, **the w15 apply still succeeds**, and NW-67 lands on a later
one-line flip. Without it, one missing directory permission would block the
Service Bus wiring and the mail transport too. Documented fallback if the
operator declines to widen the apply identity permanently: a Global Administrator
performs the single assignment out-of-band, the resource stays at `count = 0`,
and the deviation is recorded with an `import` path for a later wave. **The wave
is never blocked on a permission grant, only degraded.**

**NW-05's Azure delta is four non-secret env vars on the API app and nothing
else.** `modules/identity` **already outputs everything NW-05 needs** and
`modules/containerapps` consumes **none** of it: `api_client_id`
(`outputs.tf:12-15`), `api_identifier_uri` (`:17-23`) and `issuer` (`:25-28`)
each have **zero consumers**. The registration is already shaped for it —
`requested_access_token_version = 2` (`main.tf:74`). **No new Azure resource, no
new registration, no new scope, no SKU, no meter.**

**No `Authentication__Enabled` kill-switch, and that is the safer answer.** A key
that turns authentication off is a security control in the hands of whoever can
run an apply, and flipping it back needs **another operator-confirmed HCP
apply** — the slowest rollback in the repo. The rollback that already exists is
faster and free: `backend.yml` deploys with `az containerapp update --image`, so
reverting to the previous image SHA is one command and **no apply**. All four
keys are inert to any image that does not read them, so the apply can land days
early with zero observable effect.

### 5. Config keys per environment, named exactly as Terraform and `appsettings` expose them

**Worker** (NW-27):

| Key | Source | Binding | Note |
| --- | --- | --- | --- |
| `ConnectionStrings__Storage` | `secret_name = "st-cs"` — **new handle on the worker** | **fail-fast**, mirroring `Raffa.Api/Program.cs:66-69` | `var.storage_connection_secret_id` is **already passed from both roots** (`dev/main.tf:119`, `demo/main.tf:143`) → **no new module variable, no root change** |
| `ServiceBus__FullyQualifiedNamespace` | `module.servicebus.fqdn` | **optional** | `sbns-raffa-<env>.servicebus.windows.net` |
| `ServiceBus__TopicName` | module output, never a root literal | optional | `extraction-events` |
| `ServiceBus__SubscriptionName` | module output | optional | `document-processing` |
| `ServiceBus__MaxAutoLockRenewalMinutes` | literal `30` | optional | **coupled to `lock_duration = PT5M`** |

**API** (NW-27 publish side + NW-68 + NW-05): `ServiceBus__FullyQualifiedNamespace`,
`ServiceBus__TopicName`; `Invitations__Mail__Enabled` (`dev` **`"true"`**,
`demo` **`"false"`**), `Invitations__Mail__SenderAddress` (from
`module.communication.sender_address`), `Invitations__Mail__ConnectionString`
(**`secret_name = "acs-cs"`**), `Invitations__AcceptUrlBase`
(`"https://${var.spa_host_name}"`, **no trailing slash**),
`Invitations__GuestProvisioning__Enabled`,
`Invitations__GuestProvisioning__TenantId`; and `AzureAd__Authority`
(`module.identity.issuer`), `AzureAd__TenantId`, `AzureAd__ClientId`
(`api_client_id`), `AzureAd__Audience` (`api_identifier_uri`).

Three rules on those tables, all load-bearing:

1. **Every `ServiceBus__*` and `Invitations__*` key binds optionally, never with
   `?? throw`.** This is not a convention this seat is inventing:
   `Raffa.Worker/Program.cs` fail-fasts on exactly three connection strings
   (`:16`, `:24`, `:31`) and binds `Market` at `:44` with **no** `?? throw` and a
   documented rationale — **the optional shape is this host's own precedent for a
   dependency whose absence must degrade rather than crash.** It is what makes
   the infra→code ordering *soft* for those nine keys.
2. **`ConnectionStrings__Storage` is the boundary and must stay fail-fast.** A
   worker that silently cannot read blobs marks **every** document `Failed` — a
   config gap wearing the costume of a product defect, across a whole
   environment, with A15-2's "every row terminal" passing while every row is
   terminal-**`Failed`**.
3. **`Invitations__Mail__Enabled` is a product switch, not a provisioning gate.**
   Unlike `ai_gateway_wired` — where publishing an endpoint the account could not
   back produced HTTP 500 on every upload — publishing a *working* connection
   string behind `Enabled = false` is harmless. So resources, secret and all four
   keys land in **one** apply per environment. **No two-phase apply for mail.**
   `AzureAd__ClientId` **and** `AzureAd__Audience` are both published on purpose:
   with token version 2 the `aud` claim is the **client id** while the SPA
   requests scopes against the **identifier URI**; publishing both removes an
   entire class of "token acquired, then 401".

**Correction to a peer's finding, verified line by line, because it decides
whether the worker can call Foundry at all.** Software-architect's lane §11.3
lists the worker as missing `ConnectionStrings__Storage`, **every `AiGateway__*`
and `AZURE_CLIENT_ID`**. Only the first is true. `modules/containerapps/main.tf`
gives the worker `AZURE_CLIENT_ID` (`:277-280`), `AiGateway__Endpoint`
(`:282-285`), `AiGateway__ProjectName` (`:287-290`),
`AiGateway__DocumentIntelligenceConnection` (`:292-295`) and the same
`dynamic "env"` over `var.ai_gateway_model_env` (`:299-306`) that the API has —
placed there deliberately by task E10/F02/US01/T01, whose comment reads *"the
worker runs the hybrid OCR pre-pass (ADR-017) and needs the same non-secret AI
Gateway connection info as the api app above"* (`:274-276`). **Foundry therefore
needs nothing from w15** — no role assignment, no deployment, no key — because
the shared workload identity already holds `Cognitive Services User` +
`Cognitive Services OpenAI User` (ADR-008 `:107-110`). Acting on the uncorrected
list would add duplicate `env` names to a container app, which is an apply-time
error, not harmless noise. Recorded because *"the AI calls moved, so the AI RBAC
must move"* is a plausible and wrong inference.

**Not added to the worker, named so nobody adds them "for symmetry"**:
`ConnectionStrings__IdentityWorkspace`, `__Chat`, `__Suppliers`, `__Market`, and
every `Invitations__*` / `AzureAd__*` key — the worker neither issues nor sends
invitations (`:198-200`) and validates no token (it has no ingress). Note for
reviewers: the worker's five existing `ConnectionStrings__*` are **not** evidence
of what it reads — `AddWorkerHost` takes three, and `__Savings` / `__Quotes` are
read by nothing, as the module's own comment concedes (`:257-263`). Counting
five and concluding storage is among them is exactly the available mistake.

### 6. Two corrections to the record

- **`demo/main.tf:117-118` claims Service Bus Standard is used for "topics +
  *sessions* for extraction events".** No session setting exists on the topic and
  no subscription exists to carry one. w15's subscription **does not enable
  sessions**, deliberately (§2). Recorded so the comment is not read as a
  requirement to restore.
- **`environments/demo` has no Service Bus output** while `dev` does
  (`dev/outputs.tf:75-78`). Adding the mirror is free and makes the roots
  comparable at plan time — a nicety, not a requirement.

### 7. Finding handed to delivery-manager: a missing worker key fails **silently**

The worker's `ConnectionStrings__Storage` ordering is HARD because the worker
crash-loops — and **nothing reports it**. `backend.yml`'s only per-app loop
(`:155-191`) is a **pre-deploy** ACR registry attach, not a health check;
deployment is `az containerapp update --image` (`:196-208`), which returns when
ARM **accepts** the revision, not when it is healthy; the next step's own comment
asserts "after both Container Apps above are already **running** the new image",
a claim **nothing in the job verifies**; and there is no
`az containerapp revision show` / `runningState` / `healthState` assertion
anywhere in the workflow. **A worker that cannot start produces a green
`backend.yml` run** — the API keeps serving, uploads keep returning 201, and
documents simply never leave `Uploaded`, which looks exactly like slow
processing. The NW-27 worker task should assert **one** post-deploy revision
state for `ca-raffa-<env>-worker`; that is a `backend.yml` edit, so the clause is
**ADR-016's** and delivery-manager rules where it lives. This seat supplies the
gap, not the workflow.

### 8. Apply path and the two hard orderings

**One Terraform PR, one apply per environment — not three.** `dev`: merge to
`main` → `infra.yml` → **confirm the VCS-triggered run in HCP until it reads
`CURRENT`** (the `apply` job at `:114-135` **invokes no Terraform**; both
workspaces are VCS-connected and HCP runs plan/apply from the repo).
`demo`: the ADR-016 promotion tag → environment approval → `target_environment:
demo` → confirm on `raffa-demo`, with `invitation_mail_enabled` and
`guest_provisioning_enabled` still `false` until `demo`'s own acceptance flips
them. The bundling is safe **only because every risky resource is behind a
`count` flag or is inert when unread**.

**"The HCP run is `CURRENT`" does not prove the messaging path works.** An apply
that creates no subscription, no scale rule and no role assignment succeeds
cleanly. Three named Azure-side checks belong in the operator step: the
subscription exists on `extraction-events`; the worker app has a scale rule; the
workload identity holds both topic-scoped roles.

| Key(s) | Ordering | Consequence if violated |
| --- | --- | --- |
| `ConnectionStrings__Storage` (worker) | **HARD — apply `CURRENT` before the worker image that reads it merges** | `backend.yml` deploys the worker on **every** merge to `main`, unconditionally → the worker crash-loops on the next merge **by anyone**, and per §7 CI stays green |
| `AzureAd__*` (API, 4) | **HARD — apply before the code** | API fails to start; `dev` is down. Near-zero risk in practice: the keys are inert to today's image, so the apply can land at the very start of the wave |
| `ServiceBus__*` (5), `Invitations__*` (6) | **SOFT** | Absent value degrades to today's in-process path / `mailDelivered: false` — the behaviour that already ships |

**Two shortcuts are named drift and must not be taken** (ADR-016 clause 2,
restated because this is the first wave where the temptation is real): adding
`--set-env-vars` to `backend.yml`, and folding any of these keys into the
`dynamic "env"` block over `var.ai_gateway_model_env`, which ADR-004 binds to
per-role model ids. **The next HCP apply reverts anything CI sets behind
Terraform's back.**

**One manual, non-Terraform operator act in the whole wave**: granting the HCP
apply identity the Graph write permission (§4), before the apply. Nothing else
in this lane requires a portal.

## Amendment (2026-09-13, wave w15 — round 3: the delivery budget, the `email` claim, and three peer reconciliations)

Round 3 of the w15 table, clauses continuing at **9**. **No resource is added or
removed, no SKU changes, no module is created, and the wave's fixed-cost delta is
still $0.00 in both environments.** One Terraform *attribute* moves (§9), one
Terraform *block* that §4 failed to name is added (§11), and four clauses
reconcile with peers who ruled on this lane's asks. The §1–§8 footer above stands
as written; where a value in it is superseded the clause below says so, and the
earlier text is left intact as the record of what was decided when.

### 9. `max_delivery_count` moves `5` → `8`, because the pair is under-budgeted on *this* seat's side

`:358` set `max_delivery_count = 5` and handed the app-side number to
software-architect with the words *"the app-side number is software-architect's;
the inequality is joint and normative."* ADR-027 §C7 discharged it —
**`MaxAttempts = 3`** — and its arithmetic closes **exactly**: *"2 abandons + 3
claimed attempts = 5, with the third attempt writing the terminal row and
completing, so the ceiling is reached and never breached"* (`ADR-027:742-745`).

**Exactly is the defect.** The same clause states, one sentence earlier, that a
delivery is spent by *"any failure at or before the claim — a transient Postgres
error on the `UPDATE` itself"* (`ADR-027:736-740`): a delivery that advances no
`attempt_count` and **is not in the sum**. ADR-027 §C6 added the second such
consumer this round (bounded abandons, `:703-711`), which is what moved the worst
case to 5 in the first place. A third and a fourth exist that only this seat can
see, because this seat created them:

- **Scale-in eviction.** `min_replicas = 0` plus a KEDA queue rule (§2) means a
  replica is reclaimed as the queue drains. A message delivered to a replica
  evicted **before** its claim `UPDATE` commits spends a delivery and advances
  nothing. Scale-to-zero is not free of this; it is the price of the
  ~$14/env/month §2 declines to spend, and it is the right trade — but it must be
  in the budget.
- **The renewal ceiling.** `ServiceBus__MaxAutoLockRenewalMinutes = 30` (`:520`)
  against `lock_duration = PT5M` (`:361`) bounds renewal, not processing. An
  extraction that outruns thirty minutes loses its lock and is redelivered.

Realistic worst case: **2 abandons + 1 pre-claim failure + 1 eviction + 3 claimed
attempts = 7**, against a broker ceiling of 5. The broker therefore
dead-letters **before the handler writes `Failed`** — precisely the failure §C7
exists to prevent, and the one A15-2 reads as a non-terminal row.

**Decision: `max_delivery_count = 8`**, with the rule written as an inequality
that carries slack rather than a bare `>`:

> `max_delivery_count ≥ §C6's abandon cap (2) + MaxAttempts (3) + unclaimed-delivery slack (≥ 2)`

**What does not change, so that no other seat re-works anything.**
`MaxAttempts = 3` stands exactly as §C7 set it: this is a correction on *this*
seat's side of the pair, **not** a request to software-architect, and §C7's
inequality holds *a fortiori* (`3 < 8`). ADR-027 `:609` / `:732` and INDEX `:709`
quote `5` and are superseded by this clause alone; **no ADR body is edited and no
peer's footer is touched.**

**Why raising it is safe, and why it is not "more retries".** The database still
owns the terminal state — the handler writes `Failed` at attempt 3 and
**completes**, so on every path the handler reaches, the extra headroom is never
consumed. The outer bound on a looping message is **time, not count**:
`default_message_ttl = P1D` with `dead_lettering_on_message_expiration = true`
(`:359-360`) means a message cannot outlive a day whatever the count. The
headroom is billed as operations on a message that is already failing, against
12.5 M included in the Standard namespace: **$0.00**.

The DLQ also becomes *cleaner*, which §10 depends on: at 5 a message could reach
the DLQ merely because the broker ran out of redeliveries; at 8 the DLQ holds
§C6's explicit `job-not-found` dead-letters and genuine poison — the two meanings
§10 asks the operator to read, and nothing else.

### 10. The dead-letter hand-off is accepted, and it lands on an operator check rather than on a resource

ADR-027 §C6 hands this seat and delivery-manager a changed signal: *"a non-empty
DLQ now also means 'an upload's commit failed', not only 'a bug'"*
(`ADR-027:726-728`). **Accepted**, with no change to the subscription's settings
beyond §9's count. Two facts make the hand-off executable rather than rhetorical:

- **The dead-letter queue is durable and nothing sweeps it.**
  `default_message_ttl` (`:360`) governs the active queue; a dead-lettered
  message is not re-expired and leaves only when something explicitly receives
  and completes it. That is what makes §C6's *"visible and recoverable"* true
  rather than aspirational — nothing silently drains the evidence — and it is
  why a non-empty DLQ is a **standing condition to be read**, not an event that
  passes.
- **Draining it is an admin action inside the tenant**, per §D5's re-enqueue. No
  cross-tenant operator sweep exists, and ADR-009 forbids inventing one, so the
  operator's job is to **notice and route**, never to drain.

**§8's operator check list (`:625-629`) gains a fourth and a fifth line**, since
"the HCP run is `CURRENT`" still proves nothing about messages:

4. after the wave's backend deploy, the worker revision is the active one and has
   not failed provisioning — worded per §14, which `min_replicas = 0` constrains;
5. before acceptance the `document-processing` dead-letter queue is **empty**;
   after acceptance a non-empty DLQ is read with §C6's two meanings and routed
   (`job-not-found` ⇒ an upload's commit failed; anything else ⇒ a defect).

### 11. §4 under-stated NW-05's Azure delta: the `email` optional claim is a real Terraform addition (S15-9)

§4 records NW-05's delta as four non-secret env vars on the API app **and nothing
else**. That is now incomplete, and the missing piece is in this seat's file.
ADR-010 §2.4 (`:232-239`) requires the access token to carry `email` — the second
branch of its `oid` → `email` → 403 resolution order — and assigns the change
here explicitly: *"cloud-architect owns the file; this seat owns the
requirement."*

**Verified on the tree this round**: a repository-wide search for
`optional_claims` returns **no hit anywhere under `infra/`** — every occurrence
is in this council's own documents. `azuread_application "api"` is at
`infra/modules/identity/main.tf:57`, with `sign_in_audience` `:63`,
`identifier_uris` `:70` and `requested_access_token_version = 2` `:75`.

**Decision** — `modules/identity`, on `azuread_application.api` and nowhere else:

```
optional_claims {
  access_token {
    name = "email"
  }
}
```

- **Ungated.** No `count`, no variable. The claim serves ADR-010 §2.3 for every
  sign-in, not only for guest provisioning; tying it to
  `guest_provisioning_enabled` would make A15-4's last step depend on a flag that
  has nothing to do with it.
- **The public-client registration (`:116`) is not touched.** The access token is
  issued for the API app, so the optional claim belongs on the resource
  application only. Named because "add it to both for symmetry" is the error this
  lane has now refused four times.
- **Proved, not asserted — and this one has teeth.** The PR plan
  (`infra.yml:80-102`) must show an **in-place update (`~`)** of
  `azuread_application.api`. If it shows a **replacement (`-/+`)** the task stops
  and returns here: replacing that registration mints a **new client id**, which
  is `AzureAd__ClientId` (§5), the `aud` the API validates, the client id both
  SWAs record, and the identity `web.yml:201-205`'s hardcoded scope literals
  resolve against. A replacement is not a slower apply — it is an
  environment-wide sign-in outage behind a green CI run.
- **Ordering: SOFT.** Its absence degrades to a 403 and a re-invite (ADR-010 §2.4
  is explicit that the design does not depend on it, because `oid` is bound at
  invite time), never to a silent grant. It rides the one infra PR.
- **Cost: $0.00.** No resource, no SKU, no meter — one block on a registration
  that already exists.

### 12. The `listen`-only scaler fallback: three conditions accepted verbatim, one narrowing offered back

ADR-011 §2c (`:258-273`) accepts OQ-w15-cl-01's fallback **with three
conditions**. All three bind this seat's Terraform and are restated because they
constrain a resource this footer owns: (i) `listen = true, send = false,
manage = false`, a `manage` or namespace-wide **send** rule rejected outright;
(ii) the secret appears **only** in `custom_scale_rule.authentication`, never in
an `env {}` block and never in application code; (iii) the fallback is acceptable
**only because** ADR-009's w15 footer §5 keeps the message to ids alone, since
namespace-wide `listen` grants read on every entity **including the dead-letter
queue**, whose bodies an operator can inspect.

**One narrowing, against this seat's own draft.** The lane draft §1.3(2) — and so
the shape ADR-011 §2c reasoned about — named
`azurerm_servicebus_namespace_authorization_rule`. A **topic-scoped** rule
(`azurerm_servicebus_topic_authorization_rule` on `extraction-events`) is the
narrower instrument: `listen` on that topic and its subscriptions and on nothing
else, so condition (iii)'s residual shrinks from *every entity in the namespace,
now and in future* to *the one dead-letter queue ADR-009 §5 already bounds to
ids*.

**Subject to the same proof discipline as the scale rule itself** (§2): whether
KEDA's `azure-servicebus` scaler resolves a subscription's message count from a
topic-scoped connection string is proved by `terraform validate` plus one real
scale event on `dev`, **never** copied from this footer. If it cannot be proved,
the namespace-scoped rule stands **exactly** as ADR-011 §2c accepted it — the
three conditions do not weaken, only the scope narrows. Preference order is
unchanged: workload identity first, this fallback second, and **`min_replicas = 1`
is still not a fallback** (§2's arithmetic).

### 13. OQ-w15-ca-01 — the ingress ceiling is not pinned this wave; client-architect's 120 s stands

Client-architect asked this seat to pin the Container Apps ingress ceiling or
accept their 120 s client budget. **Their 120 s stands, and w15 pins nothing.**

**What exists**: `modules/containerapps/main.tf:179-186` declares
`external_enabled = true`, `target_port = 8080` and a `traffic_weight` block —
and **no timeout argument of any kind**. The platform default governs today and
has governed every environment this product has run in.

**Why pinning it would be wrong in this wave specifically**: NW-27 takes the long
pole *out* of the request path. After it, `POST /api/documents` returns once the
blob is stored and OCR / classify / extract / embed run on the worker, so the
longest request the API still serves synchronously gets **shorter**, not longer.
A ceiling raised now would be raised for a request that no longer exists.

**The rule recorded instead**: a synchronous request needing more than the
platform default is a design defect, not a missing knob — this wave's headline
item is that argument made once already. If a later wave does need it pinned, the
value is read from the pinned `azurerm ~> 4.0` schema and proved in the plan job
exactly like the scale rule (§2), never quoted from a council document —
including this one.

### 14. Delivery-manager's two asks confirmed, and the shape of the revision-state assertion, which `min_replicas = 0` decides

**Both asks are confirmed as binding on this seat.**

1. **The infrastructure PR contains only `infra/**`.** Confirmed — and it is also
   *sufficient*: every change this lane owns in w15 (the subscription, the two
   role assignments, the scale rule, both `max_replicas`, the `communication`
   module, the Key Vault secret, the worker's `st-cs` handle, all eleven config
   keys, the Graph assignment and §11's optional claim) lives under `infra/`, and
   `infra/README.md:18-29` — ADR-007's w15 footer §1's second layout tree — is
   **inside that path**, so it rides the same merge instead of leaking into the
   wave PR. **Nothing this seat needs touches `.github/**`**, which is what holds
   w15's CI-YAML set at **zero**.
2. **`demo`'s flags default `false`.** Confirmed: `invitation_mail_enabled` and
   `guest_provisioning_enabled` are `false` in the `demo` root (§3, §4), flipped
   only at `demo`'s own acceptance after the `demo-v5` promotion. Product-owner's
   addendum clause 12 ratifies the same two values from the scope side.

**And one correction to the clause that answers §7.** ADR-016's w15 footer puts
the post-deploy revision-state assertion **on the gate** rather than in
`backend.yml` — a better home than §7 proposed, and the reason the CI-YAML set
can stay at zero. Its *wording*, however, is decided by a choice this seat made:
**`min_replicas = 0` means a healthy worker has zero replicas at rest.**

- *"A replica of `ca-raffa-<env>-worker` is running"* **fails on a perfectly
  healthy environment** whenever the queue is empty — which is nearly always.
- *"The revision exists"* passes while the image crash-loops, which is the hole
  §7 opened in the first place.

A gate check that fails on a healthy environment is worse than no check: it gets
waived, and the waiver is what the next silent worker death hides behind. The
form that works is the assertion made against a worker **that has been given
work** — enqueue one document, then assert the row reaches a terminal state on
deployed `dev`, which is A15-2's walk and already where ADR-016 puts it. The
static half must say explicitly that **zero replicas at rest is a pass**. The
wording is delivery-manager's; the constraint is this seat's, because
`min_replicas = 0` is this seat's decision and the ~$14/env/month it saves is why
it is not negotiable.

## Amendment (2026-09-15, wave w17 — one topic-scoped role assignment, and the Worker *image* is where a rasteriser lands)

**Items served**: NW-73, NW-26. **Owner**: cloud-architect. Clauses continue at
**15** (w15 round 3 ended at §14). **No Azure resource is created or destroyed,
no SKU changes in either environment, no capacity or model deployment moves, and
the wave's fixed-cost delta is $0.00 on both `dev` and `demo`.** The entire cloud
delta of w17 is **one RBAC row**. Everything above stands verbatim; the SKU table
at `:34-57` is untouched.

### 15. NW-73 — one topic-scoped `Azure Service Bus Data Sender` for the CI deploy principal

`modules/servicebus` gains a **third** `azurerm_role_assignment`, alongside the
two of the w15 footer §2:

```
resource "azurerm_role_assignment" "ci_servicebus_sender" {
  scope                            = azurerm_servicebus_topic.extraction_events.id
  role_definition_name             = "Azure Service Bus Data Sender"
  principal_id                     = var.ci_deploy_principal_id
  skip_service_principal_aad_check = true

  lifecycle {
    ignore_changes = [skip_service_principal_aad_check, principal_type, name]
  }
}
```

**The `lifecycle` block is not decoration and is the one line a task will drop.**
Both existing assignments carry it (`modules/servicebus/main.tf:80-86`, `:95-101`)
for the reason `modules/acr` documents: **ARM rejects in-place updates to a role
assignment**. Omit it and the resource plans clean today, then fails a *later,
unrelated* apply — a trap that surfaces in someone else's wave.

**Send only.** Never `Data Receiver`, never `Manage`, never namespace scope, never
an `azurerm_servicebus_namespace_authorization_rule`, never a SAS key in Key
Vault. `modules/servicebus/main.tf:64-70` is the module's own standing rule
("identity + RBAC, never a connection string and never the namespace's default
full-access shared key") and this clause does not weaken it.

**Two peers supplied the warrant and both are recorded rather than paraphrased.**
Security-architect (S17-1): the message is a **pointer**, not content — the Worker
re-reads all authority from the database under RLS when it claims the job, and
`MessageId` collapses duplicates — so a forged or replayed message cannot make the
Worker read anything the sender could not already read; **Receive** would be
different, because a Receiver drains the deliveries the Worker depends on.
Delivery-manager (D2): `raffa-sp-<env>` already holds **Contributor on the
resource group** (`backend.yml:251`), and Contributor can call `az servicebus
namespace authorization-rule keys list` — so the SAS shortcut ADR-016 clause 30
forbids is **mechanically available today**. An explicit, topic-scoped, auditable
Send right is therefore *narrower* than the capability the principal already
carries ambiently, and it makes the forbidden path the anomalous one.

**Cost: $0.00.** Role assignments are free; no resource, SKU or capacity moves.

### 16. OQ-w17-ca-01 — the console runs on the GitHub runner (A), and the reason inverts the "zero new rights" argument

Three shapes were priced in this seat's lane: **(A)** a GitHub Actions runner as
`raffa-sp-<env>`, **(B)** an operator laptop, **(C)** a Container Apps Job under
the existing workload identity. Security-architect prefers **(C)** on the ground
that it needs **no new role assignment at all** — the workload identity already
holds Send — so "the wave buys zero new rights". **That is true and it is not the
whole measure**, and the missing half is in this seat's file:

> `modules/servicebus/main.tf` grants the workload identity **both**
> `Azure Service Bus Data Sender` (`:71-87`) **and** `Azure Service Bus Data
> Receiver` (`:89-102`) on `extraction-events`.

So under **(C)** the console would run as an identity that can **receive** from
the `document-processing` subscription — the one subscription the Worker depends
on, where a stray receive takes a message the Worker needed
(`main.tf:53-62`: a topic with no live consumer discards, and this is the only
subscription). Under **(A)** the console is granted **Send and nothing else**, on
a principal that exists only inside CI.

**The question is not how many rights the wave adds; it is what the console can
do.** (C) adds zero rights and gives the console the **wider** capability. (A)
adds one right and gives it the **narrower** one. **Ruling: (A).**
Delivery-manager's independent reason (D3) agrees and is adopted: (C) also costs a
new Terraform resource, a third image in ACR Basic, a third deploy path and a
**permanently triggerable mutation surface in Azure**, where a CI-scoped role is
reachable only through a workflow with an approval gate. **(B) is refused** — the
human principal holds no Send right, and granting one means either an out-of-band
assignment invisible to Terraform or a person's object id in source.

This clause, not the module, is where a future council re-opens the choice: **if
(C) is ever taken, §15 is dropped entirely** and the role assignment is not needed.

### 17. The variable is **required** and both roots are wired in the same change

Answering delivery-manager's D1 ask directly.

- **Name: `ci_deploy_principal_id`** — this seat's lane proposed
  `ci_publisher_principal_id` and **yields**. `modules/keyvault/variables.tf:34`
  already uses `ci_deploy_principal_id` for the same principal, so one grep finds
  every grant to it; a second spelling for one identity is how an inventory goes
  stale.
- **Required, not optional** — delivery-manager's reason is the correct one and is
  adopted: `demo-promote.yml:128-136` calls `infra.yml` with
  `target_environment: demo`, and `infra.yml` validates only the changed root's
  path filter, so a **dev-only wiring passes `dev` and breaks `demo`** — one tag
  later, in the job nobody runs weekly.
- **One line per root, no new data source.** Both roots already resolve the object
  id and already feed it to `modules/keyvault`: `environments/dev/main.tf:223-225`
  → `:236`, `environments/demo/main.tf:245-247` → `:258`. The module call sites are
  `dev/main.tf:106` and `demo/main.tf:128`.
- The new variable's description carries the **per-root isolation rule verbatim**
  from the sibling it copies (`modules/servicebus/variables.tf:22-30`): each root
  passes **its own** environment's principal, never the other's.

**Wiring is not applying, and conflating them is the error this clause prevents.**
Both roots are wired in the **same PR** (source symmetry, so `validate` is green
for both); the **apply** reaches `dev` on merge and reaches `demo` only at its
**next promotion**. This seat's lane said "demo gets the grant at its next
promotion" and delivery-manager said "wire both roots now" — those are statements
about different things and **both hold**.

### 18. Config keys per environment — w17 adds **none**

The console is **not** a container app, so `modules/containerapps` gains no `env`
block and no variable this wave. It binds the section names the deployed apps
already bind: `ServiceBus__FullyQualifiedNamespace`
(`containerapps/main.tf:195-199` ← `module.servicebus.fqdn`),
`ServiceBus__TopicName` (`:200-204`), `ConnectionStrings__DocumentsContracts` and
`ConnectionStrings__Audit` (Key Vault `postgres-connection`,
`keyvault/main.tf:81-90`). `ServiceBus__SubscriptionName` is **not needed** — the
console publishes and never receives; only the Worker carries it (`:408`).

**`AZURE_CLIENT_ID` must not be set on the runner.** It selects the user-assigned
managed identity for container apps (`containerapps/variables.tf:35`, `:159`,
`:433`); on a runner `DefaultAzureCredential` resolves through the `azure/login`
leg, and an MI client id there aims the chain at an identity absent from that
host — a failure that reads as a broken credential rather than a wrong one.

### 19. NW-26 — no Azure resource; the shared `cpu`/`memory` pair is the live risk

- **Nothing on the AI account renders a page.** Document Intelligence is attached
  as a connection (`containerapps/main.tf:174`, `:448`) and returns **geometry,
  not pixels**. **No model deployment, no capacity change, no Foundry change, no
  new resource.** If a renderer calls an Azure API for the raster it must **name
  the operation** and this seat re-prices.
- **The rasteriser is in-process and Worker-only**, so pressure lands on
  `ca-raffa-<env>-worker`, not the API.
- **The number that decides it: both apps run at 0.25 vCPU / 0.5 GiB per replica**
  (`containerapps/variables.tf:70-80`). An A4 page at 150 DPI is ≈ 1240×1754×4 B ≈
  **8.4 MB per bitmap** before allocator overhead, and the Worker runs
  **`ServiceBus__MaxConcurrentCalls = 4`** per replica (`:424-427`).
- **Binding task constraint, because it decides whether the SKU moves: render and
  store page-by-page, disposing each bitmap, never materialising a document's
  pages as a set.** Multi-page then multiplies the *work*, not the *peak*.
- **If a bump is needed it is a pair and it is shared.** Consumption accepts only a
  fixed ladder (0.25↔0.5Gi, 0.5↔1.0Gi, 0.75↔1.5Gi, 1.0↔2.0Gi) — **memory cannot be
  raised alone** — and the module exposes **one** `cpu`/`memory` pair consumed by
  *both* apps, so bumping the Worker bumps the API.
- **Pre-authorised contingency, with a named ceiling, so no new council round is
  needed if measurement disagrees**: split `modules/containerapps` into
  `worker_cpu` / `worker_memory` and raise **the Worker only to 0.5 vCPU /
  1.0 GiB**, leaving the API at the floor. Recorded then as an ADR-005 amendment
  plus an operator HCP apply. **Expected $0.00** either way: `min_replicas = 0` on
  both apps means an idle environment bills nothing, and a bump changes only the
  *rate* while a replica is alive. Retail lookups (Container Apps Consumption
  vCPU-second and GiB-second, North Europe) are owed **only if the bump is taken**.
- **Storage.** Per-page previews turn one object per document into N. On
  **StorageV2 / Standard / LRS**, 1,000 documents × 20 pages × ~200 KB ≈ **4 GB ≈
  $0.07/month** — immaterial. One rule: reprocessing **overwrites by deterministic
  path**, never accumulates a suffix, or the `documents` container grows unbounded
  and nothing deletes it today.
- **Acceptance, because a memory question answered by hope is not answered**: a
  renderer that *throws* is absorbed (`DocumentPreviewService:56-61` degrades to
  "no preview"), but one that *OOMs* kills the replica and the message is
  redelivered up to `max_delivery_count = 8` (`main.tf:53-62`) before
  dead-lettering. **NW-26's acceptance includes one 20-file batch on `dev` with
  zero Worker restarts.**

### 20. The Worker **image** — the constraint no seat had priced (the cloud half of OQ-w17-sa-04)

ADR-029 places a rasteriser in the Worker. That is not only a memory question; it
is an **image** question, and the image is this seat's.

- **Base is `mcr.microsoft.com/dotnet/runtime:10.0`**
  (`backend/src/Raffa.Worker/Dockerfile:29`), chosen over `aspnet` **deliberately**
  (`:25-28`: Generic Host, no Kestrel, "smaller, cheaper runtime image"). It is a
  **slim Debian** layer: **no `libfontconfig1`, `libfreetype6`, `libjpeg`,
  `libpng`, no `libgdiplus`**. A managed-only rasteriser needs none of them; a
  SkiaSharp/PDFium-class one does not start without them. **This decides whether
  the Dockerfile changes at all, and it is invisible from the SKU table.**
- **`USER $APP_UID` at `:34` is a hard ordering constraint**: any `apt-get` layer
  must sit **above** that line or the build fails permission-denied — and it fails
  inside **ACR Tasks** (`backend.yml:130-144`, `az acr build`), not on a runner
  where someone would see it locally.
- **Layer placement is a cost decision**: the native `RUN` sits **above** `COPY
  --from=build /app .` (`:31`) so it lands in a layer **shared by every `:<sha>`
  tag**. Below it, the libraries are re-stored on every commit.
- **ACR stays `Basic`** (`modules/acr/main.tf:24`). Native dependencies add tens of
  MB **once** to a shared layer, so **NW-26 does not move the ACR SKU**: the image
  ruling is **$0.00** too.
- **Software-architect's rule is adopted verbatim** (OQ-w17-sa-04): this council
  does **not** name the renderer package; the task's Definition of Done names the
  package, its **licence** and its **Linux native-dependency list**, and **if that
  list is non-empty the Dockerfile layer lands in the same task as the renderer** —
  never a later one, because a missing native layer fails at **runtime** with
  `Unable to load shared library`, not at build.
- **Config keys: zero.** A render knob (DPI, max pages) belongs in `appsettings` as
  `Preview__*` with a shipped default, **not** a Terraform-managed env var: a
  render default is not environment-specific, and putting it in the module would
  make every change a Terraform apply.

### 21. Recorded and deliberately **not** fixed this wave — nothing prunes ACR (OQ-w17-ca-04)

`modules/acr` declares **no `retention_policy`** (untagged-manifest retention is a
**Premium** feature and the registry is **Basic**, `main.tf:24`) and **no workflow
purges** — zero `acr purge` / `acr repository delete` across `.github/workflows` —
while `backend.yml:134,142` pushes `raffa-api:<sha>` **and** `raffa-worker:<sha>`
on every commit against Basic's included 10 GB. Growth is slow because tags share
base layers, but it is **unbounded and nothing reclaims it**. This is a
**pre-existing condition, not NW-26's to absorb**: it is recorded here so it is
not rediscovered as a surprise, no task is minted, and it is revisited when the
registry approaches its included storage.

### 22. What this wave does not change

No region change (**ADR-006 `none`** — North Europe, both environments). No
Foundry account, project, model deployment or capacity change (**ADR-008
`none`**). No Key Vault secret, no Postgres SKU or firewall rule, no storage
account change, no new container app, no new environment key, no promotion-path
change. `Invitations__Mail__Enabled` and `guest_provisioning_enabled` stay
`false` on `demo`. **Postgres reach for the console needs no firewall change**:
the only rule is `AllowAzureServices` (`postgres/main.tf:72-77`), no workflow
creates one, and four operator workflows reach it successfully today.

**Two applies, not one, are confirmed at the w17 gate** (delivery-manager's D7 and
OQ-w17-dm-03): §15's assignment, **and** ADR-016 clause 33's outstanding apply
from PR #118. HCP state is not in this tree, so this seat **asks rather than
asserts** whether the latter has landed — it is read in the HCP UI at the gate.
SKUs named for the price researcher, all **unchanged**: Service Bus **Standard**,
Postgres **B_Standard_B1ms**, Storage **StorageV2 / Standard / LRS**, Key Vault
**standard**, Container Apps **Consumption 0.25 vCPU / 0.5 GiB**, ACR **Basic**,
Static Web Apps **Free**. **No retail lookup is owed by w17.**

### 23. Where the two clauses that stop an OOM and an unbounded container actually live (round 2)

This seat's round-1 vote ratified **ADR-029** *"on condition it carries the four
clauses this lane owns"*. Verified by **reading ADR-029 in full** rather than by
trusting the ratification: **one of the four is in it** — the image layer
(`ADR-029:121-129`), attributed to this seat by name. The other three are
**here**, in §19–§20. Nothing is missing on disk and the condition holds.

**The placement is the finding.** The two clauses that decide whether the Worker
OOMs and whether the `documents` container grows forever —

- *render and store page-by-page, disposing each bitmap, never materialising a
  document's pages as a set* (§19), and
- *reprocessing overwrites by deterministic path, never accumulates a suffix* (§19)

— live in the **Azure SKU ADR**. The engineer who writes the render loop opens
**ADR-029**, the *document page rendering* ADR, whose "Implications for the
decomposition" (`:148-162`) lists the files to touch and says **nothing** about
the shape of the render loop or the overwrite path. Read alone, ADR-029 points
the other way: its Consequences record "**storage grows per page per document**"
(`:142-144`) as an accepted cost, with no overwrite rule attached to it.

**Binding on the decomposer**: both clauses are copied into **NW-26's Definition
of Done**, beside the renderer package / licence / native-dependency list that
ADR-029 already requires there — **as the task's own words, not as a citation to
ADR-005**. A capacity rule reachable only from the cost ADR is a rule that gets
discovered by an incident.

This seat does **not** edit ADR-029: it is software-architect's, the protocol
gives the owning seat the pen, and the clause is not in dispute — only its
**reachability** is.

### 24. NW-26 × NW-73 — this wave ships a page renderer and a bulk re-render trigger together

Neither item's row carries this, because it exists only in the composition of the
two.

**NW-73 is a whole-tenant reprocess console. A reprocess re-runs the pipeline,
and after NW-26 the pipeline rasterises.** So w17 ships, in one wave, the thing
that writes pages and the thing that rewrites all of them at once — and the
second is the **largest concurrent render this product will ever have run**.

**Verified, not assumed**: `infra/modules/storage` declares **no
`management_policy`, no lifecycle rule, no `delete_retention`, no versioning** —
zero matches across the module. Nothing prunes blob storage, and unlike ACR (§21)
there is not even a Premium feature being declined: the policy simply does not
exist. So §19's overwrite rule is not a tidiness preference — **a
non-deterministic page key turns one bulk reprocess into a permanent doubling of
the container, reclaimable only by hand.** At pilot scale the money is still
cents, which is the point: the cost of getting this wrong is **operational, not
financial**, and that is exactly why a cost ADR is the wrong and currently only
place it lives (§23).

**Order constraint, this seat's because it is a capacity question**: §19's
measurement — one 20-file batch on `dev` with zero Worker restarts — runs
**before** the first whole-tenant reprocess on `dev`, never after. A bulk run is
**not** a substitute for the measurement: `MaxConcurrentCalls = 4` per replica
with `min_replicas = 0` means a tenant-sized queue scales replicas out and
multiplies the concurrent bitmaps, so a bulk run that survives proves less than
it appears to, and one that fails burns `max_delivery_count = 8` across every
document of the tenant at once.

### 25. `demo` under product-owner's clause 9 — what the promotion carries for this seat

ADR-001 w17 clause 9 rules that **`demo` is not dormant and its promotion is not
deferred a fourth time**. That is a priority ruling and the mechanism stays
ADR-016's. Its consequences on this seat's plane:

- **§17 is unchanged and is now load-bearing rather than theoretical**: both roots
  are wired in the same PR, `dev` applies on merge, **`demo` applies at its
  promotion** — which clause 9 makes *this* wave's business rather than an
  indefinite "next time".
- **The renderer reaches `demo` at that same promotion**, onto the **same shared
  0.25 vCPU / 0.5 GiB pair** and the same unpruned storage account. `demo` is
  where `percorso-pilota-v1.md` is run **for a client**, so it is the worst place
  to discover the memory question.
- **If §19's contingency bump is taken, it is wired in both roots in the same
  PR** — §17's rule applied to `worker_cpu` / `worker_memory` instead of to a
  principal id. A bump wired only in `dev` passes `dev` and leaves `demo` running
  the pilot at the floor with a renderer already measured as needing more: the
  same failure shape as §17's, one variable later.
- **Still $0.00**: `min_replicas = 0` on both environments, and a bump changes
  only the *rate* while a replica is alive.

### 26. §18 re-verified against an item that arrived after it was written

§18 ruled **w17 adds no environment key**, reasoning from NW-73 and NW-26.
Security then permitted NW-20's `activity` projection (OQ-w17-sa-03), which makes
`Raffa.Api` read the **audit trail** — the one round-2 decision that could have
added a key to `modules/containerapps`. **It does not**:
`ConnectionStrings__Audit` is already bound on the API
(`containerapps/main.tf:90`) and on the Worker (`:354`), from the same
`postgres-connection` secret. **§18 stands — zero new environment keys in w17**,
now verified against every item at this table rather than against the two this
seat was rostered on.

### 27. Correction (round 3) — §25's "`demo` applies at its promotion" is wrong

§25 `:1189-1190` reads "`dev` applies on merge, **`demo` applies at its
promotion**". **The second half is false.** Delivery-manager raised it (round 2,
finding (i)); it was verified first-hand and the corrected rule is written in
**ADR-007 w17 §9**, which owns the apply path. In one line: *one merge to `main`
touching `infra/` queues a VCS run on **both** `raffa-dev` and `raffa-demo`; no
promotion and no `demo-v*` tag applies any Terraform* (`infra.yml:114-136`,
`demo-promote.yml:123-127`, `hcp_vcs_wiring.py:104-106`).

What it changes on **this** ADR's plane — cost and SKUs — and what it does not:

- **The cost line does not move: still $0.00/month on both environments.** The
  wave's delta is one RBAC row, and a role assignment is free wherever and whenever
  it applies. **No SKU, region, Foundry or environment-key ruling in this footer
  depends on the apply path**, which is why this is a correction of record and not
  a re-pricing.
- **§17's "both roots in the same PR" is reinforced**, not weakened — see ADR-007
  §9: a dev-only wiring fails *sooner and more quietly* than §25 assumed.
- ⚠ **§25's contingency bullet is the one that actually changes.** The renderer
  reaches `demo` in the **promotion** (that is `backend.yml` pushing an image, which
  the promotion really does do), but a **`worker_cpu` / `worker_memory` bump is
  Terraform**, so it lands on `demo` **at the merge** — *ahead of* the image that
  needs it. The contingency's two halves therefore reach `demo` **at different
  moments, in that order**, which is the **reverse of `dev`**, where one merge
  carries both. Harmless in money (`min_replicas = 0`; a bump changes only the
  *rate* while a replica is alive) but it must not be discovered as a surprise
  during the client pilot: the raised SKU sits on `demo` first, doing nothing, and
  that is expected rather than a failed apply.
