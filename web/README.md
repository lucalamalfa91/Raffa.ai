# Raffa web client

React + TypeScript + Vite SPA. OIDC Authorization Code + PKCE via MSAL, config
injected at runtime. Honours ADR-012 (web stack), ADR-010 (Entra ID / OIDC),
and ADR-018 (information architecture / route map).

## Stack

- **React 19 + TypeScript + Vite** — static bundle, no server runtime
  (ADR-012). Build output is `dist/`.
- **`@azure/msal-browser` + `@azure/msal-react`** — Authorization Code + PKCE
  against Entra ID. `PublicClientApplication` has no client-secret field: this
  is structural, not just convention (AC-1).
- **Generated OpenAPI client** (`src/api/`) — the only way `src/` talks to the
  backend API; see "API client" below (AC-3).
- **`react-router-dom`** (task E06/F03/US02/T01; `BrowserRouter` hoisted to
  `App.tsx` by task E14/F03/US02/T01, wave w14) — one router now spans the
  whole app: a public branch for `/invite/accept` (reachable signed out and
  with no workspace) and a `/*` fallthrough to the account/workspace gate,
  which mounts the signed-in shell (`src/components/shell/`) once both
  resolve. `/signin` itself still stays gated on MSAL auth state, not a
  router route of its own (see "Screens" below).
- **Vitest + Testing Library** — unit tests under `tests/`, mirroring `src/`.

## Commands

```bash
npm ci                    # install (CI uses this)
npm run dev               # Vite dev server on :5173, reads public/config.json
npm run generate:api      # regenerate src/api/generated/schema.ts from openapi/raffa-api.v1.json
npm run build             # generate:api, then tsc --noEmit type-check, then vite build -> dist/
npm test                  # vitest run (single pass, CI mode)
npm run preview           # serve dist/ locally
npx playwright install --with-deps chromium   # one-time browser download for the commands below
npm run test:e2e          # e2e/day1.spec.ts -- the §20 Day-1 browser walk (see "End-to-end" below)
npm run test:e2e:report   # open the last e2e run's HTML report (trace/video on failure)
```

## Runtime config injection (ADR-012 "config, not code")

The SPA never hard-codes the API origin, OIDC authority, or client id. At
boot (`src/main.tsx`), it fetches same-origin `/config.json` and validates it
(`src/config/appConfig.ts`) before constructing MSAL or rendering the app; a
missing or malformed config fails loudly (a full-page error), never silently
falls back to a guessed value.

This is required by how the CI pipeline is already shaped, not just by
preference: `.github/workflows/web.yml`'s `build` job runs `npm run build`
**once** and uploads a single `web-dist` artifact; the `deploy` job downloads
that same artifact for either `dev` (push to `main`) or `demo` (`workflow_call`
reuse, ADR-016). One compiled bundle is deployed unchanged to both
environments, so per-environment values cannot be baked in at build time
(e.g. Vite `import.meta.env.VITE_*` statics) — they must be resolved from the
deployed origin at request time. `config.json` being a plain static asset
(not part of the JS bundle) is what makes it independently overwritable per
environment after the shared build.

`public/config.json` in this repo is a **local-dev-only placeholder** with
safe, non-secret values (`https://localhost:7109` — the API's dev
`launchSettings.json` port — and `REPLACE_WITH_*` OIDC placeholders). None of
the values are secret by design: `client_id` and the redirect URI are public
for a PKCE public client (ADR-010); the API base URL is not sensitive.

### Per-environment config injection in CI

`web.yml`'s `deploy` job overwrites `web/dist/config.json` after
`download-artifact` and before `swa-cli deploy`, using live Azure lookups
(no GitHub Environment variables beyond the existing `AZURE_*` trio):

| Field | Source |
|-------|--------|
| `apiBaseUrl` | Ingress FQDN of `ca-raffa-<env>-api` |
| `oidcAuthority` | `https://login.microsoftonline.com/<AZURE_TENANT_ID>` (MSAL; no `/v2.0` suffix) |
| `oidcClientId` | Tag `oidcPublicClientId` on `id-raffa-<env>-workload` (set by `modules/identity`) |
| `oidcRedirectUri` | `https://<swa defaultHostname>/` (trailing slash; matches Entra SPA redirect) |
| `oidcApiScopes` | `api://raffa-<env>-api/Raffa.Read` and `.../Raffa.Write` (stable App ID URI) |

`scripts/write_web_runtime_config.py` validates the payload and refuses
localhost / `REPLACE_WITH_*` placeholders. The Static Web App itself is
`swa-raffa-<env>` in `rg-raffa-<env>`. Deploy 404s until the HCP VCS
apply that creates it (and the workload-identity tag) is CURRENT — re-run
the web workflow after that apply.

## Screens (ADR-024 V2 route map, amending ADR-018)

Task E13/F09/US01/T01 (web-shell-v2, gap G-IA-V2) moved this app from the flat Day-1 rail to the
V2 IA: `/` now redirects to `/ask` (Ask Raffa is the home), the rail is two-tier (Ask Raffa +
Documents; "From your contracts" -- Portfolio, Renewals, Quote check, greyed until the first
validated contract), there is no Home item, and Review is a redirect into Documents rather than its
own rail destination. Pixel/behaviour reference: `inputs/design/prototypes/Raffa V2 Prototype.html`
(unpacked `raffa-v2/`). See "App shell, navigation, and the role guard" below for the rail itself.

| Route | Screen(s) | Task |
|-------|-----------|------|
| `/signin` | Sign-in (Entra redirect, idle/redirecting states) -> workspace picker, server-driven (skeleton, error + Retry, list, create) | E06/F03/US01/T01; server-backed by E14/F03/US02/T01 |
| `/invite/accept` | Public, reachable signed out and with no workspace, rendered outside `AppShell`. Ten states (no token / checking / offer with Join or the Entra CTA / joining / wrong account / expired-or-revoked sharing one first sentence / already joined / lookup unavailable + Retry); reads the token from the URL fragment on mount, holds it in memory for that one mount, clears the address bar before first paint, sends it only in `X-Invitation-Token`. See "Invitation accept" below. | E14/F03/US02/T01 |
| `/` | Redirects to `/ask` (R-WEB-01) -- there is no standalone Home screen in V2. | E13/F09/US01/T01 |
| `/ask` | Ask Raffa, V2 rebuild: off state below 1 validated contract (fixed headline + doc-count-dependent reason + one CTA to `/documents`); new chat (hello line, scope line naming the validated count, two capability-sourced suggestion chips, optional `?scope=<contractId>`); conversation view rendering the phase-2 reply contract (markdown, numbered citation cards, actions, follow-ups) via `ReplyBody`. Calls the real `GET/POST /api/conversations`, `POST /api/conversations/{id}/messages`, `GET /api/capabilities`, `GET /api/market/records/{id}`. See "Ask Raffa" below. | E07/F04/US01/T01; V2 rebuild E13/F09/US01/T04 |
| `/ask/:conversationId` | Same `AskRoute` as `/ask`, resuming: `useConversation` loads the conversation (`GET /api/conversations/{id}`) and renders every past turn, oldest first, with citation cards and actions still clickable; a named "not found" state for an unknown/foreign/another-user's id. | E13/F09/US01/T01 (route only); resume wired by E13/F09/US01/T04 |
| `/documents` | V2 rebuild (ADR-024 amendment to ADR-020 screen 3): onboarding empty state ("First your contracts. Then your questions.") -> a server-backed list (`GET /api/documents`, survives a reload) with a **Needs your attention · N** (default) / **All documents · N** / **Not added · K** (only while K > 0) filter whose numbers are the server's own tenant-wide `counts` (never the fetched page), multi-file drop (up to 20 files, <=3 uploads in flight, one row per file from the moment it is picked, a 120 s client-owned deadline per request), a stored row reading **Queued…** until the Worker picks it up and then its real stage text polled every 2 s — a poll that stops after five minutes without a change and offers **Check again** — a **Not added** *row* for a refused file (the server's `Rejected` row with the requirements' own reason sentence; a local row for an oversized/413/415 file), Admin-only Delete, and Review as a *state* of this same route (`?review=<id>`, reusing `routes/contracts/review/*` as-is). Calls the real `GET /api/documents`, `GET /api/documents/{id}/preview`, `POST /api/documents`, `POST /api/documents/{id}/reprocess` (202), `DELETE /api/documents/{id}`. See "Documents" below. | E06/F05/US01/T01, E06/F05/US02/T01; V2 rebuild E13/F09/US01/T03; truthful surfaces E16/F03/US01/T01 (wave w15) |
| `/contracts` | Portfolio, V2: header ("Portfolio" + "N validated contracts · CHF 4.2M annual · K notice deadlines within 45 days", or "Lights up from validated contracts"), one table sorted by notice deadline (Supplier · Contract · Annual spend · Ends · Give notice by · Status; **More columns** adds Start · Auto · Risk; rows inside the 45-day window tinted + accent bar; rows open Contract 360), and the reroute state ("Nothing to triage yet" → Upload a contract) while nothing is validated. Calls the real `GET /api/contracts` (now carrying `currency`). See "Portfolio" below. | E07/F01/US01/T01; V2 design alignment (Sept 2026) |
| `/contracts/:id` | Contract 360, V2 **no tabs**: origin back link ("← Ask Raffa / Documents / Portfolio / Renewals / Savings", else "← Back"), supplier · title · "{type} · {spend} / year · N documents · {status}", **Ask about it** → `/ask?scope=<id>`; the **answers band** (Where you can save · When you must move · What to do, with Start negotiation / Assign to me or the negotiation tracker once acted); **Why — the clauses behind it** (clause rows with leverage tags; click → the original wording highlighted; **Open in document viewer**; `?clause=<id>`/`?page=<n>` pre-select it); **Details ▾** (key terms, documents, "N facts still need you — Review all →" when any `review_required` decision remains, priority score, Products/Obligations/Risks). Calls the real `GET /api/contracts/{id}`, `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`, `GET /api/contracts/{id}/evidence`, `POST /api/renewals/{id}/action`. See "Contract 360" below. | E07/F02/US01/T01; citation landing E13/F10/US01/T01; V2 layout (Sept 2026); details and Why E22/F04/US01/T01 |
| `/contracts/:id/review` | Review / correction: 4-column field list (critical marker, extracted value + real source line, real per-field confidence tag from the extraction evidence, Accept/Correct) + right-hand evidence pane (file · page header, the quoted passage with the span highlighted, model + confidence, correction form, real correction-history trail) + gated "Mark as validated" that really signs the document off. Calls the real `GET /api/contracts/{id}`, `GET /api/contracts/{id}/corrections`, `GET /api/contracts/{id}/evidence`, `PATCH /api/contracts/{id}`, `POST /api/documents/{id}/validate`. Shares its whole lifecycle with the Documents review state through `routes/contracts/review/useReviewSession.ts`. See "Review / correction" below. | E07/F03/US01/T01 |
| `/renewals` | Renewals, V2: header ("Renewals" + "N contracts with validated dates · sorted by priority"), one list sorted by score (Score · Supplier · contract · Renews in · Notice in · Status) and the selected row's **Why it is here** pane (recommended action + rationale, Start negotiation / Assign to me, "See the facts behind this →"), plus loading/error states and the reroute "No renewal dates yet" while nothing is validated. Calls the real `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`, `POST /api/renewals/{id}/action`. See "Renewals" below. | E08/F01/US01/T01; V2 design alignment (Sept 2026) |
| `/quotes`, `/quotes/:id` | Quote check, V2: constant header ("Optional · new purchase" · intro sentence); landing = the dashed drop card (**Upload a quote** + "or use the sample: Databricks proposal Q-88213", optional supplier/currency/geography/date under a disclosure); loaded = the Supplier quote · Market range · Assessment band, the lines table (Line · Quoted · P50 · Position · Benchmark) and "Target and negotiation levers are one step further — shown only if you want them." revealing Target, then Negotiation (outcome capture); unmapped SKUs show the mapping block instead. Calls the real `POST /api/quotes`, `POST /api/quotes/{id}/assessment/recalculate`, `POST /api/negotiations/outcomes`. See "Quote check" below. | E08/F03/US01/T01; V2 design alignment (Sept 2026) |
| `/savings` | Savings, V2 (not a rail item -- reached from Ask actions, Renewals and Contract 360): header + summary, three KPI cells (Contracts analyzed · Upcoming renewals · Savings identified, each with a meta line), the opportunities table (Supplier · Action · Estimate · Status; rows open Contract 360), a stale-labelled KPI degrade when the benchmark provider is unreachable, and the reroute "No savings opportunities yet" → Renewals. Calls the real `GET /api/savings/kpis`, `GET /api/savings`, plus `GET /api/contracts` for supplier names. See "Savings" below. | E08/F02/US01/T01; moved by E13/F09/US01/T01; V2 design alignment (Sept 2026) |
| `/review` | Redirects to `/documents?filter=attention` -- Review is a *state* of Documents in V2, not its own rail destination or screen. The old `src/routes/review/` rail landing (V1 review queue) has been deleted. | E13/F09/US01/T01 |
| `/workspace/members` | Workspace & members, V2: "Setup" header (tenant meta line), the "invite the team once the first contract is validated" tip while nothing is validated, a server-read Member/Role/Status/Actions table (skeleton while loading, error + Retry, never a stale roster) and the **Invite a colleague** pane rendering the server's own `deliveryOutcome` in three states ("Invitation sent to {email}."; "Invitation created, but the email could not be sent." + the copyable link; "Invitation ready for {email}." + the link and its expiry), the one-time-code sentence while `identityProvisioned` is true, and a declared **502** ("No invitation was created.") when the company directory would not provision the guest. Admin-only `Actions` column: revoke an `Invited` row, remove an `Active` one, both confirmed inline in the row, the last Admin's Remove disabled with a `.hint`. A Procurement visit sees the same roster read-only, with a real `mailto:` **Request access** instead of the invite pane. Calls the real `GET /api/workspaces/{tenantId}/members`, `POST .../invites`, `DELETE .../invites/{id}` and `DELETE .../members/{membershipId}`. See "Workspace & members" below. | E06/F04/US01/T01; server-backed roster + invitation lifecycle E15/F02/US01/T01 (wave w14) |

### Layout -- full-bleed, matching the prototype's own canvas (ADR-018/019/020, task E06/F06/US01/T01)

Every screen now occupies the viewport the way
`inputs/design/prototypes/day1-demo.html` does: no screen renders inside a
centered, `max-width`-capped column. `src/index.css` used to put `max-width:
40rem; margin: 3rem auto` on the bare `<main>` element (an E01 OIDC-scaffold
rule) -- that hit *every* app-level main landmark, so sign-in rendered as a
~640px card (statement panel crushed to a sliver) and the whole authenticated
shell (`AppShell.tsx`'s `<main className="shell-main">`) rendered its rail +
content inside the same narrow column. `<main>` now carries no width opinion
in `index.css`; each screen owns its own full-bleed layout instead:

- **Sign-in / workspace picker** (`src/routes/signin/`) -- `SignInScreen.tsx`
  renders `.signin-screen`, a `100vh` two-column grid (`SignInStatementPanel`
  + `.signin-action`), matching the compiled prototype's own sign-in
  container (`display:grid;grid-template-columns:1fr 1fr`) once nothing above
  it caps the width. `WorkspacePickerScreen.tsx` reuses that exact same
  canvas (`SignInStatementPanel` + `.signin-action`) for all of its own
  states (skeleton, error + Retry, list, create) instead of the old
  standalone, centered `.workspace-picker` card -- the prototype never treats
  the workspace list as a separate screen, only a state of screen 1. Task
  E14/F03/US02/T01 (wave w14) removed the screen's former "You're in
  `<workspace>`" interstitial (Continue / Switch workspace / Sign out)
  entirely -- nothing resembling it exists in the V2 export, and every path
  (auto-entered, hint-matched, manually picked, freshly created) now lands
  straight in the shell.
- **App shell** (`src/components/shell/shell.css`) -- `.shell-main` now
  explicitly declares `max-width: none` so it fills the shell grid's `1fr`
  track (224px rail + fluid main) rather than floating as a narrow column
  inside it.
- **Documents** (`src/routes/documents/documents.css`) -- task E13/F09/US01/T03
  (V2 rebuild) replaced V1's own ~400px/1fr two-column grid (a dropzone
  column that stayed beside the table at all times) with the prototype's own
  stacked single-column layout: a centered onboarding block first, then a
  full-width list with a slim `.upload-dropzone--list` bar above the table --
  `screens-v2.md` #3 never shows the two side by side. `.documents-screen`
  itself carries no `max-width`, the same "fills the track, never a narrow
  column" rule this section states above. Filenames in the result card and
  the document table still wrap with `overflow-wrap: anywhere`
  (word/character-run boundaries) instead of `word-break: break-all`, so a
  long filename never renders one glyph per line -- originally fixed by task
  E11/F04/US01/T01 (gap G-DOC) on the V1 grid's own `th:nth-child(1)`/
  `td:nth-child(1)` column selector, carried into the V2 rebuild on the
  actual content classes rendered inside that column
  (`.upload-result-filename`, `.document-status-table-filename`,
  `.document-status-table-link`).

