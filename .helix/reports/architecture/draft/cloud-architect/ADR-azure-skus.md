# ADR-NNN — Azure services and SKUs for `dev` and `demo`

- **Status**: proposed
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
