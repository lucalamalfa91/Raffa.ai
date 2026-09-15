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
`raffa.signin.currentWorkspace` keeps its name and its three functions, but
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
`web/openapi/raffa-api.v1.json` in one phase produce a conflicting
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

## Amendment (2026-09-13, wave w15 — the identity a request carries, and the server facts a screen may not infer)

Serves **NW-61, NW-69, NW-67, NW-05, NW-06** and **NW-10**
(`reports/architecture/waves/w15.md`). The `## Decision outcome` above is
unchanged — React + TypeScript + Vite, OIDC Authorization Code + PKCE, a static
bundle on Static Web Apps — and `Status:` stays `accepted`. Both w14 footers
stay in force. This is ADR-012's **third** amendment footer; it supersedes
**one sentence** of the first (clause 1 below) and **corrects one factual
statement** in it (clause 9), and nothing else.

Ownership is unchanged: copy and states are ADR-020 (ux-ui-designer); endpoint
shapes are ADR-026 / ADR-027 (software-architect); token validation and the
authorization rule are ADR-010 / ADR-025 (security-architect) — **where this
footer and ADR-025 touch one mechanism, ADR-025 governs**. Every `web/` line
number below was read on baseline `3720543`; per W15-01 the wave base is
`origin/main` @ `3c89d35` and its five-file diff contains nothing under
`web/src`, so the citations hold on the base.

**1. One authorized-fetch choke point, and `X-User-Id` is deleted rather than
made conditional.** `client.ts:48` declares the identity accessor
`GetUserId = () => string | null` — **synchronous** — and `userIdHeaders`
(`:55-58`) is spread inline into **37 separate `await fetch(` call sites**
(verified: exactly 37), one per operation. Token acquisition is asynchronous,
so NW-05 cannot be a find-and-replace across 37 header literals: 37 literals
are 37 chances to omit one, and after NW-05 **an omitted header is a 401 with
no fallback**. `createApiClient`'s identity parameter becomes an **async token
accessor** and a single internal helper attaches `Authorization: Bearer` to
every request. `userIdHeaders`, the `X-User-Id` header, `GetUserId` and
`main.tsx:70-73`'s closure over `getAllAccounts()[0]?.username` are **deleted**
— ADR-010's w14 clause 3 requires the header to stop being *read*; this is the
client half, that it stops being *sent*.

This **supersedes one sentence of the first w14 footer's clause 4** — *"Headers
are attached per method in `client.ts`, never by a global wrapper"* — which
described a mechanism, not a rule. Clause 4's two actual **rules** are
unaffected and re-affirmed: **the SPA declares no role to the API, ever**
(`X-Role|X-Workspace-Role`: still **zero** occurrences in `web/src`), and **a
route carrying `{tenantId}` gets no `X-Tenant-Id`**. Per-call facts
(`X-Tenant-Id`, `X-Invitation-Token`) stay per-method; only identity moves to
the choke point.

Token acquisition is `acquireTokenSilent({ scopes: appConfig.oidcApiScopes,
account })` with `InteractionRequiredAuthError` → `acquireTokenPopup`. **Never
a redirect from inside a request**: a redirect unloads the page and destroys
in-flight state — and on `/invite/accept` the in-memory invitation token
(clause 6 of the first w14 footer; ADR-020 state 5). Popup blocked → ADR-018
`:117`'s error state with a sign-in affordance, never a silent retry loop. A
**401 after a successful silent acquisition is a server rejection, not a stale
token**: it is surfaced, not retried.

**No new SPA runtime-config key, and no contract edit.** `oidcApiScopes` is
already declared (`appConfig.ts:34`), validated (`:75-82`) and assigned
(`:91`), and `buildLoginRequest` (`msalConfig.ts:80-84`) **already requests the
API scopes** — the SPA acquires the right consent today and throws the token
away (verified: **zero** `acquireTokenSilent|Popup|Redirect` and **zero**
`Bearer` in all of `web/src`). So the web half of NW-05 needs **no Terraform,
no CI change and no OpenAPI edit** (the generator parses only `responses`, so
headers are hand-written by construction) — stated plainly so the decomposer
does not place the client task behind the infra phase.

**2. The SPA never stores the access token, and never reads a role from it.**
The token is read from MSAL's own cache per request and never copied into React
state, a module variable, `sessionStorage` or `localStorage`. Two corollaries,
recorded so a later task does not harden the wrong thing: **ADR-025 T15 is
about the *invitation* token**, and MSAL's `sessionStorage` cache
(`msalConfig.ts:36`, chosen deliberately for a narrower XSS blast radius) is
not a violation of it; and `localStorage` remains **unwritten anywhere** in
`web/src` (the one mention, `workspaceStore.ts:6`, is prose about the array w14
deleted — live code is `sessionStorage` at `:45`).

**NW-06's client half is that nothing changes, and the guard is what matters.**
`workspaceRole.ts:37-39`'s parse and the deliberate split at `:41-53`
(permissions degrade to least privilege; the **label** does not, `:55-59`) are
unchanged, and ADR-026's w14 clause 3 makes that parse mandatory rather than
defensive because `role` is a bare wire string. The one way this item can go
wrong on the client: **NW-05 must not introduce a `roles` claim read in the
SPA.** ADR-010's w14 clause 1 forbids a `roles`/`tenant_id` claim as an
authorization source server-side; the client mirror is that the SPA keeps
reading `role` from the **`GET /api/workspaces` row** (ADR-026 §D1) and never
from the token it is now holding. **A token is trivially decoded in a browser**,
which is exactly why this is written before the task exists.
security-architect co-signs (their NW-05 and NW-06 rows).

**3. `X-Tenant-Id` stays a header and becomes a selector — ruled, not assumed.**
This seat asked the question at the table and **security-architect ruled it**
(their NW-05 row; software-architect concurs): the header survives, **demoted to
an authorized selector** verified against the token subject's live membership on
every request, **404** on failure — never 403, which would be a tenant-existence
oracle. A caller may belong to several workspaces, so the token subject alone
names no tenant, and only **6 of 36** routes carry `{tenantId}`.

The client consequence this seat claimed and now records as decided: the
`raffa.signin.currentWorkspace` **hint survives unchanged** (second clause of
the first w14 footer, with its four-way resolution order), and the e2e seam
built on it (`v2.spec.ts:68-85`) **survives unchanged**. Had tenant selection
moved into the route path, it would have been a change to **every tenant-scoped
method in `client.ts` plus every call site** — a different task size. It did
not; no task should re-open it.

**4. NW-61 — a screen is *told* it is not ready; it never infers it.** The first
w14 footer's clause 1 forbids a client store standing in for a missing GET.
This wave binds the **sibling defect: a client must not infer a server state it
can be told.** Three surfaces do exactly that today and all three are corrected
by reading a server field:

- **Ask.** `ask/index.tsx:99-105` sets `hasAnyDocument` from
  `page.totalCount > 0` — documents in **any** state, including `Failed` — and
  `askViewModel.ts:250-261` then tells the user the document "is still
  processing or waiting for review", **a lie for a tenant whose only document
  failed**. The off-state branches on the server's **not-yet-terminal** count,
  not on `totalCount`. This makes a third off-state expressible — *documents
  exist, none in flight, none validated* — which **must not be collapsed back
  into the "still processing" sentence**, since that would re-ship today's lie
  under a server number. **Its copy is the ux-ui-designer's** (ADR-020).
- **Ask must re-read while it is off.** `useValidatedContractCount.ts:42-50`
  states in its own comment that it fetches once per shell mount with **no
  re-poll**, and `ask/index.tsx`'s effect deps confirm it — so a user sitting on
  `/ask` while a document completes **never sees the gate flip**. While the
  off-state shows *and* the in-flight count is non-zero, Ask re-reads on the
  **same 2 s cadence and predicate shape** `useDocumentsList.ts:110-119`
  already uses, reusing the existing `listDocuments(pageSize: 1)` call at
  `ask/index.tsx:102` — not a new endpoint, not a poll of `GET /api/workspaces`.
  It stops the moment it flips.
- **Portfolio.** `contracts/index.tsx:130-139` renders "Nothing to triage yet"
  with no processing read at all; it makes the same one-row call and shows the
  processing state instead of the reroute when the in-flight count is non-zero.
