---
wave: w15
source: inputs/next/w15-todo.md
source_sha256: d54da30d7564ea5cebdfd5bc003404626107ebf04daeec4ad2f5858cd3b51e6f
design_sources: [inputs/design/prototypes/raffa-v2/screens-v2.md]
baseline: 3720543 (helix/w15) — origin/main is 3c89d35, five commits ahead; backend/src, web/src, infra/ and .github/ are byte-identical between the two (see W15-01)
generated: 2026-09-13T18:15Z
previous_wave: w14
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w15 — normalized requirements

Written by `next-intake` from the raw file above. The raw file stays the
human's; this file is the process oracle for the council and the decomposer.
Items keep the source ids when the raw file has them (`NW-27`), otherwise
they get `W15-NN`. Every claim about "today" cites a file in `../`.

Theme, from the raw file's own §5 grouping: **W15 — upload feels instant, and
inviting a colleague works end to end.** Two outcomes lead the wave (§0.3 of
the raw file, stakeholder 2026-09-13); the W15 queue inherited from
`w14-requirements.md` §5 follows.

## 1. Oracles in force

- Product: `inputs/product-spec.md` §20 Day 1 (`:863` "Create a workspace and
  invite Procurement users"), §13.4 / §16.1 (`:619`, `:626`, `:756` — email /
  notification delivery is **P1 / V1**, not a non-goal);
  `inputs/requirements.md` R-DOC-01 (`:147` multi-file upload), R-DOC-03
  (`:163` admission gate), R-DOC-09 (`:225` processing stages surfaced, polled
  to terminal), and **A7 (`:827`) — "upload stays synchronous in the request
  for V2 … moving it to the worker queue is a later task" — which this wave's
  raw file explicitly supersedes** (see OQ-w15-003);
  `inputs/percorso-pilota-v1.md` §2 (`:36`) / §5 (`:110`) / §7 (`:152`), whose
  "Non è onboarding: … email" and "Niente email nel pilota" exclusions the
  stakeholder ruling of 2026-09-13 overrides for this wave (OQ-w15-002).
- Locked: `reports/context/locked-decisions.md` — Azure only; `dev` + `demo`,
  no production; cheapest SKUs, nothing idle-expensive; HCP Terraform under
  `infra/`; OIDC / Entra ID, secrets in Key Vault, never in source or bundle;
  API-first; modular monolith **+ background worker**. Cite, never re-open.
- ADRs touched by this wave: **ADR-002** (Api + Worker hosts; the queue port),
  **ADR-005** (Service Bus Standard already in the SKU list; the w14 footer's
  ACS Email design, "decided, not applied"), **ADR-007** (a new
  `infra/modules/communication`), **ADR-009** (worker-side tenant scoping),
  **ADR-010** (the w14 footer "the token carries identity only" binds NW-05 /
  NW-08 by name), **ADR-011** (a mail secret in Key Vault; no mail body in
  logs or audit rows), **ADR-012** (a client store never stands in for a
  missing GET — governs NW-10 and NW-61), **ADR-014 / ADR-016** (wave base;
  the promotion + HCP-apply gate, and `Invitations__AcceptUrlBase` as the key
  `backend.yml` cannot deliver), **ADR-015** (the identity that calls Graph),
  **ADR-017 / ADR-004** (OCR and classify move off the request thread),
  **ADR-018 / ADR-019 / ADR-020** (documents states, invite pane, screen 11),
  **ADR-021** (a new document status value is a migration), **ADR-022** (the
  interim header posture NW-05 retires), **ADR-024** (R-DOC-01/03/09 and the
  admission gate), **ADR-025 / ADR-026** (the invitation lifecycle this wave
  extends with an identity and a transport).
- Design: `inputs/design/prototypes/raffa-v2/screens-v2.md` §3 (`:59-79` —
  the six real stage strings "Uploading · Classifying · OCR / text · Sections
  & tables · Extracting facts · Validating schema", the **"Not added"**
  rejected card marked *session-only, never counted*, and the state inventory
  `onboarding empty · uploading/processing · needs_review · completed ·
  failed (retry) · rejected (not added) · attention-empty · list error`);
  §2 (`:25-30` — the Ask off-state `askOffReason` "Your document is still
  processing or waiting for review. Ask only answers from facts that passed
  validation — so it never guesses."); §10 (`:147-154` — Members table and
  invite form). The invite pane's delivery copy is **already pre-decided** in
  the ADR-020 w14 footer and is not re-opened.
