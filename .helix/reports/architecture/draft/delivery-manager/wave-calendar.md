# Delivery wave order & calendar — R0 foundation and R0–R4 horizon

- **Status**: proposed (delivery-manager lane)
- **Date**: 2026-09-01
- **Owner**: delivery-manager

## Wave order (follows product §16 and brief §11)

Product §16 fixes R0→R4 as the release ladder. Brief §11 fixes the **first technical slice**:

> "the public `lucalamalfa91/raffa` monorepo (folder layout above) + Terraform for `dev` and `demo` + CI/CD to
> both + git-flow ADR, then an API that can be deployed."

So the order is: **(S0 platform) → R0 Foundation → R1 Contract Intelligence → R2 Renewals →
R3 Savings → R4 Quote Check.**

R3/R4 must not depend on a paid external benchmark API for the first `demo` (brief §11) — the benchmark
adapter starts as an internal **fixture adapter** (spec §10.2, brief §3 "fixture adapter is enough").

## Wave calendar (week numbers; no fixed start date is set by the brief)

Assumption in force (recorded in `reports/open-questions.md` as OQ-DM-006): calendar is expressed in
**sequential weeks** from an unspecified kickoff; the brief gives no absolute date and none is invented
here. Each wave is sized for the *first pass* through council + Helix implementation; durations are
planning estimates, not commitments, and are owned by the decomposition (backlog-decomposer) downstream.

| Wave | Week(s) | Environment | Deliverable at exit |
| --- | --- | --- | --- |
| **S0 — Platform** | W1–W2 | `dev` + `demo` | Public repo `lucalamalfa91/raffa` (`infra/`, `backend/`, `web/`, `mobile/`, `.helix/`); Terraform applies both envs; CI/CD to both; git-flow ADR accepted; a deployable **empty-but-authenticating API** in both envs |
| **R0 — Foundation** | W3–W5 | `dev` → `demo` (tag) | Auth/workspace/multi-tenancy/roles, upload→object storage, DB schema+migrations, queue/worker, audit baseline — "a secure workspace can ingest documents" |
| **R1 — Contract Intelligence** | W6–W9 | `dev` → `demo` | Extraction, schema, portfolio, Contract 360, Ask Raffa with citations, validation/corrections |
| **R2 — Renewals** | W10–W12 | `dev` → `demo` | Deterministic dates, cancellation deadline, threshold alerts, dashboard, priority, recommendations |
| **R3 — Savings** | W13–W15 | `dev` → `demo` | Benchmark Service + fixture adapter, price comparison, savings dashboard/workflow |
| **R4 — Quote Check** | W16–W18 | `dev` → `demo` | Quote extraction, benchmark match, market assessment, target/saving range, negotiation strategy |

Total horizon: **~18 weeks** for R0–R4 first pass, plus S0 platform (2 weeks) ahead of it. This is a
calendar *of waves*, not person-days; per-task hour estimates belong to the backlog decomposition, not
this lane.

## Dependency and sequencing notes

- **S0 must complete before R0**: nothing ships without the org/repo, Terraform `dev`+`demo`, and CI/CD
  to both; the git-flow ADR is part of S0's exit.
- **R0 enables all of R1–R4**: tenant-aware auth, storage, DB, and worker are the common substrate.
- **R1 (extraction/evidence) precedes R2/R3/R4**: renewals, savings, and quote-check all read validated
  structured data produced by R1's extraction/correction loop. R1 includes OCR in V1 (ADR-017): hybrid
  native-text + Document Intelligence behind the AI Gateway; full document; not deferred.
- **R2 before/parallel-with R3**: savings uses renewal "time urgency" in priority, but R3's fixture
  benchmark does not hard-block R2's deterministic date/cancellation logic.
- **R3 before R4**: quote-check reuses the Benchmark Service and savings range logic from R3.
- **Foundry/Gateway**: the AI Gateway (brief §8) is required from R1 onward (OCR + extraction + Ask Raffa);
  R0 may stand it up minimally or defer to R1 — the gateway's *empty deployable* lives in S0/R0, its
  first *model and OCR calls* land in R1. The Document Intelligence endpoint is provisioned with the
  Foundry/AI services account in R0 (ADR-008, ADR-017).

## Environment plan (aligned with git-flow + promotion ADRs)

- `dev`: auto-deployed on **merge to `main`** (integration).
- `demo`: promoted explicitly via **tag + `demo` environment approval** (see
  `ADR-promotion-dev-demo.md`); curated for stakeholders.
- Isolation is structural (separate resource groups, DB, storage, identities) per the locked
  environments row and brief §4; never via a shared data plane.

## Assumptions

- OQ-DM-001 — GitHub Environments with required reviewers are available on the org plan (else fallback
  to tag + `demo/*` pointer PR).
- OQ-DM-002 — `demo` approval reviewers are product-owner + security-architect during V1.
- OQ-DM-004 — OIDC subject-claim pinning restricts `demo` deploy to tag-triggered runs (fork-safe).
- OQ-DM-006 — No absolute start date exists in the brief; weeks are sequential from kickoff, not a
  named calendar date.
- OQ-DM-007 — Wave durations are first-pass planning estimates owned downstream by the decomposer;
  this lane does not commit hours or set a delivery date.
