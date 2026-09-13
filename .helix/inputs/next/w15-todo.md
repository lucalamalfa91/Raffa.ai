# Raffa — next-waves input · W15

Status: **binding input** for the W15 requirements document. Written 2026-09-13
after the first stakeholder walk of wave w14 on deployed `dev`. IDs continue from
`next-waves-todo.md` (last used NW-66); carried-over items keep their id.

| | |
|---|---|
| Compared | `origin/main` @ `08d4785` (2026-09-13, merge of PR #98) — w14 closed by PRs #92, #95, #96 + follow-ups #93, #97, #98; `backend.yml` run 34770255797 reached "Verify schema applied (ADR-021)" and `deploy (dev)` |
| Previous wave | w14 "workspace is real" — `reports/context/waves/w14-requirements.md`, `reports/architecture/waves/w14.md`, acceptance `docs/waves/w14-acceptance.md` |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, `inputs/percorso-pilota-v1.md` |
| Architecture | ADR-002 (worker / queue), ADR-005 (SKUs; w14 footer "mail transport: decided, not applied"), ADR-010 (Entra), ADR-015 (identities), ADR-016 (promotion), ADR-024 (Ask V2), ADR-025 / ADR-026 (membership & invitations) |

## 0. Binding instructions for this and the following runs (stakeholder, 2026-09-13)

1. **Every wave the w14 intake queued is executed, in order, in full.** W15 takes
   its own queue (§3) plus the two outcomes below; W16, W17 and W18 keep the
   queues recorded in `w14-requirements.md` §5 and are the next three runs.
   Nothing is dropped and nothing is pushed to a later wave than the one it is
   already queued for; an item that does not fit this wave's cap becomes the
   **head** of the next wave, never its tail.
2. **An item the raw input marks `must` or "no longer deferred" is never queued
   beyond the next wave.** The w14 intake queued NW-27 and NW-61 to W18 against
   the input's own "no longer deferred" and demoted NW-27 to `should`; that is
   reversed here and must not recur.
3. Two outcomes are `must` and lead W15's selection; if the cap (20 tasks /
   5 phases) binds, `should` items overflow to the head of W16 — never these:
   - **A. Uploading feels instant** — NW-27 + NW-61.
   - **B. Inviting a colleague works end to end without the Azure portal** —
     NW-67, NW-68, NW-69 (+ the NW-58 residual).
4. The w14 zero-infra-delta rule does **not** apply to W15: both outcomes need
   Terraform (queue / worker scaling, ACS Email, a Graph permission), applied
   through the ADR-016 HITL gate.

---

## 1. Uploading feels instant (NW-27, NW-61 — carried over, promoted to `must`)

### NW-27 — Upload HTTP returns as soon as the file is stored

- **Status:** OPEN — **must** (was queued W18 / `should` by the w14 intake).
  Stakeholder: "il caricamento deve essere quasi immediato a livello UI, poi
  continua in background".
- **Reported (dev, 2026-09-13 19:34):** 15 PDFs dropped on `/documents`; every
  row "Processing / Uploading…" for many minutes; both counters read
  "Needs your attention · 0 / All documents · 0" (the server list is empty
  until the first request returns).
