---
id: feature-02
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-01 F05 (identity-workspace: roles, membership, invite)
---

# feature-02-workspace-bootstrap-and-admin-authority — the creator is Admin, and only the membership row says so

## Slice

Extends **epic-01 F05**. Two things that only look unrelated: creating a
workspace now writes the creator's `workspace_membership` (Admin) inside the
same tenant scope and the same `SaveChangesAsync`, and *nothing but that row*
decides an Admin-only action. `X-Role` / `X-Workspace-Role` are demoted to
non-authoritative UI shaping on `/api/capabilities` only.

Folded in here, at the delivery-manager's ordering correction: the
**authorization guard on `POST /api/workspaces/{tenantId}/invites`**.
ADR-025's OQ-sec-001 requires it to merge **before** NW-01 — this wave converts
a dormant hole (anyone who can reach the API and guesses a tenant GUID can grant
themselves Admin over another customer's contracts) into a working cross-tenant
join path. It cannot merge on its own earlier, because the guard resolves
`Admin` **from the membership row**, so before the creator has one nobody could
invite at all. Exactly one placement satisfies both, at zero task cost: the
phase-1 task that already owns `WorkspaceEndpointExtensions.cs`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Creating a workspace makes the creator its Admin, and the invite endpoint is guarded | w14 |
| us-02 | An Admin-only action resolves from the membership row, never from a header | w14 |

## Architecture decisions in force

- ADR-025 §A (the one identity seam), §B (the rejection contract: 401 / 404 / 403), §D.1 (issue), §D.2 (bootstrap), §E (the Admin gate)
- ADR-026 §D5 (the create 201 gains `role: "Admin"`), implications 5 and 6
- ADR-009 (w14 footer clause 6 — bootstrap; clause 7 — verify-then-scope)
- ADR-022 (w14 footer — the interim posture **narrows**: membership is the role source of truth; the header is never the product answer)
- ADR-010 — **not** wired in w14; NW-05 is queued to W15

## Target repo

`raffa-backend` (this monorepo: `backend/`)
