---
id: feature-01
type: feature
parent: epic-15
wave: w14
status: active
extends: epic-01 F05 (identity-workspace: invite + sign-in link)
---

# feature-01-invitation-lifecycle — issue, accept, revoke, remove, re-invite

## Slice

Extends **epic-01 F05**, and supersedes one of its tasks. Today
`POST /api/workspaces/{tenantId}/invites` calls
`WorkspaceMembershipService.InviteAsync`, which writes `workspace_user`
(`:80-88`) **and a live `workspace_membership`** (`:98-105`) — no token, no
expiry, no accept URL — and a repeat invite at the same role fails at `:90-96`.
`WorkspaceSignIn.cs:28-44` and `LinkSignInAsync:118-145` implement the "first
sign-in after being invited" link, but **no host endpoint calls them** (they
appear only in `WorkspaceSignInTests.cs`). There is **no membership DELETE**
anywhere (`MapDelete` appears only at `DocumentsEndpointExtensions.cs:87`). And
no mail transport exists: a repo-wide grep for
`smtp|sendgrid|MailKit|Graph…sendMail|IEmailSender|communication.services` over
`backend/src` and `infra` returns **zero** files.

This feature makes membership happen **at accept**, gives the invitation a
signed single-use expiring token, adds revoke, remove and re-invite, and puts
the transport behind `IInvitationMailer` so the wave lands whole with or without
a mail service.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Invite issues a token; accept joins that workspace; remove revokes immediately | w14 |

## Architecture decisions in force

- ADR-025 §C (256-bit CSPRNG, SHA-256 at rest, 7-day absolute expiry, single use, no server-held key — in w14 or after; Rules C4 / C5 bound the token prefix), §D.1 / §D.3 / §D.5, §G (nine audit actions, no schema change), §H (T5, T7–T12, T14)
- ADR-026 §D4 (the `{tenantId:N}.{secret}` token shape), §D5 (endpoint contracts), §D6 (`IInvitationMailer` / `NullInvitationMailer`, `mailDelivered`), implications 3, 5, 7
- ADR-009 (w14 footer clause 4) — the accept path's caller-supplied scope is the **second and last** named exception
- ADR-011 (w14 footer) — no Key Vault entry for the token; authz-before-retrieval; "removed but Ask still answers" gets its own test
- ADR-005 / ADR-006 (w14 footers) — the mail transport is **decided and deferred**; w14's Azure delta is zero

## Target repo

`raffa-backend` (this monorepo: `backend/`, plus the OpenAPI/generated client
under `web/`)
