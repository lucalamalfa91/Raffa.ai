# Raffa — next-waves input · SaaS Readiness & Customer Infra Integration

Status: **binding input** for a next-wave requirements document. Written
2026-09-25 by the founders, revised same day after a founders' review of the
first draft. Two corrections from that review are now load-bearing and are
called out explicitly in §0 and §1: **data residency is a hard non-goal**
(no customer ever hosts or receives a copy of Raffa's data), and
**conversational connectors (Teams first) are now `must`, not a later
idea**. A third round moved everything that only matters once the founders
decide to sell to paying customers into a companion file — see §5.

IDs are new and stable (`SR-nn`); they do not collide with `NW-01…NW-97` or
`CS-01…CS-10`. **SR-03, SR-06, SR-08 and SR-10 from the first draft are no
longer in this file** — moved verbatim to
`inputs/next/2026-09-25-production-readiness-deferred.md`, not dropped.

| | |
|---|---|
| Companion input (support) | `inputs/next/2026-09-25-customer-success-concierge.md` (CS-01…CS-10) |
| Companion input (deferred) | `inputs/next/2026-09-25-production-readiness-deferred.md` — hold until the founders decide to sell beyond pilots |
| Product oracles | `inputs/product-spec.md` §3 (tenancy), §13 (API, events, integration priority P1/P2/P3), §14 (security/privacy/governance) |
| ADRs in force | ADR-009 (RLS), ADR-010 (Entra OIDC — the finding this file starts from), ADR-011 (secrets/RAG isolation, no training on customer content, audit) |
| Verified today (2026-09-25, this checkout) | `infra/modules/identity/main.tf:74,148` — `sign_in_audience = "AzureADMyOrg"` (single-tenant app registration); ADR-010 §2.2 — every customer user is a B2B guest inside Raffa's own Entra directory; no SAML/SCIM anywhere; every `backend/src/*/*.csproj` NuGet list has zero integration SDKs (no Teams/Slack/Bot Framework, no SharePoint/Drive SDK); the documented domain event catalogue (spec §13.2, Appendix B) is never actually published; UI chrome is English-only, no i18n library |

## 0. The direct answer, revised

**For a pilot or demo with a real prospect**, the blocker is identity and
connectors, and this file closes it: a prospect's own users should be able
to sign in with their own corporate identity and reach Raffa — including
through Teams, where their procurement conversation already happens — while
every byte of their data still lives only in Raffa's own Azure tenant.
**For actually selling and scaling to paying customers**, production
infrastructure, a trust package and API-key access are also needed, but
those are consciously **not** in this wave — you said you are not selling
yet, only getting ready to. They are tracked, ranked, and ready to pick up
the day that decision changes; see the companion deferred file.

## 1. Binding instructions

1. **Data residency is not negotiable and is not "integration."** Every
   document, every extracted fact, every embedding, the market corpus, the
   audit log — all of it lives exclusively in Raffa's own Azure tenant
   (North Europe), in Raffa's own RLS-isolated Postgres and Blob Storage,
   for every customer, always. "Integrate Raffa into a new customer's
   infra" in this file means exactly one thing: **their users can sign in
   with their own identity, and their systems can push data to Raffa or
   pull/receive data from Raffa through a defined API or connector.** It
   never means a self-hosted or on-premise Raffa, a database or storage
   account inside the customer's own cloud subscription, or a canonical
   copy of tenant data living anywhere but Raffa's own infra. Every item
   below is written to this rule; SR-01 states it as an explicit
   acceptance criterion because it is the item most at risk of being
   misread as "deploy into their environment."
2. **A connector copies, never references in place.** SR-04's SharePoint,
   Drive and Teams connectors pull a file into Raffa's own object storage
   and run the normal admission-gate pipeline on the copy. Raffa never
   leaves the canonical copy of a document on a system it does not own,
   and never indexes a customer's SharePoint or Teams tenant in place.
3. **Conversational connectors are channels onto the existing Ask engine,
   never a second brain.** Teams (and later Slack) call the same
   `Raffa.Chat` pipeline — DomainGate, IntentPlanner, grounding guards,
   citations — that the web app calls. A channel adapter renders the same
   reply object; it must never reimplement retrieval, grounding or
   negotiation logic of its own (ADR-024's engine stays the one engine).
4. **Every new external surface goes through an adapter behind an
   interface**, exactly as SR-04's connectors and SR-11's Teams channel are
   specified below — never a direct SDK call wired straight into
   `Raffa.Api` or `Raffa.Worker`.
5. **RLS and the no-cross-tenant rule are not renegotiated for any
   integration** (ADR-009, ADR-011). A connector or a Teams conversation
   resolves to exactly one tenant before it touches any data.
6. **Out of scope (do not queue):** anything requiring a production
   environment (see the deferred file); self-hosted/on-premise deployment
   of Raffa in any form (ruled out permanently by instruction 1, not just
   deferred); autonomous posting into a Teams/Slack channel the bot was not
   summoned into; negotiation or email-sending from inside a chat
   connector (that is CS/negotiation-agent scope, tracked elsewhere).

## 2. Items (priority order)

### SR-01 — Customer IdP federation: identity only, data always stays in Raffa's own infra (must)

- **Today (evidence):** `infra/modules/identity/main.tf:74,148` —
  `sign_in_audience = "AzureADMyOrg"`; ADR-010 §2.2 confirms every
  workspace's users live as B2B guests in the **one** Entra directory Raffa
  owns. `tid` in the token is Raffa's own directory GUID, never read for
  any purpose. A customer's IT admin has no control over who exists in
  Raffa; every account is provisioned by `GraphGuestProvisioner.cs`.
- **What it should be:** Raffa's Entra app registration becomes
  **multi-tenant**, so a customer's own Entra tenant can consent to Raffa
  and its users sign in with their own corporate identity — no guest
  invite, no second account. `tid` becomes the signal that distinguishes
  **which customer directory** a sign-in came from. Nothing about where
  data lives changes: the customer's directory only ever authenticates
  their users; Raffa's own Postgres and Blob Storage remain the sole
  location of every document, fact and embedding, exactly as today.
  Existing guest-provisioned workspaces keep working (migration path, not
  a breaking change).
- **Acceptance:**
  - a second, throwaway Entra tenant can consent to the Raffa app and its
    user signs in without ever receiving a B2B invite email;
  - `tid` correctly separates two customer directories in the same `dev`
    environment;
  - the existing guest flow still works for workspaces that have not
    migrated;
  - **data-residency check (explicit):** after this item, grep and manual
    review confirm no code path writes a document, an extracted fact, an
    embedding or a database row to any destination outside Raffa's own
    Azure subscription; the only thing that ever crosses into a customer's
    own systems is an identity token (SR-01/SR-02), an API response the
    customer's own system requested (SR-06, deferred file), or a webhook
    payload Raffa sends out (SR-05) — never a copy of the underlying store.
- **Seats (hint):** security-architect (owns this call — ADR-010's own
  author, and the data-residency acceptance criterion), cloud-architect
  (app registration change), software-architect (workspace resolution
  from `tid`).

### SR-02 — SAML 2.0 and SCIM for customers not on Entra (must)

- **What it should be:** a customer on Okta, Google Workspace, OneLogin or
  any SAML 2.0 IdP federates without being on Microsoft Entra at all. SCIM
  2.0 (provisioning/deprovisioning pushed from the customer's IdP) means an
  employee who leaves the customer company loses Raffa access without an
  Admin remembering to do it by hand. Same data-residency rule as SR-01:
  SCIM carries user identity attributes only, never tenant content.
- **Acceptance:** a SAML test IdP (e.g. a free Okta developer org)
  federates a sign-in; deactivating a user in that IdP revokes their Raffa
  access within the SCIM sync window.
- **Seats (hint):** security-architect, cloud-architect, software-architect.

### SR-03 — Deleted from this file

See `inputs/next/2026-09-25-production-readiness-deferred.md` (originally
SR-03, production environment). Held until the founders decide to sell.

### SR-04 — Document ingestion connectors: email, SharePoint, OneDrive, Google Drive (must)

- **What it should be:** one `Raffa.Integrations` module with a
  provider-agnostic seam (mirroring `Raffa.Benchmark`'s adapter pattern),
  and three concrete adapters:
  1. **Email ingestion**: a per-workspace inbound address
     (`workspace-slug@intake.raffa.ai`) that runs the same admission gate
     as a manual upload.
  2. **SharePoint / OneDrive connector**, read access to a chosen folder,
     polling on a schedule; every new file is **copied** into Raffa's own
     storage and run through the normal pipeline (instruction 2 above —
     never indexed in place, never left as the canonical copy on the
     customer's tenant).
  3. **Google Drive connector**, same shape.
  Every connector produces a `Document` row through the existing
  pipeline — no parallel ingestion path, no special-cased extraction.
- **Acceptance:** a contract forwarded by email on `dev` appears in
  Documents within the normal processing time; a file dropped in a
  connected SharePoint folder appears within one polling cycle and a
  second look at the customer's SharePoint confirms Raffa left the
  original file untouched, reading a copy only; a non-contract forwarded
  by email is rejected by the same admission gate that rejects a manually
  uploaded one.
- **Seats (hint):** software-architect, cloud-architect (Graph/Drive API,
  secrets), security-architect (scoped read-only grants, never full
  mailbox or drive access).

### SR-11 — Ask Raffa inside Microsoft Teams: chat and upload from the tool procurement already lives in (must)

- **Why this is `must`, not `should`:** flagged directly by the founders
  as the single most-requested integration shape — procurement and finance
  conversations already happen in Teams; a contract-intelligence assistant
  that only lives on a separate web app misses the moment a question is
  actually asked.
- **What it should be:** a Microsoft Teams app (Azure Bot Service / Bot
  Framework, or the current Teams AI-agent pattern) that is a **channel
  adapter onto the existing Ask engine** (instruction 3), not a new brain:
  - **Chat.** A user DMs the Raffa app in Teams, or @-mentions it in a
    channel it was added to, and gets the same grounded reply Ask gives on
    the web — same DomainGate, same intent planner, same grounding and
    numeric guards, same abstain behaviour. Citations render as Adaptive
    Cards with a working deep link back into the web app's viewer; where
    Teams cannot render a rich card, a clean text fallback with the link
    is used. Never a degraded, less-grounded "Teams-only" answer.
  - **Upload.** Dragging a file into the DM with the bot, or a Teams
    message action on a file already in a channel, runs it through the
    exact same admission gate and pipeline as a Documents-screen upload —
    the file is copied into Raffa's own storage (instruction 2), it does
    not stay referenced inside the customer's Teams/SharePoint.
  - **Identity, never anonymous.** A Teams user must resolve to a known
    Raffa user in a known workspace before any data is touched. On first
    contact with the bot, an unlinked user gets a one-time "connect your
    Raffa account" flow (ideally riding SR-01's federation, so a user
    already signed into Teams with their corporate identity needs no
    second login); until linked, the bot answers nothing tenant-specific.
  - **Never proactive.** The bot only replies when addressed (DM or
    explicit @-mention); it never posts unprompted into a channel in this
    wave (a future proactive-notification item, if wanted, is a separate
    decision — it would overlap CS-07's proactive triggers and should be
    scoped together with that file, not assumed here).
- **Acceptance:** a linked Teams user DMs "quando scade il contratto
  Salesforce?" and receives the same answer, with the same citation, that
  Ask gives on `dev`'s web app for the identical question; dropping a PDF
  into that DM creates a `Document` row that appears in Documents and
  processes normally; an unlinked Teams user gets the connect-account
  prompt and no tenant data in the reply; a channel the bot was added to
  but not @-mentioned in receives no message from it.
- **Seats (hint):** software-architect (Teams/Bot Framework as a channel
  adapter over the existing `AskCopilotService`, not a parallel
  implementation), client-architect (Adaptive Card mapping from the
  existing reply-kind shapes), ux-ui-designer (card design, fallback
  text), security-architect (account-linking flow, no data before
  linking).

### SR-12 — Slack, same pattern as SR-11 (should)

- **What it should be:** once SR-11 proves the channel-adapter pattern,
  Slack is the same adapter shape (chat + upload + identity linking, never
  proactive) for customers on Slack instead of Teams. Lower priority than
  Teams because the founders named Teams as the immediate demand signal;
  ranked `should` so it does not compete with SR-11 for the same wave
  unless capacity allows both.
- **Acceptance:** same shape as SR-11's acceptance, on Slack.
- **Seats (hint):** same as SR-11; largely a reuse of the adapter interface
  SR-11 defines.

### SR-05 — Domain events actually emitted, plus outbound webhooks (should)

- **Today (evidence):** spec §13.2 / Appendix B document nine domain
  events; none is published anywhere in the codebase.
- **What it should be:** the documented events are published on a Service
  Bus topic (reusing the pattern already proven for `extraction-events`),
  and a per-workspace outbound webhook registration (Admin-configured URL
  + shared secret, signed payloads, tenant-scoped, retried with backoff)
  lets a customer's own systems react to `renewal.approaching`,
  `savings.opportunity.created` and `document.processed` without polling.
- **Acceptance:** registering a webhook URL on `dev` and forcing a renewal
  into the 90-day window delivers a signed `renewal.approaching` payload
  within one scheduler tick; a webhook that 500s three times is disabled
  with an Admin-visible reason, never retried forever.
- **Seats (hint):** software-architect, security-architect (payload
  signing, no tenant data to a misconfigured URL), cloud-architect.

### SR-07 — Audit log, visible to the workspace Admin (should)

- **Today (evidence):** `GET /api/audit` exists, Admin-only, capped at 200
  rows, no UI (ADR-001 w16 cl. 5 ruled it out for that wave).
- **What it should be:** a Workspace & members → Audit screen: filterable
  by actor, action, date range, paginated past the current 200-row cap.
- **Acceptance:** deleting a document and inviting a member both appear in
  the Admin's audit screen within one page load, correctly attributed.
- **Seats (hint):** client-architect, ux-ui-designer, software-architect.

### SR-09 — UI chrome in five languages: English, Italian, German, French, Spanish (should)

- **Today:** no i18n library; UI chrome is hard-coded English
  (`web/package.json` has no i18n dependency; `index.html lang="en"`).
- **What it should be:** UI chrome (labels, buttons, empty states, InfoTip
  copy) in English, Italian, German, French and Spanish, selected from the
  workspace's country or a per-user preference. This item is the
  surrounding chrome only; Ask's own language behaviour is SR-12b below.
- **Acceptance:** a workspace created with country `IT` shows Italian
  chrome by default; a user can switch to any of the five; no string in
  the pilot-walk screens is left hard-coded English once a non-English
  language is selected.
- **Seats (hint):** client-architect, ux-ui-designer, product-owner
  (translation review — accurate, not machine-literal, for all five).

### SR-13 — Ask Raffa's own language matching extended to five languages (should)

- **Today (evidence):** document extraction prompts already claim "any
  language" and read Italian/German source contracts correctly
  (`ExtractPromptTemplate.cs`, `ClassifyPromptTemplate.cs`). But the
  **deterministic** layers that shape Ask's reply — `QuestionLanguage.cs`,
  the DomainGate's keyword lexicons, the capability-gap catalog's wording,
  the interview and feedback-card copy — are IT/EN only. A French- or
  Spanish- or German-speaking user today gets English replies from those
  deterministic paths even though the `answer` persona itself could handle
  their language.
- **What it should be:** every deterministic lexicon and hard-coded reply
  string Ask uses (gate labels' warm decline, the capability-gap catalog's
  five entries, the interview block, the feedback card, the gap-preface
  text) gains DE/FR/ES entries alongside the existing IT/EN, on the same
  tie-break rule (`QuestionLanguage.cs`'s "ties go to English" is
  unchanged, just with five candidates instead of two).
- **Acceptance:** the golden set gains German, French and Spanish variants
  of the existing IT/EN off-domain, legal-refusal and capability-gap cases;
  each responds in the question's own language, never falling back to
  English mid-conversation.
- **Seats (hint):** software-architect, product-owner (accurate copy in
  all five, same review discipline as SR-09).

## 3. Order constraints

- SR-01 before SR-02 (federation model settled before a second protocol on
  top of it).
- SR-11's Teams identity-linking step is stronger and simpler once SR-01
  exists (a Teams user already signed in with their corporate Entra
  identity needs no separate linking step), but SR-11 does not strictly
  require SR-01 to ship first — a manual one-time linking flow works
  without it. If capacity allows only one of the two in the same wave,
  SR-01 goes first; SR-11's manual-linking fallback is acceptable for one
  wave, not a permanent shape.
- SR-04 does not depend on SR-01/SR-02.
- SR-12 (Slack) depends on SR-11 shipping first (reuses its adapter shape).
- SR-09 and SR-13 can run in parallel with everything else.
- If the wave cap is reached: SR-01 and SR-11 are never pushed past the
  next wave — they are the two items the founders named as the actual
  demand signal. SR-12 and SR-13 are the first to move to the following
  wave if something must give.

## 4. Cancels / touches

- Cancels nothing from `next-waves-todo.md` or other prior inputs.
- **Supersedes the first draft of this same file** (same day, 2026-09-25):
  the original SR-01 acceptance criteria are replaced by the data-residency
  version above; the original SR-04 is unchanged in substance but now
  cites the data-residency rule explicitly; SR-03, SR-06, SR-08 and SR-10
  are moved out, not cancelled — see the companion deferred file, which
  restates them verbatim with a pointer back here.
- Extends ADR-010 (federation model — the security-architect who owns
  ADR-010 owns SR-01/SR-02's decision, including the data-residency
  acceptance criterion). Touches spec §13.4's integration priority table
  (SharePoint/Drive move into this wave instead of "later"; Teams/Slack as
  a conversational surface is new — not in the original spec's table at
  all, added by this file).
- Does not touch `inputs/next/2026-09-25-customer-success-concierge.md`.

## 5. Companion file

`inputs/next/2026-09-25-production-readiness-deferred.md` holds the four
items moved out of this file (production environment, public API docs +
keys, the trust package, usage/cost visibility) plus the binding
instructions that only apply to them. Nothing in that file is queued for
any wave until the founders say so explicitly in a future round.

## 6. Acceptance walk (for `docs/waves/<wave>-acceptance.md`)

1. A throwaway external Entra tenant's user signs into a `dev` workspace
   without ever receiving a guest invite email; a review of Raffa's own
   Azure subscription confirms no data moved anywhere outside it.
2. That same workspace's Admin deactivates the user in their own IdP; the
   user's Raffa access is gone within the sync window.
3. Forward a contract PDF to the workspace's intake email address; it
   appears in Documents, processes, and is askable.
4. Connect a SharePoint test folder; drop a file in it; it appears in
   Documents within one poll; the original file is untouched in
   SharePoint, only a copy was pulled.
5. In Teams, DM the linked bot "quando scade il contratto Salesforce?" and
   get the same grounded answer, with a working citation link, that the
   web app gives for the same question; drop a PDF into that same DM and
   watch it appear in Documents.
6. An unlinked Teams user gets the connect-account prompt, never tenant
   data.
7. Register a webhook URL; force a contract into the 90-day renewal
   window; the URL receives a signed `renewal.approaching` payload.
8. Open the Audit screen as Admin; delete a document; see the row.
9. Switch a workspace through all five UI languages; ask Ask a question in
   German and in French and get a reply in that language, not English.
