---
id: us-02
type: user-story
parent: feature-03
wave: w14
status: active
---

# us-02-signin-resolves-from-server — one membership enters, several pick, and the picker stops inventing three facts

## Story

As a **user reopening the browser**, I want to land back in my workspace instead
of a picker built from `localStorage`, so that "Se ne ha uno, entra"
(`percorso-pilota-v1.md` §2 step 1) is what actually happens — and I want the
number, the currency and the role tag on screen 1 to be facts the server holds.

## Acceptance criteria

- [ ] AC-1 (**N2**) In a **fresh browser context** (no `storageState`), signing
      in lists the workspace with **no create step** — the one assertion a
      `localStorage` list could never pass.
- [ ] AC-2 (**N4**) Reload with `sessionStorage` cleared and the MSAL account
      intact → the shell mounts on `/ask`, `.shell-rail-workspace-name`
      unchanged, **no picker**. Resolution order on every mount once
      `GET /api/workspaces` resolves: empty → create form; **exactly one row →
      enter it, no picker**; ≥2 with the session hint in the list → enter the
      hint; ≥2 with the hint absent or **not** in the list → picker.
- [ ] AC-3 `hint ∉ list ⇒ discard the hint`. That is the client half of NW-58's
      removal requirement, and it needs no endpoint, no polling and no cache
      invalidation — **revalidation is the mechanism**.
- [ ] AC-4 (**N8**) After ≥1 upload, returning to `/signin` shows a pick-row meta
      that does **not** match `/^0 contracts/`, and the rail's secondary badge
      shows **the same number**. Two assertions, one number.
- [ ] AC-5 (**W14-A2**) The create form is **Company · Industry · Country**
      (both lists verbatim from the export), the derived currency is echoed
      under Country as "Amounts are shown in CHF.", and the pick row is a
      **segment list that degrades**: up to three segments joined by ` · `, a
      null segment **dropped** (never an empty gap, a dash or a placeholder),
      with zero and singular forms for the count.
- [ ] AC-6 The pick row's role tag renders the **server** role from the
      `GET /api/workspaces` row. It no longer hardcodes "Workspace Admin".
- [ ] AC-7 (**N9**, client half) Admin-only affordances follow the server role.
      `parseWorkspaceRole(wire)` maps any **unmodelled** role to the
      **least-privileged** modelled role and **never to `admin`**.
- [ ] AC-8 Screen 1 gains the states w14 creates: **skeleton** while loading;
      **error + Retry** that never falls back to a cached list; the **create form
      as the empty state**; and a **resolving** state that must **not flash the
      sign-in screen** — that reads as a logout on every reload.
- [ ] AC-9 `/invite/accept` exists as a **public** route, reachable signed out
      and with no workspace, rendered **outside `AppShell`**, and reads its token
      from the URL **fragment**.
- [ ] AC-10 `cd web && npm run build` and `npm test` exit 0; `day1.spec.ts:33-41`
      and `v2.spec.ts:68-79`, whose header comments assert the **opposite** of
      this wave, are rewritten by this task.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `workspaces-directory-api` (E14/F03/US01/T01) | this story consumes `listWorkspaces()`. **A web task consuming an endpoint that lands in its own phase is a broken chain** |
| `admin-role-from-membership` (E14/F02/US02/T01) | the affordances this story renders are backed by a server that honours membership, not a header |
| `invitation-lifecycle-api` (E15/F01/US01/T01) | the accept screen calls `getInvitation` / `acceptInvitation` |
| `membership-seed-and-backfill` (E14/F05/US01/T01) | **a prerequisite of this story, not a follow-up**: the moment the picker reads the server list, every pre-existing `dev` workspace disappears for its creator, and the client must not soften that with a cache. It is in phase 1 for exactly this reason |

## Architecture decisions in force

- **ADR-012 (w14 footer, clauses 1, 2, 5, 7)** — a client store never stands in
  for a missing GET; the session hint and its resolution order; the client role
  is presentation-only, server-derived and defaults to least privilege; a count
  has one definition and it is the server's.
