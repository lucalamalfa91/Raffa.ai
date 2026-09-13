---
id: E14/F03/US02/T01
type: task
story: us-02-signin-resolves-from-server
wave: w14
status: live
target_repo: raffa-web
---

# task-01-web-workspace-resolution — the app gate resolves from the server, the picker stops inventing, and `/invite/accept` exists

## Context

**Closes: NW-01 (web), NW-03, NW-09 (web), NW-24 (web), NW-14 (web), NW-58 (the
accept screen and the router hoist).**
Decision rows: `reports/architecture/waves/w14.md`, rows **NW-01**, **NW-03**,
**NW-09**, **NW-14** and **NW-24** (client-architect and ux-ui-designer halves)
and row **NW-58** (client-architect: the `BrowserRouter` hoist and Rules C9/C10;
ux-ui-designer: screen 11 in ten states). ADRs in force: **ADR-012 w14 footer
clauses 1, 2, 5, 7**, **ADR-018 w14 route footer + `:112-119`**, **ADR-020 w14
design footer (screen 1 and screen 11)**, **ADR-019**, **ADR-025 Rules C9 /
C10**.

The council mandated this fold: `web/src/routes/signin/*` is claimed by NW-01,
NW-03, NW-09 and NW-24 and they are **one coherent change** — the picker stops
being a `localStorage` cache. The `BrowserRouter` hoist and the accept screen
join it because `App.tsx` cannot route to a component another task creates in
the same phase (the e13 union-merge defect).

**This task owns, in phase 4**: `App.tsx`, `WorkspaceShellApp.tsx`,
`AppShell.tsx`, `workspaceRole.ts`, `navItems.ts`,
`useValidatedContractCount.ts`, `routes/signin/*`, `routes/contracts/contractStatus.ts`,
the new `routes/invite/accept/`, and both e2e specs.

**Interface contract with its phase-4 sibling `E15/F02/US01/T01`** (which owns
`routes/workspace/members/*` and nothing else): this task adds
`workspaceId: string` to `WorkspaceShellAppProps` and passes **both**
`workspaceId` and `role` into `<MembersRoute …>` at
`WorkspaceShellApp.tsx:71`, and **removes the `<RequireRole role={role}
allow="admin">` wrap from the `workspace/members` route** (`:67-74`) so a
Procurement member reaches the screen and the sibling renders the read-only
variant inside it. `RequireRole.tsx` itself is **not** edited by either task.

- **Architecture decisions in force**: ADR-012 w14 footer, ADR-018 w14 route
  footer (the hoist, the public route, and `:112-119`'s skeleton / error+Retry
  rule for every list surface), ADR-020 w14 design footer, ADR-019 (no new token
  or component is added by screen 1), ADR-025 Rules C9/C10.
