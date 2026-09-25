# Raffa — next-waves input · Customer Success Concierge

Status: **binding input** for the next wave's requirements document. Written
2026-09-25 by the founders after the Vertice competitive review (Claude Doc
"Raffa.ai vs Vertice — analisi competitiva e proposte", §"Domande aperte").
IDs are new and stable (`CS-nn`); they do not collide with `NW-01…NW-97`.
The intake re-audits *status today* against this checkout; nothing here is
already built.

| | |
|---|---|
| Previous wave | w20 (epic-30…32, Ask Q1/Q2/Q3) |
| Product oracles | `inputs/product-spec.md` §3 (roles), §13.3 (background jobs, email delivery), §14 (security, AI privacy, audit), §15.1 (telemetry); `inputs/requirements.md` R-ASK-02 (domain gate labels), R-CONV-01 (conversations); `inputs/percorso-pilota-v1.md` §2 (onboarding) |
| ADRs in force | ADR-009 (RLS), ADR-010 (identity), ADR-011 (audit carries outcomes, never content), ADR-024 (Ask engine, reply kinds), ADR-030 (gap catalog, interview, GitHub issue), ADR-031 (capability investigator, human-approved issues), ADR-032 (web-search toggle, Admin settings) |
| Competitor reference | Vertice: customer success dedicated per contract, help center (`help.vertice.one`), community, in-app support. G2 praise: "responsive CS/onboarding". G2 complaint: "user guides are missing". We are not copying the human-per-contract model; we are building the concierge that makes one human enough for a hundred workspaces |

## 0. Why (the problem, in one paragraph)

The chat → GitHub-issue loop (ADR-030/031) turns a **missing feature** into
backlog. It does nothing for a **stuck user**: a Procurement member whose PDF
sits in `Uploaded` for ten minutes, an Admin whose invitation mail never
arrived, a buyer who cannot find where a saving went. Today that user has no
button, no address, no answer, and Raffa has no record that it happened. A
SaaS has support. Ours is a **concierge**: Ask Raffa recognises "I am stuck"
turns, solves the ones the system can solve itself (status, retry, where is
X, how do I), escalates the rest to a human with the full context already
attached, and the human answers **inside the product**, not by email.

## 1. Binding instructions

1. **The concierge is Ask Raffa, not a second chatbot.** Same engine, same
   composer, same conversations (R-CONV-01). A new gate label and a new reply
   kind; no widget from a third-party support vendor, no iframe.
2. **Self-service first, human second.** A support turn escalates to a human
   only when the deterministic resolvers and the capability catalog cannot
   answer. Escalation is never the first reply.
3. **Content never leaves the tenant.** A ticket carries outcomes, ids,
   statuses, hashes, screen and route — never contract text, never chat text
   the user did not explicitly attach (ADR-011). The public GitHub loop is
   **not** the ticket channel: tickets are private, in Postgres, under RLS.
4. **One human is enough.** The operator console (`Raffa.Tools`) gains a
   support queue; a founder answers from there. No new SaaS subscription for
   the queue in this wave; the seam allows one later.
5. **No SLA promise in copy that the system does not enforce.** If the
   product says "we answer within 4 business hours", a scheduler must measure
   it and surface breaches. Otherwise the copy says "a person will answer".
6. **Persistence rule unchanged:** tickets, replies, status, help articles
   read back from Postgres from any browser (RLS).
7. **Out of scope (do not queue):** live chat with a human in real time,
   phone, chatbot for prospects on a marketing site, customer health scoring,
   CSAT surveys beyond one thumbs up/down, multi-language help articles
   beyond IT + EN.

## 2. Items (priority order)

### CS-01 — Ask recognises a "stuck" turn (must)

- **What you see:** "il mio PDF è fermo da dieci minuti", "non ho ricevuto
  l'invito", "dove è finito il saving di Zoom?", "come faccio a cancellare un
  documento?" fall into `in_domain` or `off_domain` and get a portfolio hook
  or an abstain. Nobody learns the user was stuck.
- **What it should be:** a new DomainGate label `support` (deterministic
  IT/EN lexicon first, `classify` fallback like the other labels, R-ASK-02),
  planned into sub-intents: `document_stuck`, `invite_missing`,
  `where_is` (a screen, a contract, a saving, a chat), `how_do_i`,
  `something_broke` (error shown), `account_access` (role, workspace,
  sign-in). The capability investigator (ADR-031) keeps running beside it;
  a `support` turn is never a feature gap.
- **Where:** `Raffa.Chat` DomainGate + IntentPlanner; golden set gains
  ≥ 12 support cases IT/EN with zero false positives on the existing
  in-domain cases.
