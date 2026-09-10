# Council open questions

Items the engineering brief lists under **"Council decides"** (non-exhaustive) that remain **unanswered** and are owned by the Helix council — not the docs-ingester. Source: `inputs/engineering-brief.md` §1 ("Council decides (non-exhaustive)") and §2 (git flow). These are questions, not answers; no implied default is adopted here.

Each item: `unanswered`.

---

## CQ-001 — Git flow on the single repo

- **Status**: unanswered
- **Source**: brief §1, §2, §2.1
- **Question**: What git flow governs the one `raffa` repository — default branch, branch strategy, PRs, protections, tags, GitHub environments, approvals, rollbacks? The brief does not assume GitHub Flow, Git Flow, tags, or Environment approvals.

## CQ-002 — Exact Azure services and SKUs

- **Status**: unanswered
- **Source**: brief §1, §4
- **Question**: Which concrete Azure services and SKUs (within the cost guideline) host the web client, API/worker, object storage, queue, relational store, secrets, and identity? Brief does not prescribe Container Apps vs App Service, a queue product, log caps, or registry.

## CQ-003 — Region

- **Status**: unanswered
- **Source**: brief §4
- **Question**: Which Azure region hosts `dev` and `demo` (kept in the same region)?

## CQ-004 — Terraform module layout

- **Status**: unanswered
- **Source**: brief §6
- **Question**: What is the Terraform module layout and structure in `infra/` (double both `dev` and `demo`, remote state per environment, module/network/identity wiring)?

## CQ-005 — .NET solution shape

- **Status**: unanswered
- **Source**: brief §7, §8, §10
- **Question**: What is the ASP.NET Core solution/project layout, library choices, and API versioning scheme for the modular monolith + worker (Appendix A capabilities must exist)?

## CQ-006 — Frontend stack

- **Status**: unanswered
- **Source**: brief §1, §9
- **Question**: Which web client stack delivers the product UX against the API, and how is the web client hosted (under the cost guideline)?

## CQ-007 — Mobile stack

- **Status**: unanswered
- **Source**: brief §1, §9
- **Question**: Which mobile native stack is chosen (web-first in V1; native must not block `dev`/`demo`; `mobile/` folder still exists)?

## CQ-008 — Foundry model IDs

- **Status**: unanswered
- **Source**: brief §1, §8
- **Question**: Which exact Microsoft Foundry models (cheapest that perform classification, structured extraction, grounded Q&A with citations, embeddings) and what IDs/prices are confirmed in the target region at implementation time?
- **Answered here (no longer part of this question)**: OCR vs native parse → **ADR-017** (OCR in V1, hybrid, Document Intelligence behind the AI Gateway). One-vs-two Foundry accounts → **ADR-008** (one hub, two projects, one PAYG account).

## CQ-009 — How CI authenticates to Azure

- **Status**: unanswered
- **Source**: brief §1, §2, §6
- **Question**: How does GitHub CI/CD authenticate to Azure `dev` and `demo` (e.g., OIDC/federated credentials, service principals) without secrets in code or Terraform source?

## CQ-010 — How promotion `dev` → `demo` works

- **Status**: unanswered
- **Source**: brief §2.1
- **Question**: How is an explicit promotion from `dev` to `demo` performed (branch/tag/environment/approval mechanics)? Promotion must be explicit, not an accidental copy of every `dev` deploy; `dev` and `demo` must not share databases or document storage.

---

## Process note

These council-owned decisions are recorded as ADRs by the Helix council at council-close. Until then they stay `unanswered`; the docs-ingester does not adopt implied defaults (git flow, SKUs, models, libraries, region).
