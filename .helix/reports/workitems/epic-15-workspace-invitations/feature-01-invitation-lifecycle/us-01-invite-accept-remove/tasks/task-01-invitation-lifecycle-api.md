---
id: E15/F01/US01/T01
type: task
story: us-01-invite-accept-remove
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-invitation-lifecycle-api — issue, pre-accept, accept, revoke, remove, the mailer seam, and the phase-3 API contract

## Context

**Closes: NW-58** (the backend half — the whole lifecycle).
Decision row: `reports/architecture/waves/w14.md`, row **NW-58** (all seven
seats). ADRs in force: **ADR-025 §C / §D.1 / §D.3 / §D.5 / §G / §H (T5, T7–T12,
T14)**, **ADR-026 §D4 / §D5 / §D6 and implications 3, 5, 7**, **ADR-009 w14
footer clause 4**, **ADR-011 w14 footer**, **ADR-005 / ADR-006 w14 footers**
(the transport is decided and deferred; w14's Azure delta is zero).

**This task owns, in phase 3**: `WorkspaceInvitesEndpointExtensions.cs`,
`WorkspaceMembersEndpointExtensions.cs`, `WorkspaceMembershipService.cs`, the new
`InvitationsEndpointExtensions.cs`, the **one** `Program.cs` line ADR-026
implication 5 budgets for the wave, and the three API-contract files
(`web/openapi/raffa-api.v1.json`, `web/src/api/generated/schema.ts`,
`web/src/api/client.ts`).

- **Architecture decisions in force**: ADR-025 (§C token, §D lifecycle, §G nine
  audit actions with **no schema change**, §H the tests), ADR-026 (§D4 shape,
  §D5 contracts, §D6 the mailer seam), ADR-009 w14 footer clause 4, ADR-011 w14
  footer.
- **Do not touch**: `Migrations/**` and `identity-workspace.sql`
  (`E14/F01/US01/T02` owns them for the wave — the table, its policy and its
  three indexes already exist; **this task adds no migration**);
  `TenantRlsConnectionInterceptor.cs` (`E14/F01/US01/T01`);
  `WorkspaceEndpointExtensions.cs` and `WorkspaceDirectoryService.cs`
  (`E14/F03/US01/T01`); `WorkspaceRoleResolver.cs` (`E14/F02/US02/T01`);
  `backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
  (**ADR-025 §I — no w14 task edits it**, and doing so would ship a
  stale-authorization window in W15); `infra/**` and every
  `.github/workflows/*.yml` (**zero delta**; `E14/F05/US01/T01` owns the only two
  workflow changes in this wave); anything under `web/src` other than the two
  contract files.

## Coding objective

**1. `WorkspaceInvitationService`** — new, in
`backend/src/Raffa.Identity.Workspace/Infrastructure/`.
- **Token**: `{tenantId:N}.{secret}` where `secret` is a **256-bit CSPRNG** value
  (`RandomNumberGenerator`), base64url. Store **only** `SHA-256(secret)` in
  `token_hash`. **No server-held key, in w14 or after** — verification is a hash
  lookup against a row that must exist anyway. Do **not** add an HMAC, a Key
  Vault secret or an `Invitations__TokenSigningKey`; that was proposed and
  **withdrawn** at the table (rotation would invalidate every outstanding
  invitation, and it would cost an env var, hence Terraform plus a human-gated
  HCP apply mid-wave).
- **No constant-time compare** — this is an index seek, not a secret-to-secret
  comparison. Comment it so nobody "hardens" it into a table scale scan.
- 7-day **absolute** expiry, evaluated against the existing `IClock`. Single use.

**2. `POST /api/workspaces/{tenantId}/invites`** (changed) in
`WorkspaceInvitesEndpointExtensions.cs` — keep the phase-1 guard
(401 → 404 → 403) exactly as it is. The handler now:
- writes `workspace_user` (**no membership**) + one `workspace_invitation` row.
  **Membership is written only at accept.** Without the user row,
  `WorkspaceSignIn.ResolveSignedInUser` fails on every accept
  (`WorkspaceSignIn.cs:36-41`, "sign-in cannot provision a new workspace user")
  and the roster could not render `Invited`.
- returns `201 { id, email, role, expiresAt, acceptUrl, mailDelivered }` where
  **`acceptUrl` is site-relative**: `/invite/accept#<token>`. **An absolute URL
  is forbidden in w14** — it would force a new `Invitations__AcceptUrlBase` env
  var onto the API app, and `backend.yml` deploys with
  `az containerapp update --image` **only** (`:198-201`, `:205-208`), so that key
  could arrive only through Terraform plus a human-confirmed HCP apply. The
  absolute URL is a property of the **mailer** (which has no browser to resolve a
  relative path against), not of the invitation.
- calls `IInvitationMailer.TrySendAsync`; its `bool` result **is**
  `mailDelivered`.
- **Behaviour change to a service with existing passing tests**: `InviteAsync`
  in `WorkspaceMembershipService.cs` stops writing a membership. Update its
  tests; do not delete them. `WorkspaceMembershipService.cs:93-96`'s "already
  holds the {role} role in this workspace" rejection must now be evaluated
  against **live memberships and live invitations**, so a **removed** person can
  be re-invited at the same role (AC-8) while a current member still cannot.
- The partial unique index `(tenant_id, lower(email)) WHERE accepted_at IS NULL
  AND revoked_at IS NULL` keeps at most one live invitation per address per
  tenant. Translate its violation to a clean 409, never a 500.

**3. `GET /api/invites`** (new) in a new `InvitationsEndpointExtensions.cs`:
- header `X-Invitation-Token`, **never** a path or query string (query strings
  land in access logs, `Referer` headers and browser history) — which is why
  this route is **not** parameterised on `{token}`.
- Split on `.`; `Guid.TryParseExact(prefix, "N")` **before** anything else; a
  failure is **404 with no scope entered**. **No string-taking overload may be
  added to `TenantId`** — the prefix is the first caller-controlled value ever to
  reach `BuildSetCommandText`, which interpolates and justifies that *solely* by
  the Guid invariant.
- `BeginScope(tenantId)`, then the **token-hash match is the first statement**.
  Nothing is read until it succeeds, or pre-accept becomes a workspace-name
  oracle for any guessed tenant id.
- `200 { workspaceName, role, expiresAt }` and **nothing else, ever** — **not**
  the invited email (echoing it turns a leaked link into an address-discovery
  tool), no roster, no counts. Expired → **410**; unknown / revoked / accepted /
  malformed → **404**.

**4. `POST /api/invites/accept`** (new), same file:
- headers `X-Invitation-Token` + the identity through `ICallerIdentity`; absent
  identity → **401**.
- The signed-in email must match the invited address **case-insensitively**, or a
  forwarded link is an open door into a customer tenant → **403** with a reason
  that **never echoes the address**, and **no** membership row written.
- On success, **one transaction**: bind the subject via the already-written
  `LinkSignInAsync:118-145` (**unchanged**), insert the membership, stamp
  `accepted_at`. `200 { workspaceId, workspaceName, role }`.
- A second accept → **409**. Two concurrent accepts → exactly one membership row
  and a **409, not a 500**: the unique index
  `ix_workspace_membership_workspace_user_id_workspace_role_id`
  (`identity-workspace.sql:90`) already enforces the row; the requirement is
  translating the violation.

**5. `DELETE /api/workspaces/{tenantId}/invites/{id}`** (new, revoke) in
`WorkspaceInvitesEndpointExtensions.cs` — identity → membership → Admin;
stamps `revoked_at`; 204. The link stops working immediately.

**6. `DELETE /api/workspaces/{tenantId}/members/{membershipId}`** (new, remove)
in `WorkspaceMembersEndpointExtensions.cs` — identity → membership → Admin →
**last-Admin guard (409)**.
- The guard is a **pure domain function**, `WorkspaceMembershipRemoval.CanRemove`
  in `backend/src/Raffa.Identity.Workspace/Domain/`, **provable without a
  database**: the invariant is "at least one live Admin per tenant, always".
- Removal deletes the **membership**, never the user (the FK is
  `ON DELETE CASCADE` from user to membership,
  `Infrastructure/Configurations/WorkspaceMembershipConfiguration.cs:30-33` —
  deleting the user would cascade).
- It **revokes that email's live invitations in the same transaction**. Without
  that, a still-valid link re-admits them and NW-58's own "re-adding needs a new
  invite" is false.
- It is **immediate because nothing caches authorization** — role and membership
  are read from the database on every request. There is no session and no token
  to revoke.

**7. `IInvitationMailer`** (ADR-026 §D6) in
`backend/src/Raffa.Identity.Workspace/Application/`, with
`Task<bool> TrySendAsync(...)`; default `NullInvitationMailer` returns `false`
and logs. Register it in the module's own
`Infrastructure/ServiceCollectionExtensions.cs`, **never** in `Program.cs`.
**No new Azure resource, no Terraform change, no new config key, no new Key
Vault secret** — w14's delta is zero in both environments.

**8. Audit (ADR-025 §G) — nine actions, no schema change.** Write
`workspace.invitation.issued`, `.accepted`, `.rejected` (email mismatch),
`.revoked`, `workspace.membership.granted` (at accept),
`workspace.membership.removed`. **Never** record the token, the token hash, any
raw header dump, or any contract or business datum. `Detail` may carry the
invited email and the role and nothing else. These endpoints all require an
identity, so the `"unattributed"` actor literal cannot occur on any of them.

**9. `Program.cs` — exactly one line**: `app.MapInvitationEndpoints()` for the
new file, at the contended tail before `app.Run()`. `MapWorkspaceEndpoints()` is
already registered at `:225` and must stay untouched. **This is the wave's only
`Program.cs` edit after phase 1.**

**10. The phase-3 API contract.** Add the five operations above to
`web/openapi/raffa-api.v1.json` per ADR-026 §D5, regenerate
`web/src/api/generated/schema.ts` with `npm run generate:api` from `web/`, and
hand-write the client methods in `web/src/api/client.ts`:
`inviteMember(tenantId, body)` (changed response), `getInvitation(token)`,
`acceptInvitation(token)`, `revokeInvitation(tenantId, id)`,
`removeMember(tenantId, membershipId)`. `getInvitation` and `acceptInvitation`
are **hand-written wrappers** — headers and request bodies are **not** generated
(`generate-api-client.mjs:134-143` parses only `responses`) — and **never** put
`{token}` in a path. `role` and `status` stay **non-nullable strings** (the
generator checks `enum` before the nullable branch, `:63-65` precedes `:72-76`,
so a nullable enum silently loses its `null`).

## Parent story AC covered

- AC-1 … AC-12

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceInvitationService.cs` | new — token mint/hash/verify, issue, pre-accept, accept, revoke |
| `backend/src/Raffa.Identity.Workspace/Domain/WorkspaceMembershipRemoval.cs` | new — the last-Admin guard as a pure function |
| `backend/src/Raffa.Identity.Workspace/Application/IInvitationMailer.cs` | new — the seam |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/NullInvitationMailer.cs` | new — returns `false`, logs |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs` | `InviteAsync` stops writing a membership; accept-time grant; removal + same-transaction invitation revocation; the re-invite rule |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs` | register the invitation service and `NullInvitationMailer` |
| `backend/src/Raffa.Api/WorkspaceInvitesEndpointExtensions.cs` | changed `POST …/invites`; new `DELETE …/invites/{id}` |
| `backend/src/Raffa.Api/WorkspaceMembersEndpointExtensions.cs` | new `DELETE …/members/{membershipId}` with the last-Admin 409 |
| `backend/src/Raffa.Api/InvitationsEndpointExtensions.cs` | new — `GET /api/invites`, `POST /api/invites/accept` |
| `backend/src/Raffa.Api/Program.cs` | **exactly one line**: `app.MapInvitationEndpoints()` |
| `web/openapi/raffa-api.v1.json` | the five operations of ADR-026 §D5 |
| `web/src/api/generated/schema.ts` | regenerated with `npm run generate:api` |
| `web/src/api/client.ts` | the five client methods; `getInvitation` / `acceptInvitation` hand-written, token in the header |
| `backend/tests/Raffa.Api.Tests/InvitationLifecycleEndpointTests.cs` | new — T10, T11, T12 |
| `backend/tests/Raffa.Api.Tests/MembershipRemovalEndpointTests.cs` | new — T7, T8, T9 |
| `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceMembershipRemovalTests.cs` | new — the last-Admin guard, no database |
| `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceInvitationServiceTests.cs` | new — token hygiene, expiry, single use, re-invite after removal |
| `backend/tests/Raffa.Api.Tests/RemovedMemberRetrievalTests.cs` | new — T7d: a removed member's Ask call scoped to that tenant retrieves nothing (ADR-011) |
| `backend/tests/Raffa.Api.Tests/TokenIdentityRetirementTests.cs` | new — **T14**, written now and `Skip`ped with the named reason "activated by NW-05/NW-08 in W15" |

## Context the implementer needs

- The table, its `tenant_isolation` policy and its three indexes already exist
  from `E14/F01/US01/T02`. **Add no migration.** If the model needs a change the
  schema task did not make, **HALT** and name it rather than adding a fourth
  migration in a later phase — `IdentityWorkspaceMigrationScriptStaleCheckTests`
  byte-compares the checked-in script.
- The **invite endpoint's authorization guard already shipped in phase 1**
  (`E14/F02/US01/T01`, ADR-025 §D.1a / OQ-sec-001). Do not re-implement it and
  do not weaken it.
- `WorkspaceMembershipService.ListMembersAsync` was added in phase 2 by
  `E14/F04/US01/T01` and reads memberships ∪ live invitations. Your changes must
  keep it correct: after this task, `Invited` comes from the invitation row you
  write and `Active` from the membership you write at accept.
- **N3b's e2e is not this task's** — it is `E14/F06/US01/T01`'s, and it needs a
  **second Entra account**. Record nothing as proven that you did not run.
- ADR-025 §H **T14 is written now and activated in W15**: with a validated token
  present, `X-User-Id` is **ignored**, not overridden (token `A` +
  `X-User-Id: B` acts as `A`); a token carrying `roles` / `tenant_id` claims does
  **not** grant that role or tenant. Author it, `Skip` it with that reason.
- Docker Desktop does not start on the operator's machine; Postgres
  Testcontainers suites run in CI only. Do not weaken a test so it runs locally.
- **Do not touch**: `Migrations/**`, `identity-workspace.sql`,
  `TenantRlsConnectionInterceptor.cs`, `WorkspaceEndpointExtensions.cs`,
  `WorkspaceDirectoryService.cs`, `WorkspaceRoleResolver.cs`,
  `WorkspacePrincipalAuthorization.cs`, `infra/**`, `.github/workflows/**`, any
  `web/src` file other than `api/client.ts` and `api/generated/schema.ts`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/Raffa.slnx` exits 0
- [ ] `cd web && npm ci && npm run generate:api` exits 0 and leaves
      `src/api/generated/schema.ts` byte-identical to the committed file
- [ ] `cd web && npm run build` exits 0; `cd web && npm test` exits 0
- [ ] `rg -n "token" backend/src/Raffa.Identity.Workspace --glob '!*Tests*' -i`
      shows no path that writes a raw token or its hash to an audit row or a log
- [ ] `rg -n "\{token\}" web/src/api/client.ts web/openapi/raffa-api.v1.json`
      returns nothing — the token is never in a path or query string
- [ ] `git diff --stat origin/main -- infra .github/workflows` shows **no**
      change from this task
- [ ] `rg -n "MapInvitationEndpoints|MapWorkspaceEndpoints" backend/src/Raffa.Api/Program.cs`
      shows exactly two calls, one of them unchanged
- [ ] `git diff origin/main -- backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
      is empty

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | **T11** — email mismatch → 403 with no membership written; expired → 410; unknown/revoked/malformed → 404; second accept → 409; two concurrent accepts → one row + 409, not 500; `LinkSignInAsync` finds the `workspace_user` row the invite wrote | `backend/tests/Raffa.Api.Tests/InvitationLifecycleEndpointTests.cs` |
| integration | **T12** — a non-Guid prefix → 404 with no scope entered; a foreign tenant prefix + wrong secret → 404 with no workspace name disclosed | `backend/tests/Raffa.Api.Tests/InvitationLifecycleEndpointTests.cs` |
| integration | **T10** — the stored column is a hash; the pre-accept response carries only name/role/expiry; the token is in a header, never a query string; it appears in no audit row | `backend/tests/Raffa.Api.Tests/InvitationLifecycleEndpointTests.cs` |
| integration | **T7** — after removal: no longer listed although the user row exists; document read/write → 404; the stale link → 404 | `backend/tests/Raffa.Api.Tests/MembershipRemovalEndpointTests.cs` |
| integration | **T7d** — a removed member's Ask call scoped to that tenant retrieves nothing from its corpus (ADR-011) | `backend/tests/Raffa.Api.Tests/RemovedMemberRetrievalTests.cs` |
| integration | **T8** — re-invite after removal succeeds at the same role and the new token differs from the revoked one | `backend/tests/Raffa.Api.Tests/MembershipRemovalEndpointTests.cs` |
| unit | **T9** — the last-Admin guard, with no database | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceMembershipRemovalTests.cs` |
| unit | 256-bit CSPRNG, SHA-256 at rest, 7-day absolute expiry, single use; `NullInvitationMailer` → `mailDelivered: false`; `acceptUrl` is site-relative | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceInvitationServiceTests.cs` |
| unit (skipped) | **T14** — the ADR-010 retirement contract, activated by NW-05/NW-08 in W15 | `backend/tests/Raffa.Api.Tests/TokenIdentityRetirementTests.cs` |

## Open questions blocking this task

- **OQ-w14-002** — a real email transport. **Answered at the table**: deferred.
  Ship `NullInvitationMailer` and `mailDelivered: false`. Not blocking.

## Wave-spec entry
```yaml
- id: E15/F01/US01/T01
  prompt: reports/workitems/epic-15-workspace-invitations/feature-01-invitation-lifecycle/us-01-invite-accept-remove/tasks/task-01-invitation-lifecycle-api.md
  produces: [invitation-lifecycle-api]
  depends_on: [workspace-schema, workspaces-directory-api, workspace-roster-api]
  effort: L
  layer: backend
  status: live
```