- **Contract 360** reads no `processingStatus` anywhere (the feature's only read
  is a per-row tag, `contract360ViewModel.ts:449`) and is **contract**-scoped, so
  the workspace envelope cannot answer it — hence ADR-027 §D8's per-contract
  `readiness`.

**Both counters become the server's number**, which the first w14 footer's
clause 7 already requires ("a count has one definition and it is the server's")
and the documents screen violates twice: `useDocumentsList.ts:123`
`allCount = documents.length` is the **fetched page**, capped at
`LIST_PAGE_SIZE = 100` (`:15-18`), and `attentionCount` (`:122`) is a client
filter over that same page (`documentTable.ts:141-143`). Meanwhile
`result.page.totalCount` is fetched and **never read** there. Both read
ADR-027 §D7's server `counts`.

**No client-side derivation is proposed for any of the four.** Deriving "still
processing" by fetching every row and testing it is precisely the heuristic
clause 7 deleted for contract counts.

**5. The optimistic upload row is handed off on evidence, not on a timer — and
this is a regression NW-27 *creates*.** `useDocumentsList.ts:140` drops the
local entry and `:141` **then** calls `load()`, so the row is absent between
those two statements. Today the POST takes tens of seconds and the gap is one
invisible RTT at the very end; **once the POST returns in under 2 s the gap
becomes a visible flicker on every file of a 15-file batch** — precisely the
"all 15 rows exist" claim A15-1 makes. The `"admitted"` branch records the
**server id from the 201** (`schema.ts:199`) on the local entry, and the local
entry is dropped only when a server row with that id is present in `documents`.
Never on a timer. If `load()` fails the local row **stays** and the list shows
ADR-018 `:117`'s error state — the row does not vanish.

**No change to the poll predicate**, and this seat withdraws the intake's
implied one: `hasNonTerminalRow` (`:105-108`) already reads the **server**
array and `:110-119` starts the interval on its flip. What is already right
must not be rebuilt — the optimistic row exists before the request starts
(`:131-134`), and the progress bar renders the **server's** stage, never a
client timer (`DocumentStatusTable.tsx:151`, `ProcessingPipeline.tsx:4-6`).

**One client-owned upload deadline**, configured in one place and never per
call site (`client.ts:1726` has no `signal`; `uploadPipeline.ts` has no
`AbortController` — zero matches for `AbortController|AbortSignal|signal:` in
`client.ts`). An abort maps to the existing `"failed"` branch
(`uploadPipeline.ts:127-132`, which already handles `statusCode === null`), so
the user gets Raffa's retryable message rather than an opaque platform 502. Two
constraints bound the number and neither is invented: it must exceed the time
to **send** a legitimate body (`MAX_FILE_BYTES` is 50 MB,
`uploadPipeline.ts:19`; `fetch` + `AbortController` cannot express
"time-to-first-byte after the body is sent", so the deadline is necessarily
absolute), and it must sit **below** the platform ceiling or it never fires.
**The ceiling is still implicit** — `infra/modules/containerapps/main.tf:179-194`
declares no `timeout` and ADR-005's w15 footer pins none (verified). Assumption
in force: **120 s**, with its derivation recorded in the task (OQ-w15-ca-01).
This is a safety net; the mechanism that makes upload feel instant is NW-27.

**6. NW-10 — the rail badge reads the server, and `documentStore.ts` is deleted
outright.** No seat is listed on NW-10 because the rule that decides it is
already this ADR's (first w14 footer, clause 1); the client shape is recorded
here because it now costs nothing. The store's write half `rememberDocument` is
**declared at `documentStore.ts:77` with zero production call sites** (every
other hit is prose), so `RailNav.tsx:67`'s `loadTrackedDocuments()` returns `[]`
in every fresh session and `getDocumentsBadge` (`navItems.ts:99-107`) returns
`null`: **the badge is not wrong, it is always absent.** `RailNav.tsx:65-66`'s
comment ("There is still no `GET /api/documents` collection endpoint") is stale
— `listDocuments` is at `client.ts:1811` — and dies with the store.

Replacement: a `useDocumentCounts` hook over `listDocuments(pageSize: 1)`
reading the **same `counts` object** clause 4 adds, fetched by `AppShell` and
passed down as props — the exact shape `useValidatedContractCount.ts:51-79`
already establishes and the one `documentStore.ts:21-25` itself asks for.
`DocumentCounts` (`navItems.ts:84-92`) keeps its two fields and loses its NW-10
note; `getDocumentsBadge`'s honest-absence rule (`:94-98`, "never a fabricated
`0 docs`") is **unchanged**. `pageSize: 1` means the rail costs one row, not a
hundred. Recorded and deliberately **not** turned into tasks: three sibling
session stores are the same defect class and **no w15 item touches them** —
`renewalActionStore.ts:28`, `quoteOutcomeStore.ts:31`,
`negotiationStepsStore.ts:10`.

**7. NW-69 — the outcome discriminant is a server string, and the link renders
only when it is usable.** `memberViewModel.ts:166-173` is today a discriminated
union on the server boolean `mailDelivered`, and **its own doc comment names
this wave as its owner**, verbatim: *"not a third, invented state; **the
transport wave that needs a third value owns that change**, not this one."*
ADR-020's w14 footer `:283` settled the same point from the copy side — a
failure needs "a third value, not a second boolean".

