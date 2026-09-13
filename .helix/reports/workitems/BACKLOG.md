# Raffa V1 — Work-item backlog

Source of truth: `reports/architecture/INDEX.md` (17 accepted ADRs), `reports/context/product-context.md`, `reports/context/locked-decisions.md`, `inputs/product-spec.md`.

Decomposed against the **full** V1 wave ladder R0–R4 (ADR-001, spec §16), not only the R0 foundation slice. Every accepted ADR in INDEX is carried into at least one task objective.

Target: `dev` + `demo` only. No production. R3/R4 use the fixture benchmark adapter, never a paid external market API for the first `demo`.

## Wave overview

| Wave | Epic | Capability | Definition of success (spec §16) |
|------|------|------------|----------------------------------|
| R0 | epic-01 platform | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline + org/repo/Terraform/CI-CD/deployable API | A secure workspace can ingest documents |
| R1 | epic-02 Contract Intelligence | Extraction including OCR in V1 (ADR-017), schema, portfolio, Contract 360, Q&A, citations, validation | Customer can upload contracts (digital and scanned) and ask reliable questions |
| R2 | epic-03 Renewal Intelligence | Dates, cancellation deadline, alerts, dashboard, priority, recommendations (deterministic) | Procurement does not miss material renewal windows |
| R3 | epic-04 Savings Intelligence | Benchmark service/adapters, price comparison, savings dashboard/workflow | Raffa quantifies credible savings opportunities |
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
| epic-12 | ask-copilot | 12 | superseded by epic-13 (never launched; ADR-023 → ADR-024) |
| epic-13 | ask-v2 | 13 | active — decomposed (Ask Raffa V2: Documents intake + admission gate, conversations, market feed, strategies, capability catalog, V2 IA) |

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
| ADR-023 | Ask Raffa savings copilot | epic-12 (Foundry copilot, two corpora, rich `/ask`) |

## Non-goals (excluded, ADR-001, spec §1.2)

Full CLM/authoring · e-signature · PO/invoice management · supplier onboarding · full sourcing/RFP · ERP replacement · autonomous supplier comms · complex enterprise approval orchestration · production-only platform (AKS/multi-region/dedicated DB).

## Web delta (epic-06+, wave 6+)

The web pass (`layer: web`, `target_repo: raffa-web`) closes the user-visible
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

Residuals only after e09 and e06–e08: fixture seed on `raffa_demo`,
Foundry/OCR CA wiring if live env is empty, first `demo-v*` / SWA config
smoke. Gap matrix: `reports/audit/demo-readiness-gaps.md`. HITL required
before fan-out (`reports/audit/demo-readiness-hitl.md`).

## Ask savings copilot (epic-12 / e12)

Ask Raffa becomes the domain savings copilot (ADR-023). Foundry behind
`IAiGateway`, fixture market catalog, domain gate, re-embed, rich `/ask` UI.
Gap matrix: `reports/audit/ask-copilot-gaps.md`. HITL required before fan-out
(`reports/audit/ask-copilot-hitl.md`). Do not launch in parallel with e1011.

## Epics (continued) — next-wave process

Appended by `next-decomposer`. The `## Epics` table above is append-only as a
prefix (`scripts/assert_next_plan_untouched.py`), so waves cut by
`raffa-next-process.yaml` add their rows here.

| ID | Slug | Wave | Status |
|----|------|------|--------|
| epic-14 | workspace-identity | w14 | active — decomposed (next-wave) |
| epic-15 | workspace-invitations | w14 | active — decomposed (next-wave) |

## ADR → wave coverage (continued) — next-wave process

