# Contigo V1 — Work-item backlog

Source of truth: `reports/architecture/INDEX.md` (17 accepted ADRs), `reports/context/product-context.md`, `reports/context/locked-decisions.md`, `inputs/product-spec.md`.

Decomposed against the **full** V1 wave ladder R0–R4 (ADR-001, spec §16), not only the R0 foundation slice. Every accepted ADR in INDEX is carried into at least one task objective.

Target: `dev` + `demo` only. No production. R3/R4 use the fixture benchmark adapter, never a paid external market API for the first `demo`.

## Wave overview

| Wave | Epic | Capability | Definition of success (spec §16) |
|------|------|------------|----------------------------------|
| R0 | epic-01 platform | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline + org/repo/Terraform/CI-CD/deployable API | A secure workspace can ingest documents |
| R1 | epic-02 Contract Intelligence | Extraction including OCR in V1 (ADR-017), schema, portfolio, Contract 360, Q&A, citations, validation | Customer can upload contracts (digital and scanned) and ask reliable questions |
| R2 | epic-03 Renewal Intelligence | Dates, cancellation deadline, alerts, dashboard, priority, recommendations (deterministic) | Procurement does not miss material renewal windows |
| R3 | epic-04 Savings Intelligence | Benchmark service/adapters, price comparison, savings dashboard/workflow | Contigo quantifies credible savings opportunities |
| R4 | epic-05 Quote Check | Quote extraction, benchmark, assessment, target, negotiation strategy | A new proposal can be assessed in minutes |

Each wave ends with a single-task `us-XX-final-integration` story. R4's integration story is the customer Day-1 path on `demo` (spec §20).

## Epics

| ID | Slug | Wave | Status |
|----|------|------|--------|
| epic-01 | platform | R0 | active — decomposed |
| epic-02 | contract-intelligence | R1 | active — decomposed |
| epic-03 | renewal-intelligence | R2 | active — decomposed |
| epic-04 | savings-intelligence | R3 | active — decomposed |
| epic-05 | quote-check | R4 | active — decomposed |
| epic-06 | web-foundation | 6 | active — decomposed (web) |
| epic-07 | web-contract-intelligence | 7 | active — decomposed (web) |
| epic-08 | web-renewals-savings-quotes | 8 | active — decomposed (web) |
| epic-09 | schema-apply | 9 | active — decomposed (schema) |
| epic-10 | demo-readiness | 10 | active — decomposed (readiness residuals) |
| epic-11 | visual-fidelity | 11 | active — decomposed (visual) |
| epic-12 | ask-copilot | 12 | active — decomposed (Ask savings copilot) |

## ADR → wave coverage

| ADR | Topic | Carried into |
|-----|-------|--------------|
| ADR-001 | V1 scope R0–R4 | epic-01..05 (wave framing, §1.2 non-goals, fixture adapter); epic-12 amendment (market corpus) |
| ADR-002 | .NET solution shape | epic-01 F04, epic-02..05 backend |
| ADR-003 | PostgreSQL + pgvector | epic-01 F02/F04 |
| ADR-004 | Foundry model roles | epic-02 (AI Gateway); epic-12 amendment (answer = savings copilot) |
| ADR-005 | Azure SKUs | epic-01 F02 |
| ADR-006 | Region west europe | epic-01 F02 |
| ADR-007 | Terraform layout | epic-01 F02 |
| ADR-008 | Foundry account shape | epic-01 F02 |
| ADR-009 | Tenancy / RLS | epic-01 F04/F05 |
| ADR-010 | Entra ID / OIDC | epic-01 F02/F05 |
| ADR-011 | Key Vault + RAG isolation | epic-01 F02/F05, epic-02 F04; epic-12 amendment (two corpora) |
| ADR-012 | Web stack | epic-01 F07, epic-02..05 web |
| ADR-013 | Mobile stack | epic-01 F08 (non-gating) |
| ADR-014 | Git flow | epic-01 F01/F03 |
| ADR-015 | CI → Azure auth | epic-01 F02/F03 |
| ADR-016 | Promotion dev→demo | epic-01 F03 |
| ADR-017 | OCR in V1 | epic-01 F02 (DI endpoint), epic-02 (AI Gateway `ocr` + hybrid parse) |
| ADR-018 | Web IA | epic-06..08 (left-rail routes, roles); epic-12 amendment (`/ask` copilot) |
| ADR-019 | Web design system | epic-06..08 (tokens, semantic mapping, states) |
| ADR-020 | Web screen inventory | epic-06..08 (screens 1–10 ↔ §16/§20); epic-12 amendment (screen 7 rich reply) |
| ADR-021 | Schema apply on Azure Postgres | epic-09 (idempotent SQL + CI apply + CA env vars) |
| ADR-022 | Day-1 demo auth + fixture seed | epic-10 (seed + Foundry/OCR CA + `demo-v*` smoke) |
| ADR-023 | Ask Contigo savings copilot | epic-12 (Foundry copilot, two corpora, rich `/ask`) |

