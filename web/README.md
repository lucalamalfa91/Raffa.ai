# Contigo web client

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
- **`react-router-dom`** (task E06/F03/US02/T01) — client-side routing for the
  signed-in app shell (`src/components/shell/`). `/signin` itself stays gated
  on MSAL auth state, not a router route (see "Screens" below) — the router
  only covers the authenticated-and-workspace-selected app.
- **Vitest + Testing Library** — unit tests under `tests/`, mirroring `src/`.

## Commands

```bash
npm ci                    # install (CI uses this)
npm run dev               # Vite dev server on :5173, reads public/config.json
npm run generate:api      # regenerate src/api/generated/schema.ts from openapi/contigo-api.v1.json
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
| `apiBaseUrl` | Ingress FQDN of `ca-contigo-<env>-api` |
| `oidcAuthority` | `https://login.microsoftonline.com/<AZURE_TENANT_ID>` (MSAL; no `/v2.0` suffix) |
| `oidcClientId` | Tag `oidcPublicClientId` on `id-contigo-<env>-workload` (set by `modules/identity`) |
| `oidcRedirectUri` | `https://<swa defaultHostname>/` (trailing slash; matches Entra SPA redirect) |
| `oidcApiScopes` | `api://contigo-<env>-api/Contigo.Read` and `.../Contigo.Write` (stable App ID URI) |

`scripts/write_web_runtime_config.py` validates the payload and refuses
localhost / `REPLACE_WITH_*` placeholders. The Static Web App itself is
`swa-contigo-<env>` in `rg-contigo-<env>`. Deploy 404s until the HCP VCS
apply that creates it (and the workload-identity tag) is CURRENT — re-run
the web workflow after that apply.

## Screens (ADR-024 V2 route map, amending ADR-018)

Task E13/F09/US01/T01 (web-shell-v2, gap G-IA-V2) moved this app from the flat Day-1 rail to the
V2 IA: `/` now redirects to `/ask` (Ask Contigo is the home), the rail is two-tier (Ask Contigo +
Documents; "From your contracts" -- Portfolio, Renewals, Quote check, greyed until the first
validated contract), there is no Home item, and Review is a redirect into Documents rather than its
own rail destination. Pixel/behaviour reference: `inputs/design/prototypes/Contigo V2 Prototype.html`
(unpacked `contigo-v2/`). See "App shell, navigation, and the role guard" below for the rail itself.

