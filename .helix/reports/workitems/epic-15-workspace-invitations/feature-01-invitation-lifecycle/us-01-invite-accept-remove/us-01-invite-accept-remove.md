---
id: us-01
type: user-story
parent: feature-01
wave: w14
status: active
---

# us-01-invite-accept-remove — an invitation is an offer until it is accepted, and removal is immediate

## Story

As a **Workspace Admin**, I want to invite a colleague with a single-use link
that makes them a member only when they click it and sign in, and to remove them
so that their access is gone on their very next request, so that the roster and
the grants it describes can never disagree.

## Acceptance criteria

- [ ] AC-1 (**N3b-1**) `POST /api/workspaces/{tenantId}/invites` as an Admin →
      `201 { id, email, role, expiresAt, acceptUrl, mailDelivered }`. It writes
      `workspace_user` + a `workspace_invitation` row and **no membership**.
      `acceptUrl` is **site-relative**: `/invite/accept#<token>`.
- [ ] AC-2 (**N3b-2**) An unaccepted invitation grants **nothing**: the invited
      identity's `GET /api/workspaces` does not list that tenant, and every
      tenant-scoped read in it is 404.
- [ ] AC-3 (**N3b-3**) `GET /api/invites` with the `X-Invitation-Token` header
      returns `{ workspaceName, role, expiresAt }` and **nothing else** — not the
      invited email, no roster, no counts.
- [ ] AC-4 (**N3b-4**) `POST /api/invites/accept` with the token and an identity
      whose email matches the invited address (case-insensitively) → `200
      { workspaceId, workspaceName, role }`, one membership row, `accepted_at`
      stamped, in **one transaction**. A mismatch → **403** whose reason never
      echoes the address.
- [ ] AC-5 (**T11**) Expired → **410**; unknown / revoked / malformed → **404**;
      a second accept → **409**; two concurrent accepts → exactly one membership
      row **and a 409, not a 500**.
- [ ] AC-6 (**T12**) A token whose prefix is not a valid Guid → **404** with
      **no scope entered**; a valid foreign tenant prefix with a wrong secret →
      **404** with **no workspace name disclosed**.
- [ ] AC-7 (**N3b-5**, **T7**) `DELETE /api/workspaces/{tenantId}/members/{membershipId}`
      as an Admin → 204; the removed identity's next `GET /api/workspaces` no
      longer lists the tenant although their `workspace_user` row still exists;
      their next document read/write in it → 404; **their Ask call scoped to that
      tenant retrieves nothing from its corpus**; and their unaccepted
      invitations in that tenant are revoked **in the same transaction**, so a
      stale link → 404.
- [ ] AC-8 (**N3b-6**, **T8**) Re-invite after removal succeeds at the same role
      and the new token differs from the revoked one.
- [ ] AC-9 (**N3b-7**, **T9**) The sole Admin removing themselves → **409**; the
      sole Admin removing the only other Admin → 409; with two Admins either
      removal → 204. The guard is a **pure domain function** provable without a
      database.
- [ ] AC-10 (**N3b-8**, **T10**) The stored column is a **hash**, not the token;
      the token appears in no audit row and no log sink; the accept request
      carries it in a header, never a query string.
- [ ] AC-11 `DELETE /api/workspaces/{tenantId}/invites/{id}` as an Admin revokes
      an `Invited` row → 204; the link stops working.
- [ ] AC-12 `mailDelivered` is a **server fact**. In w14 it is `false` by
      construction (`NullInvitationMailer`), so nothing downstream may claim a
      mail was sent.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `workspace-schema` (E14/F01/US01/T02) | the `workspace_invitation` table, its `tenant_isolation` policy and the partial unique index |
| `workspaces-directory-api` (E14/F03/US01/T01) | the phase-2 contract owner; this story is the phase-3 contract owner and appends to what it left |
| `workspace-roster-api` (E14/F04/US01/T01) | `WorkspaceMembersEndpointExtensions.cs` and `WorkspaceMembershipService.ListMembersAsync`, both of which this story extends |