Only `.startup-error` (`src/main.tsx`'s boot-config-failure alert -- not a
shipped mockup screen) keeps a narrow, centered column.

`src/routes/signin/` (`SignInRoute`, the folder's default export) is still
gated on MSAL auth state (`useMsal().accounts`) rather than a URL route --
that has not changed. What task E06/F03/US02/T01 added, and task
E14/F03/US02/T01 (wave w14) made asynchronous, is `src/App.tsx`'s own
`AuthenticatedGate`: **three** states now, not two -- *resolving* (a neutral
placeholder, never `SignInRoute`'s own chrome, so a returning user with one
workspace never sees a picker flash before auto-entering it); *no account*
-> `SignInRoute`; *account + resolved* -> `WorkspaceShellApp.tsx`, once one
`apiClient.listWorkspaces()` call and the resolution order (empty -> create;
exactly one -> enter; ≥2 with the session hint in the list -> enter it; ≥2
otherwise -> picker, hint discarded) settle on a definite workspace.
`BrowserRouter` itself lives in `App.tsx`, not `WorkspaceShellApp.tsx` (see
"Stack" above) -- a signed-out or not-yet-a-member invitee still needs a
router mounted above them for `/invite/accept`, which no earlier gate can
satisfy. There is no more a "Continue to `<workspace>`" hard-navigation
control anywhere in this flow (see "Layout" above): every resolution outcome
lands straight in the shell through the one router already mounted.

### Invitation accept (ADR-020 screen 11's own w14 footer; ADR-025 Rules C9/C10; task E14/F03/US02/T01)

`src/routes/invite/accept/index.tsx` (`AcceptInvitationRoute`) is `/invite/accept` -- the one public
route this wave adds: reachable signed out and with no workspace, rendered outside `AppShell`
entirely (an invitee is not yet a member of anything the rail could list). Nothing resembling this
screen exists in the V2 export; every state below is ADR-020's own w14 footer, not a pixel quoted
from `markup.html`.

- **Ten states**: no token; checking (skeleton rows); offer (workspace name + offered role, and
  nothing else -- adding a contract count here would leak tenant data to anyone holding the URL)
  with a real Join button when signed in or the Entra CTA when not; joining; wrong account (cannot
  echo the invited address -- the 403's own reason is non-echoing); expired-or-revoked, sharing one
  first sentence on purpose so the screen never tells a probing visitor which case they hit; already
  accepted (with its own "Go to `<workspace>`" control); lookup unavailable (a 503 gets ADR-018's
  error + Retry treatment, which the terminal informational states above do not).
- **Rule C9** (ADR-025 §C): the token travels in the URL *fragment*
  (`/invite/accept#<token>`), never a path segment or query string -- a query string would land in
  the Static Web Apps platform's own access log, every third-party asset's `Referer` header, and
  browser history.
- **Rules C10/C10a/C10b**: the token is read once, on mount, held in memory for that one mount only,
  and never written to `sessionStorage`/`localStorage`/anywhere else.
  `readAndClearInvitationToken` runs inside a lazy `useState` initializer specifically so the
  address bar is already clear *before this component's first render* -- strictly before any
  control that could start authentication (the Entra CTA) ever paints (Rule C10b).
  `auth/msalConfig.ts`'s `handleRedirectPromiseOptions` (`navigateToLoginRequestUrl: false`, wired
  through `main.tsx` -- see that file's own header comment for why it moved there) is Rule C10a, a
  structural guarantee independent of this component's own render order; the two rules are
  deliberately redundant ("C10a survives a refactor of this ordering, C10b survives a change to
  MSAL's own default").
- **`/signin`'s own CTA is redirect-only** (`msalConfig.ts`; `loginRedirect` at
  `routes/signin/index.tsx`), so a signed-out invitee who signs in from *there* loses this page (and
  the token) to the round-trip -- "sign in, then open the invitation link again" is the guaranteed
  flow, not a fallback, and the "no token" state's own copy says exactly that, framed as a normal
  outcome, never "this invitation is invalid" (which would send a user back to their Admin for a
  replacement they do not need -- invitations are single-use, so that "fix" costs a real one).
  **This screen's own Entra CTA calls `loginPopup` instead** (ADR-012 w14 footer clause 6) -- the one
  path on which the invitee never leaves the accept screen, so sign-in is single-click where the
  browser allows it, because the page is never unloaded and the in-memory token survives the
  round-trip. If the popup is blocked, closed, or otherwise fails to complete, `handleContinueWithEntra`
  falls back to the same "no token" state a reload lands on -- one state either way, deliberately:
  "the popup-blocked and reload cases share one state, so it ships regardless."
- **After a successful accept**, the screen re-resolves the workspace list and enters it --
  membership is proven by the next `GET /api/workspaces`, never trusted from the accept response
  body, which is a hint only (the same "hint, not a trust" posture `workspaceStore.ts`'s session key
  already takes).

### App shell, navigation, and the role guard (ADR-024 V2 amendment to ADR-018/ADR-019; task E13/F09/US01/T01, gap G-IA-V2; originally task E06/F03/US02/T01)

- **Two-tier rail** (`src/components/shell/RailNav.tsx`, model in
  `src/components/shell/navItems.ts`) replaces the flat, eight-item Day-1 list.
  **Primary**: Ask Raffa (badge `⌘K`, a nested conversations slot -- the
  caller's own last 5 conversations from `GET /api/conversations`, active one
  in accent, plus "+ New chat"; `useRecentConversations` re-fetches on every
  navigation rather than once per shell mount, since a new conversation is a
  routine, every-few-clicks event -- wired by task E13/F09/US01/T04) and
  Documents (badge `N to review`, accent, off the server's
  `counts.needsReview`, else `N docs` off `counts.all`, else no badge --
  `useDocumentCounts.ts`, one `listDocuments({ pageSize: 1 })` read
  `AppShell.tsx` makes once per mount and passes down beside the validated
  contract count, the same shape `useValidatedContractCount.ts` establishes
  for the secondary tier below; task E16/F03/US01/T01, wave w15, NW-10 --
  the `sessionStorage` tracker this badge used to read is deleted).
  **Secondary, "From
  your contracts"**: Portfolio, Renewals (badge = the validated-contract
  count), Quote check (badge is always the constant `optional`) -- the whole
  tier's foreground dims to a muted grey until the first validated contract
  (`kbReady`, a small accent/neutral dot next to the "From your contracts"
  kicker mirrors the same signal). There is no Home item and no Review queue
  item in V2. No icon library is a dependency yet, so rail items are
  text-only (design-system.md calls for Lucide icons; adding that library is
  not this task's scope).
- **`kbReady` / the validated-contract count** (`src/components/shell/useValidatedContractCount.ts`)
  is fetched once per shell mount via the existing `GET /api/contracts`
  (`apiClient.getPortfolio`, first 100 rows) and passed down to both the rail
  and the global Ask bar -- there is no dedicated "validated contracts"
  endpoint yet, so a contract counts as validated once it is past every
  transient/blocking status that endpoint can report today (`processing`,
  `failed`, anything containing "review"; see that module's own doc comment
  for the full provenance and the `requirements.md` R-CMP-03 citation). A
  contract that becomes validated mid-session only updates the rail on the
  next full shell mount (fetched once, not polled).
- **Role guard (AC-2)** -- "Workspace & members" is a footer link
  (`navItems.ts#canManageMembers`, unit-tested), not a row in the flat list.
  `src/components/shell/RequireRole.tsx` used to wrap the `workspace/members`
  route as a route-level guard (defense in depth for a direct URL visit),
  rendering ADR-018's "request access" state for a non-Admin instead of the
  real screen -- task E14/F03/US02/T01 (wave w14) removed that wrap, because
  the server role, not a client-side gate, now decides what a non-Admin
  caller sees there (`GET /api/workspaces/{tenantId}/members` makes a real,
  Procurement-visible read-only roster buildable for the first time this
  wave; that variant itself lands in `E15/F02/US01/T01`, same phase).
  `RequireRole.tsx` is untouched and, as of this wave, has no caller left
  anywhere in this app -- it is not deleted only because
  `routes/workspace/members/**` (its one former call site) is that sibling
  task's own file scope, not this one's.
- **Role source is a server fact** (`src/components/shell/workspaceRole.ts`,
  task E14/F03/US02/T01, wave w14; ADR-012/ADR-025 w14 footers): `GET
  /api/workspaces`'s row carries `role` (`WorkspaceSummaryBody.role`), and
  `parseWorkspaceRole(wire)` parses it -- there is no more a `?role=` query
  override or a `sessionStorage`-mirrored default (both halves of the
  deleted `resolveWorkspaceRole()`, which defaulted unrecognised roles to
  `"admin"`, are gone; git history has the old function for reference). The
  wire vocabulary is wider than the nav's own two-role model (the backend
  also accepts `Legal` / `Finance` / `ReadOnly`, `memberViewModel.ts:9-11`),
  so `parseWorkspaceRole` maps any role this nav does not model to the
  **least-privileged** modelled role and never to `"admin"` -- carrying the
  old default forward would re-introduce the exact bug it fixed for
  Procurement while reintroducing it for every unmodelled role. The role
  still only decides which affordances *render*; the API's own `403` is the
  authority.
- **Global Ask bar (AC-3)** -- `src/components/ask-bar/GlobalAskBar.tsx`
  renders on every routed screen except `/ask` and `/ask/:conversationId`
  (mounted above `<Outlet/>` in `AppShell.tsx`; suppressed on those two by a
  plain `useLocation()` pathname check, `isAskRoute` in that same file --
  task E25/F06/US01/T01, NW-60 -- because that route renders its own
  composer and a second Ask input on one screen is exactly the duplicate
  ADR-018/ADR-020 forbid). Enter (or a suggestion chip) always opens a **new
  chat**: it navigates to `/ask` with `{ state: { query, newChat: true } }`
  (`useLocation().state` -- `AskRoute` reads `state.query` to seed and ask a
  brand-new conversation immediately, task E13/F09/US01/T04). Cmd/Ctrl+K
  focuses the input from anywhere the bar itself renders; on `/ask` and
  `/ask/:conversationId` the identical shortcut instead focuses that
  screen's own composer input (`src/routes/ask/index.tsx`'s
  `composerInputRef`) -- exactly one of the two components is ever mounted,
  so the two window listeners never overlap. Suggestion-chip copy
  (`src/components/ask-bar/askSuggestions.ts#getAskBarCopy`) fetches
  `GET /api/capabilities` once (task E13/F09/US01/T04, gap G-CAPABILITIES)
  and, once it resolves, prefers that catalog's own `exampleQuestions` for
  the capability key matching the current route; the pre-existing static
  per-route copy is the fallback while the fetch is in flight, fails, has
  no entry for the current screen, or that entry's own `roleGate` is not
  `"any"` and the caller is not Admin (task E25/F01/US01/T01, AC-1;
  ADR-022 S16-11 / ADR-012 w17 cl 40 -- presentation only, `role` threaded
  from `AppShell.tsx`, never re-derived; `GET /api/capabilities` itself
  stays un-gated and identical for both roles) -- never a blank chip row.
  The same role gate applies to `AskRoute`'s own `suggestionsFor` below.
  The placeholder
  itself still switches to "Ask Raffa switches on after your first
  validated contract" while `!kbReady`, regardless of route (ADR-024 V2
  amendment) -- this bar gets the user to `/ask`, it does not answer them
  itself.

**Workspace list is a server query, not a client-side cache** (task
E14/F03/US02/T01, wave w14; ADR-026 §D1) -- `GET /api/workspaces` now
answers from the caller's own real membership, and `App.tsx`'s
`AuthenticatedGate` calls it on mount and whenever the account changes.
Three things that used to live in `src/routes/signin/workspaceStore.ts` died
together when that endpoint landed: the per-account `localStorage` array of
"workspaces this browser has created" (never the same question as
"workspaces this identity belongs to", and unanswerable from a fresh browser
or another device); `contractCount` frozen at `0` on every row (this screen
never called the portfolio API); and `roleLabel` frozen at the string
literal `"Workspace Admin"` (there was no server-issued role claim to read
yet). All three are now real, server-held fields on
`WorkspaceSummaryBody` (`src/api/client.ts`) -- see "Ask Raffa"'s sibling
hook `useValidatedContractCount.ts` for `contractCount`'s other consumer,
and "Role source is a server fact" above for `role`. `currencyRegion` is
still omitted; `currency` is the server's own derivation from the create
form's `country` field (ADR-003 w14 footer), echoed live under Country
during creation, never client-computed once a workspace exists.

What survives in `workspaceStore.ts` is only the **session hint**:
`raffa.signin.currentWorkspace` (`sessionStorage`, not `localStorage` --
switching tabs or reopening the browser should not silently resume a
previous tenant without revalidating it), read by `loadCurrentWorkspace()`
and written by `selectCurrentWorkspace()`. Nothing trusts that value on its
own any more -- `resolveWorkspaceSelection`
(`src/routes/signin/WorkspacePickerScreen.tsx`) checks it against the fresh
`GET /api/workspaces` response on every mount (**`hint ∉ list ⇒ discard the
hint`**, no endpoint, no polling, no cache invalidation of its own:
revalidation against that same GET *is* the mechanism) before entering the
row it names. The resolution order, exactly: empty list -> create form;
exactly one row -> enter it, no picker; ≥2 rows with the hint present in the
list -> enter that row; ≥2 rows with the hint absent or not found -> picker,
hint discarded.

### Documents (ADR-020 screen 3; V1 tasks E06/F05/US01/T01 + E06/F05/US02/T01; V2 rebuild task E13/F09/US01/T03, `raffa-v2/screens-v2.md` #3/#4)

`src/routes/documents/` implements `/documents` as four states
(`index.tsx`'s own header comment; mirrors `raffa-v2/app.jsx`'s own
`docView: 'list' | 'review'` state machine, widened by wave w15 round 3), not
V1's single upload-then-table screen:

1. **Onboarding empty** (`OnboardingEmptyState.tsx`) -- this tenant has no
   tracked document at all, not even an in-flight/rejected one this session
   (`index.tsx`'s `isEmpty`: fetch state is `"ready"` and `documents` /
   `localUploads` / `rejected` are all empty). "First your contracts. Then
   your questions." and the three-step copy (`01 · Upload` / "Drop your
   contracts", `02 · Process` / "Raffa extracts the facts", `03 · Ask` /
   "Ask Raffa") are quoted **verbatim from the literal prototype markup**
   (`raffa-v2/markup.html`), not from `screens-v2.md`'s own shorthand
   summary of the same block ("02 · Review") -- ADR-024 names the prototype
   itself, not a summary of it, as the pixel/copy reference.
