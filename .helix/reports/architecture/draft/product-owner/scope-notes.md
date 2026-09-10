# Product-owner scope notes — Raffa V1

Author: product-owner (independent lane). Source of truth: `inputs/product-spec.md`
(§1, §1.1, §1.2, §3, §4, §16, §17, §20) and `inputs/engineering-brief.md` (§3, §11,
§13). These notes become the seed for user stories during decomposition; they do not
decide platform (SKU/region/git flow/stack), which other seats own per the locked table.

## 1. V1 jobs (what the product must deliver)

| Job | User-visible outcome |
| --- | --- |
| 1. Contract Intelligence | Upload contracts → structured key terms → ask questions with source evidence. |
| 2. Renewal Intelligence | See upcoming renewals and cancellation deadlines, prioritized. |
| 3. Savings Intelligence | Compare current price to market benchmarks; see quantified savings opportunities. |
| 4. New Purchase / Quote Check | Assess a supplier proposal before signature, in minutes. |

All four jobs are in V1 scope. They are delivered across R0–R4 (see the ADR), but
the Day-1 path (workspace, upload, extract, review, Ask Raffa) must work on `demo`
first per brief §13.

## 2. Personas and roles (spec §3.1)

- **Workspace Admin** — workspace config, users/roles, uploads/deletion, integrations,
  all contracts, audit logs.
- **Procurement** — contracts, spend, renewals, benchmarks, savings, quote checks,
  negotiation recommendations (the primary V1 end user).
- **Legal** — clauses, risks, liability, obligations, termination, evidence.
- **Finance** — spend, financial obligations, payment terms, savings.
- **Read-only / Business** — authorized search and Q&A without editing.

Multi-tenancy is hard: every business object carries `tenant_id`; isolation enforced at
app **and** DB level; no cross-tenant query path; RAG retrieval respects authorization
(spec §3.2, §14). These are locked/spec-level constraints, not council scope decisions.

## 3. Non-goals (spec §1.2, verbatim — these stay OUT of V1)

Full CLM and contract authoring; Electronic signature; Purchase order and invoice
management; Supplier onboarding; Full sourcing / RFP platform; ERP replacement;
Autonomous supplier communication without human approval; Complex enterprise approval
orchestration.

Plus, from brief §3: production-only platform (AKS, multi-region, dedicated-per-tenant
DB) is out of scope. Only `dev` + `demo` exist.

## 4. Wave slice — user-visible ladder (spec §16 + §20)

| Release | User-visible scope | Definition of success (verbatim intent) |
| --- | --- | --- |
| **R0 — Foundation** | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline | A secure workspace can ingest documents |
| **R1 — Contract Intelligence** | Extraction **including OCR in V1** (ADR-017), schema, portfolio, Contract 360, Q&A, citations, validation | Customer can upload contracts (digital and scanned) and ask reliable questions |
| **R2 — Renewals** | Dates, cancellation deadline, alerts, dashboard, priority, recommendations | Procurement does not miss material renewal windows |
| **R3 — Savings** | Benchmark service/adapters, price comparison, savings dashboard/workflow | Raffa quantifies credible savings opportunities |
| **R4 — Quote Check** | Quote extraction, benchmark, assessment, target, negotiation strategy | A new proposal can be assessed in minutes |

Ordering note (brief §11): the **first technical slice is platform** — public
`lucalamalfa91/raffa` monorepo + Terraform `dev`/`demo` + CI/CD + git-flow ADR, then a deployable API.
Product waves R0–R4 then build on that. R3/R4 must **not** depend on a paid external
benchmark API for the first `demo` (see ADR-scope-r0-r4.md).

## 5. Day-1 promise (spec §20) — what `demo` must show

- **Day 1:** create workspace, invite Procurement users, upload a portfolio of
  contracts, auto-classify/extract/structure supported documents.
- **After processing:** reliable cross-portfolio Q&A with evidence; renewal and
  cancellation deadlines; relevant risks; market benchmarks where data exists;
  prioritized savings opportunities.
- **New purchase:** upload quote → line-level market assessment → target range +
  potential savings → explainable negotiation strategy.
- **After negotiation:** record outcome, track realized savings, use outcome as
  permissioned learning data.

North Star (spec title): *"Raffa knows what we bought, what we pay, when we need to
act, and where we can save money."*

## 6. Acceptance hooks (spec §17, §Appendix C) for later stories

- **Contract Intelligence (§17.1):** 100 contracts processable; key dates/commercials/
  auto-renewal/clauses extracted; evidence shown; corrections stored; cross-portfolio Q&A
  works.
- **Renewal (§17.1):** deterministic renewal/cancellation calculation where data exists;
  threshold events; recommendations must not invent dates (Appendix C #6).
- **Savings (§17.1):** matched contracts show current price, P25/P50/P75 where available,
  percentile, target range, saving, confidence + provenance.
- **Quote Check (§17.1):** quote PDF → line items → benchmark match → assessment → savings
  range → negotiation recommendation; user can correct SKU matching.

Appendix C rules every story must honor (short list):
1. Never store critical contract truth only inside an LLM response.
2. Never show a consequential fact without source evidence + confidence.
3. Never call a benchmark provider directly from renewal/savings/quote logic.
4. Never include unauthorized data in AI retrieval.
5. Never destructively overwrite contract history or human corrections.
6. Prefer deterministic arithmetic/date calc to LLM reasoning.
7. Prefer modular monolith + workers before microservices.
8. Instrument AI/benchmark/processing cost from first customer.
9. Capture negotiation outcomes and corrections from day one.
10. If data quality is insufficient, return uncertainty, not fabricated precision.

Final test (Appendix C): *"Does this help Raffa build its own procurement intelligence
layer, or are we simply building a UI around somebody else's API?"*

## 7. Boundaries this seat does NOT own

Git flow, Azure services/SKUs, region, Terraform layout, .NET solution shape, frontend
stack, mobile stack, Foundry model IDs, CI→Azure auth, `dev`→`demo` promotion mechanics —
these are the other seats' ADRs (see `reports/context/council-open-questions.md`). This
seat's scope ends at user-visible waves, jobs, personas, non-goals, and acceptance.
