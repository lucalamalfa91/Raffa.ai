# Raffa — next-waves input · W16

Status: **binding input** for the W16 requirements document. Written 2026-09-14
after the operator confirmed wave **w15 closed**. IDs continue from
`next-waves-todo.md` / `w15-todo.md`; carried-over items keep their id.

| | |
|---|---|
| Compared | `origin/main` @ `ff66ee6` (2026-09-14, merge of PR #117 `fix/nw05-data-plane-gate-v2`) — w15 closed by PR #103 (`integration`) + follow-ups #111–#117; acceptance `docs/waves/w15-acceptance.md` is on `main` |
| Previous wave | w15 "upload feels instant, and inviting a colleague works end to end" — `reports/context/waves/w15-requirements.md`, `reports/architecture/waves/w15.md`, ADR-027, epics 16–18 |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, `inputs/percorso-pilota-v1.md` |
| Architecture | ADR-010 (API JWT — **wired in w15**), ADR-012 (web; no session as source of truth), ADR-022 (interim headers — residual retirement), ADR-024 (Ask V2 / `OQ-askv2-005`), ADR-025 / ADR-026 (membership) |

## 0. Binding instructions (unchanged from w15 §0; stakeholder 2026-09-13)

1. **Every wave the previous intake queued is executed, in order, in full.**
   This run **is W16**. W17 and W18 keep the queues in
   `w15-requirements.md` §5 (restated in §4 below). Nothing is dropped and
   nothing is pushed to a later wave than the one it is already queued for;
   an item that does not fit this wave's cap becomes the **head** of W17,
   never its tail.
2. **An item this file marks `must` or "no longer deferred" is never queued
   beyond the next wave** and is never demoted without a written reason.
3. **Do not skip ahead.** `helix/w17-input` already holds `w17-todo.md`
   (NW-71 auto-accept ≥ 90 %, scheduled with the review-screen group
   NW-63/64/65/66). That file is **out of this run**. Do not ingest it, do
   not assign NW-71 a W16 task, do not change its wave.
4. **Carry-over first.** The four `should` items w15 overflowed are the
   **head** of this wave, already decomposed under epic-18 F02/F03 with
   `status: queued` and **no** line in `slices/w15.yaml`. The W16 intake
   picks them up without re-auditing the *intent*; it **does** re-audit
   *status today* against this checkout (`origin/main` @ `ff66ee6`).
5. **NW-10 leaves this queue if w15 landed it.** w15 co-located it with
   NW-61 (`E16/F03/US01/T01`). On this checkout `documentStore.ts` is
   **deleted**, `useDocumentCounts.ts` exists, and `RailNav.tsx` documents
   the server `counts`. Intake must confirm CLOSED-ON-MAIN with evidence
   and keep it **out** of `slices/w16.yaml` if so. Do not reopen it.

## 1. Head of W16 — identity residuals (w15 overflow, already decomposed)

These four are `should` in the w15 raw file — the only category §0.3 allowed
to overflow. Each has a full task file with real paths, real DoD commands
and evidence. **Promote them to `live` in this wave**; do not rewrite the
task bodies unless the code on `main` has moved the evidence.

| ID | Item | Priority | Decomposed task (queued) |
|---|---|---|---|
| NW-07 | Conversation `user_id` is the token subject | should | `E18/F02/US01/T01` |
| NW-08 | `GET /api/audit` works for a real Admin | should | `E18/F02/US02/T01` |
| NW-31 | Retire the dual role headers | should | `E18/F03/US01/T01` — **owns `reprocess-tenant-documents.yml`** (w15 took it out of service and did not edit it, so one wave opens that file once — ADR-016 w15 clause 21) |
| NW-32 | Every write names its actor; delete `"unattributed"` | should | `E18/F03/US02/T01` |

### NW-07 — Conversation `user_id` is the token subject

- **Status:** PARTIAL (w15-requirements). w15 landed NW-05 (JWT +
  `ICallerIdentity`). Conversations still have a private `TryResolveUserId`
  that prefers `NameIdentifier`/`sub` only if authenticated, otherwise the
  raw header, with a **normalization mismatch** (`CallerIdentity` lower-cases;
  this helper does not) that silently splits one user into two.
- **Must:** key create + all reads on the same `ICallerIdentity` seam;
  forged `X-User-Id` is 401 before this code; decide the fate of rows
  written under the old key and record it.
- **Closes:** `OQ-askv2-005`. Evidence: the queued task file; ADR-010,
  ADR-024, ADR-009.
- **Acceptance:** a conversation created under `User@Example.com` is
  readable by the same subject presenting `user@example.com`, and by nobody
  else.

### NW-08 — `GET /api/audit` works for a real Admin

- **Status:** OPEN. Route exists (`AuditEndpointExtensions`) but the guard
  still wants a custom `tenant_id` claim + `ClaimTypes.Role` (forbidden as
  an authorization source by ADR-010's w14 footer). Path is **absent** from
  `web/openapi/raffa-api.v1.json`, so no generated client can call it.
- **Must:** membership Admin on the route tenant (404 non-member, 403
  member-not-Admin); publish the path + typed client; a forged header
  without a token is 401.
- **Acceptance:** signed-in Admin reads the tenant's audit on `dev`; a
  Procurement member cannot; OpenAPI lists `/api/audit`.

### NW-31 — Dual role headers (`X-Role` / `X-Workspace-Role`) retired

- **Status:** PARTIAL. w14 deleted the header branch from
  `WorkspaceRoleResolver`. Residues: `CapabilitiesEndpointExtensions`
  still reads `X-Role`; OpenAPI still declares that header parameter and a
  stale `X-Workspace-Role` description on document delete; 
  `.github/workflows/reprocess-tenant-documents.yml` still sends both
  headers and has **no working auth path** after NW-05.
- **Must:** delete the capabilities reader and the contract parameter;
  give the reprocess workflow a working authentication (or convert it to
  a worker-side enqueue — NW-27 made that possible) **in this wave**,
  because this is the one wave that is allowed to open that YAML.
- **Acceptance:** no `X-Role` / `X-Workspace-Role` in OpenAPI, capabilities,
  or the workflow; Admin catalog rows come from membership.

### NW-32 — Every write names its actor

- **Status:** OPEN. `"unattributed"` is still a service-layer default on
  documents, corrections, renewals, savings, quotes, SKU mapping, RAG,
  Ask — nine sites that **do not take an actor from the request**, so they
  survived NW-05. Collapse the three absent-identity behaviours (401 on
  `ICallerIdentity` consumers, 400 on conversations, silent
  `"unattributed"` audit rows) to a **single 401**.
- **Must:** delete the constant; thread the resolved actor; a request
  without a validated identity writes nothing.
- **Acceptance:** grep for `unattributed` under `backend/src` is empty
  (or only historical comments the council allows); unsigned writes 401.

## 2. Rest of W16 — no session as source of truth

From `w14-requirements.md` §5 / `w15-requirements.md` §5, after the
overflow head. Persistence rule (unchanged): **if a human can create or
change it, another browser / device / session must read it back from
Postgres (RLS).** Browser `sessionStorage` / `localStorage` is not a
system of record.

### NW-10 — Rail Documents badge reads the server

- **Status:** likely **CLOSED-ON-MAIN** (w15 `E16/F03/US01/T01`). Confirm
  against this checkout; if closed, **out**. Do not invent residual work.

### NW-11 — Renewal actions have no HTTP read-back

- **Status:** OPEN. `POST /api/renewals/{id}/action` persists;
  `RenewalActionService.GetActionAsync` exists but is **unrouted**.
  `web/src/routes/renewals/renewalActionStore.ts` mirrors posted actions in
  `sessionStorage`. Renewals list, Contract 360 and Savings all read that
  store.
- **Must:** GET (list and/or by id) so a second browser sees the action;
  delete the session store as the source of truth (a short-lived UI
  optimistic mirror is allowed only if the next GET replaces it).
- **Acceptance:** post an action → reload / second browser → same action
  on Renewals, Contract 360 and Savings.

### NW-12 — Quote GET + negotiation-outcome list

- **Status:** OPEN. Quotes API maps upload / assessment / recalculate
  only — no `GET /api/quotes`, no `GET /api/quotes/{id}`.
  `quoteOutcomeStore.ts` keeps outcomes in `sessionStorage`. Product shape
  of a history-inside-Quote-check / Ask-citable list is **NW-57 (W18)** —
  this item is the **read-back**, not that UX.
- **Must:** GET the quote and the recorded outcomes for the tenant so a
  second browser sees them; stop using session as the record.
- **Acceptance:** upload a quote, record an outcome, reload / second
  browser → both visible.

### NW-13 — Contract 360 negotiation-step ticks are server state

- **Status:** OPEN. `negotiationStepsStore.ts` stores four booleans per
  contract under `sessionStorage["raffa.contract360.steps.<id>"]`; no
  endpoint records them.
- **Must:** persist the ticks with the contract (or a named child
  resource); another session reads the same four booleans.
- **Acceptance:** tick a step → reload / second browser → still ticked.

### NW-21 — Quote outcome updates Savings

- **Status:** PARTIAL. `NegotiationOutcomePropagationService.PropagateAsync`
  runs **only** when the body carries `savingsOpportunityId`; the web
  never sends one (`quoteOutcomeStore.ts`), so a recorded outcome never
  moves the Savings KPI.
- **Must:** recording a negotiation outcome that names (or can be resolved
  to) a savings opportunity updates that opportunity / the Savings KPI
  on the next read, without a client-side store.
- **Acceptance:** record an outcome against a known opportunity → Savings
  KPI / row changes; a second browser agrees.

## 3. Out of this wave

| ID | Why |
|---|---|
| NW-05, NW-06, NW-27, NW-61, NW-67, NW-68, NW-69, NW-58r | **w15** — operator confirmed the wave terminated; intake may record CLOSED-ON-MAIN / PARTIAL with evidence, but must **not** reopen them as live W16 tasks |
| NW-10 | **out if closed on main** (see §0.5) |
| NW-20, NW-22, NW-23, NW-25, NW-26, NW-62–NW-66 | **W17** — Contract 360 / viewer / domain completeness |
| NW-71 | **W17** — auto-accept ≥ 90 %; parked on `helix/w17-input`; do not ingest |
| NW-30, NW-40, NW-41, NW-50, NW-55–NW-57, NW-59, NW-60 | **W18** |
| NW-52, NW-53, NW-54 | **DEFERRED** (paid market API / mobile beyond scaffold / extra roles in nav) |

## 4. Full remaining schedule (restated so nothing is dropped)

| Wave | Queue (head first) |
|---|---|
| **W16 (this run)** | NW-07, NW-08, NW-31, NW-32, then NW-11, NW-12, NW-13, NW-21 (NW-10 only if still open) |
| **W17** | NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66, **NW-71** |
| **W18** | NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 |

## 5. Suggested grouping and seats

| Theme | Items | Seats |
|---|---|---|
| A · identity residuals (overflow head) | NW-07, NW-08, NW-31, NW-32 | security-architect, software-architect, delivery-manager (NW-31 owns a workflow) |
| B · no session as source of truth | NW-11, NW-12, NW-13, NW-21 | software-architect, client-architect, product-owner; ux-ui-designer only if a surface copy/state changes |

Order: **A before B** (A is the overflow head). Inside A, NW-07/NW-32 share
the conversations 400→401 seam — council decides one writer. NW-31 owns
the one CI-YAML file. Cap 20 tasks / 5 phases; overflow → **head of W17**.

## 6. Acceptance seeds (on deployed `dev`)

| # | Check | Expected |
|---|---|---|
| A16-1 (NW-07) | create a chat, sign out, sign in as the same user with different UPN casing | history is there; a second user in the same workspace does not see it |
| A16-2 (NW-08) | Admin opens audit (or calls `GET /api/audit`) | events for this tenant; Procurement gets 403; no token → 401 |
| A16-3 (NW-31) | capabilities + reprocess-tenant-documents | no role headers on the wire; Admin rows follow membership; the workflow authenticates |
| A16-4 (NW-32) | unsigned POST that used to write `"unattributed"` | 401; nothing persisted |
| A16-5 (NW-11) | post a renewal action | second browser / reload shows it |
| A16-6 (NW-12) | upload quote + record outcome | second browser sees both |
| A16-7 (NW-13) | tick a Contract 360 negotiation step | second browser still ticked |
| A16-8 (NW-21) | record an outcome tied to a savings opportunity | Savings KPI / row moves; second browser agrees |

## 7. Traceability

Overflow head ← `w15-requirements.md` §5 + `BACKLOG.md` "Queued for the next
wave" + epic-18 F02/F03 task files. Session items ← `w14-requirements.md` §5
"W16 — No session as source of truth". NW-10 closed-on-main test ←
`E16/F03/US01/T01` / `useDocumentCounts.ts` / deleted `documentStore.ts`.
NW-71 exclusion ← `helix/w17-input` `w17-todo.md`. Operator: w15 terminated,
2026-09-14.