The discriminant becomes the **server's outcome string** on the invite 201: not
a second boolean, not two booleans the client combines, and **never a client
inference from a status code** — `members/index.tsx:118-120` records why in its
own comment (*"this fallback used to claim a mail had gone out on this same
failure path"*). Four outcomes become expressible: sent · mail failed · guest
provisioning failed · created-with-no-transport-configured.

**The `acceptUrl`/`expiresAt` arm is keyed to "the link is the usable path",
not to "mail did not go out"** — A15-7's whole point is never a "ready" link
that cannot be used. Mail failed → link **shown** (it is the only way in), with
ADR-020's pre-decided copy. **Guest provisioning failed → link suppressed**,
named error only: a link to a sign-in the invitee cannot complete is the defect,
not the remedy. Sent → no link. **security-architect ruled the same split from
the security side** (their ask to ux-ui-designer) and this seat concurs
exactly. **Reconciled with ux-ui-designer**: if their outcome vocabulary differs
from the wire values, the mapping lives in `memberViewModel.ts` and is theirs to
name — the pane still renders **one server fact**.

**NW-68's absolute accept link costs this seat zero.** `composeAcceptLink`
(`memberViewModel.ts:175-179`) is `new URL(acceptUrl, origin).toString()` and
its own comment already states it accepts a site-relative **and** an absolute
value unchanged. When `Invitations__AcceptUrlBase` lands, **the client does not
change** — recorded so no task is written for it.

**8. NW-67 — one gesture, and it is the one already on the screen.** Verified
current flow: the token is read from the fragment and the address bar cleared
in a lazy `useState` initializer (`invite/accept/index.tsx:57-68`, `:101`),
before first paint — satisfying ADR-025 Rule C10b's intra-route ordering, which
the second w14 footer adopted. The only effect (`:126-134`) is mount-only and
calls the **GET**, never the accept. `handleContinueWithEntra` (`:172-183`)
calls `loginPopup` and, verbatim, *"Success needs no handling here"* — the
`useMsal()` re-render merely flips the CTA (`:224-238`). **So A15-4 dies today
on a second, mandatory click.**

`handleContinueWithEntra` **awaits its own `loginPopup` promise** and continues
into the accept on resolution. Deliberately **not** a `useEffect` on
`accounts`: it is the *same* user gesture, and the consent the button carries
is consent to join; it fires only on the transition **this screen initiated**,
so a visitor **already signed in** on arrival still gets ADR-020 **state 3** and
its explicit "Join {workspace}" button — an effect on `accounts` would
auto-join them silently and remove the one consent step state 3 exists for; and
an effect keyed on an MSAL account array can fire for reasons that are not this
sign-in. Ordering inside the chain: if the GET has not resolved when the popup
does, the accept **waits for the offer** — `handleJoin`'s existing guard
(`:137-139`, `state.phase !== "offer"`) must not be weakened into a race.
Popup-blocked keeps its current landing (`:175-177` → ADR-020 state 5), which is
why the second w14 footer's clause 4 still holds: **no `must` depends on the
popup.** Nothing else on this route changes — not the 8-phase state union
(`:70-80`), not the fragment rule, not the storage rule.

**The client-architecture reason NW-05 must not slip behind NW-67.** The accept
binds identity from `X-User-Id` (`client.ts:1093`), whose value is the MSAL
account `username` (`main.tsx:70-73`), and the server matches that string
case-insensitively against the invited email. For a freshly provisioned **B2B
guest** the directory UPN is the `…#EXT#@<tenant>.onmicrosoft.com` form and what
MSAL surfaces is the provider's `preferred_username` — **provider-defined for an
email-OTP guest and not assertable from this tree**. If it is not the plain
invited address the match yields 403 → ADR-020 state 6 "wrong account": the
exact failure A15-4 exists to remove, arriving *after* the mail, the guest and
the passcode all worked. The decision that removes the guess is that the accept
binds to a **server-verified identity**, not a client-supplied string — which is
NW-05, and which is why the intake's ordering constraint is load-bearing rather
than tidy. security-architect's independent instruction is the other half and
this seat adopts it: **bind the Graph-returned guest object id into
`workspace_user.ExternalSubjectId` at invite time** (ADR-025 §J.3b). If NW-05
slips inside the wave, **A15-4 must be walked against a real B2B guest** before
the wave is called done — a passing walk with an existing member's account
proves nothing about this path (OQ-w15-ca-02).

**9. Correction to clause 3 of the first w14 footer: nesting IS supported;
`$ref` is not.** That clause states a row schema "must be **flat scalars** — the
`object` case renders a flat property map and **a nested object renders
`unknown`**". **The second half is false, and it is this seat's own error.**
`renderSchemaType`'s `object` case renders each property with
`renderSchemaType(propSchema)` (`generate-api-client.mjs:110`), which re-enters
the same `object` branch at `:97` — so a nested object declared inline with
`properties` renders as a nested inline type literal.

**Proved by existence, not by reading the code**: `raffa-api.v1.json:3103-3105`
and `:5253-5261` already nest an object **three deep**, and
`web/src/api/generated/schema.ts:646` already carries the rendered result
(`renewalUrgency: { score: number; …`). The misleading source is the
generator's own comment at `:98-106`, which describes the contract as it stood
when the case was written ("the response bodies this contract documents today
are all a single flat level") and invites an extension that is **not needed**.

What genuinely renders `unknown` is narrower and unchanged: an `object` schema
with **no `properties`** (`:107`), and any schema the `type` switch never
reaches (`:114-118`) — which is **every `$ref`, `oneOf`, `anyOf` and `allOf`**,
because a `$ref` object declares no `type` at all. The contract already works
around exactly that and says so in its own words at `:3910`: a shape is
*"duplicated here, not `$ref`'d (web/scripts/generate-api-client.mjs has no
`$ref` support)"*. There are **zero real `$ref` usages and no `components`
section** in the contract today.

**Why this is load-bearing this wave rather than pedantry.** ADR-027 §D7–§D9
and the NW-61 row promote `counts { all, needsAttention, processing, rejected }`
on `listDocuments` and `readiness { … }` on `getContract360`, and record — on
the strength of clause 3 — that they "are nested objects rendered `unknown`".
Left standing, that belief has three outcomes and **two are defects**: the
fields get flattened onto the envelope to dodge a limit that does not exist; or
the generator is extended for a case it already handles; or — the likely one,
because it is the smallest edit sitting in front of an `unknown` — the client
**hand-writes the type in `client.ts`**, which is the "no hand-written
divergent DTOs" prohibition of `:53`, the one rule this ADR has held since it
was accepted. **Corrected: `counts` and `readiness` land as inline nested
objects, the generator is not touched, and no DTO is hand-written.**

**The binding rule that replaces the false one**: every schema this wave adds is
declared **inline** with `type`, `properties` and `required` — **no `$ref`, no
`oneOf`/`anyOf`/`allOf`, at any depth**. Nesting is free; referencing is what
breaks. Clause 3's other three constraints are unchanged and were re-verified
line-exact this wave: `operationId` mandatory or generation throws
(`:125-130`); **enums become literal unions** (`:63-65`); and **only
`responses` are parsed** (`:135-146`). software-architect's own generator
caveat stands exactly as they wrote it and this seat re-affirms it:
`readiness.stage` is nullable and therefore **must not** carry an `enum`,
because the `enum` branch (`:63-65`) is evaluated **before** the nullable-union
branch (`:72-76`) and silently **drops the `null`** — a hazard the contract
already documents against `stage` at `raffa-api.v1.json:982`. For the same
reason NW-69's invite-outcome field is a **non-nullable** string with `enum`:
non-nullability is not a preference there, it is what keeps the pane's branch
exhaustive under `tsc --noEmit`.

**10. `processingStatus` lives in EIGHT contract sites, and the eighth is
invisible to every check the build runs.** A grep for the member `"NeedsReview"`
in `web/openapi/raffa-api.v1.json` returns eight lines: `:738, :873, :972,
:1101, :1292, :1394, :2371, :3415`. Seven are response schemas. The **eighth,
`:864-878`, is `listDocuments`' `status` *query parameter*** (`"in": "query"`,
"Exact match on Document.ProcessingStatus"). Because only `responses` are
parsed, a `parameters` enum **never reaches `schema.ts` at all** — so if
OQ-w15-004's `Rejected` value lands and the task edits the seven response
schemas, the eighth **does not fail the build, does not fail `tsc --noEmit`,
and appears in no generated type**. The contract would go on declaring that a
documents-list filter accepts five values while the server has six, and **a
terminal `Rejected` row is precisely a row a user will filter to**. Ruling:
**eight sites, one task, and the task's check is a grep of the contract for the
vocabulary — not a green build.**

**One task owns `web/openapi/raffa-api.v1.json` per phase** (clause 3, first w14
footer: `schema.ts` is regenerated wholesale on every build, so two tasks in one
phase produce a conflicting artefact rather than a mergeable diff). This wave
has three contract edits — NW-27's status value, NW-61's `counts`/`readiness`,
NW-69's invite outcome — and they must be **one task or different phases**.
**NW-05 does not contend** (clause 1: zero contract edits), which is the one
piece of slack in the budget. Contended client files and their rulings:
`client.ts` — **NW-05's task does not share a phase with any other task editing
it**; `useDocumentsList.ts` — NW-61 and NW-10 are **one task**;
`routes/workspace/members/*` — NW-68's pane path and NW-69's outcomes are **one
task**, as the intake already says.

**11. The router is untouched, and mobile is untouched.** **No w15 item adds,
moves or removes a route** — recorded explicitly so no task re-opens either
table: w14's `BrowserRouter` hoist stands (`App.tsx:221-237`,
`WorkspaceShellApp.tsx:64-98`), `/invite/accept` remains the one public route,
and the three new screen states of clause 4 are ADR-018 `:112-119`'s states
contract, whose copy is the ux-ui-designer's. **ADR-013 is untouched** — mobile
remains a non-gating scaffold and no w15 item reaches it.