| Route | Screen(s) | Task |
|-------|-----------|------|
| `/signin` | Sign-in (Entra redirect, idle/redirecting states) -> workspace picker (list + create + confirm) | E06/F03/US01/T01 |
| `/` | Redirects to `/ask` (R-WEB-01) -- there is no standalone Home screen in V2. | E13/F09/US01/T01 |
| `/ask` | Ask Contigo, V2 rebuild: off state below 1 validated contract (fixed headline + doc-count-dependent reason + one CTA to `/documents`); new chat (hello line, scope line naming the validated count, two capability-sourced suggestion chips, optional `?scope=<contractId>`); conversation view rendering the phase-2 reply contract (markdown, numbered citation cards, actions, follow-ups) via `ReplyBody`. Calls the real `GET/POST /api/conversations`, `POST /api/conversations/{id}/messages`, `GET /api/capabilities`, `GET /api/market/records/{id}`. See "Ask Contigo" below. | E07/F04/US01/T01; V2 rebuild E13/F09/US01/T04 |
| `/ask/:conversationId` | Same `AskRoute` as `/ask`, resuming: `useConversation` loads the conversation (`GET /api/conversations/{id}`) and renders every past turn, oldest first, with citation cards and actions still clickable; a named "not found" state for an unknown/foreign/another-user's id. | E13/F09/US01/T01 (route only); resume wired by E13/F09/US01/T04 |
| `/documents` | V2 rebuild (ADR-024 amendment to ADR-020 screen 3): onboarding empty state ("First your contracts. Then your questions.") -> a server-backed list (`GET /api/documents`, survives a reload) with a **Needs your attention** (default) / **All documents · N** filter, multi-file drop (up to 20 files, <=3 uploads in flight, one row per file from the moment it is picked), real per-file stage text polled every 2s, a **Not added** card for a rejected file (422/415/oversized, session-only, never counted), Admin-only Delete, and Review as a *state* of this same route (`?review=<id>`, reusing `routes/contracts/review/*` as-is). Calls the real `GET /api/documents`, `GET /api/documents/{id}/preview`, `POST /api/documents`, `POST /api/documents/{id}/reprocess`, `DELETE /api/documents/{id}`. See "Documents" below. | E06/F05/US01/T01, E06/F05/US02/T01; V2 rebuild E13/F09/US01/T03 |
| `/contracts` | Portfolio: filter chips + attention strip + a table sorted by severity then deadline (critical rows tinted + a red bar), plus loading/empty/error/no-match-for-filter states. Calls the real `GET /api/contracts`. See "Portfolio" below. | E07/F01/US01/T01 |
| `/contracts/:id` | Contract 360: header + 6-cell fact row + 10 tabs (Overview's recommendation card + drivers + "Needs your attention" + "Top risks", then Commercials/Products/Clauses/Obligations/Risks/Documents/Benchmark/Renewal/Activity through one shared Term/Value/Source/Confidence table), plus loading/not-found/error states. `?clause=<id>`/`?page=<n>` (an Ask citation landing) opens straight on Clauses with that clause's original wording highlighted; `state.from` drives the header's back link; header offers **Ask about it** -> `/ask?scope=<id>`. Calls the real `GET /api/contracts/{id}`, `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`. See "Contract 360" below. | E07/F02/US01/T01; citation landing by E13/F10/US01/T01 |
| `/contracts/:id/review` | Review / correction: 4-column field list (critical marker, extracted value, confidence/decision tag, Accept/Correct) + right-hand evidence pane (correction form + real correction-history trail) + gated "Mark as validated". Calls the real `GET /api/contracts/{id}`, `GET /api/contracts/{id}/corrections`, `PATCH /api/contracts/{id}`. See "Review / correction" below. | E07/F03/US01/T01 |
| `/renewals` | Renewal pipeline: threshold strip (0-30 ... 270-365 d, click = filter) + priority table (Score/Supplier/Contract/Annual spend/Renews in/Cancel by/Status) + insight card (facts + recommended action + rationale) with three actions (Start negotiation / Assign to me / Snooze) -> confirmation + Contract 360 + Savings links, plus loading/error/empty/no-window states. Calls the real `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`, `POST /api/renewals/{id}/action`. See "Renewal pipeline" below. | E08/F01/US01/T01 |
| `/quotes`, `/quotes/:id` | Quote check: this task's own upload form (no id yet) -> 4-step stepper Extract (line table + unmatched-SKU manual mapping + recalculate) -> Assessment (4 numbers, line-level P25/P50/P75 + confidence, provenance card; blocked until every line resolves) -> Target (price ladder, editable target/walk-away) -> Negotiation (outcome capture -> recorded outcome). Calls the real `POST /api/quotes`, `POST /api/quotes/{id}/assessment/recalculate`, `POST /api/negotiations/outcomes`. See "Quote check" below. | E08/F03/US01/T01 |
| `/savings` | Savings (moved from `/`, not a rail item in V2 -- reached from actions, Renewals and Contract 360): 6 KPI cells (Annual spend analyzed · Savings identified · Savings realized · Savings in progress · Contracts analyzed · Upcoming renewals) + opportunities table (Opportunity · Type · Current spend · Estimated savings · Confidence · Owner · Status · Realized), rows opening Contract 360 › Benchmark or Quote check; a benchmark-provider-unreachable KPI refresh degrades to the last-known numbers, stale-labelled, rather than blocking the screen. Calls the real `GET /api/savings/kpis`, `GET /api/savings`. See "Savings" below. | E08/F02/US01/T01; moved by E13/F09/US01/T01 |
| `/review` | Redirects to `/documents?filter=attention` -- Review is a *state* of Documents in V2, not its own rail destination or screen. `src/routes/review/` (the old rail-landing component) is no longer routed; it is unrouted/orphaned pending a cleanup task, not deleted (outside this task's own file scope). See "Review queue" below. | E13/F09/US01/T01 |
| `/workspace/members` | Members & roles: Member/Role/Status/Last active table + invite pane (email, Admin vs Procurement radios, Send). Calls the real `POST /api/workspaces/{tenantId}/invites`. Non-admin visits stay on the shell's request-access gate. See "Members & roles" below. | E06/F04/US01/T01 |

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
  states (list, create, "you're in `<workspace>`") instead of the old
  standalone, centered `.workspace-picker` card -- the prototype never treats
  the workspace list as a separate screen, only a state of screen 1.
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
that has not changed. What task E06/F03/US02/T01 added is `src/App.tsx`'s
*second* decision, evaluated after that: signed in **and** a workspace
already picked this session (`workspaceStore.ts`'s `loadCurrentWorkspace()`)
mounts `src/components/shell/WorkspaceShellApp.tsx` (its own `<BrowserRouter>`
+ route table) instead of `SignInRoute`. `WorkspacePickerScreen.tsx`'s
"Continue to `<workspace>` →" control (added by the same task) is a plain
hard navigation (`<a href="/">`), not a client-side link, since no router is
mounted yet at that point in the tree -- the resulting fresh page load is
what re-evaluates `App.tsx`'s check with both facts already true.

### App shell, navigation, and the role guard (ADR-024 V2 amendment to ADR-018/ADR-019; task E13/F09/US01/T01, gap G-IA-V2; originally task E06/F03/US02/T01)

- **Two-tier rail** (`src/components/shell/RailNav.tsx`, model in
  `src/components/shell/navItems.ts`) replaces the flat, eight-item Day-1 list.
  **Primary**: Ask Contigo (badge `⌘K`, a nested conversations slot -- the
  caller's own last 5 conversations from `GET /api/conversations`, active one
  in accent, plus "+ New chat"; `useRecentConversations` re-fetches on every
  navigation rather than once per shell mount, since a new conversation is a
  routine, every-few-clicks event -- wired by task E13/F09/US01/T04) and
  Documents (badge `N to review`, accent, when this browser has a tracked
  document in `NeedsReview`, else `N docs`, else no badge --
  `src/routes/documents/documentStore.ts`'s session-scoped tracked list).
  **Known gap (task E13/F09/US01/T03, out of that task's own
  `components/shell/**` do-not-touch scope):** `GET /api/documents` exists
  now and `routes/documents/` reads it exclusively
  (`useDocumentsList.ts`), but nothing under `routes/documents/` calls
  `documentStore.ts#rememberDocument` any more, so this rail badge silently
  reads as empty (no badge, never `N to review` / `N docs`) for any session
  that starts after this change -- see `documentStore.ts`'s own header
  comment for the full provenance. A follow-up `components/shell/` task
  should replace this rail's `loadTrackedDocuments()` call with a real count
  sourced from `listDocuments` (e.g. a small `useDocumentCounts` hook
  `AppShell.tsx` fetches once, the same shape
  `useValidatedContractCount.ts` already establishes for the secondary rail
  tier below), then `documentStore.ts` can be deleted outright.
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
- **Role guard (AC-2)** -- "Workspace & members" is the one admin-only
  surface, now a footer link (`navItems.ts#canManageMembers`, unit-tested)
  rather than a row in the flat list; `src/components/shell/RequireRole.tsx`
  is the same guard at the route level (defense in depth for a direct URL
  visit), which renders ADR-018's "request access" state instead of the real
  screen.
- **Role source is interim** (`src/components/shell/workspaceRole.ts`): no
  JWT/claims wiring exists yet (ADR-010 is not wired into
  `backend/src/Contigo.Api/Program.cs`), so there is no server-issued "what
  is my role" answer today. The default is `admin` (whoever picked a
  workspace in this browser created it, and is therefore its Admin --
  the same fact `workspaceStore.ts`'s `roleLabel` already encodes). **An
  operator/demo-runner can see the Procurement-gated state by visiting the
  app with `?role=procurement` once** (e.g. `https://<swa-host>/?role=procurement`);
  the override is session-scoped (`sessionStorage`) and two-valued only --
  it is not, and must never be read as, a real authorization claim. Once
  ADR-010's claim wiring lands, only this one function changes.
- **Global Ask bar (AC-3)** -- `src/components/ask-bar/GlobalAskBar.tsx`
  renders on every routed screen (mounted once, above `<Outlet/>`, in
  `AppShell.tsx`). Enter (or a suggestion chip) always opens a **new chat**:
  it navigates to `/ask` with `{ state: { query, newChat: true } }`
  (`useLocation().state` -- `AskRoute` reads `state.query` to seed and ask a
  brand-new conversation immediately, task E13/F09/US01/T04). Cmd/Ctrl+K
  focuses the input from anywhere. Suggestion-chip copy
  (`src/components/ask-bar/askSuggestions.ts#getAskBarCopy`) fetches
  `GET /api/capabilities` once (task E13/F09/US01/T04, gap G-CAPABILITIES)
  and, once it resolves, prefers that catalog's own `exampleQuestions` for
  the capability key matching the current route; the pre-existing static
  per-route copy is the fallback while the fetch is in flight, fails, or has
  no entry for the current screen -- never a blank chip row. The placeholder
  itself still switches to "Ask Contigo switches on after your first
  validated contract" while `!kbReady`, regardless of route (ADR-024 V2
  amendment) -- this bar gets the user to `/ask`, it does not answer them
  itself.

**Workspace list is a client-side cache, not a server query** -- there is no
backend endpoint that lists the workspaces a signed-in identity belongs to
(`backend/src/Contigo.Api/WorkspaceEndpointExtensions.cs` maps only
`POST /api/workspaces` create and `POST /api/workspaces/{tenantId}/invites`;
creating a workspace does not create a membership for the caller, since
ADR-010's claims wiring is not in force yet -- see that file's own doc
comment). `src/routes/signin/workspaceStore.ts` documents this gap in full
(including exactly which backend types a future `GET` endpoint would touch)
and is the interim: it remembers, per signed-in account
(`localStorage`, keyed by MSAL `homeAccountId`), every workspace *this
browser* has actually created via the real `POST /api/workspaces` call --
never fabricated data, just not discoverable from another browser/device
until a backend list endpoint exists. The selected/current workspace is
`sessionStorage`-scoped (`workspaceStore.ts`'s `selectCurrentWorkspace`/
`loadCurrentWorkspace`) so a later screen (the nav shell, portfolio, ...) can
read which tenant to send as the `X-Tenant-Id` header every other backend
endpoint requires today -- no screen outside this task's scope consumes it
yet.

`contractCount` is always `0` and `roleLabel` is always `"Workspace Admin"`
on every row this screen renders: the former because this screen never calls
the portfolio API (a freshly-known workspace has genuinely ingested nothing
yet), the latter because there is no server-issued role claim to read yet
(ADR-010). `currencyRegion` is omitted entirely -- `WorkspaceTenant`
(backend/src/Contigo.Identity.Workspace/Domain/WorkspaceTenant.cs) has no
such column. All three are flagged in `workspaceStore.ts`'s own doc comments
rather than silently invented.

### Documents (ADR-020 screen 3; V1 tasks E06/F05/US01/T01 + E06/F05/US02/T01; V2 rebuild task E13/F09/US01/T03, `contigo-v2/screens-v2.md` #3/#4)

`src/routes/documents/` implements `/documents` as three states
(`index.tsx`'s own header comment; mirrors `contigo-v2/app.jsx`'s own
`docView: 'list' | 'review'` state machine), not V1's single
upload-then-table screen:

1. **Onboarding empty** (`OnboardingEmptyState.tsx`) -- this tenant has no
   tracked document at all, not even an in-flight/rejected one this session
   (`index.tsx`'s `isEmpty`: fetch state is `"ready"` and `documents` /
   `localUploads` / `rejected` are all empty). "First your contracts. Then
   your questions." and the three-step copy (`01 · Upload` / "Drop your
   contracts", `02 · Process` / "Contigo extracts the facts", `03 · Ask` /
   "Ask Contigo") are quoted **verbatim from the literal prototype markup**
   (`contigo-v2/markup.html`), not from `screens-v2.md`'s own shorthand
   summary of the same block ("02 · Review") -- ADR-024 names the prototype
   itself, not a summary of it, as the pixel/copy reference.
2. **List** (`AttentionFilter.tsx` + `DocumentStatusTable.tsx`) -- the
   default once anything exists; server-backed
   (`useDocumentsList.ts`, `GET /api/documents`, R-DOC-06 "reloading the
   browser shows the same list as before"), not the V1 `sessionStorage`
   table (see "`documentStore.ts` is deprecated..." below).
3. **Review, a state of Documents** (`ReviewState.tsx`,
   `?review=<documentId>`) -- rendered in place of the list, never a
   separate route.

**Upload (dropzone, shared by onboarding and list; `UploadDropzone.tsx`,
`uploadPipeline.ts`)**:

- Widened accept list -- PDF · DOCX · XLSX · PNG · JPG
  (`accept=".pdf,.docx,.xlsx,.png,.jpg,.jpeg"`, PNG/JPG via OCR, D7/ADR-017),
  50 MB / file (`uploadPipeline.ts#MAX_FILE_BYTES`, checked client-side
  purely to skip a doomed round trip -- the server's own `413` stays
  authoritative). A visible "Upload contracts" button plus a
  visually-hidden `aria-label="Choose contract files from your computer"`
  file input (the keyboard-/screen-reader-operable path, ADR-019) +
  drag-and-drop as a progressive enhancement, + "Use sample file"
  (`sampleDocument.ts`, unchanged from V1 -- a small, syntactically minimal,
  content-free PDF; **this repo still ships no real sample contract
  asset**).
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
  2 s while any row is non-terminal (`useDocumentsList.ts`, R-DOC-09) and
  stopped once every row is terminal.
- **"Not added" card, R-DOC-04** (`UploadResultCard.tsx`,
  `uploadPipeline.ts#getRejectionReasonCopy`) -- a rejected file never
  becomes a row and is never counted in the list summary; copy is keyed by
  the admission gate's own `reason`: `not_a_contract` ("this looks like a
  recipe, not a contract...") or `no_readable_text` ("Contigo could not read
  any contract text in this file...") for a `422`, the server's own message
  for a `415` (wrong format) or an oversized file rejected client-side
  before any request is sent -- all quoted verbatim from
  `inputs/requirements.md` §6, not the backend's own shorter `hint`
  fragment. A `Quote`-typed outcome offers "Open Quote check" -> `/quotes`
  (`documentTable.ts#getRowAction`, OQ-askv2-008's own assumption: no
  automatic Quote record, just a hand-off).
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
  and every pure function in `../contracts/review/reviewViewModel.ts`
  **unmodified** (this task's own "reuse as-is" file-scope boundary) -- this
  file is a new orchestration wrapper around those building blocks, not a
  copy of them, because the routed `../contracts/review/index.tsx`'s own
  `ReviewRoute` is bound to the URL param `:contractId` and hard-codes
  `navigate('/contracts/:id')` on validation, neither of which fits a
  document-id query param or "return to Documents with the validated hook."
  Fetch order mirrors that same file's own logic (`getContract360` then
  `getCorrectionHistory`) rather than importing it.
- **No backend "finalize" endpoint exists, by design.** "Mark as validated"
  is a purely client-side gate
  (`computeReviewProgress`/`isValidationBlocked` -- every blocking field
  resolved), with no write call of its own; a reload before that click
  re-asks any field that was only session-`Accept`ed, never `Correct`ed --
  the same, already-shipped consequence the routed Review screen's own
  header comment names.
- On validation, `index.tsx` returns to the list and shows the "*X* is now
  askable." hook (`justValidated`, a single slot, superseded by the next
  upload batch or another validation) with an "Ask: when does it expire?"
  link into a new, scoped Ask chat (`/ask?scope=<contractId>`).

**Provenance -- documented ahead of its own backend counterpart.** This
task (`target_repo: contigo-web`) added `GET /api/documents`,
`GET /api/documents/{id}/preview`, `POST /api/documents/{id}/reprocess` and
`DELETE /api/documents/{id}` to `openapi/contigo-api.v1.json`, plus the
widened `documentType` / `detectedType` enum (the original six members plus
`Quote` / `Invoice` / `PriceList` / `Nda` / `Dpa`) and the `413` / `415` /
`422` admission-gate responses on `POST /api/documents` -- all authored from
`inputs/requirements.md` §6 and the sibling backend tasks' own spec text
(epic-13/feature-04, `task-01-documents-admission.md` /
`task-02-documents-v2-api.md`), **not read off a running handler**: none of
those four operations, the enum widening, or the admission gate exist in
`backend/` in this worktree yet. See each operation's own OpenAPI
`description` for the exact provenance note. `npm run generate:api`
reproduces `src/api/generated/schema.ts` from this contract today regardless
of backend state (AC-6) -- once the real backend lands, only its own
response shapes need reconciling against what is already documented here,
never the other way around.

**`documentStore.ts` is deprecated for this route, kept only for
`RailNav.tsx`'s own "N to review"/"N docs" badge**, which this task's own
`components/shell/**` do-not-touch boundary could not rewire -- see "App
shell, navigation, and the role guard" above for the full gap and its own
named follow-up.

### Portfolio (ADR-020 screen 4, task E07/F01/US01/T01, us-01-portfolio-list-filters)

`src/routes/contracts/` implements screen 4: AC-1 filter chips, AC-2 attention strip, AC-3
severity/deadline sort with critical-row tinting, AC-4 loading/empty/error/no-match states. This is
the first web epic to build a screen against a backend route from E02-E05 that `typescript-client-regen`
deliberately left out of `openapi/contigo-api.v1.json` -- see "API client" below for what this task
added to the contract.

- **Fetch-once, filter client-side** (`index.tsx`'s own header comment has the full reasoning): the
  screen calls `GET /api/contracts` exactly once per mount (plus once per manual Retry), for the whole
  first page (`PortfolioPageRequest.MaxPageSize` = 100 rows), then computes every row's
  attention/severity and applies both the seven AC-1 chips and the AC-2 attention-strip toggle entirely
  in memory (`portfolioAttention.ts`, `portfolioFilterState.ts`) -- mirroring day1-demo.html's own
  `allContracts.filter(...)` architecture. The attention-strip counts must reflect the whole portfolio
  regardless of which filters are active, and the severity computation (deadline-within-45-days,
  needs-review, ...) has no server-side query-parameter equivalent at all, so a fetch-once/filter-local
  design avoids seven independent round trips for no accuracy gain. `apiClient.getPortfolio` still
  accepts the endpoint's full filter/paging surface for a future task to push filtering server-side once
  portfolios routinely exceed one page.
- **AC-1, filter chips** (`PortfolioFilters.tsx`) -- Supplier, Category, Renewal period, Spend, Status,
  Risk, Auto-renewal, in that order (day1-demo.html's own `filters` array). Two are honest placeholders:
  **Category** is disabled (`PortfolioFilter.cs`'s own doc comment: no Category concept exists anywhere
  in the schema yet -- Suppliers/Products is still an empty scaffold); **Supplier** filters by the raw
  id `GET /api/contracts` itself returns, entered as free text, because no supplier-name-resolution
  endpoint exists anywhere in this codebase (same gap `documents/documentStore.ts` already names for the
  Documents table).
- **AC-2, attention strip** (`AttentionStrip.tsx`) -- "Deadlines < 45 d", "Need review",
  "Failed / processing", "High risk", quoted verbatim from day1-demo.html's own `attDef` array. Each
  cell's count is always computed from the whole portfolio; clicking one narrows the table to that
  bucket (click again to clear) via the exact same `AttentionRow` predicates the seven chips use.
- **AC-3, sort + tint** (`portfolioAttention.ts`, `PortfolioTable.tsx`) -- severity (0-3) then soonest
  cancellation deadline; severity 3 gets the shared `.row-critical` tint + red bar (ADR-019 component
  catalogue), severity 2 gets a lighter, screen-scoped neutral bar (`.row-attention`, `contracts.css`) --
  both quoted from day1-demo.html's own per-row `rowBg`/`bar` fields. Column one (`Attention`) always
  carries a text label (ADR-019 "urgency in column one, not colour-only"), never a bare colour. Two
  fields the compiled prototype's own mock data has but `GET /api/contracts` does not are adapted, not
  fabricated: the prototype's `status` is a closed `'Failed' | 'Processing' | 'Needs review' | ...`
  enum, while the real `Contract.Status` is free text (bootstrap default is the literal string
  `processing`), so this module matches case-insensitively and treats "contains 'review'" as the
  needs-review signal; and the domain's fourth risk tier, `RiskSeverity.Critical` (ADR-019 only defines
  three), folds into the same "High risk" bucket/tag as `High`.
- **AC-4, states** (`index.tsx`) -- loading (a `.skeleton`-bar placeholder), empty (zero contracts at
  all -> a "No contracts yet" CTA linking to `/documents`), error (any non-2xx, with a 503-specific
  plain-language message + Retry), no-match-for-filter (contracts exist but none pass the current
  filters -> its own "Clear filters" CTA, separate from the filter panel's own always-present one).
- **No currency anywhere**: `GET /api/contracts` (`PortfolioListItem`) carries no currency code --
  `PortfolioEndpointExtensions.GetPortfolioAsync`'s own response projection never selects
  `Contract.Currency` -- so "Annual spend" renders a grouped plain number, never a fabricated symbol.
  "Contract" shows the contract's `Type` (Msa/OrderForm/...), the closest identifying field the schema
  currently records; `Contract` itself has no title/name column yet.

### Contract 360 (ADR-020 screen 5, task E07/F02/US01/T01, us-01-contract-360)

`src/routes/contracts/contract360/` implements screen 5: AC-1 header (supplier kicker, type/status
tags, doc count), AC-2 6-cell fact row (annual spend, TCV, start→end, renewal, cancellation
deadline, risk + priority score), AC-3 Overview (recommendation card + drivers + "Needs your
attention" + "Top risks"), AC-4 the remaining 9 tabs with facts/AI never mixed (ADR-019). This is
the second web epic to build a screen against E02-E05 backend routes `typescript-client-regen`
deliberately left out of `openapi/contigo-api.v1.json` -- see "API client" below for what this task
added.

- **Fetch order, not fetch-once**: unlike Portfolio's single call, this screen fetches
  `GET /api/contracts/{id}` first (`index.tsx`'s own header comment has the full reasoning) -- a
  `404` there is a named "not found" state, distinct from a transport/5xx error, and short-circuits
  before anything else runs. Once the contract is confirmed to exist, `GET /api/renewals` (the
  tenant's whole auto-renewing pipeline) and `GET /api/renewals/{contractId}/priority` (this
  contract's score breakdown) fetch together -- **both independently optional**: a non-auto-renewing
  contract legitimately has no pipeline entry, and either failing degrades the Overview
  recommendation / header priority fact to its own honest "not yet available" state rather than
  failing the whole screen.
- **AC-1/AC-2, header + fact row** (`Contract360Header.tsx`) -- quoted verbatim from
  day1-demo.html's own header block. Two gaps already established for the identical problem on the
  Portfolio screen are reused, not re-solved: `Contract` has no title field, so the h2 title reuses
  the type-label proxy (`getContractTypeLabel`); `Contract.SupplierId` is a bare id, so the kicker
  reuses `formatSupplier`'s short-id-plus-tooltip treatment. The prototype's "N-day notice" subtext
  is dropped (no `CancellationNoticeDays` column exists anywhere yet) in favour of a real days-until
  countdown; "N documents in family" becomes "N documents" (this endpoint returns one contract's own
  linked documents, not the full `ParentContractId` amendment chain).
- **AC-3, Overview tab** (`OverviewTab.tsx`, `contract360ViewModel.ts#buildRecommendation`) -- the
  recommendation's `statement`/`rationale` are the Renewals module's own real, deterministic (not
  LLM) `recommendedAction`/`explanation` text for whichever `GET /api/renewals` pipeline item
  matches this contract's id (`Contigo.Renewals` is not reachable from `GET /api/contracts/{id}`
  at all) -- never invented UI copy. A contract with no matching pipeline entry gets a named gap
  ("No renewal recommendation for this contract"), not a fabricated one. The 3 driver numbers are
  quoted from day1-demo.html's own `cur.cancelDays`/`cur.market`/`cur.potential` block: Cancellation
  deadline is a real header fact shown regardless of whether a recommendation exists; Market
  position/Potential savings are honestly "Not yet available" (need the R3 Benchmark/Savings
  modules, `RenewalInsightRecommendations`'s own backend doc comment). "Needs your attention" and
  "Top risks" adapt the cited prototype's own `keyTerms`/`topRisks` filter-and-slice rules to every
  real per-field confidence this aggregate carries (Products/Clauses/Obligations; Risks gets its own
  section instead of competing for the same 3 slots).
- **AC-4, the other 9 tabs** (`FactTable.tsx`, `RenewalTab.tsx`) -- one shared Term/Value/Source/
  Confidence template (ADR-020: "one template ... .table pattern") reused by Commercials, Products,
  Clauses, Obligations, Risks, Documents, and Overview's own supplementary "Contract details" block
  (the `Contract360Overview` fields -- effective date, governing law, version -- have no other tab
  home in the whole 10-tab set, so they render here rather than being silently dropped). Benchmark
  and Activity render the template's own empty state with a named reason (R3/R4), never a blank box.
  Renewal adds the priority-score component table on top of its own facts half (`RenewalTab.tsx`) --
  `PriorityScoreCalculator`'s five named components, each with its own real, computed explanation
  string. "Why this score" switches this same screen's own tab to Renewal (no navigation); "Open in
  renewals" is a real link to `/renewals`.
- **Facts vs AI (ADR-019)**: kept apart at the type level, not just in markup --
  `contract360ViewModel.ts#buildRecommendation` returns a `Recommendation` (statement/rationale/
  drivers) that is never merged into the `FactRow[]` shape every tab-row builder returns; only
  `OverviewTab.tsx` ever mounts the `.ai-recommendation` card, and every other tab renders through
  the shared `FactTable`. `tests/routes/contracts/contract360/*.test.tsx` assert the separation both
  ways: structurally (a `Recommendation` has no `source`/`confidencePct` keys) and in the rendered
  DOM (the recommendation card contains no `<table>`; no fact table repeats the recommendation's own
  text).
- **Confidence is a 0-1 fraction on the wire** (`Contract360ProductBody.confidence` etc., e.g.
  `0.92`), not the 0-100 percentage `styles/semantics.ts#getConfidenceTag` expects --
  `contract360ViewModel.ts#toConfidencePercent` is the one conversion point every row builder uses.

**Task E13/F10/US01/T01** (contract360-landing, ADR-024 "citation landing, scoped conversations";
ADR-020 screen 5 amendment; us-01-contract360-landing) makes this screen the landing of every Ask
citation, still inside today's tabbed shell (the no-tabs V2 answers-band layout is this feature's
own P2 follow-up, R-WEB-06):

- **`?clause=<clauseId>` / `?page=<n>`** (`contract360ViewModel.ts#resolveHighlightedClauseId`) open
  straight on the Clauses tab; a `?clause=` naming a real clause on this contract wins outright, a
  bare `?page=` highlights the first clause whose `sourcePage` matches -- the two never combine (an
  unmatched `?clause=` does not fall back to `?page=` even when both are present, to avoid
  highlighting a different clause than the one actually cited). The matched clause's own `rawText`
  (its original wording, `sourceSpan` emphasised when present) renders unconditionally in a new
  `ClauseHighlight.tsx` card directly below the unmodified Clauses list -- no row click required, and
  it scrolls itself into view.
- **`state.from`** (`contract360ViewModel.ts#resolveBackLink`) drives a "← Ask Contigo" back link
  above the header when set to `"ask"` (ADR-020 screen 5 "Back label follows the origin"); the other
  three named origins (Documents/Portfolio/Renewals) resolve too but nothing sends them yet -- only a
  future Ask-route task (`web/src/routes/ask/**`, out of this task's own file scope) can set `"ask"`
  for real, so this is proven by a router-state unit test today, not an end-to-end click from Ask.
- **Ask about it** (`.btn-secondary`, header) links to `/ask?scope=<contractId>` -- a *new* chat
  scoped to this contract. Live since task E13/F09/US01/T04: `AskRoute` reads `?scope=` (while no
  conversation is open yet), passes `scopeContractId` to `POST /api/conversations`, and templates its
  two suggestion chips with this contract's own supplier name (`GET /api/contracts/{id}`, defensively
  read off the wire object -- see that task's own `askViewModel.ts#suggestionsFor` doc comment for
  why this is not yet a typed generated field).
- **Supplier name, defensively** (`contract360ViewModel.ts#resolveSupplierLabel`) -- the header kicker
  now prefers a wire-provided `supplierName` over the `formatSupplier` id-fragment fallback above,
  reading it off the response object rather than the generated `Contract360HeaderBody` type (which
  does not carry that field yet -- `web/openapi/contigo-api.v1.json` / `web/src/api/generated/` are
  out of this task's own file scope). Once the phase-4 backend task regenerates both, this starts
  rendering the real name with no further client change.

### Review / correction (ADR-020 screen 6, task E07/F03/US01/T01, us-01-field-review-correction)

`src/routes/contracts/review/` implements screen 6: AC-1 4-column field list (critical marker,
extracted value + source, confidence tag, decision), AC-2 confidence mapping, AC-3 evidence pane
(correction form + version history), AC-4 gated "Mark as validated" with a visible reason. Reached
from `contract360/Contract360Header.tsx`'s "Review extraction" button and
`contract360/OverviewTab.tsx`'s "Needs your attention → Review all" link (both already wired by
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
  (`backend/src/Contigo.Documents.Contracts/Domain/ExtractionEvidence.cs`, task E02/F01/US02/T01)
  stores exactly the per-field confidence/source-span/extraction-job trail AC-2/AC-3 describe, keyed
  by the same `FieldName` scheme `CorrectionHistory` already uses -- but no endpoint in
  `backend/src/Contigo.Api` reads it, and `Contract360QueryService` never joins it either. Until a
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

### Ask Contigo (ADR-020 screen 7 / ADR-024 §6, task E07/F04/US01/T01; V2 rebuild task
E13/F09/US01/T04, us-01-web-v2 AC-1/AC-3/AC-5/AC-6, `contigo-v2/screens-v2.md` #2)

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
  reason + CTA do (`askViewModel.ts#buildOffCopy`): "Upload a contract first" / "Upload a contract"
  when the tenant has no document at all, "still processing or waiting for review" / "Go to
  Documents" once at least one exists but none is validated yet (`GET /api/documents`'s own
  `totalCount`, fetched only while off -- not the session-only `documentStore.ts` tracker the rail
  badge's own known gap above already documents as broken).
- **New chat** -- hello line (`ASK_HELLO`, "What do you want to know?"), scope line naming the
  validated count (`askViewModel.ts#buildScopeLine`, "Answers only from N validated contract(s) ·
  cites or abstains", the parenthetical supplier-name list omitted honestly until a future backend
  task resolves it) plus the prototype's structured/legal trailer sentence, input placeholder "Ask
  Contigo — spend, dates, clauses, liability…", two suggestion chips (`suggestionsFor`) from the
  `ask` capability's own `exampleQuestions` (`GET /api/capabilities`), falling back to a small static
  pair while the catalog has not loaded. Asking (typed, a chip, or the seed query the global Ask bar
  carries in router state, `newChat: true`) runs `createConversationAndAsk`:
  `POST /api/conversations` (with `scopeContractId` when `?scope=` is present) then
  `POST /api/conversations/{id}/messages`, then the URL becomes `/ask/<conversationId>`
  (`navigate(..., { replace: true })`). `?scope=<contractId>` templates the two chips with the real
  supplier name instead (`buildScopedSuggestions`, defensively read off `GET /api/contracts/{id}`
  until that field is a typed generated one).
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
  P25/P50/P75 band, provenance label, updated date); a **contigo** feature citation navigates to its
  own href. Follow-up chips post as a new message in the same conversation, the same `ask()` path a
  typed question uses.
- **Resume** (`/ask/:conversationId`, R-CONV-02 AC-1) -- `useConversation.ts` loads the conversation
  (`GET /api/conversations/{id}`) and turns every stored message, oldest first, into the same turn
  shape a live turn produces (`askViewModel.ts#buildTurnsFromConversation`); a resumed Contigo turn's
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
- **`X-User-Id` on every call** (`client.ts`, OQ-askv2-005/ADR-022) -- see "API client" below for the
  full header provenance; today only `/api/conversations*` actually reads it
  (`ConversationsEndpointExtensions.TryResolveUserId`).

### Renewal pipeline (ADR-020 screen 8, task E08/F01/US01/T01, us-01-renewal-pipeline)

`src/routes/renewals/` implements screen 8: AC-1 threshold strip, AC-2 priority table, AC-3 insight
card + three actions with a real write + confirmation, AC-4 loading/error/empty/no-window states.

- **Fetch order, gated on the whole score set**: like Contract 360, this screen calls `GET /api/renewals`
  first (a non-2xx is the named "error (engine unavailable)" state) and only declares `"ready"` once
  every row's own `GET /api/renewals/{contractId}/priority` score has *also* resolved (`Promise.all`) --
  AC-2's "Score" column is a named, required column, not an optional extra, and there is no bulk
  priority endpoint on the backend (it only ever answers for one contract at a time), so this is
  genuinely N calls. A single row's own priority call failing degrades only that row's Score cell to
  "-", never the whole screen.
- **AC-1, threshold strip** (`ThresholdStrip.tsx`, `renewalPipelineViewModel.ts`) -- seven buckets, 0-30
  through 270-365 days, quoted from day1-demo.html's own `winDef=[365,270,180,120,90,60,30]` window
  list and rendered ascending to match this task's own AC-1 wording (the prototype's own array order is
  descending, an implementation detail of its JS, not a locked visual spec). Reuses the exact same
  `.attention-strip`/`.attention-cell` classes Portfolio's own `AttentionStrip.tsx` already established
  (ADR-019 names "attention/threshold strip" as one shared catalogue entry); `.is-urgent` is gated on
  both a non-zero count *and* the bucket's own upper bound being <=120 days (quoted from that
  prototype's own `fg` rule for its window strip) -- unlike Portfolio's four buckets, a renewal due in
  270-365 days is not inherently bad news.
- **AC-2, priority table** (`RenewalTable.tsx`) -- Score / Supplier / Contract / Annual spend / Renews
  in / Cancel by / Status, quoted verbatim from screens.md #8. Two honest, cited adaptations: `GET
  /api/renewals` carries no contract name or type at all (unlike `GET /api/contracts`), so "Contract"
  reuses the same short-id-plus-tooltip treatment `formatSupplier` already established for the
  identical `supplierId` gap, applied to `contractId` instead, rather than adding a second whole-portfolio
  fetch just to resolve one column; "Status" has no server read-back at all (`POST
  /api/renewals/{id}/action` has no matching `GET`), so it shows this browser's own session-tracked
  action text once set, "Open" otherwise (`renewalActionStore.ts`). Selecting a row for the insight card
  is a native `<button>` in the Score cell (ADR-019: "every interactive control is native"), not a bare
  `<tr onClick>`.
- **AC-3, insight card + actions** (`InsightCard.tsx`) -- spec §9.3's fields (spend, cancellation
  deadline, uplift, market position, potential savings, owner) plus the recommended action + rationale,
  in one `.detail-pane` card (ADR-019's own "340-400px ... wraps below the list" component). Unlike
  Contract 360's Overview tab (a *separate* card from every fact table), spec §9.3 and screens.md #8
  both put facts and the recommendation in one card -- the backend's own `RenewalInsightCard` wire
  shape already keeps the two apart as named, non-overlapping objects, so this component preserves that
  same separation visually (plain facts, then a nested `.ai-recommendation` block) without needing a
  second card. Uplift/market position/potential savings are honestly "Not yet available" (need the R3
  Benchmark/Savings modules) -- never a fabricated figure.
  - **The three actions call the real, durable `POST /api/renewals/{id}/action`** (`RenewalActionService
    .SetActionAsync`, task E03/F03/US01/T02) -- not a client-side simulation. "Start negotiation" /
    "Assign to me" / "Snooze to 90-day threshold" map to `RenewalActionStatus`/free-text `action`
    quoted verbatim from day1-demo.html's own `rAct` object (`renewalPipelineViewModel.ts#getRenewalActionPlan`);
    `owner` is always this screen's own signed-in `userLabel` (threaded from `WorkspaceShellApp.tsx`,
    the same identity `RailNav.tsx` renders) -- there is no separate assignee picker in V1.
  - **The confirmation panel is adapted, not copied, from the prototype.** day1-demo.html's own
    confirmation text claims "A SavingsOpportunity was created and appears on the Savings screen" --
    this app cannot honestly say that: `SavingsOpportunityService.CreateAsync` ("identify" a new
    opportunity) exists on the backend but is not yet wired to any HTTP route (`POST /api/savings` does
    not exist; `GET`/`PATCH /api/savings/{id}` both need an id this screen has no way to obtain). The
    real, durable write here is the renewal action above; `renewalActionStore.ts`
    (`sessionStorage`, the same interim pattern `workspaceStore.ts`/`documentStore.ts` already
    establish for their own missing-endpoint gaps) records it as this browser's own tracked opportunity
    for the council decision carried into this story ("Action creates an opportunity visible on Home",
    its own literal wording at the time), and the confirmation links to both **Home** (`/`) and
    **Contract 360** (`/contracts/:id`, AC-3's own named link) instead of overclaiming a backend entity
    that was not actually created. Home (task E08/F02/US01/T01, see "Savings" below) landed and does
    exactly this: `savingsViewModel.ts#buildOpportunityRows` imports `loadTrackedRenewalActions()` from
    this module and merges its rows -- most-recently-acted first -- ahead of the real, persisted
    opportunity list. **Known gap, task E13/F09/US01/T01 (out of that task's own file scope):** V2
    moved this screen to `/savings` and made `/` redirect to `/ask` -- this confirmation panel's own
    `<Link to="/">` (`InsightCard.tsx`) still targets the old path verbatim and now lands on Ask, not
    Savings; likewise `../quotes/NegotiationStep.tsx`'s "See it on Home ->" link. Neither
    `routes/renewals/**` nor `routes/quotes/**` is in that task's "Files to create or modify" -- a
    follow-up task should repoint both links to `/savings`.
- **AC-4, states** -- loading (`.renewal-skeleton`), error (503-aware, names the renewal engine
  specifically per screens.md #8's own "error (engine unavailable)", with Retry), empty (zero renewals
  at all -> "No renewals in your pipeline yet" + a link to `/contracts`), no-window (renewals exist but
  none fall in the selected threshold bucket -> its own "Show all renewals" CTA, the same
  named-empty-state-per-cause convention Portfolio's own no-match-for-filter state already uses).

### Quote check (ADR-020 screen 10, task E08/F03/US01/T01, us-01-quote-check)

`src/routes/quotes/` implements screen 10: AC-1 the 4-step stepper (Extract -> Assessment -> Target
-> Negotiation), AC-2 Extract's line table + unmatched-SKU manual mapping + recalculate (assessment
blocked until resolved), AC-3 Assessment's 4 numbers + line-level P25/P50/P75 table + provenance
card, and Target's price ladder + editable target, AC-4 Negotiation's outcome form -> recorded
outcome.

- **Real backend, not the cited prototype's own fixture.** By the time this task started, backend
  epic E05 (`Contigo.Quotes` module) had already implemented and wired `POST /api/quotes`,
  `GET /api/quotes/{id}/assessment`, `POST /api/quotes/{id}/assessment/recalculate`, and
  `POST /api/negotiations/outcomes` into `Program.cs` -- the parent story's own "E05 quote API
  (assumed)" dependency turned out to already be real. `inputs/design/prototypes/day1-demo.html`'s
  own Quote check screen is one hard-coded demo scenario (fixed `qlines`/`assess`/`qbench`/`levers`
  array literals); this screen instead derives every number from the real endpoints above -- see
  `src/routes/quotes/quoteCheckViewModel.ts`'s own header comment and "API client" below.
- **No upload screen exists in the design** (screens.md #10 starts directly at "Extracted line
  items"; ADR-018 names only the detail route `/quotes/:id`, no list/upload route) --
  `src/routes/quotes/index.tsx` renders `UploadQuoteForm.tsx` itself whenever the route has no
  `quoteId` yet (both the rail nav's own `/quotes` and a fresh `/quotes/:quoteId` visit before any
  upload), then navigates to the real id `POST /api/quotes` returns. `UploadQuoteForm.tsx`'s own
  "Use sample file" button mirrors `../documents/sampleDocument.ts`'s exact precedent (a small,
  syntactically-minimal, content-free PDF -- this repo ships no real sample quote asset either).
- **AC-2, Extract** (`ExtractStep.tsx`) -- the line table's Product/SKU/discount/annual-total columns
  cannot all be sourced honestly: `QuotesEndpointExtensions.BuildAssessmentResponse` never serializes
  `QuoteLine.Sku`/`Description`/`DiscountPercent` for a *matched* line (only `SkuMappingService
  .GetUnmatchedLinesAsync`'s own small query returns those, for the still-unresolved lines only) --
  a real, pre-existing backend gap this task's `contigo-web` file scope cannot close.
  `quoteCheckViewModel.ts#mergeKnownLineDetails` keeps a running, session-local memory of every
  sku/edition/description this screen has actually seen from a real response, so a line does not
  lose its own real name the moment it resolves; an unmatched line still renders honestly as
  `Line {n}` the first time it is seen (before any correction has ever been read back). The
  manual-mapping control is a free-text "canonical SKU"/"product name" pair, not the cited
  prototype's fixed 3-option `<select>`: `SkuMappingCorrection`'s real wire shape takes an arbitrary
  caller-chosen canonical SKU and no endpoint exposes a catalog to pick from.
- **AC-2's own gate** (`quoteCheckViewModel.ts#isAssessmentBlocked`) is exactly
  `unmatchedLines.length > 0` -- `recalculateQuoteAssessment`'s own real, server-computed list, never
  re-derived. Gated at the *content* level (`AssessmentStep.tsx`'s own "Assessment blocked" card +
  "<- Back to extract"), never at the stepper-navigation level: every step is directly clickable
  (day1-demo.html's own `qsteps[i].go` behaviour), matching AC-1's "stepper" literally.
- **`recalculateQuoteAssessment`, not `getQuoteAssessment`, is this screen's own read call** -- see
  `src/api/client.ts`'s own header comment on `recalculateQuoteAssessment` for why: called with an
  empty `mappings` array as this screen's "read the current assessment + unmatched lines" call
  (`SkuMappingService.RecalculateAsync`'s own doc comment names this a valid, side-effect-free "pure
  refresh" -- there is no separate `GET` that also returns `unmatchedLines`).
- **AC-3, Assessment** (`AssessmentStep.tsx`, `quoteCheckViewModel.ts#aggregateQuote`) -- every
  quote-level number (the 4-number grid, the Target ladder, the outcome form's default "Original
  quote total") is deterministically summed from each line's own real assessment (Appendix C rule
  6), never a second, independent calculation. **No quote-level "overall position" rollup**:
  `Contigo.Quotes.Application.Assessment.QuoteMarketAssessment`'s own doc comment explicitly declines
  to invent one ("no ADR/spec names a deterministic way to collapse several lines' positions into
  one"); `summarizePositions` renders a real *tally* ("2 above market · 1 in line") instead.
- **AC-3, Target** (`TargetStep.tsx`) -- two honest departures from the cited prototype: no backend
  endpoint computes a distinct "opening target" or "walk-away/escalation" figure
  (`LineTargetSaving` gives exactly one recommended range), so "Your target"/"Walk-away" are real,
  user-editable inputs pre-filled from the real aggregate as a starting point, not a third computed
  tier; the ladder is drawn proportionally from whatever real figures `aggregateQuote` produced, not
  the prototype's own fixed pixel positions (those were specific to its one hard-coded demo quote).
- **AC-4, Negotiation levers** (`NegotiationStep.tsx`) -- a named, honest gap: `Contigo.Quotes
  .Application.Strategy.NegotiationStrategyService` (negotiation-lever recommendations + evidence,
  spec §12.1) is fully implemented and unit-tested, but `backend/src/Contigo.Api/Program.cs` never
  maps an HTTP endpoint for it (checked: no `MapGet`/`MapPost` anywhere in `backend/src/Contigo.Api`
  references `NegotiationStrategyService`/`NegotiationStrategyCalculator`/`QuoteNegotiationStrategy`)
  -- a backend task would need to add one (e.g. `GET /api/quotes/{id}/strategy`) before this screen
  can show AI-recommended levers/evidence for real. The outcome-capture control is still real: a
  required multi-select of the same 7-member `NegotiationLeverType` vocabulary
  `POST /api/negotiations/outcomes` itself validates `leversUsed` against.
- **AC-4, outcome capture** -- `realizedSaving`/`discountPercent` are always the server's own
  response (`NegotiationOutcomeCalculator.Compute`), never the client-side `previewOutcome` figure
  shown before submit; the two happen to agree only because the arithmetic is mirrored verbatim
  (spec §12.2's own worked example: 520,000 -> 435,000 = 85,000 saving, ~16.3%).
- **"...-> Home Savings Realized updates"** -- there is no `GET` (list or single) anywhere on
  `POST /api/negotiations/outcomes`, and this screen never supplies a `savingsOpportunityId` (no
  Savings UI exists yet to pick one from), so `NegotiationOutcomePropagationService`'s own
  cross-module write never runs for an outcome this screen records. `quoteOutcomeStore.ts` is the
  same kind of interim `../documents/documentStore.ts`/`../signin/workspaceStore.ts` already
  establish: a real, `sessionStorage`-scoped record of every outcome this browser actually captured.
  Savings (task E08/F02/US01/T01, see "Savings" below) has since landed, but its own "Savings
  realized" KPI cell reads the real `GET /api/savings/kpis` response directly, not this store --
  `quoteOutcomeStore.ts` stays this screen's own unconsumed local record, the propagation gap named
  above being the real, pre-existing reason no code path connects the two yet. The recorded-outcome
  panel's own "See it on Home ->" link (`<Link to="/">`, `NegotiationStep.tsx`) is real, but --
  **known gap, task E13/F09/US01/T01, out of that task's own file scope** -- `/` now redirects to
  `/ask` (V2 "No Home item"), not Savings; `routes/quotes/**` is not in that task's file scope to
  repoint it to `/savings`.

### Savings (ADR-020 screen 9, task E08/F02/US01/T01, us-01-savings-home; moved from `/` to `/savings` by task E13/F09/US01/T01, ADR-024 V2 amendment, gap G-IA-V2)

`src/routes/savings/` implements screen 9: AC-1 six KPI cells, AC-2 opportunities table, AC-3 row
navigation to Contract 360 › Benchmark or Quote check plus the benchmark-provider-unreachable
stale-labelled KPI state. Originally wired into `components/shell/WorkspaceShellApp.tsx`'s index
route (`/`, ADR-018 "/ (home)"); task E13/F09/US01/T01 moved the folder and the route to `/savings`
(V2 "No Home item" -- Savings is reached from actions, Renewals and Contract 360, not the rail) --
every fetch/render rule below is unchanged, "keep behaviour" per that task's own text.

- **Two independent fetches, two independent degrade states** (`index.tsx`) -- `GET
  /api/savings/kpis` backs AC-1's KPI row and `GET /api/savings` backs AC-2's opportunities table;
  each fetch's own failure degrades only its own section, the same "independently optional" shape
  `../contracts/contract360/index.tsx` already established for its own renewals+priority pair.
- **AC-1, KPI row** (`KpiRow.tsx`, `savingsViewModel.ts#buildKpiCells`) -- the six cells, in AC-1's own
  order: Annual spend analyzed, Savings identified, Savings realized, Savings in progress, Contracts
  analyzed, Upcoming renewals, one formatted line per currency bucket (never summed across
  currencies -- the same "group by currency" discipline `SavingsRangeByCurrency`'s own backend doc
  comment states). Only "Savings realized" carries the accent-700 highlight, quoted from
  day1-demo.html's own `kpis[2].fg` rule. `kpis === null` (never fetched, or a first-load failure)
  renders an honest "-" per cell rather than a fabricated number (Appendix C rule 10).
- **AC-3, benchmark-provider-unreachable -> stale-labelled** (`savingsViewModel.ts#reduceKpiFetch`,
  this task's own required test, proven at both the reducer level and the rendered-component level)
  -- a failed KPI refresh never blanks the row: the reducer keeps whichever summary it last
  successfully fetched (or `null`, before any success) and marks every cell `Stale` (text, not
  colour, ADR-019 accessibility baseline), with a named `.error-state` notice ("Benchmark provider
  unreachable") + Retry, copy quoted from day1-demo.html's own text for this exact state. Retrying
  never flashes the row back to a loading skeleton -- the stale numbers stay visible, tagged, for the
  whole in-flight retry, only updating once it resolves.
- **AC-2, opportunities table** (`OpportunitiesTable.tsx`) -- the eight named columns (Opportunity /
  Type / Current spend / Estimated savings / Confidence / Owner / Status / Realized), the
  "Opportunity" cell a real `<Link>` (cell-level link, not a whole-row click -- the same pattern
  `../contracts/PortfolioTable.tsx` already established, ADR-019 accessibility baseline). Loading
  (skeleton rows), error (a scoped `.error-state` + Retry, independent of the KPI row's own error
  state), and empty ("No savings opportunities yet" -> `/renewals`) states.
- **Council decision "Action creates an opportunity visible on Savings"** (`savingsViewModel.ts
  #buildOpportunityRows`) -- this session's own tracked renewal actions
  (`../renewals/renewalActionStore.ts#loadTrackedRenewalActions`, recorded by
  `../renewals/InsightCard.tsx`'s three actions) render first, ahead of every real, persisted
  `SavingsOpportunity` row from `GET /api/savings` -- immediate, visible confirmation of that
  decision without inventing a backend write this task's own file scope cannot make (`POST
  /api/savings` still does not exist; see "Renewal pipeline" above). No de-duplication is attempted
  between the two lists: a tracked action never actually creates a `SavingsOpportunity` row, so there
  is no shared id to merge on (Appendix C rule 10). A tracked row's own honest gaps -- no confidence,
  "Not yet available" estimated savings, no realized value, no currency on current spend -- render as
  such rather than a borrowed or invented figure.
- **AC-3, row navigation** (`savingsViewModel.ts#getOpportunityNavigation`) -- quoted from
  day1-demo.html's own row handler for this screen: a contract-linked opportunity opens Contract
  360's Benchmark tab (`/contracts/:id`, `state: { tab: "Benchmark" }` -- the same
  `location.state.tab` deep-link seam `../ask/index.tsx` already uses to open Clauses); one with no
  `contractId` (`SavingsOpportunityResult` carries no `quoteId` at all yet) falls back to the
  `/quotes` landing route, the same "no id yet" seam `../quotes/index.tsx` already establishes.
- **No workspace guard bypassed** -- like every other route, this screen reads
  `loadCurrentWorkspace()` directly rather than trusting `App.tsx`'s own earlier check, rendering a
  named "No workspace selected" state if that invariant is ever violated.

### Review queue (unrouted since ADR-024 V2, task E13/F09/US01/T01, gap G-IA-V2)

`src/routes/review/` used to be the `/review` rail landing (ia.md/ADR-018 only ever named
`/contracts/:id/review`, the field-review detail, as a locked route; `/review` itself was this app's
own list-landing convention for it). V2 makes Review a *state* of Documents instead
(`/documents?review=:id`, F09/T03) -- `/review` now redirects to `/documents?filter=attention`
(`WorkspaceShellApp.tsx`), and this folder's own `ReviewQueueRoute` component is no longer mounted by
any route. It is **not deleted** -- `routes/review/**` is outside task E13/F09/US01/T01's own "Files
to create or modify" table -- so the code (and its own passing unit tests,
`tests/routes/review/*.test.tsx`) still exists and still compiles, just unreachable from the app. A
future cleanup task should remove it. For the record, its old behaviour: it called
`GET /api/contracts` and kept rows whose status contains "review" (the same signal Portfolio's
attention strip uses), plus this-session uploads still in `NeedsReview` that were not already on that
page; rows with a `contractId` opened the detail screen, unlinked uploads stayed visible with "Not
yet linked to a contract".

### Members & roles (ADR-020 screen 2, task E06/F04/US01/T01)

`src/routes/workspace/members/` is the `/workspace/members` screen: AC-1 Member/Role/Status/Last
active table, AC-2 invite pane (email, Admin vs Procurement radios with permission summaries, Send).
AC-3 (non-admin) stays on `RequireRole` around this route. Invite is a real
`POST /api/workspaces/{tenantId}/invites`. There is still no list-members GET, so the table seeds the
current Admin locally and appends each successful invite in `sessionStorage` -- discovery gap, not
fabricated members. Client-side validation rejects a different email domain than the signed-in Admin
(screens.md #2 "non-tenant domain").

## API client (ADR-012 "one generated TypeScript client, no hand-written divergent DTOs")

Task E01/F07/US01/T02 ("Generate TS API client from OpenAPI; wire /health"):

- `openapi/contigo-api.v1.json` is the single OpenAPI document this client is
  generated from (AC-3). It documents exactly the routes
  `backend/src/Contigo.Api/Program.cs` implements today -- `GET /health` and
  (task E06/F03/US01/T01) `POST /api/workspaces` -- cross-checked against
  `backend/tests/Contigo.Api.Tests` and `backend/src/Contigo.Api
  /WorkspaceEndpointExtensions.cs` respectively.
  **Interim provenance**: the API host does not yet self-publish this document
  (no `Microsoft.AspNetCore.OpenApi`/Swashbuckle/NSwag wired into
  `Program.cs`, and adding that is backend work outside this task's
  `target_repo: contigo-web` scope). This file must grow endpoint-by-endpoint
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
  to backend epics E02-E05: `openapi/contigo-api.v1.json` gained
  `POST /api/workspaces` (create), `POST /api/workspaces/{tenantId}/invites`
  (invite), `POST /api/documents` (upload), and `GET /api/documents/{id}`
  (read back) -- exactly epic-06-web-foundation's own R0 surface (sign-in ->
  workspace picker, members & roles, document upload/status). `Program.cs`
  now also serves many more routes from those same backend epics (portfolio,
  Contract 360 + correction history, audit, renewals, savings (+ KPIs), Ask
  Contigo chat, quotes, negotiation outcomes) that this task deliberately did
  **not** add to the contract: epic-06-web-foundation's own "Out of scope"
  list names exactly that set as "later web epics," and
  feature-01-typescript-client-regen is a documented **repeating chore** --
  whichever web epic first builds a screen against one of those endpoints
  extends `openapi/contigo-api.v1.json` next, the same way this task extended
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
  `openapi/contigo-api.v1.json` with `GET /api/contracts` (operationId
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
  full filter/paging surface even though `src/routes/contracts/index.tsx`
  itself only ever calls it unfiltered (see "Portfolio" above for why).
- **Task E07/F02/US01/T01 (contract-360)** extended `openapi/contigo-api.v1.json` with three more
  operations -- `GET /api/contracts/{id}` (`getContract360`), `GET /api/renewals` (`getRenewals`),
  and `GET /api/renewals/{contractId}/priority` (`getRenewalPriority`) -- the same "repeating chore"
  `typescript-client-regen`'s own doc comment named. `header.risk` and `tabs.clauses[].riskLevel`
  reuse the same bare-nullable-string workaround `getPortfolio`'s own `risk` field already
  established (the generator drops `null` when `enum` and a nullable `type` union combine);
  `tabs.risks[].severity` is a real non-nullable enum since the wire field is required. `client.ts`'s
  three new methods follow the same never-throws convention as every other call (a `404` on
  `getContract360`/`getRenewalPriority` is a normal, expected outcome the caller renders as a named
  "not found" state).
- **Task E07/F03/US01/T01 (field-review-correction)** extended `openapi/contigo-api.v1.json` with `PATCH
  /api/contracts/{id}` (`correctContract`) and `GET /api/contracts/{id}/corrections`
  (`getCorrectionHistory`) -- the third web epic to extend this document (same "repeating chore"
  provenance paragraph). Both operations were already implemented by backend task
  E02/F05/US01/T01/T02; this task only wraps them for the web client. `correctContract`'s request
  body (`CorrectContractRequest`) is hand-written, like `createWorkspace`'s -- the generator does not
  parse `requestBody`. Both response shapes (a flat object, and an array of a flat object) use
  generator cases `getContract360`/`getPortfolio` already exercise, so no generator change was
  needed this time.
- **Task E08/F01/US01/T01 (renewal-pipeline, ADR-020 screen 8)** extended `openapi/contigo-api.v1.json`
  with `POST /api/renewals/{id}/action` (`postRenewalAction`) -- the fourth web epic to extend this
  document (see "API client" provenance paragraphs above). Already implemented by backend task
  E03/F03/US01/T02; this task is the first web caller (the insight card's three actions). The 200
  response's `status` field is a real, closed three-value enum (`NotStarted`/`InProgress`/`Completed`)
  declared with `enum`, the same way `GET /api/renewals`'s own `status` field already is, so
  `RenewalActionStatusValue` is derived from the generated response type rather than hand-duplicated.
  Unlike every other write operation in this client, a well-formed request against an unknown or
  cross-tenant contract id still succeeds (`Contigo.Renewals` cannot reference
  `Contigo.Documents.Contracts` at all, ADR-002) -- `postRenewalAction` never special-cases a 404 the
  way `getContract360`/`getRenewalPriority`/`correctContract` do for theirs.
- **Task E08/F02/US01/T01 (savings-home, ADR-020 screen 9)** extended `openapi/contigo-api.v1.json`
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

- **Task E13/F09/US01/T03 (web-documents-v2, ADR-024 V2 rebuild)** extended `openapi/contigo-api.v1.json`
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

- **Task E13/F09/US01/T04 (web-ask-v2, ADR-024 §6)** extended `openapi/contigo-api.v1.json` with
  `GET/POST /api/conversations`, `GET /api/conversations/{id}`,
  `POST /api/conversations/{id}/messages` (the reply contract), `GET /api/capabilities`,
  `GET /api/market/records/{id}`, and `supplierName` on the portfolio/360/renewals/documents
  responses -- the seventh web epic to extend this document (see "API client" provenance paragraphs
  above), and the first to add a header every method in this file now sends when supplied:
  `X-User-Id` (`userIdHeaders`, OQ-askv2-005/ADR-022), resolved lazily via a `getUserId` callback
  `main.tsx` supplies once MSAL resolves an account, mirroring `X-Tenant-Id`'s own
  resolved-per-request shape -- an absent/blank id omits the header key entirely (never a blank
  `X-User-Id: ""`), so every pre-existing call site/test keeps its exact `toEqual` headers check
  passing unchanged. `getCapabilities`/`getMarketRecord` are the first genuinely tenant-agnostic
  reads in this file (no `X-Tenant-Id` at all -- the catalog and the market index are both
  static/shared, not per-tenant); `getCapabilities` also has no documented non-2xx body
  (`CapabilitiesEndpointExtensions` has no failure branch), so its own error path is a status-based
  message only, never an attempted JSON parse, unlike every write/tenant-scoped read above it.

## Directory layout

```
web/
  playwright.config.ts      # task E08/F04/US01/T01 -- e2e/day1.spec.ts's runner config (see "End-to-end" below)
  e2e/
    day1.spec.ts             # task E08/F04/US01/T01 -- the §20 Day-1 browser walk, the web-pass integration gate
  openapi/
    contigo-api.v1.json       # single OpenAPI document (interim, hand-authored -- see "API client" above)
  scripts/
    generate-api-client.mjs   # openapi/contigo-api.v1.json -> src/api/generated/schema.ts (npm run generate:api)
  public/
    config.json               # runtime config contract; dev-only placeholder values
    staticwebapp.config.json  # Azure SWA: SPA fallback routing
  src/
    api/
      generated/schema.ts     # AUTO-GENERATED; do not edit by hand
      client.ts                # createApiClient(baseUrl, getUserId?) -> { getHealth(), createWorkspace({ name }), uploadDocument(tenantId, file), getDocument(tenantId, id), listDocuments(tenantId, query?), getDocumentPreviewUrl(tenantId, id), reprocessDocument(tenantId, id), deleteDocument(tenantId, id), getPortfolio(tenantId, query?), getContract360(tenantId, id), getRenewals(tenantId), getRenewalPriority(tenantId, contractId), getCorrectionHistory(tenantId, id), correctContract(tenantId, id, request), postRenewalAction(tenantId, contractId, request), askContigo(tenantId, request), uploadQuote(tenantId, file, fields?), getQuoteAssessment(tenantId, id), recalculateQuoteAssessment(tenantId, id, mappings?), captureNegotiationOutcome(tenantId, request), getSavingsKpis(tenantId), getSavingsOpportunities(tenantId), listConversations(tenantId), createConversation(tenantId, request?), getConversation(tenantId, id), postMessage(tenantId, conversationId, request), getCapabilities(), getMarketRecord(id) } -- every method also sends X-User-Id when getUserId is supplied (task E13/F09/US01/T04, OQ-askv2-005)
    config/appConfig.ts       # fetch + validate runtime config
    auth/msalConfig.ts        # AppConfig -> MSAL Configuration (no secret, ever)
    styles/                   # design system (tokens + component catalogue); see below
    routes/
      signin/               # ADR-018 `/signin`; gated on MSAL auth state, not a URL route (see "Screens" above)
        index.tsx             # SignInRoute -- no account: SignInScreen; signed in: WorkspacePickerScreen
        SignInScreen.tsx      # idle / redirecting states around instance.loginRedirect(); also exports SignInStatementPanel (shared left-column canvas, task E06/F06/US01/T01)
        WorkspacePickerScreen.tsx # list (workspaceStore cache) + create via POST /api/workspaces + "Continue" into the shell -- renders SignInStatementPanel + .signin-action, the same full-bleed canvas as SignInScreen (task E06/F06/US01/T01), not a standalone card
        workspaceStore.ts     # per-account localStorage cache + sessionStorage "current workspace"; documents the missing list/membership backend gap
        signin.css            # this route's styles -- see "Layout" above
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
        documentStore.ts      # DEPRECATED for this route (V2 reads GET /api/documents instead) -- kept only because RailNav.tsx's badge still reads it; see "Documents" above
        documents.css         # this route's styles (V2: stacked single-column layout, no more the V1 two-column grid)
      contracts/            # task E07/F01/US01/T01 -- ADR-020 screen 4 (see "Portfolio" above)
        index.tsx             # PortfolioRoute -- fetch-once-filter-client-side state machine (AC-4 states)
        AttentionStrip.tsx    # AC-2: the four-cell strip, click = filter
        PortfolioFilters.tsx  # AC-1: the seven filter chips
        PortfolioTable.tsx    # AC-3: the sorted, tinted table
        portfolioAttention.ts       # pure helpers: per-row severity/issue text, sort comparator, attention buckets
        portfolioFilterState.ts     # pure helpers: filter state + the AND-composed row predicate
        portfolioTableFormatters.ts # pure helpers: date/number formatting, status/risk -> tag mapping
        contracts.css         # this route's styles
        contract360/          # task E07/F02/US01/T01 -- ADR-020 screen 5 (see "Contract 360" above)
          index.tsx              # Contract360Route -- fetch-order state machine (contract, then renewals + priority together)
          Contract360Header.tsx  # AC-1/AC-2: header + 6-cell fact row
          OverviewTab.tsx        # AC-3: recommendation card (.ai-recommendation) + drivers + attention + top risks
          FactTable.tsx          # AC-4: the shared Term/Value/Source/Confidence table (Commercials..Activity)
          RenewalTab.tsx         # AC-4: Renewal's own facts + the priority-score component table
          contract360ViewModel.ts # pure helpers: row builders per tab, needs-attention/top-risks derivation, recommendation lookup
          contract360.css        # this screen's styles
        review/                # task E07/F03/US01/T01 -- ADR-020 screen 6 (see "Review / correction" above)
          index.tsx              # ReviewRoute -- fetch order (contract, then correction history), decision state, correction submit
          ReviewHeader.tsx        # AC-4: title/summary + gated "Mark as validated" + progress line + confidence legend
          ReviewFieldList.tsx     # AC-1: the 4-column field list (critical marker, value, confidence tag, decision)
          EvidencePane.tsx        # AC-3: evidence + correction form + real correction-history trail
          reviewViewModel.ts      # pure helpers: correctable-field catalogue, decision/tag/gate computation (no live confidence yet -- see its own header comment)
          review.css              # this screen's styles
      ask/                  # task E07/F04/US01/T01 -- ADR-020 screen 7; V2 rebuild task E13/F09/US01/T04 (see "Ask Contigo" above)
        index.tsx              # AskRoute -- off/new-chat/conversation/resume states; seeds+asks the global Ask bar's router-state query once; create-then-ask, citation-click routing
        AskOffState.tsx        # off state (0 validated contracts): fixed headline, doc-count-dependent reason + one CTA to /documents
        MarketRecordPanel.tsx  # side panel for a market citation: GET /api/market/records/{id}
        useConversation.ts     # resume: GET /api/conversations/{id} -> turns, oldest first
        useRecentConversations.ts # rail's last-5 list: GET /api/conversations, refetches on navigation (see "App shell" above)
        reply/                  # phase-2 (task E13/F09/US01/T02) rich reply renderer -- ReplyBody/CitationCard/ActionRow/ReplyMarkdown/replyTypes; this task maps the wire reply onto it (askViewModel.ts) but does not modify it
        askViewModel.ts        # pure(ish) helpers: wire reply -> presentational Reply, turn construction, off-copy/scope-line/suggestion-chip text, citation-click resolution
        ask.css                # this screen's styles
      renewals/                 # task E08/F01/US01/T01 -- ADR-020 screen 8 (see "Renewal pipeline" above)
        index.tsx                 # RenewalsRoute -- fetch-then-score-batch state machine (AC-4 states), selection, action handling
        ThresholdStrip.tsx        # AC-1: the seven-bucket strip, click = filter
        RenewalTable.tsx          # AC-2: the priority table, native button per row to select for the insight card
        InsightCard.tsx           # AC-3: facts + recommendation + three actions + confirmation (Contract 360 + Home links)
        renewalPipelineViewModel.ts # pure helpers: threshold buckets, score/status/contract-ref formatting, the three action plans
        renewalActionStore.ts     # sessionStorage-scoped mirror of this session's own renewal actions -- no GET read-back endpoint exists yet, and the honest stand-in for "opportunity visible on Home"
        renewals.css              # this screen's styles
      quotes/                 # task E08/F03/US01/T01 -- ADR-020 screen 10 (see "Quote check" above)
        index.tsx               # QuoteCheckRoute
        UploadQuoteForm.tsx      # upload entry point + sample-file convenience
        QuoteStepper.tsx         # AC-1: the 4-step header
        ExtractStep.tsx          # AC-2: line table + unmatched-SKU mapping
        AssessmentStep.tsx       # AC-3: numbers + P25/P50/P75 + provenance
        TargetStep.tsx           # AC-3: price ladder + editable target
        NegotiationStep.tsx      # AC-4: outcome capture
        quoteCheckViewModel.ts   # pure helpers
        quoteOutcomeStore.ts     # sessionStorage-scoped outcomes
        quotes.css               # this screen's styles
      savings/                 # task E08/F02/US01/T01 -- ADR-020 screen 9; moved from routes/home/ to /savings by E13/F09/US01/T01 (see "Savings" above)
        index.tsx                # SavingsRoute -- two independent fetches (KPIs, opportunities), two independent degrade states
        KpiRow.tsx               # AC-1: the six KPI cells + AC-3's stale-labelled notice
        OpportunitiesTable.tsx   # AC-2: the eight-column table, cell-level links (AC-3 navigation)
        savingsViewModel.ts      # pure helpers: reduceKpiFetch (AC-3), buildKpiCells (AC-1), buildOpportunityRows (AC-2, council "opportunity visible on Savings")
        savings.css              # this screen's styles
    components/
      shell/                  # task E06/F03/US02/T01 -- app shell, router, role guard; V2 two-tier rail by E13/F09/US01/T01 (see "App shell" above)
        navItems.ts             # V2 two-tier model: buildPrimaryNavItems/buildSecondaryNavItems, badge builders, canManageMembers(role) role guard (AC-1/AC-2)
        useValidatedContractCount.ts # kbReady / validated-contract count, fetched once via apiClient.getPortfolio
        workspaceRole.ts        # interim client-side role resolution (?role= override; see "App shell" above)
        RailNav.tsx              # 224px left rail, two tiers + footer
        RequireRole.tsx          # route-level guard; ADR-018 "request access" state
        ScaffoldScreen.tsx       # generic placeholder for routes later epics build for real
        AppShell.tsx             # rail + global Ask bar + <Outlet/>; fetches kbReady once via useValidatedContractCount
        WorkspaceShellApp.tsx    # <BrowserRouter> + V2 route table (ShellRoutes is the router-free export tests use)
        shell.css                # rail/shell layout
      ask-bar/                # task E06/F03/US02/T01 -- global Ask bar scaffold (AC-3); V2 new-chat state + off placeholder by E13/F09/US01/T01; capability-sourced chips by E13/F09/US01/T04
        GlobalAskBar.tsx         # the bar itself: input, chips, Enter -> /ask with { query, newChat: true }, Cmd/Ctrl+K focus, fetches GET /api/capabilities once
        askSuggestions.ts        # getAskBarCopy: static per-route fallback copy + kbReady off-copy, overridden by the capability catalog's own exampleQuestions once it resolves for the current route
        ask-bar.css
    App.tsx                   # composition root: /health effect; SignInRoute, or (signed in + workspace picked) WorkspaceShellApp
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
citations + one abstain -> renewal action -> savings opportunity -> quote
check -> record outcome -> Home realized updates), never `dotnet test`,
never Swagger (AC-1), against `demo`, never a `localhost` `config.json`
shell (AC-2), which only makes sense once `demo-v*` promotion (ADR-016) has
actually happened (AC-3).

The task's own "Files to create or modify" table names this file as
`workspace/contigo-web/e2e/day1.spec.ts`; it lives at `web/e2e/day1.spec.ts`
instead, the same "product tree is the four domain folders at the worktree
root, not a `workspace/<repo>/` stand-in" correction `reports/open-questions.md`
already recorded for two earlier tasks (OQ-impl-001/002) -- there is no other
location where a browser test could reach the real, already-scaffolded ten
screens this file drives.

**Known regression, task E13/F09/US01/T01 (ADR-024 V2 shell, gap G-IA-V2;
out of that task's own file scope, `web/e2e/**` is not in its "Files to
create or modify" table):** this spec is the Day-1 (V1) IA's own walk and now
fails at the V2 shell -- `page.goto("/")` (`assertHomeOpportunity`) no longer
renders a "Home" screen (`/` redirects to `/ask`), so the
`getByRole("heading", { name: "Home", exact: true })` assertion in "Home
Savings Realized -- link back" (step 10) does not resolve, and every other
step's own implicit "the rail has a Home item" assumption no longer holds
either. `npm run test:e2e` is not part of this task's own proof (`npm test` /
`npm run build` only) and is not run by CI yet (see "CI wiring" below), so
this did not block the V2 shell landing -- but it does mean this suite itself
is red until the V2 replacement lands: ADR-024's own "Implications for the
decomposition" already names `web/e2e/v2.spec.ts` (gap G-INTEGRATION,
task F11/T01) as that replacement, not a fix to this V1 file.

### Running it

```bash
npx playwright install --with-deps chromium   # one-time browser download
CONTIGO_E2E_BASE_URL=https://<swa-demo-host> \
CONTIGO_E2E_ENTRA_EMAIL=<a demo-tenant test account UPN> \
CONTIGO_E2E_ENTRA_PASSWORD=<that account's password> \
  npm run test:e2e
npm run test:e2e:report   # opens the HTML report -- trace/video/screenshot on failure, "smoking recorded"
```

All three environment variables are required; the suite `test.skip()`s
itself (not a false pass, not a silent no-op exit code) with a message
naming exactly what is missing when any are unset -- see the spec file's own
header comment. There is deliberately no `localhost` fallback for
`CONTIGO_E2E_BASE_URL`: that would let a local run masquerade as the `demo`
gate AC-2 requires.

**The test account must be exempt from interactive MFA / Conditional
Access** (a standard "automation/service" account exclusion on the Entra
side) -- `e2e/day1.spec.ts#signInWithEntra` drives the identifier + password
steps and an optional "Stay signed in?" prompt only; it cannot answer an
MFA challenge. This is an Entra tenant configuration decision for whoever
provisions the `demo`-tenant test account, not something this file's own
scope (`web/`) can set.

### Three real, honestly-tested divergences from the prototype

`day1-demo.html` is one hard-coded demo scenario; the real app is not. The
spec's own header comment has the full citations -- in short:

1. **Members list has no GET.** The invite screen is real (`POST /api/workspaces/{tenantId}/invites`)
   but there is still no list-members endpoint, so the table is this-browser's Admin row plus
   invites sent from this session (`memberStore.ts`) -- not a fabricated roster, and not a
   workspace-wide directory.
2. **A fresh, self-created workspace cannot discover the ADR-022
   fixture-seeded tenant** (the workspace picker is a per-browser
   `localStorage` cache, `workspaceStore.ts`'s own documented gap) --
   Ask-citation, renewal-pipeline and savings-opportunity steps assert
   whichever real, already-tested state (populated or honestly empty)
   actually renders for the workspace this run creates, and name the gap
   inline via `test.info().annotations` rather than asserting a fabricated
   populated state. A reused Playwright `storageState` pointed at a
   pre-seeded fixture workspace exercises the fuller, populated path
   instead -- the same `pickOrCreateWorkspace` code path handles both.
3. **A recorded quote outcome does not update Home's "Savings realized"
   KPI** (`NegotiationOutcomePropagationService` never runs for it -- see
   `src/routes/quotes/NegotiationStep.tsx`'s own header comment). The final
   step asserts the real outcome + the real "See it on Home ->" link, not a
   KPI change this build does not perform.

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
pass, not a hang -- when the three `CONTIGO_E2E_BASE_URL` /
`CONTIGO_E2E_ENTRA_EMAIL` / `CONTIGO_E2E_ENTRA_PASSWORD` env vars are
unset, exactly as designed. What genuinely is operator/CI-only is a real
pass with real Entra test-account credentials against a live,
`demo-v*`-promoted `demo` deployment -- that, and only that, is the actual
gate this suite exists to satisfy; no local or CI session without those
live credentials can supply it.
