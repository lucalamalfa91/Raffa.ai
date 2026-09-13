---
id: feature-04
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-06 F04 (workspace members UI), epic-01 F05 (identity-workspace)
---

# feature-04-workspace-roster — the Members table is a server read

## Slice

Extends **epic-06 F04**'s members screen — whose three acceptance criteria
(members table, invite pane + roles, non-Admin state) are still exactly what the
product wants — by giving it a real data source. Today
`web/src/routes/workspace/members/memberStore.ts:57,77` reads and writes
`sessionStorage`, so the table is this session's optimistic echo of invites made
in this tab; another Admin, another browser or a reload sees a different roster
than Postgres holds. `GET /api/workspaces/{tenantId}/members` returns the roster
as **live memberships ∪ live invitations**, with `status` derived and never
stored.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | `GET /api/workspaces/{tenantId}/members` | w14 |

## Architecture decisions in force

- ADR-026 §D3 — the roster is a union of grants and offers, never a scan of `workspace_user`; `status` is derived; `name` is the stored `WorkspaceUser.DisplayName` and is **never** derived from the email
- ADR-025 §B (rejection contract), §D.4 (who may read), §H T1b — a non-member gets **404**, never 403 and never an empty 200
- ADR-009 (w14 footer clause 7) — **verify, then scope, then read**
- ADR-022 (w14 footer) — `X-Tenant-Id` is not an input to a membership route
- ADR-012 (w14 footer clause 1) — a client store never stands in for a missing GET

## Target repo

`raffa-backend` (this monorepo: `backend/`). The web half is `E15/F02/US01/T01`.