**12. Checks that actually run.** The 37-call-site risk gets one that does: a
**vitest** case enumerating the `ApiClient` surface (`client.ts:1021`, 36
methods) and asserting **every** method attaches `Authorization` — `web.yml`
runs `npm test` today, so this is a real gate this wave. NW-69's four outcomes
are vitest cases for the same reason. The browser cases (A15-1's post-drop row
count and reload, A15-3's processing states on `/ask` and `/contracts`) are
**added to `v2.spec.ts` rather than tightening its existing terminal-state
polls, which legitimately still take minutes** — and, per ADR-016's w14 footer,
**no workflow runs Playwright at all** (NW-50 is W18), so a spec file is
evidence for the acceptance runbook this wave, not a delivered check. A15-8's
forged-header assertion belongs to the backend seats: a browser cannot forge a
header it does not send.

**13. Round-two reconciliation — two peer corrections adopted, and the client
mechanisms they need.** ux-ui-designer's table turn followed this seat's and
corrected two statements above. Both corrections are adopted as stated; each
needs a mechanism that is this seat's, and one of them is a **deletion that
cannot proceed as written**. Everything here was re-read on the same baseline.

**13.1 Clause 7's outcome set is corrected from four values to three.** The
NW-69 row (citing software-architect's NW-67 row) rules that a provisioning
failure **aborts the invitation — no row, no mail, no token, and a 502**. There
is therefore no 201 that can carry a "guest provisioning failed" outcome, and
clause 7's list of four is wrong by one: the wire vocabulary is **sent · mail
failed · created-with-no-transport-configured**. Clause 7's *rule* — the
`acceptUrl`/`expiresAt` arm is keyed to "the link is the usable path" — is
unchanged, and now holds **by construction** instead of by a branch a task could
mis-key: their mechanism is strictly stronger than the link suppression this
seat proposed. Consequence for the task: the `enum` has **three** members, the
pane's branch is exhaustive over three, and **no fourth arm is written** — a
dead branch keyed to the link is precisely the one a later reader re-enables.

**13.2 The 502's `reason` has no way to reach the pane, and the smallest repair
is the forbidden one.** `InviteWorkspaceMemberResult` (`client.ts:160-170`)
carries `ok`, `statusCode`, `member` and **`error: string | null`** — declared
in its own doc comment as a *"plain-language failure reason"* — and
`members/index.tsx:121` puts exactly that prose into the pane's pre-creation
error slot (`InvitePane.tsx:110-114`). ux-ui-designer's copy for the closed
reason set (`consent_missing` · `provisioning_failed` · `directory_unavailable`)
and their catch-all row need a **machine-readable discriminant**, and with none
in the envelope the shortest path in front of the task is to **string-match the
server's prose** — a client inference of a server fact (clause 4 forbids it) that
breaks on any copy edit. Decision: the envelope gains a typed **`failureReason`**
anchored to the generated `…["responses"][502]["content"]["application/json"]`,
exactly as `InvitedMemberBody` is anchored to `[201]` (`client.ts:150-151`);
`error` stays the prose fallback for the statuses that carry no reason
(400/401/403/409). **This binds the contract**: `renderOperation` renders
**every** status key under `responses` (`generate-api-client.mjs:135-143`), so a
**declared** 502 is typed for free — and an **undeclared** one forces a
hand-written DTO, the one prohibition of `:53`. The reason field is non-nullable
with `enum` (clause 9). **And the runtime catch-all row is mandatory even though
the type says it is unreachable**: a literal union describes the contract, not
the wire, so `tsc` will report ux-ui-designer's catch-all as dead code and a task
will delete it. It stays, and this sentence is the reason it stays.

**13.3 `identityProvisioned` is a non-nullable boolean, and `false` means "not
configured", never "failed".** ux-ui-designer keys their one-time-code sentence
to `identityProvisioned === true` and renders nothing when provisioning is not
configured; software-architect's `NotConfigured` value plus the abort rule of
13.1 make those the only two states a 201 can carry. A plain boolean is
therefore sufficient, and a **nullable** one is harmful (clause 9: the generator
drops the `null` from a nullable `enum`, and a third rendering would have no
meaning). The pane **must not infer a failure from `false`** — a failure never
reaches this code path at all.

**13.4 The refusal has FOUR producers, exactly ONE becomes a server row, and
`UploadResultCard.tsx` may not be deleted until the other two have a surface.**
The NW-27 row deletes the card on the ground that "with the gate on the Worker
the file **is** stored", quoting the card's own premise (`UploadResultCard.tsx:14-16`:
*"a rejected file was never stored, has no id"*). Read against `runUploadBatch`,
that premise dies for one path and **survives for two**:

- `uploadPipeline.ts:117-120` — **422 + `rejection.reason`** (`not_a_contract` /
  `no_readable_text`): the content gate that moves to the Worker. Becomes a
  `Rejected` **row**. Their ruling applies exactly.
- `:105-108` — **`isOversized`, checked in the browser with no HTTP call at
  all** (`getOversizedCopy`, `:50-52`). Nothing is stored: no row, no id, in this
  wave or any later one.
- `:122-125` — **413 / 415**, the server's format/size refusal, which
  product-owner's split gate keeps **in-request and before storage**. Same: no
  row exists to carry it.
- `:127-132` — `failed`, unchanged.

Deleting the card as written leaves an oversized file and a refused file type
with **no surface at all** — a silent drop, a worse defect than the two-surfaces
one the deletion fixes. Decision, which keeps their principle rather than trading
it: **`LocalUploadEntry.phase` (`uploadPipeline.ts:69-75`) gains `"rejected"`**,
and the two pre-storage refusals render as a **local row** in the grid the table
already renders for local entries (`DocumentStatusTable.tsx:86-110` — filename
cell, the existing `hint` slot at `:90`, tag cell, next-step cell), with no
action and no delete cell. One file is then **one row on every path**, which is
ux-ui-designer's own rule; the row is session-only **by construction** (no server
id, never in `documents`, never in `counts`), which is R-DOC-04's; and the card
plus `RejectedFileOutcome`, `rejected`, `dismissRejected` and
`onDismissRejected` (`useDocumentsList.ts:38-40`, `:64`, `:145-149`, `:176-180`,
`:205-207`; `DocumentStatusTable.tsx:21-22`, `:60-62`) all leave together. **The
copy and the tag stay ux-ui-designer's** — their "Not added" reading and the
three sentences minus the `"Not added: "` lead-in apply unchanged to a local row;
only the *surface* is this seat's. If they would rather keep the card for these
two paths, that is their call and it is recorded as such. What may not happen is
the deletion with no replacement.