## Non-goals (excluded, ADR-001, spec §1.2)

Full CLM/authoring · e-signature · PO/invoice management · supplier onboarding · full sourcing/RFP · ERP replacement · autonomous supplier comms · complex enterprise approval orchestration · production-only platform (AKS/multi-region/dedicated DB).

## Web delta (epic-06+, wave 6+)

The web pass (`layer: web`, `target_repo: contigo-web`) closes the user-visible
ladder that E01–E05 decomposed as backend-only. It treats E01–E05 as done and
adds the browser surface, per ADR-018/019/020 and the Claude Design handoff at
`inputs/design/prototypes/`.

Master web DAG: `reports/plan/wave-spec.web.yaml`. Web slices: `reports/plan/slices/e06.yaml` … (via `python scripts/cut_web_slices.py`). See `reports/plan/slices/INDEX-web.md` and `MANIFEST-web.yaml`.

The last web story is `us-01-final-integration` (E08/F04): a single task walking
spec §20 Day-1 in the browser on `demo`, matching `inputs/design/prototypes/day1-demo.html`.

## Status

Fully decomposed R0–R4. Master DAG: `reports/plan/wave-spec.execution.yaml`. Nightly slices: `reports/plan/slices/`.
Web delta (wave 6+) decomposed. Web DAG: `reports/plan/wave-spec.web.yaml`. Web slices: `reports/plan/slices/e06.yaml` … .
Schema-apply (wave 9) decomposed. Schema DAG: `reports/plan/wave-spec.schema.yaml`. Slice: `reports/plan/slices/e09.yaml`.
Demo-readiness (wave 10) decomposed. Readiness DAG: `reports/plan/wave-spec.readiness.yaml`. Slice: `reports/plan/slices/e10.yaml`. See `reports/plan/slices/INDEX-readiness.md` and `MANIFEST-readiness.yaml`.
Visual fidelity (wave 11) decomposed. Visual DAG: `reports/plan/wave-spec.visual.yaml`. Slice: `reports/plan/slices/e11.yaml`.
Ask savings copilot (wave 12) decomposed. Ask DAG: `reports/plan/wave-spec.ask.yaml`. Slice: `reports/plan/slices/e12.yaml`. See `reports/plan/slices/INDEX-ask.md` and `MANIFEST-ask.yaml`. Launch only after e1011 HITL (`previous: e1011`).

## Demo-readiness (epic-10 / e10)

Residuals only after e09 and e06–e08: fixture seed on `contigo_demo`,
Foundry/OCR CA wiring if live env is empty, first `demo-v*` / SWA config
smoke. Gap matrix: `reports/audit/demo-readiness-gaps.md`. HITL required
before fan-out (`reports/audit/demo-readiness-hitl.md`).

## Ask savings copilot (epic-12 / e12)

Ask Contigo becomes the domain savings copilot (ADR-023). Foundry behind
`IAiGateway`, fixture market catalog, domain gate, re-embed, rich `/ask` UI.
Gap matrix: `reports/audit/ask-copilot-gaps.md`. HITL required before fan-out
(`reports/audit/ask-copilot-hitl.md`). Do not launch in parallel with e1011.