- **Acceptance:** the 12 golden cases route to `support` on the fixture
  gateway; "quali contratti scadono nel 2026?" still routes to the planner;
  audit records `ask.support.detected` with sub-intent, never the text.
- **Seats (hint):** software-architect, product-owner, security-architect
  (what the audit row may carry).

### CS-02 — Deterministic resolvers answer without a human (must)

- **What it should be:** each sub-intent has a server-side resolver that
  reads system state the user is entitled to and answers with a `support`
  reply kind (prose + one card + one action), no model call for the facts:
  - `document_stuck`: the document's real stage, time in stage, whether the
    hung-job retry (3 / 15 min, README) already fired, and an action
    **Reprocess now** (Admin) or **Ask an Admin to reprocess** (Procurement).
  - `invite_missing`: invitation status (Invited / Expired / Accepted), sent
    at, and actions **Resend** / **Copy link** (Admin only; Procurement gets
    "ask your Admin" with the Admin's name).
  - `where_is`: resolves the object by name against the tenant (supplier,
    contract, saving, chat title) and deep-links to it; "not found" says what
    was searched, never invents.
  - `how_do_i`: answers from the help articles (CS-05) with a citation to the
    article, else from the capability catalog (`GET /api/capabilities`).
  - `something_broke` / `account_access`: collects the facts (route, last
    API error code from the client, role, workspace) and offers **Send to a
    person** (CS-03).
- **Acceptance:** on `dev`, with a document forced into `Processing` by the
  fixture, "il mio documento è fermo" returns the stage and a Reprocess
  action that works; "non è arrivato l'invito a mario@…" returns the
  invitation row and Resend; both replies show `corpus: raffa` cards, no
  tenant text in the audit.
- **Seats (hint):** software-architect, client-architect, ux-ui-designer.

### CS-03 — Escalation to a person: the ticket (must)

- **What it should be:** tables `support_ticket` and `support_message`
  (tenant-scoped, RLS, keyed by tenant + user like `conversation`). A ticket
  is opened from a `support` reply's **Send to a person** action, or by the
  user writing "voglio parlare con una persona". The card asks at most two
  questions (what were you trying to do; may we look at this document / this
  chat? — explicit opt-in per object, default **no**). The ticket carries:
  workspace id, user id and role, route, sub-intent, object ids the user
  opted in, client error code, browser, timestamp. Never contract text,
  never the conversation transcript.
- **Status:** `open → answered → closed` (+ `reopened`). The user sees the
  ticket inside the same Ask conversation as a pinned card; a human reply
  arrives as a new message in that conversation, with a reply-ready notice
  (the parallel-session notifier already exists).
- **API:** `POST /api/support/tickets`, `GET /api/support/tickets`,
  `GET/POST /api/support/tickets/{id}/messages`, `PATCH …/{id}` (close,
  reopen). OpenAPI hand-authored gaps are **not** acceptable (NW-30 lesson).
- **Acceptance:** Procurement opens a ticket; Admin of the same workspace
  sees it in Workspace & members → Support (read-only list, CS-06); another
  tenant cannot read it even with the id (RLS test); audit has
  `support.ticket.opened` with outcome only.
- **Seats (hint):** software-architect, security-architect (opt-in scope,
  what a human may see), client-architect, ux-ui-designer.

### CS-04 — Operator queue in `Raffa.Tools` and email notification (must)

- **What it should be:** the operator console lists open tickets across
  tenants (operator identity, not a tenant member; same posture as bulk
  reprocess), shows the context the ticket carries, opens the opted-in
  objects **read-only** through the existing tenant-scoped endpoints with
  the tenant GUC set to that tenant and an audit row `support.operator.viewed`
  per object. The operator answers; the reply lands in the user's Ask
  conversation (CS-03). ACS mail sends **one** notification to the operator
  mailbox on open and **one** to the user on answer ("Raffa has answered your
  request" + deep link; no content in the mail). This is the second mail
  type after the invitation; ADR-001 w15 cl. 6 is amended, not a
  notification framework.
- **Acceptance:** a ticket opened on `dev` produces one operator mail; the
  operator's reply appears in the user's chat within one poll; the user's
  mail carries no ticket text; every operator object view is audited.
- **Seats (hint):** cloud-architect (ACS second template, operator
  identity), software-architect, security-architect.

### CS-05 — Help articles as a cited corpus (should)

- **What it should be:** a versioned, static, IT + EN help corpus in the API
  (like the capability catalog): one article per screen guide and per
  known-stuck situation (upload formats and limits, statuses and retries,
  review and validation, invitations, roles, delete-all, web search
  opt-in). The InfoTip copy (`infoTipCopy.ts`) is the seed; the article is
  the long form. `how_do_i` cites the article (`corpus: raffa`, card opens
  the article in the artifact panel already used for drafts). A public
  `/help` route renders the same corpus for non-signed-in users.
- **Acceptance:** "come cancello tutti i documenti?" answers with the
  article card and the **Documents** action; the same article is reachable
  at `/help/documents` unauthenticated; Italian question → Italian article.
- **Seats (hint):** product-owner (writes the articles), client-architect,
  ux-ui-designer.

### CS-06 — Support visible to the workspace Admin (should)

- **What it should be:** Workspace & members gains a **Support** section:
  the workspace's tickets (who, when, status, sub-intent), no message
  bodies of other members unless the ticket author opted in. Admin can close
  a ticket on behalf of a member. Procurement sees only their own.
- **Acceptance:** two members, two tickets; Admin sees both rows; each
  member sees one.
- **Seats (hint):** client-architect, ux-ui-designer, security-architect.

### CS-07 — Proactive concierge: the system opens the conversation (should)

- **What it should be:** three triggers, all deterministic, all writing a
  system message into a **new** Ask conversation titled by the system
  (never a mail with content):
  1. a document in `Uploaded` or `Processing` beyond the retry windows
     **and** a failed retry → "Your <file> could not be processed; here is
     why we know and what to do";
  2. an invitation expired unaccepted after 7 days → to the inviting Admin;
  3. a workspace with ≥ 1 validated contract and no sign-in for 21 days →
     to the Admin, once, "here is what changed while you were away"
     (counts only: renewals entering 90 days, savings verified).
- **Acceptance:** each trigger has a scheduler test with a fake clock; a
  trigger never fires twice for the same object; the conversation is
  private to the addressed user (R-CONV-01).
- **Seats (hint):** software-architect (scheduler, uses the renewal
  scheduler's daily host), product-owner (copy), security-architect.

### CS-08 — One question after resolution (could)

- **What it should be:** after a `support` reply with a resolver action, and
  after a human answer, the card offers **Did this solve it?** thumbs
  up/down (reuses the feedback card of ADR-030). Down on a resolver reply
  offers **Send to a person**. Counts land in the operator console: solved
  by system / solved by human / unsolved, per sub-intent, per week.
- **Acceptance:** thumbs are persisted per message; operator console shows
  the weekly split.
- **Seats (hint):** client-architect, software-architect.

### CS-10 — Raffa mascot: an animated nudge into the concierge (should)

- **What you see today:** the concierge (CS-01…CS-07) only answers once the
  user already typed into Ask. A stuck user who never opens the composer —
  staring at a stalled upload, an empty Renewals list, an error toast — gets
  nothing. Nothing on screen ever *offers* help; the user has to think to
  ask for it.
- **What it should be:** a small animated mascot (the Raffa mark, not a new
  brand asset — reuse the square mark already in the Ask bar per the design
  brief) that appears as a **corner bubble**, never a modal, never blocking
  the screen underneath. It has exactly two jobs: **surface a support
  opportunity**, and **hand off to Ask** — it never answers anything itself,
  never opens its own text box, never calls the AI gateway directly. All
  intelligence stays in Ask (CS-01/CS-02); the mascot is a trigger, not a
  second assistant.
  - **Reactive appearance** (system-detected friction, client-side only,
    no new AI call): a document stuck in `Processing`/`Uploaded` past the
    existing retry windows (reusing CS-02's `document_stuck` state, polled
    by the Documents screen that already tracks it); an API call returning
    a client error the user did not dismiss within ~10s; three failed
    attempts at the same action (e.g. quote SKU mapping); an empty state
    the user has stared at for over 45s with no navigation (Renewals,
    Savings, Portfolio with zero rows). Each trigger maps 1:1 to a CS-02
    sub-intent — the mascot never invents a reason to appear that the
    concierge cannot resolve.
  - **Proactive appearance** (server-computed, reuses CS-07's three
    triggers): on sign-in, if CS-07 has a system message waiting, the
    mascot appears once with a one-line preview ("Il tuo documento X non è
    passato, vuoi vedere perché?") instead of silently dropping the message
    into a new conversation the user has to notice in the rail.
  - **The bubble copy is always the same shape**: one short sentence
    naming the *specific* thing it noticed (never "Do you need help?" with
    no context — that trains the user to dismiss it), one primary button
    **Chiedi a Raffa** and one dismiss (×, no "don't show again" nagging,
    but a per-object cooldown: the same document/screen does not re-trigger
    for 30 minutes after a dismiss).
  - **The handoff**: clicking the primary button opens Ask in a **new
    conversation** (per the global Ask bar's existing "always opens a new
    chat" rule) with the matching CS-02 resolver already invoked — the user
    lands on the answer (stage, reason, action), not on an empty composer
    they have to re-explain themselves into. This is the one hard
    requirement: the mascot must never be a decorative dead end that opens
    Ask empty.
  - **Animation**: a light entrance (slide/fade, ≤300ms), a subtle idle
    breathing loop while the bubble is open, no sound, respects
    `prefers-reduced-motion` (static fade only). Built as a client-side
    component (`web/src/components/concierge-mascot/`), no new backend
    surface beyond what CS-02/CS-07 already expose — this item is UI-only
    wiring plus the 45s-empty-state and click-error client detectors.
  - **Frequency discipline**: at most one mascot bubble on screen at a
    time; at most 3 reactive appearances per session; never on `/ask`
    itself (Ask is already open); never during onboarding's first empty
    state (percorso-pilota-v1.md §2 — "Carica → Elabora → Chiedi" owns that
    moment, the mascot would compete with it). An Admin can turn the
    reactive mascot off entirely per workspace (Workspace & members
    setting, alongside the existing web-search toggle) — some users find a
    proactive avatar intrusive and the setting costs one boolean.
- **Acceptance:** force a document into `Processing` past the retry window
  on `dev` → mascot bubble appears within one poll cycle, names the file,
  **Chiedi a Raffa** opens a new Ask conversation already showing the
  `document_stuck` resolver's answer; dismissing it suppresses that same
  document's bubble for 30 minutes but not other documents'; toggling the
  Admin setting off removes all reactive bubbles for that workspace on
  next load; `prefers-reduced-motion` gives a static, non-breathing bubble;
  no more than one bubble is ever visible at once across three simultaneous
  triggers.
- **Seats (hint):** ux-ui-designer (the asset, motion spec, copy shape),
  client-architect (detectors, cooldown state, the Admin toggle wiring),
  product-owner (which triggers are worth it — cut any that nag in testing).

### CS-09 — Response-time measurement before any promise (could)

- **What it should be:** the scheduler records time-to-first-human-reply per
  ticket; the operator console shows the median and the max for the last 30
  days. Product copy may state a response time only when this exists and
  the last 30 days meet it; until then the card says "a person will answer".
- **Acceptance:** copy on `dev` has no numeric promise; the console shows
  the two numbers.
- **Seats (hint):** product-owner, software-architect.

## 3. Order constraints

- CS-01 before CS-02 before CS-03 before CS-04 (each consumes the previous).
- CS-05 can run in parallel with CS-02 (the `how_do_i` resolver consumes it).
- CS-06, CS-07, CS-08, CS-09 after CS-04.
- CS-10 depends on CS-02 (its reactive triggers reuse the resolver states)
  and on CS-07 (its proactive appearance previews CS-07's system messages).
  It does not depend on CS-03/CS-04/CS-06 — the mascot only ever routes into
  Ask, never opens a ticket directly.
- If the wave cap is reached, CS-07…CS-10 become the **head of the next
  wave**, never dropped. If only one of CS-07/CS-10 fits, CS-07 goes first —
  the mascot without proactive messages to preview is a weaker version of
  itself, not a broken one.

## 4. Cancels / touches

- Cancels nothing. Extends ADR-030 (a `support` turn is not a gap; the
  feedback card is reused) and ADR-031 (the investigator ignores `support`
  turns). Amends ADR-001 w15 cl. 6 (second mail type: support notification).
- Touches `renewalActions.ts` "soon" group: no change, but a "soon" click
  that the user then calls "non funziona" is a `support` turn, not a gap.

## 5. Acceptance walk (for `docs/waves/<wave>-acceptance.md`)

1. Upload the password-protected fixture PDF → `Failed`. Ask "il mio pdf
   è fallito, perché?" → stage, reason, action **Upload a new file**.
2. Force a document into `Processing` → Ask "è fermo" → stage + time +
   **Reprocess now** (Admin) → row moves.
3. Invite `x@example.com`, wait for expiry (fake clock) → Admin gets the
   proactive conversation; Ask "non è arrivato l'invito" → **Resend**.
4. "come si fa a cancellare tutti i documenti?" → article card + action;
   `/help/documents` open without sign-in.
5. "voglio parlare con una persona" → two-question card → ticket; operator
   console shows it; operator replies; user sees the reply in the same
   chat and gets one mail without content; another tenant gets 404 on the
   ticket id.
6. Thumbs down on a resolver reply → **Send to a person** offered.
7. Force a document into `Processing` past the retry window → the mascot
   bubble appears naming the file → **Chiedi a Raffa** opens a new Ask
   conversation already showing the stage, reason and **Reprocess now** —
   never an empty composer. Dismiss it, force the same state again within
   30 minutes → no bubble. Toggle the Admin setting off → no bubble at all
   on next load, for any trigger.
