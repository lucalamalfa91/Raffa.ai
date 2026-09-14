# ADR-018 — Web information architecture (Day-1 sitemap, nav, roles)

- **Status**: accepted
- **Date**: 2026-09-04
- **Deciders**: ux-ui-designer (draft), product-owner (concur), council-close
- **Locked citations**: "Surface = web only; web/ in the monorepo"; "ADR-012 is locked (React + TS + Vite SPA, OIDC PKCE, SWA)"; "New execution slices start at wave/epic 6" (web-integration-brief §2); ADR-009 tenancy/RLS; ADR-010 Entra ID/OIDC. None of these re-opened.

## Context and problem statement

ADR-012 and BACKLOG already promise the SPA delivers the full user-visible
ladder as slices land, but E02–E05 were decomposed `layer: backend`
(`*-dashboard-api`); no screen was ever authored. The user outcomes in
product-spec §16 (definitions of success per release) and §20 (Definition of
V1 done) are not HTTP status codes — they are things a procurement user must be
able to *do in the browser*. This ADR fixes the information architecture (IA)
for the Day-1 web path: sitemap, primary navigation, role variants, and the
empty/error/loading contract, so the decomposer can attach every §16/§20 row to
a concrete route and screen.

The IA is authored in and sourced from the Claude Design export, not invented
as prose. The canonical sitemap/nav lives at `inputs/design/prototypes/ia.md`
and is implemented in `inputs/design/prototypes/day1-demo.html`. This ADR
commits to that structure and documents the roles and cross-links that the
decomposer must turn into `layer: web` stories.

## Decision drivers

- **Every §16 row + §20 step has a reachable screen** — "API exists" is not a
  delivered screen (web-integration-brief §6).
- **Two roles only** for V1 nav variation (Workspace Admin vs Procurement),
  matching the permission model already built in ADR-009/010; Legal/Finance/
  read-only remain model-level, not separate nav variants.
- **One clickable Day-1 path** that proves the end state end-to-end on `demo`
  in the browser.
- **Claude Design is the source of truth** — the IA is already-authored there;
  this ADR points at it, not beside it.

## Considered options

1. **Single left-rail IA centred on the R0→R4 ladder** (chosen; see below).
2. **Dashboard-portal IA** — one landing page aggregating widgets; deeper
   modules behind a secondary nav.
3. **Top-tab / module-switcher IA** — a horizontal module bar heavily influenced
   by the backend `*-dashboard` naming.

## Decision outcome

**Chosen: Option 1** — a single 224px left rail that lists the Day-1 capability
ladder in the order a procurement user meets it (Home → Portfolio → Renewals →
Ask Raffa → Quote check → Documents → Review queue → Workspace & members),
with a global Ask bar on every screen, and exactly two role variants.