- **Today:** `POST /api/documents` reads the file → admission gate (Foundry
  classify) → `UploadAsync` (blob + rows) → `processingPipeline.ProcessAsync`
  (parse / OCR / staged extraction / embedding) → only then 201
  (`../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:256-280`). The SPA
  runs at most 3 uploads in flight (`../web/src/routes/documents/uploadPipeline.ts:21`)
  and keeps "Uploading…" until each request ends; a request that dies flips the
  row to `failed` + "Retry upload" (`uploadPipeline.ts:128`). `Raffa.Worker`
  exists but consumes an in-process queue and dispatches nothing
  (`backend/README.md` → "Worker"). `requirements.md` A7 ("upload stays
  synchronous in V2; worker queue is a later task") is **superseded** by this
  item. Container Apps' HTTP request timeout (≈ 4 min, platform) is a hard
  ceiling on the current design with real Foundry + Document Intelligence.
- **Must:**
  - `POST /api/documents` answers **within ~2 s** once the blob and the
    `document` row exist (status `Uploaded`), for every file of a batch;
    classify / OCR / extraction / embedding run on `Raffa.Worker` through a
    **durable** queue (Service Bus is already in Terraform — ADR-002 / ADR-005;
    the council decides the queue, the retry policy and the worker's scaling).
  - The admission gate (R-DOC-01) stays a product rule: either it fits the 2 s
    budget synchronously, or it moves to the worker and a refused file ends in
    a terminal `Rejected` state the list renders as today's "Not added" card.
  - The worker writes `processingStatus` + `stage` (R-DOC-09); the existing
    `GET /api/documents` polling (2 s) renders the progression; a failed stage
    ends in `Failed` with a reason and "Retry upload" (`POST …/reprocess`)
    works on it; no document is lost on an API or worker restart; N files
    dropped together are all accepted immediately and processed with bounded
    parallelism.
  - `POST /api/documents/{id}/reprocess` follows the same async shape.
- **Evidence:** files above; `web/README.md` "Documents" (≤ 3 in flight);
  `reports/context/waves/w14-requirements.md:610` (NW-27 audit).

### NW-61 — Upload must feel instant; details only when the document is ready

- **Status:** PARTIAL → **must** (was queued W18 by the w14 intake).
- **Today** (w14 intake audit, `w14-requirements.md:656`): the optimistic row,
  the real server stages and the 2 s polling exist
  (`../web/src/routes/documents/DocumentStatusTable.tsx:86-110`,
  `useDocumentsList.ts:105-119,131-141`); with NW-27 unresolved the row sits on
  the client phase "Uploading…" instead of a server progression, the counters
  stay 0, and Contract 360 renders whatever the aggregate returns with no
  completeness check (`../web/src/routes/contracts/contract360/index.tsx:72-115`).
- **Must:** within ~2 s of a drop every file is a **server row** (counted,
  listed, survives a reload and shows in a second browser), then progresses
  `Uploaded → Processing (stage) → Needs review | Completed | Failed` by
  polling; "Completed documents are hidden — already askable" stays; Ask,
  Portfolio and Contract 360 gate on completeness (a document still processing
  is shown as such, never as an empty contract or a fabricated fact); the
  rail's Documents badge reads the server (this overlaps NW-10 — take NW-10
  into W15 if the same task touches it).

---

## 2. Inviting a colleague works end to end (NW-67, NW-68, NW-69; NW-58 residual)

**Binding flow:** Admin invites an email → the person **receives an email** →
opens the link → **is signed in and inside that workspace**. The Workspace
Admin never opens the Azure portal; nobody pre-creates the identity.

### NW-67 — Raffa provisions the invitee's Entra identity at invite time (B2B guest via Microsoft Graph)

- **Status:** OPEN — **must**. Decided by the stakeholder 2026-09-13 among three
  options: Entra External ID / CIAM (a longer migration of SPA + API + ADR-010),
  `common` authority + personal Microsoft accounts (no provisioning, but forces
  a Microsoft account), **chosen: B2B guest in the existing tenant**.
- **Reported (dev, 2026-09-13):** the Admin invited an external gmail address;
  the accept link opened `/invite/accept`; the sign-in popup showed Entra's
  "Non è stato possibile trovare un account con il nome utente specificato" —
  the address is not a user of the tenant the SPA authenticates against
  (single-tenant `oidcAuthority` in `config.json`). w14 recorded "a second
  Entra account on the pilot tenant" as an **operator prerequisite**
  (`docs/waves/w14-acceptance.md` known gap 2); that prerequisite is now the
  product's own job.
- **Today:** `POST …/invites` writes `workspace_user` + `workspace_invitation`
  and returns the accept link (`WorkspaceMembershipService`, ADR-025 / ADR-026);
  nothing talks to Entra. A guest exists only when a tenant admin invited it by
  hand — which is how the stakeholder's own gmail account works today.
- **Must:**
  - On invite, Raffa creates the **Entra B2B guest** for the invited address in
    the tenant through Microsoft Graph `POST /invitations`
    (`invitedUserEmailAddress`, `inviteRedirectUrl` = the absolute Raffa accept
    link, `sendInvitationMessage: false` — Raffa's own mail is the channel,
    NW-68). An address that is already a member or guest is a no-op.
  - The API (or the Worker) calls Graph with the application's own identity:
    application permission `User.Invite.All` (or the least-privilege
    equivalent), admin consent granted **once** by the tenant admin at setup —
    the council decides the identity (managed identity vs app registration),
    where the secret lives (ADR-011 / ADR-015) and whether Terraform manages
    the permission (ADR-010 amendment).
  - Redemption: opening the Raffa link → Entra sign-in (email one-time passcode
    for an address with no Microsoft account; consent on first redemption) →
    back on `/invite/accept` signed in → **accept completes without a further
    click** (today the user must press "Join" after the popup — ADR-012 w14
    footer clause 6 already sanctions the popup).
  - Provisioning failure (Graph 4xx, missing consent) is a **named** error on
    the invite pane and an audit row — never a "ready" link that cannot be
    used. Audit `workspace.guest.provisioned` / `.provisioning_failed` with no
    token and no PII beyond the address (ADR-011).
  - Open question for the council: removing a member does **not** delete the
    guest (it may belong to other workspaces); record the rule.
- **Evidence:** `../web/src/routes/invite/accept/index.tsx:172-183`
  (`loginPopup` CTA), `../web/src/App.tsx:140` (`account.username` is the
  identity sent as `X-User-Id` — for a B2B guest it is the invited address, so
  the accept's case-insensitive email match holds), ADR-025 §A2, ADR-010,
  ADR-015, `docs/waves/w14-acceptance.md` N3b.

### NW-68 — Raffa sends the invitation email (ACS Email)

- **Status:** OPEN — **must**. Stakeholder 2026-09-13: "dovevi già farlo ora".
- **Today:** `NullInvitationMailer`, `mailDelivered: false`; the pane shows
  "Invitation ready for …" + Copy link (ADR-026 §D6). The transport is
  **decided, not applied**: the ADR-005 w14 footer records ACS Email + Azure
  Managed Domain, the module / secret shape, the config keys and the rejected
  alternatives; the delivery-failure copy is pre-decided in the ADR-020 w14
  footer ("Invitation created, but the email could not be sent." + "Try
  sending again" — never "invite again").
- **Must:** apply the ADR-005 design in both environments (Terraform + HCP
  apply through the ADR-016 gate); sender identity / secret via Key Vault
  (ADR-011 / ADR-015); an `IInvitationMailer` implementation sending a
  Raffa-branded mail with the **absolute** accept link — the API must learn
  the SPA origin per environment (the config key the w14 zero-delta rule
  avoided; ADR-016 w14 footer names the two shortcuts that are drift);
  `mailDelivered: true` only on an accepted send; the pane then says
  "Invitation sent to {email}." and still offers Copy link; the failure path
  uses the ADR-020 copy and a re-send action; audit
  `workspace.invitation.mail_sent` / `.mail_failed`; no mail body in logs or
  audit rows.

### NW-69 — Invite pane: honest delivery and identity state

- **Status:** OPEN — should. With NW-67 / NW-68 landed: show delivery state
  (sent · could not be sent · provisioning failed) and the expiry. If either
  slips, the pane must tell the Admin what the invitee still needs (an account
  on the tenant) — today the first signal is Microsoft's error inside the
  popup, where Raffa cannot intervene.

### NW-58 — residual

- **CLOSED-ON-MAIN** for the token / accept / remove lifecycle (w14). Its two
  deferred `must` clauses — the email and the identity — are NW-68 and NW-67.
  `web/e2e/invite.spec.ts` (N3b) un-skips once the flow creates the second
  account itself; the council decides how the e2e reads the one-time passcode
  (a test-only seam or a mail-catcher on `dev`).

---

## 3. W15 queue (from `w14-requirements.md` §5 — carried over unchanged)

| ID | Item | Priority | Status |
|---|---|---|---|
| NW-05 | API JWT (ADR-010) replaces spoofable headers | must | OPEN — pairs with NW-67: the token subject becomes the identity the accept and every tenant-scoped route trust; sequence it so NW-67's guest redemption yields a token the API validates |
| NW-06 | Workspace role from membership / claims, not `?role=` | must | OPEN |
| NW-07 | Conversation `user_id` is the token subject | should | PARTIAL |
| NW-08 | `GET /api/audit` works for a real Admin | should | OPEN |
| NW-31 | Dual role headers (`X-Role` vs `X-Workspace-Role`) until NW-05 | should | PARTIAL — w14 already deleted the header branch from `WorkspaceRoleResolver` (E14/F02/US02/T01); what remains is `GET /api/capabilities` and the OpenAPI wording |
| NW-32 | Unattributed actor on writes when `X-User-Id` is absent | should | OPEN |

W16 (NW-10, NW-11, NW-12, NW-13, NW-21), W17 (NW-20, NW-22, NW-23, NW-25,
NW-26, NW-62, NW-63, NW-64, NW-65, NW-66) and W18 (NW-30, NW-40, NW-41, NW-50,
NW-55, NW-56, NW-57, NW-59, NW-60) stay queued exactly as recorded in
`w14-requirements.md` §5 and are the next three runs (§0.1).

---

## 4. Also observed on the first `dev` walk (traceability)

- **NW-70** — The create-workspace form submitted the country **label**
  ("Switzerland") where `POST /api/workspaces` validates the ISO code (`CH`);
  the pick row would have shown "· CHF · CH". **CLOSED-ON-MAIN** (PR #98,
  2026-09-13). Recorded so the intake does not re-open it.
- `main` had been red on two Testcontainers fixtures since the wave merged and
  `deploy (dev)` was skipped twice; fixed in PR #97. The wave-close report
  (HITL issue #94) could not see it — a Helix process item, tracked outside
  this product backlog.

---

## 5. Suggested grouping and seats (dynamic council)

| Theme | Items | Seats |
|---|---|---|
| A · upload feels instant | NW-27 (backend / worker / infra), NW-61 (web), NW-10 if co-located | software-architect, cloud-architect, client-architect, ux-ui-designer, delivery-manager, product-owner |
| B · invite end to end | NW-67 (identity / Graph), NW-68 (mail / ACS / infra), NW-69 (web) | security-architect, cloud-architect, software-architect, client-architect, ux-ui-designer, delivery-manager, product-owner |
| C · identity hardening (W15 queue) | NW-05, NW-06, NW-07, NW-08, NW-31, NW-32 | security-architect, software-architect, client-architect, delivery-manager |

Order: A and B first (both `must`), C's NW-05 / NW-06 next (both `must`), then
C's `should` rows. Overflow → head of W16.

---

## 6. Acceptance seeds (on deployed `dev`, after the ADR-016 gate)

| # | Check | Expected |
|---|---|---|
| A15-1 (NW-27 / 61) | drop 15 PDFs on `/documents` | within 2 s all 15 rows exist server-side ("All documents · 15"), a reload and a second browser show them; each reaches Needs review / Completed / Failed with the browser closed; `POST /api/documents` p95 < 2 s |
| A15-2 (NW-27) | restart the API and the Worker mid-batch | no document lost; every row ends in a terminal status |
| A15-3 (NW-61) | open Ask / Portfolio / Contract 360 while a document is processing | "still processing" — never an empty contract or an invented fact |
| A15-4 (NW-67 / 68) | invite a gmail address the tenant has never seen | the address receives a Raffa email within 1 min → link → sign-in (passcode) → inside the workspace as Procurement, **no** "Join" click, **no** Azure portal; the roster shows Active |
| A15-5 (NW-67) | invite an address already in the tenant | same flow; no duplicate guest |
| A15-6 (NW-68) | mail transport failing | ADR-020 failure copy + "Try sending again"; `mailDelivered: false`; the link still works |
| A15-7 (NW-67) | admin consent missing | named error on the pane, audit row, no invitation claiming "sent" |
| A15-8 (NW-05) | a request with a forged `X-User-Id` / `X-Tenant-Id` and no valid token | 401; the token subject is the only identity |
| N3b (w14) | `web/e2e/invite.spec.ts` | runnable — the flow creates the second account |

---

## 7. Traceability

NW-27 / NW-61 ← `next-waves-todo.md` §6 (NW-27 "no longer deferred", NW-61) and
`w14-requirements.md:121,131`. NW-67 / NW-68 ← NW-58's deferred clauses, ADR-005
w14 footer, ADR-020 w14 footer, `docs/waves/w14-acceptance.md` known gaps 1–2.
NW-70 ← PR #98. W15 queue ← `w14-requirements.md` §5.