## Architecture decisions in force

- **ADR-025 §C** — 256-bit CSPRNG secret, base64url, stored **only** as a
  SHA-256 hash (a database read cannot mint an acceptance); 7-day absolute
  expiry; single use; **no constant-time compare** (index seek, no
  secret-to-secret comparison — recorded so nobody "hardens" it into a table
  scan); **no server-held key, in w14 or after** — which is the condition
  cloud-architect's zero-delta rests on.
- **ADR-026 §D4 + ADR-025 Rules C4 / C5** — the token is
  `{tenantId:N}.{secret}`. The prefix is **`Guid.TryParseExact(…, "N")`-parsed
  before it reaches `BeginScope`** and a failure is **404, never a scope** — the
  type system already enforces it (`ITenantContext.BeginScope(TenantId)`;
  `readonly record struct TenantId(Guid Value)`) and **no string-taking overload
  may be added**. Inside that scope the **token-hash match is the first
  statement** — nothing is read until it succeeds, or pre-accept becomes a
  workspace-name oracle for any guessed tenant id.
- **ADR-009 (w14 footer clause 4)** — this is the second and last named
  exception to one-scope-per-request.
- **ADR-025 §D.5b** — removal deletes the **membership**, never the user (the FK
  is `ON DELETE CASCADE` from user to membership,
  `WorkspaceMembershipConfiguration.cs:30-33`). Removal is **immediate because
  nothing caches authorization**: role and membership are read from the database
  on every request, so there is no session and no token to revoke.
- **ADR-025 §G** — nine audit actions, **no schema change**. The token, its hash
  and raw identity headers are **never** written to an audit row or a log.
- **ADR-026 §D6** — `IInvitationMailer.TrySendAsync` + `mailDelivered` on the
  201 make "must not say sent" a **server fact in code** rather than a
  convention, and make the wave land whole whether or not a transport ships. Any
  `Invitations__*` configuration binds like `ConnectionStrings:Market`
  (`Program.cs:122-128`, optional, no `?? throw`), not like the nine fail-fast
  guards.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | The invitation lifecycle API: issue, pre-accept, accept, revoke, remove, and the phase-3 contract | L | phase-3 |

## Council decisions carried into this story

- **Membership moves to accept time.** `InviteAsync` today writes a live
  membership at invite time, so once discovery is keyed on identity an invitee
  would hold full access **without ever clicking the link**, and any address an
  Admin typos becomes a member. Invite now writes `workspace_user` (**no**
  membership) + the invitation row.
- Invite must still write `workspace_user`: without it
  `WorkspaceSignIn.ResolveSignedInUser` fails on **every** accept
  (`WorkspaceSignIn.cs:36-41`, "sign-in cannot provision a new workspace user")
  **and** the roster could not render `Invited`.
- The token travels in an **`X-Invitation-Token` header**, never a path or query
  string (query strings land in access logs, `Referer` headers and browser
  history), so the two `/api/invites` routes are **not** token-parameterised.
- Concurrency is **already schema-enforced**:
  `ix_workspace_membership_workspace_user_id_workspace_role_id`
  (`identity-workspace.sql:90`) means two concurrent accepts produce one row and
  one unique violation — **the real requirement is translating it to 409, not
  500**.
- `LinkSignInAsync:118-145` is **unchanged** and is called by accept.

## Open questions

- **OQ-w14-002** — is a real email transport in scope for w14? **Answered at the
  table**: no. The lifecycle is IN and stays `must`; the transport is deferred
  (ACS Email + Azure Managed Domain, recorded in ADR-005's w14 footer so the
  transport wave *applies* a design instead of re-opening one). w14 ships
  `NullInvitationMailer` and `mailDelivered: false`.
- **OQ-sec-001** — the invite guard must merge before NW-01. **Closed**: it
  shipped in phase 1 with `E14/F02/US01/T01`.
