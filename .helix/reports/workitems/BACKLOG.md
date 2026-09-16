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
| epic-16 | async-document-processing | w15 | active — decomposed (next-wave) |
| epic-17 | invitation-delivery-and-identity | w15 | active — decomposed (next-wave) |
| epic-18 | api-authentication | w15 | active — decomposed (next-wave; F02 and F03 queued to W16) |

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
| ADR-027 | Async document processing | **new at the w15 table** — epic-16 F02/F03 (publish-before-commit, the conditional-`UPDATE` claim, the `DeliveryCount` split, `counts` / `readiness`, `Rejected`) |
| ADR-001 | V1 scope R0–R4 | epic-16 F02 (w15 footer clauses 1–3: a refusal is persistent, visible and terminal; "still processing" outranks "empty"), epic-17 F01 (clauses 4–6: a guest per invited address; the mail transport's deferral ends), epic-18 (addendum clause 12: which acceptance is `dev`-only) |
| ADR-002 | .NET solution shape | epic-16 F02 (w15 footers: the queue port becomes durable; **`Raffa.Storage`**; the Graph adapter lives in the host, never the module) |
| ADR-005 | Azure SKUs | epic-16 F01 (w15 footers: Service Bus **wired** — one subscription, two topic-scoped role assignments, the scale rule, `max_delivery_count = 8`; the four ACS rows; the four `AzureAd__*` keys; the `email` optional claim; **$0.00**) |
| ADR-007 | Terraform layout | epic-16 F01 (w15 footer: `infra/modules/communication/` joins the layout; `servicebus → containerapps` and `identity → servicebus` become real edges) |
| ADR-009 | Tenancy / RLS | epic-16 F02 (w15 §5: only the *source* of `app.identity_subject` changes; the GUC stays **parameter-bound**), epic-17 F01 (`ExternalSubjectId` is populated earlier on a table that already has its policy) |
| ADR-010 | Entra ID / OIDC | epic-18 F01 (w15 §1–§4: `ValidateAudience` with the **client id**, the pinned issuer, `oid` as identity, the claims-branch deletion), epic-17 F01 (§2.3–§2.4: the `#EXT#` UPN is never parsed; the `email` optional claim) |
| ADR-011 | Key Vault + RAG isolation | epic-16 F01/F02 (w15 §2: Service Bus is identity + RBAC, **no secret**), epic-17 F01 (§1, §4: `acs-connection` is the wave's **one** new entry; the log/audit split; the apply identity's grant priced) |
| ADR-012 | Web stack | epic-16 F03 (w15 §4–§6, §9–§10, §13–§18: the provenance rule, the counters, the local refusal row, the poll budget), epic-17 F02 (§7, §8, §13.1–§13.3), epic-18 F01 (§1–§3: the choke point, the never-stored token) |
| ADR-014 | Git flow | epic-16 F01/F04 (w15 clauses 1–7: the base is read at the gate, the zero-product-delta merge test, **two merges to `main`**, the zero-CI-YAML set, W15-A1) |
| ADR-015 | CI → Azure auth | epic-17 F01 (w15 clauses 1–9 — **its first amendment since 2026-09-01**: the runtime identity's directory permission, the **apply** identity's rights granted out of band by default, CI gains nothing) |
| ADR-016 | Promotion dev→demo | epic-16 F01/F04 (w15 clauses 13–26: the first per-environment API key, one infrastructure PR merged first, the corrected revision-state assertion, the DLQ as a standing condition, the `demo-v4` ruling), epic-17 F01/F03 |
| ADR-018 | Web IA | epic-16 F03 (w15 clauses 1–3, 6: the **not ready yet** and **nothing made it through** states, what each Documents number counts, one stopped-updates budget across five surfaces) |
| ADR-019 | Web design system | epic-16 F03 (w15: **one** semantic row, `Rejected → .tag-outline` "Not added", derived from the shipped card — no token, no component) |
| ADR-020 | Web screen inventory | epic-16 F03 (w15 §0–§2, §6, §8: screen 3's row and third chip, screens 2/5/6's new states, the stopped-updates notice, the wave-wide rebrand rule), epic-17 F01/F02 (§3 screen 10's three outcomes; **§4 surface 12, the invitation email**) |
| ADR-021 | Schema apply on Azure Postgres | epic-16 F02 — **`none`**: `processing_status` is `varchar(30)` with no CHECK, so `Rejected` needs no DDL and neither `backend.yml` array moves |
| ADR-022 | Day-1 demo auth + fixture seed | epic-18 F01 (w15 footer: the interim identity is **retired** — `X-User-Id` deleted, `X-Tenant-Id` demoted to a membership-verified selector) |
| ADR-024 | Ask Raffa V2 | epic-16 F02 (w15 footer §1–§4: R-DOC-01/03/09 become asynchronous; A7, `OQ-askv2-007` and R-DOC-05 AC-1 are `assumed-wrong`; one definition of *validated*) |
| ADR-025 | Workspace membership authorization + invitation lifecycle | epic-17 F01/F03 (w15 — a new **§J**: the Graph permission and its blast radius, guest-before-row, the `oid` bind, the failure contract, removal never deletes a guest, the test-seam refusal and **S-T23**) |
| ADR-026 | Workspace discovery, roster, invitations: API contract + data model | epic-17 F01/F02 (w15 §1–§9: `identityProvisioned`, `deliveryOutcome`, the declared 502, replace-on-live-invitation) |

## Wave w15 (2026-09-14) — "Upload feels instant, and inviting a colleague works end to end"

- **Source**: `inputs/next/w15-todo.md`
  (sha256 `d54da30d7564ea5cebdfd5bc003404626107ebf04daeec4ad2f5858cd3b51e6f`)
- **Requirements**: `reports/context/waves/w15-requirements.md`
- **Council decisions**: `reports/architecture/waves/w15.md` (approved; ten
  in-wave decisions, all seven seats involved — no seat sat this wave out)
- **Wave file**: `reports/plan/slices/w15.yaml` · **HITL**: `reports/audit/w15-hitl.md`
- **Previous**: `w14`
- **New epics**: `epic-16-async-document-processing` (extends epic-01 F06,
  epic-06 F05, epic-13 F04), `epic-17-invitation-delivery-and-identity`
  (extends epic-15, epic-06 F04), `epic-18-api-authentication` (extends
  epic-01 F05, epic-14 F02)
- **Caps**: 20 tasks / 5 phases → **11 live tasks in 5 phases, 15 stories**
  (11 of them carrying a live task), **4 queued tasks**

### Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| W15-01 | Wave base is `origin/main`, merged in | **no task** — operator act at HITL; its proof (W15-A1, six points) is recorded by `E16/F04/US01/T01` | — |
| NW-27 | `POST /api/documents` returns once the file is stored; processing on the Worker | `E16/F01/US01/T01`, `E16/F02/US01/T01`, `E16/F02/US02/T01`, `E16/F02/US03/T01`, `E16/F03/US01/T01` | 1, 2, 3, 4 |
| NW-61 | Upload feels instant; details only when the document is ready | `E16/F02/US03/T01`, `E16/F03/US01/T01` | 3, 4 |
| NW-10 | Rail Documents badge reads the server | `E16/F03/US01/T01` (co-located, per OQ-w15-008) | 4 |
| NW-67 | Raffa provisions the invitee's Entra B2B guest at invite time (Graph) | `E16/F01/US01/T01`, `E17/F01/US01/T01`, `E17/F02/US01/T01` | 1, 3, 4 |
| NW-68 | Raffa sends the invitation email (ACS Email) | `E16/F01/US01/T01`, `E17/F01/US01/T01`, `E17/F02/US01/T01` | 1, 3, 4 |
| NW-69 | Invite pane: honest delivery and identity state | `E17/F02/US01/T01` | 4 |
| NW-58r | Invitation e2e (N3b) runs because the flow creates the second account | `E17/F03/US01/T01` | 4 |
| NW-05 | API JWT (ADR-010) replaces spoofable headers | `E16/F01/US01/T01`, `E18/F01/US01/T01`, `E18/F01/US02/T01` | 1, 2 |
| NW-06 | Workspace role from membership / claims, never a client assertion | `E18/F01/US01/T01` (the deletion rides NW-05's seam) | 1 |
| — | Final integration + acceptance runbook | `E16/F04/US01/T01` | 5 |

### Added after the fan-out (2026-09-14, built by hand)

The first real twenty-file batch on `dev` (`docs/waves/w15-acceptance.md` §0.2)
found a UX failure NW-61's own design had not modelled: twenty truthful rows
that still read as stalled (~30 s cold start, ~100 s/document measured, a bar
at 0% the whole time). Two tasks close it — `feature-03-documents-truthful-surfaces/us-02-perceived-instant-batch/` —
appended to phase 4 of `reports/plan/slices/w15.yaml`, no council round, no new
ADR, no infrastructure change.

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| — | Priority by claim: the column, the service, the endpoint, the Worker's queue-jump (ADR-027 w15 footer C12) | `E16/F03/US02/T01` | 4 |
| — | The perceived-instant batch: the row reading and the progress panel (ADR-020 w15 footer §10–12) | `E16/F03/US02/T02` | 4 |

### Queued for the next wave

Per `reports/context/waves/w15-requirements.md` §5, these four `should` items are
**decomposed with full evidence** but carry `status: queued` and have **no entry
in `reports/plan/slices/w15.yaml`**. They are the **head of W16**, in this order.
The next run's intake picks them up first.

- **NW-07** — conversation `user_id` is the token subject → `E18/F02/US01/T01`
- **NW-08** — `GET /api/audit` works for a real Admin → `E18/F02/US02/T01`
- **NW-31** — retire the dual role headers → `E18/F03/US01/T01`
  (it also **owns `reprocess-tenant-documents.yml`**, which NW-05 takes out of
  service in w15 and which w15 neither repairs nor edits, so one wave opens that
  file once — ADR-016 w15 clause 21)
- **NW-32** — one answer for an absent caller identity → `E18/F03/US02/T01`

Then, unchanged from `w15-requirements.md` §5: **W16** continues with NW-11,
NW-12, NW-13, NW-21 (NW-10 leaves the W16 queue — w15 co-located it);
**W17** NW-20, NW-22, NW-23, NW-25, NW-26, NW-62–NW-66; **W18** NW-30, NW-40,
NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60.

**Nothing the council decided for an in-wave item was dropped for the cap.** The
three release valves product-owner named — NW-10, then NW-58r's runbook walk,
then NW-69 — were **not used**: 11 live tasks against a cap of 20.

### Superseded items

| Work item | Instruction | Why |
|---|---|---|
| `epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2/us-01-documents-v2.md` | **Partial banner, `status` stays `active`** — `## Superseded in part (2026-09-14, wave w15)` naming **AC-6 entirely** and **AC-1's 422 / nothing-persisted clause only** | AC-6 says verbatim *"no `Rejected` status exists server-side"* — negated word for word by OQ-w15-004's split gate. AC-1's *"no blob, no `document` row"* is half negated; its **415 format clause stands**. AC-2–AC-5 stand, so the story is **mostly still wanted** and must not be superseded whole (`waves/w15.md` §"Work-item instructions", product-owner correcting §6 of the intake) |
| `.../us-01-documents-v2/tasks/task-01-documents-admission.md` | **Partial banner, `status` stays `active`** — the same footer, naming its two required-test rows (`:63`, `:81`, "nothing persisted on 422") as superseded by the split gate, and its `OQ-askv2-007` line (`:85`) as retired | The task carries the same premise as the story it serves |
| `epic-15-workspace-invitations/feature-01-invitation-lifecycle/us-01-invite-accept-remove` | **No banner** | NW-67 / NW-68 land its two *deferred* clauses; the delivered lifecycle stands unchanged. The intake and product-owner agree here |

**No work item is superseded whole this wave**, and no `status:` line is changed
to `superseded`. What *is* superseded is an oracle assumption, on the record and
never edited in place (`inputs/**` is never written by this process):
`inputs/requirements.md` **A7** (`:827`), **`OQ-askv2-007`** and **R-DOC-05 AC-1**
are `assumed-wrong` from this wave on (OQ-w15-003, OQ-w15-D3; ADR-024 w15 footer
§3). R-DOC-04's *session-only / not stored* clause is superseded; its **"never
counted" clause stands**.

No earlier wave file lists either bannered item as `live`, so there is nothing for
the operator to reconcile in a historical slice this wave.

### ADRs touched

- **New**: ADR-027 (async document processing) — the wave's only new ADR, now
  on its **fourth** round (rounds 2–4; round 4, C12–C13, built by hand
  2026-09-14, priority by claim).
- **Amended by w15 footers** (bodies untouched, every `Status: accepted`
  unchanged, nothing superseded): ADR-001, ADR-002, ADR-005 (×2), ADR-007,
  ADR-009, ADR-010, ADR-011 (×2), ADR-012 (×3, its third built by hand
  2026-09-14), ADR-014, ADR-015 (**its first amendment since 2026-09-01**),
  ADR-016 (×2), ADR-018 (×2), ADR-019 (×2), ADR-020 (×3, its third built by
  hand 2026-09-14), ADR-022, ADR-024, ADR-025 (a new §J), ADR-026.
- **`none — no change`, with reasons recorded**: ADR-003, ADR-004, ADR-006,
  ADR-008, ADR-013, ADR-017, ADR-021, ADR-023.

## Epics (continued) — wave w16

| ID | Slug | Wave | Status |
|----|------|------|--------|
| epic-19 | server-side-state | w16 | active — decomposed (next-wave) |

**No new epic was cut for theme A.** The four carry-over head items (NW-07,
NW-08, NW-31, NW-32) were already decomposed under **epic-18** in w15 with
`status: queued`; w16 **promotes them to `live`**. Raw `w16-todo.md` §0.4 forbids
rewriting their bodies, so each task file keeps its body unchanged (and its
`wave: w15` line, which records when it was cut) and carries an appended
`## Wave w16 addendum` section that supersedes the stale lines. epic-18's row in
the table above therefore still reads `w15`, correctly.

**epic-19 extends**: epic-03 F03 (renewals), epic-04 F03 (savings),
epic-05 F03 (quotes), epic-07 F02 (Contract 360), epic-08 (web surfaces),
epic-16 (the Worker, for W16-01).

## ADR → wave coverage (continued) — wave w16

| ADR | Topic | Carried into |
|-----|-------|--------------|
| ADR-028 | Server-side state | **new at the w16 table** — epic-19 F01/F02/F03/F04/F06/F07 (§D1 the renewal-action read-back and the binding `savedAction` name; §D2 the quote resource with its outcomes embedded; §D3 `contract_negotiation_step`, keyed by step **name**; §D4 Undo is two idempotent writes, ticks first; §D5 resolved, never guessed; §D6 what this wave does not deliver) |
| ADR-001 | V1 scope R0–R4 | epic-18 F02/F03 + epic-19 (w16 clauses 0–8: zero new capability; the pre-w15 conversation rows are recorded, not migrated; the four canonical named steps; the Savings **status** move and the money fence; §D5 clause 2 ships; the re-worded A16-1, A16-3, A16-4) |
| ADR-002 | .NET solution shape | epic-19 F03/F04/F05 + epic-18 F02/F03 (w16 clauses 1–5: `Raffa.Documents.Contracts` owns the ticks; `Raffa.Api` composes the savings link; one read-only supplier lookup; the actor is a signature change not a boundary change; two deletions authorised) |
| ADR-003 | PostgreSQL + pgvector | epic-19 F03 (w16 clause 1: `contract_negotiation_step`, unique `(tenant_id, contract_id, step)`, **the row's presence is the tick**; three schema changes refused) |
| ADR-009 | Tenancy / RLS | epic-19 F03 + epic-18 F02 (w16 clauses 1–3b: the wave's entire RLS delta is one table; `ENABLE` **+ `FORCE`** + both `USING` and `WITH CHECK` in the table's own migration; the audit read runs **inside** the verified scope) |
| ADR-010 | Entra ID / OIDC | epic-18 F02 (w16 clauses 1–4: the email leg is deleted from both authorization comparators; normalization is fixed **at the source**; a re-key by email match is forbidden outright; S-T24) |
| ADR-011 | Key Vault + RAG isolation | epic-18 F02/F03 (w16 clauses 14–19: who may read the audit trail; the actor as a **required parameter with no default**; the reserved `system:<component>` principal; the append-only trigger is never dropped) |
| ADR-012 | Web stack | epic-19 F02/F06/F07 + epic-18 F02/F03 (w16 clauses 21–31: the three stores retire **with** their read-backs; `client.ts` is one-writer-per-phase; the wrapper rule; the reverting tick; the paired grep; the pseudo-opportunity row retired) |
| ADR-014 | Git flow | epic-19 F08 (w16 clauses 1–5: the two-file CI-YAML set; **one** PR; the wave-scoped close record; W16-A1 gains item (g); the corrected wave order) |
| ADR-016 | Promotion dev→demo | epic-18 F03 + epic-19 F08 (w16 clauses 27–35: OQ-w16-004 answered "neither option"; the workflow's three broken axes; delete/keep/add; three refused shortcuts; the W17 bulk console; the final-integration list and the known-gaps set) |
| ADR-021 | Schema apply on Azure Postgres | epic-19 F03 (w16: one migration regenerating one byte-compared script; **no `backend.yml` array moves**, and a `backend.yml` diff is a defect) |
| ADR-022 | Day-1 demo auth + fixture seed | epic-18 F03 (w16 clauses 1–6: the interim **identity** posture is fully retired; the capability catalog is served whole, with the tenant-free rule it now depends on; `roleGate` is presentation; **`X-Tenant-Id` does not retire**) |
| ADR-024 | Ask Raffa V2 | epic-18 F02/F03 (w16 clauses 1–2: `OQ-askv2-005` retires with NW-07; the capability catalog stops filtering) |
| ADR-025 | Workspace membership authorization + invitation lifecycle | epic-18 F02 (w16 §K.1–§K.3: the audit read joins the membership-guarded set; `WorkspacePrincipalAuthorization` is **deleted whole**; the deletion proof a ladder test cannot give) |
| ADR-026 | Workspace discovery, roster, invitations: API contract + data model | epic-18 F02/F03 + epic-19 F06 (w16 clauses 1–6: `/api/audit` in with its prose half; the `X-Role` parameter out; the five theme-B paths; the gate is a **grep**, not a green build) |
| ADR-027 | Async document processing | epic-18 F03 + epic-19 F05 (w16 clauses 1–2: what `:198`'s "re-enqueue" does **not** license; the R0 placeholder queue deleted) |
| ADR-005/006/007/008 | Azure SKUs, region, Terraform layout, Foundry | **`none`** — cloud-architect's conditional seat resolved to *not involved*: w16 opens **no `infra/` file**, changes no SKU, env key or apply path. Cost delta **€0.00/month** |
| ADR-015 | CI → Azure auth | **`none`** — no federated credential, GitHub secret, subject claim or Graph right changes. w16 buys CI **no new credential** |
| ADR-013/018/019/020 | Mobile, Web IA, design system, screen inventory | **`none`** — ux-ui-designer unseated: no new screen, state or copy. Both conditions that would have seated the designer were tested and did not fire (OQ-w16-003 ruled **no** web audit surface; OQ-w16-ca-03 resolved **server-side**) |

## Wave w16 (2026-09-15) — "Nothing the product knows lives only in a browser tab"

- **Source**: `inputs/next/w16-todo.md`
  (sha256 `d3518a64062ee1a867b0a6de433820e6dd48e4ea8c8c6b86b0bf84cd47103edc`)
- **Requirements**: `reports/context/waves/w16-requirements.md`
- **Council decisions**: `reports/architecture/waves/w16.md` — all nine in-wave
  item rows filled; six seats involved, ux-ui-designer `PASS` (unseated) and
  cloud-architect `PASS` (conditional seat resolved to *not involved*)
- **Wave file**: `reports/plan/slices/w16.yaml` · **HITL**: `reports/audit/w16-hitl.md`
- **Previous**: `w15` · **Baseline**: `f0b3436` (`helix/w16` == `origin/main`, `0 0`)
- **New epic**: `epic-19-server-side-state`. **epic-18 F02/F03 promoted**
  `queued → live` (four tasks, bodies unchanged, each with a w16 addendum)
- **Caps**: 20 tasks / 5 phases → **13 live tasks in 5 phases, 13 stories across
  2 epics**, **0 queued**

### Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| NW-32 | Every write names its actor (nine services, five modules) | `E18/F03/US02/T01` | 1 |
| NW-13 | Contract 360 step ticks are server state | `E19/F03/US01/T01` (API + the wave's one new table), `E19/F06/US01/T01` (contract + wrappers), `E19/F07/US01/T01` (screen) | 1, 3, 4 |
| W16-01 | The dead R0 in-process Worker queue is deleted | `E19/F05/US01/T01` | 1 |
| NW-08 | `GET /api/audit` works for a real Admin | `E18/F02/US02/T01` | 2 |
| NW-11 | Renewal actions have an HTTP read-back | `E19/F01/US01/T01` (API), `E19/F06/US01/T01` (contract), `E19/F07/US01/T01` (screen) | 2, 3, 4 |
| NW-12 | Quote GET + negotiation-outcome read-back | `E19/F02/US01/T01` (API), `E19/F06/US01/T01` (contract + `getQuote`), `E19/F02/US02/T01` (screen) | 2, 3, 4 |
| NW-21 | Quote outcome updates Savings | `E19/F04/US01/T01` — **no column, no migration, no contract delta, no client change** | 2 |
| NW-07 | Conversation `user_id` is the token subject | `E18/F02/US01/T01` | 3 |
| NW-31 | Dual role headers retired (+ the reprocess workflow) | `E18/F03/US01/T01` — the wave's **only** task that opens `.github/workflows/**` | 4 |
| — | Final integration + acceptance runbook | `E19/F08/US01/T01` | 5 |

**NW-10 is not in the wave**: CLOSED-ON-MAIN (`9c4975e`), confirmed with evidence
per raw §0.5 and deliberately not reopened. Its one documentation residual
(`web/README.md:1166`) is swept by `E19/F08/US01/T01`.

### Queued for the next wave

**None.** Every item of the raw file's W16 queue fits: 13 live tasks against a cap
of 20, 5 phases against a cap of 5. **No item is demoted and none is deferred**,
so W17 keeps its recorded head unchanged (NW-20, NW-22, NW-23, NW-25, NW-26,
NW-62–NW-66, NW-71).

Three residuals were **ruled into W17 at the table** rather than decomposed here,
and none of them is a wave item this run created: the realized **amount** in the
Savings KPI (OQ-w16-po-01 — an unmet AC of the still-`active` `E04/F03/US01`);
the **bulk whole-tenant reprocess** (ADR-016 w16 clause 31, shape already
designed); and `roleGate` chip hiding (OQ-w16-sa-02 — presentation, never a
security fix). A **designed** Savings section for tracked renewal actions
(OQ-w16-ca-01's product half) seats ux-ui-designer in W17.

### Superseded items

**None.** `w16-requirements.md` §6 records no "cancels / replaces" statement in
the raw file: **no status banner is written this wave and no `status:` line is
changed to `superseded`.** What changes is four `status: queued → live` lines on
the epic-18 F02/F03 task files — a **promotion, not a supersession**.

One existing story is explicitly **re-pointed rather than cancelled**:
`epic-08 .../us-01-quote-check` **AC-4** is discharged today by a session store;
ADR-001 w16 clause 2 rules that NW-12 + NW-21 are its honest server-side
implementation. **The story stays `active`, gets no status banner and no
`superseded:` line**, with two wording corrections on the record — its surface is
**Savings**, not "Home", and what updates is the realized **count**, not a money
tile.

One **oracle assumption** retires rather than a work item: `OQ-askv2-005` closes
with NW-07 (ADR-024 w16 clause 1), and `inputs/requirements.md` R-CONV-03
(`:252-256`) is satisfied in substance.

No earlier wave file lists any of these as `live`, so there is nothing for the
operator to reconcile in a historical slice this wave.

### ADRs touched

- **New**: ADR-028 (server-side state) — the wave's only new ADR.
- **Amended by w16 footers** (bodies untouched, every `Status: accepted`
  unchanged, nothing superseded): ADR-001, ADR-002, ADR-003, ADR-009, ADR-010,
  ADR-011, ADR-012, ADR-014, ADR-016, ADR-021, ADR-022, ADR-024, ADR-025,
  ADR-026, ADR-027 — **fifteen**.
- **`none — no change`, with reasons recorded**: ADR-004, ADR-005, ADR-006,
  ADR-007, ADR-008, ADR-013, ADR-015, ADR-017, ADR-018, ADR-019, ADR-020,
  ADR-023.

## Epics (continued) — wave w17

| ID | Slug | Wave | Status |
|----|------|------|--------|
| epic-20 | w16-residuals | w17 | active — decomposed (next-wave) |
| epic-21 | contract-360-answers | w17 | active — decomposed (next-wave) |
| epic-22 | officialized-facts-and-viewer | w17 | active — decomposed (next-wave) |

**epic-23 (`portfolio-and-savings-filters`) was NOT minted.** `w17-requirements.md`
§4 makes it conditional on its items surviving the cap; both NW-23 and NW-25 are
`could` and both overflow to W18, so the epic is not created. The next intake takes
the number that is free at that time.

**epic-20 extends**: epic-04 F02/F03 (savings engine + dashboard), epic-13 F03
(supplier identity), epic-18 F03 (the CI-YAML set).
**epic-21 extends**: epic-02 (extraction / 360), epic-03 (renewals), epic-04 F01
(benchmark), epic-07 F02 (Contract 360 web).
**epic-22 extends**: epic-02 (extraction / evidence), epic-07 F01/F02/F03 (360,
Review), epic-16 (the Worker and the reprocess path).

## ADR → wave coverage (continued) — wave w17

| ADR | Topic | Carried into |
|-----|-------|--------------|
| ADR-029 | Document page rendering and the preview contract | **new at the w17 table** — epic-22 F02/F03 (clauses 1–7: rasterise in the Worker after admission, independent of extraction success; the real renderer registers ahead of the placeholder; `BuildPreviewPage` under the same tenant guard; `?page=n` bounded by `page_count` with **404**, never a silent page 1; `pageCount` on the read model; a stated page cap; round-3: the deterministic key replaced in place, and a shorter re-render reaps `n > pageCount`) |
| ADR-001 | V1 scope R0–R4 | epic-20 F01 (w17 clauses 1, 10 — the money fence lifted and replaced; the cell is labelled **"Savings verified"**), epic-22 F01/F04/F05 (clauses 2, 5, 6, 8 — one bar at 90 % with no always-review list; the always-shown critical fields; confidence leaves Contract 360; three acceptance states stay distinguishable), epic-21 (clauses 3, 4 — what "activity" means, and the two honest shapes of a market claim); clause 7 rules **NW-75 OUT** and clause 9 routes the `demo` promotion to the HITL gate |
| ADR-002 | .NET solution shape | epic-20 F02 (w17 clause 1 — `Raffa.Tools` is a **third composition root**, joins `AllRaffaProjects` only), epic-21 F02 (clause 2 — `RenewalPipelineBuilder` **stays pure**; the host resolves the band), epic-21 F01/F03 (clause 3 — the 360 composes in `Raffa.Api`, never in the module) |
| ADR-003 | PostgreSQL + pgvector | epic-22 F01 (w17 clause 1 — `decision` + `decided_at` on the **existing** `extraction_evidence`, two nullable columns, **no SQL enum, no new table**, and the three-state derivation table; clause 2 — geometry columns **refused** this wave) |
| ADR-005 | Azure SKUs | epic-20 F02 (w17 §15–§18, §22, §27 — one topic-scoped `azurerm_role_assignment`, **$0.00/month**), epic-22 F02 (§19–§21, §23–§25 — render page-by-page disposing each bitmap, the conditional Dockerfile layer above `USER $APP_UID`, ACR stays Basic, the deterministic overwrite, the **20-file measurement before any bulk run**) |
| ADR-007 | Terraform layout | epic-20 F02 (w17 §5–§9 — the required `ci_deploy_principal_id`, both env roots in the same PR, the `lifecycle { ignore_changes }` line, and **§9: `demo`'s infra moves at the merge, never at the promotion tag**) |
| ADR-009 | Tenancy / RLS | epic-20 F02 (w17 clauses 1, 4 — five binding rules for the first non-HTTP host: three-argument `Configure` inside `BeginScope`, one tenant per run, **zero rows exits non-zero**), epic-22 F01 (clause 2 — zero new isolation surface), epic-22 F02 (clauses 3, 6–8 — the page bound lives in the builder, `page` is an `int`, and the reap is bounded by prefix **and** a confirmed positive `pageCount`) |
| ADR-011 | Key Vault + RAG isolation | epic-20 F02 (w17 clauses 20, 23, 26 — actor `system:bulk-reprocess`, **no CI-controlled string in `Actor`**, the log rule, and **stop at the first publish failure**), epic-22 F01 (clause 21 — one audit row per document per run, actor `system:extraction`, **names and confidences, never values**), epic-21 F01 (clauses 22, 25 — the activity projection's five conditions as **query predicates**, `Detail` never projected) |
| ADR-012 | Web stack | epic-22 F01/F03/F04/F05 (w17 clauses 32–48 — **zero** new runtime dependencies, the object-URL revoke rule, the client renders the server's decision and `acceptedThisSession` retires, one answer source per screen, the `getConfidenceTag` writer math, the 404 that names the wrong thing, **no page cache across a navigation**), epic-20 F01 (clauses 37, 39, §42–§44 — the declared wire break names its call sites; `lines: []` means "no figure" and the reason rides `meta`) |
| ADR-014 | Git flow | epic-22 F06 (w17 clauses 1–9 — the one-file CI set, the **two-PR shape**, the standing `--record-hitl` stamp, W17-A1 (a)–(h), the close-record replacement, `backend.yml` parked, and the phase skeleton) |
| ADR-016 | Promotion dev→demo | epic-20 F02 (w17 clauses 36–45 — the console runs on the GitHub runner, the four-file `infra/` set, the workflow's gating idiom and the CI scanner gate, the composed worklist, the `demo` ruling, and **clause 44: not dispatched until the `raffa-dev` apply is confirmed**), epic-22 F06 (clauses 41, 42 — the final-integration list and the known-gaps table) |
| ADR-017 | OCR in V1 | epic-22 F03 (w17 footer — `prebuilt-layout` stays **available and uncalled**; page-level anchoring is derivable from the stored `SourcePage` + `SourceSpan`; bounding boxes are W18) |
| ADR-018 | Web IA | epic-22 F03 (w17 clauses 9–11, 15 — **one route added**, the first since w14: `/documents/:documentId/viewer?page=<n>&clause=<clauseId>`; `navItems.ts` gains **no row**; a 404 does not rewrite the URL), epic-22 F03/F04 (clauses 12–14 — the viewer's **four** states, not-found is the route's own state and never the shell's catch-all) |
| ADR-019 | Web design system | epic-22 F01 (w17 clauses 7, 8, 10 — the first change to the confidence rows since the ADR was accepted: two treatments over three decisions, **floor never round**, and the w14 §6 evidence correction), epic-22 F04 (clauses 9, 11 — the three leverage words, label-only; **no confidence tag on Contract 360 at all**), clause 12 — **no new token and no new component** anywhere in the wave |
| ADR-020 | Web screen inventory | epic-20 F01 (w17 §15, §17, §20–§21 — a **fourth equal cell** in every state), epic-22 F01/F03/F04/F05 (§13, §14, §16, §18, §24–§26 — the two-item legend, the "Not found in the document" section, the viewer chrome and its not-found copy, and the five ratified divergences from the export) |
| ADR-021 | Schema apply on Azure Postgres | epic-22 F01 (w17 clauses 1–3 — one migration regenerating one byte-compared script, **no `backend.yml` array moves**, and the stale array counts assigned to the NW-73 sweep) |
| ADR-022 | Day-1 demo auth + fixture seed | epic-20 F02 (w17 clauses 1–3, 6 — the "owed to W17" ruling is **discharged**: a CI principal may hold **Send only**, topic-scoped; the Admin gate is **relocated to the CI plane**; `environment:` sits on the job that acts; `target_environment` ships **`dev` only**) |
| ADR-024 | Ask Raffa V2 | epic-22 F01 (w17 §A — raw `>= 0.90`, **one** `ExtractionConfidencePolicy`, three wire states, the threshold exposed once, a client-supplied decision is **400**), epic-21 (clauses 6–11 — the market-claim shape, one benchmark resolution per screen, the 360 consumes `/strategy`, and the two 360 record shapes) |
| ADR-027 | Async document processing | epic-22 F02 (w17 clause 1 — rasterisation is a Worker stage), epic-22 F01 (clause 2 — **a reprocess never silently re-auto-accepts a field a human decided**), epic-20 F02 (clause 3 — the console calls the pipeline and never reimplements it) |
| ADR-028 | Server-side state | epic-20 F01 (w17 footer + clauses 6, 7 — `RealizedSavingsByCurrency(Currency, Amount, Count)` replaces `SavingsRangeByCurrency` on one member: a **declared type change, not a rename**; the wire key stays **`savingsRealized`**; **nothing is renamed this wave**) |
| ADR-006/ADR-008/ADR-010/ADR-013/ADR-015/ADR-025/ADR-026 | Region, Foundry, Entra, mobile, CI→Azure auth, membership, workspace contract | **`none`** — recorded as decisions, not silence: no region, Foundry account/deployment/capacity, token/claim/app-registration/federated-credential, mobile, GitHub-secret, membership or invitation change. The wave's entire identity delta is **one Azure data-plane role on an existing principal** |

## Wave w17 (2026-09-15) — "The product officializes what it knows, shows the page it read it from, and answers where you can save"

- **Source**: `inputs/next/w17-todo.md`
  (sha256 `8afa1243587461327dfec6e0eafa3003d676345c0b8c9b9aed5edaa8b8de9275`)
- **Requirements**: `reports/context/waves/w17-requirements.md`
- **Council decisions**: `reports/architecture/waves/w17.md` — **APPROVED**, eleven
  in-wave item rows, **all seven seats involved** (the first wave since w14 where that
  is true). One new ADR (**ADR-029**), 19 amended, 6 `none`.
- **Wave file**: `reports/plan/slices/w17.yaml` · **HITL**: `reports/audit/w17-hitl.md`
- **Previous**: `w16` · **Baseline at intake**: `d3d2d24` (`helix/w17` == `origin/main`,
  `0 0`). ⚠ **`origin/main` has since moved** — see the hitl doc's prerequisites.
- **New epics**: `epic-20-w16-residuals`, `epic-21-contract-360-answers`,
  `epic-22-officialized-facts-and-viewer`
- **Caps**: 20 tasks / 5 phases → **14 live tasks in 5 phases, 14 stories across
  3 epics**, **0 queued tasks** (three items queued, none decomposed — see below)

### Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| NW-73 | Bulk whole-tenant reprocess console | `E20/F02/US01/T01` (Terraform — **PR 1, alone**), `E20/F02/US02/T01` (console + the wave's one added workflow) | 1, 2 |
| NW-20 | Contract 360 `benchmark` / `activity` stop being `[]` | `E21/F01/US01/T01` (also produces the shared benchmark key resolver) | 1 |
| NW-26 | Preview stops being a placeholder PNG | `E22/F02/US01/T01` | 1 |
| NW-22 | Renewal insight `MarketPosition` is filled | `E21/F02/US01/T01` | 2 |
| NW-71 | A field extracted at ≥ 90 % is accepted automatically | `E22/F01/US01/T01` (server rule + the wave's one migration + contract-A), `E22/F01/US02/T01` (Review renders the server's decision) | 2, 3 |
| NW-62 | Contract 360 answers "where you can save" and "when you must move" | `E21/F03/US01/T01` (strategy + benchmark wired), `E21/F03/US02/T01` (the answers band) | 2, 3 |
| NW-63 | Document viewer over the uploaded pages | `E22/F03/US01/T01` — **split** per OQ-w17-001: real pages + text-level highlight ship; bounding boxes and editable OCR phrases are the **head of W18** | 3 |
| NW-72 | Savings KPI shows verified money | `E20/F01/US01/T01` (calculator + contract-B + the fourth KPI cell) | 4 |
| NW-64 | Unrecovered fields get a fillable section | `E22/F05/US01/T01` | 4 |
| NW-65 | Details: only officialized facts | `E22/F04/US01/T01` (one task with NW-66 — both write `contract360ViewModel.ts`) | 4 |
| NW-66 | Why-clauses: no quote, leverage not confidence | `E22/F04/US01/T01` | 4 |
| — | Final integration + the `dev` acceptance walk | `E22/F06/US01/T01` | 5 |

### Queued for the next wave

**No task files were minted for the queued items**, and that is deliberate: the
council did not rule on them (`waves/w17.md` header — "not decided here, not
decomposed, and not demoted"), so there is no decision row, no ADR action and no file
set to write a task against. They are the **head of W18** in the raw file's own
overflow order, and the next intake picks them up first.

1. **NW-63's W18 remainder** — the bounding-box overlay, `prebuilt-layout` with the
   widened gateway contract and `AiOcrPage`, the geometry columns ADR-003 w17 clause 2
   refuses, and the phrase-edit write path. It heads W18 **ahead of** the overflow
   items: a `must`'s remainder outranks a `should`/`could` overflow. Its shape is
   pre-decided in ADR-029 so it is not re-litigated.
2. **NW-23** (`could`) — portfolio category filter. OQ-w17-007 travels with it
   (supplier category joined in the host; `screens-v2.md`'s "Type" column stays the
   document type).
3. **NW-25** (`could`) — savings list filters (supplier and status; Estimate is a
   sort, not a filter).
4. **NW-74** (`should`) — hide admin-gated Ask chips from a non-Admin. **Not demoted**:
   it keeps `should` and heads W18 behind the three `could`s only because the raw
   file fixes that sequence. Its shape is recorded in ADR-012 w17 clause 40 (one
   server-derived `role` prop into two renderers), and ADR-022 S16-11 stands —
   **presentation, never a security fix**, and `GET /api/capabilities` stays un-gated.

**NW-75 is not queued — it is OUT.** Product-owner ruled it out for V1 at the table
(ADR-001 w17 clause 7): the oracle's opportunities table is Supplier · Action ·
**Estimate**, and a tracked action holds no system-held estimate, so the row would
either invent a number or ship an undefined column — which is why `buildTrackedOpportunityRow`
was retired in w16. **No task, no story, no ADR-020 change, and no work item is
cancelled.** It returns as a *new* item once a tracked action can carry a system-held
estimate, i.e. after NW-62's benchmark join.

### Superseded items

**None.** `w17-requirements.md` §6 records no "cancels / replaces" statement in the
raw file: **no status banner is written this wave and no `status:` line is changed to
`superseded`.**

`E04/F03/US01` AC-1 (`epic-04-savings-intelligence/.../us-01-savings-kpis.md:19`) is
**completed, not superseded** — NW-72 discharges its realized half. The story stays
`active` with **no banner and no `superseded:` line**.

What *is* superseded is **two product oracles, on the record only** (`inputs/**` is
never edited by this process — the same treatment ADR-001's w15 footer used):

| Oracle | Line | Superseded by | Note |
|---|---|---|---|
| `inputs/product-spec.md` §7.3 confidence table | `:333-335` (and `:341`'s threshold half) | **NW-71** | One threshold, 90 %, every field, no always-review list. The **criticality** of the five fields is **not** superseded — only their threshold |
| `inputs/percorso-pilota-v1.md` "Soglia HITL" | `:32`, `:46`, `:55`, `:122` | **NW-71** | The "critical stricter" clause is removed. `:65` and `:55`'s unlock rule are **threshold-relative and survive**, re-parameterized |
| `inputs/product-spec.md` Appendix C | `:954` | **NW-66** | Superseded in its **rendering half, on Contract 360 only**. It stands verbatim in Review and in Ask, and its **storage half is untouched** |

**Design-oracle staleness this wave creates** (recorded, never edited):
`screens-v2.md:83`, `:87`, `:89-91`, `:101-102`, `:103-104`, `:134-135` — five
ratified divergences (ADR-020 w17 §17) plus a sixth surface (the viewer) absent from
every export. Five re-exports are requested and **none blocks a task**.

No earlier wave file lists any in-wave item as `live`, so there is nothing for the
operator to reconcile in a historical slice this wave.

### ADRs touched

- **New**: ADR-029 (document page rendering and the preview contract) — the wave's
  only new ADR, with a round-3 footer (deterministic key, and the reap of
  `n > pageCount`).
- **Amended by w17 footers** (bodies untouched, every `Status: accepted` unchanged,
  nothing superseded): ADR-001, ADR-002, ADR-003, ADR-005, ADR-007, ADR-009, ADR-011,
  ADR-012, ADR-014, ADR-016, ADR-017, ADR-018, ADR-019, ADR-020, ADR-021, ADR-022,
  ADR-024, ADR-027, ADR-028 — **nineteen**.
- **`none — no change`, with reasons recorded**: ADR-004, ADR-006, ADR-008, ADR-010,
  ADR-013, ADR-015, ADR-023, ADR-025, ADR-026.

## Epics (continued) — wave w18

| ID | Slug | Wave | Status |
|----|------|------|--------|
| epic-23 | viewer-bounding-boxes-and-phrase-edit | w18 | active — decomposed (next-wave) |
| epic-24 | portfolio-and-savings-filters | w18 | active — decomposed (next-wave) |
| epic-25 | ask-and-quote-product-completeness | w18 | active — decomposed (next-wave) |
| epic-26 | contract-and-ops-residuals | w18 | active — decomposed (next-wave) |

**epic-23 extends**: epic-22 (the w17 viewer).
**epic-24 extends**: epic-02 F03, epic-04 F03, epic-07 F01, epic-08 F02.
**epic-25 extends**: epic-13, epic-07 F02, epic-08 F03.
**epic-26 extends**: epic-13 F05, epic-01 F03, epic-10.

## ADR → wave coverage (continued) — wave w18

| ADR | Topic | Carried into |
|-----|-------|--------------|
| ADR-029 | Document page rendering and the preview contract | epic-23 F01/F04 (w18 footer: phrase-edit provenance — override beside proposal, never in-place; the box and its wording ship together, clause 2) |
| ADR-017 | OCR in V1 | epic-23 F01 (w18 footer: `prebuilt-layout` is now called behind the gateway; the wire widens to carry `words`/`polygon`; `AiOcrPage` grows geometry) |
| ADR-003 | PostgreSQL + pgvector | epic-23 F02 (w18 footer: geometry + override columns on `extraction_evidence`, one migration; clause 2 discharged) |
| ADR-027 | Async document processing | epic-23 F03 (w18: a reprocess never silently overrides a human correction) |
| ADR-002 | .NET solution shape | epic-24 F01 (w18: the portfolio supplier-category join composes in the host `Raffa.Api`, never in the module) |
| ADR-024 | Ask Raffa V2 | epic-25 F02/F03/F04/F05 (w18: citation deep-link, scoped-chat entry, quote job-to-be-done, abstain recovery) |
| ADR-012 | Web stack | epic-25 F01/F02/F03/F06 + epic-26 F01 (w18: the generated client wrappers, the citation deep-link, the scoped entry, the role-gated chip presentation, the swept prose) |
| ADR-018 | Web IA | epic-25 F02/F06 + epic-24 F01/F02 (w18: the viewer deep-link route is consumed, the global Ask bar is suppressed on `/ask`, the portfolio/savings query-param controls) |
| ADR-019 | Web design system | epic-25 F02/F05 (w18: native-button citation CTA, secondary action reuse — no new token or component) |
| ADR-020 | Web screen inventory | epic-24 F02/F01 + epic-25 F05/F06 (w18: the portfolio category and savings filter controls, the abstain recovery copy, the suppressed duplicate bar) |
| ADR-022 | Day-1 demo auth + fixture seed | epic-25 F01 (w18: S16-11 stands — chip hiding is presentation, never a security fix) |
| ADR-028 | Server-side state | epic-25 F04 (w18: quote history is durable server state, never a client store) |
| ADR-001 | V1 scope R0–R4 | epic-25 F04 (w18: the quote benchmark is fixture-adapter fenced, never a paid external API) |
| ADR-016 | Promotion dev→demo | epic-26 F02 (w18: the e2e walk is a runbook spec, never CI — no workflow runs Playwright) |
| ADR-005 | Azure SKUs | epic-26 F01 + epic-23 F01 (w18: `none` — the ops walks are confirm-only and `prebuilt-layout` needs no new role/SKU) |
| ADR-026 | Workspace discovery, roster, invitations: API contract + data model | epic-26 F01 (w18: `none` — the OpenAPI residual is a prose sweep) |

## Wave w18 (2026-09-16) — "the viewer draws the box, the cited phrase is editable, and the lists/Ask/Quote surfaces stop dead-ending"

- **Source**: `inputs/next/w18-todo.md`
  (sha256 `unavailable — no sha256sum/python in this harness at intake`)
- **Requirements**: `reports/context/waves/w18-requirements.md`
- **Council decisions**: `reports/architecture/waves/w18.md` — APPROVED, all seven seats involved (security-architect confirm-only on NW-74)
- **Wave file**: `reports/plan/slices/w18.yaml` · **HITL**: `reports/audit/w18-hitl.md`
- **Previous**: `w17`
- **New epics**: `epic-23-viewer-bounding-boxes-and-phrase-edit` (extends epic-22), `epic-24-portfolio-and-savings-filters` (extends epic-02 F03, epic-04 F03, epic-07 F01, epic-08 F02), `epic-25-ask-and-quote-product-completeness` (extends epic-13, epic-07 F02, epic-08 F03), `epic-26-contract-and-ops-residuals` (extends epic-13 F05, epic-01 F03, epic-10)
- **Caps**: 20 tasks / 5 phases → **20 live tasks in 5 phases, 0 queued tasks**

### Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| NW-63r | Bounding-box overlay + phrase-edit (W18 remainder, must) | `E23/F01/US01/T01`, `E23/F02/US01/T01`, `E23/F03/US01/T01`, `E23/F04/US01/T01` | 1, 2, 3, 4 |
| NW-23 | Portfolio supplier-category filter | `E24/F01/US01/T01`, `E24/F01/US02/T01` | 1, 2 |
| NW-25 | Savings list filters | `E24/F02/US01/T01` | 1 |
| NW-74 | Hide admin-gated Ask chips | `E25/F01/US01/T01` | 1 |
| NW-55 | Ask citation cards: preview or deep-link | `E25/F02/US01/T01`, `E25/F02/US02/T01` | 1, 3 |
| NW-56 | 360 "Ask about it" briefs | `E25/F03/US01/T01`, `E25/F03/US02/T01` | 2, 3 |
| NW-57 | Quote check: market benchmark + history | `E25/F04/US01/T01`, `E25/F04/US02/T01` | 2, 4 |
| NW-59 | Ask never dead-ends (abstain recovery) | `E25/F05/US01/T01`, `E25/F05/US02/T01` | 3, 4 |
| NW-60 | Hide global Ask bar on Ask screens | `E25/F06/US01/T01` | 2 |
| NW-30 | OpenAPI prose sweep | `E26/F01/US01/T01` | 4 |
| NW-50 | day1.spec.ts reconcile | `E26/F02/US01/T01` | 1 |
| NW-40 / NW-41 | HCP + market walk (runbook) | **no task** — recorded in `E23/F05/US01/T01` + `docs/waves/w18-acceptance.md` | phase 5 |
| — | Final integration + acceptance runbook | `E23/F05/US01/T01` | 5 |

### Queued for the next wave

**None.** All 13 items fit within the 20-task cap; NW-40/NW-41 are runbook walks (no task, ride final integration). **NW-75 stays OUT** (ADR-001 w17 clause 7) — recorded, not queued.

### Superseded items

**None.** `w18-requirements.md` §6 records no "cancels / replaces" statement; no status banner and no `status: superseded` line is written.

### ADRs touched

- **Amended by w18 footers** (bodies untouched): ADR-029 (phrase-edit provenance + box/wording), ADR-017 (`prebuilt-layout` called), ADR-003 (geometry + override columns).
- **`none — no change`, with reasons recorded**: ADR-001, ADR-002, ADR-005, ADR-012, ADR-016, ADR-018, ADR-019, ADR-020, ADR-022, ADR-024, ADR-026, ADR-027, ADR-028.
