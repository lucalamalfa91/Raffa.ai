---
id: epic-14
type: epic
wave: w14
status: active
extends: [epic-01 F05, epic-06 F03, epic-06 F04]
---

# epic-14-workspace-identity — the workspace is a server fact

## Business capability

A person who signs in finds the workspaces they belong to, from any browser and
any device, because membership lives in Postgres under RLS and not in one
browser's `localStorage`. Creating a workspace makes the creator its Admin.
The picker shows the real number of validated contracts, the real role and the
workspace's own country and currency. Admin-only actions (delete a document,
retry an upload) succeed for the person who actually is an Admin and keep
failing for everyone else — decided from the membership row, never from a
header the browser can type.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/next-waves-todo.md` §1 | NW-01, NW-02, NW-03, NW-04, NW-09, NW-14 |
| `inputs/next/next-waves-todo.md` §3 | NW-24 (workspace currency / region, HITL) |
| `reports/context/waves/w14-requirements.md` | W14-01 (wave base, operator act at HITL — no task) |
| spec §3.1 / §3.2 | roles table; "no cross-tenant query path is acceptable" |
| spec §20 Day 1 | "Create a workspace and invite Procurement users" |
| `inputs/percorso-pilota-v1.md` §2 step 1 | "Se non ha workspace: form nome / industria / paese. Se ne ha uno, entra" |
| ADR-025, ADR-026 | membership authorization; discovery / roster API contract |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | Tenant RLS identity seam and the w14 schema | w14 |
| feature-02 | Workspace bootstrap and Admin authority | w14 |
| feature-03 | Workspace directory (list, count, profile, sign-in resolution) | w14 |
| feature-04 | Workspace roster | w14 |
| feature-05 | Membership seed and backfill (operator path) | w14 |
| feature-06 | w14 integration | w14 |

## Success looks like

- Sign in from a second browser with cleared storage → the same workspace, no
  second create (N2). Close the tab and reopen → the same tenant (N4).
- `GET /api/workspaces` returns only the caller's tenants; a crafted
  `X-Tenant-Id` has nowhere to enter (N5).
- The picker row reads "N validated contracts · CHF · Switzerland" with a real
  N, a real currency and a real role tag (N8, W14-A2).
- The workspace creator deletes a document (204) and retries a failed upload
  (200); a Procurement member still gets 403 (N9).
- Members matches Postgres after a reload and from a second browser (N3).

## Architecture decisions in force

- ADR-009 — tenancy / RLS (w14 footer: the `identity_self` policy, verify-then-scope, bootstrap)
- ADR-025 — workspace membership authorization and the invitation lifecycle (new)
- ADR-026 — workspace discovery, roster and invitations: API contract, data model, module composition (new)
- ADR-022 — day-1 demo auth (w14 footer: `X-Role` / `X-Workspace-Role` demoted; membership is the role source of truth)
- ADR-003 — PostgreSQL (w14 footer: `industry` / `country` / `currency` columns)
- ADR-012 — web stack (w14 footer: a client store never stands in for a missing GET)
- ADR-018 / ADR-019 / ADR-020 — route map, design system, screen inventory (w14 footers: screen 1 states, screen 10, screen 11)
- ADR-010 — Entra / OIDC: the target, **not** wired in w14. NW-05 is queued to W15
- ADR-014 / ADR-016 — wave base and integration branch; promotion `dev` → `demo`

## Out of scope

- **API JWT (NW-05)** and everything keyed on it (NW-06, NW-07, NW-08, NW-31,
  NW-32) — queued to W15. w14 uses the interim `X-User-Id` behind one
  `ICallerIdentity` seam (OQ-w14-001).
- **A mail transport** — decided and deferred (ADR-005 w14 footer); w14 ships
  `IInvitationMailer` + `NullInvitationMailer`. See epic-15.
- **`WorkspacePrincipalAuthorization.cs`** — no w14 task edits it (ADR-025 §I).
- The Documents rail badge (`getDocumentsBadge`, NW-10) — queued to W16.
- Role change / promotion affordances — no such control ships in w14.