2. **List** (`AttentionFilter.tsx` + `DocumentStatusTable.tsx`) -- the
   default once anything exists; server-backed
   (`useDocumentsList.ts`, `GET /api/documents`, R-DOC-06 "reloading the
   browser shows the same list as before"), never a client store -- every
   number on it is the server's own `counts` (ADR-027 §D7).
3. **Review, a state of Documents** (`ReviewState.tsx`,
   `?review=<documentId>`) -- rendered in place of the list, never a
   separate route.
4. **Progress, a state of Documents** (`DocumentProgressPanel.tsx`,
   `?progress=<documentId>`; task E16/F03/US02/T02, wave w15, built by hand
   2026-09-14, ADR-020 w15 footer §10-12) -- same idiom as Review, opened
   from an `"uploaded"`/`"processing"` row's own filename link. Shows the
   six real stages as a checklist against the document's `stage`, calls
   `POST /api/documents/{id}/prioritise` once on open (ADR-027 w15 footer
   C12 -- the Worker takes this document at its next free slot in this
   tenant, ahead of the FIFO), and never auto-navigates: a document that
   finishes while the panel is open offers a link, the user clicks through.

**Upload (dropzone, shared by onboarding and list; `UploadDropzone.tsx`,
`uploadPipeline.ts`)**:

- Widened accept list -- PDF · DOCX · XLSX · PNG · JPG
  (`accept=".pdf,.docx,.xlsx,.png,.jpg,.jpeg"`, PNG/JPG via OCR, D7/ADR-017),
  50 MB / file (`uploadPipeline.ts#MAX_FILE_BYTES`, checked client-side
  purely to skip a doomed round trip -- the server's own `413` stays
  authoritative). A visible "Upload contracts" button plus a
  visually-hidden `aria-label="Choose contract files from your computer"`
  file input (the keyboard-/screen-reader-operable path, ADR-019) +
  drag-and-drop as a progressive enhancement, + two sample buttons,
  **Sample MSA · clean** and **Sample MSA · needs review**
  (`sampleDocument.ts#SAMPLE_DOCUMENTS`): two-page PDFs built in the browser
  (one `/Type /Page` object per page paired with its own `BT ... Tj ... ET`
  stream, exactly the shape `NativeDocumentTextExtractor` reads natively) and
  sent through the same `POST /api/documents` a real upload uses. Two
  different suppliers, two honest outcomes: the Northwind Traders SA MSA
  states every term once with the supplier role labelled and completes
  without review; the Fabrikam Software GmbH MSA names its parties without a
  role, disagrees with its own Schedule 1 on the annual fee and both affirms
  and denies automatic renewal, so exactly those fields come back weak and
  the document lands in `needs_review` with real evidence to show. The
  outcome is still whatever the deployed pipeline returns -- the texts only
  make it meaningful, never scripted.
- **Multi-file, R-DOC-01 AC-1**: up to 20 files per batch
  (`uploadPipeline.ts#MAX_FILES_PER_BATCH`; files beyond the 20th are
  silently dropped from the batch today -- there is no "N files ignored"
  outcome card for the overflow, a known, untested edge case, not a
  deliberate UX decision), at most 3 uploads in flight at once
  (`MAX_CONCURRENT_UPLOADS`, `runUploadBatch`'s own worker-pool loop) -- V1's
  one-at-a-time queue is gone. A row exists **the moment a file is picked**
  (`useDocumentsList.ts#uploadFiles` seeds `localUploads` synchronously,
  before any request even starts), and each file reaches its own terminal
  outcome independently, without blocking the others.
- **The 6-stage client-side pacing ticker is gone.** V1's
  `PIPELINE_STAGE_LABELS`/`getPipelineStageViews` simulated progress while
  one upload request was in flight; V2 shows the real stage
  (`documentTable.ts#DOCUMENT_PROCESSING_STAGES`: Uploading · Classifying ·
  OCR / text · Sections & tables · Extracting facts · Validating schema)
  read straight off `GET /api/documents`'s own `stage` field, polled every
  2 s while any row is non-terminal (`useDocumentsList.ts`, R-DOC-09). A
  stored row that is genuinely `Processing` (the Worker has claimed the job)
  shows the real stage and the bar; a row at `Uploaded` -- stored or, before
  the POST resolves, still local -- reads **Uploaded**, no bar, next step
  **"Processing in the background"** (wave w15 round 3, ADR-020 w15 footer
  §10, task E16/F03/US02/T02, built by hand 2026-09-14: the "Uploading…" /
  "Queued…" readings this section used to name are gone from the row grid --
  the batch reads as loaded the instant it appears; the "Queued, starting
  shortly" reading moved into the Progress panel above, where the six-stage
  checklist now lives). The local row's own "Uploaded" label is the one
  place on this screen that runs ahead of the 201 -- declared and bounded to
  that row's own text, for as long as its own request is in flight. The poll
  has a five-minute no-change budget
  (`components/shell/usePollBudget.ts`, ADR-012 w15 §17): after it, rows
  stay exactly as the server last said and a list-level `.hint` --
  "Nothing has changed for five minutes, so this page stopped checking for
  updates." -- offers **Check again**; nothing is re-labelled. The same
  hook drives Ask's off state, Portfolio's zero state and Contract 360's
  "still being prepared" state (ADR-020 w15 §8.3).
- **"Not added" row, R-DOC-04 / ADR-020 w15 §1 and §6** -- a refused file
  is a *row* reading **Not added** (`.tag-outline`, ADR-019 w15 clause 1),
  never a card. The content gate runs on the Worker after the upload has
  returned, so `not_a_contract` / `no_readable_text` arrive as the server's
  own `Rejected` row with a `rejectionReason` code that
  `uploadPipeline.ts#getRejectionReasonCopy` maps onto the requirements'
  sentences (the "Not added: " lead-in is gone -- the tag is the lead-in;
  an unknown code renders no hint, never a guess); such rows live under the
  **Not added · K** chip, which reads the server's `status=Rejected` bucket,
  and are never in "All documents" (`counts.all` excludes them). An
  oversized file (refused in the browser, no request) and a 413/415
  (refused in-request) render as a **local** row with designed copy --
  `getOversizedCopy` / `getRefusalCopy`; the server's error prose is never
  rendered -- with no next step and no dismiss (a reload clears it). A
  `Quote`-typed outcome offers "Open Quote check" -> `/quotes`
  (`documentTable.ts#getRowAction`, OQ-askv2-008's own assumption: no
  automatic Quote record, just a hand-off).
- **The optimistic row leaves on evidence** (ADR-012 w15 §5): the 201's
  server id is recorded on the local entry, which is dropped only once the
  server list carries that id -- never on a timer, and a failed reload keeps
  the row on screen with the error inline.
- `tenantId` is still read directly from `loadCurrentWorkspace()`
  (`src/routes/signin/workspaceStore.ts`), not threaded as a prop -- the
  same posture V1 already took; `apiClient` is still threaded as a prop
  (`App.tsx` -> `WorkspaceShellApp` -> `DocumentsRoute`).

**List (`useDocumentsList.ts`, `AttentionFilter.tsx`,
`DocumentStatusTable.tsx`, `documentTable.ts`)**:

- **Fetch-once, filter client-side -- the same architecture `getPortfolio`
  established for Portfolio**: `GET /api/documents` fetches the tenant's
  whole list unfiltered (first 100 rows, `LIST_PAGE_SIZE`), and the
  **Needs your attention** (default, R-DOC-06 -- processing / needs_review /
  failed, i.e. everything except `Completed`) / **All documents · N** toggle
  buckets it client-side (`documentTable.ts#filterDocumentsByAttention`) --
  `status` is a real `GET /api/documents` query parameter but is **not**
  how this toggle works (attention is a union of three statuses, not one),
  so the web client never sends it. An empty attention bucket renders
  "Nothing needs you right now." (`.documents-attention-empty`), not a blank
  table.
- **Rows**: Document (filename, page count, uploaded-at; a real `<Link>` to
  Contract 360 once `completed`) · Supplier (`supplierName`, resolved
  server-side when a resolver is registered, else "—" -- no longer the V1
  "always Not yet available") · Type · Status (tag + live stage while
  processing) · action -- **Review N fields** (`?review=<id>`,
  `weakFactCount`) for `needs_review`, **Ask about it**
  (`/ask?scope=<contractId>`, a new chat pre-seeded with "When does
  `{supplier}` expire?") for `completed`, **Retry upload**
  (`POST /api/documents/{id}/reprocess`) for `failed`. **Retry is visible to
  every role** -- only the server enforces Admin-only (a `403` for
  Procurement surfaces inline via `useDocumentsList.ts`'s own
  `retryError`); **Delete is the one action hidden client-side for
  Procurement** (`DocumentStatusTable.tsx`'s `isAdmin` prop, R-WEB-07), with
  an inline "Confirm delete" / "Cancel" step before the real
  `DELETE /api/documents/{id}` call fires.
- **A local (in-flight) upload and a server row are mutually exclusive
  states of the same file, never both**: the moment `uploadDocument`
  resolves into a real document, `useDocumentsList.ts` drops the
  `localUploads` entry and re-fetches the server list, which is what
  actually brings the new row in -- R-DOC-01 AC-1's "row from the moment it
  is picked" therefore spans two different data sources across the upload's
  lifetime, seamlessly from the user's point of view.

**Review, a state of Documents (`ReviewState.tsx`, `?review=<documentId>`,
R-WEB-05)**:

- Reuses `../contracts/review/{ReviewHeader,ReviewFieldList,EvidencePane}.tsx`
  and the shared `../contracts/review/useReviewSession.ts` hook -- the same
  fetch/decision lifecycle the routed `../contracts/review/index.tsx` runs
  (`getContract360`, then `getCorrectionHistory` + `getContractEvidence`
  together, each degrading independently with a visible banner), so the two
  screens can no longer drift on what a decision does. This file only maps
  the hook's phases onto the screen and returns to Documents with the
  validated hook, where the routed screen navigates to Contract 360.
- **"Mark as validated" is a real write.** The hook posts every Accepted
  field's name to `POST /api/documents/{id}/validate` (`validateDocument`);
  the backend moves the reviewed document (`?review=<id>`) from
  `needs_review` to `completed` and writes one `document.validated` audit row
  naming those fields, and only a `200` fires the validated hook. A `409`
  (still processing / failed) or a network failure shows the server's own
  reason under the CTA and stays on the screen. An already-`Completed`
  document reads as closed ("Validated", disabled). Corrections are durable
  the moment they are saved (`PATCH /api/contracts/{id}`); a reload before the
  sign-off re-asks any field that was only session-`Accept`ed.
- **Accept can be a write too.** A `supplier` proposed below the critical
  bar is recorded as evidence but never linked by the pipeline
  (`ReviewFieldRow.proposalPending`); its row shows the proposed name with
  its real weak confidence, and Accept sends it through `correctContract`
  -- that is the correction the backend is waiting for, and what makes the
  supplier appear on the Documents row and in Ask.
- **Confidence and source are real** (`GET /api/contracts/{id}/evidence`):
  each row's tag applies spec §7.3's >95 / 80-95 / <80 thresholds to the
  extraction's own score, the source line reads `p. N · “quoted span”`, and
  the evidence pane renders `FILE · PAGE N`, the passage of page text with the
  span highlighted, and `Extracted by <model> · confidence NN%`. A field with
  no evidence row keeps the conservative "Needs review" tag and says no source
  was recorded -- nothing is ever invented to fill the card.
- On validation, `index.tsx` returns to the list and shows the "*X* is now
  askable." hook (`justValidated`, a single slot, superseded by the next
  upload batch or another validation) with an "Ask: when does it expire?"
  link into a new, scoped Ask chat (`/ask?scope=<contractId>`).

**Provenance -- documented ahead of its own backend counterpart.** This
task (`target_repo: raffa-web`) added `GET /api/documents`,
`GET /api/documents/{id}/preview`, `POST /api/documents/{id}/reprocess` and
`DELETE /api/documents/{id}` to `openapi/raffa-api.v1.json`, plus the
widened `documentType` / `detectedType` enum (the original six members plus
`Quote` / `Invoice` / `PriceList` / `Nda` / `Dpa`) and the `413` / `415` /
`422` admission-gate responses on `POST /api/documents` -- all authored from
`inputs/requirements.md` §6 and the sibling backend tasks' own spec text
(epic-13/feature-04, `task-01-documents-admission.md` /
`task-02-documents-v2-api.md`), **not read off a running handler**: none of
those four operations, the enum widening, or the admission gate existed in
`backend/` in this worktree yet. Task E13/F04/US01/T01 has since landed the
admission gate itself -- `POST /api/documents` really does answer `413` /
`415` / `422` now, and `documentType` really is the widened enum, matching
what was documented here; `GET /api/documents`, the preview, reprocess and
delete operations are still ahead of their backend counterpart (task
E13/F04/US01/T02). See each operation's own OpenAPI `description` for the
exact provenance note. `npm run generate:api`
reproduces `src/api/generated/schema.ts` from this contract today regardless
of backend state (AC-6) -- once the real backend lands, only its own
response shapes need reconciling against what is already documented here,
never the other way around.

The session-backed document tracker is gone (task E16/F03/US01/T01, wave w15): the
rail badge reads `useDocumentCounts.ts` -- see "App shell, navigation, and
the role guard" above -- and no client store stands in for
`GET /api/documents` anywhere.

### Portfolio (ADR-024 V2, `raffa-v2/screens-v2.md` #6; originally ADR-020 screen 4, task E07/F01/US01/T01)

`src/routes/contracts/` is the V2 Portfolio: one list of validated contracts sorted by the soonest
notice deadline, the header summary the prototype's `pfSummary` builds, and the R-WEB-02 reroute
state while nothing is validated. The Day-1 filter chips and attention strip are gone with V2.

- **Fetch once** (`index.tsx`): `GET /api/contracts` for the whole first page
  (`PortfolioPageRequest.MaxPageSize` = 100), then everything is derived in memory
  (`portfolioViewModel.ts`): `buildPortfolioRows` keeps only validated contracts (the shared
  `contractStatus.ts#isValidatedContractStatus` predicate the rail's `kbReady` also uses -- the two
  can never disagree), sorts by days-to-notice ascending (nulls last, then end date, then id) and
  flags rows inside the locked 45-day window (`styles/semantics.ts#isDeadlineCritical`);
  `buildPortfolioSummary`/`formatPortfolioSummary` produce "N validated contracts · CHF 4.2M annual
  · K notice deadlines within 45 days", grouping spend **per currency** (`GET /api/contracts` now
  returns `currency`, added to `PortfolioListItem`/the OpenAPI document for this screen) rather than
  summing across currencies.
- **Table** (`PortfolioTable.tsx`, `contracts.css`) -- Supplier · Contract · Annual spend · Ends ·
  Give notice by (+ "· N d") · Status, 13px tabular figures; **More columns** (`aria-pressed`) adds
  Start · Auto · Risk. A row inside the 45-day window carries the shared `.row-critical` tint + inset
  accent bar and the notice cell turns accent-700 -- text carries the meaning, colour only adds
  emphasis. The Contract cell is a real `<Link>` to Contract 360; the row's own click is the
  prototype's mouse convenience on top. Supplier is the wire's resolved `supplierName` (R-SUP-04) or
  an honest "—"; "Contract" still shows the type label (`Contract` has no title field yet).
- **Category filter** (`PortfolioFilterControl.tsx`, task E24/F01/US02/T01, closes NW-23): a
  free-text control anchored under the header, visible whenever the page has loaded. It reads and
  writes the route's own `?category=` search parameter
  (`portfolioViewModel.ts#readCategoryFilter`/`withCategoryFilter`) -- never a component store or
  `sessionStorage` (ADR-012) -- so applying it re-issues `GET /api/contracts` with `?category=<value>`
  (an exact-match `Supplier.Category`, resolved by the host join task E24/F01/US01/T01 added) and
  clearing it removes the parameter and re-loads the full portfolio. Free text rather than a
  dropdown: no endpoint lists a tenant's distinct categories, and this task's own scope excludes
  adding one, so a hard-coded option list would either invent categories the tenant's suppliers do
  not carry or silently omit real ones -- the council's own "never a hard-coded enum" rule. An
  unmatched value narrows to an empty list, never a fabricated one (backend AC-3); the control stays
  on screen either way so the filter can always be cleared.
- **States** -- loading (`.portfolio-skeleton`), error (503-aware + Retry), and the reroute
  `.screen-reroute` ("Nothing to triage yet · The portfolio lights up from validated contracts.
  Upload one to start." → `/documents`) when nothing is validated yet. The header summary reads
  "Lights up from validated contracts" in that state.

### Contract 360 (ADR-024 V2 "no tabs", `raffa-v2/screens-v2.md` #5; originally ADR-020 screen 5, task E07/F02/US01/T01; citation landing task E13/F10/US01/T01)

`src/routes/contracts/contract360/` is the V2 Contract 360: one page -- header, answers band, "Why —
the clauses behind it", then a "Details ▾" drawer. The Day-1 ten-tab strip is gone; every fact the
tabs held is still on the page, inside the drawer.

- **Fetch order** (`index.tsx`): `GET /api/contracts/{id}` first (a `404` is the named "not found"
  state), then `GET /api/renewals` (this contract's recommendation) and `GET /api/renewals/{id}/
  priority` together, both independently optional -- either failing degrades its own answer to an
  honest "not yet", never the screen.
- **Header** (`Contract360Header.tsx`) -- the origin back link (`resolveBackLink`: Ask Raffa /
  Documents / Portfolio / Renewals / Savings from `state.from`, else a plain "← Back" that walks
  history), the supplier kicker (`resolveSupplierLabel`: the wire's `supplierName`, else the id
  fragment), the type-label title, the meta line `formatHeaderMeta` ("{type} · {spend} / year · N
  documents · {status}"), **Ask about it** (`/ask?scope=<id>`) and **Review extraction**.
- **Answers band** (`AnswersBand.tsx`, `contract360ViewModel.ts#buildAnswers`) -- *Where you can
  save* (the pipeline item's `potentialSavingsRange` and `marketPosition`/uplift lever, honestly
  "Not yet available" until the Benchmark Service produces them), *When you must move* (the notice
  deadline, accent-700 inside the 45-day window, "in **N days** — last day to give notice. Term ends
  {end} and auto-renews for N months."), *What to do* (`buildRecommendation`: the Renewals module's
  own deterministic `recommendedAction`/`explanation`, or the named gap "No renewal recommendation
  for this contract"). The last cell carries **Start negotiation** / **Assign to me** -- the same real
  `POST /api/renewals/{id}/action` the Renewals screen makes, owner = the signed-in `userLabel` --
  or, once acted, the tracker: "{action} · owner {user}", "target … · close by …", the four-step
  checklist (`buildNegotiationSteps`, ticks keyed by the four named steps and persisted through
  `GET`/`PUT /api/contracts/{id}/negotiation-steps`),
  **Track it in Renewals →** and **Undo** (PUT empty ticks, then POST `NotStarted`).
  The recorded action is the same `savedAction` on `GET /api/renewals` that the Renewals list, its
  pane and Contract 360 read, so all three agree. `NotStarted` renders as no action taken.
- **Why — the clauses behind it** (`WhyClauses.tsx`, `ClauseHighlight.tsx`) -- one row per clause (type · accepted value · leverage tag · one-line why · **Open in document viewer**); the selected clause opens the evidence card: the short capped `p.N · §` reference over the original wording with the normalised value `<mark>`ed inside it when it is a literal substring (`buildClauseEvidence`), else the whole `rawText`. No quote and no confidence percentage on the row. The citation landing `?clause=<clauseId>` / `?page=<n>`
  (`resolveHighlightedClauseId`, R-EVD-02) pre-selects the cited clause with no click; an unmatched
  `?clause=` never falls back to `?page=`.
- **Details ▾** (`DetailsSection.tsx`) -- "All terms, documents and open facts ▾" / "Hide details":
  key terms (`buildKeyTerms`, every row kept, unofficialized values as "—"), documents
  (`buildDocumentRows`), a trailing **"N facts still need you — Review all →"** when
  `computeNeedsAttention` counts `review_required` decisions (the line is absent at N = 0), the
  explainable priority score (`buildPriorityComponentRows`, `formatPriorityFact`),
  and the extracted Products / Obligations / Risks through the shared `FactTable.tsx` (no confidence column).
- **Facts vs AI (ADR-019)**: the recommendation is a `Recommendation`, never a `FactRow`; the answers
  band contains no table and no drawer table repeats the recommendation's text --
  `tests/routes/contracts/contract360/*.test.tsx` assert both.
- **Confidence is a 0-1 fraction on the wire**; `toConfidencePercent` is the one conversion point.

### Review / correction (ADR-020 screen 6, task E07/F03/US01/T01, us-01-field-review-correction)

`src/routes/contracts/review/` implements screen 6: AC-1 4-column field list (critical marker,
extracted value + source, confidence tag, decision), AC-2 confidence mapping, AC-3 evidence pane
(correction form + version history), AC-4 gated "Mark as validated" with a visible reason. Reached
from `contract360/Contract360Header.tsx`'s "Review extraction" button and
`contract360/DetailsSection.tsx`'s "N facts still need you — Review all →" count line (both already wired by
task E07/F02/US01/T01 to this exact route).

- **Field set is the backend's real correctable set, not the cited prototype's own mock fields**
  (screens.md #6's "Cancellation notice"/"Price uplift at renewal" have no backend correlate).
  `reviewViewModel.ts#CORRECTABLE_FIELDS` mirrors `ContractCorrectionService.CorrectableFieldNames`
  verbatim (type, status, currency, three more dates, cancellation deadline, annual spend, TCV,
  auto-renewal, renewal term, payment terms, governing law) -- the only fields `PATCH
  /api/contracts/{id}` will ever accept, and the story's own "E02 correction API (assumed)"
  dependency. A field renders only when the contract actually has a value for it (no "—" rows).
- **No live per-field confidence exists yet for this field set -- a real, pre-existing backend
  gap, not one this task's file scope can close.** `ExtractionEvidence`
  (`backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs`, task E02/F01/US02/T01)
  stores exactly the per-field confidence/source-span/extraction-job trail AC-2/AC-3 describe, keyed
  by the same `FieldName` scheme `CorrectionHistory` already uses -- but no endpoint in
  `backend/src/Raffa.Api` reads it, and `Contract360QueryService` never joins it either. Until a
  backend task adds a read endpoint (e.g. `GET /api/contracts/{id}/evidence`), every undecided field
  is conservatively treated as spec §7.3's <80% "must be reviewed" band -- never a fabricated
  percentage (Appendix C rule 10) -- so AC-4's gate is still real and testable today. The evidence
  pane's "Source" line is an honest "not yet available" note for the same reason (no highlighted
  passage to show). See `reviewViewModel.ts`'s own header comment for the full provenance.
- **Two real, working decisions, one durable and one not.** "Correct" calls the real `PATCH
  /api/contracts/{id}` (a genuine value change, a new `ContractVersion`, and a new
  `CorrectionHistory` row an immediate re-fetch of `GET /api/contracts/{id}/corrections` picks back
  up as "Corrected"). "Accept" has no backend call to make -- `ContractCorrectionService
  .CorrectAsync` rejects a no-op correction outright, so there is no way to durably record "a human
  looked at this and it was already right" -- it is this screen's own session-only React state and
  does not survive a reload. An honest consequence of the real API's shape, not a bug; see
  `index.tsx`'s own header comment.
- **AC-4 gate**: `reviewViewModel.ts#isFieldBlocking`/`computeReviewProgress` -- a resolved field
  (accepted or corrected) never blocks; a pending field blocks under a real <80% score, or -- always,
  today -- when no score exists (the conservative default above). "Mark as validated" is a real
  `<button disabled>` paired with a visible `.hint` reason (ADR-019 accessibility baseline), and
  navigates to `/contracts/:id` on click (screens.md #6's own `finishReview` behaviour).

**Task E11/F07/US01/T01** (gap G-REV, `reports/audit/visual-fidelity-gaps.md`) brought this screen's
markup/CSS in line with the export: the `<h2>` now reads "Review extraction" (was "Review &
correction" -- matching this exact route's own CTA label on `contract360/Contract360Header.tsx`), the
kicker gained "· Human validation", and a one-line summary (supplier id + contract type, the same
honest substitutes `Contract360Header.tsx` already uses -- `Contract` has no supplier-name/filename
field) sits under the title. The progress line gained the export's own confidence legend
(`>95% auto-accepted` / `80-95% flagged` / `<80% review required`). The field table's fixed-layout
columns now carry the export's own narrow-field/wide-value/auto/auto proportions (were four equal
columns), the extracted-value cell truncates instead of wrapping, "Correct" is `.btn-ghost` (was
`.btn-primary`), and the evidence pane's honest "not yet available" source note sits inside the
export's own white/serif "highlighted passage" card (`.review-evidence-passage`) -- container only,
never a fabricated document name/page/quote. See `review.css.test.ts` for the CSS-source proof
(`test.css: false` means no computed style exists to assert against under jsdom, same reasoning
`signin.css.test.ts` already documents).

### Ask Raffa (ADR-020 screen 7 / ADR-024 §6, task E07/F04/US01/T01; V2 rebuild task
E13/F09/US01/T04, us-01-web-v2 AC-1/AC-3/AC-5/AC-6, `raffa-v2/screens-v2.md` #2)

`src/routes/ask/` implements screen 2: one screen, four faces (off / new chat / conversation /
resume -- `turns.length === 0` vs `> 0` and two route-derived ids inside one component, not four
separate components; see `index.tsx`'s own header comment for the full state-machine reasoning).
Reached from the global Ask bar (`components/ask-bar/GlobalAskBar.tsx`, Enter or Cmd/Ctrl+K) on any
screen, directly at `/ask`, a rail conversation click, or Contract 360's "Ask about it"
(`/ask?scope=<contractId>`). Replaces the V1 single-turn `POST /api/chat/query` screen this same
task deleted (`ChatMessage.tsx`, `askViewModel.ts#ROUTE_LINE_BY_INTENT`) with real, resumable,
per-user conversations.

- **Off** (`AskOffState.tsx`, R-ASK-10) -- gated by `useValidatedContractCount`'s `kbReady`, the same
  shell hook `AppShell.tsx`/`GlobalAskBar.tsx` already share for the identical signal, never
  re-derived here. Fixed headline ("Ask needs at least one validated contract.") never varies; the
  reason + CTA do (`askViewModel.ts#buildOffCopy`, three states read off `GET /api/documents`'s
  own `counts` -- never `totalCount`, which counts a failed document too; task E16/F03/US01/T01,
  ADR-020 w15 §2.1): "Upload a contract first" / "Upload a contract" when Raffa.ai holds no
  document (`counts.all === 0`, so a tenant holding only refused files lands here), "still
  processing or waiting for review" / "Go to Documents" while a document is `Uploaded`,
  `Processing` or `NeedsReview`, and "Raffa.ai could not finish processing your documents." / "Go
  to Documents" when documents are held but none is in flight or validated. While in flight the
  one-row read repeats every 2 s under the shared five-minute budget (`usePollBudget.ts`) and the
  gate re-reads the validated count, so Ask switches on without a reload; once the budget is spent
  the block adds "Check again" beside its CTA.
- **New chat** -- hello line (`ASK_HELLO`, "What do you want to know?"), scope line naming the
  validated count (`askViewModel.ts#buildScopeLine`, "Answers only from N validated contract(s) ·
  cites or abstains", the parenthetical supplier-name list omitted honestly until a future backend
  task resolves it) plus the prototype's structured/legal trailer sentence, input placeholder "Ask
  Raffa — spend, dates, clauses, liability…", two suggestion chips (`suggestionsFor`) from the
  `ask` capability's own `exampleQuestions` (`GET /api/capabilities`), falling back to a small static
  pair while the catalog has not loaded. Asking (typed, a chip, or the seed query the global Ask bar
  carries in router state, `newChat: true`) runs `createConversationAndAsk`:
  `POST /api/conversations` (with `scopeContractId` when `?scope=` is present) then
  `POST /api/conversations/{id}/messages`, then the URL becomes `/ask/<conversationId>`
  (`navigate(..., { replace: true })`). `?scope=<contractId>` (Contract 360's "Ask about it") templates
  the two chips with the real supplier name instead (`buildScopedSuggestions`, read off
  `GET /api/contracts/{id}`'s typed `supplierName`; "this supplier" when it is null or blank) **and**
  briefs the contract instead of rendering the generic hello/scope line (task E25/F03/US02/T01,
  NW-56, `askViewModel.ts#buildScopedBrief`): a `.screen-kicker` naming the supplier ("this contract"
  while the name is still loading or unknown), a heading "Ask about {supplier}", and a one-line
  contract-specific scope ("Answers cite this contract's pages.") in place of the N-validated-
  contracts sentence, which would otherwise misdescribe a chat scoped to one contract. The off-state
  gate above is unaffected -- a scoped link into a tenant with zero validated contracts still renders
  the generic `AskOffState`, never a briefed-but-off face.
- **Conversation** -- header shows the derived title (`deriveConversationTitle`, collapsed
  whitespace, hard-truncated at 48 chars, no ellipsis) + "+ New chat"; every turn renders through the
  phase-2 `ReplyBody` (task E13/F09/US01/T02, `routes/ask/reply/*`, this task maps the wire reply
  onto it -- `askViewModel.ts#mapConversationReplyToReply`/`mapConversationMessageToReply` -- but
  does not modify that renderer itself): `answer` gets markdown + numbered citation cards + actions +
  follow-up chips, `redirect`/`refusal` share warm prose + one CTA, `abstain` is the accent-left
  block, `error` is a transport/400 failure -- never confused with an abstain. Citation clicks
  resolve by corpus (`resolveCitationOpenAction`): a **tenant** citation navigates to
  `/contracts/<contractId>?page=<n>` (the real backend never sends `?clause=` yet -- confirmed
  against `AskCopilotService.cs`'s own `PackItem` constructions, a documented, honest gap, not a
  guess) with `state.from: "ask"`, which Contract 360's own back-link picks up; a **market** citation
  opens `MarketRecordPanel.tsx` (`GET /api/market/records/{id}`: title, category, geography,
  P25/P50/P75 band, provenance label, updated date); a **raffa** feature citation navigates to its
  own href. Follow-up chips post as a new message in the same conversation, the same `ask()` path a
  typed question uses.
- **Resume** (`/ask/:conversationId`, R-CONV-02 AC-1) -- `useConversation.ts` loads the conversation
  (`GET /api/conversations/{id}`) and turns every stored message, oldest first, into the same turn
  shape a live turn produces (`askViewModel.ts#buildTurnsFromConversation`); a resumed Raffa turn's
  `followUps` is always empty (`ConversationMessage` has no such column on the wire -- an honest
  limitation, not an oversight). A `404` (unknown id, another tenant's, or another user's -- one
  honest outcome per that operation's own OpenAPI description) renders a named "Conversation not
  found" state with a "+ New chat" link, never a generic error.
- **Rail** -- the shell's nested conversations slot (see "App shell" above); `RailNav.tsx` consumes
  `useRecentConversations.ts` itself, not threaded through as a prop from a fetch-once parent.
- **No raw ids, no route line, no V1 copy anywhere** (R-ASK-08) -- `Reply`
  (`routes/ask/reply/replyTypes.ts`) carries no `route`/raw-id field for any variant to leak; a
  tenant citation's deep link is built client-side from `contractId`/`page` only. `"Structured
  query…"`, `ROUTE_LINE_BY_INTENT`, and a raw `Document:<guid>`/`Clause:<guid>` chip are gone with
  the V1 screen this task deleted -- the "thinking" copy (`THINKING_COPY`) is the one line kept
  verbatim.
- **`Authorization: Bearer <token>` on every call** (`client.ts`, task E18/F01/US02/T01, NW-05;
  ADR-012 w15 footer clause 1) -- see "API client" below for the full header provenance; the interim
  `X-User-Id` header this bullet used to name is deleted, not conditional.

### Renewals (ADR-024 V2, `raffa-v2/screens-v2.md` #7; originally ADR-020 screen 8, task E08/F01/US01/T01)

`src/routes/renewals/` is the V2 Renewals screen: a header with the prototype's `rnSummary`, one list
sorted by priority, the selected row's **Why it is here** pane, and the reroute state while nothing
has validated dates. The Day-1 threshold strip, seven-column table, six-fact card and "Snooze" are
gone with V2.

- **Fetch order, gated on the whole score set** (`index.tsx`): `GET /api/renewals` first (a non-2xx
  is the named "Renewals unavailable" error with Retry), then every row's own
  `GET /api/renewals/{contractId}/priority` together before declaring ready -- Score is the sort
  key. One row's failure degrades only its score to "—" (it sorts last), never the screen.
- **List** (`RenewalTable.tsx`, `renewalPipelineViewModel.ts#buildRenewalRows`) -- Score (heading
  face, accent-700 from 80 up) · Supplier · contract · Renews in · Notice in (accent-700 inside the
  45-day window) · Status, sorted by score descending (ties: sooner notice, then id). The selected row
  carries the neutral-200 background + accent bar; selection is a native button in the Score cell,
  the row click a convenience on top. Supplier is the wire's name or "Supplier not resolved";
  `GET /api/renewals` carries no contract name, so the contract half is "Contract {short id}" with the
  full id as tooltip. Status is "Open" until a persisted `savedAction` exists on the list row
  (`NotStarted` is treated as no action taken). A reload or a second browser shows the same status.
- **Why it is here** (`InsightCard.tsx`) -- "{supplier} — N days to notice", the contract line, the
  accent "Recommended action" kicker, the Renewals module's own deterministic action + rationale,
  **Start negotiation** (primary → InProgress / "In negotiation") and **Assign to me** (NotStarted /
  "Assigned"), both the real `POST /api/renewals/{id}/action` with owner = the signed-in `userLabel`;
  once acted, the bordered "{action} · owner … Open contract →" box. "See the facts behind this →"
  opens Contract 360.
- **States** -- loading, error, and the reroute "No renewal dates yet · Renewals are computed from
  validated end dates and notice periods. Upload a contract to start." → `/documents`.

### Quote check (ADR-024 V2, `raffa-v2/screens-v2.md` #9; originally ADR-020 screen 10, task E08/F03/US01/T01)

`src/routes/quotes/` is the V2 Quote check: a constant header ("Optional · new purchase" · "Quote
check" · "Drop a supplier proposal; …"), the landing drop card plus this workspace's own quote check
history, and -- once a quote is loaded -- the three-cell band, the lines table and the "one step
further" footer. The Day-1 four-step stepper is gone; the same real calls remain.

- **Real backend, not the prototype's fixture** -- `POST /api/quotes`,
  `POST /api/quotes/{id}/assessment/recalculate` (called with an empty `mappings` array as the
  documented "pure refresh" read; it is the only call that also returns `unmatchedLines`),
  `GET /api/quotes/{id}` (the quote plus recorded outcomes, newest first),
  `GET /api/quotes/benchmark-history` (workspace-wide, loaded once on mount independent of which
  quote if any is open; task E25/F04/US02/T01, closes NW-57) and `POST /api/negotiations/outcomes`.
  One named gap remains: no HTTP endpoint for `NegotiationStrategyService`'s lever recommendations
  (`NegotiationStep.tsx`).
- **Landing** (`UploadQuoteForm.tsx`, `sampleQuote.ts`) -- the dashed card: **Upload a quote**
  (file picker; drag-and-drop on the card) uploads straight away, "or use the sample: Databricks
  proposal Q-88213" sends a real PDF built with `documents/sampleDocument.ts#buildSamplePdf` (three
  priced lines) with its own supplier/currency/geography -- whatever the real pipeline extracts is
  the honest answer. Supplier · currency · geography · purchase date stay reachable under a compact
  disclosure (the Benchmark Service cannot match without them).
- **Quote check history** (`history/QuoteHistoryList.tsx`; ADR-028 -- history is server state; task
  E25/F04/US02/T01, closes NW-57) -- also on the landing: every quote this workspace has ever
  checked, newest first, read back from `GET /api/quotes/benchmark-history` rather than a client
  store, so a reload or a second browser sees the same list. Each row (file · supplier · currency ·
  geography · checked-in date) re-opens `/quotes/:id`; its own tag is a real position tally or, for a
  genuine first-of-type quote, the identical honest cold-start label the loaded result uses below --
  never re-derived, so the two surfaces can never disagree. An empty workspace gets its own "No quote
  checks yet" state, not the reroute other screens use.
- **Loaded** (`quoteCheckViewModel.ts`, `QuoteLinesTable.tsx`) -- `buildAssessmentBand`: Supplier
  quote (`sum(unitPrice × quantity)`) · Market range (`sum(P25..P75 × quantity)`) · Assessment
  (`summarizePositions`, a real tally such as "2 above market · 1 in line" -- the backend deliberately
  has no quote-level rollup); `buildQuoteLineRows`: Line · Quoted (`formatUnitPrice`, decimals kept)
  · P50 · Position (tag + "+20% vs P50", `formatVersusP50`) · Benchmark (confidence tag "High ·
  n=96"). Zero extracted lines renders an honest note, never a scripted table. Both the band and the
  line table now live in `assessment/AssessmentResult.tsx`, not inlined in `index.tsx`.
- **First-of-type cold start** (`AssessmentResult.tsx#isQuoteBenchmarkColdStart`; ADR-028; task
  E25/F04/US02/T01, closes NW-57) -- when every line reports the backend's own honest
  `InsufficientBenchmarkData` status (a genuine first-of-type quote, nothing comparable on file yet
  for its supplier/product), the band still shows only its real "Not yet available"/"Not yet assessed"
  cells and a plain-language note explains why the position is missing -- no fabricated figure, no
  digit in the copy itself. One real line among otherwise-cold ones is never treated as a full cold
  start.
- **One step further** -- the footer "Target and negotiation levers are one step further — shown only
  if you want them." toggles `TargetStep.tsx` (price ladder, editable target/walk-away seeded once from
  the real aggregate), whose "Build negotiation strategy →" reveals `NegotiationStep.tsx` (outcome
  capture; the recorded panel always renders the server's own `realizedSaving`/`discountPercent`, then
  links "See it in Savings →", secondary styling -- the benchmark result above stays on screen
  throughout and is never replaced, AC-3 of task E25/F04/US02/T01).
- **Blocked assessment** (`MappingBlock.tsx`, `isAssessmentBlocked`) -- while any line is still
  `SkuMatchStatus.Unmatched` the band shows what it honestly can, the line says "Needs mapping", and
  the mapping block (free-text canonical SKU / product name per line, one recalculate call) takes the
  footer's place; `mergeKnownLineDetails` keeps each line's real description once it resolves.

### Savings (ADR-024 V2 "No Home item", `raffa-v2/screens-v2.md` #8; originally ADR-020 screen 9, task E08/F02/US01/T01; moved to `/savings` by task E13/F09/US01/T01)

`src/routes/savings/` is the V2 Savings screen: a header with a summary, three KPI cells, and the
opportunities table -- reached from Ask actions, Renewals and Contract 360, not the rail. The Day-1
six-cell row and eight-column table are gone with V2.

- **Three independent fetches, independent degrade states** (`index.tsx`) -- `GET /api/savings/kpis`
  backs the KPI band and degrades to a stale-labelled last-known state (`reduceKpiFetch`: "Benchmark
  provider unreachable" + Retry refresh, every cell tagged `Stale`, never blanked); `GET /api/savings`
  backs the table with its own scoped error + Retry; `GET /api/contracts` only supplies supplier
  *names* (`buildSupplierNameIndex` -- `SavingsOpportunityResult` carries a supplier id only) and
  falls back to the id fragment when unavailable.
- **KPI band** (`KpiRow.tsx`, `buildKpiCells`) -- Contracts analyzed ("CHF 6,270,000 annual spend"),
  Upcoming renewals ("auto-renewing contracts in the pipeline"), Savings identified (one range per
  currency, never summed across currencies; "6 identified · 2 in progress · 1 realized"). `kpis ===
  null` renders "—", never a fabricated number. The header summary (`formatSavingsSummary`) reads
  "N opportunities · CHF 410,000–590,000 identified", or "Lights up from validated contracts and
  actioned renewals".
- **Opportunities** (`OpportunitiesTable.tsx`, `buildOpportunityRows`) -- Supplier · Action (the
  opportunity type) · Estimate (+ confidence tag "High · 92%") · Status; the Supplier cell is a real
  `<Link>` to Contract 360 (`state.from = "savings"` drives its back label), the row click a
  convenience on top; an opportunity with no `contractId` opens `/quotes`. Only real
  `SavingsOpportunity` rows render (a session-tracked renewal action is no longer prepended).
- **Filters** (task-01-savings-filters, ADR-020; `savingsFilters.ts`, `filterOpportunityRows`) --
  three native `<select>`s anchored above the table restrict it by supplier / status / currency, the
  council's exact set (AC-2); "Estimate" stays a sort/numeric column, never a filter. Options are
  derived from whichever opportunities are already loaded (supplier labels resolved the same way the
  table itself resolves them), never a hardcoded or fabricated list, and never shrink each other --
  each dropdown's own options always come from the full, unfiltered list. Pure client-side view
  state: filtering never re-fetches and is never written to storage; "Clear filters" (disabled while
  no filter is active) restores the full list (AC-3), and a filtered-to-empty result gets its own
  "No opportunities match the selected filters" message rather than the reroute below.
- **Reroute** -- "No savings opportunities yet · Opportunities appear once a renewal is actioned or a
  saving is identified from validated contracts." → **Open renewals**. (Only for a genuinely empty
  opportunities list -- a filter narrowing a non-empty list to zero rows never reaches this state.)

### Workspace & members (ADR-024 V2, `raffa-v2/screens-v2.md` #10; ADR-020 w14 design footer screen 10; task E15/F02/US01/T01, wave w14)

`src/routes/workspace/members/` is the V2 Workspace & members screen: the "Setup" header (tenant
meta line, the workspace name when the shell also threads one), the tip "invite the team once the
first contract is validated — there is nothing for them to ask before that." while the shell reports
no validated contract (`components/shell/shellContext.ts#useShellContext`), and a **server-read**
roster (`GET /api/workspaces/{tenantId}/members`, re-read after every invite/revoke/remove -- never
an optimistic local guess): a loading skeleton, an error state with Retry that never falls back to a
stale roster, and the Member / Role / Status table (`MembersTable.tsx`; the signed-in row says "You";
no display name is derived from the email; `Active` / `Invited` / `Expired` tags).

An Admin also gets a fourth `Actions` column and the **Invite a colleague** pane (`InvitePane.tsx`):
"Work email" with a `name@{domain}` placeholder and a *non-blocking* cross-domain warning (format
errors still block; the domain check is a typo guard, never a safeguard -- the server has no domain
rule), Procurement first then Workspace Admin with the D8 summaries "Asks, uploads, reviews, triages
renewals" / "Also deletes documents and manages members", block **Send invitation**, then the
server's own `deliveryOutcome` string in exactly three states (task E17/F02/US01/T01, wave w15;
ADR-020 w15 §3): "Invitation sent to {email}." (no link), "Invitation created, but the email could
not be sent." (the copyable link is the remedy) or "Invitation ready for {email}." (no transport
configured) -- the last two with the single-use link (`new URL(acceptUrl, window.location.origin)`,
absolute since the API composes it from `Invitations__AcceptUrlBase`), its expiry and a **Copy
link** button -- plus "They will get a one-time code from Microsoft the first time they sign in."
while the 201's `identityProvisioned` is true. The client never infers delivery from a 201 and
never combines `mailDelivered` with the outcome. A declared **502** (the company directory would
not provision the guest -- `consent_missing` / `provisioning_failed` / `directory_unavailable`, plus
a catch-all so no raw code is ever rendered) renders its designed sentence with "No invitation was
created." beneath it; there is no "resend" affordance on the pane, because the server cannot re-send
the original link and any retry is a re-issue that kills it -- the roster row's **Send a new
invitation** is the one re-issue path. `Actions` lets an Admin **revoke** an `Invited` row (`DELETE .../invites/{id}`) or
**remove** an `Active` one (`DELETE .../members/{membershipId}`), each confirmed **inline in the
row** (never a dialog) with its own consequence copy -- revoke never claims a grant that never
existed; remove says the person loses access -- and the sole remaining Admin's Remove renders
`disabled` with a `.hint` rather than a click that could 409.

A Procurement visit renders the same roster **read-only** (no `Actions` column, no invite pane) with
a real `mailto:` **Request access** to the workspace's own live Admin addresses, resolving the
apparent conflict between the export (hides the table for non-admins) and ADR-018/`ia-v2.md`
(read-only) in favour of the latter now that a real roster exists to show. The deleted
`memberViewModel.ts`-adjacent per-browser echo module used to seed the current Admin locally and
remember each invite in this browser's own storage for the running tab -- a discovery gap masquerading
as a roster; every row on screen is now a fact the server actually holds.

## API client (ADR-012 "one generated TypeScript client, no hand-written divergent DTOs")

Task E01/F07/US01/T02 ("Generate TS API client from OpenAPI; wire /health"):

- `openapi/raffa-api.v1.json` is the single OpenAPI document this client is
  generated from (AC-3). It documents exactly the routes
  `backend/src/Raffa.Api/Program.cs` implements today -- `GET /health` and
  (task E06/F03/US01/T01) `POST /api/workspaces` -- cross-checked against
  `backend/tests/Raffa.Api.Tests` and `backend/src/Raffa.Api
  /WorkspaceEndpointExtensions.cs` respectively.
  **Interim provenance**: the API host does not yet self-publish this document
  (no `Microsoft.AspNetCore.OpenApi`/Swashbuckle/NSwag wired into
  `Program.cs`, and adding that is backend work outside this task's
  `target_repo: raffa-web` scope). This file must grow endpoint-by-endpoint
  as the backend does, and be replaced outright once the API self-publishes
  its own document. It also does **not** apply a `/v1`-style URL prefix:
  OQ-client-007 (`reports/open-questions.md`) leaves that choice open, and
  `Program.cs` itself serves bare `/health`, not `/v1/health` -- a client
  generated against a path the API does not actually serve would 404 in every
  real environment.
- `npm run generate:api` (`scripts/generate-api-client.mjs`) reads that
  document and writes `src/api/generated/schema.ts` (`paths`/`operations`
  TypeScript types -- committed, but marked auto-generated/do-not-edit).
  `npm run build` runs it first, so the committed output can never silently
  drift from the contract. Task E06/F03/US01/T01 extended the schema-type
  renderer with `object` support (inline `{ prop: T; ... }` from a schema's
  `properties`/`required`) -- `POST /api/workspaces`'s `201` body is the
  first response that needed it, unlike `GET /health`'s plain string. Request
  bodies are still hand-written (the generator does not parse `requestBody`
  at all yet); see `src/api/client.ts`'s header comment for why that is
  enough for now.
  **Codegen tool choice** (also part of OQ-client-007): the mainstream
  option, `openapi-typescript@7.13.0`, peer-depends on `typescript@^5.x`,
  which hard-conflicts under npm's default strict peer resolution with this
  repo's already-committed `typescript@^7.0.2` (`package.json`, task
  E01/F07/US01/T01) -- verified by running
  `npm install --save-dev openapi-typescript`, which fails with `ERESOLVE`.
  Rather than force an incorrect peer resolution (`--legacy-peer-deps`) or
  downgrade a previous task's already-committed TypeScript version, this repo
  uses a small first-party generator with zero dependencies of its own. It
  emits the same `paths`/`operations` shape the mainstream tools use, so
  swapping to one later (once it supports TypeScript 7) only means deleting
  `scripts/generate-api-client.mjs` -- `src/api/client.ts` does not change.
- `src/api/client.ts` is the hand-written (thin) transport layer on top of
  those generated types -- `createApiClient(baseUrl).getHealth()` and (task
  E06/F03/US01/T01) `.createWorkspace({ name })` -- the same division of
  labour `src/config/appConfig.ts` uses (generated/validated shape,
  hand-written `fetch` plumbing). Both deliberately never throw on a non-2xx
  response (an "Unhealthy" 503, or a `400` blank-name validation failure, are
  valid expected answers, not client errors) or on a network failure
  (resolves with `statusCode: null` instead), so callers can render the
  result directly without a try/catch.
- `src/App.tsx` calls `getHealth()` on mount and renders the result
  (`data-testid="api-health-status"`) independent of sign-in state -- the
  "wire /health" half of task E01/F07/US01/T02, and this static SPA's
  equivalent of that task's parent story's Definition of Done ("`curl` on
  `/health` via the API client succeeds": every load of the deployed bundle
  performs that check). `src/routes/signin/WorkspacePickerScreen.tsx` calls
  `createWorkspace()` (see "Screens" above).
  **Task E11/F01/US01/T01** took the rendered `API: ...` line off the visual
  canvas (the compiled prototype has no such line) via `.visually-hidden`
  (`src/styles/base.css`) -- a clip-based technique, not `display:none`, so
  the probe keeps running every mount and `data-testid="api-health-status"`
  stays resolvable in the accessibility tree for tests. See "Design system"
  below for the sheet, and `tests/App.test.tsx` for the coverage.
- **Task E06/F01/US01/T01 (typescript-client-regen)** caught the contract up
  to backend epics E02-E05: `openapi/raffa-api.v1.json` gained
  `POST /api/workspaces` (create), `POST /api/workspaces/{tenantId}/invites`
  (invite), `POST /api/documents` (upload), and `GET /api/documents/{id}`
  (read back) -- exactly epic-06-web-foundation's own R0 surface (sign-in ->
  workspace picker, members & roles, document upload/status). `Program.cs`
  now also serves many more routes from those same backend epics (portfolio,
  Contract 360 + correction history, audit, renewals, savings (+ KPIs), Ask
  Raffa chat, quotes, negotiation outcomes) that this task deliberately did
  **not** add to the contract: epic-06-web-foundation's own "Out of scope"
  list names exactly that set as "later web epics," and
  feature-01-typescript-client-regen is a documented **repeating chore** --
  whichever web epic first builds a screen against one of those endpoints
  extends `openapi/raffa-api.v1.json` next, the same way this task extended
  the `/health`-only version task E01/F07/US01/T02 left behind. This also
  taught `generate-api-client.mjs` two more `renderSchemaType` cases (still
  zero dependencies): a flat `object`/`properties` schema, and an OpenAPI 3.1
  nullable union (`"type": ["string", "null"]`, e.g. a freshly-uploaded
  document's `contractId`, null until classification links it) -- see that
  script's own comments.
- **Task E06/F05/US01/T01 (document-upload)** added `client.ts`'s second
  write call, `.uploadDocument(tenantId, file)` -- `multipart/form-data`
  against `POST /api/documents`, same never-throws shape as the other calls.
- **Task E06/F05/US02/T01 (document-status-readback)** added `client.ts`'s
  `.getDocument(tenantId, id)` wrapper around `GET /api/documents/{id}`,
  already generated in `src/api/generated/schema.ts` since task
  E06/F01/US01/T01 but left unwrapped until this task (the "schema.ts can be
  ahead of client.ts" pattern `inviteWorkspaceMember` still leaves
  unwrapped) -- see "Documents" above for the document table that calls it.
  A `404` (its OpenAPI response carries no body at all, unlike the `400`s'
  JSON-string bodies) is special-cased rather than attempting to parse an
  empty body as JSON.
- **Task E07/F01/US01/T01 (portfolio-list-filters)** extended
  `openapi/raffa-api.v1.json` with `GET /api/contracts` (operationId
  `getPortfolio`) -- the repeating chore `typescript-client-regen`'s own doc
  comment named ("whichever web epic first builds a screen against one of
  those endpoints extends the contract next"). Its 200 response's `risk`
  field is deliberately typed as a bare nullable string, not a nullable enum:
  `generate-api-client.mjs#renderSchemaType` checks a schema's `enum` before
  it checks whether `type` is a nullable union, so the two combined lose the
  `null` member -- every enum this document declared before this task was
  non-nullable, so that combination never came up. Rather than patch the
  generator (outside this task's own `src/routes/contracts/` scope), the
  operation's own schema omits `enum` for that one field; `client.ts` names
  the real four-value wire set (`Low`/`Medium`/`High`/`Critical`) as its own
  hand-written `PortfolioRiskSeverity` alias instead, with a runtime
  `isPortfolioRiskSeverity` guard at the one place a raw string crosses into
  it. `client.ts`'s `getPortfolio(tenantId, query?)` mirrors the endpoint's
  full filter/paging surface; `src/routes/contracts/index.tsx` calls it with
  `pageSize` and, since task E24/F01/US02/T01, the route's own `?category=`
  search parameter when set (see "Portfolio" above) -- every other filter
  field on `PortfolioQueryParams` is still unused by this screen.
- **Task E07/F02/US01/T01 (contract-360)** extended `openapi/raffa-api.v1.json` with three more
  operations -- `GET /api/contracts/{id}` (`getContract360`), `GET /api/renewals` (`getRenewals`),
  and `GET /api/renewals/{contractId}/priority` (`getRenewalPriority`) -- the same "repeating chore"
  `typescript-client-regen`'s own doc comment named. `header.risk` and `tabs.clauses[].riskLevel`
  reuse the same bare-nullable-string workaround `getPortfolio`'s own `risk` field already
  established (the generator drops `null` when `enum` and a nullable `type` union combine);
  `tabs.risks[].severity` is a real non-nullable enum since the wire field is required. `client.ts`'s
  three new methods follow the same never-throws convention as every other call (a `404` on
  `getContract360`/`getRenewalPriority` is a normal, expected outcome the caller renders as a named
  "not found" state).
- **Task E07/F03/US01/T01 (field-review-correction)** extended `openapi/raffa-api.v1.json` with `PATCH
  /api/contracts/{id}` (`correctContract`) and `GET /api/contracts/{id}/corrections`
  (`getCorrectionHistory`) -- the third web epic to extend this document (same "repeating chore"
  provenance paragraph). Both operations were already implemented by backend task
  E02/F05/US01/T01/T02; this task only wraps them for the web client. `correctContract`'s request
  body (`CorrectContractRequest`) is hand-written, like `createWorkspace`'s -- the generator does not
  parse `requestBody`. Both response shapes (a flat object, and an array of a flat object) use
  generator cases `getContract360`/`getPortfolio` already exercise, so no generator change was
  needed this time.
- **Task E08/F01/US01/T01 (renewal-pipeline, ADR-020 screen 8)** extended `openapi/raffa-api.v1.json`
  with `POST /api/renewals/{id}/action` (`postRenewalAction`) -- the fourth web epic to extend this
  document (see "API client" provenance paragraphs above). Already implemented by backend task
  E03/F03/US01/T02; this task is the first web caller (the insight card's three actions). The 200
  response's `status` field is a real, closed three-value enum (`NotStarted`/`InProgress`/`Completed`)
  declared with `enum`, the same way `GET /api/renewals`'s own `status` field already is, so
  `RenewalActionStatusValue` is derived from the generated response type rather than hand-duplicated.
  Unlike every other write operation in this client, a well-formed request against an unknown or
  cross-tenant contract id still succeeds (`Raffa.Renewals` cannot reference
  `Raffa.Documents.Contracts` at all, ADR-002) -- `postRenewalAction` never special-cases a 404 the
  way `getContract360`/`getRenewalPriority`/`correctContract` do for theirs.
- **Task E08/F02/US01/T01 (savings-home, ADR-020 screen 9)** extended `openapi/raffa-api.v1.json`
  with `GET /api/savings/kpis` (`getSavingsKpis`) and `GET /api/savings` (`getSavingsOpportunities`)
  -- the fifth web epic to extend this document (see "API client" provenance paragraphs above). Both
  were already implemented on the backend -- `getSavingsKpis` by task E04/F03/US01/T01
  (savings-kpis), `getSavingsOpportunities` by task E04/F02/US02/T01 (savings-opportunity), with its
  `confidenceLevel` field added by task E04/F03/US01/T02 (savings-list) -- this task is the first web
  caller of either. `getSavingsOpportunities` never 404s -- an empty `items` array is a tenant's own
  honest "no opportunities yet" answer, the same convention `getRenewals` already follows for its own
  tenant-scoped list. `getSavingsKpis`'s 200 shape reuses the flat-`object` generator case
  `getPortfolio`/`getContract360` already exercise for each of its four per-currency array fields
  (`annualSpendAnalyzed`/`savingsIdentified`/`savingsRealized`/`savingsInProgress`) -- no generator
  change was needed this time.

- **Task E13/F09/US01/T03 (web-documents-v2, ADR-024 V2 rebuild)** extended `openapi/raffa-api.v1.json`
  with `GET /api/documents` (`listDocuments`), `GET /api/documents/{id}/preview`
  (`getDocumentPreview`), `POST /api/documents/{id}/reprocess` (`reprocessDocument`) and
  `DELETE /api/documents/{id}` (`deleteDocument`) -- the sixth web epic to extend this document (see
  "API client" provenance paragraphs above), and the first to document endpoints **ahead of** their
  own backend counterpart (epic-13/feature-04) landing in this worktree; see "Documents" above for
  the full provenance note and the widened `documentType` enum / `413`/`415`/`422` admission-gate
  responses this task also added to `POST /api/documents`. `getDocumentPreviewUrl` is this client's
  first non-JSON response: `image/png` on `200`, turned into a browser object URL
  (`URL.createObjectURL`) the caller renders and must `URL.revokeObjectURL` itself -- see that
  method's own doc comment for why a plain `<img src>` cannot carry the required `X-Tenant-Id`
  header. `deleteDocument`'s only success shape is `204 No Content` (no body to parse at all,
  unlike `getDocument`'s already-established `404`-no-body special case).

- **Task E13/F09/US01/T04 (web-ask-v2, ADR-024 §6)** extended `openapi/raffa-api.v1.json` with
  `GET/POST /api/conversations`, `GET /api/conversations/{id}`,
  `POST /api/conversations/{id}/messages` (the reply contract), `GET /api/capabilities`,
  `GET /api/market/records/{id}`, and `supplierName` on the portfolio/360/renewals/documents
  responses -- the seventh web epic to extend this document (see "API client" provenance paragraphs
  above), and the first to add a header every method in this file sent when supplied: the interim
  `X-User-Id` (`userIdHeaders`, OQ-askv2-005/ADR-022), resolved lazily via a `getUserId` callback
  `main.tsx` supplied once MSAL resolved an account, mirroring `X-Tenant-Id`'s own
  resolved-per-request shape. **Deleted whole by task E18/F01/US02/T01 below**, not left as a
  fallback. `getCapabilities`/`getMarketRecord` are the first genuinely tenant-agnostic reads in this
  file (no `X-Tenant-Id` at all -- the catalog and the market index are both static/shared, not
  per-tenant); `getCapabilities` also has no documented non-2xx body (`CapabilitiesEndpointExtensions`
  has no failure branch), so its own error path is a status-based message only, never an attempted
  JSON parse, unlike every write/tenant-scoped read above it.

- **Task E18/F01/US02/T01 (wave w15, NW-05; ADR-012 w15 footer clause 1, ADR-010 w14 footer clause
  3)** made the SPA send the token it already asks Entra for. `createApiClient`'s identity parameter
  is now an **async token accessor** (`GetAccessToken`, `client.ts`), and **one** internal helper
  (`authHeaders`) attaches `Authorization: Bearer <token>` to every request -- the 37 inline spreads
  of the old synchronous `userIdHeaders(getUserId)` (one per method, the same 37-call-site count the
  ADR names) collapse to that one choke point, verified this wave by a vitest case that enumerates
  the whole `ApiClient` surface and asserts every method attaches the header
  (`tests/api/client.test.ts`). The interim `X-User-Id` header, its `userIdHeaders` helper, the
  synchronous `GetUserId` accessor, and `main.tsx`'s closure over
  `getAllAccounts()[0]?.username` are all **deleted**, not made conditional. Acquisition
  (`auth/msalConfig.ts#acquireApiAccessToken`) is `acquireTokenSilent({ scopes:
  appConfig.oidcApiScopes, account })`, falling back to `acquireTokenPopup` only on
  `InteractionRequiredAuthError` -- **never** `acquireTokenRedirect`, because a redirect unloads the
  page and on `/invite/accept` would destroy the in-memory invitation token. A `401` after a
  successful silent acquisition is treated as a server rejection, not a stale token: it is surfaced
  exactly as before (no call site retries), never triggering a second acquisition. The access token is
  never stored -- read from MSAL's own cache per request, never copied into React state, a module
  variable, or either Web Storage -- and the SPA still reads no `roles` claim from it: `role` keeps
  coming from the `GET /api/workspaces` row (`components/shell/workspaceRole.ts`'s least-privilege
  parse, unchanged). `X-Tenant-Id` is unaffected: it continues to be sent as a selector alongside the
  new `Authorization` header, and the `raffa.signin.currentWorkspace` hint's four-way resolution order
  survives unchanged (OQ-w15-ca-03). No OpenAPI/contract edit and no CI change -- the generator parses
  only `responses`, so headers stay hand-written by construction, and `buildLoginRequest` already
  requested `appConfig.oidcApiScopes` before this task.

- **Task E15/F01/US01/T01 (invitation-lifecycle-api, wave w14; ADR-025/ADR-026)** extended
  `openapi/raffa-api.v1.json` with `DELETE /api/workspaces/{tenantId}/invites/{id}`
  (`revokeInvitation`), `DELETE /api/workspaces/{tenantId}/members/{membershipId}`
  (`removeMember`), `GET /api/invites` (`getInvitation`) and `POST /api/invites/accept`
  (`acceptInvitation`) -- the eighth web epic to extend this document (see "API client"
  provenance paragraphs above). The last two carry no `tenantId` of any kind (route or
  header) -- unlike `getCapabilities`/`getMarketRecord` above, which are tenant-agnostic
  because the underlying data is shared, these are genuinely tenant-scoped: the invitation
  token alone lets the backend resolve which tenant server-side, so the caller never supplies
  one, sending it instead as the `X-Invitation-Token` header carrying the fragment
  `inviteWorkspaceMember`'s own `acceptUrl` returns after `/invite/accept#` (ADR-025 Rule C9 --
  a query string would land in access logs, `Referer` headers and browser history).
  `getInvitation`/`acceptInvitation` are hand-written against the generated `responses` type
  only, not anchored to a generated request shape the way the write bodies above are:
  `generate-api-client.mjs` does not parse an operation's `parameters` any more than it parses
  `requestBody` -- the same documented generator limitation, now shown to cover headers too,
  not just bodies. `revokeInvitation`/`removeMember` follow the existing never-throws
  convention (401/403/404/409 are normal, expected outcomes the caller renders inline, not
  exceptions).
  **`inviteWorkspaceMember`'s 201 body also changed shape** -- it is now an offer, not a
  grant: no membership is written by that call any more (see
  `WorkspaceInvitationService.IssueAsync`, backend), and `InvitedMemberBody` gained
  `expiresAt`/`acceptUrl`/`mailDelivered` straight off the regenerated schema, no
  hand-written change needed for the response shape itself. See "Workspace & members" above
  for the screen this powers: task E15/F02/US01/T01 (wave w14) wires `getWorkspaceMembers`,
  `revokeInvitation` and `removeMember` into that screen's roster, revoke and remove controls.
  `getInvitation` and `acceptInvitation` are called by the `/invite/accept` landing page (task
  E14/F03/US02/T01, wave w14; see "Invitation accept" above) -- every one of this section's five
  client methods now has a real caller.

## Directory layout

```
web/
  playwright.config.ts      # task E08/F04/US01/T01 -- e2e/day1.spec.ts's runner config (see "End-to-end" below)
  e2e/
    day1.spec.ts             # task E08/F04/US01/T01 -- the §20 Day-1 browser walk, the web-pass integration gate
  openapi/
    raffa-api.v1.json       # single OpenAPI document (interim, hand-authored -- see "API client" above)
  scripts/
    generate-api-client.mjs   # openapi/raffa-api.v1.json -> src/api/generated/schema.ts (npm run generate:api)
  public/
    config.json               # runtime config contract; dev-only placeholder values
    staticwebapp.config.json  # Azure SWA: SPA fallback routing
  src/
    api/
      generated/schema.ts     # AUTO-GENERATED; do not edit by hand
      client.ts                # createApiClient(baseUrl, getAccessToken?) -> { getHealth(), createWorkspace({ name }), uploadDocument(tenantId, file), getDocument(tenantId, id), listDocuments(tenantId, query?), getDocumentPreviewUrl(tenantId, id), reprocessDocument(tenantId, id), deleteDocument(tenantId, id), getPortfolio(tenantId, query?), getContract360(tenantId, id), getRenewals(tenantId), getRenewalPriority(tenantId, contractId), getCorrectionHistory(tenantId, id), correctContract(tenantId, id, request), postRenewalAction(tenantId, contractId, request), askRaffa(tenantId, request), uploadQuote(tenantId, file, fields?), getQuoteAssessment(tenantId, id), recalculateQuoteAssessment(tenantId, id, mappings?), captureNegotiationOutcome(tenantId, request), getSavingsKpis(tenantId), getSavingsOpportunities(tenantId), listConversations(tenantId), createConversation(tenantId, request?), getConversation(tenantId, id), postMessage(tenantId, conversationId, request), getCapabilities(), getMarketRecord(id) } -- every method attaches `Authorization: Bearer <token>` when getAccessToken resolves one (task E18/F01/US02/T01, NW-05), through the one `authHeaders` choke point
    config/appConfig.ts       # fetch + validate runtime config
    auth/msalConfig.ts        # AppConfig -> MSAL Configuration (no secret, ever); acquireApiAccessToken(instance, appConfig) -- acquireTokenSilent, falling back to acquireTokenPopup (task E18/F01/US02/T01)
    styles/                   # design system (tokens + component catalogue); see below
    routes/
      signin/               # ADR-018 `/signin`; gated on MSAL auth state, not a URL route (see "Screens" above)
        index.tsx             # SignInRoute -- picker prop null: SignInScreen; picker set (App.tsx's AuthenticatedGate resolved): WorkspacePickerScreen
        SignInScreen.tsx      # idle / redirecting states around instance.loginRedirect(); also exports SignInStatementPanel (shared left-column canvas, task E06/F06/US01/T01)
        WorkspacePickerScreen.tsx # server list via apiClient.listWorkspaces() (resolveWorkspaceSelection is the resolution-order rule) + create via POST /api/workspaces -- skeleton/error+Retry/list/create states, no "Continue" interstitial (task E14/F03/US02/T01)
        workspaceStore.ts     # sessionStorage "current workspace" session hint only, revalidated against the server list on every mount (task E14/F03/US02/T01)
        signin.css            # this route's styles -- see "Layout" above
      invite/accept/        # ADR-018/ADR-020/ADR-025 w14 footers -- see "Invitation accept" above (task E14/F03/US02/T01)
        index.tsx              # AcceptInvitationRoute -- the public /invite/accept route, ten states, fragment token read once into memory
        accept.css              # this screen's styles
      documents/            # V1 tasks E06/F05/US01/T01 + E06/F05/US02/T01; V2 rebuild task E13/F09/US01/T03 -- ADR-020 screen 3 (see "Documents" above)
        index.tsx             # DocumentsRoute -- onboarding-empty / list / review-as-state switch; wires useDocumentsList + apiClient.deleteDocument
        useDocumentsList.ts   # GET /api/documents fetch + 2s poll while non-terminal, attention/all filter state, upload/retry/delete orchestration
        OnboardingEmptyState.tsx # docsEmpty: "First your contracts. Then your questions." + the 3-step strip
        AttentionFilter.tsx   # "Needs your attention · N" / "All documents · N" segmented toggle
        UploadDropzone.tsx    # shared onboarding/list dropzone: drag-and-drop + file picker (multi-file, widened accept) + "Use sample file"
        ProcessingPipeline.tsx # inline per-row progress bar (real stage/percent from the API, mounted once per processing row -- no more a standalone 6-stage ticker)
        UploadResultCard.tsx  # per-file outcome card: completed / needs_review / failed / "Not added" (422/415/oversized)
        uploadPipeline.ts     # pure helpers: multi-file batch runner (<=3 concurrent), rejection-reason copy, size/count limits
        sampleDocument.ts     # synthetic sample File for "Use sample file" -- no real fixture asset in this repo
        ReviewState.tsx       # review as a state of Documents (?review=<id>); wraps ../contracts/review/* unmodified
        DocumentStatusTable.tsx # the row grid: Document/Supplier·Type/Status/Next step/Admin-only Delete (with confirm/cancel)
        documentTable.ts      # pure helpers: type-label mapping, status/action derivation, attention-filter bucketing, kb summary
        documents.css         # this route's styles (V2: stacked single-column layout, no more the V1 two-column grid)
      contracts/            # Portfolio, V2 (see "Portfolio" above)
        index.tsx             # PortfolioRoute -- fetch state machine, header summary, category filter, More columns, reroute
        PortfolioTable.tsx    # the V2 table (Supplier · Contract · Annual spend · Ends · Give notice by · Status [+ Start · Auto · Risk])
        PortfolioFilterControl.tsx # the ?category= free-text filter control (task E24/F01/US02/T01) -- router query param, never a store
        portfolioViewModel.ts # pure helpers: validated rows sorted by notice deadline, per-currency summary, compact amounts, ?category= read/write
        portfolioAttention.ts # pure helper: daysUntil (UTC day arithmetic) shared with Contract 360
        portfolioTableFormatters.ts # pure helpers: date/number formatting, type label, status/risk -> tag mapping, supplier fallback
        contractStatus.ts     # isValidatedContractStatus -- the one "validated" predicate Portfolio and the rail share
        contracts.css         # this route's styles
        contract360/          # Contract 360, V2 no tabs (see "Contract 360" above)
          index.tsx              # Contract360Route -- fetch order (contract, then renewals + priority), actions, tracker, clause selection
          Contract360Header.tsx  # back link · supplier · title · meta line · Ask about it / Review extraction
          AnswersBand.tsx        # Where you can save · When you must move · What to do (+ actions or the negotiation tracker)
          WhyClauses.tsx         # clause rows, click -> ClauseHighlight
          ClauseHighlight.tsx    # the serif evidence card: "{file} · page N · §span" + <mark>ed wording
          DetailsSection.tsx     # "Details ▾": key terms, documents, facts to decide, priority score, Products/Obligations/Risks
          FactTable.tsx          # Term/Value/Source/Confidence list for the extracted rows inside Details
          contract360ViewModel.ts # pure helpers: answers, recommendation, clause rows/evidence, key terms, attention, priority rows
          contract360.css        # this screen's styles
        review/                # task E07/F03/US01/T01 -- ADR-020 screen 6 (see "Review / correction" above)
          index.tsx              # ReviewRoute -- fetch order (contract, then correction history), decision state, correction submit
          ReviewHeader.tsx        # AC-4: title/summary + gated "Mark as validated" + progress line + confidence legend
          ReviewFieldList.tsx     # AC-1: the 4-column field list (critical marker, value, confidence tag, decision)
          EvidencePane.tsx        # AC-3: evidence + correction form + real correction-history trail
          reviewViewModel.ts      # pure helpers: correctable-field catalogue, decision/tag/gate computation
          review.css              # this screen's styles
      ask/                  # task E07/F04/US01/T01 -- ADR-020 screen 7; V2 rebuild task E13/F09/US01/T04 (see "Ask Raffa" above)
        index.tsx              # AskRoute -- off/new-chat/conversation/resume states; seeds+asks the global Ask bar's router-state query once; create-then-ask, citation-click routing
        AskOffState.tsx        # off state (0 validated contracts): fixed headline, doc-count-dependent reason + one CTA to /documents
        MarketRecordPanel.tsx  # side panel for a market citation: GET /api/market/records/{id}
        useConversation.ts     # resume: GET /api/conversations/{id} -> turns, oldest first
        useRecentConversations.ts # rail's last-5 list: GET /api/conversations, refetches on navigation (see "App shell" above)
        reply/                  # phase-2 (task E13/F09/US01/T02) rich reply renderer -- ReplyBody/CitationCard/ActionRow/ReplyMarkdown/replyTypes
        askViewModel.ts        # pure(ish) helpers: wire reply -> presentational Reply, turn construction, off-copy/scope-line/suggestion-chip text, citation-click resolution
        ask.css                # this screen's styles
      renewals/                 # Renewals, V2 (see "Renewals" above)
        index.tsx                 # RenewalsRoute -- fetch-then-score-batch state machine, selection, actions, reroute
        RenewalTable.tsx          # the priority list (Score · Supplier · contract · Renews in · Notice in · Status)
        InsightCard.tsx           # the "Why it is here" pane: action + rationale, Start negotiation / Assign to me, acted box
        renewalPipelineViewModel.ts # pure helpers: rows sorted by score, summary, formatting, the two action plans
        renewals.css              # this screen's styles
      quotes/                 # Quote check, V2 (see "Quote check" above)
        index.tsx               # QuoteCheckRoute -- header, landing + history, band + lines + footer, one-step-further reveal
        UploadQuoteForm.tsx      # the dashed drop card + optional metadata disclosure
        sampleQuote.ts           # the Databricks sample proposal, a real PDF built with buildSamplePdf
        QuoteLinesTable.tsx      # Line · Quoted · P50 · Position · Benchmark
        MappingBlock.tsx         # unmatched-SKU mapping (shown instead of the footer while blocked)
        TargetStep.tsx           # one step further: price ladder + editable target
        NegotiationStep.tsx      # one step further: outcome capture
        quoteCheckViewModel.ts   # pure helpers: aggregate, band, line rows, unit-price/P50 formatting
        quotes.css               # this screen's styles
        assessment/              # task E25/F04/US02/T01 -- benchmark-first result, extracted from index.tsx
          AssessmentResult.tsx      # the band + lines table + honest InsufficientBenchmarkData cold-start copy (AC-1)
        history/                 # task E25/F04/US02/T01 -- ADR-028 (history is server state), closes NW-57
          QuoteHistoryList.tsx      # landing-only: every quote this workspace has checked, from GET /api/quotes/benchmark-history (AC-2)
      savings/                 # Savings, V2 (see "Savings" above)
        index.tsx                # SavingsRoute -- three independent fetches (KPIs, opportunities, portfolio names), independent degrade states; renders the supplier/status/currency filter bar above the table
        KpiRow.tsx               # the three KPI cells + the stale-labelled notice
        OpportunitiesTable.tsx   # Supplier · Action · Estimate · Status, rows open Contract 360
        savingsViewModel.ts      # pure helpers: reduceKpiFetch, buildKpiCells, formatSavingsSummary, buildOpportunityRows, buildSupplierNameIndex, filterOpportunityRows
        savingsFilters.ts        # pure supplier/status/currency filter predicates + option lists (task-01-savings-filters, ADR-020; tested in savingsFilters.test.ts)
        savings.css              # this screen's styles
    components/
      shell/                  # task E06/F03/US02/T01 -- app shell, router, role guard; V2 two-tier rail by E13/F09/US01/T01 (see "App shell" above)
        navItems.ts             # V2 two-tier model: buildPrimaryNavItems/buildSecondaryNavItems, badge builders, canManageMembers(role) role guard (AC-1/AC-2)
        useValidatedContractCount.ts # kbReady / validated-contract count, fetched once via apiClient.listWorkspaces() (task E14/F03/US02/T01; was the capped getPortfolio(pageSize: 100) count)
        workspaceRole.ts        # parseWorkspaceRole(wire): the server role, mapped to least privilege for anything unmodelled, never "admin" (task E14/F03/US02/T01; see "Role source is a server fact" above)
        RailNav.tsx              # 224px left rail, two tiers + footer
        RequireRole.tsx          # route-level "request access" guard; currently no caller anywhere in this app (see "Role guard" above)
        ScaffoldScreen.tsx       # generic placeholder for routes later epics build for real
        AppShell.tsx             # rail + global Ask bar + <Outlet/>; fetches kbReady once via useValidatedContractCount; passes workspaceId down through shellContext.ts
        WorkspaceShellApp.tsx    # V2 route table (BrowserRouter itself lives in App.tsx as of task E14/F03/US02/T01; ShellRoutes is the router-free export tests use)
        shellContext.ts          # useOutletContext typing for the router outlet (workspaceId/kbReady/validatedContractCount)
        shell.css                # rail/shell layout
      ask-bar/                # task E06/F03/US02/T01 -- global Ask bar scaffold (AC-3); V2 new-chat state + off placeholder by E13/F09/US01/T01; capability-sourced chips by E13/F09/US01/T04
        GlobalAskBar.tsx         # the bar itself: input, chips, Enter -> /ask with { query, newChat: true }, Cmd/Ctrl+K focus, fetches GET /api/capabilities once
        askSuggestions.ts        # getAskBarCopy: static per-route fallback copy + kbReady off-copy, overridden by the capability catalog's own exampleQuestions once it resolves for the current route
        ask-bar.css
    App.tsx                   # composition root: BrowserRouter (public /invite/accept + the account/workspace gate), /health effect, AuthenticatedGate's three states (task E14/F03/US02/T01)
    main.tsx                  # boot: load config -> construct MSAL + API client -> render
    index.css                 # global entry; imports styles/index.css
  tests/                      # mirrors src/; vitest + Testing Library
```

## Design system (ADR-019, task E06/F02/US01/T01)

`src/styles/` is the shared Modernist token sheet + component catalogue every
later screen builds on -- one visual language, not a divergent one per
screen. `src/index.css` (imported once, in `main.tsx`) pulls it in via
`@import "./styles/index.css"`.

| File | Contents |
|------|----------|
| `styles/tokens.css` | `:root` custom properties: colour ramps, spacing, radius (always `0`), type, shadows (dialogs only), disabled opacity. Cites its sources (design-system.md, ADR-019, the compiled `day1-demo.html` bundle) in a header comment. |
| `styles/base.css` | Global element defaults: page ground/ink/font on `body`, heading weights, focus ring, `.icon`, `.grayscale`. |
| `styles/components.css` | The locked component catalogue: `.btn` (+ variants), `.tag` (+ variants), `.table`, `.input`/`.field`, `.radio + .dot`, `.seg`, `.card`, skeleton bars, attention/threshold strip, detail pane, "Facts vs AI" separation, plus the semantic-mapping treatments (`.deadline-critical`, `.abstain-block`, `.row-critical`). |
| `styles/semantics.ts` | Typed confidence/status/risk/deadline -> tag-variant + label mapping (ADR-019 "Semantic mapping"). Screens call this instead of re-deriving thresholds or reading a tag's colour -- meaning is never colour-only (AC-2). Tested in `tests/styles/semantics.test.ts`. |

**Values are consumed verbatim, not forked** (ADR-019): every token traces to
`inputs/design/prototypes/design-system.md` or the compiled
`inputs/design/prototypes/day1-demo.html` bundle. No `/design-sync` tool is
available in this harness (no Claude Design skill mounted), so the markdown
dump plus the compiled bundle is the authoritative source, per ADR-019's own
fallback.

`.btn-primary` keeps the locked `--color-accent` (#ec3013) background with a
white label; measured contrast is ~4.2:1, under WCAG's 4.5:1 normal-text
floor (it does clear the 3:1 UI-component floor). ADR-019 pins >=4.5:1 for
accent-coloured *text on the page ground* (`--color-accent-700`) but does not
separately pin a floor for light text on a filled accent surface -- flagged
in a comment on `.btn-primary` in `styles/components.css` rather than
silently shipped or "fixed" by forking the locked accent value.

**Task E11/F01/US01/T01** (chrome-foundation, AC-2) added
`.btn-primary.btn-block { padding: 12px 14px; }`, matching the inline
override the compiled prototype's own sign-in CTA carries in
`day1-demo.html` (`.btn.btn-primary.btn-block`) -- narrower than the shared
`.btn` padding (8px/16px, `--space-2`/`--space-4`) above. Scoped to that one
class combination: a plain `.btn-block` secondary/ghost action, or a
non-block `.btn-primary`, is unaffected. It also added `.visually-hidden`
(`styles/base.css`) -- a clip-based utility, not `display:none`, first
consumed by `App.tsx`'s always-on `/health` probe status (see "API client"
above) so it keeps running and stays in the accessibility tree without
painting the prototype's absent `API: ...` line onto the canvas.

## End-to-end (Day-1 browser walk) -- task E08/F04/US01/T01, us-01-final-integration

`e2e/day1.spec.ts` (Playwright, config at `playwright.config.ts`) is the
**web-pass integration gate** the parent story's Definition of Done names:
"Manual + automated smoke of the Day-1 path on `demo` passes." It drives the
real, deployed SPA in a real browser end to end -- product-spec §20's own
ladder (sign in -> invite -> upload -> review -> Contract 360 -> Ask with
citations when the reply carries one -> renewal action -> Savings shows its
KPIs -> quote check -> record outcome -> Savings link-back), never
`dotnet test`, never Swagger (AC-1), against `demo`, never a `localhost`
`config.json` shell (AC-2), which only makes sense once `demo-v*` promotion
(ADR-016) has actually happened (AC-3).

The task's own "Files to create or modify" table names this file as
`workspace/raffa-web/e2e/day1.spec.ts`; it lives at `web/e2e/day1.spec.ts`
instead, the same "product tree is the four domain folders at the worktree
root, not a `workspace/<repo>/` stand-in" correction `reports/open-questions.md`
already recorded for two earlier tasks (OQ-impl-001/002) -- there is no other
location where a browser test could reach the real, already-scaffolded ten
screens this file drives.

**Reconciled to the V2 shell, task E26/F02/US01/T01 (NW-50, wave W18).** The
regression this section used to document (task E13/F09/US01/T01, ADR-024 V2
shell, gap G-IA-V2: `page.goto("/")` no longer rendering a "Home" screen, so
the old six-cell KPI / "Home" heading assertions never resolved) is fixed --
every step below now drives the real V2 screens (Ask is home at `/ask`; the
old Home KPI row is `/savings`'s own four-cell band, reached from Ask/
Renewals/Contract 360 actions, never the rail; uploads are polled
asynchronously per wave w15's own NW-27 rebuild). See the spec file's own
header comment for the full, cited reconciliation. `npm run test:e2e` is
still not run by CI (see "CI wiring" below) -- NW-50's own scope is this file
and its runner, never a `.github/workflows/` change (ADR-016's w14 footer:
"a wave does not acquire a new CI capability as a side effect").

### Running it

```bash
npx playwright install --with-deps chromium   # one-time browser download
RAFFA_E2E_BASE_URL=https://<swa-demo-host> \
RAFFA_E2E_ENTRA_EMAIL=<a demo-tenant test account UPN> \
RAFFA_E2E_ENTRA_PASSWORD=<that account's password> \
  npm run test:e2e
npm run test:e2e:report   # opens the HTML report -- trace/video/screenshot on failure, "smoking recorded"
```

All three environment variables are required; the suite `test.skip()`s
itself (not a false pass, not a silent no-op exit code) with a message
naming exactly what is missing when any are unset -- see the spec file's own
header comment. There is deliberately no `localhost` fallback for
`RAFFA_E2E_BASE_URL`: that would let a local run masquerade as the `demo`
gate AC-2 requires.

**The test account must be exempt from interactive MFA / Conditional
Access** (a standard "automation/service" account exclusion on the Entra
side) -- `e2e/day1.spec.ts#signInWithEntra` drives the identifier + password
steps and an optional "Stay signed in?" prompt only; it cannot answer an
MFA challenge. This is an Entra tenant configuration decision for whoever
provisions the `demo`-tenant test account, not something this file's own
scope (`web/`) can set.

### Real, honestly-tested divergences from the prototype

`day1-demo.html` is one hard-coded demo scenario; the real app is not. The
spec's own header comment has the full citations -- in short:

1. **The workspace picker is server-driven, not a per-browser cache**
   (task E14/F03/US02/T01, wave w14; ADR-026 §D1) -- this used to be the
   opposite (a fresh Playwright context could never discover the ADR-022
   fixture-seeded tenant at all, only create its own empty one). `GET
   /api/workspaces` now answers from the caller's own real membership, so
   whether a run lands on an existing (possibly fixture) tenant or must
   create one depends on whether the signed-in identity holds a real
   membership row -- for `demo`, on whether `RAFFA_E2E_ENTRA_EMAIL` is the
   account `backend/scripts/demo-fixture-seed.sql`'s Admin membership was
   backfilled onto (OQ-w14-dec-001). Ask-citation, renewal-pipeline and
   savings-opportunity steps still assert whichever real, already-tested
   state (populated or honestly empty) actually renders, and name the gap
   inline via `test.info().annotations` rather than asserting a fabricated
   populated state -- `pickOrCreateWorkspace` covers all three outcomes
   NW-01 introduces: exactly one real membership skips the picker entirely
   (AC-2's "no picker").
2. **Uploads are asynchronous** (wave w15, NW-27/ADR-027): a document row
   exists the instant a file is picked and reaches a terminal status
   (`Needs review` / `Completed` / `Failed` / `Not added`) some seconds
   later via the Worker. `uploadSampleDocument` polls the real
   `DocumentStatusTable` row for that transition rather than assuming a
   synchronous result -- the pre-V2 `.upload-result-card` this file used to
   drive no longer exists anywhere in `web/src`.
3. **Whether a recorded quote outcome updates the Savings figures is not
   asserted here.** The final step follows the outcome panel's own real
   "See it in Savings →" link and confirms it lands on the real Savings
   screen with its real KPI band -- it does not assert a specific KPI value
   change, since that depends on backend propagation this web-only task did
   not re-verify.

### CI wiring is a follow-up, not this task

This task authors and wires the suite so an operator (or a human at the
keyboard) can run it against `demo` after a `demo-v*` promotion. Adding a
GitHub Actions job that runs it automatically post-promotion (with the Entra
test-account credentials as environment secrets) is a natural next step but
is a `.github/workflows/` change outside this task's own file scope.

### Harness note

This suite was authored against, and its every selector/state-branch
statically verified against, the real committed source of every screen it
drives (cited throughout the spec file). An earlier pass through this
worktree's Helix implementer session found `git`/`npm`/`node` all missing
via `command -v` and concluded nothing beyond static reading could run
here -- that conclusion was too broad and has been corrected. `PATH` in
this harness's shell is corrupted, not the tools: `git`, `node`, `npm`, and
`npx` are all installed and run fine via their absolute paths, e.g.:

```bash
"/c/Program Files/Git/bin/git.exe" status --short
"/c/Program Files/nodejs/npm.cmd" ci
"/c/Program Files/nodejs/npx.cmd" vitest run
```

Using that, everything short of a real browser run against `demo` **is**
verifiable in this harness, and was, as part of task E08/F04/US01/T01's own
review loop: `npm ci` (the exact command `.github/workflows/web.yml`'s
`build` job runs -- this caught a real bug, `package-lock.json` never
having been regenerated after `@playwright/test` was added to
`package.json`, which made `npm ci` hard-fail before `build`/`test` ever
ran; fixed by committing the regenerated lockfile alongside this note),
`npx tsc --noEmit`, `npx vitest run` (the pre-existing unit suite -- this
also caught `e2e/day1.spec.ts` being picked up by Vitest's own default
include glob and failing collection; fixed by `vite.config.ts`'s own
`test.exclude`), `npx playwright test --list` (discovers `day1.spec.ts`), and
`npx playwright test` itself, which correctly `test.skip()`s -- not a false
pass, not a hang -- when the three `RAFFA_E2E_BASE_URL` /
`RAFFA_E2E_ENTRA_EMAIL` / `RAFFA_E2E_ENTRA_PASSWORD` env vars are
unset, exactly as designed. What genuinely is operator/CI-only is a real
pass with real Entra test-account credentials against a live,
`demo-v*`-promoted `demo` deployment -- that, and only that, is the actual
gate this suite exists to satisfy; no local or CI session without those
live credentials can supply it.

Re-confirmed by task E26/F02/US01/T01's own V2 reconciliation: `npm ci`
(133 packages, `package-lock.json` untouched), `npx tsc --noEmit` against
this project's real `compilerOptions` with `e2e/` added to `include` (zero
errors in `day1.spec.ts`; the two pre-existing errors on the tree are in
`v2.spec.ts`, untouched by that task), `npx playwright test day1.spec.ts
--list` (still discovers the one declared test) and `npx playwright test
day1.spec.ts` with no environment set (still "1 skipped", exit `0`) all
still hold on this harness.

## End-to-end (Ask Raffa V2 pilot path) -- task E13/F11/US01/T01, us-01-integration

`e2e/v2.spec.ts` (same Playwright config, `playwright.config.ts`) is the V2
replacement for `e2e/day1.spec.ts`. It walks `inputs/requirements.md` §10's own
acceptance rows against a deployed environment -- `dev` first (this wave's
Definition of Done), then `demo` after a `demo-v*` promotion (ADR-016). The
prose runbook for the same rows, including the API/SQL checks a browser cannot
make, is [`../docs/ask-v2-acceptance.md`](../docs/ask-v2-acceptance.md).

`day1.spec.ts` stays checked in; it was the V1 walk and red against the V2
shell until task E26/F02/US01/T01 reconciled it (see "End-to-end (Day-1
browser walk)" above) -- the two files still share no imports and only a
handful of incidental selectors (both drive the same real `.citation-card`/
`.ask-message` Ask Raffa markup, for instance), because they cover different
ground: `v2.spec.ts` is the acceptance-row suite for the V2 pilot path on a
fixture-seeded tenant, `day1.spec.ts` is the single-clickable-flow demo walk
on a fresh workspace.

### What runs and what skips

| Row | Test | Gate |
|---|---|---|
| A14 | `/` lands on `/ask`; the rail is two-tier; the secondary tier is greyed before validation; Ask is off with the prototype copy | always |
| A1 | a recipe PDF and an unreadable PNG dropped together are both refused (**Not added** + reason), and neither ends up in the server-backed list | always |
| A1 | ...and an MSA dropped alongside them still reaches a terminal status | needs `RAFFA_E2E_MSA_PATH` |
| A3 | `ciao` -> redirect prose, one CTA, **no** abstain block | always |
| A4 | `Posso fare causa a Salesforce?` -> refusal + an action under `/contracts` | always |
| A8 | `Cosa sai fare?` -> feature cards badged **Raffa**, every action an in-app route, and the first one actually navigates | always |
| A9 | a reply carries a human citation card and an action, and never a guid / `Document:` chip / `Structured query...` route line | always |
| A10 | asking moves the browser to `/ask/<id>`; a reload and a second tab both resume the same turns | always |
| A2, A5, A6, A7 | OCR'd order form; Allianz vs market; Salesforce renewal strategy; portfolio criticality in a new chat | `E2E_LIVE_FOUNDRY=1` (A2 also needs `RAFFA_E2E_ORDER_FORM_PNG`) |

A2 / A5 / A6 / A7 are `test.skip`ped with the reason *"requires live Foundry"* --
OQ-askv2-009 and `inputs/requirements.md` §13 A9 ("live Foundry on `dev`/`demo`
is required for acceptance A2-A8; the fixture gateway proves the same paths in
CI"). A8 and A9 stay unconditional because the capability catalog is static and
"no engineer chrome" is a property of every reply, model or not.

A11 (cross-tenant isolation), A12 (no tools/grounding in the Foundry request
body) and A13 (golden set) are deliberately **not** browser assertions -- a
browser cannot observe a request body or another tenant's rows without
fabricating a second identity. They are proven by `Raffa.IntegrationTests`,
`Raffa.AiGateway.Tests` and `Raffa.AiEval`, all of which run under
`dotnet test Raffa.slnx` in `.github/workflows/backend.yml`.
`docs/ask-v2-acceptance.md` names the real check for each.

### Running it

```bash
npm ci
npx playwright install --with-deps chromium   # one-time browser download

RAFFA_E2E_BASE_URL=https://<swa-dev-host> \
RAFFA_E2E_ENTRA_EMAIL=<a test-account UPN on that Entra tenant> \
RAFFA_E2E_ENTRA_PASSWORD=<that account's password> \
RAFFA_E2E_TENANT_ID=<the fixture-seeded workspace id> \
  npx playwright test v2.spec.ts

# Add the live-Foundry rows once Foundry is wired on that environment:
E2E_LIVE_FOUNDRY=1 RAFFA_E2E_ORDER_FORM_PNG=/path/to/scan.png \
  ... npx playwright test v2.spec.ts

npm run test:e2e:report   # trace / video / screenshot on failure
```

| Variable | Meaning |
|---|---|
| `RAFFA_E2E_BASE_URL` | the deployed SPA origin (`dev`, or `demo` after promotion) |
| `RAFFA_E2E_ENTRA_EMAIL` / `_PASSWORD` | a real test account on that environment's Entra tenant, excluded from interactive MFA (the same constraint `day1.spec.ts` documents -- the spec drives the identifier/password/"stay signed in?" steps only) |
| `RAFFA_E2E_TENANT_ID` | the **fixture-seeded** workspace id, i.e. the tenant that has validated contracts |
| `RAFFA_E2E_EMPTY_TENANT_ID` | optional; a workspace known to hold zero validated contracts, for A14's greyed-rail row. Defaults to a generated uuid, which is equivalent for that assertion and is logged as such |
| `RAFFA_E2E_MSA_PATH`, `RAFFA_E2E_ORDER_FORM_PNG` | optional paths to a real contract PDF / scanned order-form image |
| `E2E_LIVE_FOUNDRY` | `1` to run A2 / A5 / A6 / A7 |

With none of them set the suite reports all 14 rows as **skipped**, with the
reason, and exits `0` -- the group-level skip is the callback form, so
Playwright never enters `beforeAll` and never attempts a real Entra sign-in.

### Why the workspace is pinned through `sessionStorage`

There is still no endpoint that lists the workspaces an identity belongs to
(`src/routes/signin/workspaceStore.ts`'s own documented gap), so a stock browser
context always lands on "No workspaces yet" and creates an **empty** workspace --
where Ask is correctly *off* (R-ASK-10) and A1/A3-A10 cannot be observed at all.
Rather than assert an honestly-empty screen and call that acceptance, the spec
writes the same `raffa.signin.currentWorkspace` key the app itself writes
(`workspaceStore.ts`'s `CURRENT_WORKSPACE_KEY`) with the operator-supplied
tenant id. That is the documented seam, not a mock: every `ApiClient` call then
carries that tenant as `X-Tenant-Id` exactly as a human selecting the workspace
in the picker would. Without `RAFFA_E2E_TENANT_ID` the pilot-path group skips
rather than walking an empty workspace.

### The upload fixtures are built in the spec, on purpose

This repo ships no `*.pdf` / `*.png` asset -- `src/routes/documents/sampleDocument.ts`
records that and builds its own minimal PDF in the browser for the same reason.
A1 needs a file whose *extracted text* reads as a recipe, so that the admission
gate can answer `not_a_contract` rather than `no_readable_text`, which the
sample's content-free PDF cannot provide. `v2.spec.ts` therefore builds a real,
structurally-valid one-page PDF (correct xref offsets, a Helvetica text stream)
and a valid 1x1 PNG in memory and feeds them through the ordinary
`<input type="file">` a human uses -- nothing about the API is mocked. The
assertion accepts **either** documented rejection reason and records which one
the environment produced, because which of the two applies depends on whether
that environment's parser reads the synthetic PDF's text; both are R-DOC-03
refusals and neither is a claim Raffa cannot back.

### Harness note

Authored against the currently-committed V2 source of every screen it drives
(cited inline in the spec). Verified here with `npx playwright test --list`
(discovers all 14 rows) and `npx playwright test v2.spec.ts` with no environment
set (14 skipped, exit `0`, no sign-in attempted). A real green run needs a
deployed environment, a fixture-seeded tenant and Entra test credentials -- an
operator/CI action, the same shape ADR-016's promotion gate already has.

## End-to-end (workspace invitation, N3b) -- task E14/F06/US01/T01, us-01-final-integration

`e2e/invite.spec.ts` (same `playwright.config.ts`) is wave w14's two-account walk of
NW-58's acceptance row **N3b**: an Admin invites an address, a **second browser
context** opens the copyable accept link, signs in through the accept screen's own
popup CTA and joins, lands in *that* workspace, then the Admin removes them and the
removed account's next load loses access -- with the link they used now rendering
"This invitation is no longer valid." The prose runbook for the whole wave, with the
API and SQL checks a browser cannot make (N1, N2, N3, N3b, N4, N5, N8, N9, W14-A1,
W14-A2), is [`../docs/waves/w14-acceptance.md`](../docs/waves/w14-acceptance.md).

The same task added one line to `e2e/day1.spec.ts`'s "Invite a Procurement user"
step -- `await page.reload()` between the click and its assertions -- which is the
whole of **N3** (a reload-surviving roster; before it both assertions passed on this
browser's own `sessionStorage` echo). Nothing else in that file changed at the time:
it was still the V1 walk and still red at its last step, so N3's verdict was that
step's line in the report, not the file's exit code. Task E26/F02/US01/T01 later
reconciled the whole file to the V2 shell (see "End-to-end (Day-1 browser walk)"
above); the reload this task added to the invite step survives unchanged in that
reconciliation.

### What runs and what skips

| Row | Test | Gate |
|---|---|---|
| N3b-1/2 | the Admin invites the second account; the pane renders `Invitation ready for ...` with a copyable `/invite/accept#...` link and never the word "sent" (`mailDelivered` is `false` by construction, ADR-026 §D6); the roster row is `Invited` | `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `_PASSWORD` |
| N3b-3/4 | a fresh context opens the link: **Join {workspace}**, no token in the address bar, no invited address on screen; **Continue with Microsoft Entra ID** opens the `loginPopup`, the CTA becomes **Join**, and the invitee lands in *that* workspace, never on a create form | same |
| N3b-5 | the Admin's reloaded roster shows the member `Active`; a sole Admin's own **Remove** is disabled with a hint (asserted only when this Admin is the sole one, annotated otherwise) | same |
| N3b-6/7/8 | **Remove** -> inline **Yes, remove** -> the row is gone; the invitee's next load no longer offers that workspace; the used link renders "This invitation is no longer valid." | same |

Without the second account the whole group `test.skip`s with the named reason
*"requires reading the invitee's one-time passcode"* -- w15's NW-67 has Raffa
provision the invitee's second Entra account itself (a B2B guest, created via
Microsoft Graph at invite time; ADR-025 §J.8b), so a pre-existing second account
is no longer the operator prerequisite this section originally recorded here for
w14. There is no mail-catcher this wave to read that emailed passcode
automatically (a `dev`-only one is bounded future work for NW-50, W18 -- ADR-025
§J.8c), so `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `_PASSWORD` below still cannot be
supplied by a script. It need **not** share the Admin's email domain: after w14
the cross-domain invite check is a non-blocking warning (ADR-001 w14 footer;
`memberViewModel.ts#inviteDomainWarning`), never a block.

### Running it

```bash
npm ci
npx playwright install --with-deps chromium   # one-time browser download

RAFFA_E2E_BASE_URL=https://<swa-dev-host> \
RAFFA_E2E_ENTRA_EMAIL=<the Admin test account UPN -- it must already own a workspace> \
RAFFA_E2E_ENTRA_PASSWORD=<that account's password> \
RAFFA_E2E_SECOND_ENTRA_EMAIL=<the invitee test account UPN> \
RAFFA_E2E_SECOND_ENTRA_PASSWORD=<its password> \
  npx playwright test invite.spec.ts

npm run test:e2e:report   # trace / video / screenshot on failure
```

| Variable | Meaning |
|---|---|
| `RAFFA_E2E_BASE_URL` | the deployed SPA origin (`dev`) |
| `RAFFA_E2E_ENTRA_EMAIL` / `_PASSWORD` | the **Admin**: a real test account excluded from interactive MFA, already holding an Admin membership in at least one workspace (N1/N2 precede this row) |
| `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `_PASSWORD` | the **invitee**: a second account on the same Entra tenant, also MFA-excluded, holding **no** membership in the Admin's workspace when the run starts |

### Harness note

Authored against the currently-committed source of every screen it drives
(`routes/workspace/members/**`, `routes/invite/accept/index.tsx`,
`routes/signin/WorkspacePickerScreen.tsx`; cited inline). Verified here with
`npx playwright test --list` and `npx playwright test invite.spec.ts` with no
environment set (skipped with the reason, exit `0`, no sign-in attempted). A real
green run needs the deployed environment and the two accounts -- an operator act.
