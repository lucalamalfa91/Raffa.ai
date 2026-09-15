# ADR-026 — Workspace discovery, roster and invitations: API contract, data model and module composition

- **Status**: accepted
- **Date**: 2026-09-10
- **Deciders**: software-architect (owner); security-architect (ADR-025 — authorization, token strength, RLS policy, audit; co-signer here); client-architect (generated client and payload shape); product-owner (ADR-001 w14 scope footer); ux-ui-designer (screen 1 / screen 10 rendering); cloud-architect (mail transport, if any); delivery-manager (migration ordering in CI)
- **Wave**: w14 — "Workspace is real"
- **Items served**: NW-01, NW-02, NW-04, NW-09, NW-58 (and NW-24's columns via the ADR-003 w14 footer)
- **Locked citations**: Backend — C#/ASP.NET Core LTS; modular monolith + worker (ADR-002); PostgreSQL + EF Core/npgsql (ADR-003); RLS on every tenant table (ADR-009); CI applies checked-in idempotent SQL, never `MigrateAsync` (ADR-021). None of these is re-opened here.

## Context and problem statement

Wave w14 turns the workspace from a browser-local idea into a server fact.
Five endpoints appear or change, one table is added, three columns are added
to an existing table, and one shared-kernel component gains a second
responsibility. ADR-025 (security-architect) decides **who may do what** —
authorization order, the rejection contract, the token's strength, the RLS
policy and the audit rows. This ADR decides **what the system looks like**:
the endpoint contracts, the read models, the table shape, the module
composition and the migration mechanics.

The forcing problem is `GET /api/workspaces` (NW-01). It must answer *"which
workspaces does this caller belong to?"*, but every table that could answer it
is tenant-scoped and the RLS guard fails closed: with no `app.tenant_id` the
policy predicate `tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid`
evaluates to NULL and every row is denied
(`backend/src/Raffa.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs:15-16,53-57`;
`backend/src/Raffa.Identity.Workspace/Migrations/Scripts/identity-workspace.sql:143-182`).
`workspace_user` is itself tenant-scoped — the same person in two workspaces
is two rows, unique on `(tenant_id, email)` (SQL `:118`). So the caller's
identity is only discoverable once the tenant is already known, which is the
unknown the endpoint exists to resolve. ADR-009 forbids the two easy exits:
*"No cross-tenant query path is acceptable"* (`:12-13`) and *"No query may run
under `BYPASSRLS`/superuser in the application path"* (`:57-58`).

The second forcing problem is that the invitation flow currently grants
access at invite time (`WorkspaceMembershipService.cs:98-105` writes a live
`workspace_membership` inside `InviteAsync`), which NW-58 replaces with an
offer that is only redeemed on accept.

## Decision drivers

- ADR-009 is not negotiable: no cross-tenant read, no `BYPASSRLS`, a policy on
  every tenant table. Any shape that needs an exception must make the
  exception *narrow, named and testable*.
- API-first: the OpenAPI document is the contract, and the TypeScript client is
  generated from it. The generator's real capabilities bound what may be
  expressed (`web/scripts/generate-api-client.mjs`).
- One definition per fact. Five surfaces currently disagree about "validated
  contracts"; this wave must end with one number produced in one place.
- Module boundaries are enforced by a test, not by convention
  (`backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs:62`).
- Cheapest correct change: no new project, no new module, no denormalised
  projection to keep in step.

## Considered options

For the discovery problem (the fork that shapes the rest):

1. **`BYPASSRLS` / superuser for the discovery query** — one query, no schema change.
2. **Enumerate `workspace`, scope into each tenant, test membership** — no policy change.
3. **A cross-tenant directory table** (`identity → tenant_id`), outside the tenant axis, written alongside every membership write.
4. **One identity-keyed, `SELECT`-only RLS policy on `workspace_user`, then one tenant-scoped read per candidate.**

## Decision outcome

**Chosen: Option 4**, because it is the only shape that answers the question
without ever reading a row the caller is not entitled to and without inventing
a second source of truth for membership that would drift in the dangerous
direction ("still listed after removal").

The security-architect reached the identical conclusion independently in their
lane (ADR-025 §3.1–§3.2, option 4 of four). This ADR does not restate the
policy — **the `identity_self` policy, its fail-closed guard and the GUC
binding rule are ADR-025's and the ADR-009 w14 footer's**. What follows is the
contract and the code shape built on top of it.

### D1 — `GET /api/workspaces` is two-phase, and takes no tenant input

```
GET /api/workspaces
  headers: X-User-Id (identity; absent -> 401 per ADR-025 §B)
  200 -> { workspaces: [ { id, name, createdAt, role, contractCount,
                           country?, currency? } ] }
```

1. **Discovery** — with `app.identity_subject` set and **no** tenant scope,
   select `tenant_id` from `workspace_user` for the matching identity. Returns
   candidate tenant ids **and nothing else**.
2. **Projection** — for each candidate, `BeginScope(tenantId)` and read
   normally: the `workspace` row, the caller's role through the existing
   membership join, and the validated-contract count. Every read here is an
   ordinary, fully scoped read of a tenant the caller has provably been
   admitted to.

- **This endpoint accepts no `X-Tenant-Id`, ever.** The response is derived
  exclusively from the caller identity, so a crafted tenant header has nowhere
  to enter. That is acceptance N5 satisfied by construction rather than by a
  guard that could be forgotten.
- **Inclusion is gated on a live `workspace_membership` row, never on the
  `workspace_user` row.** Phase 1 only nominates candidates. This is what makes
  removal work: removal deletes the membership and keeps the user row for audit
  continuity, and the workspace disappears from the list on the very next
  request — no cache to invalidate, no token to expire.
- Empty array with **200** when the caller belongs to nothing — never 404. The
  SPA must distinguish "you have none, create one" from "the request failed".
- Order by `createdAt` ascending, stable, so the picker does not reshuffle
  between loads.
- Implemented by a new `WorkspaceDirectoryService` in
  `Raffa.Identity.Workspace/Infrastructure/`, exposing
  `ListForIdentityAsync(identity, ct)`. It owns both phases' identity-side
  reads and **does not know what a contract is**.

### D2 — the validated-contract count is the definition the server already holds

`Contract.Status` is free text whose bootstrap value is the literal
`"processing"` (`Contract.cs:22`, `StagedExtractionService.cs:157`), so it can
never define "validated". The backend's real rule already exists:
**a contract is validated iff at least one linked document is
`ProcessingStatus.Completed`** (`PortfolioQueryService.cs:191`), and it is
already served as `contractsAnalyzedCount` on `GET /api/savings/kpis`
(`SavingsKpiEndpointExtensions.cs:93`), which no UI consumes today.

- That is the one definition. No new one is invented, and the client-side
  free-text heuristic (`web/src/routes/contracts/contractStatus.ts:17-21`)
  stops being a *definition* — the client-architect owns its removal.
- `contractCount` is a **field on the NW-01 row**, not a second round-trip per
  workspace.
- **Composition happens in the host, not in a module.**
  `Raffa.Identity.Workspace` may reference only `Raffa.SharedKernel`
  (`DependencyDirectionTests.cs:62`), so it cannot see `Documents.Contracts`.
  `Raffa.Api` is the one project allowed to see both; the endpoint handler
  calls `WorkspaceDirectoryService` and the Documents.Contracts query service
  and joins them in the response projection. Precedent for exactly this shape:
  `SavingsKpiEndpointExtensions.cs:73-88`. **Adding a SharedKernel port for
  this is explicitly rejected** — it would be legal but it touches a shared
  file for a composition the host already performs elsewhere.
- The counting method must be a real SQL `CountAsync` over distinct contract
  ids, not `ToListAsync().Count`. There is no `CountAsync` over `Contracts`
  anywhere today (`PortfolioQueryService.cs:153,183-200` materialise), so this
  task introduces the first one and must not copy the materialising pattern.

### D3 — the roster is a union of grants and offers, never a scan of `workspace_user`

```
GET /api/workspaces/{tenantId}/members
  headers: X-Tenant-Id + X-User-Id
  200 -> { members: [ { id, email, name?, role, status } ] }
```

- `status` is **derived, never stored**: a live `workspace_membership` renders
  `Active`; no membership plus a live (unaccepted, unrevoked, unexpired)
  invitation renders `Invited`. A stored status column would be a second
  source of truth that every write path would have to keep in step.
- **The roster is built from live memberships ∪ live invitations.** It is
  **not** a scan of `workspace_user`, and this is not a stylistic preference:
  a removed member keeps their `workspace_user` row by design (audit
  continuity, and `WorkspaceSignIn` needs it), so a `workspace_user`-driven
  roster would list removed people — and, because their `ExternalSubjectId` is
  still bound, would render them **`Active`**. That is a defect this ADR
  exists to prevent.
- `name` maps to the existing nullable `WorkspaceUser.DisplayName`
  (`WorkspaceUser.cs:28`, `string?`). It is emitted only when a real stored
  value exists and is **never derived from the email address**
  (`MembersTable.tsx:13-16` deliberately refuses to invent one).
- A person holding two memberships appears **once**, at the highest role,
  using the precedence `WorkspaceRoleResolver.cs:101-103,116-118` already
  applies. No second ordering is invented.
- Authorization, and the choice of **404 rather than 403** for a non-member,
  are ADR-025's (§B, Rule 4.1b). This ADR adopts them.

### D4 — `workspace_invitation`: an ordinary tenant table, and a token that needs no unscoped read

The token is **`{tenantId:N}.{secret}`**, where `secret` is ADR-025's 256-bit
CSPRNG value, base64url. The server splits on `.`, enters
`BeginScope(tenantId)`, and matches `token_hash` **inside** that scope.

The tenant half is a **routing prefix, not a credential**: tenant ids already
appear in route paths the client holds, and the invitee is told the workspace
name pre-accept anyway. The secret half remains the entire credential and is
stored only as a hash (ADR-025 Rule 5.2b).

The gain is precise: ADR-025 §5.5 describes a hash-to-tenant lookup as *"the
one read that must happen before a tenant scope exists"*. **With this token
shape that read does not exist at all** — accept enters a scope first and
reads nothing outside one. Both shapes fail closed on a tampered token (the
hash will not match inside the named tenant); this one additionally removes an
unscoped read, which is strictly better on ADR-009's own criterion. The
security-architect ratifies the token's strength and lifecycle; this clause is
the shape only.

`workspace_invitation` (new, tenant-scoped, in `Raffa.Identity.Workspace`):

| Column | Type | Null | Note |
|---|---|---|---|
| `id` | uuid | no | PK, `ValueGeneratedNever` per house style |
| `tenant_id` | uuid | no | RLS axis; plain index |
| `email` | varchar(320) | no | RFC 5321, matching `workspace_user.email`; stored lower-cased |
| `workspace_role_id` | uuid | no | FK to `workspace_role` — the offered role |
| `token_hash` | varchar(128) | no | SHA-256 of the secret half; unique with tenant |
| `invited_by` | varchar(320) | no | the inviting identity |
| `created_at` | timestamptz | no | |
| `expires_at` | timestamptz | no | absolute, evaluated against `IClock` |
| `accepted_at` | timestamptz | **yes** | null until accepted |
| `revoked_at` | timestamptz | **yes** | null until revoked |

Indexes follow the established tenant-first convention
(`WorkspaceUserConfiguration.cs:29`, `suppliers.sql:37`, `quotes.sql:233`):
unique `(tenant_id, token_hash)`; a plain `tenant_id` index; and ADR-025's
partial unique index `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND
revoked_at IS NULL`, which keeps at most one live invitation per address per
tenant so re-invites cannot accumulate valid links.

The table ships **`ENABLE` + `FORCE ROW LEVEL SECURITY` and its
`tenant_isolation` policy in the same migration** as the table itself. This is
not optional discipline: `TenantRlsDeployableScriptCheckTests` discovers
`TenantScopedEntity` subclasses from the EF model and fails the build if the
policy is missing. It gets **no** `identity_self` policy — that widening is
confined to `workspace_user`.

### D5 — endpoint contracts

```
POST   /api/workspaces                                    (exists; changes)
  body: { name, industry?, country? }        // NW-24, ADR-003 w14 footer
  201 -> { id, name, createdAt, role: "Admin" }
POST   /api/workspaces/{tenantId}/invites                 (exists; changes)
  201 -> { id, email, role, expiresAt, acceptUrl, mailDelivered }
GET    /api/invites                                       (new)
  header: X-Invitation-Token                 // never a query string
  200 -> { workspaceName, role, expiresAt }  // nothing else, ever
POST   /api/invites/accept                                (new)
  header: X-Invitation-Token + X-User-Id
  200 -> { workspaceId, workspaceName, role }
DELETE /api/workspaces/{tenantId}/invites/{id}            (new, Admin — revoke)
DELETE /api/workspaces/{tenantId}/members/{membershipId}  (new, Admin — remove)
```

- The token travels in the **`X-Invitation-Token` header**, never in a path or
  query string (ADR-025 Rule 5.2f/5.2g: query strings land in access logs,
  `Referer` headers and browser history). This is why the two `/api/invites`
  routes are **not** parameterised on `{token}`.
- `POST /api/workspaces` returns `role: "Admin"` on the 201 so the SPA can
  enter the new workspace without a second call.
- The pre-accept read returns workspace name, offered role and expiry — **not
  the invited email** (echoing it turns a leaked link into an
  address-discovery tool), no roster, no counts.
- `mailDelivered` is a **server fact**, so the UI can honour NW-58's "must not
  say sent unless it left" from data rather than from a convention.

### D6 — the mailer seam keeps the transport question out of the critical path

`IInvitationMailer` in `Raffa.Identity.Workspace` with
`Task<bool> TrySendAsync(...)`; default `NullInvitationMailer` returns `false`
and logs. When it returns `false` the 201 carries `mailDelivered: false` and
the SPA shows the copyable `acceptUrl` without claiming delivery.

Consequently **the wave lands whole whether or not a transport ships**
(OQ-w14-002), and a transport chosen later is a DI registration plus a
configuration binding — not a redesign. Per the delivery-manager's finding,
any `Invitations__*` configuration binds like `ConnectionStrings:Market`
(`Program.cs:122-128`, optional, no `?? throw`), not like the nine fail-fast
guards, so the code may ship before the infrastructure exists.

### Consequences

- **Good**: one source of truth for membership; no denormalised projection and
  no dual write; removal is effective on the next request with no cache or
  token to invalidate; the count has exactly one definition, already proven in
  code; the invitation table is ordinary, so nothing about it needs a
  tenancy exception; accept performs no unscoped read; the wave is
  transport-independent.
- **Bad**: `GET /api/workspaces` is **N+1 by construction** — one scoped count
  query per workspace the caller belongs to. For the pilot that is 1–3 and
  ADR-025 caps the candidate set at 50. Collapsing it into a single
  cross-tenant aggregate would reintroduce precisely the query path ADR-009
  forbids, so the N+1 is **accepted deliberately and recorded as a known
  limit**, not a defect to optimise away.
- **Bad**: the `identity_self` predicate compares `lower(email)`, which cannot
  use the existing `ix_workspace_user_tenant_id_email` index (it is on plain
  `email`, SQL `:118`). Discovery is therefore a sequential scan of
  `workspace_user`. At pilot scale this is tens of rows; if that table ever
  grows, the fix is an expression index on `lower(email)`, not a change to the
  policy. Recorded so the first person to notice does not redesign the ADR.
- **Neutral**: identities are normalised to lower-case on write (in
  `WorkspaceMembershipFactory.CreateInvitedUser`, which already trims, `:30`)
  and the GUC is set from the same normalisation. `citext` is rejected — a
  column-type change under a live unique index for no benefit once writes are
  normalised.

## Pros and cons of the options

### Option 1 — `BYPASSRLS`
- Good: trivially simple; one query.
- Bad: forbidden outright by ADR-009 `:57-58`. Not re-openable at this table.

### Option 2 — enumerate `workspace`, scope into each
- Good: no policy change.
- Bad: reading `workspace` unscoped is the same forbidden cross-tenant read,
  merely spelled differently; and it is O(all tenants) on every sign-in.

### Option 3 — cross-tenant directory table
- Good: clean isolation axis; no widening of an existing tenant table.
- Bad: a second source of truth for membership, with a dual-write obligation
  on three code paths (create, accept, remove) and a drift direction of
  "still listed after removal" — the worst possible failure. Denormalises
  workspace name and role.
- **Retained as the named fallback**: if the security seat ever withdraws the
  `identity_self` policy, this ADR's endpoint contracts are unchanged and only
  `WorkspaceDirectoryService`'s phase 1 moves.

### Option 4 — one `SELECT`-only identity policy + per-tenant scoped reads
- Good: one source of truth; no dual write; the widening is six auditable
  lines on one table; `WITH CHECK` is untouched so no write path widens.
- Bad: introduces a second isolation axis to reason about, and a multi-scope
  request that ADR-009's interface contract calls unexpected — which is why
  ADR-025 names it as the single sanctioned exception rather than leaving it
  silent.

## Implications for the decomposition

1. **The shared-kernel task lands first and lands alone.**
   `TenantRlsConnectionInterceptor.cs` is wired into every module's DbContext
   options (`Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs:33-35`,
   `Documents.Contracts/.../DocumentsContractsDbContextOptions.cs:38-41`), so
   it is the wave's highest single-writer risk. One task, sequenced before
   every NW-01/NW-04/NW-58 task, and no other task may touch that file.
2. **The identity GUC MUST be set with a bind parameter.**
   `SELECT set_config('app.identity_subject', @identity, false)` with a real
   `DbParameter` — **never** the interpolating `BuildSetCommandText` path
   (`TenantRlsConnectionInterceptor.cs:103-107`), whose own comment justifies
   interpolation *solely* because `TenantId` wraps a Guid. An identity is
   caller-controlled text from `X-User-Id`; interpolating it into a statement
   on the connection that carries `app.tenant_id` would let
   `X-User-Id: x'; SET app.tenant_id = '<victim>'; --` repoint the RLS claim
   for the rest of the request — the exact cross-tenant path ADR-009 `:12-13`
   forbids. The third argument is `false` (session-scoped); `true` would be
   transaction-local and would silently vanish for the discovery read.
   `RESET app.identity_subject` on connection close, alongside the tenant
   reset. The asymmetry between the two GUCs must be commented so a later
   reader does not "tidy" it back into `SET`. There is no existing
   `set_config` call in `backend/src`, so this is a deliberate new pattern.
   **The task carries a negative test**: an identity containing `'`, `;` and
   `--` must not alter `app.tenant_id` on that connection.
   *(Found independently by the software-architect and the security-architect
   in separate lanes; normative in ADR-025 Rule 3.2b and here.)*
3. **Invite writes `workspace_user` (no membership) + the invitation row;
   membership is written only at accept.** This keeps `WorkspaceSignIn`'s
   "sign-in never provisions a user" invariant literally true
   (`WorkspaceSignIn.cs:23-25,36-41`) and is what makes D3's `Invited` state
   derivable. It is a **behaviour change to a service with existing passing
   tests** (`WorkspaceMembershipService.InviteAsync`), so the task must say so
   rather than let an implementer discover it.
4. **Three migrations regenerate one file.** `AddWorkspaceUserIdentitySelfReadPolicy`,
   `AddWorkspaceProfileColumns` (ADR-003 w14 footer) and `AddWorkspaceInvitation`
   all regenerate `Migrations/Scripts/identity-workspace.sql`, which
   `IdentityWorkspaceMigrationScriptStaleCheckTests` byte-compares against an
   in-process regeneration. **Never hand-edit the `.sql`**; regenerate with
   `dotnet ef migrations script --idempotent` from
   `backend/src/Raffa.Identity.Workspace`. Two tasks regenerating
   concurrently **will** conflict — order them. The script is already in both
   CI arrays (`.github/workflows/backend.yml:276-286`, `:308-318`), so no
   workflow edit is needed for the migrations themselves.
5. **`Program.cs` needs exactly one edit in this wave.**
   `app.MapWorkspaceEndpoints()` is already registered (`Program.cs:225`), so
   NW-01/NW-02/NW-04/NW-09/NW-24 need none. Only the new
   `InvitationsEndpointExtensions.cs` (the two `/api/invites` routes) adds a
   line at the contended tail before `app.Run()` — confine that to one task.
   New services register in the module's own
   `Infrastructure/ServiceCollectionExtensions.cs`, never in `Program.cs`.
6. **`WorkspaceEndpointExtensions.cs` is contended by five items** (98 lines
   today). Sequence them or split the file by route group up front.
7. **The OpenAPI edit is one task, landing before the client work.** The
   generator parses **only `responses`** — not `requestBody`, not `parameters`
   (`generate-api-client.mjs:134-143`), so every request body and header in
   this wave is hand-written in `client.ts`. There is no `components` section
   and no `$ref`/`oneOf` support, so response schemas are flat inline objects;
   and the generator checks `enum` **before** the nullable branch (`:63-65`
   precedes `:72-76`), so **a nullable enum silently loses its `null`** —
   therefore `role` and `status` are non-nullable strings, and `accepted_at` /
   `revoked_at` are never exposed as nullable enums. Regenerate with
   `npm run generate:api` from `web/`. The client method is `listWorkspaces()`,
   the name `client.ts:22-24` already reserves.
8. **No new project and no new module.** ADR-002's solution shape is unchanged.
9. **Backfill is out of band.** Existing `dev` workspaces have no membership
   row and cannot be attributed retroactively — `CreateWorkspaceAsync` never
   recorded a creator, so no column, log or audit row names one. There is no
   EF data-migration precedent in this repo. The established shape is a
   separate idempotent script under `backend/scripts/` with `SET app.tenant_id`,
   fixed ids and `ON CONFLICT DO NOTHING`, run by its own workflow
   (`demo-fixture-seed.sql:8-10,60-67`, `seed-demo-fixture.yml:135`). ADR-025
   §2.3 refuses a "claim this workspace" endpoint as a tenant-takeover
   primitive; this ADR agrees and requires operator-supplied
   `(workspace id, admin email)` pairs.
10. **The `demo` fixture needs a membership seed.** No SQL or workflow in the
    repo writes a `workspace_membership` row today, so after NW-01 the demo
    tenant would return an empty list. The seed row is a prerequisite of
    promotion, not a follow-up (delivery-manager's row).

## Assumptions

- **The `identity_self` policy is accepted by the security seat** (ADR-025
  §3.2a — it is, in their lane, independently). If it were ever withdrawn,
  Option 3 is the fallback and **no endpoint contract in this ADR changes**;
  only `WorkspaceDirectoryService`'s phase 1 moves. Recorded so the fallback
  costs one section, not a redesign.
- The identity key in w14 is the interim `X-User-Id` behind one
  `ICallerIdentity` seam (OQ-w14-001, ADR-025 §A). Every endpoint here
  consumes that seam and none reads `HttpRequest.Headers` directly, so NW-05
  (W15) retires the header in one file.
- The token-shape clause (D4) awaits the security seat's ratification; if they
  prefer their unscoped hash lookup, only D4's first paragraph changes and the
  table, endpoints and client are untouched.
- RLS behaviour here is verified by design against the policy SQL and by
  CI-only test suites — the Postgres Testcontainers suites cannot run on the
  operator's machine (Docker does not start), so no claim in this ADR is
  backed by a green local run.

## Amendment (2026-09-10, wave w14 — D3's header line is corrected to the rule ADR-022 already carries)

Serves **NW-04, NW-14**. The Decision outcome above is unchanged and still in
force. D1, D2, D4, D5, D6 and every implication are untouched. This footer
corrects **one line** in D3 and answers the one contract question the
client-architect left open at the table.

Raised by the council-gate as a verified inconsistency after the close, owner
named as this seat. It failed none of the gate's five checks, so it was recorded
for HITL — but it is a normative line in an ADR I authored, and a decomposer
following it writes a task that sends a header another accepted ADR forbids.

**1. `GET /api/workspaces/{tenantId}/members` sends `X-User-Id` only.** D3's
fenced block reads `headers: X-Tenant-Id + X-User-Id`. That line is
**superseded**; read it as `headers: X-User-Id (identity; absent -> 401 per
ADR-025 §B)`, identical to D1's discovery endpoint. The tenant comes from the
route path `{tenantId}` and the caller's membership in it is verified.

This is a correction, not a preference: **ADR-022's accepted w14 footer already
governs it in two places** — clause 3 ("`X-Tenant-Id` is not the tenant of a
membership route… for every route in this wave the tenant comes from the route
path and the caller's membership in it is verified") and the retirement table
row ("`X-Tenant-Id` as the tenant of a membership route | not an input; route +
membership"). Two accepted ADRs cannot carry opposite instructions for one
operation, and the older rule is the general one.

The failure it prevents is not cosmetic. A header that names a tenant on a route
that already names one is a **second, unvalidated tenant input on an
authorization-bearing route**, and the handler must then decide which wins —
precisely the ambiguity ADR-022 demoted the header to remove. With this
correction there is nothing to decide, because the header is not read. It also
removes a split within one route prefix: the sibling invite operation on
`/api/workspaces/{tenantId}/…` already takes the tenant from the route
(`client.ts:918`, quoting that operation's own OpenAPI description), so the
members screen would otherwise send the tenant two different ways to two
endpoints on the same prefix.

**2. The correction is exactly one line — verified, not asserted.** `Grep` over
this ADR for `X-Tenant-Id` returns two hits: D1's prohibition ("This endpoint
accepts no `X-Tenant-Id`, ever"), which **stands**, and D3's header line,
corrected here. No other endpoint block, schema, test or implication carries it.

**The approved decision record is not edited by this footer.** `waves/w14.md`'s
NW-04 row already records this outcome ("the client sends `X-User-Id` only… one
word in §D3, no other clause moves"); the ADR was the outlier, not the record.
Leaving the record byte-identical to what the gate read back is deliberate — the
artifact that was checked stays the artifact that was approved.

**3. `role` is a string on the wire, never an OpenAPI `enum`.** The
client-architect's second ask, answered from the schema rather than from the
absent enum. The reason is not that the values are undecided: **role names are
per-tenant rows.** `workspace_role` is a table whose `name` is
`character varying(30)` with no CHECK constraint and no Postgres enum type
(`identity-workspace.sql:25-31`), unique only per tenant
(`ix_workspace_role_tenant_id_name`, `:104`). A tenant can hold a role name no
schema knows, and `Grep` over `backend/src` finds **no** `Procurement` / `Legal`
/ `Finance` / `ReadOnly` role literal at all. Emitting `enum: [...]` in the
OpenAPI would generate a closed client union the server can legally violate on
the next seeded row.

- This agrees with the generator clause already in this ADR, which forces `role`
  and `status` to non-nullable strings for an unrelated reason (the generator
  checks `enum` before the nullable branch and a nullable enum silently loses its
  `null`). Two independent reasons, same shape.
- The client-architect's parse rule — an unmodelled role maps to **least
  privilege, never `admin`** — is therefore **mandatory, not defensive**, and it
  matches the server's own behaviour: `WorkspaceRoleResolver.cs:116-118` returns
  **`null`** when `WorkspaceRoleClaimResolver.TryResolve` does not recognise a
  name. The backend does not fall back to a default and neither may the client.
- Adopting ux-ui-designer's clause, the split is exact: **permissions degrade to
  least privilege; the label does not.** An unmodelled role is displayed by its
  own name, never relabelled `Procurement`.

**4. What does not change in D3.** The roster as live memberships ∪ live
invitations, the derived `status`, `name` mapping to `WorkspaceUser.DisplayName`
and never derived from an email, the highest-role collapse using
`WorkspaceRoleResolver`'s existing precedence (`:101-103`), and ADR-025's
**404 rather than 403** for a non-member — all stand unchanged.

## Amendment (2026-09-13, wave w15 — guest provisioning, the absolute accept link, and what a failed invite must not leave behind)

Serves **NW-67, NW-68, NW-69**. The **Decision outcome above is unchanged and
still in force**. D1 (two-phase discovery, no tenant input), D2 (the one count
definition), D3 (the derived roster), D4 (the table, the token shape and the
partial unique index) and every implication are **untouched**. This footer adds
**two response fields and one seam** to D5/D6, and closes the `acceptUrl` shape
gap the **ADR-005 second w14 footer raised explicitly for this seat**.
Authorization, the Graph permission and the audit verbs are **ADR-025's and
security-architect's**; this footer is the contract and the code shape.

### 1. The invite 201 gains `identityProvisioned` (NW-67)

```
POST /api/workspaces/{tenantId}/invites
  201 -> { id, email, role, expiresAt, acceptUrl, mailDelivered, identityProvisioned }
```

`identityProvisioned` is **`true` for both `Provisioned` and `AlreadyPresent`**
and `false` when provisioning is not configured. It is a **server fact**,
exactly as `mailDelivered` is (D5's last bullet), and it exists because NW-69's
pane must distinguish "this person has no account on the tenant yet" from "the
mail did not leave" — two different outcomes with two different remedies. Without
it the client would infer identity state from delivery state, which is the
inference ADR-012's provenance rule forbids.

Both flags are **non-nullable booleans**, never absent: an omitted flag would be
read as `false` by one client and `undefined` by another.

### 2. `IGuestProvisioner` joins D6's seam family, with a fourth value

```
Raffa.Identity.Workspace/Application/IGuestProvisioner.cs
    Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken)
    // Provisioned | AlreadyPresent | NotConfigured | Failed(reason)
```

Exactly `IInvitationMailer`'s shape (`IInvitationMailer.cs:16-33`), in the same
folder, for the same reason D6 gives: **the wave lands whole whether or not the
directory permission ships.** `NotConfigured` is gated by
`Invitations__GuestProvisioning__Enabled` (default **false**), under which the
invite behaves exactly as it does on `main` today — link-only, no directory
write — instead of either lying or failing. Same two-phase wiring as
`invitation_mail_enabled` and `ai_gateway_wired`, both already proven on this
tenant.

**The adapter is `Raffa.Api/Infrastructure/GraphGuestProvisioner.cs`, never the
module.** `Microsoft.Graph` is a provider SDK and ADR-002's rule is that only a
host holds one — the same reason `Azure.Storage.Blobs` sits in `Raffa.Api.csproj`
(`:32-34`). It authenticates as the existing workload managed identity, so
`Azure.Identity` enters `Raffa.Api`, **which trips `SdkAllowListTests`**
(`:23` `ForbiddenPrefixes = ["Azure.AI.", "Azure.Identity"]`, `:25`
`AllowedProjectName = "Raffa.AiGateway"`). That test is therefore amended **in
the same task**, from one allowed project to a per-prefix map: `Azure.AI.*` stays
`Raffa.AiGateway`-only, `Azure.Identity` is additionally permitted in
`Raffa.Api`. **This is part of the design, not a workaround** — the test's own
doc comment (`:13-19`) already records that `Raffa.Api`'s `Azure.Storage.Blobs`
is deliberately legitimate, so the amendment runs *with* its stated intent. The
task must extend that comment to say why.

### 3. Provisioning runs inside the invite request, before the mail — and a failure leaves nothing behind

Order in the handler: existing guards (unchanged,
`WorkspaceInvitesEndpointExtensions.cs:81-124`) → **provision the guest** → mint
token + write rows → send mail → 201. The Graph call sits **outside** the
database transaction: a remote call inside one holds a connection and cannot be
rolled back anyway.

Synchronous, because `POST …/invites` is one interactive action with no 2 s
budget and a Graph `POST /invitations` is one sub-second call. Before the mail,
because a mail whose link leads to a sign-in the invitee cannot complete is
A15-4's failure, not a pass.

**On failure: no invitation row, no mail, no token.** This is forced by **D4's
own index**. The partial unique index `(tenant_id, lower(email)) WHERE
accepted_at IS NULL AND revoked_at IS NULL` means a live invitation **holds the
slot**, so an invitation left behind by a failed provisioning could not be
retried without first revoking — the exact dead-slot trap ADR-020's second w14
footer documented for the `Expired` case. Instead: one audit row
(free-form verb, no schema change — `AuditEvent.cs:29-31`), and a **502** whose
problem body carries a stable machine-readable `reason` from a **closed set** —
`consent_missing` | `provisioning_failed` | `directory_unavailable` — so the pane
picks copy from a fixed list and **ux-ui-designer owns the strings**. The Admin
can retry immediately, because no slot was taken.

**Idempotency (A15-5) is the implementation's obligation.** Inviting an address
already in the tenant must create no duplicate guest. Whether that is satisfied
by the invite permission alone or needs a directory read first is a
least-privilege question **security-architect and cloud-architect own**
(OQ-w15-005); `IGuestProvisioner`'s contract is identical either way.

### 4. `acceptUrl` becomes absolute when a transport is configured — closing the D5 gap

D5 specifies `acceptUrl` on the 201 but never fixes whether it is absolute; w14
shipped the site-relative form, which is unusable in an email body. The ADR-005
second w14 footer raised this **for this seat** to answer, and this is the
answer:

- with `Invitations__AcceptUrlBase` configured, `acceptUrl` is
  `{base}/invite/accept#{token}`;
- without it, the site-relative form of w14, unchanged.

**The fragment stays** (`AcceptRoutePrefix = "/invite/accept#"`,
`WorkspaceInvitationService.cs:58`; composed at `:86`) — ADR-025 Rule C9 and the
ADR-005 second w14 footer, never a query string, because `staticwebapp.config.json`
rewrites the accept path and the platform logs it.

**The misconfiguration "mail enabled, no accept-url base" is handled where it can
be handled honestly.** Per D6 the binding is optional and must never `?? throw`
at startup, so instead **`TrySendAsync` refuses and returns `false`**, logging one
line. The pane then shows the already-designed "could not be sent" state with the
copyable link. Mailing a relative link nobody can open would be worse; a startup
crash would violate D6.

Product-owner's w15 footer makes the consequence explicit: **a site-relative link
means NW-68 does not ship** — the item is cut, not narrowed.

### 5. The real mailer is one line, and the registration order is load-bearing

`AcsInvitationMailer` replaces `NullInvitationMailer` at exactly one line —
`Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs:61` is
`TryAddScoped`, and its own comment already records that a real mailer is "a
second `TryAddScoped` call in a later task". **`TryAdd` means the first
registration wins**, so the real mailer must be registered **before**
`AddIdentityWorkspaceModule`. That module's signature takes only a connection
string (`:34-35`), so configuration is bound by the host in the established
`sp.GetRequiredService<IConfiguration>().GetSection(...).Bind(...)` factory shape
(`Raffa.AiGateway/ServiceCollectionExtensions.cs:85-92`), **never `IOptions<T>`**,
which appears nowhere in `backend/src`.

`mailDelivered` itself is **unchanged**: it is literally the return of
`IInvitationMailer.TrySendAsync` (`WorkspaceInvitationService.cs:96-103`) and is
already `required` in the contract (`raffa-api.v1.json:463-470`). **No wire
change** — w14 designed this correctly and w15 only fills in the transport.

**No mail body, no accept URL and no token in any log or audit row** — the rule
`NullInvitationMailer.cs:25-29` already keeps deliberately, and which now matters
for real.

### 6. A finding the wave must not discover in implementation: the server cannot re-send the original link

The token is stored only as a SHA-256 hash (`WorkspaceInvitationService.cs:73`,
`:297-300`) and the plaintext exists only on the `IssueAsync` stack and in the
201 body. **So any user-initiated retry after the response is a re-issue, and the
old link dies.**

- Inside the request, a transient transport failure may be retried once by the
  mailer — that is the only true "send it again".
- After the response, "Try sending again" is **revoke-then-invite**. ADR-016's
  second w14 footer already contemplates a client-composed `DELETE` + `POST`,
  which adds no server writer.

**Proposed, and deferred to ADR-025's owner**: make `POST …/invites` **replace**
a live invitation for the same address in one transaction (revoke + issue, two
audit rows) instead of returning the 409 today's partial unique index produces
(`WorkspaceMembershipService.cs:142-151`, `:174-182`). It is atomic where the
two-call sequence is not, and it collapses "lost the link", "it lapsed" and "the
mail failed" into one affordance with one call — the gap ADR-020's second w14
footer called *"no legal path as written"*. **It changes an accepted lifecycle,
so security-architect rules**; if they decline, the client-composed sequence is
the fallback and **no server file changes**. Either way the UI warns first
("The link you already shared stops working."), which is ADR-020's rule and is
not re-opened here.

### 7. What a decomposer must carry out of this footer

1. Contract delta on `POST …/invites`: add `identityProvisioned`, document the
   new **502** and its `reason` set, and document the 401/403/404 the guard
   already returns. One task owns `raffa-api.v1.json` per phase (implication 7).
2. `Program.cs` gains the mailer and provisioner registrations **before**
   `AddIdentityWorkspaceModule` — and that file is contended by three items this
   wave (NW-27, NW-05, NW-68). One owner or an explicit sequence.
3. `Raffa.Api.csproj` gains `Microsoft.Graph` and `Azure.Communication.Email`;
   `SdkAllowListTests.cs` is amended in the NW-67 task (§2);
   `DependencyDirectionTests.ForbiddenSdkPrefixes` gains `Microsoft.Graph`
   (ADR-002 w15 footer clause 4).
4. **No schema change.** No new column, no new table, no migration, and
   `identity-workspace.sql` is **not** regenerated by this wave's invite work —
   so implication 4's ordering hazard does not apply to NW-67/NW-68.
5. Tests: provisioning failure → **no invitation row**, one audit row, 502 with a
   `reason` from the closed set; `NotConfigured` → the w14 link-only behaviour
   unchanged; mailer `false` → the link path intact; absolute link composed when
   the base is set, relative when it is not.

## Amendment (2026-09-13, wave w15 round 2 — the invite 201's delivery discriminant, re-issue by replacement, and the SDK allow-list map)

Serves **NW-68, NW-69, NW-67**. **D1–D4 and every implication are untouched**, and
so are §1–§5 and §7 of the first w15 footer. **No schema change, no new column, no
new table, no migration, no regenerated `.sql`.** This footer does three things the
table asked of this seat: it names the field two seats routed here, it closes §6's
deferral with the ruling that came back, and it completes the SDK allow-list map.

### 8. The 201 carries one discriminant string, not two booleans (NW-69)

Client-architect ruled at the table that the pane branches on **the server's
outcome string** — *"not a second boolean, not two booleans the client combines,
and never a client inference from a status code"* — and ux-ui-designer ruled the
outcome set to **three** values, because a provisioning failure aborts the
invitation (§3: one audit row and a **502**, no invitation row, no token, no
mail), so the fourth outcome both lanes had drafted **cannot occur**. Both routed
the field's **name and values** to this seat. They are:

```
POST /api/workspaces/{tenantId}/invites
  201 -> { id, email, role, expiresAt, acceptUrl,
           deliveryOutcome, mailDelivered, identityProvisioned }
```

- **`deliveryOutcome`** — a **non-nullable string carrying an `enum`** of exactly
  `"sent" | "mail_failed" | "no_transport"`. A closed server-side vocabulary, so a
  literal union is correct here where `role` is deliberately a bare string
  (w14 clause 3: role names are per-tenant rows) — and it is what makes the pane's
  branch exhaustive under `tsc --noEmit`. **Non-nullable is not a preference**: the
  generator checks `enum` (`generate-api-client.mjs:63-65`) *before* the nullable
  union (`:72-76`), so a nullable enum silently loses its `null`. `snake_case`
  matches §3's 502 `reason` set, so one endpoint speaks one vocabulary.
- The three values map 1:1 onto ux-ui-designer's three 10.1 strings: `sent` →
  "Invitation sent to {email}."; `mail_failed` → "Invitation created, but the
  email could not be sent." **+ the link block**; `no_transport` → "Invitation
  ready for {email}." **+ the link block**. **There is no fourth value** —
  `provisioning_failed` is a 502, not a 201, which is what makes *a link renders
  only when it is usable* true by construction rather than by a branch.
- **`mailDelivered` stays on the wire and stays exactly what it is**:
  `IInvitationMailer.TrySendAsync`'s return
  (`WorkspaceInvitationService.cs:96-103`), already `required` in the contract
  (`raffa-api.v1.json:463-470`), *accepted for delivery and never a receipt*
  (security-architect), never optimistically `true` (product-owner). It is **not**
  removed: three seats ruled on its meaning this wave, and deleting a `required`
  field nobody asked to delete is a wire break for nothing.
- **The invariant that stops the two diverging, and it is normative**:
  `deliveryOutcome == "sent"` **if and only if** `mailDelivered == true`. Both are
  computed at one place from one call, and **the client branches on
  `deliveryOutcome` and never combines the two** — which is what client-architect's
  rule actually forbids. A vitest case asserting the biconditional across the
  three outcomes is the only thing that keeps a later `mail_failed`-with-
  `mailDelivered: true` from being expressible at all.
- **`identityProvisioned` is unchanged from §1** — the one boolean ux-ui-designer
  keys the one-time-code sentence to. `true` for **both** `Provisioned` and
  `AlreadyPresent` (so the form is not a directory-enumeration oracle — ADR-025
  §J), `false` when provisioning is not configured, under which that sentence
  renders nothing at all and leaks no configuration fact.

### 9. Re-issue by replacement — §6's deferral is closed, and the shape §6 proposed is the one that was ruled

Security-architect ruled it in **ADR-025 §J.2b**: revoke the live invitation and
create the new one **in one transaction**, so two live tokens for one address
never coexist. §6 proposed exactly that and **this seat adopts the ruling**; where
the two ADRs touch this mechanism, ADR-025 governs. The contract and code
consequences are this seat's:

- **`POST …/invites` becomes replace-on-live-invitation. No new endpoint, no new
  verb, no schema change.** `InviteAsync`'s `alreadyInvited` pre-check
  (`WorkspaceMembershipService.cs:142-151`) stops returning `Conflict`; it revokes
  the live row and issues the new one inside the **same** `SaveChangesAsync`,
  writing two audit rows (`workspace.invitation.revoked` + `.issued` — both
  existing free-form verbs, no schema change).
- **Two things must not move with it, and both are easy to lose in that edit.**
  The `alreadyMember` branch (`:136-140`) keeps returning `Conflict`: a live
  **membership** is not a re-issue case, and replacement must never become a way
  to re-grant a role to someone who already holds it. And the `IsUniqueViolation`
  catch (`:174-182`) **stays**: it is the backstop for two concurrent invites for
  the same address and still owes that racer a clean 409, which the pre-check no
  longer produces.
- **Contract delta**: the operation's `409` stops being reachable for *"already
  has a pending invitation"* and stays documented for the concurrent-writer and
  already-a-member cases; the description states that a live invitation is
  replaced. Same `raffa-api.v1.json` task as §7 item 1 — **one owner per phase**.
- This is what finally makes ADR-020's **"Send a new invitation"** affordance
  legal — the gap its second w14 footer called *"no legal path as written"*,
  caused by D4's partial unique index holding the slot for a lapsed invitation.
  **D4's index is unchanged**; replacement satisfies it by revoking first, which
  is why no predicate had to be widened — and it could not have been, since
  "expired" is a clock comparison and a partial-index predicate must be immutable.
- **§6's client-composed `DELETE` + `POST` fallback is withdrawn.** It is two
  requests with a window between them in which the address holds no invitation at
  all — and if the second fails, the Admin has destroyed a working link and
  created nothing.

### 10. `SdkAllowListTests` — the per-prefix map, completed for the whole wave

§2 amends that test for `Azure.Identity` in `Raffa.Api`. Two corrections from the
table, both verified at the source:

- **Security-architect's §J.1**: the amendment must be **package-scoped** — a
  per-prefix map, never a second `AllowedProjectName`. Widening the single skip
  (`SdkAllowListTests.cs:25`, `:40-43`) is the one-word edit a task will reach for
  and it makes **`Azure.AI.*` legal in `Raffa.Api`**, un-guarding the Foundry
  boundary.
- **NW-27 needs the same file.** Cloud-architect's transport authenticates by
  managed identity (topic-scoped RBAC, **no connection-string secret**), so
  `DefaultAzureCredential` — `Azure.Identity` — lands in **`Raffa.Worker`** as
  well as `Raffa.Api`. The map: `Azure.AI.*` → `Raffa.AiGateway`; `Azure.Identity`
  → `Raffa.AiGateway` + `Raffa.Api` + `Raffa.Worker`; `Microsoft.Graph` →
  `Raffa.Api`. **`SdkAllowListTests.cs` is a single-writer file contended by NW-27
  and NW-67.**
- **§7 item 3 is corrected in its reading, not its instruction**: adding
  `Microsoft.Graph` to `DependencyDirectionTests.ForbiddenSdkPrefixes` is right
  (nothing in that list matches it today) **and insufficient** — that test scans
  only the fixed ADR-002 domain-module list, never a host, which is where the
  adapter lives. See **ADR-002's second w15 footer**.

**`waves/w15.md` records this under NW-67 and NW-68.** Nothing in D1–D4, the
token shape, the partial unique index, the RLS policy or the roster projection
changes.

## Amendment (2026-09-14, wave w16 — `/api/audit` joins the contract, the `X-Role` parameter leaves it, and five read-backs are published)

Written by software-architect (owner) at the w16 council table. Serves
**NW-08, NW-31, NW-11, NW-12, NW-13**, and records **NW-21's zero delta**. The
**Decision outcome above is unchanged** — two-phase workspace discovery,
membership-gated inclusion, the host-composed validated count, the roster
projection, the invitation token and schema — and so are all three earlier
footers. Six clauses, all about `web/openapi/raffa-api.v1.json`.

**1. `GET /api/audit` joins the contract (NW-08), and the prose half is the same
edit.** The path is absent from all 33 paths today; its only mention is
`info.description` (`:6`), which lists it among routes "deliberately NOT added
here". Adding the path and leaving that sentence produces a contract that
documents a route while denying that it documents it — the stale-record class
NW-31 exists to delete (client-architect **C1**, adopted).

**2. The route's tenant derivation changes, and that is a contract change on a
live route, not a drop-in** (OQ-w16-002, answered). Today the tenant comes from
a `tenant_id` **claim**, and a `?tenantId=` query is deliberately refused
(`AuditEndpointExtensions.cs:25-29`). After NW-08 it comes from **`X-Tenant-Id`
verified against membership** — the shape every other tenant-scoped route
already uses (`DocumentsEndpointExtensions.cs:492-509`). The refusal property
survives in a stronger form: the header is an **authorized selector, never an
authorization input**, enforced by a membership fact instead of by the absence
of a parameter. The ladder is security-architect **S16-4** verbatim — 401 → 400
→ **404 for a non-member** → 403 → 200 — and `/api/audit` gets no bespoke
version of it. The contract declares the header as required on this route, so
the client task cannot discover it at runtime.

**3. The `X-Role` parameter leaves the contract (NW-31)**: the declaration at
`:5239`, its description at `:5236`, and the stale `X-Workspace-Role` sentence
on `deleteDocument`'s description `:1227` (inherited by `reprocessDocument`
`:1310`). **`X-Tenant-Id` does not leave** (security **S16-7**, delivery
**D7**) — deleting it would break every tenant-scoped route. The server-side
half of this retirement is the **ADR-024 w16 footer clause 2**.

**4. This edit is invisible to every check the build runs, so its gate is a
grep** (client-architect **C4**, adopted here as a contract rule). The generator
parses **`responses` only**, so a `parameters` entry never reaches `schema.ts`:
deleting `X-Role` changes no generated byte, fails no `tsc --noEmit` and fails
no build. The task's proof is
`rg "X-Role|X-Workspace-Role" web/openapi backend/src .github` returning
nothing outside historical ADR/acceptance records — plus a **positive**
assertion that `X-Tenant-Id` is still declared, so the sweep cannot overrun.

**5. Five theme-B additions**, specified in **ADR-028**:
`GET /api/renewals/{id}/action`; the **`savedAction`** field on
`GET /api/renewals` rows — **never named `action`**, which already carries a
deterministic calculator's `RecommendedAction` (`RenewalsEndpointExtensions.cs:239`);
`GET /api/quotes`; `GET /api/quotes/{id}` with its outcomes embedded; and
`GET` + `PUT /api/contracts/{id}/negotiation-steps`. **NW-21 adds nothing** —
its field is already on the wire and already typed (`client.ts:877`). A
published path may ship **without** a `client.ts` wrapper, the convention
`info.description` already records for `/api/insights/criticality` and
`/api/contracts/{id}/strategy` (client-architect **C2**).

**6. One stale response shape is corrected while the file is open**
(delivery-manager **D2**, axis 2): `reprocessDocument` has returned **202** with
`{documentId, extractionJobId, processingStatus}` since NW-27
(`DocumentsEndpointExtensions.cs:523-535`). Any contract text still promising a
synchronous `200` with `pagesParsed` / `chunksIndexed` is w15 residue and is
swept by the theme-A contract task.

**Two contract tasks, never six** (ADR-012 §3, and the 5-phase cap):
**contract-A** = NW-08 + NW-31, **contract-B** = the five additions of clause 5,
in a later phase. `waves/w16.md` records this under NW-08, NW-31, NW-11, NW-12
and NW-13.