**13.5 A tenant-wide count over a client-side filter is clause 4's defect one
level down, and the third chip is where it bites.** The grid fetches **one page**
(`useDocumentsList.ts:79`, `LIST_PAGE_SIZE = 100` at `:15-18`) and buckets
`attention`/`all` **client-side** — deliberately, per that comment ("not via a
server round trip per filter click"). Clause 4 makes both numbers tenant-wide
**server** facts while the rows stay one page; today they agree only because both
are page-derived. Two rules follow. **(a)** `isAttentionStatus`
(`documentTable.ts:141-143`, `processingStatus !== "Completed"`) must mirror
`counts.needsAttention`'s server definition — *not `Completed` **and** not
`Rejected`* — because one definition with two implementations is exactly how the
number and the list drift. **(b)** The `Not added · K` chip **cannot filter the
page**: `counts.all` excludes `Rejected`, so whether a refused row is in the
default page at all is a server decision, and a client-side bucket would need the
page to carry rows that `counts.all` says are not in "All documents" — two
definitions of one list. The chip therefore fetches its own bucket,
`listDocuments({ status: … })`, which is **clause 10's eighth site**: the `status`
query parameter that no build and no `tsc` check reaches. The third chip is what
turns that latent hazard into a shipped one, so 13.5(b) and clause 10 are one
task. Where a bucket exceeds the page, the screen must not present the page as
the tenant; the honest reading is ux-ui-designer's to word.

**13.6 `buildKbSummary` is a third page-derived number, and "askable" is not what
it counts.** `documentTable.ts:154-160` computes its total from `items.length`
(the fetched page) and its `askable` from `processingStatus === "Completed"`.
ux-ui-designer found the same and routed it to software-architect as an ask
(`counts.askable`, or the segment goes). The client half, whichever they answer:
**no page-derived number survives on this screen.** If `counts.askable` lands the
segment reads it; if it does not, the segment is **removed** rather than left
counting `Completed` over at most 100 rows. This is clause 4's rule applied to
the one number clause 4 did not enumerate.

**13.7 "Queued…" is the server row's null stage, not the local row's.**
ux-ui-designer rules that `stage: null` reads "Queued…"; the site is
`DocumentStatusTable.tsx:155` (`(item.stage ?? "Uploading") + "…"`). The **local**
row's "Uploading…" (`:105`) is a different fact — the bytes are in flight and no
server row exists yet — and it stays. A task that changes both makes the one
genuinely-uploading reading in the product say "Queued".

**13.8 Single-writer, updated by these corrections.** 13.4–13.7 put NW-27's
refusal-states work and NW-61's counts work in the **same four files** —
`uploadPipeline.ts`, `useDocumentsList.ts`, `DocumentStatusTable.tsx`,
`documentTable.ts`. Clause 10's ruling therefore tightens: **NW-27's states task
and NW-61's counts task are one task, or they are in different phases.** And
`web/openapi/raffa-api.v1.json` gains a **fourth** w15 edit — 13.2's 502 body —
on top of the three clause 10 names; all four remain one task or different
phases.

**14. What this footer does not do.** It does not edit either w14 footer —
clause 1 supersedes one sentence of the first and clause 9 corrects one
statement in it, both **by naming them here**, because rewriting a body the
gate has already read would mean the approved record is not the one that was
checked. It does not touch ADR-018 (no route changes), ADR-013 (mobile
untouched), ADR-020 (states and copy are the designer's), ADR-010 / ADR-022 /
ADR-025 (the security seat's) or ADR-026 / ADR-027 (the software seat's) —
this footer supplies the client half and the generator facts those decisions
rest on, and takes their rulings as given.

## Amendment (2026-09-13, wave w15 round 3 — the identity config the SPA is deployed with, and a poll whose termination proof was withdrawn)

Second w15 footer, written at the re-entry round. It amends nothing in the body
and nothing in the three footers above it; §15–§17 are new decisions, §18–§19
record peer rulings that close clauses this ADR left open, and §20 states the
limits. Items served: **NW-61**, **NW-05**, **NW-27** (its client half),
**W15-01** (the gate step in §15).

### 15. The SPA's identity configuration is written at web-deploy time only, and an `infra/**` merge produces no web run at all

This ADR's own rule is that per-environment values are **runtime config, not a
build-time static**, and the tree keeps it: `appConfig.ts:1-19` fetches
`/config.json` from the deployed origin at boot because one bundle is built once
and deployed to both environments. What this footer records is the consequence
nobody's lane had followed to its end — **where that file comes from, and when**.

**Verified this round.** `web.yml:144-205` writes `web/dist/config.json` during
the *deploy* job, from four live lookups and two literals: the API origin from
the container app's ingress FQDN (`:177-183`), the authority from
`vars.AZURE_TENANT_ID` (`:146`, `:201`), the redirect URI from the SWA hostname
(`:170-176`, `:203`), the client id from
`az identity show --query "tags.oidcPublicClientId"` (`:184-185`, `:202`), and
the two API scopes as **hardcoded strings** (`:204-205`). `web.yml` fires on
`web/**`, `.github/workflows/web.yml`, `.github/actions/azure-login/**` and
`scripts/write_web_runtime_config.py` (`:9-23`), plus `workflow_call` for the
`demo` promotion (`:28-37`). There is **no `workflow_dispatch`**, and `infra/**`
appears in **neither** workflow's path filter.

**Therefore**: delivery-manager's infrastructure-only PR (ADR-014 w15 clause 5)
merges and applies with **no web run at all**. If that apply moves any value
`config.json` carries, the deployed SPA keeps serving the **stale** one until an
unrelated `web/**` push happens to redeploy it. ADR-005 clause 11 calls this
class *"an outage behind a green CI run"*; from this seat it is worse and more
precise — it is an outage behind **no run**, and on `dev` there is **no manual
redeploy to reach for**. The failure is also not loud at boot:
`AppConfigError` (`appConfig.ts:44-50`) rejects config that is *missing or
malformed*, and a stale-but-well-formed client id is neither — it validates, the
app boots, and Entra rejects the authorize request for an application id that no
longer exists, in the browser, per user.

**Rule (general).** A wave whose infrastructure PR changes a value `config.json`
carries — the **public-client** app id (`modules/identity/main.tf:43-44`, app at
`:116`), the SWA hostname, the API container-app FQDN, the tenant id, or the API
`identifier_uris` (`:70`) — must **force a web redeploy after the apply**, and
the only mechanisms that exist are a push touching one of `web.yml`'s four
filtered paths or the `workflow_call` the promotion uses. It is a **gate step**,
not a task: no code changes, and the wave that needs it cannot discover the need
from a red run.

**Answer for w15: no forced redeploy is needed, and this is checked rather than
assumed.** The registration change in this wave's infra PR is one ungated
`optional_claims` block on `azuread_application.api` (`:57`), which is **not** the
public client — ADR-005 clause 11 `:772-775` states that itself and
`appConfig.ts:25` names the field *"Public-client (no secret) Entra app
registration id"*. The container-app changes add env keys and a scale rule, and a
Container Apps ingress FQDN is per **app**, not per revision. No SWA resource
moves. So `config.json`'s five values are all stable across this wave's apply.
Recorded as a **negative result** so the gate does not add a step it does not
need — and so the next `infra/**` wave asks the question instead of inheriting
the answer.

### 16. ADR-005 clause 11's plan-shape rule is adopted; its client-half collateral is corrected in one item and inverted in another

Clause 11 mandates that the PR plan show an **in-place update (`~`)** of
`azuread_application.api` and never a **replacement (`-/+`)**. **The rule is
right, this seat adopts it, and security-architect's clause 11 co-signature as an
authorization requirement stands.** Three corrections to its client half, each
verified on the tree, none of which changes the rule, the ordering or the $0.00:

1. **"the client id both SWAs record" is struck.** Both SWAs record the
   **public-client** id, not the API's: `web.yml:202` ← `tags.oidcPublicClientId`
   ← `azuread_application.public_client.client_id`
   (`modules/identity/main.tf:43-44`), the application at `:116`. Clause 11 says
   in its own next bullet that the public client is not touched (`:772-775`), so
   the collateral list contradicts the decision ten lines above it. Replacing
   `azuread_application.api` changes nothing either SWA records. The rest of the
   list — `AzureAd__ClientId`, the `aud` — stands untouched.
2. **The scope literals do not resolve against a client id.** `web.yml:204-205`
   (`:201-203` are the authority, the client id and the redirect URI) send
   `api://raffa-<env>-api/Raffa.Read|Write`, which resolve against
   `identifier_uris` (`modules/identity/main.tf:70`) — a **name-based literal**,
   deliberately so per its own comment `:65-69`. A replacement re-creates the same
   URI, so the literals still resolve. What a replacement genuinely takes out on
   this side is the service principal (`:107`) and the pre-authorization
   (`:155-161`), whose absence turns every sign-in into a **consent prompt**
   rather than a clean failure — a worse symptom to diagnose, not a better one.
   The `appConfig.ts:30` doc comment is where the wrong model comes from: it still
   documents the scope as `api://<api-client-id>/Raffa.Read`, the form this
   product does not use. **It is corrected with whatever task next touches that
   file** — the staleness class of §9's generator comment. (`:31-32`'s
   *"placeholder names until the API surface fixes them"* is stale for the same
   reason: `Raffa.Read` / `Raffa.Write` are fixed at `modules/identity/main.tf:83`
   and `:94`.)
3. **The inversion, and it is the client-critical half.** The SPA binds to the
   **URI string**, not to the application id — so the plan shape clause 11
   declares *safe* is the one that breaks it. An **in-place update (`~`)** that
   changes `identifier_uris` — an environment rename, a naming sweep, a
   `var.environment` change — **passes clause 11's check** and silently breaks
   every login in that environment, because `web.yml:204-205` is a hand-copied
   duplicate of `modules/identity/main.tf:70` with **no test, no build step and
   no plan assertion comparing the two strings**. **The plan check therefore gains
   a second, different assertion: `identifier_uris` shows no diff at all,
   whatever the resource-level shape is.** One rule protects the id; the other
   protects the string; neither implies the other.

**The durable repair, named and not scheduled**: `web.yml` should read the API
scope from the same place it already reads the client id — a tag or a Terraform
output — instead of re-typing it. It does not ship in w15: the wave's planned
CI-YAML set is **zero files** (delivery-manager), nothing this wave does moves the
URI (§15), and a workflow edit to remove a latent duplication is exactly the
"while we are here" change that turns a zero-file set into a one-file set.
Recorded so a later wave does it deliberately.

### 17. ADR-027 §C6 withdraws the property this ADR's poll termination rested on, and the client consequence is an interval that never stops

**Verified**: `useDocumentsList.ts:105-108` polls while any row is `Uploaded` or
`Processing`; `:110-119` sets the 2 s interval and clears it **only** when that
boolean flips. That was sound while D3 guaranteed *"the database owns the
terminal state"* — every row reached a terminal status, so the boolean was
guaranteed to flip and termination was a theorem, not a hope.

**§C6 withdraws that guarantee, honestly and correctly**: a commit landing after
the abandon window leaves a row at `Uploaded` **permanently**, and §0.1 forbids
the cross-tenant sweeper that would find it. §C6 lands the recovery entirely
server-side — a DLQ alarm and a D5 re-enqueue by an admin inside the tenant. **The
client is never told.** So the interval never clears: an open Documents tab issues
`GET /api/documents` **every 2 s for as long as it stays open** — ~1,800 requests
per hour per tab against an always-on API — while the screen shows ADR-018's new
*not ready yet* state, which is **true and never resolves**. A progress
affordance that cannot finish is the same defect class NW-61 exists to remove,
reached from the other end.

**Decision — the poll gains a no-change budget.** After a bounded period in which
**no row changes**, the interval stops; the rows stay exactly as the server last
reported them; an explicit **resume** control restarts it, and a reload restarts
it too. Three prohibitions, because each is a repair a task would otherwise reach
for:

- It must **not** re-label the row `Failed` or `Rejected`. The server says
  `Uploaded`; saying anything else is the fabricated fact ADR-027 §D8 and A15-3
  forbid, and `Failed` would additionally offer a retry the row cannot honour.
- It must **not** become a client timer feeding the progress bar. The w14 rule
  holds unchanged: the bar renders the **server's** stage
  (`DocumentStatusTable.tsx:151`, `:155`), never elapsed time.
- It must **not** become a second definition of terminal. The predicate at
  `:105-108` is unchanged and still reads the server array; the budget gates the
  **interval**, not the row's meaning.

**Assumption in force**: five minutes with no change on any row, expressed as one
constant beside `POLL_INTERVAL_MS` in `useDocumentsList.ts`. No endpoint, no
contract change, no generated-client change. The stopped state's one sentence and
the resume control's label are **ux-ui-designer's** — **OQ-w15-ca-05**, with the
existing empty/error idiom as the assumption until they rule.

**Cost, stated so the table can decline it knowingly**: this is a small edit in a
file NW-61's counts task already owns (§13.8), so it adds no task and no phase.
If the table prefers it deferred to W16, the wave ships with an unbounded poll
against a stranding case the wave itself introduced — that is a legitimate call,
but it must be a **recorded** one, not the silent default.

### 18. The counts clauses close, and the one mechanism still missing is a signature

- **§C9 adopts this seat's §13.5 ask in full** — `counts.needsAttention` is *not
  `Completed` **and** not `Rejected`*, with `isAttentionStatus`'s matching
  `&& !== "Rejected"` named as the **third** site in `documentTable.ts`, checked
  by grep and not by `tsc`. Adopted with nothing added: the seat that owns the
  field re-read the premise at source rather than taking it on anyone's word,
  which is why the number and the rows it filters to are now the same set.
- **§C5 and ADR-018 clause 3 resolve §13.6 to its second branch**: `counts` gains
  no `askable`, so screen 3's segment is **removed**. The mechanism that was still
  open is a **signature change**: `buildKbSummary` (`documentTable.ts:154-160`)
  stops being a function of the fetched page and becomes a function of `counts` —
  `N documents` ← `counts.all`, `K waiting for your review` ← `counts.needsReview`
  (§C5's member). With `askable` gone and `needsReview` on the server, **no
  page-derived number survives screen 3**, which is what §13.6 asked for and could
  not have had until §C5 added that member.
- **§C9.1 lands as a client prohibition**: `counts` is five overlapping
  projections, not a partition, so **every chip and every summary segment reads
  exactly one member and none is computed from another**. Concretely, `counts.all`
  excludes `Rejected`, so `all + rejected` is the only true total and **no surface
  displays it** — correct, and recorded so no task "repairs" the arithmetic.
  §C9.2's prohibition on rendering `all − needsAttention` as *askable* binds this
  seat identically.
- **Two doc comments must move with the edit**: `documentTable.ts:136-138` and
  `:152-153` cite the V2 oracle for definitions this council has now replaced. Left
  as they are, the next reader restores the oracle's definition over the council's
  — the same staleness class as §9's generator comment and as `appConfig.ts:30`
  in §16.

### 19. Every ask and every open question of this seat is discharged

- **OQ-w15-ca-01 — answered** by ADR-005 clause 13: no ingress ceiling is pinned
  and the 120 s client deadline stands. Their reason is better than the ask: NW-27
  makes the longest synchronous request **shorter**, so a ceiling raised now would
  be raised for a request that no longer exists.
- **OQ-w15-ca-02** — assumption in force, and delivery-manager made the residual a
  **numbered acceptance step** (A15-4 walked against a real B2B guest if NW-05
  slips) rather than a note. Discharged as scheduled.
- **OQ-w15-ca-03 — resolved**: the tenant stays a header, demoted to a
  membership-verified selector, 404 on failure.
- **OQ-w15-ca-04 — answered** by ux-ui-designer with a third Ask variant that
  honours this seat's binding constraint exactly: it is **not** collapsed back
  into the "still processing" sentence.
- **One ask refused, and the refusal is accepted.** "Web deploys before backend on
  NW-05" is not enforceable — two workflows, one push, no cross-workflow
  dependency — and the outage window is symmetric; security-architect supplied
  what makes the refusal safe (NW-05 **fails closed**, a bounded 401 window on
  `dev`, no header fallback re-added). Recorded as accepted so no task re-derives
  an ordering the table examined and rejected on evidence.
- **The decomposition asks are discharged**: NW-05's client task sits alone among
  `client.ts` writers in its phase, and one task owns `web/openapi/raffa-api.v1.json`
  per phase.

### 20. What this footer does not do

It rewrites no body and no earlier footer — §16 corrects **ADR-005's** text by
naming it here, in this seat's own file, because ADR-005 belongs to
cloud-architect and a body the gate has already read must stay the one that was
checked. It adds **no route** (ADR-018 remains `none` from this seat for the whole
wave), **no new ADR**, and **no change to the storage or fragment rules**. It does
not write ADR-005 or ADR-015 (cloud-architect's and delivery-manager's), ADR-027
(§C6 and §C9 are adopted as ruled), ADR-018 or ADR-020 (the stopped-poll sentence
and the resume label are the designer's, requested as OQ-w15-ca-05). It adds no
CI-YAML file to a wave whose planned set is zero: §15's forced redeploy is a gate
step and §16's durable repair is explicitly deferred.

## Amendment (2026-09-14, wave w16 — the three stores retire, and the client file this seat recorded as having no writer)

Wave `w16`, baseline `f0b3436` (`helix/w16` == `origin/main`, `0 0`). Seat
`client-architect`, seated by `w16-requirements.md` §3 on **NW-08, NW-31, NW-11,
NW-12, NW-13, NW-21**. The body and §1–§20 are unchanged; clauses continue at 21.
Companion decisions this footer reconciles with rather than re-decides:
**ADR-028** §D1–D6 (software-architect — routes, table, resolution),
**ADR-001** w16 clauses 2–4 and 7 (product-owner — scope and the money fence),
**ADR-022** w16 clauses 6a/7/11 and **ADR-011** w16 clause 14 (security).

**Two of this footer's clauses correct this seat's own lane draft**
(`reports/architecture/draft/next/client-architect/w16.md`). Both errors were
load-bearing: a task implementing the draft as written would have built a
read-back the screen cannot see, and the decomposer would have scheduled two
tasks into one phase on a file the draft said nobody writes. They are recorded
as corrections, not silently re-worded — the same discipline NW-31 spends this
wave enforcing on stale prose, applied to our own records.

### 21. The three `sessionStorage` stores retire, and §1's disposition table gains their rows

| Key | Module | Stood in for | Disposition |
|---|---|---|---|
| `raffa.renewals.actions` | `web/src/routes/renewals/renewalActionStore.ts` | a missing GET of the persisted renewal action | **deleted (NW-11)** |
| `raffa.quotes.negotiationOutcomes` | `web/src/routes/quotes/quoteOutcomeStore.ts` | a missing read of the recorded negotiation outcome | **deleted (NW-12)** |
| `raffa.contract360.steps.<id>` | `web/src/routes/contracts/contract360/negotiationStepsStore.ts` | a tick no endpoint recorded | **deleted (NW-13)** |

Each is w14 clause 1's case in its purest form and each store says so in its own
comment (`renewalActionStore.ts:8-19`, `quoteOutcomeStore.ts`'s read half dead in
production, `negotiationStepsStore.ts:6-7`). **Ordering, binding**: a store is
deleted **with** its read-back, never before it — a deleted store with no GET is
a regression, not a step toward one (ADR-028 assumption 4, this seat's ask,
ratified). The `sessionStorage` / `localStorage` policy of §1 is otherwise
unchanged: no new key, no new fragment rule.

### 22. NW-11 closes all three surfaces with **zero** new client methods — correcting this seat's draft

The draft's shape preference asked for the action embedded in `GET /api/renewals`
"**plus** a per-contract read for Contract 360, **which does not fetch that
list**". The parenthetical is **false**. `contract360/index.tsx:115` already
calls `apiClient.getRenewals(workspace.id)` — in the same `Promise.all` as
`getRenewalPriority` at `:116` — and derives its row at `:227`
(`renewals.find(r => r.contractId === contractId)`).

Consequences, all reducing work:

- ADR-028 §D1's embedded **`savedAction`** reaches **all three** surfaces
  (Renewals, Contract 360, Savings) through the **existing** `getRenewals`
  wrapper. NW-11 adds **no `ApiClient` method at all**.
- `GET /api/renewals/{id}/action` (§D1, 200/404) is published in the contract and
  gets **no `client.ts` wrapper** under §24 — it has no caller in this app.
  Stated so the NW-11 task does not add one out of a sense of completeness.
- **`savedAction`, never `action`** — §D1 adopted this seat's C9 as binding. The
  name `action` on `GET /api/renewals` is the deterministic calculator's
  `RecommendedAction` (`RenewalsEndpointExtensions.cs:239`), rendered on a
  shipped screen; reusing it would overwrite a calculator's output with user
  state.
- **Absence of a row is the status `NotStarted`, not a missing row** (C8, §D1).
  `handleUndo` (`contract360/index.tsx:261-269`) posts `NotStarted` **and**
  calls `forgetRenewalAction`; the server row is an upsert on
  `(tenant_id, contract_id)` and **survives** the undo, so today only the local
  forget hides it. After the read-back the row returns as `NotStarted` and every
  surface renders that as "no action taken". `forgetRenewalAction` dies with the
  store; **no DELETE route is requested**.
- **The row is not widened** with `supplierId` / `annualSpend` (C9 upheld): the
  client already holds both from fetches it has made
  (`contract360/index.tsx:250`, `renewals/index.tsx:104`).

### 23. NW-12: this seat's shape preference was declined — and the premise under it was false

The draft (C10) asked for the recorded outcome to ride the **existing**
`GET /api/quotes/{id}/assessment`, "which the screen already calls on mount".
**The screen does not call it, on mount or ever.** The mount effect
(`quotes/index.tsx:118-132`) calls `load()` → `apiClient.recalculateQuoteAssessment`
(`:91`), and `client.ts:805-813` records why: the recalculate response is a
**superset** of the assessment GET's shape, so `src/routes/quotes/` "never calls
`getQuoteAssessment` directly".

So ADR-028 §D2's decline is correct **on stronger grounds than it was given**:
the shape the outcome would have ridden is the one this screen never reads, and
the call it *does* make on mount is precisely the recalculate POST §D2 names as
the hazard — a recalculation that "would appear to re-report a negotiation
record it did not touch". A task implementing C10 would have shipped a read-back
the screen cannot see.

**Decision, adopting §D2:**

- The quote screen gains **one `GET /api/quotes/{id}` call on mount**, alongside
  the existing recalculate. Two calls on mount is §D2's "cheaper cost", accepted.
- **New wrapper: `getQuote(tenantId, id)`** — it has a caller in the same task,
  so §24 permits it.
- **`GET /api/quotes` (the list) gets no wrapper** — no list screen exists and
  none is built until NW-57 (W18).
- **ADR-018 stays `none`.** `/quotes/:quoteId` **already exists**
  (`WorkspaceShellApp.tsx:90`), the screen already reads it
  (`useParams`, `quotes/index.tsx:68`), and upload already navigates to it
  (`:151`). The quote id therefore survives a reload **today** — which is the
  fact that makes this read-back possible with no routing work at all. Had it
  not, NW-12 would have needed a route and a designer.

### 24. When a route gets an `ApiClient` wrapper — the rule, re-grounded against the precedent that cuts against it

The draft (C2) asserted flatly that a caller-less `ApiClient` method is dead code
`tsc` cannot flag. The codebase carries a **counter-example with a named
convention**: `getQuoteAssessment` is wrapped with no caller "for API-contract
completeness (the same *wrap the endpoint's full surface even if this app's own
screen only ever calls it one way* convention `getPortfolio`'s own doc comment
already follows)" (`client.ts:811-813`). The opposite convention is equally on
disk: `raffa-api.v1.json:6` documents `/api/insights/criticality` and
`GET /api/contracts/{id}/strategy` "for completeness, **no `client.ts`
wrapper**".

Both exist, so the rule is stated as a test rather than a taste:

> **A wrapper is added in the same task as its first caller.** A route with no
> caller is published in the contract and left **unwrapped**, with the omission
> recorded in `info.description` so it reads as a decision and not an oversight.

`getQuoteAssessment` is the cost of the other branch, and it is why this is a
rule: the method outlived its caller, now needs three lines of comment to explain
why nobody calls it, and a later reader cannot tell dead from load-bearing. A
wrapper also carries a second writer — its row in the `client.test.ts` surface
test — which a route with no screen cannot justify.

- **Unwrapped this wave**: `/api/audit` (NW-08), `GET /api/renewals/{id}/action`
  (NW-11), `GET /api/quotes` (NW-12).
- **Wrapped this wave**: `getQuote` (NW-12, §23); `getNegotiationSteps` and
  `putNegotiationSteps` (NW-13, §26).

### 25. `web/src/api/client.ts` is a **multi-writer** file this wave — the draft recorded it as having none

The draft's single-writer table says "**No new `ApiClient` method this wave**
(C2, C10, C13)". That was wrong on its own terms: NW-13 cannot write a tick
without a wrapper, and §23 adds one for NW-12. The real count is **three new
methods across two items**, both in theme B.

`client.ts` is **hand-written** glue, not generated (`client.ts:1-8`: "only the
fetch() plumbing is hand-written"). Two tasks editing it inside one phase is
exactly the defect §3 / constraint 1 prevents for the contract file, and
`check_single_writer.py` would reject the slice.

> **Ruling: one writer per phase for `web/src/api/client.ts`**, the same
> treatment the contract file already has. Cheapest resolution for the
> decomposer: the **theme-B contract task also owns the wrapper additions**, or
> NW-12's and NW-13's web halves land in **different phases**.

Named here because ADR-028's single-writer list (`:231-237`) is backend-only —
correctly, that is software-architect's lane. This file is this seat's, and it
was the one the draft left unguarded.

**Still not writers**, re-affirmed: `web/src/components/shell/navItems.ts` (no
rail entry, no badge — NW-08 is the item that would tempt one), `App.tsx`'s
router (`:222-236`), and `WorkspaceShellApp.tsx`'s route table (`:81-94`).

### 26. NW-13: named keys, a tick that must not survive a rejection, and two idempotent writes

- **Keys, never indices** (C11, adopted by §D3). The store is a positional,
  unnamed `boolean[4]` (`negotiationStepsStore.ts:11,18,24,31`); the labels live
  in `contract360ViewModel.ts:211-218` and **two of four are parameterized**.
  Client rules: an **unknown step key from the server is ignored**, never
  rendered as a fifth tick; a **missing key is unticked**; and
  `NEGOTIATION_STEP_COUNT = 4` stops being a client constant that shapes the
  wire. The rendered label stays client-side (ADR-001 w16 clause 3).
- **The optimistic tick reverts** (C12, binding). `handleToggleStep`
  (`contract360/index.tsx:271-276`) is a pure local toggle today; it becomes
  write + read-back. On failure the tick **reverts** to the last server-known set
  — which §D3's whole-set `PUT` makes trivial, since the client already holds it
  — and the screen shows ADR-018 `:117`'s error state. **A tick the server
  rejected must never survive on screen**: that is the fabricated-fact class of
  w15 clause 4, not a cosmetic concern.
- **§D4's two writes are accepted, and its ordering is ratified from this side.**
  The draft asked for one composed call so the client would have no
  partial-failure state to invent. **That ask is withdrawn**: §D4's reason is
  better than the convenience it refuses — folding the tick-clear into the
  renewals POST would make a write named "set the renewal action" silently erase
  another module's rows for any future non-UI caller. The client's real
  requirement is met by **re-reading, never by a compensating write**: both
  writes are idempotent, and both facts are already re-read on mount
  (`contract360/index.tsx:93,115`). **Ticks `PUT` first, then the action
  `POST`** — an action in progress with no ticks is an honest state a user can
  reach; `NotStarted` with four ticks is a contradiction no flow produces. The
  order is load-bearing, not arbitrary.

### 27. NW-08: the contract's prose half, and what "a typed client" means

- **C1, binding: the contract edit has a prose half, and without it the file
  contradicts itself.** `raffa-api.v1.json:6` (`info.description`) names
  **`GET /api/audit`** by hand among the backend routes "that this task
  deliberately did NOT add here". Adding the path and leaving that sentence ships
  a contract that documents a route and denies documenting it — the exact
  stale-record class NW-31 spends this wave deleting. **Both halves are one
  edit, in one task.**
- **What "a typed client" means** (product-owner, OQ-w16-003, ruling A16-2 closes
  "through `GET /api/audit` plus a typed client"): under §24 that is the
  **generated type** — `schema.ts` is regenerated wholesale, so the type arrives
  with the path at no cost — and **not** an `ApiClient` wrapper, which would have
  no caller. Recorded so the wording is not read as ordering a method, and so the
  gate does not read its absence as an unmet ruling.
- **The declared header is documentation, not a checked fact.** ADR-026 w16
  clause 2 declares `X-Tenant-Id` on the route so the client does not discover it
  at runtime. Note for the task: a declared **parameter** never reaches
  `schema.ts` — the generator parses only `responses`
  (`generate-api-client.mjs:132-146`, §3 / §9) — so the header's presence in the
  contract cannot be checked by `tsc`.
- **No SPA surface**: no route, no rail entry, no fetch, no state. ADR-018 and
  ADR-020 untouched. A16-2's ladder (401/400/404/403/200 — security S16-4) is
  proven by backend tests and `curl`; **no e2e and no vitest from this seat**.
  If OQ-w16-003 is ever reversed the client cost is a route, a rail entry, a
  wrapper, its surface-test row, a loading and an error state — a wave item, not
  a task addendum.

### 28. NW-31's client half is contract-only, and its gate is a **paired** grep

- **C3**: `web/src` sends neither retired header — `client.ts:76` attaches
  `Authorization` and nothing else; re-verified on `f0b3436`. Three contract
  sites only: the `X-Role` **parameter** (`:5239`), its description (`:5236`),
  and the stale `X-Workspace-Role` sentence on `deleteDocument`'s description
  (`:1227`), which `reprocessDocument` (`:1310`) inherits by reference.
- **C4 — the edit is invisible to every check the build runs.** The generator
  parses only `responses`, so a `parameters` entry **never reaches `schema.ts`**:
  deleting `X-Role` changes no generated byte, fails no `tsc --noEmit`, fails no
  build. This is §10's eighth-site hazard reached from the other end, and §10's
  ruling stands: **the task's check is a grep of the contract, not a green
  build.**
- **The grep is paired** — concurring with security **S16-7** and
  delivery-manager **D7**, reached independently here: zero
  `X-Role` / `X-Workspace-Role` across `backend/src`, `web/`, `web/openapi/` and
  `.github/`, **and `X-Tenant-Id` present and unchanged**, asserted positively so
  the sweep cannot overrun. `client.ts:1943-1950` — which sends only
  `X-Tenant-Id` and the bearer — is what the paired assertion protects, and it is
  the path A16-3's re-worded in-product acceptance runs on.
- **Regression cover, no edit needed**: the Admin catalog now comes from
  membership, observable at `/ask`; the existing e2e case **`v2.spec.ts:648`**
  (A8, "cosa sai fare?" answered from the capability catalog) is the one that
  would catch a regression. Named so the acceptance walk runs it.
- **OQ-w16-sa-02 accepted as this seat's, for W17.** A non-Admin can see an
  admin-gated capability's example question once the catalog is served whole.
  Per security **S16-11** `roleGate` is **presentation, never authorization** —
  every action behind it is enforced server-side by membership — so hiding the
  chip is **client work**, deferred, and it must never be re-described as a
  security fix.

### 29. NW-21: zero client change, and the client-side half of Fence 2

- **C13 upheld** by §D5 / OQ-w16-ca-03: the link is resolved **server-side**, so
  `quotes/index.tsx:274-282` is **unchanged**, NW-21's client cost is **zero**,
  there is no picker control and no new empty state, and **ux-ui-designer stays
  unseated**. A client that matched an outcome to an opportunity on
  `contractId`/`supplierId` would be inferring a server fact — w15 clause 4.
- **Fence 2** (product-owner: when `savingsPropagated` is `null`, no surface may
  present the outcome as a realized saving and no total may absorb it) holds in
  the client **structurally**, for the same reason software-architect gave
  server-side: after §30 the Savings table renders **only real
  `SavingsOpportunity` rows**, so an unlinked outcome has no row to appear in and
  no total to enter. Nothing is left to a rendering promise.
- **Product-owner's money fence is already satisfied, and verified rather than
  promised**: `savingsViewModel.ts:110` renders the realized KPI as
  `countOf(kpis.savingsRealized)` — a **count**, in a meta line beside the
  identified and in-progress counts — and no client surface renders
  `SavingsKpiSummary.Realized` as money. No w16 client task needs to change it;
  the fence's whole cost is that **none may**.

### 30. The Savings pseudo-opportunity row is retired (OQ-w16-ca-01) — ruled on client grounds, with the product half named

`buildOpportunityRows` (`savingsViewModel.ts:236-245`) **prepends every tracked
renewal action** to the opportunities table as a pseudo-opportunity row with
`estimate: NOT_YET_AVAILABLE` (`:223`), and the code concedes its own premise at
`:210-214`. Today that set is **this session's** actions — a handful. Read back
from the server it becomes **every renewal action in the tenant, permanently**:
the opportunities table fills with rows that are not opportunities and carry no
estimate. **A naive store→GET swap ships that flood**, and it would ship it as a
side effect of a read-back nobody asked to change Savings.

> **Decision: `buildTrackedOpportunityRow` is retired. Savings renders only real
> `SavingsOpportunity` rows** — NW-21 supplies the real link the pseudo-row was
> standing in for.

**Whose call this is, stated because it is not obvious**: product-owner is seated
on NW-12, NW-13 and NW-21 but **not on NW-11**, so no seat other than this one
can rule it, and leaving it unruled means a task decides it by accident. It is
ruled here as what it is — a **client-rendering defect** that the read-back
creates. The **product** half is deferred, not answered: whether Savings should
show tracked renewal actions at all is product-owner's, and if the answer is yes
it is a **designed section** of that screen — not a pseudo-opportunity row with
no estimate — which seats ux-ui-designer in W17.

### 31. Checks, and what this footer does not do

**Checks** (per item, owned by the web half of each task): vitest over the three
view models reading a **server** row (`renewalPipelineViewModel`,
`contract360ViewModel`, `savingsViewModel`) — CI runs `npm test`; vitest that the
quote screen renders a server-supplied outcome **with no session write**; one
`v2.spec.ts` case covering post-an-action → reload → the same action on all three
surfaces, and tick-a-step → reload → still ticked. Per **§12** the Playwright
case is **acceptance-runbook evidence, not a CI gate** — no workflow runs
Playwright (NW-50 is W18) — and it must not be presented as one. Cross-tenant
negatives on the new routes are ADR-028's, server-side, where they can be
enforced.

**This footer does not**: add, move or remove a route ⇒ **ADR-018 is `none`** for
this seat for the whole wave; touch the mobile scaffold ⇒ **ADR-013 `none`**
(non-gating, ADR-013 unamended); add a screen, state or copy ⇒ **ADR-020
`none`**, unless OQ-w16-003 or §30's product half is reversed, each of which
seats the designer; change the storage or fragment rules of §1 beyond the three
deletions in §21; write **ADR-026** or **ADR-028** (software-architect's — the
shapes are reconciled here, not re-decided); or rewrite any body or earlier
footer. It creates **no new ADR** and supersedes nothing.