The left rail maps **one nav item per §16 release surface plus the two R0/R1
support surfaces a user actually needs daily** (Documents = upload/status,
Review queue = correction). It puts the AI-native actions (Ask, Quote check) at
the user's fingertips rather than burying them behind a module switcher, and it
keeps `Home` as the savings KPI/opportunity landing so the north star ("what we
bought, what we pay, when to act, where to save") is the first thing seen.
Roles are a permission gate on two rail items, never a fork in the IA.

### Consequences

- **Good**: one stable route map the decomposer can map 1:1 to §16/§20; the
  prototype's cross-links already implement it, so no re-design is needed;
  Procurement vs Admin is a data/permission difference, not an IA fork.
- **Bad**: a single flat rail does not scale to a fully expanded CLM product —
  future modules (e.g. legal, finance workspaces) will need a grouping decision;
  accepted as a V1 non-goal (spec §1.2 excludes full CLM/authoring).
- **Neutral**: the rail is thin (224px) and flat; no collapse/accordion in V1.

## Pros and cons of the options

### Option 1 — single left-rail IA on the R0→R4 ladder
- Good: 1:1 with §16/§20; prototype already built; minimal IA surface for a
  3–4 person team; role = permission gate only.
- Bad: flat list may feel long; no future module grouping.

### Option 2 — dashboard-portal IA
- Good: familiar "portal" frame; aggregates KPIs first.
- Bad: adds a hub-and-spoke layer not in the prototype; risks re-introducing the
  `*-dashboard` anti-pattern at the shell level; more IA to build.

### Option 3 — top-tab module switcher
- Good: compresses nav vertically.
- Bad: mirrors backend module naming (`contract`, `renewal`, `savings`) rather
  than user jobs; the global Ask bar (a key §20 promise) fits poorly in a
  tab-switcher mental model.

## Route map (locked)

| Route | Screen | Primary object |
|---|---|---|
| /signin | Entra sign-in → workspace picker | Tenant |
| /workspace/members | Members & roles, invite (admin only) | User, Role |
| /documents | Upload + document list + status | Document |
| /contracts | Portfolio (attention strip, filters, table) | Contract |
| /contracts/:id | Contract 360 (10 tabs) | Contract + children |
| /contracts/:id/review | Field review / correction + evidence pane | Extraction, Correction |
| /ask | Ask Raffa — chat, citations, abstain | Query |
| /renewals | Pipeline: threshold strip, table, insight card | Renewal |
| / (home) | Savings KPIs + opportunities | SavingsOpportunity |
| /quotes/:id | Quote check stepper: Extract → Assessment → Target → Negotiation | Quote, NegotiationOutcome |

## Roles (Day-1)

- **Workspace Admin** — all routes; owns `/workspace/members` and invites.
- **Procurement** — all routes except member management (sees a
  "request access" / read-only state on that surface).
- Legal / Finance / read-only exist in the permission model (ADR-009/010) but
  are **not** separate nav variants in V1.

## Empty / error / loading (IA-level contract)

Every list/detail surface must implement the three states from
`design-system.md` §States: loading = skeleton rows (`--color-neutral-300`),
empty = h3 + one sentence + primary action (max 480px, left-aligned), error =
2px accent left rule + h4 + plain endpoint/job name + secondary Retry. The
decomposer must not treat empty/error as "nice to have"; they are part of the
Day-1 path (spec §7.1 needs_review/failed, §20).

## Implications for the decomposition

- New stories are `layer: web`, `target_repo: raffa-web`, and cite
  `inputs/design/prototypes/ia.md` + `day1-demo.html` for the route they build.
- Route guards for admin-vs-procurement nav items are a thin API/claims concern
  handed to client-architect (not re-decided here).
- Every §16 row maps to ≥1 route above; every §20 step maps to a route sequence
  in `ia.md` "Day-1 path".
- No mobile IA work: ADR-013 remains non-gating scaffold.

## Assumptions

- Entra ID will return role/permission claims usable client-side to gate
  `/workspace/members` (otherwise the non-admin "request access" state must be
  server-driven). Tracked in reports/open-questions.md.
- Azure Static Web Apps free tier can serve the SPA route fallback for
  `/contracts/:id`, `/contracts/:id/review`, `/quotes/:id` (client-side routing
  needs a `navigationFallback`/rewrite). Handed to cloud-architect, not a UI open
  question.

## Amendment (2026-09-08, epic-12 / ADR-023)

Route `/ask` remains the IA home for Ask Raffa. The screen is a **copilot
conversation**, not a retrieval debugger: articulated markdown, human citation
cards (supplier / contract title, never raw `Document:<guid>`), contract
preview, and deep links the API supplies as `{ label, href }`. Hide engineer
chrome (“Structured query…”, “Clause retrieval…”). Off-domain / greeting turns
stay on `/ask` with a warm redirect into this portfolio. See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

The V2 IA replaces the Day-1 sitemap. Pixel and behaviour reference:
`inputs/design/prototypes/Raffa V2 Prototype.html`, unpacked at
`inputs/design/prototypes/raffa-v2/` (`ia-v2.md` is the canonical route
map). Sign-in lands on **`/ask`** (`/` redirects); `/ask/:conversationId`
resumes a chat. **Two-tier rail**: Ask Raffa (⌘K, last 5 conversations,
"+ New chat") and Documents; "From your contracts": Portfolio, Renewals,
Quote check, greyed until the first validated contract. **No Home item**;
Savings lives at `/savings`, reached from actions, Renewals and Contract 360.
Review is a **state of Documents** (`/documents?review=:id`). The global Ask
bar always opens a new chat. Roles: Admin and Procurement upload; only Admin
deletes and manages members. Divergences from the prototype follow
`inputs/requirements.md` and are listed in `ia-v2.md`. This footer
supersedes the epic-12 amendment above. See ADR-024.

## Amendment (2026-09-10, wave w14 — one public route joins the V2 map)

Serves **NW-58**, and resolves the `## Assumptions` claim above for **NW-14**.
This footer **adds one route to the V2 IA** recorded in the epic-13 footer and
**does not supersede it**: the two-tier rail, `/ask` as the landing route,
Review-as-a-state-of-Documents and the Admin/Procurement split all stand
unchanged. Written by client-architect under `:125-126` ("Route guards …
handed to client-architect (not re-decided here)"); the copy for every state
named below is ADR-020's and the ux-ui-designer's.

**1. The route map gains one row, and it is the first public one.**

| Route | Screen | Primary object |
|---|---|---|
| /invite/accept | Invitation accept — offer, sign-in prompt, outcome | Invitation |

It is **reachable signed out and with no workspace**, rendered **outside
`AppShell`** (no rail, no global Ask bar — an invitee is not yet a member of
anything the rail could list), and it carries the token in the URL
**fragment** (`/invite/accept#<token>`, ADR-025 Rule C9), never a query
string. `ia-v2.md`'s route map does not contain it (verified `:44-56`) and the
map above is headed "locked", which is precisely why this is an amendment
rather than an interpretation.

**2. It forces one structural change to the shell.** Every route today lives
*inside* `WorkspaceShellApp`, and `BrowserRouter` is mounted only once
`account && workspace` are both true (`App.tsx:91,108`;
`WorkspaceShellApp.tsx:81-87`) — so in the invitee's state **no router
exists**, and the accept route cannot simply be added to `ShellRoutes`.
`BrowserRouter` therefore **moves up into `App.tsx`**, with a public branch
for `/invite/accept` and `*` falling through to the existing
account/workspace gate. `ShellRoutes` is already exported separately
(`WorkspaceShellApp.tsx:41`) as a testing seam, so this is a supported change,
not a rewrite. Two consequences for the decomposer: it shares its twenty lines
with NW-03's new async "resolving" state, so **those two edits are one task or
strictly sequenced**; and it falsifies `WorkspacePickerScreen.tsx:117-123`'s
comment ("no router is mounted anywhere above this component"), which
justifies a hard `<a href="/">` at `:124` — the hoisting task updates that
comment or converts the anchor.

**3. The `## Assumptions` claim above is resolved, not reversed.** It assumed
Entra would return role/permission claims usable client-side to gate
`/workspace/members`, "**otherwise the non-admin 'request access' state must
be server-driven**". ADR-010's claims are not wired and NW-05 is queued to
W15, so **the stated alternative is the one in force**: the role is
server-driven, arriving as a field on `GET /api/workspaces` (ADR-026 §D1).
`RequireRole` (`WorkspaceShellApp.tsx:67-74`) and `canManageMembers` keep
their exact shape — only the provenance of the value changes. `:107-108`'s
Procurement "read-only" state on member management becomes **buildable for the
first time** this wave, because NW-04 ships the roster endpoint whose absence
is why `RequireRole.tsx:20-26` generalised that copy.

**4. States on the new route** (all per `:112-119`, copy owned by UX): the
offer (workspace name, offered role, expiry — and **no tenant data beyond
that**, ADR-026 §D5); a sign-in prompt for a signed-out invitee; accepting;
and four distinct failures — expired / already used / revoked, wrong signed-in
account (ADR-025's email match, whose message must never echo the invited
address), and **"you are signed in — open your invitation link again"**, which
is a normal outcome of a redirect sign-in or a reload, not an error in the
invitation.

**5. `/workspace/members` gains two destructive affordances, not one.** An
`Invited` row is **revoked** (`DELETE …/invites/{id}`) and an `Active` row is
**removed** (`DELETE …/members/{membershipId}`) — different endpoints,
different confirmation copy, and the last Admin cannot be removed (409, a
disabled control with a visible reason per ADR-019). The screen re-reads the
roster after either; there is no optimistic local removal, because the
last-Admin guard is a server rule and the server's answer is the one that
renders.

## Amendment (2026-09-10, wave w14 — design: the Procurement roster state, and where the states contract now binds)

Serves **NW-58** and **NW-24**. Written by ux-ui-designer, owner of this ADR
(`INDEX.md:51`). It **adds to the client-architect's w14 footer immediately
above and supersedes nothing**: that footer owns the route, the router hoist and
the guard; this one owns the IA-level *states* those changes create. The route
row is not restated here. Copy for every state named below lives in ADR-020's
w14 footer.

**1. `:107-108`'s "read-only" reading is the one in force, and the export
contradicts it.** The Roles section already grants **Procurement** "all routes
except member management (sees a 'request access' / **read-only** state on that
surface)", and `ia-v2.md:13-15` agrees ("Members screen is read-only with
'request access'"). The V2 export does something different: `markup.html:403`
**hides the table outright** — the entire grid at `:386-401` sits inside
`<sc-if isAdmin>` (`:384`) — leaving a non-Admin only an explanation block. This
is therefore **not a divergence from the prototype but a contradiction inside
the design oracle**, and this footer records which side is in force: **the ADR
and the IA doc win**. Procurement sees the roster, read-only — no `Actions`
column, no invite pane — with the explanation block beneath it.

This becomes buildable for the first time this wave, and the code says so:
`RequireRole.tsx:20-26` records that the prototype's "Admins: Marta Keller,
Jonas Frei" sentence was **deliberately generalised** because "this app has no
backend 'list members' endpoint yet". NW-04 is that endpoint. What changes as a
result is what `RequireRole` gates on this route, which `:125-126` hands to
**client-architect** ("route guards … not re-decided here") — the states and the
copy are this seat's, the guard is theirs, and if they keep a hard guard the
copy still applies to the block that remains.

**2. `Request access` must not ship as a dead control.** It is inert today — a
`<button>` with no handler (`RequireRole.tsx:41`) — and no endpoint exists to
give it. With NW-04 the workspace's real Admin addresses become knowable, so it
renders as `<a class="btn btn-secondary" href="mailto:…">` to those addresses
with the subject prefilled, which is real, honest and needs no backend. (Both
roles are inside the same tenant and the roster above already shows those
addresses, so nothing is exposed that the screen does not already show.) If the
table prefers to drop the button the block still reads correctly without it —
**a dead button is the one option that is not available.**

**3. Where `## Empty / error / loading (IA-level contract)` now binds.** w14
creates the SPA's first three server-backed read surfaces outside the document
path, and `:112-119` binds each of them: the **workspace list** on screen 1
(the product's first network read — today `WorkspacePickerScreen` has no fetch
at all), the **members roster**, and the **invitation accept** screen. Two
clarifications this wave forces, both recorded so a task does not have to guess:

- On screen 1 the **create form is the empty state**; there is no separate "you
  have no workspaces" screen. A failed read renders the error state and
  **never a cached list** (ADR-012 w14).
- On the accept route, **"open your invitation link again" is a normal
  outcome, not an error state**. It follows from the token living in memory for
  one mount, so it is expected after any redirect sign-in or reload, and it must
  not be dressed in the error treatment or worded as an invalid invitation.

**4. One unrecorded divergence on screen 1, named rather than left in the
code.** `WorkspacePickerScreen.tsx:100-137` renders an interstitial ("You're in
{name}" → Continue / Switch workspace / Sign out) that exists nowhere in the V2
export, where `app.jsx:139` goes straight to Ask and `screens-v2.md:21` lists
screen 1's states as idle · signing · create · pick. It sits exactly where
NW-03's "one membership → enter it" rule operates. **The routing decision is
client-architect's**; this footer records that the divergence exists and ADR-020
records the real state set either way, so it stops being an undocumented
behaviour that the next reader mistakes for the design.

## Amendment (2026-09-13, wave w15 — the states contract gains two "not empty, not ready" states, and what each Documents number counts)

Serves **NW-61** and **NW-27**, and supplies the counting rule **NW-10**'s task
needs wherever it lands (OQ-w15-008). Written by ux-ui-designer, owner of this
ADR (`INDEX.md:51`). Every section above stays in force. **The route map
(`:89-103`) is not touched: no route is added, moved or removed this wave** —
recorded here as well as in client-architect's ADR-012 w15 footer so no task
re-opens the router table from either side.

**1. `## Empty / error / loading (IA-level contract)` (`:112-119`) gains two
states, and this is the decision that generalises.** It names three today:
loading, empty, error. A surface whose content derives from **validated
contracts** can be non-empty and still have nothing to show, for two different
reasons, and those reasons carry different messages and different next steps:

| State | Means | Treatment | CTA |
|---|---|---|---|
| **empty** (existing, `:116-117`) | nothing has been uploaded | h3 + one sentence + primary action, max 480px | **Upload a contract** |
| **not ready yet** (new) | ≥1 document is in flight — `Uploaded` · `Processing` · `NeedsReview` | **the same block**, never the error treatment: nothing has failed | **Go to Documents** |
| **nothing made it through** (new) | Raffa holds ≥1 document, none is in flight, and none has produced a validated contract | **the same block**, never the error treatment: the failure is per-row and it is legible on Documents | **Go to Documents** |

Two rules bind all three:

1. **The distinction is a server signal, never a client heuristic.** The surface
   is told; it does not infer. This is ADR-012's w14 provenance rule extended one
   step — *a client must not infer a server state it can be told* — and it is why
   NW-61 asks software-architect for completeness signals on the aggregates
   rather than for a second client-side count. **If the server cannot say it, the
   surface does not guess**: it keeps the plain empty state and the gap is
   recorded as an open question, never filled by a derivation.
2. **An empty state must never tell a user to do a thing they have already
   done.** "Upload a contract" shown to someone whose fifteen files are
   mid-pipeline — or whose fifteen files all failed — is the product forgetting
   what the user just did.

This binds every tier surface, including the two this wave does not build:
**Ask**, **Portfolio**, **Contract 360** (copy in ADR-020's w15 footer), and —
same shape, no task required — **Renewals** (`screens-v2.md:125-127`) and
**Quote check** (`:141-144`). Stating it once here is what lets those two
inherit it without a second decision.

**2. What each Documents number counts — and this withdraws a ruling of this
seat's own lane draft.** My lane §A4 ruled that the filter chip
`All documents · N` **counts** refused files, on the ground that `getFilterHint`
promises "Everything, including validated documents."
(`documentTable.ts:163-167`). Reconciling with product-owner's NW-27 row
("**never counted** in 'All documents' nor in the review queue") and with
ADR-027 §D7 (`counts.all` **excludes** `Rejected`, "which is what makes *never
counted* a server fact"), **that ruling is withdrawn.** The replacement is better
than either position, because every number on screen 3 then reads **exactly one
server field**:

| Surface | Reads | Contains `Rejected`? |
|---|---|---|
| `Needs your attention · N` | `counts.needsAttention` | **no** — and the server definition must be *not `Completed` **and** not `Rejected`*, mirroring `isAttentionStatus` (`documentTable.ts:141-143`) |
| `All documents · N` | `counts.all` | **no** — it means "every document Raffa holds" |
| **`Not added · K`** (new, third chip) | `counts.rejected` | it **is** the refused set; the chip renders **only while `counts.rejected > 0`** |
| `kbSummary` "N documents · M askable · K waiting for your review" | `counts.all`, and see clause 3 | no |
| Rail **Documents** badge (`N to review` / `N docs`) | `counts.needsAttention` / `counts.all` | no — **by construction**, not by a client rule |

**Why a third chip rather than folding refusals into "All".** Product-owner ruled
they are never counted there, and `counts.all + counts.rejected` would be
**client arithmetic over two server facts** — the shape ADR-012's w14 clause 7
deleted. One chip = one label = one definition = one server field. It is also the
only arrangement in which a **"persistent, visible, terminal record"**
(product-owner's own words for a refusal) is actually *reachable*: with two chips
and `counts.all` excluding `Rejected`, the row exists on the server and **no
filter on the screen shows it** — a durable record nobody can see is worse than
the session card it replaced. `counts.rejected` was already in ADR-027 §D7's
field set with no consumer; this is its consumer.

Consequences a task must carry, all in `web/src/routes/documents/`:
`AttentionFilterValue` gains a third member (`documentTable.ts:139`),
`filterDocumentsByAttention` a third branch (`:145-150`), `isAttentionStatus`
excludes `Rejected` as well as `Completed` (`:141-143`), `getFilterHint` gains a
third string and its "all" string becomes "Everything **Raffa keeps**, including
validated documents." (`:163-167` — the one-word-pair edit that keeps the promise
true), and `AttentionFilter.tsx:3-8`/`:18-32` take a third
`<button aria-pressed>` in the existing `.seg` (ADR-019 w15 clause 3a).

This also **dissolves OQ-w15-D1**, which this seat raised in lane: two numbers on
one screen deliberately differing by the refusal count. They no longer overlap,
so the question disappears rather than being answered.

**2b. Correction to ADR-027 §D7, found by reading it back after the table, and it
is the difference between A15-1 passing and the reported defect shipping again.**
§D7 (`:253-254`) defines `needsAttention` as **`NeedsReview` + `Failed`**, on the
stated ground that this is *"the definition the client's attention filter already
applies, moved to the server rather than re-invented"*. **The premise is false,
and it is checkable in one line**: `isAttentionStatus` is
`processingStatus !== "Completed"` (`documentTable.ts:141-143`), so the filter
**includes `Uploaded` and `Processing`**, and its own doc comment (`:136-138`)
derives it from the design oracle — `app.jsx`'s
`attnDocs = docs.filter(d => d.status !== 'completed')`, *"everything except
`completed`, i.e. processing/needs_review/failed"* — and names **R-DOC-06**.

Shipped as written, the count and the rows **under the same chip** disagree:
`attention` is the default filter, so fifteen just-dropped `Uploaded` files list
**fifteen rows** beneath a chip reading **"Needs your attention · 0"**. That is
not a near-miss, it is the reported defect of NW-61 verbatim ("Needs your
attention · 0 / All documents · 0" beside fifteen rows saying "Uploading…") —
re-introduced on the server, under a server number, where it looks authoritative.

**The definition that governs the screen is this ADR's**: `needsAttention` is
*not `Completed` **and** not `Rejected`* (the row at `:355`). One rule decides it
— **a chip's number and the rows it filters to must be the same set**; a count
that disagrees with the list under it is the class of defect this whole item
exists to remove. The alternative reconciliation — narrowing the *filter* to
match §D7's count — is rejected here and named so it is not reached for: it would
hide the fifteen rows a user just dropped from the default screen, contradict
`getFilterHint`'s shipped promise that only *"Completed documents are hidden —
they are already askable."* (`documentTable.ts:163-166`), and diverge from the
export. **Ask to software-architect** (their file, their edit — this seat does not
write ADR-027): §D7's `needsAttention` bullet takes the definition above, or
states which of the two surfaces it intends to be wrong. `all`, `processing` and
`rejected` are correct as written and are not re-opened.

**3. `kbSummary`'s "M askable" states a fabricated fact today, and naming it is
this seat's charter.** `documentTable.ts:154-160` computes `askable` as
`items.filter(i => i.processingStatus === "Completed").length` over the **fetched
page** (≤100, `useDocumentsList.ts:15-18`). Two defects in one segment: it is
page-scoped, exactly like the counters NW-61 is fixing; and it equates askable
with **`Completed`**, which product-owner's NW-61 row and ADR-026 §D2 rule is
*not* the definition — **askable is validated, not merely completed**. Screen 3
therefore tells the user Ask can answer from M documents when Ask may answer from
fewer: the same class of defect software-architect found server-side at
`AskCopilotService.cs:294`, one screen earlier. **Ask to software-architect,
recorded rather than assumed: `counts` gains an `askable` member under ADR-026
§D2's single definition of validated.** If that is refused, **the segment is
removed** rather than left reading a number the product does not mean. Neither
branch gates a task, and `N documents` / `K waiting for your review` are
unaffected either way.

**Read back after the table**: ADR-027 §D7 lands `counts { all, needsAttention,
processing, rejected }` (`:248`) — **no `askable` member**. So **branch two is in
force**: the segment is **removed**, and `kbSummary` reads
`N documents · K waiting for your review`. Recorded as a resolution rather than
left as an open ask, so the decomposer writes one task instead of choosing. If
`askable` joins §D7 before the gate closes, branch one applies instead and the
segment reads `counts.askable`; **the field list on disk in ADR-027 §D7 decides
it**, not this sentence. `N documents` reads `counts.all`, which is tenant-wide,
so the page-scoping half of the defect is closed on either branch.

**4. The rail Documents badge.** `ia-v2.md:25`; `navItems.ts:99-107`;
`RailNav.tsx:67,72-75`. `N to review` is `counts.needsAttention`; `N docs` is
`counts.all`; the honest-absence rule is unchanged (**no badge** rather than a
wrong badge). Refusals never reach it — by clause 2, not by a rule the badge has
to remember. Recorded here because NW-10's task needs it whether it rides NW-61
or opens W16.

**5. What this footer does not change.** The route map, the Roles section
(`:104-111`), the loading and error treatments and their wording, the default
attention filter (R-DOC-06), and the three original states of `:112-119`, whose
text is untouched.

## Amendment (2026-09-14, wave w15 — re-entry round: a "not ready yet" state that can never resolve, and the counting table the local refusal row does not touch)

Continues the **2026-09-13 w15 footer** above (`:301-455`, clauses 1–5), every
clause of which stays in force; numbering continues from it. Serves **NW-61** and
**NW-27**. Written by ux-ui-designer, owner of this ADR (`INDEX.md:51`). The route
map (`:89-103`) is still not touched: **no route is added, moved or removed this
wave.**

**6. The states contract gains a third binding rule: bounded updates.** Clause 1
added **not ready yet** and **nothing made it through**, and bound both to a
server signal. The first of the two makes a promise the contract did not price:
*something is in flight, and this surface will notice when it lands.* It is kept
by a **repeating re-read**, and ADR-027 §C6 — written at this table, at this round
— withdraws the property that guaranteed the re-read ends. A commit that lands
after the abandon window leaves a row at `Uploaded` permanently and §0.1 forbids
the cross-tenant sweeper that would find it, so the surface waits forever while
telling the user it is nearly there. Rule:

> A **not ready yet** state that depends on a repeating re-read **stops on a
> bounded no-change budget and offers an explicit resume**; a surface that cannot
> offer the resume **must not claim it is waiting**.

Three notes that make it usable rather than pious. **(a)** It is stated here, and
not only in ADR-012 §17, because §17 bounds **one hook** while the promise is made
by the **state**, on five surfaces — Documents, Ask, Portfolio, Contract 360, and
by inheritance Renewals and Quote check — three of which have no row grid and
therefore no other way to show that nothing is moving. Ask is the sharp case: its
gate re-read is a one-shot `useEffect` today (`ask/index.tsx:100-105`) and NW-61's
client ruling turns it into a 2 s poll, so this wave introduces the unbounded wait
at a **second** call site that §17 does not name. **(b)** The resume is a *client*
act — re-read now — and never a re-label: the row's status, the chips' numbers and
the progress bar stay exactly what the server last said (ADR-012 §17's three
prohibitions, which this clause adopts unchanged). **(c)** The copy is one
sentence and one label, identical on every surface, in ADR-020 w15 round-3 §8.

**7. The local refusal row changes nothing in clause 2's counting table.** A file
refused **before storage** (in the browser, or by a 413/415) has no id and is in no
server count; it renders as a session-local row (ADR-020 w15 round-3 §6). Recorded
because the tempting repair is the wrong one: a task making `Not added · K` "agree
with the screen" by adding a client-side increment would put a number that is
partly the server's and partly this tab's under one chip — clause 2's whole point
is that **each number reads exactly one server field**. The chip counts what
Raffa.ai kept a record of; the local row is a file the product never stored.

**8. What this footer does not change.** The route map, the Roles section
(`:104-111`), the three original states of `:112-119`, the definitions,
treatments and CTAs of the two states added by clause 1, the counting table of
clause 2 and clause 2b's `needsAttention` definition, clause 3's resolution, and
the badge rule of clause 4.
