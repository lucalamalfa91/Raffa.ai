# Contigo — next-waves todo

Status: **binding input** for the next-waves requirements document. Not a
wave-spec and not an epic yet. Every row is a remaining hole after waves
R0–R4 + e06–e13, checked against **`origin/main`**.

| | |
|---|---|
| Compared | `origin/main` @ `383020e` (2026-09-10, merge of PR #73 `feat/ai-gateway-live`) |
| This worktree | `web/v2-design-alignment` |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, `inputs/percorso-pilota-v1.md` |
| Architecture | ADR-009 (RLS), ADR-010 (API JWT), ADR-022 (interim headers), ADR-024 (Ask V2) |

**Do not treat** `inputs/requirements.md` §1 (P1–P17) or
`reports/audit/ask-v2-gaps.md` OPEN rows as current. Those were the
2026-09-08 audit of `integration` @ `4a1bddf`. Epic-13 closed almost all
of them on `origin/main`. This file is the remaining inventory.

Persistence rule: **if a human can create or change it, another browser /
device / session must read it back from Postgres (RLS).** Browser
`localStorage` / `sessionStorage` is not a system of record.

IDs (`NW-*`) are stable. Status: `OPEN` · `PARTIAL` · `CLOSED-ON-MAIN` ·
`DEFERRED` · `THIS-BRANCH-ONLY`.

---

## 0. Already on `origin/main` (do not re-open)

Ask V2 / e13: Foundry gateway, admission gate, `GET /api/documents`,
conversations, supplier, market feed/index, insights, capabilities, V2
shell + Documents UI, citation landing, golden set, `v2.spec.ts`.
Terraform injects `ConnectionStrings__Suppliers` and `__Market`.

---

## 1. Identity, workspace, membership

**Reported after first login**

| What you see | What it actually is |
|---|---|
| Picker shows the workspace with **0 contracts** | Count frozen at create in `localStorage` (NW-09). Same tenant may still have files on `/documents`. |
| Picker / Documents empty after “creating again” | Cache miss → **new** tenant (NW-01/NW-02). Old files sit on the previous id. |
| **Delete** / **Retry upload** visible, click does nothing useful; Network shows `DELETE`/`POST …/reprocess` **403** (preflight 200) | UI infers Admin locally; API has no `workspace_membership` row for the caller (NW-14, caused by NW-02). |
| Invite form says **Invitation sent.** | POST writes a membership row only — **no email** (NW-58). Invitee never gets a link. |

**Oracles:** product-spec §3.2, §16 R0, §20 Day 1; `us-01-workspace-roles`;
percorso pilota §2 step 1; R-WEB-01; ADR-009; ADR-010; D5.

### NW-01 — `GET /api/workspaces` for the signed-in identity

- **Status:** OPEN on `origin/main`
- **Today:** only `POST /api/workspaces` and `POST …/invites`. Picker reads
  `localStorage` `contigo.signin.knownWorkspaces.{homeAccountId}`.
- **Must:** list workspaces the caller belongs to (membership), RLS.
- **Evidence:** `web/src/routes/signin/workspaceStore.ts`; `web/README.md`.

### NW-02 — Create workspace also writes the creator’s membership (Admin)

- **Status:** OPEN on `origin/main`
- **Today:** `WorkspaceProvisioningService.CreateWorkspaceAsync` persists
  `WorkspaceTenant` + default **roles**, not `workspace_membership` for
  the caller. Host comment: “pre-authentication signup step” (no JWT).
- **Must:** creating identity becomes Workspace Admin in
  `workspace_membership`. Without this row, NW-01 cannot return the
  workspace and NW-14 always 403s.
- **Evidence:** `WorkspaceProvisioningService.cs`;
  `WorkspaceEndpointExtensions.cs`.

### NW-03 — Current workspace is a server fact, not `sessionStorage`

- **Status:** OPEN on `origin/main`
- **Today:** `contigo.signin.currentWorkspace` in `sessionStorage`.
- **Must:** one membership → enter it; several → picker from NW-01.

### NW-04 — `GET /api/workspaces/{tenantId}/members`

- **Status:** OPEN on `origin/main`
- **Today:** invite POST is real; table is `memberStore.ts` session cache.
- **See also:** NW-58 (email + accept link + Admin remove).

### NW-58 — Invites are email + link; login joins that workspace; Admin remove requires a new invite

- **Status:** OPEN on `origin/main` (observed 2026-09-10)
- **Reported:** it is unclear how invitations are sent. They must go
  **by email**: the invitee receives a mail with a **link**, clicks it,
  **signs in**, and **joins that workspace**. They stay a member until
  an Admin **removes** them. Re-adding them needs a **new invite**.

- **Today:**
  - Members UI says **Send invitation** / **Invitation sent.**
    (`InvitePane`, `INVITATION_SENT_MESSAGE`). The API only
    `POST /api/workspaces/{tenantId}/invites` — writes
    `workspace_user` + `workspace_membership`. **No mail**, no token,
    no accept URL. Nothing in the backend sends SMTP/Graph/SendGrid.
  - The invitee never sees a message. Even if they sign in, the picker
    is `localStorage` (NW-01) so they **do not** land in the workspace
    they were invited to. `WorkspaceMembershipService.LinkSignInAsync`
    / `WorkspaceSignIn` exist in domain tests; **no host endpoint**
    calls them on Entra login.
  - **No DELETE** membership. Repeat invite of the **same role** fails
    (`already holds the {role} role`). After a real remove, a second
    invite must be allowed.

- **Must:**
  1. Admin **Send invitation** sends a real email to that address,
     with a link that names this workspace (signed token / one-time
     accept URL). UI must not say “sent” unless the mail left.
  2. Invitee clicks the link → Entra sign-in → subject is linked to
     the invited email (`LinkSignInAsync`) → they **enter that
     workspace** (picker from NW-01 includes it; no second “create
     workspace”).
  3. Admin can **remove** a member (not themselves if last Admin).
     Removed user loses access immediately (GET workspaces / RLS).
  4. A removed email is **not** a member until a **new invite** is
     sent and accepted again.

- **Depends on:** NW-01 (list after accept), NW-04 (members table is
  server), NW-05 (who is allowed to invite/remove).

- **Evidence:** `WorkspaceEndpointExtensions.InviteAsync`;
  `WorkspaceMembershipService.InviteAsync`; `InvitePane.tsx`;
  `memberStore.ts`; `WorkspaceSignIn.cs`; Sign-in copy “Ask your
  Workspace Admin for an invitation”; spec §3.2 / screens.md #2.

### NW-05 — API JWT (ADR-010) replaces spoofable headers

- **Status:** OPEN on `origin/main` (ADR-022 still in force)
- **Today:** no `AddJwtBearer`. Tenant = `X-Tenant-Id`, actor = `X-User-Id`.
- **Must:** bearer token; membership is authorization.

### NW-06 — Workspace role from membership / claims, not `?role=`

- **Status:** OPEN on `origin/main`
- **Today:** `workspaceRole.ts` treats whoever picked the workspace in
  this browser as Admin. That is why Delete is **shown** (NW-14).

### NW-07 — Conversation `user_id` is the token subject

- **Status:** PARTIAL — stored per user, keyed by `X-User-Id`.

### NW-08 — `GET /api/audit` works for a real Admin

- **Status:** OPEN — route wants a `ClaimsPrincipal`; missing from OpenAPI.

### NW-09 — Workspace picker contract count is frozen at 0

- **Status:** OPEN on `origin/main`
- **Reported:** login shows **0 contracts** after uploads.
- **Today:** `WorkspacePickerScreen` writes `contractCount: 0` into
  `localStorage` and never calls `GET /api/contracts` or
  `GET /api/documents`.
- **Must:** count from the server; agree with Documents / Portfolio.
- **Evidence:** `WorkspacePickerScreen.tsx`; `workspaceStore.ts`.

### NW-14 — Delete and Retry upload 403 for the workspace creator

- **Status:** OPEN on `origin/main` and on `dev` (observed 2026-09-10)
- **Reported:** Documents row actions **Retry upload** and **Delete**
  look live; they fire real fetches that fail. Network:

  | Request | Status |
  |---|---|
  | `OPTIONS /api/documents/{id}` (preflight) | 200 |
  | `DELETE /api/documents/{id}` | **403** (17 B) |

  Same for `POST /api/documents/{id}/reprocess` (Retry on a failed
  server row). CORS is fine; the DELETE is authorized as “not Admin”.

- **Curl (dev, 2026-09-10):** `DELETE` to
  `ca-contigo-dev-api…/api/documents/48fb4142-49e2-4dbf-9e60-058f0c6fc27b`
  with `X-Tenant-Id: 6dcf8b7a-a46b-4fec-846d-c8fbce7d6896` and
  `X-User-Id: pietronasuti35@gmail.com`, **no** `X-Role` /
  `X-Workspace-Role` → 403. Origin SWA `mango-pond-061bc6d1e`.

- **Why:** both routes are Admin-only
  (`DocumentsEndpointExtensions` → `WorkspaceRoleResolver.IsAdminAsync`).
  Resolver order: JWT claims (none) → `X-Role` / `X-Workspace-Role`
  (client does not send them) → `workspace_membership` lookup by
  `X-User-Id` email/subject. Create never inserted that row (NW-02), so
  role is null → 403. The SPA still sets `isAdmin={role === "admin"}`
  from `resolveWorkspaceRole()` (NW-06), so Delete is shown.

- **UI:** client maps 403 to “Only a Workspace Admin can delete /
  reprocess a document” (`client.ts`) into a `.hint` alert. Easy to miss;
  the buttons look like no-ops.

- **Must:** after NW-02 + NW-06, the creator’s Delete/Retry succeed
  (204 / 200). Until JWT lands, membership lookup must find the creator.
  Do not “fix” this by sending a spoofable `X-Role: Admin` from the SPA
  as the product solution.

- **Evidence:** `WorkspaceRoleResolver.cs`; `deleteDocument` /
  `reprocessDocument` in `web/src/api/client.ts`; R-DOC-07 / R-DOC-10;
  `web/README.md` Documents row actions.

---

## 2. Remaining client caches standing in for a missing GET

### NW-10 — Documents rail badge still reads `documentStore` (`sessionStorage`)

- **Status:** OPEN (list itself is `GET /api/documents`)
- **Reported:** rail can look like 0 docs while `/documents` lists files.
- **Must:** count from `GET /api/documents` at shell mount; delete
  `documentStore.ts`. Agree with NW-09.

### NW-11 — Renewal actions have no HTTP read-back

- **Status:** OPEN — `POST /api/renewals/{id}/action` persists;
  `renewalActionStore.ts` is session-only.

### NW-12 — Quote GET + negotiation-outcome list

- **Status:** OPEN — no `GET /api/quotes/{id}`; outcomes POST only;
  `quoteOutcomeStore.ts`. Product shape of the list (history inside
  Quote check, Ask-citable) is NW-57.

### NW-13 — Contract 360 negotiation-step ticks (`sessionStorage`)

- **Status:** THIS-BRANCH-ONLY (`negotiationStepsStore.ts`).

---

## 3. Domain read models

### NW-20 — Contract 360 `benchmark` and `activity` are always `[]`

- **Status:** OPEN — empty arrays on the 360 payload. Product shape of
  the answers band is NW-62.

### NW-21 — Quote outcome does not update Savings

### NW-22 — Renewal insight `MarketPosition` stays null on real contracts

- **Status:** OPEN — `buildAnswers` therefore shows “Not yet
  available”. See NW-62.

### NW-23 — Portfolio has no `category` filter

### NW-24 — Workspace has no currency / region (HITL)

### NW-25 — Savings list has no filters

### NW-26 — Preview / evidence quality residuals

  Document preview/evidence gaps in Documents / Contract 360. Distinct
  from the Ask citation-card UX in NW-55. Full page viewer + editable
  OCR highlights is NW-63.

### NW-27 — Upload HTTP must return as soon as the file is stored (was A7 “later”)

- **Status:** OPEN — no longer deferred. Product need is NW-61.
- **Today:** `POST /api/documents` reads the whole file, runs the
  admission gate (Foundry), `UploadAsync`, then
  `processingPipeline.ProcessAsync` **before** 201. The SPA sits on
  “Uploading…” until that request ends (`requirements.md`: “Upload
  stays synchronous in the request for V2 … worker queue is a later
  task”).
- **Must:** persist + 201/202 as soon as the blob and `document` row
  exist; classify / OCR / extract on a worker. List `GET` already
  polls stage (R-DOC-09). See NW-61 for the perceived-instant UX.

---

## 4. Contract / OpenAPI / host

### NW-30 — Hand-authored OpenAPI gaps (conversations `requestBody`, no `GET /api/audit`)

### NW-31 — Dual role headers (`X-Role` vs `X-Workspace-Role`) until NW-05

### NW-32 — Unattributed actor on writes when `X-User-Id` is absent

---

## 5. Infra / ops

### NW-40 — Confirm HCP apply has CA env vars + Foundry RBAC

### NW-41 — Prove deployed API serves seeded `market_record` rows

---

## 6. Product / demo residuals

### NW-50 — `web/e2e/day1.spec.ts` red against the V2 shell

### NW-51 — V2 satellite-screen design alignment (THIS-BRANCH-ONLY until merge)

### NW-52 — Paid market-intelligence provider (DEFERRED)

### NW-53 — Mobile beyond scaffold (DEFERRED, ADR-013)

### NW-54 — Legal / Finance / Read-only as first-class nav (DEFERRED)

### NW-55 — Ask citation cards: real page preview or a section link, never the empty placeholder

- **Status:** OPEN on `origin/main` and on this branch (observed 2026-09-10)
- **Reported:** Ask answers always render a dashed **“No page preview
  available”** box. Two cases, both wrong:

  1. **Document / tenant citation** (e.g. “When must we give notice to
     Northwind…”, “Which documents are not askable yet?”). The card
     already has a clause snippet or a document title, but the preview
     slot is empty. The user wants the **page preview** when it can be
     recovered from the document, and a way to **open the document at
     that point** (page / clause), not a dead placeholder.
  2. **Product / capability citation** (corpus `contigo`: Documents,
     Portfolio, Renewals, Quote check — e.g. “What can Contigo do?” /
     “How do I upload a contract?”). A page preview does not apply.
     The card should be a **button / link / destination card** to the
     section being explained (`href` is already `/documents`,
     `/contracts`, `/renewals`, `/quotes`).

- **Today:** `CitationCard` always shows `<img>` or the placeholder.
  `previewUrl` is usually null (`PackItem.PreviewUrl` is almost never
  set in `AskCopilotService`). Even when it is a relative
  `/api/documents/{id}/preview`, `<img src>` does not send
  `X-Tenant-Id` (authenticated fetch is `getDocumentPreviewUrl` →
  blob URL). Whole card is already one `onOpen`; tenant href already
  carries `?page=` via `buildTenantCitationHref`. Capability cards
  still get the same empty preview chrome.

- **Must:**
  - Tenant (and any citation grounded in a document): show the page
    preview when the API can serve it; open the document **at the cited
    page/span**. No dashed empty box when a snippet exists but the
    image is still missing — prefer a compact “Open in document”
    control over the 120px placeholder.
  - Contigo / product-section citations: **no** preview slot; a clear
    CTA (button, link, or card) to the explained route.
  - Market citations: keep the side panel; do not fake a page preview.

- **Evidence:** `web/src/routes/ask/reply/CitationCard.tsx`;
  `reply.css` `.citation-card-preview-placeholder`;
  `askViewModel.ts` `mapConversationCitation` /
  `buildTenantCitationHref`; `client.ts` `getDocumentPreviewUrl`;
  `PackItem.PreviewUrl`; R-WEB-04, R-DOC-08, R-EVD-01. Screenshots
  2026-09-10: empty boxes on capability cards, processing tenant
  cards, and a Northwind clause card that already has excerpt text.

### NW-56 — Contract 360 “Ask about it” must brief that contract, not the upload onboarding

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** from a contract **overview** (Contract 360), **Ask about
  it** lands on the Ask screen that tells you to **upload a document**.
  The user already has the contract open. Expected: a first reply with
  **info on that contract**, and at the end of that message **suggested
  questions specific to it**.

- **Today:**
  - `Contract360Header` links to `/ask?scope=<contractId>` with **no**
    seeded question. Ask only auto-asks when router `state.query` is
    set (Documents “Ask about it” does this: `When does {supplier}
    expire?`; 360 does not).
  - `AskRoute` still applies the global **R-ASK-10** gate first: if
    `useValidatedContractCount` says `kbReady === false`, it renders
    `AskOffState` (“Upload a contract first” / “still processing”) and
    **ignores** `?scope=`. A 360 page can exist while the portfolio
    status is still `processing` / `needs_review` (that predicate
    excludes anything containing `review`), so the user is gated off
    Ask even though they just came from the contract.
  - When the gate is open, `/ask?scope=` is an **empty** new chat with
    two chips (`When must we give notice to {supplier}?` / liability
    cap). No briefing is posted. Follow-ups exist only after a model
    reply (`ReplyBody` “Follow up”), and they are not authored as a
    contract-specific question set for this entry path.

- **Must:**
  - **Ask about it** from a contract the user can already see opens a
    scoped chat and **immediately** answers about **that** contract
    (overview / key facts), not the empty hello and not the upload
    off-state.
  - The first Contigo message ends with **possible questions about this
    contract** (notice, expiry, liability, spend, … — named to the
    supplier/contract, not “How do I upload a contract?”).
  - The global “Ask needs a validated contract” off-state stays for
    `/ask` with **no** scope and no corpus; it must not fire when the
    user arrived from a live Contract 360 of a specific id.

- **Evidence:** `Contract360Header.tsx` (`/ask?scope=`);
  `AskRoute` `if (!kbReady) return <AskOffState>`; `buildOffCopy`;
  `DocumentStatusTable.tsx` seed `state.query` (contrast);
  `buildScopedSuggestions`; us-01-contract360-landing AC-2 (chips name
  the supplier — navigation only, no auto-brief).

### NW-57 — Quote check: market benchmark, not a manual savings worksheet; keep every request

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** Quote check is unclear — the user does not know what to
  do, gets **no suggestions**, and must **set every field by hand**.
  That produces **useless saving projections**. The job is a **market
  benchmark for the contract they want to sign**, not a DIY target
  form. After the flow it asks whether to put the document on **Home**;
  that page **no longer exists** (Ask is home), so **results are lost**.

- **Today:**
  - Landing is a drop zone plus optional supplier / currency /
    geography / date (`UploadQuoteForm`). Unmatched SKUs require
    mapping; Target/Walk-away are **editable numbers**
    (`TargetStep`) that invent a savings range. Assessment is
    `InsufficientBenchmarkData` when the mock market feed has no
    match (`MarketAssessmentCalculator`).
  - There is **no `GET /api/quotes` list** and no `GET /api/quotes/{id}`
    for upload metadata (NW-12). Outcomes are `POST` only;
    `quoteOutcomeStore.ts` is sessionStorage. The end CTA is
    “See it in Savings →” (`NegotiationStep`) — Day-1 AC-4 “Home
    Savings Realized”. V2 removed Home; `/savings` is not a quote-check
    history.
  - Ask can route to `/quotes` but cannot retrieve **this user’s past
    quote-check requests**.

- **Must:**
  1. **Job to be done:** upload (or pick) the quote for a contract they
     want to subscribe; Contigo returns a **market position**
     (below / in line / above) with provenance — not a blank form that
     asks them to type a saving.
  2. **Suggestions:** pre-fill supplier / SKU / geography from the
     file and from similar contracts already in the workspace; do not
     require a full manual mapping before any number appears.
  3. **Cold start:** if there is no market row yet, **keep this quote
     as the first document of that type**, enrich the shared source, and
     still give an honest result (“first of this type; market band
     pending / seeded from this file”). When **someone else** later
     uploads documents from the **same company or similar** contracts,
     the comparison updates (peer + market, RLS-safe — never leak
     another tenant’s commercials as raw lines).
  4. **Do not send results to Home.** Home is gone. Persist the
     assessment on the quote.
  5. **Quote check history:** a section listing **every request this
     workspace has made** (re-open `/quotes/:id`, see the benchmark
     again). Those rows are **Ask-citable** (Ask answers “what did we
     last check on Databricks?” from this list, with a CTA back to
     that quote).

- **Related, do not merge:** NW-12 (missing GET), NW-21 (outcome ≠
  Savings KPI), NW-52 (paid market feed, deferred). This row is the
  product shape: benchmark-first UX, corpus that grows with similar
  contracts, history inside Quote check, Ask over that history.

- **Evidence:** `web/src/routes/quotes/`; `UploadQuoteForm.tsx`;
  `TargetStep.tsx`; `NegotiationStep.tsx` “See it in Savings”;
  `quoteOutcomeStore.ts`; `MarketAssessmentStatus.InsufficientBenchmarkData`;
  R-WEB-01 (Ask is home); us-01-quote-check AC-4.

### NW-59 — Ask never dead-ends: every abstain/error has a clickable next step

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** asking “When must we give notice to this supplier?”
  returns only the red **Cannot determine reliably.** block (“Nothing
  in the 1 validated contract(s) supports a reliable answer. Try a
  question about dates, spend, notice periods or clauses.”). The
  suggested questions are **plain text**, not controls. The user is
  stuck. **Always** offer a **clickable** way to fix or continue.

- **Today:** `AbstainReply` / `ErrorReply` are defined with **no
  `actions`** (`replyTypes.ts`: “abstain/error never carry actions”).
  `ReplyBody` abstain = `.abstain-block` only; error = `.error-state`
  only. `redirect`/`refusal` already have **one CTA**; `answer` has
  actions + follow-ups. Copilot abstain is reason-only
  (`CopilotReplyBuilder`). The brief already said not to use that
  block as the *only* UX (`ask-copilot-brief.md`; R-ASK-07) except
  when there is truly no hook — the UI still does.

- **Must:**
  - Any reply that cannot answer (abstain, transport error, empty
    evidence) still ends with **at least one native control** (button
    or in-app link) that **does something**: open the scoped contract /
    Review extraction, go to Documents to add a missing file, ask a
    concrete follow-up chip, retry. Never “try a question about…” as
    the only next step.
  - Prefer the action that unblocks **this** failure (notice missing
    → that contract’s 360 / review; no corpus → upload; scoped chip
    named “this supplier” → name and open the real contract).
  - Honesty stays: keep the abstain reason; add the recovery, do not
    invent a fake answer.

- **Related:** NW-56 (scoped Ask about it should brief, not hit this
  empty chip). Distinct: even a justified abstain needs a way out.

- **Evidence:** screenshot 2026-09-10; `ReplyBody.tsx` `case "abstain"`;
  `replyTypes.ts` `AbstainReply`; `CopilotReplyBuilder` abstain
  construction; screens-v2.md §2 Abstain.

### NW-60 — Hide the global Ask bar on Ask screens; keep it everywhere else

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** inside an **open chat**, typing in the **top** Ask
  Contigo bar **refreshes the page and nothing happens**. Proposal:
  **remove that bar on Ask** (already in the chat; composer is at the
  bottom). **Keep it on every other page.**

- **Today:** `AppShell` always renders `GlobalAskBar` above the
  outlet (AC-3 “on every app screen”). Submit is
  `navigate("/ask", { state: { query, newChat: true } })`.
  `AskRoute` seeds that query **once** via `askedInitialQuery` and
  **only** when `currentConversationId === null`. Same `AskRoute`
  instance serves `/ask` and `/ask/:conversationId`, so the ref stays
  `true` after the first seed; a later bar submit on an open chat
  navigates but **does not post**. Duplicate of the in-chat input
  (`ASK_INPUT_PLACEHOLDER` + Ask).

- **Must:**
  - No global Ask bar on `/ask` and `/ask/:conversationId` (off-state
    included). ⌘K on those routes focuses the **chat composer**, not
    a missing bar.
  - Bar stays on Documents, Portfolio, Contract 360, Renewals, Quote
    check, Savings, Workspace, etc., and still opens a **new** chat
    with the typed question.
  - Documented divergence from AC-3 / prototype “one pattern on every
    screen” — Ask is home *and* the conversation, two bars fight.

- **Evidence:** screenshot 2026-09-10 (bar + composer on the same
  Ask thread); `AppShell.tsx`; `GlobalAskBar.tsx` `submit`;
  `AskRoute` `askedInitialQuery`.

### NW-61 — Upload must feel instant; details only when the document is ready

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** dropping files on Documents feels slow (rows stuck on
  **Uploading…** / **Processing**). Upload must be **immediate for the
  user**: a very fast “accepted” moment, work continues **in the
  background**, latency must not be the product. In **detail**, only
  show documents that are **already loaded** — never incomplete facts.

- **Today:** dropzone copy already says “you can leave while they
  process”, but the POST is synchronous (NW-27) so the first row stays
  “Uploading…” with empty supplier/type (`—`). Screenshot 2026-09-10:
  three Northwind samples, Processing, next step Uploading… /
  Validating schema. Partial rows are honest on the **list**; the
  risk is opening 360 / Ask / review on half-extracted fields.

- **Must:**
  1. On drop/pick: the filename appears **at once** as Processing
     (optimistic local row is already there; the HTTP wait must not
     block that feeling — NW-27). User can leave the page.
  2. Pipeline (classify, OCR, extract, schema) runs in background;
     list stage text updates via poll. No fake progress timer.
  3. **Incomplete data stays off detail surfaces:** Contract 360,
     Review, Ask, page preview, Portfolio numbers — only when
     extraction is complete enough (needs_review / completed).
     Processing docs: list + stage only; no invented supplier, spend,
     notice, or empty clauses presented as the contract. Askable
     remains completed-only (existing footer).
  4. Failed / Not added stay explicit, with Retry (NW-14 for 403).

- **Evidence:** screenshot 2026-09-10 Documents; `DocumentsEndpointExtensions`
  `ProcessAsync` before 201; `DocumentStatusTable` local “Uploading…”;
  `useDocumentsList` / `uploadPipeline.ts`; R-DOC-09.

### NW-62 — Contract 360 must answer “where you can save” and “when you must move” from RAG × this document

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** Northwind MSA 360 shows **Where you can save → Not yet
  available**, **When you must move → Not determined**, **What to do →
  Review contract — missing end date**, plus a dump of tokenized
  clauses (fees split into “EUR / 48000 / …”, Flagged 80–95%). That
  screen **adds no value**. The client cares about **where they can
  save** and **when they must move**. Those two answers must come from
  **RAG crossed with the uploaded documents**, not empty honest-gaps
  over a clause list.

- **Today:** `buildAnswers` reads
  `insightCard.recommendations.potentialSavingsRange` /
  `marketPosition` (usually null — NW-20/NW-22) and
  `header.cancellationDeadline` / `endDate` (null even when the
  clause text already has 36-month term, 90-day notice, auto-renewal,
  EUR 48k). `LEVER_NOT_YET_AVAILABLE` tells the user to wait for the
  Benchmark Service. “Why — the clauses behind it” is an extraction
  dump, not proof of the two answers.

- **Must:**
  1. **Where you can save:** a real figure or band from market /
     similar contracts (RAG + this tenant’s corpus) vs this
     contract’s spend/fees, with provenance. First-of-type: honest
     seed, then update when peers land (same idea as NW-57).
  2. **When you must move:** notice deadline / term end **from the
     document** (the 90-day / 36-month wording is already in the
     file). If a structured field is missing, recover it from RAG on
     that contract — do not show “Not determined” while the clause
     is on the same page.
  3. Clauses under the band are **evidence for those two answers**,
     not a raw field dump. Token salad and Flagged rows that do not
     support save/move stay in Review, not on 360.
  4. “What to do” follows from (1) and (2), not “missing end date”
     while the term is in the text. Start negotiation only when
     there is a dated move and a save target.

- **Related, do not merge:** NW-20, NW-22 (empty benchmark wires),
  NW-61 (don’t open 360 on half-ready docs), NW-57 (quote market
  compare). This row is the 360 job-to-be-done.

- **Evidence:** screenshot 2026-09-10 Northwind MSA; `AnswersBand.tsx`;
  `buildAnswers` / `LEVER_NOT_YET_AVAILABLE`; `WhyClauses.tsx`;
  Appendix C rule 10 (honest gap ≠ empty product).

### NW-63 — Document viewer: uploaded pages with OCR phrases highlighted and editable

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** on Contract 360 the user wants a **section that opens a
  viewer** of the **uploaded document**. Phrases already recovered by
  **OCR** are **highlighted on the page**. They can **edit those
  phrases from the viewer**.

- **Today:**
  - 360 “Why” is a clause list + a **serif quote card**
    (`ClauseHighlight`) — not the PDF/page.
  - Review right pane is **page text** with a `<mark>` span and a
    correction form (`EvidencePane`), still not a page viewer.
  - `GET /api/documents/{id}/preview` is a **first-page PNG**, often
    the `PlaceholderDocumentPreviewRenderer` (NW-26). No per-page
    original, no bounding boxes, no overlay.

- **Must:**
  1. From 360 (and Review): a control opens a **document viewer**
     (the real uploaded file, page by page — not a placeholder PNG).
  2. OCR / extraction spans are **already highlighted** on those
     pages (boxes or marks aligned to `sourcePage` / span / offsets).
     Clicking a 360 clause or a Review field **jumps to that page and
     highlight**.
  3. From the highlight, the user can **edit the recovered text**
     (and the structured field it feeds). Save goes through the
     existing correction write (`correctContract` / review), not a
     silent local tweak.
  4. Viewer is read-only chrome around the file; edits are on the
     extracted phrases, with provenance kept.

- **Related:** NW-26 (real page render vs placeholder), NW-55 (Ask
  citation can deep-link into this viewer at the cited page), NW-62
  (clauses as proof of save/move — viewer is where that proof lives).

- **Evidence:** screenshot 2026-09-10 360; `ClauseHighlight.tsx`;
  `EvidencePane.tsx`; `getDocumentPreviewUrl`; R-DOC-08; R-EVD-01/02.

### NW-64 — Fields OCR did not recover still appear in a separate section the user can fill

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** when OCR / extraction **does not recover** a field, it
  must still be **visible in a section apart**, so the user can
  **fill it in if they know it**. Screenshot: end date / notice
  deadline missing → “Not determined” / “Review contract — missing
  end date”, but **no empty fields to complete** on the 360.

- **Today:**
  - `buildReviewFields` **skips** a catalogue field when both the
    contract value and the evidence proposal are empty
    (`if (currentValue === null && proposedValue empty) continue`).
    End date, cancellation deadline, etc. **vanish from Review**.
  - 360 “Facts you still need to decide” is inside **Details ▾**
    (closed by default) and only lists extracted products/clauses/
    obligations **below 95%** (`computeNeedsAttention` ignores
    `confidence: null` and never lists schema holes).
  - “What to do” complains about missing `endDate` but offers Start
    negotiation, not a form.

- **Must:**
  1. Canonical fields with **no OCR span / no extracted value**
     (`CORRECTABLE_FIELDS` and any other 360 key terms) show in a
     **dedicated “Not found in the document”** (or equivalent)
     section — next to the viewer (NW-63), not buried in Details.
  2. Each row is an **empty input** the user can complete if they
     know it; save is the same correction write as Review. Optional
     vs required is labelled; required empty still blocks Ask /
     save-move until filled or explicitly “unknown”.
  3. Do **not** invent a value. Do **not** hide the hole. Recovered
     phrases stay on the page highlights (NW-63); unrecovered stay
     in this section.

- **Related:** NW-62 (missing end date while the term is in the
  file — recover first; if still empty, this section), NW-63
  (viewer for what OCR *did* find).

- **Evidence:** screenshot 2026-09-10; `reviewViewModel.ts`
  `buildReviewFields` skip; `DetailsSection.tsx`;
  `computeNeedsAttention`; `CORRECTABLE_FIELDS`.

### NW-65 — Details: only officialized facts; drop the “still need to decide” list

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** opening Details creates an **ugly empty left column**
  (short Key terms) next to a **long “Facts you still need to
  decide”** dump (Review · 65–72%, Flagged · 80–85%). That list
  **disorients**. Show **only officialized data** (auto-accepted or
  assigned by the user). **Remove this list from Details** — there is
  already a button to review fields.

- **Today:** `DetailsSection` two-column grid: Key terms (all header
  fields, including “—”) vs Documents + `computeNeedsAttention` (every
  product/clause/obligation **not** in the >95% band) + “Review all →”.
  Flagged/Review rows duplicate Review and the Why list.

- **Must:**
  1. Details (and Key terms / Products / Obligations / Risks on this
     page) show **only officialized** values: auto-accepted (≥90%) or
     human Accept / Correct. No Flagged / Review · N% here.
  2. **Delete** the “Facts you still need to decide” block. Keep
     **Review all →** / Review extraction as the only path to pending
     fields.
  3. No sparse two-column hole. Unrecovered empties belong in NW-64,
     not this list.

- **Evidence:** screenshot 2026-09-10 Details; `DetailsSection.tsx`;
  `computeNeedsAttention`; `NO_ATTENTION_MESSAGE`.

### NW-66 — Why-clauses: no original quote on the row; click = specchietto; viewer link; explain leverage, not confidence

- **Status:** OPEN on this branch (observed 2026-09-10)
- **Reported:** each Why-row shows the **original citation on the
  right** (p.1 + wording) plus **Medium / Low / High** and
  **Flagged · N%**. The original should **not** sit on the row —
  **click the record** opens the **specchietto** (evidence card) for
  that text. **Always** a link to the **full document in the viewer**
  (NW-63). **Medium / Low / High** is unexplained and useless.
  **Remove confidence** — what is on this list is **already
  validated**. Instead, say how **important the field is for
  negotiation**: can they **leverage** it, or is it a **weak** value
  — and **say so clearly**.

- **Today:** `WhyClauses` row = type · normalized · `source` (page +
  span, often the long quote) · `getClauseRiskTag` (raw
  `riskLevel` string, no legend) · `getConfidenceTag` (Flagged /
  Accepted · %). Click already selects and shows `ClauseHighlight`.
  No viewer link.

- **Must:**
  1. Row: type + officialized value only. **No** original wording /
     page quote in the right column.
  2. Click → specchietto with original text (keep `ClauseHighlight`).
  3. Every row (and the specchietto) has **Open in document viewer**
     to that page/span (NW-63).
  4. **No confidence %** on this list.
  5. Replace bare Medium/Low/High with a **plain-language
     negotiation tag**, e.g. **Leverage** (you can push) vs **Weak
     for you** / **Protect** (unfavourable or little room) vs
     **Watch** — plus a one-line why. A short legend on the section
     so the tag is never colour-only.
  6. Only officialized clauses here (same gate as NW-65). Pending
     stay in Review.

- **Related:** NW-62 (this list is proof of save/move), NW-63
  (viewer), NW-65 (Details vs Review).

- **Evidence:** screenshot 2026-09-10 Why-clauses; `WhyClauses.tsx`;
  `getClauseRiskTag`; `ClauseHighlight.tsx`; ADR-019 (text, not
  colour alone).

---

## 7. Suggested grouping

| Wave | Theme | Rows | Success |
|---|---|---|---|
| **W14 — Workspace is real** | membership + list | NW-01, NW-02, NW-03, NW-04, NW-09, NW-14, NW-24, NW-58 | Second browser sees the workspace. Delete/Retry work for the creator. Picker count matches Documents. Invitee joins from the email link; Admin remove needs a new invite. |
| **W15 — API JWT** | ADR-010 | NW-05, NW-06, NW-07, NW-08, NW-31, NW-32 | Spoofed headers rejected. |
| **W16 — No session as SoT** | remaining GET | NW-10, NW-11, NW-12, NW-13, NW-21 | Reload / other device sees badge, actions, outcomes. |
| **W17 — Domain completeness** | empty tabs / numbers | NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66 | 360 answers save + move; viewer + OCR; missing fields fill-in; Details = officialized only; Why-rows = leverage, not confidence or the original quote. |
| **W18 — Contract + ops** | OpenAPI, e2e, live env | NW-30, NW-40, NW-41, NW-50, NW-51, NW-55, NW-56, NW-57, NW-59, NW-60, NW-27, NW-61 | OpenAPI matches host; Foundry A2/A5–A7; Ask citations show a page or a section CTA; Contract 360 “Ask about it” briefs that contract; Quote check is a re-openable market benchmark; Ask abstain always has a clickable recovery; no duplicate Ask bar on the chat screen; upload feels instant and details wait for ready docs. |

**Order:** W15 (token) then W14 (create membership from `sub`) then W16.
W14 can land membership using `X-User-Id` as an interim if JWT slips.

**Out:** NW-52, NW-53, NW-54.

---

## 8. Acceptance seeds

| # | Check |
|---|-------|
| N1 | Create workspace → rows in `workspace` **and** `workspace_membership` (Admin, this identity). |
| N2 | Second browser / cleared storage → same workspace, no second create. |
| N3 | Invite Procurement → other account’s picker lists it; Members matches Postgres. |
| N3b | Invite sends email with a link. Invitee clicks, signs in, enters **that** workspace (not a new one). Admin removes them → access gone. Re-join only after a **new** invite. |
| N4 | Close tab, reopen → same tenant if the user has one membership. |
| N5 | Valid Entra token, no `X-Tenant-Id` → authorized from membership; crafted other-tenant header → 403/404. |
| N6 | Documents badge, renewal action, quote outcome, 360 tracker survive reload. |
| N7 | Another tenant never appears (extend RLS tests). |
| N8 | Upload ≥1 document, sign out, sign in → picker count non-zero; rail matches; `/documents` lists the same files. |
| N9 | Workspace creator: **Delete** → 204 and row gone; **Retry upload** on a failed doc → 200 and processing resumes. A Procurement member still gets 403 on Delete. Repeat from a second browser. |
| N10 | Ask a document question → citation shows a page preview (when the preview API can serve it) and opens the doc at that page/clause. Ask “what can Contigo do?” → capability cards have **no** page-preview slot; each is a CTA to Documents / Portfolio / Renewals / Quote check. No “No page preview available” dashed box in either case. |
| N11 | From Contract 360 **Ask about it**: first Contigo turn is a briefing of **that** contract (not “Upload a contract” / Ask-off). That turn ends with follow-up questions named to this supplier/contract. `/ask` with no scope and zero validated contracts still shows the off-state. |
| N12 | Quote check: upload a quote → market (or honest first-of-type) benchmark without typing a savings target. History lists that request; reopen it after reload. Ask can answer from that history. No CTA to a Home page. A later similar quote from the same/similar supplier updates the comparison. |
| N13 | Force an Ask abstain (question the corpus cannot ground). The red reason is still there, **plus** at least one clickable control that navigates or asks a follow-up. No dead-end “try a question about…” as the only next step. |
| N14 | On `/ask` and `/ask/:id` there is **no** top global Ask bar; the composer at the bottom still works. On `/documents` (and other non-Ask routes) the bar is present and Enter opens a **new** chat that actually asks the typed question. |
| N15 | Drop a contract on Documents: the row is visible immediately; the user can leave. Background stage advances without blocking the HTTP of the drop. Opening that file’s 360 / Ask / Review before it is ready shows **no** half-extracted facts (list may show Processing + stage only). |
| N16 | Open a validated contract whose text has term, notice and fees (e.g. Northwind MSA). **Where you can save** and **When you must move** are concrete answers with citations into that document (and market/similar when available). No “Not yet available” / “Not determined” while those facts are in the file. The clause list under the band supports those answers, not a tokenized field dump. |
| N17 | From that 360, open the **document viewer**: the uploaded file’s pages, OCR phrases highlighted. Click a clause → that page/span. Edit a highlighted phrase → it persists as a correction and the 360/review field updates. |
| N18 | A contract missing `endDate` / notice (or any catalogue field OCR did not recover) shows those fields in a **separate empty section** the user can fill. They are **not** omitted from Review. Saving a value there updates the header and the save/move band. |
| N19 | Details shows only officialized key terms (auto-accepted or human-assigned). **No** “Facts you still need to decide” list. **Review all →** is still there and opens Review. No empty left column beside a Flagged dump. |
| N20 | Why-rows: type + officialized value; **no** original quote on the right. Click opens the specchietto. Each row has **Open in document viewer**. **No** Flagged · N%. Tag is leverage / weak-for-you (or equivalent) with a legend — not unexplained Medium/Low/High. |

---

## 9. Traceability

| This file | Source |
|---|---|
| NW-01…04, N1–N3 | spec §3.2, §20 Day 1; `workspaceStore.ts` / `memberStore.ts` |
| NW-58, N3b | invite is POST-only, no email; 2026-09-10 |
| NW-09, N8 | picker `contractCount: 0` |
| NW-14, N9 | `dev` 403 on DELETE/reprocess, 2026-09-10; `WorkspaceRoleResolver` |
| NW-05…08 | ADR-010, ADR-022 |
| NW-10…13 | `documentStore.ts`, `renewalActionStore.ts`, `quoteOutcomeStore.ts` |
| NW-20…26 | `backend/README.md` honest gaps |
| NW-62, N16 | Contract 360 empty save/move + clause dump, 2026-09-10 |
| NW-63, N17 | 360 document viewer + editable OCR highlights |
| NW-64, N18 | Fields OCR missed hidden (Review skip + Details ▾); fill-in section |
| NW-65, N19 | Details Flagged dump + empty left column, 2026-09-10 |
| NW-66, N20 | Why-row original quote + unexplained Medium/Flagged % |
| NW-55, N10 | Ask citation cards, 2026-09-10; `CitationCard.tsx` placeholder |
| NW-56, N11 | Contract 360 Ask about it → Ask-off “upload”, 2026-09-10 |
| NW-57, N12 | Quote check unclear / manual savings / Home gone; history + Ask |
| NW-59, N13 | Ask abstain dead-end, 2026-09-10; `ReplyBody` abstain has no actions |
| NW-60, N14 | Global Ask bar on open chat no-ops; hide on `/ask` |
| NW-27, NW-61, N15 | Upload feels instant; POST no longer waits on ProcessAsync; no incomplete detail |
