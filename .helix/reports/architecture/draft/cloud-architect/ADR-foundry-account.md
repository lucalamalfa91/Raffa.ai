# ADR-NNN — Foundry account shape and billing (one vs two accounts)

- **Status**: proposed
- **Date**: 2026-09-01
- **Deciders**: cloud-architect (co-owner), software-architect (co-owner)
- **Locked citations**: AI — Microsoft Foundry only, via AI Gateway; use cheapest Foundry models for the product tasks; Cost — free/cheapest; Environments — `dev`+`demo` isolated.

## Context and problem statement

All model I/O flows through the Raffa AI Gateway (brief §8), and Foundry is the only provider. The brief (§4) explicitly leaves **one-vs-two Foundry accounts and billing** to the council, under the cost guideline. The decision must balance isolation (contract content in `demo` must not touch public/shared models) against cost (a second Foundry/AI services subscription could add fixed charges).

## Decision drivers

- **Isolation of content**: customer contract content must not train public/shared models, and `dev` vs `demo` should not accidentally share a model-deployment surface.
- **Cost**: free tier where it exists; avoid paying for two AI services subscriptions if one project/connection group suffices.
- **Operational simplicity**: a single Foundry hub with per-environment connections is easier to run than two hubs.

## Considered options

1. **One Azure AI Foundry hub, two projects (one per env), shared under one pay-as-you-go AI services subscription** — isolation via distinct projects/connections, single billing surface.
2. **Two separate Foundry hubs/accounts (one per env)** — maximal isolation, possibly double billing.
3. **One Foundry project shared across both envs** — simplest, weakest isolation.

## Decision outcome

**Chosen: Option 1** — a single Azure AI Foundry hub with **one project per environment** (`raffa-dev` and `raffa-demo`), under a **single pay-as-you-go Azure AI services account**, because it gives per-environment logical isolation (distinct model deployments, connections, and audit trails) without paying for two AI services subscriptions, staying within the cheapest-SKU mandate. Foundry has no meaningful free tier for inferencing, so billing is usage-based (pay-per-token) on one account with no fixed idle charge.

### Consequences

- **Good**: no fixed second-subscription cost; per-environment project isolation keeps `dev` and `demo` content and deployment configs separate; single billing/usage view for the cost researcher.
- **Bad**: the AI services account is a shared billing boundary — a runaway in either project hits one quota/budget; isolation is logical (projects/connections), not physical (separate account).
- **Neutral**: model deployments are duplicated per environment (each project pins its own model IDs), which is necessary for isolation and clean promotion but adds a small amount of config.

## Foundry footprint and SKUs

| Concern | Resource | SKU / tier | Notes |
| --- | --- | --- | --- |
| Foundry control surface | Azure AI Foundry **Hub** | One hub, **no hub-level SKU charge** (hub is metadata) | One hub for the whole org. |
| Per-env isolation | Azure AI Foundry **Project** | Two projects (`raffa-dev`, `raffa-demo`), **no project-level charge** | Distinct deployments, connections, audit. |
| Inference billing | Azure AI Services account | **Pay-as-you-go (standard / S0)** — metered per 1K tokens; no free tier for model inference | One shared account; usage attributed per project. |
| Deployments | Foundry model deployments | **Serverless / standard deployment** — check regional availability in `westeurope` | Exact model IDs + pricing confirmed jointly with software-architect (CQ-008). |
| Embeddings | Foundry embedding model | **Pay-per-token** | Cheapest compatible embedding model, e.g. text-embedding-3-small (confirm at implementation). |
| OCR / layout | Azure AI Document Intelligence | **S0 / pay-per-page** on the same AI services account | V1 capability (ADR-017). `prebuilt-read` + `prebuilt-layout`; per-project connection (`raffa-dev` / `raffa-demo`). No second account. |

> Note: HCP Terraform does **not** fully manage Foundry projects/deployments in V1; the gateway reads endpoints + managed identity to call Foundry. Terraform manages the identity/Key Vault/connection secrets that let the AI Gateway authenticate to the Foundry account (see security-architect ADR). Model deployment may be a one-time Azure-based or portal step recorded as an implementation task, not part of the Terraform module surface initially.

## Pros and cons of the options

### Option 1 — one hub, two projects, one AI services account
- Good: no fixed duplicated cost; per-env isolation via projects; single usage view.
- Bad: shared billing boundary; logical (not physical) isolation.

### Option 2 — two hubs/accounts
- Good: strongest physical isolation.
- Bad: potentially double AI-services spend; more ops; likely violates the cheapest-SKU mandate for a no-prod V1.

### Option 3 — one shared project
- Good: simplest.
- Bad: `dev` and `demo` content/config not isolated; weakest audit; risks cross-env leakage.

## Implications for the decomposition

- Any task wiring the AI Gateway must target distinct Foundry **projects** per environment (`raffa-dev` and `raffa-demo`) under a single hub and a single pay-as-you-go AI services account.
- Do not create two AI services accounts/subscriptions; attribute usage per project for the cost researcher.
- Model IDs/prices must be confirmed for `westeurope` before pinning (CQ-008); pick the cheapest that meets extract / embed / grounded-Q&A-with-citations **and** Document Intelligence Read/Layout for V1 OCR (ADR-004, ADR-017; jointly with software-architect).
- The AI Gateway reads Foundry endpoint + credentials via managed identity/Key Vault; no model key in Terraform source or app code.

## Assumptions

- Azure AI Foundry (hub + projects) and Azure AI Services pay-as-you-go are available in `westeurope`.
- Foundry has no meaningful free inference tier, so a single pay-as-you-go account is the cheapest compliant option.
- Per-project deployment isolation satisfies the brief's isolation requirement without a second account.
