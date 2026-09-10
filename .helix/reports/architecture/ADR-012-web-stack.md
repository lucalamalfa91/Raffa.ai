# ADR-012 — Web client stack and hosting

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: client-architect (draft), council-close
- **Locked citations**: Frontend/mobile "Council decides the stacks" (brief §1); API "API-first. Web and mobile consume the backend API." (brief §1); "No secrets in code, client bundles" (brief §1); "Hosting choice for the web client: council, under the cost guideline." (brief §9)

## Context and problem statement

Raffa V1 is **web-first** (brief §9). The web client must deliver the full user-visible ladder — auth, workspace/roles, document upload, portfolio, Contract 360 with evidence + confidence, review/correction, Ask Raffa with citations, then renewals, savings, and quote check as later slices land (spec §16; brief §3, §9). It is a pure API consumer: every byte of business data arrives via the ASP.NET Core backend API (brief §1 API-first). The stack is council-owned and must stay within the cost guideline while giving a small team (Claude Code via Helix) fast iteration and a single deployable `web/` folder in the monorepo.

## Decision drivers

- **Cost** — hosting must be cheap/free-tierable and scale-to-zero friendly; no idle-expensive node runtime.
- **API-first, no secrets** — an OIDC/SSO SPA (Entra ID) that holds only a client id and calls the API, never a client secret; refresh/session handled by the auth provider or a BFF.
- **One repo, one deployable** — a static `web/` folder build product (HTML/JS/css) that any cheap static host can serve, with an OIDC auth flow that does not require the static host to proxy anything.
- **Coordination with backend** — TypeScript shares a natural API contract with ASP.NET Core JSON and OpenAPI; it also pairs with the mobile decision (ADR-mobile-stack) so the same language/tooling covers both surfaces.

## Considered options

1. **React + TypeScript + Vite SPA, served as a static bundle** — the team-standard web UI.
2. **Blazor (WASM or Server)** — same C# language as the backend.
3. **Angular + TypeScript** — batteries-included framework.

## Decision outcome

**Chosen: Option 1 — React + TypeScript + Vite, an OIDC SPA with the Authorization Code + PKCE flow against Entra ID, built to a static bundle and hosted on Azure Static Web Apps (free tier).**

React + Vite produces a fully static output, which is the cheapest thing Azure can host (Static Web Apps free tier, scale-to-zero friendly, TLS and global CDN included) and it keeps `web/` a single buildable folder in the monorepo with no server process to pay for or secure. TypeScript is the lowest-friction language for a small AI-assisted team consuming a JSON/OpenAPI backend, and it aligns the web and mobile decisions onto one language family. The OIDC Authorization Code + PKCE flow means no client secret in the bundle (satisfying the "no secrets in client bundles" lock), while the backend remains the single source of authorization.

### Consequences

- **Good**: free-tier hosting; no runtime to operate; fast iteration; shared TypeScript with the RN mobile client (ADR-mobile-stack); secrets stay out of the bundle via PKCE (no client secret).
- **Bad**: static hosting means any server-side-only need would require a separate function/API; build output must be deployed through the council's CI/CD path-filters (`web/`), which is one more job lane.
- **Neutral**: TypeScript is a second language alongside the C# backend (a real but accepted trade-off for a web-native UI).

## Pros and cons of the options

### React + TypeScript + Vite (SPA)
- Good: free static hosting; mature OIDC/PKCE library support (e.g. MSAL); huge ecosystem; shares language with mobile RN.
- Bad: separate toolchain/language from .NET backend; needs OpenAPI/BFF contract discipline to avoid drift.