| ADR | Topic | Carried into |
|-----|-------|--------------|
| ADR-001 | V1 scope R0–R4 | epic-15 (w14 footer: the invite domain rule is deferred to the ADR-010 wave) |
| ADR-003 | PostgreSQL + pgvector | epic-14 F01 (w14 footer: `industry` / `country` / `currency` on `workspace`) |
| ADR-005 | Azure SKUs | epic-15 (w14 footer: mail transport decided — ACS Email + Azure Managed Domain — and **deferred**; w14 delta zero) |
| ADR-006 | Region west europe | epic-15 (w14 footer: global-only resource types are not a second region) |
| ADR-009 | Tenancy / RLS | epic-14 F01/F03/F04 (w14 footer: the `identity_self` policy, verify-then-scope, bootstrap, the two named scope exceptions) |
| ADR-010 | Entra ID / OIDC | **not wired in w14** — NW-05 queued to W15 (w14 footer: the token carries identity only) |
| ADR-011 | Key Vault + RAG isolation | epic-15 (w14 footer: no Key Vault entry for the invitation token; authz-before-retrieval) |
| ADR-012 | Web stack | epic-14 F03, epic-15 F02 (w14 footer: a client store never stands in for a missing GET) |
| ADR-014 | Git flow | epic-14 F06 (w14 footer: wave base and integration branch, the green-base proof) |
| ADR-015 | CI → Azure auth | epic-14 F05 — `none`: no new identity, federated credential or stored secret |
| ADR-016 | Promotion dev→demo | epic-14 F05/F06 (w14 footer: seeds and backfills are never promoted; promotion is a HITL gate, never a `depends_on`) |
| ADR-018 | Web IA | epic-14 F03 + epic-15 F02 (**two** w14 footers: `/invite/accept`, the `BrowserRouter` hoist, revoke-vs-remove) |
| ADR-019 | Web design system | epic-15 F02 (w14 footer: member / invitation statuses, inline-not-dialog confirmation) |
| ADR-020 | Web screen inventory | epic-14 F03 + epic-15 F02 (w14 footer: screen 1 states, screen 10, **new screen 11**) |
| ADR-021 | Schema apply on Azure Postgres | epic-14 F01 (three migrations regenerate one checked-in script) |
| ADR-022 | Day-1 demo auth + fixture seed | epic-14 F02/F05 (w14 footer: the role header is demoted; membership is the role source of truth) |
| ADR-024 | Ask Raffa V2 | epic-13 |
| ADR-025 | Workspace membership authorization + invitation lifecycle | **new at the w14 table** — epic-14 F01/F02/F03/F04, epic-15 F01/F02 |
| ADR-026 | Workspace discovery, roster, invitations: API contract + data model | **new at the w14 table** — epic-14 F01/F03/F04, epic-15 F01 |

## Wave w14 (2026-09-11) — "Workspace is real"

- **Source**: `inputs/next/next-waves-todo.md`
  (sha256 `e3fd34176de46d24f4c8fdb7526c29c91930fe97874cf75fd36c44554bacd23c`)
- **Requirements**: `reports/context/waves/w14-requirements.md`
- **Council decisions**: `reports/architecture/waves/w14.md` (approved; nine
  decisions, all seven seats)
- **Wave file**: `reports/plan/slices/w14.yaml` · **HITL**: `reports/audit/w14-hitl.md`
- **Previous**: `e13`
- **New epics**: `epic-14-workspace-identity` (extends epic-01 F05, epic-06
  F03/F04), `epic-15-workspace-invitations` (extends epic-01 F05, epic-06 F04)
- **Caps**: 20 tasks / 5 phases → **11 live tasks in 5 phases, 10 stories**

### Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| W14-01 | Wave base is the post-rebrand `origin/main` | **no task** — operator act at HITL; its proof (W14-A1) is recorded by `E14/F06/US01/T01` | — |
| NW-01 | `GET /api/workspaces` for the signed-in identity | `E14/F01/US01/T01`, `E14/F01/US01/T02`, `E14/F03/US01/T01`, `E14/F03/US02/T01`, `E14/F05/US01/T01` | 1, 2, 4 |
| NW-02 | Create workspace also writes the creator's membership (Admin) | `E14/F02/US01/T01`, `E14/F05/US01/T01` | 1 |
| NW-03 | Current workspace is a server fact, not `sessionStorage` | `E14/F03/US02/T01`, `E14/F05/US01/T01` | 1, 4 |
| NW-04 | `GET /api/workspaces/{tenantId}/members` | `E14/F04/US01/T01`, `E15/F02/US01/T01` | 2, 4 |
| NW-09 | Workspace picker contract count is frozen at 0 | `E14/F03/US01/T01`, `E14/F03/US02/T01` | 2, 4 |
| NW-14 | Delete and Retry upload 403 for the workspace creator | `E14/F02/US02/T01`, `E14/F03/US02/T01` | 2, 4 |
| NW-24 | Workspace has no currency / region (HITL) | `E14/F01/US01/T02`, `E14/F03/US01/T01`, `E14/F03/US02/T01` | 1, 2, 4 |
| NW-58 | Invites are email + link; login joins that workspace; Admin remove requires a new invite | `E14/F01/US01/T02`, `E14/F02/US01/T01` (the ADR-025 §D.1a guard), `E15/F01/US01/T01`, `E14/F03/US02/T01` (accept screen), `E15/F02/US01/T01` | 1, 3, 4 |
| — | Final integration + acceptance runbook | `E14/F06/US01/T01` | 5 |

### Queued for the next wave

Per `reports/context/waves/w14-requirements.md` §"Queued items": **no task file
is written for any item below**. The next run's intake picks them up as
carry-over.

- **W15 — API JWT (ADR-010)**: NW-05, NW-06, NW-07, NW-08, NW-31, NW-32. Plus
  the never-delivered "with OIDC claims" half of the superseded
  `E01/F05/US01/T02`. ADR-025 §H **T14 is authored in w14 and `Skip`ped**
  (`E15/F01/US01/T01`), to be activated by NW-05/NW-08.
- **W16 — no session as source of truth**: NW-10, NW-11, NW-12, NW-13, NW-21.
- **W17 — domain completeness (Contract 360)**: NW-20, NW-22, NW-23, NW-25,
  NW-26, NW-62, NW-63, NW-64, NW-65, NW-66.
- **W18 — contract, ops, and the Ask/Quote residuals**: NW-27, NW-30, NW-40,
  NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60, NW-61.
- **Out**: NW-51 (closed on `main`); NW-52, NW-53, NW-54 (deferred).

Nothing the council decided for an in-wave item was dropped for the cap. NW-24
was named as "the first item to cut" and **was not cut**.

### Superseded items

| Work item | Superseded by | Why |
|---|---|---|
| `epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-02-membership-invite.md` (was `status: live`) | `E15/F01/US01/T01` (NW-58) | Ruled at the w14 table (`reports/architecture/waves/w14.md`, §"Work items this wave supersedes"). It delivered "invite ⇒ membership row **immediately**"; NW-58 makes membership happen **on accept**. Same word, two different product rules — a replacement, not an extension. Its never-delivered "with OIDC claims" half moves to W15 / NW-05 |
| `epic-06-web-foundation/feature-04-workspace-members-ui/us-01-workspace-members-invite/tasks/task-01-workspace-members-invite.md` | **not superseded** — no banner | Its three ACs are still exactly what the product wants. NW-04 changes the table's **data source** and NW-58 changes the pane's **copy and payload**: amended behaviour inside a still-wanted story |

`reports/plan/slices/e01.yaml:52` and `reports/plan/wave-spec.execution.yaml:87`
still list the superseded task as `status: live`. Those are historical wave files
and this process never edits them — see `reports/audit/w14-hitl.md`
§"Superseded items".

### ADRs touched

- **New**: ADR-025 (workspace membership authorization and the invitation
  lifecycle), ADR-026 (workspace discovery, roster and invitations: API
  contract, data model and module composition).
- **Amended by w14 footer** (bodies untouched, every `Status: accepted`
  unchanged, every footer a narrowing): ADR-001, ADR-003, ADR-005, ADR-006,
  ADR-009, ADR-010, ADR-011, ADR-012, ADR-014, ADR-016, ADR-018 (two footers),
  ADR-019, ADR-020, ADR-022.
- **`none — no change`**: ADR-002, ADR-004, ADR-013, ADR-015, ADR-017, ADR-021,
  ADR-023, ADR-024.