- **Do not touch**: `web/src/routes/workspace/members/**` and
  `web/src/components/shell/RequireRole.tsx` (`E15/F02/US01/T01`, same phase);
  `web/openapi/raffa-api.v1.json`, `web/src/api/generated/schema.ts`,
  `web/src/api/client.ts` (phases 2 and 3 own them — **if a client method you
  need is missing, HALT and name it**, do not add one);
  `web/src/routes/ask/**` (`kbReady`'s early return is NW-56, queued W18);
  `navItems.ts`'s `getDocumentsBadge` (`:99-107` — NW-10, queued W16); anything
  under `backend/`, `infra/` or `.github/`.

## Coding objective

**1. `App.tsx` — the third state and the router hoist.**
- Replace the synchronous `loadCurrentWorkspace()` at `:49` with an async
  resolution: on mount (and whenever the account changes) call
  `apiClient.listWorkspaces()`. `App` now has **three** branches, not two:
  *resolving* → a neutral full-page state that **must not flash the sign-in
  screen** (that reads as a logout on every reload); *no account* →
  `SignInRoute`; *account + resolved* → the shell.
- Resolution order, exactly: **empty → create form**; **exactly one row → enter
  it, no picker** (`percorso-pilota-v1.md` §2 step 1, "Se ne ha uno, entra");
  ≥2 with the session hint ∈ list → enter the hint; ≥2 with the hint ∉ list or
  absent → picker. **`hint ∉ list ⇒ discard the hint`** — that is the client
  half of NW-58's removal requirement and it needs no endpoint, no polling and
  no cache invalidation.
- **Hoist `BrowserRouter` into `App.tsx`** with a public branch: `/invite/accept`
  renders outside `AppShell` and is reachable **signed out and with no
  workspace**; `*` falls through to the existing gate. `ShellRoutes` is already
  exported at `WorkspaceShellApp.tsx:41` as a testing seam, so this is supported,
  not a rewrite. Remove the `BrowserRouter` from
  `WorkspaceShellApp.tsx:81-87`.
- Pass NW-01's `role` field into `WorkspaceShellApp` instead of calling
  `resolveWorkspaceRole()` at `:113`.
- The hoist **falsifies** `WorkspacePickerScreen.tsx:117-123`'s comment ("no
  router is mounted anywhere above this component"), which justifies the hard
  `<a href="/">` at `:124`. **Fix the comment and the link in the same task.**

**2. `routes/signin/workspaceStore.ts` — delete the list, demote the hint.**
- Delete `loadKnownWorkspaces`, `rememberWorkspace` and the
  `raffa.signin.knownWorkspaces.*` `localStorage` key prefix (`:32,88-106`).
- Delete `WorkspaceSummary`'s two invented fields: `contractCount` ("Always 0
  from this screen", `:39-45`) and `roleLabel` ("Client-side inference, not a
  server-issued claim", `:56-64`, typed as the **string literal** `"Workspace
  Admin"`).
- `raffa.signin.currentWorkspace` **survives as a session hint, demoted from
  source of truth**: `loadCurrentWorkspace` / `selectCurrentWorkspace` /
  `clearCurrentWorkspace` keep their signatures, and **nothing trusts the value
  until it is checked against the server list**.

**3. `routes/signin/WorkspacePickerScreen.tsx` — the server list and the real
create form.**
- Read `apiClient.listWorkspaces()`; render the server rows. It is this screen's
  **first network read**, so add the two states it has never had: a **loading
  skeleton** and **error + Retry** that **never** falls back to a cached list
  (ADR-018 `:115,117`). The **create form is the empty state** — there is no
  separate "you have no workspaces" screen — and **200 + `[]` is not an error**.
- Stop writing a `WorkspaceSummary` on create (`:36,78-85`); the create 201 is no
  longer a cache seed.
- **The create form adopts `markup.html:52-57` whole and in order**:
  `Create your workspace` / "A workspace is your tenant. Contracts uploaded here
  never leave it." / **Company · Industry · Country** / `Create workspace`,
  replacing today's single field labelled "Workspace name" (`:189-212`, label
  `:192`). **The label is "Company"; the field is `name`** — do not rename the
  field to match the label. Keep the tenancy sentence: it is the only place in
  the product that tells a user their contracts stay in their tenant.
- Both lists ship **verbatim**: Industry is exactly the five options of
  `markup.html:55`, ending in `Other`, and **`Other` is the "not specified"
  case** — do **not** add a leading "Not specified" option, which the export does
  not contain. Country is exactly Switzerland · Italy · Germany · Austria
  (`:56`), which makes the currency derivation **total over the export's own
  list**, so no fallback branch and no "unknown country" state ships.
- Echo the derived currency under the Country select as a `.micro-meta` line,
  **"Amounts are shown in CHF."**, so a wrong guess is catchable *before* the
  workspace exists. The **stored** value is the server's; this line is a display
  echo of the same four-entry mapping (CH→CHF, IT→EUR, DE→EUR, AT→EUR).
- **The pick-row meta is a segment list that degrades**: up to three segments
  joined by ` · `; a null segment is **dropped** — never an empty gap, a dash or
  a placeholder — with zero and singular forms for the count
  (`No validated contracts yet` / `1 validated contract` / `N validated
  contracts`). Today the row renders **two** segments (`:173-174` appends
  `currencyRegion` as one pre-joined string); splitting it into two real fields
  is part of this item, not a refactor to skip. `app.jsx:160` is the export's own
  singular/plural idiom; `app.jsx:138` hardcodes `seededCount: 3` and therefore
  never exercises 0 or 1 — **every pre-existing `dev` workspace renders the zero
  form on day one**.
- **Divergence, named**: the third segment is the **business country name**
  (`Switzerland`), never the export's `eu-west` cloud-region slug.
- **The role tag renders the server role** (`:177`, hardcoded at `:83` today),
  reusing the existing vocabulary with **pass-through for unmodelled roles**.
  This half must **not** be cut with NW-24: the moment invitations land, an
  invited Procurement member would otherwise be told they are a Workspace Admin
  on the product's first screen after sign-in.

