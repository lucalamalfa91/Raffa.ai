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
Ask Contigo → Quote check → Documents → Review queue → Workspace & members),
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
| /ask | Ask Contigo — chat, citations, abstain | Query |
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

- New stories are `layer: web`, `target_repo: contigo-web`, and cite
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

Route `/ask` remains the IA home for Ask Contigo. The screen is a **copilot
conversation**, not a retrieval debugger: articulated markdown, human citation
cards (supplier / contract title, never raw `Document:<guid>`), contract
preview, and deep links the API supplies as `{ label, href }`. Hide engineer
chrome (“Structured query…”, “Clause retrieval…”). Off-domain / greeting turns
stay on `/ask` with a warm redirect into this portfolio. See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

The V2 IA replaces the Day-1 sitemap. Pixel and behaviour reference:
`inputs/design/prototypes/Contigo V2 Prototype.html`, unpacked at
`inputs/design/prototypes/contigo-v2/` (`ia-v2.md` is the canonical route
map). Sign-in lands on **`/ask`** (`/` redirects); `/ask/:conversationId`
resumes a chat. **Two-tier rail**: Ask Contigo (⌘K, last 5 conversations,
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
