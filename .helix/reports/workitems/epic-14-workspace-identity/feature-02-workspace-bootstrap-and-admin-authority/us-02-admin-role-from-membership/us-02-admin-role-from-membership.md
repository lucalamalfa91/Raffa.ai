---
id: us-02
type: user-story
parent: feature-02
wave: w14
status: active
---

# us-02-admin-role-from-membership — Delete and Retry work for the workspace creator, and a typed header never grants anything

## Story

As the **workspace creator**, I want Delete and Retry upload to succeed, so that
the two row actions that have always been visible stop firing real requests that
always fail — and as the **platform**, I want a client-declared role header to
be incapable of granting an Admin-only action.

## Acceptance criteria

- [ ] AC-1 (**N9**, half 1) The workspace creator: `DELETE /api/documents/{id}`
      → **204** and the row is gone; `POST /api/documents/{id}/reprocess` → **200**
      and processing resumes.
- [ ] AC-2 (**N9**, half 2) A **Procurement** member of the same workspace still
      gets **403** on Delete and on reprocess.
- [ ] AC-3 (**T2a**) A Procurement member sending `X-Role: Admin` gets **403**
      on `DELETE /api/documents/{id}`, on `POST …/reprocess`, on
      `POST …/invites` and on `DELETE …/members/{id}`; repeat with
      `X-Workspace-Role: Admin`. A header claiming `Admin` never grants.
- [ ] AC-4 A header claiming `Procurement` never **revokes** a real Admin —
      where header and membership disagree, **membership wins in both
      directions**.
- [ ] AC-5 `/api/capabilities` keeps reading `X-Role` for UI shaping and is
      unchanged in behaviour; it is the one remaining reader, and it is
      documented there as non-authoritative
      (`CapabilitiesEndpointExtensions.cs:44-48`).

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (`workspace-bootstrap`) | N9 needs no new authorization code — once the creator's membership row exists, the existing join at `WorkspaceRoleResolver.cs:104-114` returns `Admin` and the gate passes. This is a **proof** story, not a fix story |

## Architecture decisions in force

- **ADR-022 (w14 footer)** — the interim posture **narrows**. Authorization for
  an Admin-only action resolves from role claims on an authenticated principal
  (`WorkspaceRoleResolver.cs:50-55`), then from the `workspace_membership` row
  (`:85-119`), and **nothing else**. `X-Role` / `X-Workspace-Role` (`:37-38`,
  read `:70-83`) are demoted **now** to non-authoritative UI shaping on
  `/api/capabilities` only.
- **ADR-025 §E** — a client-declared role is never an authorization source.
- **ADR-010** — `none` for w14: the JWT swap is NW-05, queued to W15.
- **R-DOC-07 / R-DOC-10** (`inputs/requirements.md` §5.1) — Delete and reprocess
  are Admin-only; that stays true.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Delete the header branch from the role resolver and prove N9 | M | phase-2 |

## Council decisions carried into this story

- **The implementation is a deletion.** `ResolveAsync`'s middle branch
  (`WorkspaceRoleResolver.cs:57-60`) is removed so the order becomes
  claims → membership, leaving `TryResolveHeaderRole` one caller
  (`/api/capabilities`).
- **This costs nothing and breaks no client**: `Grep` over `web/src` for
  `X-Role|X-Workspace-Role` returns **zero** matches — the SPA sends
  `X-Tenant-Id` + `X-User-Id` only (`client.ts:49-54`). The change restates a
  convention the codebase already holds; letting the header drift into an authz
  decision would be the regression.
- This executes NW-14's own instruction **not** to "fix" the 403 by sending a
  spoofable `X-Role: Admin` from the SPA as the product solution.
- The role decides which affordances render and **never what is permitted** —
  the 403 is the authority, the button state is a courtesy. The client half is
  `E14/F03/US02/T01`.

## Open questions

- none. OQ-w14-001's assumption (the interim `X-User-Id` as the membership key)
  is already in force from us-01.
