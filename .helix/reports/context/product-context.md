# Raffa V1 — Product Context

Source of truth: `inputs/product-spec.md` (v1.0, 25 August 2026), `inputs/engineering-brief.md` (v1.2, 1 September 2026), `inputs/engineering-constraints.md`. Section numbers below cite `product-spec.md` unless stated.

## 1. V1 jobs and user outcomes (spec §1, table)

| V1 job | User outcome | Primary value |
| --- | --- | --- |
| 1. Contract Intelligence | Upload contracts, structure key terms, ask questions with evidence. | Visibility and speed |
| 2. Renewal Intelligence | Prioritize upcoming renewals and cancellation deadlines. | Avoid leakage and late action |
| 3. Savings Intelligence | Compare current prices with market benchmarks and quantify opportunities. | Measurable savings |
| 4. New Purchase / Quote Check | Assess a supplier proposal before signature. | Negotiate before spend is locked |

North Star (spec title page): *"Raffa knows what we bought, what we pay, when we need to act, and where we can save money."*

V1 mission (spec §1): *"Give Procurement a trusted view of contracts, renewals, pricing position and savings opportunities — and make a new supplier quote assessable in minutes."*

## 2. Explicit V1 non-goals (spec §1.2, quoted verbatim)

> Full CLM and contract authoring
>
> Electronic signature
>
> Purchase order and invoice management
>
> Supplier onboarding
>
> Full sourcing / RFP platform
>
> ERP replacement
>
> Autonomous supplier communication without human approval
>
> Complex enterprise approval orchestration

Additional out-of-scope framing from `engineering-brief.md` §3: *"production-only platform (AKS, multi-region, dedicated-per-tenant DB)"* is out of scope.

## 3. Deployable topology intent (spec §5.1; engineering-brief §3)

From spec §5.1:

> **V1 architecture choice** — Deploy a modular monolith, a separate background worker, relational database, object storage and queue. Keep module boundaries explicit so services can be separated later only when scale or team ownership requires it.

Intended components:

- **Browser / Web App** (and later native mobile; web-first in V1, engineering-brief §9)
- **Backend API (modular monolith)** with modules: Identity/Workspace, Documents/Contracts, Suppliers/Products, Renewals/Savings/Quotes, Benchmark Service, AI Gateway
- **Background Worker / Queue**
- **PostgreSQL + pgvector** (relational store; spec §5.1)
- **Object Storage**
- **External Providers / Enterprise Integrations** (via the Benchmark Service abstraction; spec §10.2)

Engineering-brief §11: "Topology intent from the spec: modular monolith, worker, relational store, object storage, queue, AI gateway."

Database guideline (engineering-brief §5): SQLite acceptable on a developer laptop only; on Azure `dev`/`demo` use the cheapest managed relational store that satisfies product constraints (tenant isolation at DB level, shared API + worker, embeddings/search, durable shared storage). Non-relational store must not be the system of record.

## 4. Delivery waves R0–R4 (spec §16) with definition of success

| Release | Scope | Definition of success |
| --- | --- | --- |
| R0 — Foundation | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline | A secure workspace can ingest documents |
| R1 — Contract Intelligence | Extraction including OCR in V1 (ADR-017), schema, portfolio, Contract 360, Q&A, citations, validation | Customer can upload contracts (digital and scanned) and ask reliable questions |
| R2 — Renewals | Dates, cancellation deadline, alerts, dashboard, priority, recommendations | Procurement does not miss material renewal windows |
| R3 — Savings | Benchmark service/adapters, price comparison, savings dashboard/workflow | Raffa quantifies credible savings opportunities |
| R4 — Quote Check | Quote extraction, benchmark, assessment, target, negotiation strategy | A new proposal can be assessed in minutes |

Engineering-brief §11 ordering note: first technical slice is the platform (public `lucalamalfa91/raffa` monorepo folder layout + Terraform for `dev`/`demo` + CI/CD to both + git-flow ADR, then a deployable API). R3/R4 must not depend on a paid external benchmark API for the first `demo`.

## 5. Day-1 promise (spec §20)

Deliverables on Day 1 and after (paraphrased from spec §20):

- **Day 1:** create a workspace and invite Procurement users; upload a portfolio of contracts; automatically classify, extract and structure supported documents.
- **After processing:** ask reliable questions across the portfolio with source evidence; see renewal and cancellation deadlines; see relevant risks; see market benchmarks where data is available; see prioritized savings opportunities.
- **During a new purchase:** upload a supplier quote; receive a line-level market assessment; receive a recommended target range and potential savings; receive an explainable negotiation strategy.
- **After negotiation:** record the final negotiated outcome; track realized savings; use the outcome as permissioned proprietary learning data.

> **V1 customer promise:** Raffa knows what we bought, what we pay, when we need to act, and where we can save money.

Engineering-brief §13 ("Done when") requires: `dev` + `demo` in Azure isolated from Terraform; GitHub releases to both via council git flow; backend API + worker in both; product Day-1 path works on `demo` (workspace, upload, extract, review) with Foundry including Ask Raffa with citations or an explicit "cannot determine"; the public repository `lucalamalfa91/raffa` exists with `infra/`, `backend/`, `web/`, `mobile/`, `.helix/`; cost stays on free/cheap SKUs.

## 6. Appendix C — Developer decision rules (spec §C, short list)

1. Never store critical contract truth only inside an LLM response.
2. Never show a consequential extracted fact without source evidence and confidence metadata.
3. Never call a benchmark provider directly from renewal, savings or quote business logic.
4. Never include data in AI retrieval that the current user is not authorized to access.
5. Never destructively overwrite contract history or human corrections.
6. Prefer deterministic arithmetic/date calculations to LLM reasoning.
7. Prefer a modular monolith + workers before microservices.
8. Instrument AI, benchmark and processing cost from the first customer.
9. Capture negotiation outcomes and corrections from day one.
10. If data quality is insufficient, return uncertainty instead of fabricated precision.

Final engineering test (spec §C): *"For every architectural decision ask: Does this help Raffa build its own procurement intelligence layer, or are we simply building a UI around somebody else's API?"*

## 7. Cross-cutting constraints carried in from the brief and constraints file

- Azure only, two environments `dev` + `demo` from day one, isolated (data, identities, resource groups); no production (engineering-brief §1, §4).
- Cost: free tiers / cheapest SKUs that still satisfy the spec; nothing idle-expensive; scale-to-zero where possible (engineering-brief §1, §4).
- Benchmark is an interface + replaceable adapter; calculations that are deterministic (dates, money) stay in code, not the LLM (engineering-brief §7).
- Foundry only via AI Gateway; customer contract content must not train public/shared models; log model/version/prompt/timestamp/input hash (engineering-brief §8).
- `tenant_id` on business data; isolation in both environments; RAG must not retrieve unauthorized documents (engineering-brief §10).
- No SQLite on Azure; no shared data stores between `dev` and `demo` (engineering-constraints.md).