- Last wave: **no `reports/execution/wave-close-w14.md` exists** — the w14
  close ran as HITL issue #94 and, per the raw file §4, could not see that
  `main` was red on two Testcontainers fixtures (fixed in PR #97). The three
  close reports on disk (`wave-close.md` = e05, `wave-close-e13.md`,
  `wave-close-r0-a.md`) carry no `salvage/*` tag that falls in W15 scope. w14
  delivered every in-wave item (`BACKLOG.md:186` — "Nothing the council
  decided for an in-wave item was dropped for the cap").
- Carry-over: `reports/workitems/BACKLOG.md:175-183` §"Queued for the next
  wave" — **W15 = NW-05, NW-06, NW-07, NW-08, NW-31, NW-32**, with no task
  file written for any of them. These enter §5 **first** as carry-over
  (queue discipline, binding since w15). Also carried: ADR-025 §H **T14** is
  authored in w14 and `Skip`ped (`E15/F01/US01/T01`), "to be activated by
  NW-05 / NW-08" (`BACKLOG.md:177-178`). NW-27 and NW-61 were queued to W18
  by the w14 intake and are **promoted to `must` in W15** by the stakeholder
  ruling — the defect §0.2 of the raw file names.

### Baseline hazards (read before decomposing)

1. **This checkout is behind `origin/main`.** HEAD is `3720543`
   (`helix/w15`); `origin/main` is `3c89d35`, **five commits ahead**
   (`git log --oneline HEAD..origin/main`): `566326e` (E14/F06/US01/T01 — w14
   final integration), `fdf56a3` (w14 known gap 8), `a0964ae`, `08d4785`
   (PR #98), `3c89d35` (PR #96). The merge-base is `9d3da0a`. Consequences
   that matter to this wave: **`docs/waves/w14-acceptance.md` (568 lines) and
   `web/e2e/invite.spec.ts` (262 lines) do not exist on this tree** — both are
   named by the raw file (§2 NW-67 evidence, §2 NW-58 residual, §6 N3b), and
   `Glob ../docs/waves/*` returns nothing here. `web/README.md` and
   `backend/README.md` are also shorter here. **Everything else is identical**:
   `git diff --stat origin/main..HEAD -- backend web infra .github docs
   scripts` lists **only** those five files, so **no file under
   `backend/src`, `web/src`, `infra/` or `.github/workflows/` differs** and
   every code citation in §2 holds verbatim on `origin/main`. See **W15-01**.
2. **The two `must` outcomes both need Terraform.** The raw file §0.4 lifts
   w14's zero-infra-delta rule. Three distinct applies are in scope (Service
   Bus wiring + worker scaling, ACS Email, a Graph application permission),
   each through the **ADR-016 HITL gate** — HCP runs plan/apply remotely
   (`../.github/workflows/infra.yml` is fmt/validate/plan only), and
   `backend.yml` deploys `--image` only, so **no new API environment variable
   can arrive except through Terraform + an operator-confirmed HCP apply**
   (ADR-016 w14 footer `:112`). The two shortcuts (`--set-env-vars`, folding a
   value into the one `dynamic "env"` block) are **named drift**.
3. **Operator hygiene, not a wave item**: `git -C .. status --porcelain`
   shows only `fed-cred-demo.json` and `fed-cred-dev.json`, still untracked at
   the repo root — the same two files w14 flagged. Neither is referenced by
   `infra/` or a workflow. No W15 task touches them.

## 2. Items

| ID | Title | Kind | Priority | Area | Status today | Seats | ADR touchpoints | Design refs | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| W15-01 | Wave base is `origin/main` @ `3c89d35` | ops | must | ci | OPEN | delivery-manager | ADR-014 | — | W15-A1 |
| NW-27 | `POST /api/documents` returns once the file is stored; processing on the Worker | feature | must | backend, infra | OPEN | software-architect, cloud-architect, delivery-manager, product-owner, ux-ui-designer | ADR-002, ADR-005, ADR-009, ADR-017, ADR-021, ADR-024 | `screens-v2.md:59-79` | A15-1, A15-2 |
| NW-61 | Upload feels instant; details only when the document is ready | change | must | web, backend | PARTIAL | client-architect, ux-ui-designer, software-architect, product-owner | ADR-012, ADR-018, ADR-020, ADR-024 | `screens-v2.md:25-30,59-79` | A15-1, A15-3 |
| NW-10 | Rail Documents badge reads the server, not `sessionStorage` | bug | should | web | OPEN | none | ADR-012 (w14 footer, already decided) | — | A15-1 |
| NW-67 | Raffa provisions the invitee's Entra B2B guest at invite time (Graph) | feature | must | auth, backend, infra | OPEN | security-architect, cloud-architect, software-architect, client-architect, delivery-manager, product-owner | ADR-010, ADR-011, ADR-015, ADR-025, ADR-026 | `screens-v2.md:147-154` | A15-4, A15-5, A15-7 |
| NW-68 | Raffa sends the invitation email (ACS Email) | feature | must | infra, backend | OPEN | cloud-architect, delivery-manager, software-architect, security-architect, ux-ui-designer, product-owner | ADR-005, ADR-006, ADR-007, ADR-011, ADR-016, ADR-020, ADR-026 | `screens-v2.md:147-154` | A15-4, A15-6 |
| NW-69 | Invite pane: honest delivery and identity state | design | should | web | OPEN | ux-ui-designer, client-architect | ADR-019, ADR-020 | `screens-v2.md:147-154` | A15-6, A15-7 |
| NW-58r | Invitation e2e (N3b) runs because the flow creates the second account | carry-over | should | ci, web | PARTIAL | delivery-manager, security-architect | ADR-014, ADR-016, ADR-025 §H | — | N3b |
| NW-05 | API JWT (ADR-010) replaces spoofable headers | carry-over | must | auth, backend, web, infra | OPEN | security-architect, software-architect, client-architect, cloud-architect, delivery-manager | ADR-010, ADR-015, ADR-022, ADR-025 | — | A15-8 |
| NW-06 | Workspace role from membership / claims, never a client assertion | carry-over | must | auth, backend | PARTIAL (header branch CLOSED-ON-MAIN) | security-architect, client-architect | ADR-010, ADR-022 | — | A15-8 |
| NW-07 | Conversation `user_id` is the token subject | carry-over | should | backend | PARTIAL | software-architect, security-architect | ADR-010, ADR-024 | — | A15-8 |
| NW-08 | `GET /api/audit` works for a real Admin | carry-over | should | backend, auth | OPEN | security-architect, software-architect | ADR-010, ADR-011, ADR-026 | — | A15-8 |
| NW-31 | Retire the dual role headers (`X-Role` / `X-Workspace-Role`) | carry-over | should | backend, ci | PARTIAL | none | ADR-022 (w14 footer, already decided) | — | A15-8 |
| NW-32 | One answer for an absent caller identity, never `"unattributed"` | carry-over | should | backend | OPEN | security-architect, software-architect | ADR-011, ADR-022 | — | A15-8 |
| NW-70 | Create-workspace form submits the ISO country code | bug | — | web | CLOSED-ON-MAIN | none | — | — | — |

### W15-01 — Wave base is `origin/main` @ `3c89d35`

- **Source**: this intake (the raw file's comparison line names `origin/main`
  @ `08d4785`, which is **not** this checkout)
- **Raw**: "Compared | `origin/main` @ `08d4785` … w14 closed by PRs #92, #95,
  #96 + follow-ups #93, #97, #98"
- **Today (evidence)**: `git -C .. rev-parse --short HEAD` → `3720543`,
  branch `helix/w15`; `git -C .. rev-parse --short origin/main` → `3c89d35`;
  `git -C .. merge-base --is-ancestor origin/main HEAD` → **NO**.
  `git log --oneline HEAD..origin/main` → five commits, headed by `566326e`
  "E14/F06/US01/T01: w14 final integration — no-drift proof, N3 reload, N3b
  two-account e2e, acceptance runbook, README sweep". `Glob ../docs/waves/*`
  → **no files**; `../web/e2e/` holds only `day1.spec.ts` and `v2.spec.ts`.
- **Gap**: two oracles this wave cites do not exist on this tree
  (`docs/waves/w14-acceptance.md`, `web/e2e/invite.spec.ts`). The product
  source is unaffected — the full `git diff --stat origin/main..HEAD` over
  `backend web infra .github docs scripts` is exactly five files, none of them
  under a `src/` or `infra/` path.
- **Seats**: delivery-manager (ADR-014 w14 footer already rules that a wave
  base is produced by **merging**, never by branching afresh, and is proven
  green before task 1)
- **ADR touchpoints**: none — ADR-014's w14 footer already governs; the seat
  records the ordering
- **Design refs**: none
- **Acceptance**: **W15-A1** — before the first task, the wave base contains
  `origin/main` @ `3c89d35`; `docs/waves/w14-acceptance.md` and
  `web/e2e/invite.spec.ts` resolve; one `dev` deploy is green.
- **Proposed epic**: epic-16 (recorded by the wave's final-integration task,
  as `E14/F06/US01/T01` recorded W14-A1)
- **Task sketch**: no fan-out task — an operator act at HITL, exactly as
  W14-01 was (`BACKLOG.md:158`).

### NW-27 — `POST /api/documents` returns once the file is stored

- **Source**: NW-27, `inputs/next/w15-todo.md` §1 (carried from
  `next-waves-todo.md` §6; queued W18 and demoted to `should` by the w14
  intake — **reversed here**, §0.2)
- **Raw**: "il caricamento deve essere quasi immediato a livello UI, poi
  continua in background" — `POST /api/documents` answers **within ~2 s**
  once the blob and the `document` row exist (status `Uploaded`), for every
  file of a batch; classify / OCR / extraction / embedding run on
  `Raffa.Worker` through a **durable** queue.
- **Today (evidence)**: the whole pipeline is awaited inline inside the
  request. `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:81` maps
  the route; `:209-215` reads the entire file into memory; `:229-230` awaits
  the **admission gate**, which itself parses/OCRs
  (`../backend/src/Raffa.Documents.Contracts/Application/Admission/DocumentAdmissionGate.cs:110-112`)
  and calls the Foundry `classify` role (`:136-138`); `:255-257` awaits
  `UploadAsync`; `:265-273` awaits `processingPipeline.ProcessAsync` (staged
  extraction + per-page embedding,
  `../backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs:227-288`);
  only `:280-288` returns 201. The pipeline's own type comment states it:
  `DocumentProcessingPipeline.cs:65-77` — "**Synchronous, in-request, not a
  queue dispatch** … nothing in this codebase enqueues a durable message".
  `Raffa.Worker` is deployed (`../.github/workflows/backend.yml:138-143`,
  `:203-208` push and deploy `ca-raffa-<env>-worker`) but consumes an
  in-process `ConcurrentQueue`
  (`../backend/src/Raffa.Worker/Queue/InMemoryQueueConsumer.cs:17-59`) and its
  hosted service **dispatches nothing** — it logs the message id and completes
  it (`../backend/src/Raffa.Worker/Queue/QueueConsumerHostedService.cs:36-37`).
  Service Bus **is** provisioned (`../infra/modules/servicebus/main.tf:11-18`
  namespace `sbns-raffa-${var.environment}` Standard, `:20-23` topic
  `extraction-events`, instantiated `../infra/environments/dev/main.tf:98-103`
  and `demo/main.tf:119-124`) but its outputs are **never passed to
  `module "containerapps"`**: no `ServiceBus*` / `Queue*` env var exists in
  `../infra/modules/containerapps/main.tf`, and
  `../backend/src/Raffa.Worker/Raffa.Worker.csproj` has no
  `Azure.Messaging.ServiceBus` reference.
  **A durable work record already exists**:
  `../backend/src/Raffa.Documents.Contracts/Application/DocumentUploadService.cs:114-121`
  already inserts an `ExtractionJob` row with `Stage = Classification,
  Status = Queued` in the same transaction as the `Document` (`:95`,
  `ProcessingStatus = Uploaded`) — the seam a worker can claim.
  `POST …/{id}/reprocess` is mapped at `DocumentsEndpointExtensions.cs:85` and
  is **also fully synchronous**, and more expensive than upload (it re-parses,
  re-classifies and re-embeds —
  `../backend/src/Raffa.Documents.Contracts/Application/DocumentReprocessService.cs:58-116`;
  the operator workflow allows it ten minutes,
  `../.github/workflows/reprocess-tenant-documents.yml:252` `--max-time 600`).
  No HTTP request timeout is pinned anywhere: the API ingress block is
  `../infra/modules/containerapps/main.tf:179-194` with no `timeout`
  attribute, so the Container Apps platform default (≈ 240 s) is in force
  implicitly and undocumented; the SPA sets no `AbortController` either
  (`../web/src/api/client.ts:1726`).
- **Gap**: a batch of N files occupies N synchronous requests, each running
  OCR + classify + extraction + embedding against live Foundry, against an
  unpinned ~4-minute platform ceiling. **Two things are missing, not one**: a
  durable dispatch (producer + Service Bus wiring + a worker handler for the
  `ExtractionJob` row that already exists), and a **terminal state for a
  refused file** — `DocumentProcessingStatus`
  (`../backend/src/Raffa.Documents.Contracts/Domain/DocumentProcessingStatus.cs:4-11`)
  has exactly `Uploaded, Processing, NeedsReview, Completed, Failed` and **no
  `Rejected`**; today a refusal is a 422 with nothing persisted
  (`DocumentsEndpointExtensions.cs:242-252`) and the web treats "not added" as
  session-only (`../web/src/routes/documents/documentTable.ts:74-79`). If the
  gate moves to the worker, a refused file **must** become a row.
- **Seats**: software-architect (the queue contract, the producer seam, the
  `ExtractionJob` claim/retry/idempotency model, the reprocess shape);
  cloud-architect (Service Bus → container-app wiring, worker replica scaling
  and its cost, which env keys land where); delivery-manager (Terraform + HCP
  apply before the feature, worker deploy/scale path, ordering); product-owner
  (does the admission gate stay in the 2 s budget or move to the worker with a
  terminal `Rejected` row — a product rule, R-DOC-01/03; and A7's
  supersession); ux-ui-designer (a server-side `Rejected` row contradicts
  `screens-v2.md:72-75`'s *session-only, never counted* "Not added" card —
  the states contract needs the ruling)
- **ADR touchpoints**: amend ADR-002 (the queue port becomes a durable
  transport with a real handler), amend ADR-005 (Service Bus moves from
  provisioned to wired; worker scaling + cost), amend ADR-024 (R-DOC-01 /
  R-DOC-03 become asynchronous; A7 retired), amend ADR-021 **if** a new
  `processing_status` value ships (it is a `character varying(30)` with no
  CHECK — `Migrations/20260902205243_Initial.cs:126` — so the migration is a
  data/contract change, not a type change), ADR-009 (the worker opens its own
  tenant scope), ADR-017 / ADR-004 (OCR and classify leave the request thread)
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:59-79`
  (the six stage strings already implemented verbatim in
  `../backend/src/Raffa.Documents.Contracts/Application/DocumentProcessingStageMap.cs:20-39`;
  the "Not added" card and the `rejected (not added)` state)
- **Acceptance**: **A15-1** (drop 15 PDFs → within 2 s all 15 rows exist
  server-side, "All documents · 15", survive a reload and a second browser;
  each reaches Needs review / Completed / Failed with the browser closed;
  `POST /api/documents` p95 < 2 s); **A15-2** (restart API and Worker
  mid-batch → no document lost, every row terminal)
- **Proposed epic**: epic-16-async-document-processing (extends epic-01 F06,
  epic-13 F04)
- **Task sketch**: (a) Terraform — pass `module.servicebus` outputs into
  `containerapps` for both hosts, worker `min_replicas`/scale rule; (b)
  producer + durable consumer around the existing `ExtractionJob` row, with
  claim, retry and idempotency; (c) endpoint returns after `UploadAsync`;
  `reprocess` takes the same shape; (d) the refusal path (gate placement +
  terminal state) per the council's ruling.

### NW-61 — Upload feels instant; details only when the document is ready

- **Source**: NW-61, `inputs/next/w15-todo.md` §1 (queued W18 by the w14
  intake — **reversed here**, §0.2)
- **Raw**: "within ~2 s of a drop every file is a **server row** (counted,
  listed, survives a reload and shows in a second browser), then progresses
  `Uploaded → Processing (stage) → Needs review | Completed | Failed` by
  polling … Ask, Portfolio and Contract 360 gate on completeness (a document
  still processing is shown as such, never as an empty contract or a
  fabricated fact)".
- **Today (evidence)**: the client half is built and correct — the optimistic
  row is created before the request starts
  (`../web/src/routes/documents/useDocumentsList.ts:132-134`), the server
  stage and its progress bar render from `item.stage`
  (`../web/src/routes/documents/DocumentStatusTable.tsx:151-155`, never a
  client timer — `documentTable.ts:67`), and the 2 s poll exists
  (`useDocumentsList.ts:12-13`, `:110-119`). **Three defects follow from
  NW-27, and one is independent**:
  (1) the poll predicate reads the **server** list —
  `useDocumentsList.ts:105-108` `hasNonTerminalRow` — so while the first
  upload of a session is still inside the synchronous POST the server list is
  empty and **no polling runs at all**;
  (2) both counters read the server list only
  (`useDocumentsList.ts:122-123`, rendered
  `../web/src/routes/documents/AttentionFilter.tsx:22-27`), which is exactly
  the reported "Needs your attention · 0 / All documents · 0" beside 15 rows
  saying "Uploading…" (`DocumentStatusTable.tsx:105`);
  (3) uploads run at most 3 in flight
  (`../web/src/routes/documents/uploadPipeline.ts:22`, `:136-137`) with **no
  timeout** on the request (`../web/src/api/client.ts:1726`), so a request the
  platform kills lands in `failed` (`uploadPipeline.ts:127-132`,
  `useDocumentsList.ts:151-156`).
  **Completeness gating is absent everywhere but one string**: Contract 360
  (`../web/src/routes/contracts/contract360/index.tsx:72-117` load, `:229-255`
  render) has no `processingStatus` read at all; the Portfolio route
  (`../web/src/routes/contracts/index.tsx`) likewise; the only "still
  processing" surface in the whole web app is Ask's off-state copy
  (`../web/src/routes/ask/askViewModel.ts:250-261`), and it is **inferred**,
  not checked — the gate is `kbReady` (validated-contract count > 0) plus
  `hasAnyDocument`, which is `totalCount > 0` for documents **in any state**
  (`../web/src/routes/ask/index.tsx:93`, `:99-105`, `:254-257`).
- **Gap**: once NW-27 makes the server row appear immediately, (1) and (2)
  resolve themselves; what this item must still deliver is the **downstream
  completeness contract** — Contract 360, Portfolio and Ask must distinguish
  "nothing here" from "still processing", which needs a server-side
  completeness signal on the aggregates, not a second client heuristic.
- **Seats**: client-architect (the read-back contract, the poll predicate, the
  request timeout, and ADR-012's provenance rule); ux-ui-designer (the
  "still processing" state on three screens that have none, and whether the
  Ask off-state copy is reused or narrowed); software-architect (the
  completeness signal the aggregates must return — Contract 360 renders
  whatever the aggregate gives it); product-owner ("never an empty contract or
  a fabricated fact" is a product promise — it ranks above showing something)
- **ADR touchpoints**: amend ADR-012 (the provenance rule already forbids a
  client store standing in for a GET; this adds "a client must not infer a
  server state it can be told"), amend ADR-018 / ADR-020 (the processing state
  joins three more screens), ADR-024 (grounding: Ask never answers from a
  document that has not passed validation)
- **Design refs**: `screens-v2.md:25-30` (`askOffReason` — the exact sentence
  already shipped at `askViewModel.ts:254`), `screens-v2.md:59-79` (the list
  states and stages)
- **Acceptance**: **A15-1** (the rows, counters, reload and second browser);
  **A15-3** (open Ask / Portfolio / Contract 360 while a document is
  processing → "still processing", never an empty contract or an invented
  fact)
- **Proposed epic**: epic-16-async-document-processing
- **Task sketch**: (a) completeness signal on the Contract 360 / Portfolio
  aggregates + the Ask gate reading a real status; (b) the three screens'
  processing states; (c) poll predicate + request timeout on the upload call.

### NW-10 — Rail Documents badge reads the server

- **Source**: NW-10 (queued **W16** by the w14 intake); pulled forward by the
  raw file §1 — "the rail's Documents badge reads the server (this overlaps
  NW-10 — take NW-10 into W15 if the same task touches it)"
- **Raw**: "the rail's Documents badge reads the server"
- **Today (evidence)**: `../web/src/components/shell/RailNav.tsx:67` calls
  `loadTrackedDocuments()`, `:72-75` derives the badge from it;
  `../web/src/routes/documents/documentStore.ts:31`, `:67-69` read
  `sessionStorage["raffa.documents.readback"]`; the **write half has no
  caller** — `documentStore.ts:11-25` says so verbatim ("`rememberDocument` …
  now has no caller anywhere in `src/routes/documents/` … a known, flagged
  regression"), which I confirmed. Net behaviour: the badge is **always
  absent** in any fresh session, whatever the tenant holds
  (`../web/src/components/shell/navItems.ts:99-107`). `navItems.ts:84-92`
  names NW-10 by id. `RailNav.tsx:61-66`'s own comment claims "There is still
  no `GET /api/documents` collection endpoint" — **that is stale**: the
  endpoint exists (`DocumentsEndpointExtensions.cs:82`) and
  `useDocumentsList` already consumes it. The sibling secondary badge already
  reads the server
  (`../web/src/components/shell/useValidatedContractCount.ts:59-67`).
- **Gap**: one component reads a dead `sessionStorage` key instead of the
  endpoint that already exists.
- **Seats**: **none** — a contained web bug whose rule is already accepted
  (ADR-012 w14 footer: "a client store never stands in for a missing GET";
  the three sibling stores were deleted in w14 and this one was missed).
- **ADR touchpoints**: none — ADR-012's w14 footer already decided it
- **Design refs**: none
- **Acceptance**: folded into **A15-1** — after the drop, the rail badge
  matches the list.
- **Proposed epic**: epic-16-async-document-processing
- **Task sketch**: `RailNav` reads the documents list (or a small count
  hook mirroring `useValidatedContractCount`); delete `documentStore.ts`'s
  dead read/write pair and the stale comment.

### NW-67 — Raffa provisions the invitee's Entra B2B guest at invite time

- **Source**: NW-67, `inputs/next/w15-todo.md` §2 (new; decided by the
  stakeholder 2026-09-13 among three options)
- **Raw**: "On invite, Raffa creates the **Entra B2B guest** for the invited
  address in the tenant through Microsoft Graph `POST /invitations` …
  `sendInvitationMessage: false` — Raffa's own mail is the channel … accept
  completes without a further click … Provisioning failure … is a **named**
  error on the invite pane and an audit row — never a 'ready' link that cannot
  be used."
- **Today (evidence)**: **nothing talks to Entra.** `POST
  /api/workspaces/{tenantId}/invites`
  (`../backend/src/Raffa.Api/WorkspaceInvitesEndpointExtensions.cs:66`,
  handler `:71-154`, guards 401→404→403 `:80-113`) writes `workspace_user` if
  absent and always a `workspace_invitation`
  (`../backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs:118-131`,
  `:153-168`), mints a `{tenantId:N}.{secret}` token stored only as SHA-256
  (`…/WorkspaceInvitationService.cs:68-74`), and returns a **site-relative
  fragment** accept link (`:58`, `:86`;
  `WorkspaceInvitesEndpointExtensions.cs:137-150`). Repo-wide there is **no**
  `Microsoft.Graph` package, no `GraphServiceClient`, no
  `graph.microsoft.com`, no `User.Invite.All` — in `../backend` or `../infra`.
  The single Graph mention in Terraform is a **negative**:
  `../infra/modules/identity/main.tf:41-42` — "web.yml reads it over ARM so
  the deploy job does not need Microsoft Graph (ADR-015)"; `:131-143` declares
  no Graph `required_resource_access`.
  **The tenant shape supports the chosen option**: both environments
  authenticate against the *same* single tenant
  (`AzureADMyOrg` at `../infra/modules/identity/main.tf:63` and `:118` —
  "dev/demo isolation is by distinct registration, not by separate Entra
  tenants"; live `oidcAuthority` values recorded for both SWAs carry the same
  GUID, `../tests/test_check_demo_swa_config.py:32`, `:42`), and
  `../scripts/write_web_runtime_config.py:80-82` **forbids** a `common` /
  `organizations` authority — so an address unknown to that tenant cannot sign
  in, which is exactly the reported failure.
  **A per-environment workload identity already exists and is already
  attached to both hosts** (`../infra/modules/identity/main.tf:36-46`;
  `../infra/modules/containerapps/main.tf:34-37` API, `:214-217` worker) with
  Key Vault Secrets User (`../infra/modules/keyvault/main.tf:48-53`) — the
  natural Graph caller.
  **Accept needs a second click today**:
  `../web/src/routes/invite/accept/index.tsx:224-238` renders "Continue with
  Microsoft Entra ID" (`handleContinueWithEntra` → `loginPopup`, `:172-183`)
  and only then a separate "Join" button (`handleJoin`); no effect calls
  `handleJoin` when the account appears. The accept match is already
  case-insensitive on the email
  (`…/WorkspaceInvitationService.cs:187`, `:197`), and the identity sent is
  the MSAL account username (`../web/src/main.tsx:70-73` →
  `../web/src/api/client.ts:55-58` `X-User-Id` →
  `../backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs:58-73`,
  lower-cased) — so for a B2B guest the invited address is the identity, and
  the existing match holds.
  Audit verbs are inline string literals with no constants class —
  `workspace.invitation.issued` (`WorkspaceMembershipService.cs:186`),
  `.accepted` (`:311`), `.revoked` (`WorkspaceInvitationService.cs:260`,
  `WorkspaceMembershipService.cs:412`), `.rejected`
  (`WorkspaceInvitationService.cs:206`), `workspace.membership.granted`
  (`:306`) / `.removed` (`:404`) — so new verbs are additive, no schema change
  (`../backend/src/Raffa.Audit/Domain/AuditEvent.cs:29`, free-form action).
- **Gap**: the invitation is an offer to an identity **that does not exist**.
  w14 recorded "a second Entra account on the pilot tenant" as an *operator
  prerequisite* (`reports/audit/w14-hitl.md:302`); this item makes it the
  product's own job.
- **Seats**: security-architect (the application permission and its blast
  radius, admin consent, what an invitation-time write to the directory means
  for tenancy, the audit rows, and the rule that removing a member never
  deletes the guest); cloud-architect (managed identity vs app registration,
  whether Terraform manages the Graph app-role assignment and the consent,
  where any secret lives); software-architect (where the Graph call sits — API
  or Worker —, the already-a-member/guest no-op, the failure contract so an
  unusable link is never returned); client-architect (accept completing
  without a second click, and what the popup's return actually lands on);
  delivery-manager (admin consent is a one-time operator act at the ADR-016
  gate; Terraform before the feature); product-owner (a guest in the company
  directory per invited address is a scope statement, not an implementation
  detail)
- **ADR touchpoints**: amend **ADR-010** (the tenant now provisions guests;
  the authority stays single-tenant), amend **ADR-015** (a new application
  permission held by a workload identity), amend **ADR-011** (if a secret is
  involved) and **ADR-025** (new audit verbs
  `workspace.guest.provisioned` / `.provisioning_failed`; the
  removal-does-not-delete-the-guest rule; invite becomes a directory write),
  amend **ADR-026** (the invite 201 gains a provisioning outcome)
- **Design refs**: `screens-v2.md:147-154` (§10 members/invite); the named
  error state belongs with NW-69 and the ADR-020 w14 footer
- **Acceptance**: **A15-4** (invite a gmail address the tenant has never seen
  → mail within 1 min → link → passcode sign-in → inside the workspace as
  Procurement, **no** "Join" click, **no** Azure portal; roster Active);
  **A15-5** (an address already in the tenant → same flow, no duplicate
  guest); **A15-7** (admin consent missing → named error on the pane, audit
  row, no invitation claiming "sent")
- **Proposed epic**: epic-17-invitation-delivery-and-identity (extends
  epic-15)
- **Task sketch**: (a) Terraform/consent for the Graph permission on the
  workload identity; (b) an `IGuestProvisioner` seam called from the invite
  transaction with a named failure contract; (c) accept auto-completes after
  the popup resolves.

### NW-68 — Raffa sends the invitation email (ACS Email)

- **Source**: NW-68, `inputs/next/w15-todo.md` §2 (NW-58's first deferred
  `must` clause)
- **Raw**: stakeholder 2026-09-13, "dovevi già farlo ora" — "apply the ADR-005
  design in both environments (Terraform + HCP apply through the ADR-016
  gate); sender identity / secret via Key Vault; an `IInvitationMailer`
  implementation sending a Raffa-branded mail with the **absolute** accept
  link … `mailDelivered: true` only on an accepted send".
- **Today (evidence)**: the seam exists and is empty.
  `../backend/src/Raffa.Identity.Workspace/Application/IInvitationMailer.cs:16-33`
  declares `Task<bool> TrySendAsync(...)`;
  `…/Infrastructure/NullInvitationMailer.cs:16-38` logs one line and returns
  `false`, deliberately never interpolating the accept URL (`:25-29`);
  `…/Infrastructure/ServiceCollectionExtensions.cs:61` is the **only**
  registration. `mailDelivered` is that bool verbatim
  (`…/WorkspaceInvitationService.cs:99-103`) and reaches the pane
  (`../web/src/routes/workspace/members/InvitePane.tsx:116-120` "Invitation
  sent to {email}." when `true`; `:122-140` the copyable link + expiry when
  `false`) — so today the pane always takes the link path.
  **No ACS anywhere in `../infra`**: a case-insensitive grep of every `.tf`
  for `communication|email|acs|smtp|sendgrid|mailer` returns **zero** source
  matches; Key Vault provisions exactly two secrets
  (`../infra/modules/keyvault/main.tf:81-101` — `postgres-connection`,
  `storage-connection`) and the API container app exactly two secret handles
  (`../infra/modules/containerapps/main.tf:44-54` — `pg-cs`, `st-cs`).
  **No SPA-origin config key exists** in `../backend` or `../infra`; the
  accept link is a hardcoded relative constant
  (`…/WorkspaceInvitationService.cs:58`, `:86`) that only the browser
  resolves (`../web/src/routes/workspace/members/memberViewModel.ts:177-179`,
  `InvitePane.tsx:131`, `:134`). **But Terraform already knows the SPA
  host**: `../infra/modules/containerapps/main.tf:188-193` sets CORS
  `allowed_origins = ["https://${var.spa_host_name}"]`, declared at
  `../infra/modules/containerapps/variables.tf:59` and wired from
  `module.staticwebapp.default_host_name`
  (`../infra/environments/dev/main.tf:120`, `demo/main.tf:144`) — so
  `Invitations__AcceptUrlBase` is a **reuse of an existing variable**, not a
  new discovery.
  **The design is already accepted and must not be re-litigated**: the ADR-005
  w14 footer fixes ACS Email + Azure Managed Domain (`ADR-005:156-165`), the
  rejected alternatives (`:167`), a new `infra/modules/communication` wired
  `communication → keyvault → containerapps` (`:192-194`), Key Vault secret
  `acs-connection` → container-app handle `acs-cs`, the four config keys
  `Invitations__Mail__Enabled` / `__SenderAddress` / `__ConnectionString` and
  `Invitations__AcceptUrlBase` (`:200-202`), the per-env
  `var.invitation_mail_enabled` flag mirroring `ai_gateway_wired` (`:206`),
  `data_location = "Europe"` (ADR-006 w14 footer `:82`) and the failure copy
  (ADR-020 w14 footer).
- **Gap**: a decided design that has never been applied. `backend.yml`
  deploys `--image` only, so both new env keys arrive **only** through
  Terraform + an operator-confirmed HCP apply (ADR-016 `:112`, `:128`).
- **Seats**: cloud-architect (the module, both environments, cost at the
  ~$0.00025/message order recorded in ADR-005, the managed-domain sender);
  delivery-manager (the HCP apply ordering and the promotion sequence — this
  is the first wave to add an API environment variable, the exact case ADR-016
  w14 named); software-architect (the real `IInvitationMailer`, absolute-link
  composition, and `mailDelivered` staying a server fact); security-architect
  (the secret via managed identity, and "no mail body in logs or audit rows",
  plus the new `workspace.invitation.mail_sent` / `.mail_failed` verbs);
  ux-ui-designer (the `true`-path copy and the re-send action; the failure
  copy is already pre-decided and is only confirmed); product-owner (the
  pilot-path doc excludes email — OQ-w15-002)
- **ADR touchpoints**: amend **ADR-005** (decided → applied; confirm the cost
  line), **ADR-007** (the new module), **ADR-011** (the `acs-connection`
  secret), **ADR-016** (the first per-environment API key; promotion
  ordering), **ADR-026** (`acceptUrl` becomes absolute when the transport is
  on — the shape §D5 left unstated, raised in the ADR-005 second w14 footer);
  ADR-020 action is likely `none — already decided in the w14 footer`
- **Design refs**: `screens-v2.md:147-154`; ADR-020 w14 footer (screen 10's
  two `mailDelivered` strings and the pre-decided delivery-failure copy)
- **Acceptance**: **A15-4** (the address receives a Raffa email within 1 min);
  **A15-6** (mail transport failing → ADR-020 failure copy + "Try sending
  again"; `mailDelivered: false`; the link still works)
- **Proposed epic**: epic-17-invitation-delivery-and-identity
- **Task sketch**: (a) `infra/modules/communication` + both env roots + the
  two container-app env keys + `acs-connection` (own phase, HCP apply at the
  gate); (b) `AcsInvitationMailer` + configuration binding; (c) the pane's
  `true` path and the re-send action.

### NW-69 — Invite pane: honest delivery and identity state

- **Source**: NW-69, `inputs/next/w15-todo.md` §2
- **Raw**: "With NW-67 / NW-68 landed: show delivery state (sent · could not
  be sent · provisioning failed) and the expiry. If either slips, the pane
  must tell the Admin what the invitee still needs (an account on the tenant)
  — today the first signal is Microsoft's error inside the popup, where Raffa
  cannot intervene."
- **Today (evidence)**: the pane has exactly **two** states, driven by one
  boolean — `../web/src/routes/workspace/members/InvitePane.tsx:116-120`
  ("Invitation sent to {email}.") and `:122-140` (copy link + expiry),
  mapped from the 201 body at
  `../web/src/routes/workspace/members/index.tsx:127-131` /
  `memberViewModel.ts:171-173`. With `NullInvitationMailer` always returning
  `false` (`NullInvitationMailer.cs:30-36`) only the second is reachable
  today, which is why `docs/waves/w14-acceptance.md` known gap 1 calls an
  "Invitation sent to …" on `dev` a **defect**.
- **Gap**: NW-67 and NW-68 introduce a third and fourth outcome (guest
  provisioning failed; mail failed) that one boolean cannot express, and a
  pre-send state ("this person has no account on the tenant yet") that the
  Admin currently learns from Microsoft's own error inside the popup.
- **Seats**: ux-ui-designer (the state vocabulary and copy, extending the
  ADR-020 w14 footer's two strings to the real outcome set without inventing
  a token or a component — ADR-019 w14 clause 2); client-architect (the pane
  renders server facts only; no client inference of a delivery state)
- **ADR touchpoints**: amend **ADR-020** (screen 10's invite-result states),
  **ADR-019** only if a status treatment is needed that the semantic map does
  not already carry
- **Design refs**: `screens-v2.md:147-154`; ADR-020 w14 footer §10.3
- **Acceptance**: **A15-6**, **A15-7** (the named error is on the pane, not
  only in the audit trail)
- **Proposed epic**: epic-17-invitation-delivery-and-identity
- **Task sketch**: one web task, co-located with NW-68's pane change.

### NW-58r — Invitation e2e (N3b) runs because the flow creates the second account

- **Source**: `inputs/next/w15-todo.md` §2 "NW-58 — residual". NW-58's
  token / accept / remove lifecycle is **CLOSED-ON-MAIN** (w14); its two
  deferred `must` clauses are NW-67 and NW-68. This id covers only the
  remaining test clause.
- **Raw**: "`web/e2e/invite.spec.ts` (N3b) un-skips once the flow creates the
  second account itself; the council decides how the e2e reads the one-time
  passcode (a test-only seam or a mail-catcher on `dev`)."
- **Today (evidence)**: on **`origin/main`** the spec exists and skips itself
  with a named reason — `git show origin/main:web/e2e/invite.spec.ts` →
  `:138-139` `test.skip(() => !SIGN_IN_READY, …)` and
  `test.skip(() => !SECOND_ACCOUNT_READY, SECOND_ACCOUNT_REASON)`, where
  `:61-63` reads "requires a second Entra account on the pilot tenant — set
  `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `RAFFA_E2E_SECOND_ENTRA_PASSWORD` (an
  operator prerequisite recorded in `reports/audit/w14-hitl.md`…)"; the four
  tests are `:164`, `:187`, `:218`, `:235`. **On this checkout the file does
  not exist** (W15-01). `../web/e2e/` holds only `day1.spec.ts` and
  `v2.spec.ts`, and day 1's invite step never opens an accept link
  (`../web/e2e/day1.spec.ts:145-159`). **No workflow runs Playwright at all**
  — `../.github/workflows/web.yml` runs `npm test` (vitest) only; the ADR-016
  w14 footer already states it: "a task that writes only a spec file has not
  delivered the check", and "do not wire Playwright into CI in w14 … NW-50 is
  queued W18".
- **Gap**: with NW-67 the second account is created by the product, so the
  `SECOND_ACCOUNT_READY` guard can fall — but the invitee's first sign-in is
  an **email one-time passcode**, which no automated run can read today, and
  NW-50 (Playwright in CI) is still queued W18.
- **Seats**: delivery-manager (where this check actually runs, given no
  Playwright runner exists and adding one is explicitly NW-50's job);
  security-architect (a test-only passcode seam on `dev` is an authentication
  bypass surface — it must be scoped, or rejected in favour of a mail-catcher)
- **ADR touchpoints**: amend **ADR-025 §H** (T14/T15 activation conditions)
  and **ADR-016** (what proves a browser-expressed test) — or `none` if the
  council rules the check stays a manual walk this wave
- **Design refs**: none
- **Acceptance**: **N3b** — runnable, the flow creating the second account
  itself
- **Proposed epic**: epic-17-invitation-delivery-and-identity
- **Task sketch**: at most one task; may legitimately reduce to "the
  acceptance runbook walks N3b by hand and the spec's skip reason is
  rewritten" if the council rejects both passcode seams.

### NW-05 — API JWT (ADR-010) replaces spoofable headers

- **Source**: NW-05, `inputs/next/w15-todo.md` §3 (carry-over, queued W15 by
  the w14 intake; `BACKLOG.md:175`)
- **Raw**: "API JWT (ADR-010) replaces spoofable headers … pairs with NW-67:
  the token subject becomes the identity the accept and every tenant-scoped
  route trust; sequence it so NW-67's guest redemption yields a token the API
  validates."
- **Today (evidence)**: **no authentication of any kind is wired.**
  `../backend/src/Raffa.Api/Program.cs:19-356` contains no
  `AddAuthentication`, `AddJwtBearer`, `UseAuthentication`,
  `UseAuthorization` or `RequireAuthorization`; no
  `Microsoft.AspNetCore.Authentication.JwtBearer` / `Microsoft.Identity.Web`
  package reference exists in any `../backend/src/**/*.csproj`; there is no
  `AzureAd` / `Authentication` / `Jwt` section in
  `../backend/src/Raffa.Api/appsettings.json` or
  `appsettings.Development.json`, and no authority/audience env var in
  `../infra/modules/containerapps/main.tf:66-163`. Identity is two raw
  headers: `X-Tenant-Id`, parsed as a GUID by a **copy-pasted** block in about
  a dozen endpoint files (canonical form
  `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:556-567`; the same
  block in `ContractsEndpointExtensions.cs`, `RenewalsEndpointExtensions.cs`,
  `QuotesEndpointExtensions.cs`, `SavingsEndpointExtensions.cs`,
  `PortfolioEndpointExtensions.cs`, `InsightsEndpointExtensions.cs`,
  `NegotiationsEndpointExtensions.cs`,
  `ConversationsEndpointExtensions.cs:355-369`, …), and `X-User-Id` through
  the one seam `../backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs:58-73`.
  **A forged header is full impersonation**: the string is matched straight
  against `workspace_user.email` / `external_subject_id`
  (`../backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:122-132`),
  so `-H "X-User-Id: <a known Admin's address>"` yields that Admin's real
  role. `WorkspacePrincipalAuthorization` is built on the opposite premise and
  is dead: `../backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs:56-74`
  requires an authenticated principal, a custom `tenant_id` claim (`:35`) and
  `ClaimTypes.Role` — the shipped contract ADR-010's w14 footer `:109-112`
  names as the seam to amend.
  **The Entra side is already provisioned and unconsumed**:
  `../infra/modules/identity/main.tf:57-105` declares the API application,
  `identifier_uris = ["api://raffa-${var.environment}-api"]` (`:70`),
  `requested_access_token_version = 2` (`:75`) with the comment "v2 access
  tokens carry `aud` = this application's client_id, matching ADR-010's 'each
  environment's API validates iss + aud'", and the `Raffa.Read` / `Raffa.Write`
  scopes (`:79`, `:90`); `../infra/modules/identity/outputs.tf:7-17` exposes
  `api_identifier_uri` "as audience" — **not referenced by `containerapps`**.
  **The SPA already asks for the API scopes and then throws the token away**:
  `../web/src/auth/msalConfig.ts:80-84` `buildLoginRequest` requests
  `appConfig.oidcApiScopes`, but there is **no `acquireTokenSilent` /
  `acquireTokenPopup` / `acquireTokenRedirect` anywhere in `../web/src`** and
  **no `Authorization: Bearer` header is ever set** — the only identity header
  is `X-User-Id` (`../web/src/api/client.ts:55-58`), whose value is the MSAL
  account username (`../web/src/main.tsx:70-73`). The scopes are built in CI
  at `../.github/workflows/web.yml:201-205`, which the ADR-010 w14 footer
  `:129-133` already flags as a rename risk.
- **Gap**: the entire authentication half of ADR-010 — API validation, SPA
  token acquisition, and the ~12 copy-pasted tenant reads collapsing onto one
  seam that derives tenant from the token subject's membership, never from a
  claim (ADR-010 w14 footer clause 1).
- **Seats**: security-architect (owner: validation parameters, `iss`/`aud`,
  the rule that tenant and role are never claims, the stale-authorization
  window, and what a B2B guest's token actually carries); software-architect
  (the single resolver seam replacing a dozen header blocks, and the migration
  path for every endpoint); client-architect (`acquireTokenSilent` + the
  `Authorization` header on 39 call sites, and interactive fallback);
  cloud-architect (the authority/audience env vars on both container apps —
  a Terraform change, per hazard 2); delivery-manager (the hardcoded scopes in
  `web.yml`, the apply ordering, and the fact that this is the change that
  can lock everyone out of `dev`)
- **ADR touchpoints**: amend **ADR-010** (the w14 footer already binds this
  item; the amendment records what shipped), amend **ADR-022** (the interim
  posture retires — `X-User-Id` and `X-Tenant-Id` stop being inputs), amend
  **ADR-025** (T14 activates), possibly **ADR-015** (no new CI identity
  expected)
- **Design refs**: none
- **Acceptance**: **A15-8** — a request with a forged `X-User-Id` /
  `X-Tenant-Id` and no valid token returns **401**; the token subject is the
  only identity
- **Proposed epic**: epic-18-api-authentication (extends epic-01 F05,
  epic-14 F02)
- **Task sketch**: (a) JWT bearer + per-env authority/audience config
  (Terraform); (b) one identity seam, every endpoint's header block deleted;
  (c) SPA acquires and attaches the token.

### NW-06 — Workspace role from membership / claims, never a client assertion

- **Source**: NW-06, `inputs/next/w15-todo.md` §3 (carry-over; the raw file
  records it `OPEN`)
- **Raw**: "Workspace role from membership / claims, not `?role=`"
- **Today (evidence)**: **the substantive half already shipped in w14 — the
  raw file's `OPEN` is stale.**
  `../backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:55-71`
  has exactly two sources: claims on an authenticated principal (`:60-65`,
  dead until NW-05) and the membership read (`:70`, `:104-137`, a
  tenant-scoped join matching `user.Email == identity ||
  user.ExternalSubjectId == identity`, highest role wins). `:67-69` is the
  deletion, in the code: "the header branch that used to sit here between
  claims and membership is deleted, not narrowed — a client-declared role is
  never an authorization source", delivered by commit `583c3f1`
  (`E14/F02/US02/T01`). **`?role=` is gone from both trees**: no
  `Query["role"]` anywhere in `../backend/src`; in the web only two comments
  describing the removal (`../web/src/components/shell/workspaceRole.ts:9`,
  `:15`) with the live code parsing the **server's** value and degrading
  anything not `Admin` to least privilege (`:37-39`).
- **Gap**: two residues, both small — the dead header constants
  (`WorkspaceRoleResolver.cs:48-49`) and the unreferenced parser
  `TryResolveHeaderRole` (`:89-102`, zero call sites) must go, and the claims
  branch must be proven live once NW-05 lands (today it can never execute).
- **Seats**: security-architect (the claims branch is an authorization path
  that has never run — it must be reviewed, not merely enabled);
  client-architect (the SPA's least-privilege parse stays mandatory, per the
  ADR-026 w14 footer: `role` is a string on the wire, never an enum)
- **ADR touchpoints**: amend **ADR-022** (retire the interim mechanism whose
  last reader disappears), **ADR-010** `none` beyond NW-05's amendment
- **Design refs**: none
- **Acceptance**: folded into **A15-8** — with a valid token, the role a
  request gets is the one its membership row says, and no header or query
  parameter changes it.
- **Proposed epic**: epic-18-api-authentication
- **Task sketch**: one small task riding NW-05's seam; delete the dead
  constants and parser; prove the claims branch.

### NW-07 — Conversation `user_id` is the token subject

- **Source**: NW-07, `inputs/next/w15-todo.md` §3 (carry-over; PARTIAL)
- **Raw**: "Conversation `user_id` is the token subject"
- **Today (evidence)**:
  `../backend/src/Raffa.Chat/Domain/Conversations/Conversation.cs:26` holds
  `UserId`, documented `:13-18` as "the `X-User-Id` header (MSAL account
  username), non-authoritative until the task that lands the API JWT
  (ADR-010) replaces it with the token subject". Set on create at
  `../backend/src/Raffa.Chat/Application/Conversations/ConversationService.cs:72`,
  `:76-78`, `:87`, from
  `../backend/src/Raffa.Api/ConversationsEndpointExtensions.cs:372-399`, which
  prefers `NameIdentifier` / `sub` **only if** the principal is authenticated
  (`:377-387`, never today) and otherwise takes the raw header **untrimmed and
  un-normalized** (`:389-393`), else 400 (`:396-398`). Reads filter on that
  same string (`ConversationService.cs:125`, `:151`, `:201`), so a forged
  `X-User-Id` reads another person's threads.
  **An independent defect surfaced by this audit**: `HeaderCallerIdentity`
  lower-cases the identity (`CallerIdentity.cs:71`) while `TryResolveUserId`
  does **not** — the same browser can key its conversations under a different
  string than its workspace membership, so a case difference in the Entra UPN
  silently splits one user into two.
- **Gap**: the claims branch exists but cannot run; and the normalization
  mismatch is a live bug independent of NW-05.
- **Seats**: software-architect (the conversation keying, and what happens to
  rows written under the old key — a data question, not only a code one);
  security-architect (per-user isolation is the property at stake)
- **ADR touchpoints**: amend **ADR-024** / close **OQ-askv2-005**, which
  already names this exact swap
- **Design refs**: none
- **Acceptance**: folded into **A15-8**
- **Proposed epic**: epic-18-api-authentication
- **Task sketch**: normalize on one seam; decide the fate of existing rows.

### NW-08 — `GET /api/audit` works for a real Admin

- **Source**: NW-08, `inputs/next/w15-todo.md` §3 (carry-over)
- **Raw**: "`GET /api/audit` works for a real Admin"
- **Today (evidence)**: the endpoint exists
  (`../backend/src/Raffa.Api/AuditEndpointExtensions.cs:18`, mapped
  `Program.cs:260`) and its guard is purely claims-based — `:22-36` binds a
  `ClaimsPrincipal` and calls
  `WorkspacePrincipalAuthorization.TryAuthorize(user, Admin, …)`, which
  requires an authenticated principal, a custom `tenant_id` claim and
  `ClaimTypes.Role` (`WorkspacePrincipalAuthorization.cs:56-74`). With no auth
  middleware (NW-05) `HttpContext.User` is always anonymous, so **every call
  returns 401** and no workspace Admin can read the audit trail.
  `../backend/README.md:243` says as much. It is also **absent from the
  OpenAPI document** (`../web/openapi/raffa-api.v1.json` has 34 paths and no
  `/api/audit`), so no generated client can reach it.
- **Gap**: NW-05 unblocks authentication, but the authorization source must
  still move from claims to **membership** — ADR-010's w14 footer is explicit
  that a `tenant_id` or `roles` claim is never the authorization source — and
  the route must join the published contract.
- **Seats**: security-architect (who may read a tenant's audit trail, and the
  scoping of the read); software-architect (the OpenAPI entry and the
  membership-based guard replacing the claims one)
- **ADR touchpoints**: amend **ADR-010** (the seam its footer names), amend
  **ADR-026** (a route joins the contract), ADR-011 (the audit read surface)
- **Design refs**: none
- **Acceptance**: folded into **A15-8**
- **Proposed epic**: epic-18-api-authentication
- **Task sketch**: swap the guard to membership; add the OpenAPI path.

### NW-31 — Retire the dual role headers

- **Source**: NW-31, `inputs/next/w15-todo.md` §3 (carry-over; the raw file
  already narrows it: "w14 already deleted the header branch from
  `WorkspaceRoleResolver`; what remains is `GET /api/capabilities` and the
  OpenAPI wording")
- **Raw**: "Dual role headers (`X-Role` vs `X-Workspace-Role`) until NW-05"
- **Today (evidence)**: one live reader remains —
  `../backend/src/Raffa.Api/CapabilitiesEndpointExtensions.cs:54`
  (`RoleHeaderName = "X-Role"`), `:77-80` (`CallerIsAdmin`), used at `:64-67`
  to include or hide `CapabilityRoleGate.Admin` rows, so `-H "X-Role: Admin"`
  still flips catalog visibility (UI shaping only — not a data gate).
  Published in the contract: `../web/openapi/raffa-api.v1.json:5131` declares
  `X-Role` as an optional header **parameter** (the only such parameter in the
  document) and `:5128` describes it; `:1142` still tells readers that
  `DELETE /api/documents/{id}` "carries the role as `X-Workspace-Role`", which
  is **stale** — that endpoint's gate is membership-based
  (`DocumentsEndpointExtensions.cs:508`). Dead residue in
  `WorkspaceRoleResolver.cs:48-49`, `:89-102`. Docs:
  `../docs/ask-v2-acceptance.md:71`, `:357`, `:600`. CI still sends **both**
  headers: `../.github/workflows/reprocess-tenant-documents.yml:205-206`,
  `:256-257` (and `:42-43`, `:274`) — harmless since w14, because that job now
  depends on its `X-User-Id` being a real Admin member.
  No occurrence of either header in `../web/src`.
- **Gap**: a published, spoofable header parameter and a stale contract
  description; both end with NW-05.
- **Seats**: **none** — ADR-022's w14 footer already demoted these headers to
  UI shaping and gave each interim mechanism a named retirement item. This is
  the retirement, not a new decision.
- **ADR touchpoints**: none — ADR-022's w14 footer governs
- **Design refs**: none
- **Acceptance**: folded into **A15-8** — no role header changes any response
- **Proposed epic**: epic-18-api-authentication
- **Task sketch**: delete the capabilities reader and the dead resolver code;
  remove the OpenAPI parameter and fix `:1142`; drop both headers from the
  reprocess workflow and the acceptance docs.

### NW-32 — One answer for an absent caller identity

- **Source**: NW-32, `inputs/next/w15-todo.md` §3 (carry-over)
- **Raw**: "Unattributed actor on writes when `X-User-Id` is absent"
- **Today (evidence)**: `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:71`
  defines `UnattributedActor = "unattributed"`; `:569-573` `ResolveActor`
  returns it whenever the header is missing or blank — **it never rejects** —
  and it is used on four write paths: `:128` (validate), `:230` (upload),
  `:460` (reprocess), `:514` (delete). Nine service-layer sites hardcode the
  same constant and never take an actor from the request at all:
  `DocumentUploadService.cs:48` (`:106` `CreatedBy`, `:136` audit),
  `ContractCorrectionService.cs:118` (`:306`, `:332`, `:346`, `:360`),
  `../backend/src/Raffa.Renewals/Application/RenewalActionService.cs:57`
  (`:138`),
  `../backend/src/Raffa.Savings/Application/SavingsOpportunityService.cs:95`
  (`:165`, `:316`),
  `../backend/src/Raffa.Quotes/Application/QuoteUploadService.cs:38` (`:135`),
  `…/Outcome/NegotiationOutcomeService.cs:88` (`:190`),
  `…/Normalization/SkuMappingService.cs:90` (`:205`),
  `../backend/src/Raffa.Chat/Application/RagAnswerService.cs:67` (`:140`),
  `../backend/src/Raffa.Api/AskCopilotService.cs:103` (`:989`).
  **The real defect is the inconsistency**: the same absent header produces
  **three different outcomes** across the API — `401` on every
  `ICallerIdentity` consumer (`WorkspaceEndpointExtensions.cs:68-71`,
  `:123-126`; `WorkspaceMembersEndpointExtensions.cs:66-69`, `:169-172`;
  `WorkspaceInvitesEndpointExtensions.cs:81-84`, `:173-176`;
  `InvitationsEndpointExtensions.cs:73-76`), `400` on conversations
  (`ConversationsEndpointExtensions.cs:396-398`), and a silent
  `"unattributed"` audit row on the nine paths above.
- **Gap**: with NW-05 an unauthenticated request is a 401 and the constant
  should become unreachable — but the nine service-layer defaults would
  survive as the value written to `CreatedBy` / `CorrectedBy` and to audit
  rows, so the item is a deliberate deletion, not a side effect.
- **Seats**: security-architect (an audit row that cannot name its actor is a
  gap in ADR-011's audit posture); software-architect (threading the resolved
  identity into nine services that today take none)
- **ADR touchpoints**: amend **ADR-011** (every write names its actor),
  ADR-022 (the last interim fallback retires)
- **Design refs**: none
- **Acceptance**: folded into **A15-8**
- **Proposed epic**: epic-18-api-authentication
- **Task sketch**: one seam supplies the actor; the constant is deleted; the
  three divergent absent-identity behaviours collapse to 401.

### NW-70 — Create-workspace form submits the ISO country code

- **Source**: NW-70, `inputs/next/w15-todo.md` §4 ("Recorded so the intake
  does not re-open it")
- **Today (evidence)**: **CLOSED-ON-MAIN.** `9d3da0a` "fix(web): the
  create-workspace form submits the ISO country code, not the label" is on
  this checkout (HEAD~2) and reached `origin/main` as PR #98 (`08d4785`).
- **Seats**: none · **ADR touchpoints**: none · **Acceptance**: none — out.

## 3. Seat roster for this wave

| Seat | Involved | Items | Why (one line) |
|---|---|---|---|
| product-owner | yes | NW-27, NW-61, NW-67, NW-68 | Two product rules are genuinely open — where the admission gate lives once upload is async (and what a refused file *is* once it must be a row), and "never an empty contract or a fabricated fact" — plus two scope statements: a guest per invited address in the company directory, and email, which `percorso-pilota-v1.md` excludes from the pilot and the stakeholder now requires (OQ-w15-002). |
| software-architect | yes | NW-27, NW-61, NW-67, NW-68, NW-05, NW-07, NW-08 | Owns the queue contract and the producer/consumer seam, the completeness signal the aggregates must return, where the Graph call sits and its no-op/failure contract, the real `IInvitationMailer`, and the single identity seam that must replace ~12 copy-pasted header blocks. |
| cloud-architect | yes | NW-27, NW-67, NW-68, NW-05 | Every one of this wave's three Terraform applies is this seat's: Service Bus → container-app wiring plus worker scaling and its cost, the new `communication` module in both environments, the Graph application permission, and the authority/audience env vars the API cannot otherwise receive. |
| security-architect | yes | NW-67, NW-68, NW-05, NW-06, NW-07, NW-08, NW-32, NW-58r | The wave's centre of gravity: an invitation becomes a **directory write**, a forged header is full impersonation today, a passcode test-seam would be an authentication bypass, and four queue items (role, conversation keying, audit read, unattributed actor) are all authorization properties. |
| client-architect | yes | NW-61, NW-69, NW-67, NW-05, NW-06 | The read-back and poll contract, the pane rendering only server facts, accept completing without a second click, and the SPA half of the token — it requests API scopes today and discards them. |
| ux-ui-designer | yes | NW-27, NW-61, NW-69, NW-68 | A server-side `Rejected` row contradicts the design oracle's *session-only, never counted* "Not added" card; three screens need a "still processing" state they do not have; the invite pane grows from two outcomes to four. |
| delivery-manager | yes | W15-01, NW-27, NW-67, NW-68, NW-05, NW-58r | The wave base is a merge, not a branch; three HCP applies must land before their features through the ADR-016 gate; this is the first wave to add an API environment variable; and no Playwright runner exists for N3b. |
| council-gate | yes | all | Verifies votes and files, closes the table. |

**Not involved on any item**: no seat. Two items carry **`seats: none`** and go
straight to a task — **NW-10** (ADR-012's w14 footer already decided it) and
**NW-31** (ADR-022's w14 footer already decided it); **NW-70** is out.

## 4. Proposed epics (append-only, next free numbers)

`Glob reports/workitems/epic-*` → `epic-01` … `epic-15` exist
(`epic-14-workspace-identity`, `epic-15-workspace-invitations` from w14).
Next free: **16, 17, 18**.

| Epic | Slug | Theme | Items | Extends |
|---|---|---|---|---|
| epic-16 | async-document-processing | Upload returns when the file is stored; the Worker does the work; every surface tells the truth about a document that is not ready | W15-01, NW-27, NW-61, NW-10 | epic-01 F06 (document ingestion), epic-06 F05 (upload UI + status read-back), epic-13 F04 (documents V2) |
| epic-17 | invitation-delivery-and-identity | An invited colleague receives a mail, has an identity, and lands inside the workspace — with no Azure portal | NW-67, NW-68, NW-69, NW-58r | epic-15 (invitation lifecycle), epic-06 F04 (members & roles) |
| epic-18 | api-authentication | The token subject is the only identity the API trusts | NW-05, NW-06, NW-07, NW-08, NW-31, NW-32 | epic-01 F05 (identity / workspace roles), epic-14 F02 (role from membership) |

## 5. Selection for this wave (cap 20 tasks / 5 phases)

The raw file §0 is binding and this section applies it literally: every wave
the w14 intake queued is executed in order; nothing is pushed to a wave later
than the one it is already queued for; a `must` is never queued beyond the
next wave and never demoted; an item that does not fit becomes the **head** of
the next wave, never its tail.

- **In wave** (priority order, dependencies first):
  1. **W15-01** — wave base is `origin/main` @ `3c89d35` (operator act at
     HITL, no fan-out task; blocks everything, and two cited oracles do not
     exist until it lands).
  2. **NW-27** — upload returns once the file is stored (`must`, outcome A;
     the largest item in the wave and the one the cap will press on).
  3. **NW-61** — the server row, the progression, and the completeness gate
     (`must`, outcome A; strictly after NW-27's server rows exist).
  4. **NW-10** — rail badge from the server (`should`; **pulled forward from
     W16** on the raw file's own invitation, "if the same task touches it" —
     it rides NW-61's work; if the decomposer does **not** co-locate it, it
     returns to the **head of W16**, never later).
  5. **NW-67** — the Entra B2B guest (`must`, outcome B).
  6. **NW-68** — the invitation email (`must`, outcome B).
  7. **NW-69** — the honest invite pane (`should` but named in outcome B, so
     §0.3's "never these" protects it from overflow).
  8. **NW-58r** — N3b runs (`should`, outcome B residual; may legitimately
     reduce to a runbook step — see its row).
  9. **NW-05** — API JWT (`must`, carry-over; sequenced with NW-67 so a
     guest's redemption yields a token the API validates).
  10. **NW-06** — role from membership / claims (`must`, carry-over; small,
      rides NW-05's seam).
- **Queued** (decomposed, **no task in `w15`**) — these become the **head** of
  W16, in this order: **NW-07, NW-08, NW-31, NW-32**. All four are `should` in
  the raw file, which is the only category §0.3 allows to overflow. Each is
  recorded in §2 with full evidence so the W16 intake picks them up without
  re-auditing.
- **Out**: CLOSED-ON-MAIN: **NW-70** (PR #98 / `9d3da0a`), **NW-51** (w14),
  and NW-58's token / accept / remove lifecycle (w14 — only NW-58r remains).
  DEFERRED: **NW-52** (also out of scope — ADR-001 §1.2 / INDEX: "R3/R4 on
  fixture adapter, **never a paid API for first `demo`**"), **NW-53**
  (ADR-013: mobile is a non-gating lane, no store release for R0–R4),
  **NW-54** (deferred by the raw file; Legal / Finance / Read-only stay in the
  model, not in the nav).
- **Everything in "In wave" is closable in this product**: no paid market API
  (NW-52 stays out), nothing beyond the mobile scaffold, nothing in ADR-001
  §1.2 non-goals. ACS Email and Microsoft Graph are first-party Azure/Entra
  surfaces on the existing tenant and subscription, not a third-party paid
  API; ACS Email is metered per message with no idle charge (ADR-005 w14
  footer) and is compatible with the locked "cheapest SKUs, nothing
  idle-expensive" rule.

### Full remaining schedule (restated so nothing is dropped between runs)

Every wave below is executed, in order, and is the next run after the one
before it. W16/W17/W18 are `w14-requirements.md` §5 verbatim, minus what W15
takes and plus this run's overflow.

| Wave | Queue (head first) | Change vs `w14-requirements.md` §5 |
|---|---|---|
| **W16** | **NW-07, NW-08, NW-31, NW-32** (W15 overflow, head), then NW-10 *(only if W15 did not co-locate it)*, NW-11, NW-12, NW-13, NW-21 | + the four W15 `should` overflows at the head; NW-10 leaves if W15 lands it |
| **W17** | NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66 | unchanged |
| **W18** | NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 | **NW-27 and NW-61 removed** — promoted into W15 (§0.2). NW-50 (Playwright in CI) stays here and constrains NW-58r |

- **Order constraints**:
  - **W15-01 before every task** — otherwise `docs/waves/w14-acceptance.md`
    and `web/e2e/invite.spec.ts` do not resolve and the acceptance runbook has
    no base.
  - **Infrastructure before the feature that needs it, in its own phase, with
    an operator-confirmed HCP apply** (ADR-016 w14 footer; `backend.yml`
    deploys `--image` only). Three applies: (a) Service Bus → container apps +
    worker scaling (NW-27); (b) `infra/modules/communication` + `acs-connection`
    + `Invitations__Mail__*` + `Invitations__AcceptUrlBase` (NW-68); (c) the
    Graph application permission + one-time admin consent (NW-67). They are
    independent of each other and **should be one phase**, not three.
  - **NW-27 before NW-61** — the web cannot render a server progression until
    the server row exists within 2 s.
  - **NW-67 with or after NW-05's token validation** — the raw file §3 states
    it: a guest's redemption must yield a token the API validates. If NW-05
    slips inside the wave, NW-67 must still not ship an accept path built on
    `X-User-Id` that NW-05 then rewrites.
  - **NW-05 before NW-06** — the claims branch cannot be proven until a
    principal exists.
  - **NW-67 before NW-68's acceptance** — a mail whose link leads to a
    sign-in the invitee cannot complete is A15-4's failure, not a pass.
  - **NW-68 before NW-69's `true` path** — the pane cannot render a delivery
    state no transport can produce.
- **Budget note**: ten items against a 20-task / 5-phase cap, and the two
  largest (NW-27, NW-05) each touch backend + infra + web. This is the
  tightest budget the process has cut. **If the cap binds, cut by narrowing
  scope inside an item, never by dropping one** — §0.1 and §0.3 leave no item
  available to drop, and the four `should` overflows are already out. The two
  narrowings the council should pre-authorise, in this order: (1) **NW-58r**
  reduces to a runbook step plus a rewritten skip reason (no passcode seam,
  no Playwright runner — NW-50 owns that and is W18); (2) **NW-05's** single
  identity seam lands with **every** endpoint migrated in one task rather
  than one task per endpoint family — the ~12 header blocks are
  copy-pasted, so they are one edit, not twelve. Never narrow NW-27's
  durability (A15-2 is the point of the item) or NW-67's failure contract
  (A15-7 exists because a "ready" link that cannot be used is the defect).

## 6. Superseded work items

| Existing item | Superseded by | Why |
|---|---|---|
| none | — | The raw file contains no "cancels / replaces" statement about a **work item**. Its §4 NW-70 note is a do-not-reopen record, not a cancel. **No status banner is written this wave.** Two existing items are *amended, not superseded*, and the decomposer must not banner them: `epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2` (its synchronous-upload premise is amended by NW-27 — the story's ACs are still exactly what the product wants) and `epic-15-workspace-invitations/feature-01-invitation-lifecycle/us-01-invite-accept-remove` (NW-67 / NW-68 land its two *deferred* clauses; the delivered lifecycle stands). What **is** superseded is an oracle assumption, not a work item: `inputs/requirements.md` A7 (`:827`) and its open question `OQ-askv2-007` — see OQ-w15-003. |

## 7. Open questions and assumptions in force

- **OQ-w15-001** — Which commit is the wave base? This checkout (`3720543`,
  `helix/w15`) is **five commits behind `origin/main` (`3c89d35`)**, and two
  oracles the raw file cites — `docs/waves/w14-acceptance.md` and
  `web/e2e/invite.spec.ts` — exist only on `origin/main`. **Status**: `open` —
  operator act at HITL. **Assumption in force**: the operator merges
  `origin/main` into the wave base **before** fan-out; every path in this
  document then resolves unchanged, because
  `git diff --stat origin/main..HEAD -- backend web infra .github docs scripts`
  is exactly five files (`backend/README.md`, `docs/waves/w14-acceptance.md`,
  `web/README.md`, `web/e2e/day1.spec.ts`, `web/e2e/invite.spec.ts`) and
  **none** of them is under `backend/src`, `web/src`, `infra/` or
  `.github/workflows/` — so every code citation in §2 is already true on
  `origin/main`. Ref: W15-01, ADR-014 w14 footer.
- **OQ-w15-002** — Is a real email transport in scope, given the pilot path
  excludes it? `inputs/percorso-pilota-v1.md:36` lists **email** under *"Non è
  onboarding"*, `:110` says "Niente email nel pilota" and `:152` repeats it,
  while `inputs/product-spec.md:619`/`:626`/`:756` rank email **P1 / V1** and
  the ADR-001 w14 footer already ruled the transport "**deferred, not a §1.2
  non-goal**". The stakeholder ruling of 2026-09-13 marks NW-68 `must`
  ("dovevi già farlo ora"). **Status**: `open` — product-owner at the table.
  **Assumption in force**: the stakeholder ruling governs; the pilot doc's
  exclusion is read as "email is not a *pilot demo step*", not "email is out
  of the product", so NW-68 ships and the pilot script is unchanged — nobody
  is asked to demo an inbox. Ref: NW-68, ADR-001, ADR-005, OQ-w14-002.
- **OQ-w15-003** — `inputs/requirements.md` A7 (`:827`, "upload stays
  synchronous in the request for V2 … moving it to the worker queue is a later
  task") and its open question **OQ-askv2-007** are superseded by NW-27, which
  the raw file states outright. **Status**: `open` — recorded here so the
  decomposer does not build to A7. **Assumption in force**: OQ-askv2-007 is
  **`assumed-wrong` from this wave on**; the asynchronous worker path is the
  product rule, and R-DOC-01 / R-DOC-03 / R-DOC-09 are read with the
  admission gate's placement decided at this table (OQ-w15-004). The
  `../backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs:65-77`
  comment that cites OQ-askv2-007 as authority becomes stale in the same task.
  Ref: NW-27, ADR-024.
- **OQ-w15-004** — Does the admission gate (R-DOC-03) stay synchronous inside
  the 2 s budget, or move to the Worker? The raw file offers both and the
  answer changes the data model: today a refusal is a 422 with **nothing
  persisted** (`DocumentsEndpointExtensions.cs:242-252`), the design oracle
  calls the "Not added" card *session-only, never counted*
  (`screens-v2.md:72-75`), and `DocumentProcessingStatus` has **no `Rejected`
  value** (`DocumentProcessingStatus.cs:4-11`). **Status**: `open` —
  product-owner + software-architect + ux-ui-designer. **Assumption in
  force**: the gate **moves to the Worker** and a refused file becomes a
  terminal `Rejected` row, because the gate parses/OCRs and calls Foundry
  `classify` (`DocumentAdmissionGate.cs:110-112`, `:136-138`) and cannot be
  guaranteed inside 2 s against live Foundry — which makes a new
  `processing_status` value, a migration (ADR-021), and an amendment to the
  design oracle's session-only rule part of NW-27's scope. Ref: NW-27, R-DOC-03.
- **OQ-w15-005** — Which identity calls Microsoft Graph, and does Terraform
  manage the permission? A per-environment user-assigned managed identity
  already exists and is already attached to both container apps
  (`../infra/modules/identity/main.tf:36-46`;
  `../infra/modules/containerapps/main.tf:34-37`, `:214-217`), but
  `../infra/modules/identity/main.tf:41-42` records that the current design
  deliberately **avoids** Graph. **Status**: `open` — cloud-architect +
  security-architect. **Assumption in force**: the existing workload managed
  identity is granted the least-privilege Graph application permission
  (`User.Invite.All` or narrower) through Terraform, with **admin consent
  granted once by the tenant admin as an operator step at the ADR-016 gate**,
  and **no new secret** — so ADR-011 gains no Key Vault entry for NW-67.
  Both environments authenticate against the **same Entra tenant**
  (`identity/main.tf:63`, `:118`; `tests/test_check_demo_swa_config.py:32`,
  `:42`), so a guest provisioned from `dev` is visible to `demo` — the council
  must say whether that is acceptable or whether provisioning is gated per
  environment. Ref: NW-67, ADR-010, ADR-011, ADR-015.
- **OQ-w15-006** — How does N3b read the invitee's one-time passcode? No
  workflow runs Playwright at all (`../.github/workflows/web.yml` runs vitest
  only), and wiring one is **NW-50, queued W18**. **Status**: `open` —
  delivery-manager + security-architect. **Assumption in force**: NW-58r does
  **not** introduce a passcode seam in the product this wave; the spec's skip
  reason is rewritten to name the passcode (not the missing second account,
  which NW-67 removes) and N3b is walked by hand in the acceptance runbook. A
  `dev`-only mail-catcher or a test seam is the council's call; if neither is
  approved, this item closes on the runbook step. Ref: NW-58r, ADR-016 w14
  footer, ADR-025 §H.
- **OQ-w15-007** — What happens to the Entra guest when a member is removed?
  The raw file asks the council to record the rule and offers the answer.
  **Status**: `open` — security-architect + product-owner. **Assumption in
  force**: removing a workspace membership **never** deletes or blocks the
  Entra guest — the guest may belong to other workspaces, and w14 already
  rules that removal is immediate because nothing caches authorization
  (ADR-025). The rule is recorded in an ADR-025 amendment; no directory
  deletion path is built. Ref: NW-67, ADR-025.
- **OQ-w15-008** — Does NW-10 ride NW-61, or return to W16? The raw file makes
  it conditional ("take NW-10 into W15 if the same task touches it").
  **Status**: `open` — decided by the decomposer, not by a seat. **Assumption
  in force**: NW-10 is **in wave**, co-located with NW-61's server-row work,
  because the fix is one component reading an endpoint that already exists
  (`RailNav.tsx:67` → `DocumentsEndpointExtensions.cs:82`). If the decomposer
  cannot co-locate it, it becomes the **head of W16**, never later. Ref:
  NW-10, NW-61, ADR-012 w14 footer.
