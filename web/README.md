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
- **Vitest + Testing Library** — unit tests under `tests/`, mirroring `src/`.

## Commands

```bash
npm ci                    # install (CI uses this)
npm run dev               # Vite dev server on :5173, reads public/config.json
npm run generate:api      # regenerate src/api/generated/schema.ts from openapi/contigo-api.v1.json
npm run build             # generate:api, then tsc --noEmit type-check, then vite build -> dist/
npm test                  # vitest run (single pass, CI mode)
npm run preview           # serve dist/ locally
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

No client-side router is wired in yet (no `react-router-dom` in
`package.json`) -- `src/routes/signin/` (`SignInRoute`, the folder's default
export) is gated on MSAL auth state (`useMsal().accounts`) instead of a URL
route, mounted directly by `src/App.tsx`. Introducing a real router and the
admin/procurement route guards it enables is E06/F03/US02/T01's
("navigation-shell") job; swapping this gate for a `<Route path="/signin">`
later does not change either screen underneath.

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

## Directory layout

```
web/
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
      client.ts                # createApiClient(baseUrl) -> { getHealth(), createWorkspace({ name }) }
    config/appConfig.ts       # fetch + validate runtime config
    auth/msalConfig.ts        # AppConfig -> MSAL Configuration (no secret, ever)
    styles/                   # design system (tokens + component catalogue); see below
    routes/
      signin/               # ADR-018 `/signin`; gated on MSAL auth state, not a URL route yet (see "Screens" above)
        index.tsx             # SignInRoute -- no account: SignInScreen; signed in: WorkspacePickerScreen
        SignInScreen.tsx      # idle / redirecting states around instance.loginRedirect()
        WorkspacePickerScreen.tsx # list (workspaceStore cache) + create via POST /api/workspaces
        workspaceStore.ts     # per-account localStorage cache + sessionStorage "current workspace"; documents the missing list/membership backend gap
        signin.css            # this route's styles
    App.tsx                   # composition root: /health proof-of-connectivity effect, mounts routes/signin
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