**4. `components/shell/workspaceRole.ts` — the parse rule.**
- Delete `resolveWorkspaceRole()` as the product path, **both halves**: the
  `?role=` query override (`:40-44`) and the `sessionStorage` mirror that
  **defaults to `"admin"`** (`:46`) for whoever is looking.
- Add `parseWorkspaceRole(wire: string)`. **The wire vocabulary is wider than the
  nav's** — `memberViewModel.ts:9-11` records that the backend also accepts
  **Legal / Finance / ReadOnly** while `navItems.ts:29` models two roles and
  NW-54 is deferred — and neither ADR-025 nor ADR-026 mentions those three, so
  the enum is unspecified. **Map any unmodelled role to the least-privileged
  modelled role and never to `admin`**: carrying today's default forward would
  re-introduce this exact bug for Legal / Finance / ReadOnly members while fixing
  it for Procurement.
- It lives **here**, not in `navItems.ts`. `WORKSPACE_ROLE_LABEL`
  (`navItems.ts:32-35`) already maps wire→label and is **untouched**.
- The role decides which affordances render and **never what is permitted** —
  the 403 is the authority, the button state is a courtesy.

**5. `components/shell/useValidatedContractCount.ts` + `AppShell.tsx` +
`navItems.ts` — one number, four consumers.**
- The hook reads NW-01's `contractCount` field instead of
  `getPortfolio(workspace.id, { pageSize: 100 })` counted client-side (`:60`,
  `:41-43`), which its own comment admits is **capped at 100** (`:35-39`, "a
  tenant with more validated contracts than this undercounts here"). Its four
  consumers stay: `AppShell.tsx:29` (rail badge `:39`, `GlobalAskBar` `:43`,
  `Outlet` context `:48`) and `routes/ask/index.tsx:93`.
- This honours `AppShell.tsx:45-47`'s own promise that "a screen never has to
  re-fetch the portfolio for a second opinion", so **picker and rail cannot
  disagree** (N8's "rail matches").
- `kbReady` (`:67`, `count > 0`) **stays computed where it is** — `ask/index.tsx`'s
  `!kbReady` early return is NW-56's bug, queued to W18.
- `useValidatedContractCount.ts:42` **stops filtering** with
  `isValidatedContractStatus`, which therefore stops being a count definition.
  Leave `routes/contracts/contractStatus.ts` in place as a row-level display /
  filter helper — `portfolioViewModel.ts:38` still needs it — and comment that
  it is **no longer a count definition**. **The two must never both produce a
  number.**
- **Scope fence**: only `buildSecondaryNavItems` (`navItems.ts:128-139`). The
  Documents badge (`getDocumentsBadge`, `:99-107`) is **NW-10, W16** and
  `navItems.ts:85-88` already names it. Do not touch it.
- `AppShell.tsx` and `WorkspaceShellApp.tsx` also stop calling
  `loadCurrentWorkspace()` themselves (`useValidatedContractCount.ts:54`, effect
  keyed on `workspace?.id` at `:76`) and read the one resolved value.

**6. `routes/invite/accept/index.tsx` — screen 11, the public accept route.**
- Read the token from the **URL fragment** on mount, hold it in **memory for one
  mount**, **clear the address bar**, and send it only in the
  `X-Invitation-Token` header via `apiClient.getInvitation` /
  `apiClient.acceptInvitation`. **Never persist it** — no `sessionStorage`, no
  `localStorage`, and **not MSAL's `state`** (it round-trips through the identity
  provider, placing the token in an Entra authorize URL's query string and logs).
- MSAL is **redirect-only** with a single configured `redirectUri`
  (`msalConfig.ts:19-33`; `loginRedirect` at `routes/signin/index.tsx:34`), so a
  signed-out invitee does **not** come back holding anything. The guaranteed w14
  flow is **sign in, then open the invitation link again**; use `loginPopup`
  here to make it single-click where the browser allows it, because the page is
  never unloaded and memory survives. The popup-blocked and reload cases share
  one state, so that state ships either way.
- **A reload or a sign-out loses a live token by design** (the address bar was
  cleared), so the copy is "**open your invitation link again**" — a **normal
  outcome, not an error**. Never "this invitation is invalid": that sends a user
  back to their Admin for a replacement they do not need, and invitations are
  single-use so that "fix" costs a real one.
- **Pre-accept** shows the workspace name and the offered role **and nothing
  else** — adding "3 validated contracts" for warmth would leak tenant data to
  anyone holding a URL.
- **The wrong-account state cannot echo the invited address** (the 403's reason
  is non-echoing and the pre-accept payload does not carry it).
- **Expired and revoked share a first sentence**, so the screen never tells a
  prober which case they hit. A 503 on the token lookup gets ADR-018's **error**
  treatment, which terminal informational states do not satisfy.
- Reuse the export's idioms: the Entra CTA copy `markup.html:44` **verbatim**,
  the "Signed in as {email}" kicker (`markup.html:51,60`; already shipping at
  `WorkspacePickerScreen.tsx:148`).
- After a successful accept, **re-resolve the workspace list and enter** —
  membership is proven by `GET /api/workspaces`, not trusted from the accept
  body, which is a hint.

**7. e2e.** Rewrite the two header comments that assert the opposite of this
wave: `day1.spec.ts:33-41` ("must create its own workspace — it has no way to
discover the ADR-022 fixture-seeded tenant") is **false after this task**, and
`v2.spec.ts:68-79` ("the documented seam, not a mock") — the seam **survives**,
but only for a tenant the caller is a **member** of. Add:
- **N2**: sign in in a **fresh browser context** (no `storageState`) and assert
  `.workspace-row` with **no create step**.
- **N4**: reload with `sessionStorage` cleared and the MSAL account intact → the
  shell mounts on `/ask`, `.shell-rail-workspace-name` unchanged
  (`day1.spec.ts:132`'s own selector), no picker.
- **N8**: after the existing upload step, return to `/signin` and assert
  `.workspace-row-meta` does **not** match `/^0 contracts/`, then assert the
  rail's secondary badge shows the same number.

## Parent story AC covered

- AC-1 … AC-10

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/App.tsx` | the third (resolving) state; async resolution from `listWorkspaces()`; the `BrowserRouter` hoist with the public `/invite/accept` branch; pass the server `role` |
| `web/src/components/shell/WorkspaceShellApp.tsx` | drop its `BrowserRouter`; add `workspaceId` to the props; pass `workspaceId` + `role` to `MembersRoute`; remove the `RequireRole` wrap from the `workspace/members` route |
| `web/src/components/shell/AppShell.tsx` | consume the one resolved workspace and the server count |
| `web/src/components/shell/useValidatedContractCount.ts` | read NW-01's `contractCount`; drop the capped `getPortfolio` count; keep `kbReady` where it is |
| `web/src/components/shell/navItems.ts` | `buildSecondaryNavItems` reads the server count. **`getDocumentsBadge` untouched (NW-10, W16)** |
| `web/src/components/shell/workspaceRole.ts` | delete `resolveWorkspaceRole` as the product path; add `parseWorkspaceRole` defaulting to least privilege |
| `web/src/routes/signin/workspaceStore.ts` | delete `knownWorkspaces`, `contractCount`, `roleLabel`; demote `currentWorkspace` to a hint |
| `web/src/routes/signin/WorkspacePickerScreen.tsx` | server list; skeleton + error/Retry; the Company · Industry · Country form; the degrading segment meta; the server role tag; fix the `:117-124` comment and link |
| `web/src/routes/invite/accept/index.tsx` | new — screen 11, ten states, fragment token, memory-only |
| `web/src/routes/contracts/contractStatus.ts` | comment only: it is no longer a count definition (still a row-level helper) |
| `web/e2e/day1.spec.ts` | rewrite the false premise at `:33-41`; add N2, N4, N8 |
| `web/e2e/v2.spec.ts` | rewrite the `:68-79` comment — the seam survives only for a tenant the caller is a member of |
| `web/src/routes/signin/WorkspacePickerScreen.test.tsx` | new/extended — the segment contract, the states, the role tag |
| `web/src/components/shell/workspaceRole.test.ts` | new — the least-privilege parse |
| `web/src/routes/invite/accept/acceptInvitation.test.tsx` | new — the ten states, the fragment read, the cleared address bar |

## Context the implementer needs

- **Design oracle**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §1
  (`:13-21`) and `ia-v2.md` route map (`:44-56`, `/signin` "Entra sign-in →
  workspace create/pick → lands on Ask", `:46`). **Anchors**: `markup.html:52-57`
  (create form, both option lists verbatim), `markup.html:62-64` (the pick row's
  two-cell grid; `:63` the meta, `:64` the role tag), `markup.html:44` (the Entra
  CTA copy), `markup.html:51,60` (the "Signed in as {email}" kicker),
  `app.jsx:138-139` (`signCreate` / `signPick` / `enterWs`), `app.jsx:140` (the
  role tag), `app.jsx:160` (the singular/plural idiom). **There is no accept
  screen anywhere in the V2 export** — screen 11's ten states are ADR-020's w14
  footer, decided from ADR-019's locked catalogue; six Claude Design exports are
  recorded as owed and **none is blocking**.
- One unrecorded divergence is named rather than left in the code:
  `WorkspacePickerScreen.tsx:100-137`'s "You're in {name}" interstitial exists
  nowhere in the export (`app.jsx:139` goes straight to Ask). Whether it survives
  for a **single-membership** user is this task's routing rule — it must not
  stand between "one membership" and "entra".
- `web/package.json` has **no `lint` or `typecheck` script**: type checking is
  folded into `build` (`generate:api && tsc --noEmit && vite build`).
- The **50-candidate cap** on `GET /api/workspaces` means the picker renders what
  arrives with **no pagination**; if truncation is ever reachable it must be
  visible, never silent.
- **Do not touch**: `web/src/routes/workspace/members/**`,
  `web/src/components/shell/RequireRole.tsx`, the three API-contract files,
  `web/src/routes/ask/**`, `getDocumentsBadge`, `backend/**`, `infra/**`,
  `.github/**`.

## Definition of done

- [ ] `cd web && npm ci` exits 0
- [ ] `cd web && npm run build` exits 0 (`generate:api && tsc --noEmit && vite build`)
- [ ] `cd web && npm test` exits 0
- [ ] `cd web && npx playwright test e2e/day1.spec.ts` exits 0 against `dev`
      (cases needing a second Entra account are `test.skip` with a **named**
      reason, never a silent gap — `v2.spec.ts:107-112`'s own pattern)
- [ ] `rg -n "knownWorkspaces|roleLabel|contractCount: 0" web/src` returns nothing
- [ ] `rg -n "resolveWorkspaceRole" web/src` returns nothing outside a comment
- [ ] `rg -n "sessionStorage|localStorage" web/src/routes/invite` returns nothing

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit (vitest) | the resolution order: empty → create; one → enter; ≥2 + hint ∈ list → enter; hint ∉ list → discarded → picker | `web/src/routes/signin/WorkspacePickerScreen.test.tsx` |
| unit (vitest) | the segment contract: null segments dropped; zero / singular / plural forms; the country **name**, never `eu-west` | `web/src/routes/signin/WorkspacePickerScreen.test.tsx` |
| unit (vitest) | skeleton, error + Retry with **no** cached fallback, create-form-as-empty-state | `web/src/routes/signin/WorkspacePickerScreen.test.tsx` |
| unit (vitest) | `parseWorkspaceRole` maps `Legal` / `Finance` / `ReadOnly` / anything unknown to the least-privileged modelled role, never `admin` | `web/src/components/shell/workspaceRole.test.ts` |
| unit (vitest) | the accept screen's ten states; the token is read from the fragment, the address bar is cleared, nothing is persisted | `web/src/routes/invite/accept/acceptInvitation.test.tsx` |
| e2e | **N2** fresh context → `.workspace-row`, no create step | `web/e2e/day1.spec.ts` |
| e2e | **N4** reload with `sessionStorage` cleared → shell on `/ask`, no picker | `web/e2e/day1.spec.ts` |
| e2e | **N8** picker meta ≠ `/^0 contracts/` and the rail badge shows the same number | `web/e2e/day1.spec.ts` |

## Open questions blocking this task

- **OQ-w14-cl-01** — answered: fragment → memory for one mount →
  `X-Invitation-Token`; MSAL `state` rejected. Not blocking.
- **OQ-w14-client-02** — the V2 suite needs a membership row for the e2e
  account. Delivered in phase 1 by `E14/F05/US01/T01`. If it is missing at run
  time, **HALT** and name that task rather than re-adding a client-side cache.

## Wave-spec entry
```yaml
- id: E14/F03/US02/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-03-workspace-directory/us-02-signin-resolves-from-server/tasks/task-01-web-workspace-resolution.md
  produces: [web-workspace-resolution]
  depends_on: [workspaces-directory-api, admin-role-from-membership, invitation-lifecycle-api]
  effort: L
  layer: frontend
  status: live
```