- **ADR-018 (w14 route footer)** — the locked route map gains `/invite/accept`
  (public, outside `AppShell`, signed-out reachable, fragment-carrying) and the
  `BrowserRouter` **hoist**. `:112-119` binds every list surface: skeleton, and
  error + Retry that **never** renders a cached list.
- **ADR-020 (w14 design footer, screen 1)** — the create form and its two closed
  lists, the derived-currency line, the pick-row segment contract with its
  degraded / singular / zero forms, the server-role tag, and the full state
  inventory including the previously unrecorded "You're in {name}" interstitial.
- **ADR-025 Rules C9 / C10** — the token is read from the **fragment**, held in
  **memory for one mount**, the address bar is cleared, and it is sent only in
  `X-Invitation-Token`. It is **never persisted client-side**.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | The app gate, the picker, the shell role and count, and the public accept route | L | phase-4 |

## Council decisions carried into this story

- **The real change is the app's top-level gate, not the store.** `App.tsx:49`
  calls `loadCurrentWorkspace()` **synchronously during render** and `:91`
  branches on it immediately, so today `App` renders either `SignInRoute` or
  `WorkspaceShellApp` with nothing in between. Making the workspace a server
  fact inserts an inherently async step, so `App` needs a **third state**.
- **Three frozen fields die together**: `contractCount: 0`
  (`WorkspacePickerScreen.tsx:82`), `roleLabel: "Workspace Admin"` (`:83`, typed
  as that **string literal** in `workspaceStore.ts:56-64` — so the type system
  itself asserts "every workspace you can see is one where you are Admin") and
  the `knownWorkspaces` `localStorage` array (`workspaceStore.ts:32,88-106`).
- **`WorkspacePickerScreen` has no `useEffect` today** — `:36-41` are all
  `useState` initialisers — so this is the screen's **first network read**.
- **Divergence, named**: the third pick-row segment is the **business country
  name** (`Switzerland`), never the export's `eu-west` cloud-region slug —
  printing a region slug directly beneath "Contracts uploaded here never leave
  it" reads as a data-residency promise the product has not made, and
  contradicts ADR-006's `northeurope` besides.
- **Scope fence on the rail**: only the *secondary tier* count
  (`navItems.ts:128-139`, `buildSecondaryNavItems`). The **Documents** badge
  (`getDocumentsBadge`, `:99-107`) is **NW-10, queued to W16** — `navItems.ts:85-88`
  already names NW-10 in its own doc comment. Both badges live in one file and
  only one is in scope. `kbReady` (`useValidatedContractCount.ts:67`) stays
  computed where it is: `ask/index.tsx`'s `!kbReady` early return is **NW-56's
  bug, queued to W18**.
- **`isValidatedContractStatus` (`routes/contracts/contractStatus.ts:17`) stops
  being a count definition** in w14 and survives only as a row-level display /
  filter helper (`portfolioViewModel.ts:38` still needs it). **The two must
  never both produce a number.**
- **MSAL's `state` parameter is explicitly rejected** for carrying the token — it
  round-trips through the identity provider, so it would place the token in an
  Entra authorize URL's query string and logs: strictly worse than the store it
  replaces, and the obvious shortcut a task would otherwise reach for.

## Open questions

- **OQ-w14-cl-01** — how the accept token survives sign-in. **Answered at the
  table**: fragment → memory for one mount → `X-Invitation-Token` header. MSAL
  here is **redirect-only** with a single configured `redirectUri`
  (`msalConfig.ts:19-33`), so the **guaranteed w14 flow is: sign in, then open
  the invitation link again**; `loginPopup` is the one sanctioned way to make it
  single-click, because the page is never unloaded and memory survives.
- **OQ-w14-client-02** — the V2 e2e suite pins the fixture tenant by writing the
  very `sessionStorage` key this story revalidates (`v2.spec.ts:68-79`). It goes
  red unless a membership row exists for the e2e account. **Covered** by
  `membership-seed-and-backfill` in phase 1 — a verified fact, not an assumption.
