# ADR-006 — Azure region for `dev` and `demo`

- **Status**: accepted (amended 2026-09-04)
- **Date**: 2026-09-01
- **Deciders**: cloud-architect (owner)
- **Locked citations**: Cloud — Microsoft Azure; Environments — `dev` and `demo` in the same region (brief §4: "Region: council (keep `dev` and `demo` in the same region)").

## Context and problem statement

Both Azure environments must live in one region to keep latency, data-residency reasoning, and Terraform plumbing identical, while honoring the cost guideline and no-multi-region constraint. The brief does not prescribe a region; the council must pick one that (a) supports all the SKUs in ADR-azure-skus — notably Container Apps consumption, PostgreSQL Flexible Server Burstable with `pgvector`, Foundry model serving, and Service Bus Standard — and (b) remains cheap and low-latency for the stakeholder-facing `demo`.

## Decision drivers

- **Service coverage**: the region must host Container Apps (consumption), Flexible Server with pgvector, Foundry model endpoints for the cheapest models, Service Bus Standard, and Entra ID.
- **Latency / reach**: `demo` is stakeholder-facing; prefer a region with good general latency to the expected audience without paying multi-region cost.
- **Cost stability**: pick a region with stable pricing for the chosen SKUs; avoid newly launched regions with incomplete feature coverage.
- **Subscription eligibility**: the chosen region must accept new customers on this tenant (Azure `locationineligible` / `RequestDisallowedByAzure`).

## Considered options

1. **West Europe (westeurope)** — mature region, full coverage of all required services.
2. **North Europe (northeurope)** — equivalent maturity, slightly different peering/latency profile.
3. **East US (eastus)** — mature and full-coverage but transatlantic latency for a Europe-based team/stakeholders.

## Decision outcome

**Chosen: Option 2 — North Europe (`northeurope`)**.

The 2026-09-01 council chose West Europe. First apply on this tenant failed with `RequestDisallowedByAzure` / [locationineligible](https://learn.microsoft.com/en-us/azure/azure-resource-manager/troubleshooting/error-region-access-policy): Microsoft is not accepting new customers in West Europe when the tenant has no existing resources there. North Europe was already the documented fallback (same SKU coverage, still EU latency) and unblocks `dev`/`demo` without splitting regions.

### Consequences

- **Good**: single region for both envs keeps Terraform, remote state, and identity wiring identical; every required SKU is available; no multi-region spend; deployable on this subscription.
- **Bad**: regional outages affect both `dev` and `demo` simultaneously (acceptable for a no-prod V1); single region means no provider diversity.
- **Neutral**: Foundry model IDs must be confirmed as available in `northeurope` at implementation time (jointly with software-architect on CQ-008).

## Pros and cons of the options

### west europe
- Good: full, mature service + model coverage; stable pricing; low latency for EU stakeholders.
- Bad: currently `locationineligible` for new customers on this tenant; apply cannot create Storage, Container Apps, Postgres, ACR, Key Vault, Service Bus, VNet, or managed identity there.

### north europe
- Good: near-identical coverage to West Europe; EU latency; eligible for new resources on this tenant.
- Bad: slightly different peering/latency profile than Amsterdam; Foundry model IDs must be re-checked here (CQ-008).

### east us
- Good: full coverage.
- Bad: transatlantic latency for `demo`; no cost advantage.

## Implications for the decomposition

- All Terraform providers/remote-state must pin `location = "North Europe"` (or the canonical `northeurope`) for both `dev` and `demo`.
- Any task that confirms Foundry model IDs must check `northeurope` availability and price before pinning (CQ-008).
- Do not introduce a second region for either environment.

## Assumptions

- All services named in ADR-azure-skus are GA in `northeurope` (Container Apps consumption, PostgreSQL Flexible Server with `pgvector`, Service Bus Standard, Foundry model serving).
- The primary `demo` audience is European, making `northeurope` an acceptable latency choice once West Europe is ineligible.

## Amendment (2026-09-10, wave w14 — global-only resource types are not a second region)

**Items served**: NW-58 (the deferred mail-transport decision recorded in the
ADR-005 w14 footer). **No w14 task follows from this footer**; it closes a gap
between this ADR and the deployed tree that has been open since the first
apply. `northeurope` is unchanged and is not re-opened.

**Rule.** Some Azure resource types are **global**: they take no `location`
and instead carry a `data_location` property that pins where their data
rests. A global-only type is **not** "a second region" in the sense
`:55` forbids, and it cannot take the `northeurope` pin because the property
does not exist on it. Such a type carries `data_location = "Europe"`. Every
**regional** resource in both environments continues to pin
`location = "North Europe"` / `northeurope`, without exception.

Two cases, one already in force and never written down:

| Case | Status | Evidence |
| --- | --- | --- |
| **Azure Static Web Apps** (`Microsoft.Web/staticSites`) | **In force since the first apply, undocumented until now.** The module states in its own comment that the type "is not offered in North Europe … This module therefore does NOT inherit the env-root North Europe pin (ADR-006)", and both environment roots repeat it. This ADR said the opposite at `:55` and carried no footer recording the exception. | `infra/modules/staticwebapp/main.tf:6-9`; `environments/dev/main.tf:64-65`; `environments/demo/main.tf:75-76` |
| **Azure Communication Services / Email Services** | **Decided, not applied** (ADR-005 w14 footer). Global types with `data_location`; when the transport ships they carry `data_location = "Europe"`. | ADR-005 `## Amendment (2026-09-10, wave w14 …)` |

**Why this is recorded rather than left to a module comment**: an accepted ADR
that contradicts the deployed tree is a fact the system does not hold. The
next seat to reason about region would have read `:55` and either re-litigated
a settled deviation or blocked a correct one. Recording the rule costs one
footer; leaving it costs a wave.
