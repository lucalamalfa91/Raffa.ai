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

## Screens (ADR-018 route map)

| Route | Screen(s) | Task |
|-------|-----------|------|
| `/signin` | Sign-in (Entra redirect, idle/redirecting states) -> workspace picker (list + create + confirm) | E06/F03/US01/T01 |
| `/documents` | Upload dropzone (drag-and-drop + "Choose from computer" + "Use sample file") + formats/size/sources strip -> 6-stage processing pipeline (current stage pulsing) -> result card by outcome (needs_review / completed / failed); below it, a document table (Document / Type / Supplier / Status / Uploaded, rows linking to Contract 360). Calls the real `POST /api/documents` and `GET /api/documents/{id}`. See "Documents" below. | E06/F05/US01/T01, E06/F05/US02/T01 |
| `/contracts` | Portfolio: filter chips + attention strip + a table sorted by severity then deadline (critical rows tinted + a red bar), plus loading/empty/error/no-match-for-filter states. Calls the real `GET /api/contracts`. See "Portfolio" below. | E07/F01/US01/T01 |
| `/contracts/:id` | Contract 360: header + 6-cell fact row + 10 tabs (Overview's recommendation card + drivers + "Needs your attention" + "Top risks", then Commercials/Products/Clauses/Obligations/Risks/Documents/Benchmark/Renewal/Activity through one shared Term/Value/Source/Confidence table), plus loading/not-found/error states. Calls the real `GET /api/contracts/{id}`, `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`. See "Contract 360" below. | E07/F02/US01/T01 |
| `/contracts/:id/review` | Review / correction: 4-column field list (critical marker, extracted value, confidence/decision tag, Accept/Correct) + right-hand evidence pane (correction form + real correction-history trail) + gated "Mark as validated". Calls the real `GET /api/contracts/{id}`, `GET /api/contracts/{id}/corrections`, `PATCH /api/contracts/{id}`. See "Review / correction" below. | E07/F03/US01/T01 |
| `/renewals` | Renewal pipeline: threshold strip (0-30 ... 270-365 d, click = filter) + priority table (Score/Supplier/Contract/Annual spend/Renews in/Cancel by/Status) + insight card (facts + recommended action + rationale) with three actions (Start negotiation / Assign to me / Snooze) -> confirmation + Contract 360 + Home links, plus loading/error/empty/no-window states. Calls the real `GET /api/renewals`, `GET /api/renewals/{contractId}/priority`, `POST /api/renewals/{id}/action`. See "Renewal pipeline" below. | E08/F01/US01/T01 |
| `/ask` | Ask Contigo: chat with a route line, numbered citation chips, abstain block. Calls the real `POST /api/chat/query`. See "Ask Contigo" below. | E07/F04/US01/T01 |
| `/quotes`, `/quotes/:id` | Quote check: this task's own upload form (no id yet) -> 4-step stepper Extract (line table + unmatched-SKU manual mapping + recalculate) -> Assessment (4 numbers, line-level P25/P50/P75 + confidence, provenance card; blocked until every line resolves) -> Target (price ladder, editable target/walk-away) -> Negotiation (outcome capture -> recorded outcome). Calls the real `POST /api/quotes`, `POST /api/quotes/{id}/assessment/recalculate`, `POST /api/negotiations/outcomes`. See "Quote check" below. | E08/F03/US01/T01 |
| `/` (home) | Home: 6 KPI cells (Annual spend analyzed · Savings identified · Savings realized · Savings in progress · Contracts analyzed · Upcoming renewals) + opportunities table (Opportunity · Type · Current spend · Estimated savings · Confidence · Owner · Status · Realized), rows opening Contract 360 › Benchmark or Quote check; a benchmark-provider-unreachable KPI refresh degrades to the last-known numbers, stale-labelled, rather than blocking the screen. Calls the real `GET /api/savings/kpis`, `GET /api/savings`. See "Home" below. | E08/F02/US01/T01 |
| `/review`\*, `/workspace/members` | App shell: 224px left rail + global Ask bar + routed content. Both routes in this row still render a `ScaffoldScreen` placeholder today: `/review`'s real "Review queue" list ships in epic-07/feature-03-review-correction-ui (distinct from the already-real `/contracts/:id/review` detail route above); `/workspace/members`'s real members table + invite ships in epic-06/feature-04-workspace-members-ui (see each route's own `note` in `src/components/shell/WorkspaceShellApp.tsx`). | E06/F03/US02/T01 |

\* `/review` is this task's own placeholder landing path, not a row in
ADR-018's locked route map -- that table only ever names the *detail* route
for this nav destination (`/contracts/:id/review`). See
`src/components/shell/navItems.ts`'s header comment. (`/quotes` used to be
the identical kind of placeholder until task E08/F03/US01/T01 made it a real
screen; `/` was the same until task E08/F02/US01/T01 made it a real screen
too.)

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
- **Documents** (`src/routes/documents/documents.css`) -- the two-column
  `minmax(280px, 400px) 1fr` mockup layout was already correct; filenames in
  the result card and the document table now wrap with `overflow-wrap:
  anywhere` (word/character-run boundaries) instead of `word-break:
  break-all`, so a long filename never renders one glyph per line.

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

### App shell, navigation, and the role guard (ADR-018, ADR-019, task E06/F03/US02/T01)

- **Rail** (`src/components/shell/RailNav.tsx`) -- the eight items and their
  order are locked verbatim from parent story us-02's AC-1 list / ia.md's
  "Navigation (left rail)" (`src/components/shell/navItems.ts`). No icon
  library is a dependency yet, so rail items are text-only (design-system.md
  calls for Lucide icons; adding that library is not this task's scope).
- **Role guard (AC-2)** -- "Workspace & members" is the one admin-only item.
  `navItems.ts#getVisibleNavItems` hides it from the rail for a Procurement
  role (unit-tested); `src/components/shell/RequireRole.tsx` is the same
  guard at the route level (defense in depth for a direct URL visit), which
  renders ADR-018's "request access" state instead of the real screen.
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
  `AppShell.tsx`). Enter submits the typed text and navigates to `/ask` with
  it in router state (`useLocation().state?.query` -- consumed by whichever
  future task builds the real Ask Contigo screen,
  epic-07/feature-04-ask-contigo-ui); Cmd/Ctrl+K focuses the input from
  anywhere. Suggestion-chip copy (`src/components/ask-bar/askSuggestions.ts`)
  is placeholder text keyed by route prefix, not real query intelligence --
  this bar is explicitly a scaffold that gets the user to `/ask`, it does not
  answer them.

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

### Documents -- upload + status read-back (ADR-020 screen 3, tasks E06/F05/US01/T01 + E06/F05/US02/T01)

`src/routes/documents/` implements both of screen 3's halves -- ADR-020's own
note that "screen 3 may be two: upload UI + document-status read-back":

**us-01, upload half** (task E06/F05/US01/T01):

- **AC-1, dropzone + strip** (`UploadDropzone.tsx`) -- drag-and-drop, native
  "Choose from computer" file picker (`accept=".pdf,.docx,.xlsx"`,
  product-spec.md §4.1), and "Use sample file". The formats/size/sources
  strip ("PDF · DOCX · XLSX", "50 MB / file", "Local · SharePoint soon") is
  quoted verbatim from the compiled prototype
  (`inputs/design/prototypes/day1-demo.html`); "SharePoint soon" matches
  product-spec.md's own P1/V1-vs-P2 integration roadmap, not decorative copy.
  Drag-and-drop is a progressive enhancement over the button, which stays the
  keyboard-/screen-reader-operable path (ADR-019 accessibility baseline).
- **AC-2, 6-stage pipeline** (`ProcessingPipeline.tsx` + `uploadPipeline.ts`)
  -- `POST /api/documents` runs the whole parse -> classify -> extract
  pipeline **synchronously** before responding
  (`backend/src/Contigo.Api/Program.cs`, task E02/F06/US01/T01), so there is
  no server-sent per-stage event. The 6 stage labels are quoted verbatim from
  the compiled prototype's own `pipeLabels` array and the list is a
  client-side pacing animation shown *while the one upload request is in
  flight* -- it holds on the last stage rather than looping if the request
  outlives it, and the request's actual resolution always wins.
- **AC-3, result card by outcome** (`UploadResultCard.tsx`) -- tag
  variant/label reuse `styles/semantics.ts#getStatusTag` (never re-derived);
  message copy is adapted from the compiled prototype's own `uplMap`, with
  one deliberate departure: the API's `uploadDocument`/`processingStatus`
  response carries no field-count/confidence or failure-reason detail (see
  `openapi/contigo-api.v1.json`), so messages name the uploaded file instead
  of the prototype's fabricated "41 fields extracted" figures, and the
  `failed` message suggests checking for password-protection/corruption
  rather than asserting it as a confirmed cause.
- **"Use sample file"** (`sampleDocument.ts`) builds a small, syntactically
  minimal PDF in the browser and uploads it through the exact same
  `apiClient.uploadDocument()` path a real file would use -- **this repo ships
  no real sample contract asset** (checked: no `*.pdf` anywhere in the repo).
  Whatever `processingStatus` the pipeline actually returns for that synthetic
  file is the honest answer, not a scripted one; a future task that adds a
  real fixture document can swap this module out without touching any other
  file in this folder.
- **One upload at a time**: extra files picked/dropped while another is
  uploading are queued (`index.tsx`'s own `queue` state) and start
  automatically the next time "Upload another" is clicked -- screens.md #3
  shows one pipeline / one result card at a time, never several at once.
- `tenantId` for the required `X-Tenant-Id` header is **not** threaded down
  as a prop -- `DocumentsRoute` reads `loadCurrentWorkspace()`
  (`src/routes/signin/workspaceStore.ts`) directly, exactly what that
  module's own doc comment names as the reason it keeps the current
  workspace id available. `apiClient` *is* threaded as a prop
  (`App.tsx` -> `WorkspaceShellApp` -> `DocumentsRoute`), the same
  generated-client instance every other screen shares.

**us-02, status read-back half** (task E06/F05/US02/T01) -- `DocumentStatusTable.tsx` + `documentTable.ts` + `documentStore.ts`, rendered below the upload UI on the same `/documents` route:

- **AC-1, document table** -- columns Document / Type / Supplier / Status /
  Uploaded, quoted verbatim from screens.md #3. There is no
  `GET /api/documents` collection endpoint on the backend
  (`backend/src/Contigo.Api/Program.cs` maps only `POST /api/documents` and
  `GET /api/documents/{id}`), so the table is a client-side,
  `sessionStorage`-scoped record (`documentStore.ts`) of documents *this
  browser* has uploaded this session -- the same kind of interim
  `workspaceStore.ts` already establishes for the workspace list, never
  fabricated data. Every terminal upload (`index.tsx`'s `startUpload`) adds a
  row, then "reads back" its `documentType` (absent from the `POST` response)
  via `GET /api/documents/{id}` -- the one backend operation whose own OpenAPI
  description is "Read back one document's metadata and processing status",
  this task's own name. A cell whose read-back has not resolved yet shows
  "Classifying…" (first-class loading state, not a blank cell); a mount-time
  effect retries any still-unresolved row once per page load.
  **Supplier is always "Not yet available"**: `Document`
  (`backend/.../Contigo.Documents.Contracts/Domain/Document.cs`) has no
  supplier column at all -- `Contract.SupplierId` exists but is an id-only
  cross-module reference (ADR-002 module map), and even the Portfolio list
  (`GET /api/contracts`) returns that raw id, never a resolved name. Rendered
  honestly rather than invented; revisit once a supplier-name-resolving
  endpoint exists.
- **AC-2, status tags** -- reuses `styles/semantics.ts#getStatusTag` via the
  same `uploadPipeline.ts#getUploadOutcome` mapping the result card already
  uses (`documentTable.ts#getDocumentStatusTag`), never re-derived.
- **AC-3, row cross-link** -- a real `<Link>` (react-router-dom, the same
  primitive `RailNav.tsx` already uses) to `/contracts/:contractId` when a row
  has one. `contractId` stays `null` when processing failed before
  classification could link a contract; that row renders plain text plus a
  visible "Not yet linked to a contract" reason instead of a dead link
  (ADR-019 accessibility baseline: "a visible reason, not a hidden control").

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

### Ask Contigo (ADR-020 screen 7, task E07/F04/US01/T01, us-01-ask-contigo)

`src/routes/ask/` implements screen 7: AC-1 chat + route line, AC-2 numbered citation chips opening
Contract 360 › Clauses, AC-3 abstain block, AC-4 empty/thinking/answered/abstain/unknown-fallback
states. Reached from the global Ask bar (`components/ask-bar/GlobalAskBar.tsx`, Enter or Cmd/Ctrl+K)
on any screen, or directly at `/ask`.

- **One call, one turn** (`index.tsx`) -- every question (typed, a "Try" suggestion click, or the
  seed query the global Ask bar carries in router state) calls the real `POST /api/chat/query`
  (`apiClient.askContigo`) exactly once and appends one "You" bubble plus one "Contigo" bubble.
  `askViewModel.ts#buildContigoMessage` decides whether that reply is an `answer` (AC-2), an
  `abstain` (AC-3), or an `error` (a transport/400 failure) -- an error is never rendered as an
  abstain: "the request failed" and "the AI honestly could not determine an answer" are different
  claims (`ChatMessageKind`'s own doc comment).
- **AC-1, route line** -- quoted verbatim from the task text: `"Structured query…"` for the
  backend's `Structured` intent, `"Clause retrieval…"` for `Semantic` (`askViewModel.ts
  #ROUTE_LINE_BY_INTENT`).
- **AC-3/AC-4, abstain and the unknown-question fallback share one block** -- the compiled
  prototype's own chat template (`inputs/design/prototypes/day1-demo.html`) renders both through the
  same "Cannot determine reliably." + reason markup (`.abstain-block`,
  `styles/components.css`), varying only the reason/route text; this screen follows the same rule
  (`askViewModel.ts`'s own header comment has the full reasoning). A `Structured`-intent question is
  not wired to live data by any task yet (`ChatEndpointExtensions`'s own doc comment), so it always
  comes back `canDetermine: false` with a real backend `message` explaining the gap -- shown
  verbatim as the reason. A `Semantic`-intent `canDetermine: false` always has `message: null` on the
  wire (`AbstainGuard`'s own free-text reason is deliberately excluded from the response), so this
  screen renders a fixed, honest "Contigo found no supporting evidence..." line instead of inventing
  a per-query reason the API does not supply.
- **AC-2, citations open Contract 360 › Clauses** -- only resolved on click
  (`askViewModel.ts#resolveCitationContractId`), not eagerly for every citation on every answer. The
  backend's `citations[].documentId` is a composite `SourceType:SourceId` string
  (`ChatEndpointExtensions.ToEvidenceSnippet`); a `Document:<id>` citation resolves via the existing
  `GET /api/documents/{id}` (its `contractId` field) and then navigates to `/contracts/:id` with
  `{ state: { tab: "Clauses" } }` -- `contracts/contract360/index.tsx` reads that exact shape
  (`contract360ViewModel.ts#isContract360TabName`) to open directly on the Clauses tab instead of
  always resetting to Overview. A `Clause:<id>` citation has no equivalent lookup endpoint anywhere
  in this backend yet (`Embedding.SourceType`'s own doc comment: "Document or Clause content today"
  is a loose pointer, not a foreign key) -- that chip shows an honest inline reason instead of a
  guessed link.
- **Citation label is the raw composite id, not a resolved filename** -- resolving every citation's
  real document name up front would need one extra round trip per citation before the message could
  even render; `openapi/contigo-api.v1.json`'s `askContigo` operation does not return a filename at
  all. A deliberate, documented scope cut, not an oversight.
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
    for the council decision carried into this story ("Action creates an opportunity visible on Home"),
    and the confirmation links to both **Home** (`/`) and **Contract 360** (`/contracts/:id`, AC-3's own
    named link) instead of overclaiming a backend entity that was not actually created. Home (task
    E08/F02/US01/T01, see "Home" below) has since landed and does exactly this:
    `homeViewModel.ts#buildOpportunityRows` imports `loadTrackedRenewalActions()` from this module and
    merges its rows -- most-recently-acted first -- ahead of the real, persisted opportunity list.
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
  Home (task E08/F02/US01/T01, see "Home" below) has since landed, but its own "Savings realized"
  KPI cell reads the real `GET /api/savings/kpis` response directly, not this store --
  `quoteOutcomeStore.ts` stays this screen's own unconsumed local record, the propagation gap named
  above being the real, pre-existing reason no code path connects the two yet. The recorded-outcome
  panel's own "See it on Home ->" link (`<Link to="/">`) is real, and now lands on a real screen.

### Home (ADR-020 screen 9, task E08/F02/US01/T01, us-01-savings-home)

`src/routes/home/` implements screen 9: AC-1 six KPI cells, AC-2 opportunities table, AC-3 row
navigation to Contract 360 › Benchmark or Quote check plus the benchmark-provider-unreachable
stale-labelled KPI state. Wired into `components/shell/WorkspaceShellApp.tsx`'s index route (`/`,
ADR-018 "/ (home)") in place of that task's `ScaffoldScreen` placeholder, the same seam
`../renewals/index.tsx` already used for `/renewals`.

- **Two independent fetches, two independent degrade states** (`index.tsx`) -- `GET
  /api/savings/kpis` backs AC-1's KPI row and `GET /api/savings` backs AC-2's opportunities table;
  each fetch's own failure degrades only its own section, the same "independently optional" shape
  `../contracts/contract360/index.tsx` already established for its own renewals+priority pair.
- **AC-1, KPI row** (`KpiRow.tsx`, `homeViewModel.ts#buildKpiCells`) -- the six cells, in AC-1's own
  order: Annual spend analyzed, Savings identified, Savings realized, Savings in progress, Contracts
  analyzed, Upcoming renewals, one formatted line per currency bucket (never summed across
  currencies -- the same "group by currency" discipline `SavingsRangeByCurrency`'s own backend doc
  comment states). Only "Savings realized" carries the accent-700 highlight, quoted from
  day1-demo.html's own `kpis[2].fg` rule. `kpis === null` (never fetched, or a first-load failure)
  renders an honest "-" per cell rather than a fabricated number (Appendix C rule 10).
- **AC-3, benchmark-provider-unreachable -> stale-labelled** (`homeViewModel.ts#reduceKpiFetch`,
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
- **Council decision "Action creates an opportunity visible on Home"** (`homeViewModel.ts
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
- **AC-3, row navigation** (`homeViewModel.ts#getOpportunityNavigation`) -- quoted from
  day1-demo.html's own row handler for this screen: a contract-linked opportunity opens Contract
  360's Benchmark tab (`/contracts/:id`, `state: { tab: "Benchmark" }` -- the same
  `location.state.tab` deep-link seam `../ask/index.tsx` already uses to open Clauses); one with no
  `contractId` (`SavingsOpportunityResult` carries no `quoteId` at all yet) falls back to the
  `/quotes` landing route, the same "no id yet" seam `../quotes/index.tsx` already establishes.
- **No workspace guard bypassed** -- like every other route, this screen reads
  `loadCurrentWorkspace()` directly rather than trusting `App.tsx`'s own earlier check, rendering a
  named "No workspace selected" state if that invariant is ever violated.

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
      client.ts                # createApiClient(baseUrl) -> { getHealth(), createWorkspace({ name }), uploadDocument(tenantId, file), getDocument(tenantId, id), getPortfolio(tenantId, query?), getContract360(tenantId, id), getRenewals(tenantId), getRenewalPriority(tenantId, contractId), getCorrectionHistory(tenantId, id), correctContract(tenantId, id, request), postRenewalAction(tenantId, contractId, request), askContigo(tenantId, request), uploadQuote(tenantId, file, fields?), getQuoteAssessment(tenantId, id), recalculateQuoteAssessment(tenantId, id, mappings?), captureNegotiationOutcome(tenantId, request), getSavingsKpis(tenantId), getSavingsOpportunities(tenantId) }
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
      documents/            # tasks E06/F05/US01/T01 + E06/F05/US02/T01 -- ADR-020 screen 3, both halves (see "Documents" above)
        index.tsx             # DocumentsRoute -- upload state machine (wires apiClient.uploadDocument) + trackedDocuments table state (wires apiClient.getDocument)
        UploadDropzone.tsx    # AC-1 (us-01): drag-and-drop + file picker + formats/size/sources strip
        ProcessingPipeline.tsx # AC-2 (us-01): 6-stage list, current stage pulsing
        UploadResultCard.tsx  # AC-3 (us-01): result card by outcome (needs_review / completed / failed)
        uploadPipeline.ts     # pure helpers: stage labels/view-model, outcome mapping, result-card copy
        sampleDocument.ts     # synthetic sample File for "Use sample file" -- no real fixture asset in this repo
        DocumentStatusTable.tsx # AC-1/AC-2/AC-3 (us-02): the document table, rows linking to Contract 360
        documentTable.ts      # pure helpers: type-label mapping, status->tag reuse, "Uploaded" date formatting
        documentStore.ts      # sessionStorage-scoped TrackedDocument list -- no GET /api/documents collection endpoint exists yet
        documents.css         # this route's styles
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
          ReviewHeader.tsx        # AC-4: title + gated "Mark as validated" + progress line
          ReviewFieldList.tsx     # AC-1: the 4-column field list (critical marker, value, confidence tag, decision)
          EvidencePane.tsx        # AC-3: evidence + correction form + real correction-history trail
          reviewViewModel.ts      # pure helpers: correctable-field catalogue, decision/tag/gate computation (no live confidence yet -- see its own header comment)
          review.css              # this screen's styles
      ask/                  # task E07/F04/US01/T01 -- ADR-020 screen 7 (see "Ask Contigo" above)
        index.tsx              # AskRoute -- one apiClient.askContigo() call per turn, seeds the global Ask bar's router-state query once
        ChatMessage.tsx        # one chat turn: text, abstain block, numbered citation chips, route line
        askViewModel.ts        # pure(ish) helpers: chat-turn construction, citation parsing, resolveCitationContractId (the one impure call)
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
      home/                    # task E08/F02/US01/T01 -- ADR-020 screen 9 (see "Home" above)
        index.tsx                # HomeRoute -- two independent fetches (KPIs, opportunities), two independent degrade states
        KpiRow.tsx               # AC-1: the six KPI cells + AC-3's stale-labelled notice
        OpportunitiesTable.tsx   # AC-2: the eight-column table, cell-level links (AC-3 navigation)
        homeViewModel.ts         # pure helpers: reduceKpiFetch (AC-3), buildKpiCells (AC-1), buildOpportunityRows (AC-2, council "opportunity visible on Home")
        home.css                 # this screen's styles
    components/
      shell/                  # task E06/F03/US02/T01 -- app shell, router, role guard (see "App shell" above)
        navItems.ts             # locked 8-item rail model + getVisibleNavItems(role) role guard (AC-1/AC-2)
        workspaceRole.ts        # interim client-side role resolution (?role= override; see "App shell" above)
        RailNav.tsx              # 224px left rail
        RequireRole.tsx          # route-level guard; ADR-018 "request access" state
        ScaffoldScreen.tsx       # generic placeholder for routes later epics build for real
        AppShell.tsx             # rail + global Ask bar + <Outlet/>
        WorkspaceShellApp.tsx    # <BrowserRouter> + route table (ShellRoutes is the router-free export tests use)
        shell.css                # rail/shell layout
      ask-bar/                # task E06/F03/US02/T01 -- global Ask bar scaffold (AC-3)
        GlobalAskBar.tsx         # the bar itself: input, chips, Enter -> /ask, Cmd/Ctrl+K focus
        askSuggestions.ts        # per-route placeholder copy (not real query intelligence)
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

1. **Invite has no real screen yet** (`workspace/members` still renders
   `ScaffoldScreen`; `epic-06/feature-04-workspace-members-ui` has not
   shipped as of this task) -- asserted as the honest placeholder it is.
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
