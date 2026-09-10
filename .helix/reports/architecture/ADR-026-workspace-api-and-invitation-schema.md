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
(`backend/src/Contigo.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs:15-16,53-57`;
`backend/src/Contigo.Identity.Workspace/Migrations/Scripts/identity-workspace.sql:143-182`).
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
  (`backend/tests/Contigo.ArchitectureTests/DependencyDirectionTests.cs:62`).
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
  `Contigo.Identity.Workspace/Infrastructure/`, exposing
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
  `Contigo.Identity.Workspace` may reference only `Contigo.SharedKernel`
  (`DependencyDirectionTests.cs:62`), so it cannot see `Documents.Contracts`.
  `Contigo.Api` is the one project allowed to see both; the endpoint handler
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

`workspace_invitation` (new, tenant-scoped, in `Contigo.Identity.Workspace`):

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

`IInvitationMailer` in `Contigo.Identity.Workspace` with
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
   `backend/src/Contigo.Identity.Workspace`. Two tasks regenerating
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