### Blazor (WASM or Server)
- Good: one language (C#) across UI and API.
- Bad: Blazor WASM is heavier to run/load and Blazor Server needs a persistent circuit + SignalR hosting (idle cost, not static); smaller ecosystem for the Procurement UX; poorer fit for a pure static/cheap host.

### Angular + TypeScript
- Good: batteries included (forms, DI, router).
- Bad: heavier framework and learning curve than this slice needs; no cost advantage over React; less aligned with a lean RN-sharing mobile story.

## Implications for the decomposition

- A change to the API surface must regenerate/update a shared OpenAPI/TypeScript client so `web/` and `mobile/` consume one versioned contract — no hand-written divergent DTOs.
- CI/CD must add a `web/` path-filtered job that runs `npm ci`, `npm run build`, and deploys the static output to the per-environment Static Web App.
- The OIDC client registration (Entra ID) exposes only a public-client `client_id` + redirect URI; no secret is ever written into `web/` source or the static bundle.
- `web/` must read the API base URL and OIDC authority from per-environment config (runtime injection), not hard-coded, so the same bundle deploys to `dev` and `demo`.

## Assumptions

- Azure Static Web Apps free tier is available and sufficient in the chosen region (to be confirmed with cloud-architect at council-close).
- Entra ID is available for the `dev`/`demo` tenants and supports the Authorization Code + PKCE public-client flow (confirmed with security-architect).
- A BFF/API-proxy is not required for V1; the SPA calls the API origin directly with CORS scoped to the registered front-end origins.

## Amendment (2026-09-10, wave w14 — provenance: a client store never stands in for a missing GET)

Serves **NW-01, NW-03, NW-04, NW-09, NW-14, NW-58**. The `## Decision
outcome` above is unchanged (React + TypeScript + Vite, OIDC PKCE, static
bundle on Static Web Apps) and `Status:` stays `accepted`. This is ADR-012's
first amendment footer. It adds the **provenance** half of the discipline
`:53` already states for *shape* ("no hand-written divergent DTOs"): this
wave deletes or demotes four client stores that exist only because an
endpoint did not, and each one says so in its own header comment. Contract
and data model are ADR-026; authorization, token strength and audit are
ADR-025 — **where this footer and ADR-025 touch one mechanism, ADR-025
governs**.

**1. A client store never stands in for a missing GET.** The four stores and
their disposition:

| Store | Storage | Stood in for | w14 |
|---|---|---|---|
| `workspaceStore.ts:32,88-106` `knownWorkspaces.<homeAccountId>` | `localStorage` | `GET /api/workspaces` | **deleted** (NW-01) |
| `workspaceStore.ts:33,117-145` `currentWorkspace` | `sessionStorage` | "which tenant am I in" | **demoted to a hint** (NW-03) |
| `memberStore.ts:25,54-86` `members.<tenantId>` | `sessionStorage` | `GET …/members` | **deleted** (NW-04) |
| `workspaceRole.ts:30,36-47` `workspaceRole` + `?role=` | `sessionStorage` | "what is my role" | **deleted** (NW-14) |

When the server read fails the screen shows ADR-018 `:117`'s error state
(2px accent left rule + h4 + plain endpoint name + secondary Retry) — never a
stale local answer. **There is no "keep the old list as a fallback" task**: a
fallback would reproduce the exact defect each item reports, a client that
disagrees with Postgres and is confident about it. Two invented facts die with
the stores and are **not** back-filled client-side: `lastActiveAt` ("now",
always, `memberStore.ts:62-68`) and the hardcoded self-row `role: "Admin"`
(`:65`). If the server has no last-active column the column is dropped from
the table.

**2. The one surviving key is a hint, never a source of truth.**
`contigo.signin.currentWorkspace` keeps its name and its three functions, but
nothing trusts it until it is checked against the server list. Resolution
order on every mount, once `GET /api/workspaces` has resolved: list empty →
create form; exactly one row → enter it (`percorso-pilota-v1.md` §2 step 1);
≥2 and hint ∈ list → enter the hint; ≥2 and hint ∉ list or absent → picker.
**`hint ∉ list` ⇒ discard the hint** — that is the client half of NW-58's
removal requirement and it needs no endpoint, no polling and no cache
invalidation: revalidation *is* the mechanism (ADR-025 Rule F.1d is the server
half of the same rule).

**3. The generated client is generated, and the generator has four hard
constraints** (`web/scripts/generate-api-client.mjs` — facts, not
preferences, binding on every OpenAPI entry this wave adds): `operationId` is
mandatory or generation throws (`:125-130`); a row schema must be **flat
scalars** — the `object` case renders a flat property map and a nested object
renders `unknown`, with no `$ref`/`oneOf` support (`:97-119`); enums become
literal unions (`:63-65`), which is what keeps `RequireRole` and
`canManageMembers` compiling against a real type; and **only `responses` are
parsed** (`:132-146`), so request bodies, path parameters and headers stay
hand-written in `client.ts` (`:17-21`). Because `schema.ts` is regenerated
wholesale on every build (`:21-23`), two tasks editing
`web/openapi/contigo-api.v1.json` in one phase produce a conflicting
regenerated artefact rather than a mergeable diff: **one task owns the
contract file per phase.**

**4. What the SPA sends is fixed, and it is not the tenant twice.** The client
sends `X-Tenant-Id` and `X-User-Id` and nothing else — verified: `Grep` for
`X-Role|X-Workspace-Role` over `web/src` returns **zero** matches. **The SPA
declares no role to the API, ever**, in this wave or any other (NW-14's own
guard). Headers are attached per method in `client.ts`, never by a global
wrapper, and methods already omit `X-Tenant-Id` deliberately (`:1139`,
`:1147`). Therefore **a route that carries `{tenantId}` gets no
`X-Tenant-Id`** — the members and invites routes take the tenant from the
route, the convention `client.ts:918` already states for the sibling invite
operation and which the ADR-022 w14 footer restates as a rule ("`X-Tenant-Id`
is not an input to a membership route"). Sending both would let one caller
present two candidate tenants for one operation, which is the ambiguity the
header was demoted for.

**5. The client's role is presentation-only, server-derived, and defaults to
least privilege.** `resolveWorkspaceRole()` is deleted with its `?role=`
override and its `?? "admin"` default — that default *is* NW-14's defect.
`parseWorkspaceRole(wire)` replaces it **in `workspaceRole.ts`**, and any
wire value the nav does not model maps to the **least-privileged** modelled
role, never `admin`. The wire vocabulary is wider than the nav's
(`memberViewModel.ts:9-11` records Legal / Finance / ReadOnly against
`navItems.ts:29`'s two, and NW-54 is deferred), so a default of `admin` would
re-introduce the identical bug for those roles while fixing it for
Procurement. The role decides which affordances render and **never what is
permitted**: the 403 is the authority, the button state is a courtesy.

**6. The invitation token is never persisted client-side.** ADR-025 Rules
C9/C10 own the rule; this is its realization, which is client-architecture.
The token arrives in the URL **fragment**, is copied into memory on mount,
and the fragment is cleared from the address bar; it is sent only in the
`X-Invitation-Token` header. It is **not** written to `sessionStorage` (this
seat's own lane draft proposed that and it is withdrawn), not to
`localStorage`, and **not into MSAL's `state` parameter** — `state`
round-trips through the identity provider, so it would put the token in an
Entra authorize URL's query string and hence in a third-party server's logs,
which is strictly worse than the store it replaces. Two consequences follow
and both are deliberate: MSAL is redirect-only here with one configured
`redirectUri` (`msalConfig.ts:19-33`; `loginRedirect` at
`routes/signin/index.tsx:34`), so a signed-out invitee who signs in by
redirect **returns without the token** — the guaranteed w14 flow is
*sign in, then open the invitation link again*, and `loginPopup` is the one
sanctioned way to make it single-click because the page is never unloaded and
memory survives. The popup-blocked and reload cases share one state, so it
ships regardless. And a reload or a sign-out **loses a valid token by
design** — the accept screen must therefore say "open your invitation link
again", never "this invitation is invalid": telling a user their live
invitation is dead because they pressed F5 sends them back to their Admin for
a replacement they do not need.

**7. A count has one definition and it is the server's.** Once
`contractCount` exists on the NW-01 row, **no screen computes a count from
the client-side status predicate**. `isValidatedContractStatus`
(`routes/contracts/contractStatus.ts:17`) stops being a definition and
survives only as a row-level display/filter helper, because
`portfolioViewModel.ts:38` still needs it for a screen no w14 item touches;
`useValidatedContractCount.ts:42` stops filtering and reads the server field.
The file is deleted by the item that gives the portfolio a server-side
filter, not by this wave — and until then the two must never both produce a
number.

**8. Scope.** Five new states enter screens that have none today (picker
loading, picker error+Retry, the app-gate "resolving" state, members loading,
members error+Retry); they are ADR-018 `:112-119`'s contract, not polish, and
the UX seat owns their copy. **Mobile is untouched** — ADR-013 remains a
non-gating scaffold and no w14 item reaches it.

## Amendment (2026-09-10, wave w14 — the auth library's store, and the screen the redirect actually lands on)

Serves **NW-58, NW-03, NW-01**. Everything above is unchanged and in force;
`Status:` stays `accepted`. This is the second w14 footer on this ADR and it
**supersedes nothing** in the first. It does three things: it adopts ADR-025's
new Rules C10a/T15 (which name this seat's files), it records that **C10b's
routing half is already an accepted decision** so no second task is invented
for it, and it closes one gap of this seat's own making that neither footer
reached.

**1. Rule C10a and test T15 are adopted without qualification.** ADR-025
`:791-795` requires `navigateToLoginRequestUrl: false` set **explicitly** in
`buildMsalConfig`, and `:807-815` adds T15 (no browser-store key holds the
token, at every step of a redirect round-trip). Both land in this seat's files
(`msalConfig.ts`, the accept route). Verified here independently:
`navigateToLoginRequestUrl` has **zero** occurrences in all of `web/src`, so
the library default governs today; `msalConfig.ts:31` is
`BrowserCacheLocation.SessionStorage`; `routes/signin/index.tsx:34` is the
**only** `loginRedirect` call site. One line of config, one e2e assertion, no
new task — they join the NW-03/NW-58 client tasks that already own those files.

**2. C10b's routing half is already accepted — do not schedule it twice.**
ADR-025 `:784-789` states that the accepted remedy "cannot run" because
`App.tsx:91` short-circuits before the accept route mounts. That is exactly
true of the code **as it stands today**, and it is *not* true of the accepted
w14 design: **ADR-018's w14 footer `:182-204` already hoists `BrowserRouter`
into `App.tsx`, with a public branch for `/invite/accept` and `*` falling
through to the account/workspace gate**, and states the route is "reachable
signed out and with no workspace, rendered outside `AppShell`". The ordering
C10b asks for is therefore already on disk, in the one ADR of this seat's that
the security seat correctly declined to read or edit. The seam is closed —
**but only if the decomposer reads both footers**, which is why it is recorded
here rather than left to inference. C10a remains necessary regardless: it is
what fails closed if the hoist is later refactored, which is precisely the
"both, not either" argument ADR-025 `:803-805` makes.

What C10b genuinely adds, and this ADR did not state, is the **intra-route**
ordering: on `/invite/accept` the fragment must be captured to memory and
cleared **before the sign-in control is interactive**, not merely "on mount".
The accept screen renders its own auth-initiating control (ADR-020 state 2,
"Continue with Microsoft Entra ID"), so mount-order and click-order are the
same screen's problem. Adopted.

**3. The gap neither footer reached: the redirect lands on a screen that says
nothing about the invitation.** `oidcRedirectUri` is a **single** configured
value per environment, used for both login and post-logout (`msalConfig.ts:24-25`;
`appConfig.ts:28,41,90` — one required key, no per-flow variant). So a
signed-out invitee who signs in from the accept screen **cannot** return to
`/invite/accept`; they land on the configured redirect URI, fall through `*` to
`App.tsx:91`, and — with an account but no picked workspace
(`workspaceStore.ts:117`, session-scoped, set only by picking at `:136-141`,
cleared on sign-out at `App.tsx:120`) — reach `SignInRoute:41-52` →
`WorkspacePickerScreen`. Under NW-01 that screen calls `GET /api/workspaces`,
and an invitee who has not yet accepted has **no membership**, so the list is
`[]` — which clause 2 above resolves to the **create-a-workspace form**.

The invitee is shown a form inviting them to create a workspace, with no
mention anywhere of the invitation they are holding. ADR-020's state 5 ("Open
your invitation link again", `:400`) is the correct copy and it is **not
reachable on this path**, because it is a state of a screen the user has just
left. It remains correct and reachable for reload and popup-blocked — those
paths stay on the accept route — so nothing in ADR-020 changes.

This is newly broken **by this wave**, not pre-existing: before NW-01 the list
came from a `localStorage` cache that could be non-empty; after NW-01 an
unaccepted invitee's list is empty by construction.

**Remedy — one sentence, on a screen NW-01 already rewrites.** The picker's
**empty-list state** carries a pointer back to the invitation ("Invited to a
workspace? Open your invitation link again."), beside the create form and not
instead of it. It needs no endpoint, no route, no stored token and no
knowledge of whether an invitation exists — it is unconditional copy, which is
what keeps it free of the tenant-data leak ADR-020 `:422-425` guards. **Copy is
the ux-ui-designer's**; the placement and the trigger are this seat's; the
change belongs to the NW-01 picker task that already owns that state. **No new
task, no new endpoint, no infrastructure** — cloud-architect's zero-delta
confirmation is untouched.

**4. `loginPopup` is new code and stays an optimisation.** Verified: `web/src`
contains **no** `loginPopup` call site (grep: zero). The first footer sanctions
it as the single-click variant because the page is never unloaded and memory
survives; that is still right, and it is the one path on which the invitee
never leaves the accept screen. But it is unwritten code with a
popup-blocker fallback, so **no `must` item may depend on it**: the guaranteed
w14 flow remains sign in, then open the invitation link again — and clause 3
above is what makes that flow discoverable instead of a dead end.

**5. What this footer does not do.** It does not edit `waves/w14.md`: this
seat's record entry is not wrong, only silent, and appending to the artifact
the gate read back would mean the approved record is not the one that was
checked (the product-owner's principle, which binds this seat too). It does not
touch ADR-025, ADR-018, ADR-020 or ADR-026 — the first is the security seat's,
the third the designer's, the fourth the software seat's, and ADR-018's clause
2 already says what is needed. The decomposer reaches this footer through the
NW-01, NW-03 and NW-58 rows, which already name ADR-012 as governing, and
through the INDEX note.
