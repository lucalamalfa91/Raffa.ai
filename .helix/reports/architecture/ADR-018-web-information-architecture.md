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
