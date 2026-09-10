# ADR-025 — Workspace membership authorization and the invitation lifecycle

- **Status**: accepted
- **Date**: 2026-09-10
- **Deciders**: security-architect (owner) + software-architect (entity, endpoint
  and OpenAPI shape, ADR-026) + product-owner (N3b wording, transport deferral,
  ADR-001 w14 footer) + cloud-architect (zero-delta confirmation, ADR-005/006 w14
  footers) + client-architect (accept route, token-not-in-URL realization) +
  ux-ui-designer (screen 10 copy and states) + delivery-manager (migration order)
- **Wave**: w14 — serves **NW-01, NW-02, NW-04, NW-14, NW-58**
- **Locked citations**: `reports/context/locked-decisions.md` — "OIDC, SSO-ready
  (Entra ID)"; "secrets in Key Vault; no secrets in code, client bundles or
  Terraform source"; "`tenant_id` on business data"; "isolation in **both** Azure
  environments"; "RAG must not retrieve unauthorized documents"; "audit of access
  and corrections"
- **Reads with**: **ADR-026** (contract, data model, module composition). Neither
  restates the other. **Where the two touch one mechanism, this ADR governs.**

## Context and problem statement

Wave w14 ("workspace is real") turns membership from a decorative row into the
mechanism that decides who sees which tenant. Five items depend on one
authorization model: discovery (**NW-01**), bootstrap (**NW-02**), the roster
(**NW-04**), the Admin gate (**NW-14**) and the invitation lifecycle
(**NW-58**). ADR-009 (RLS), ADR-010 (Entra/OIDC), ADR-011 (Key Vault,
authz-before-retrieval, audit) and ADR-022 (interim headers) are accepted and are
**not re-opened**; this ADR adds the membership and invitation decisions those
four do not contain, and amends each of them at its own footer.

Three facts on the wave-base checkout force the decision.

1. **`POST /api/workspaces/{tenantId}/invites` has no authorization at all.**
   `WorkspaceEndpointExtensions.cs:59-97` takes a route `tenantId`, an
   `InviteRequest`, the service and a `CancellationToken` — no `HttpContext`, no
   resolver, no claim, no membership check; `MapPost` at `:32` attaches no
   authorization metadata and the host wires no auth middleware
   (`WorkspacePrincipalAuthorization.cs:14-17`). The role comes from the request
   body (`:75`) with `Admin` among the accepted values (`:78`), and
   `WorkspaceMembershipService.InviteAsync` writes a `workspace_user` row
   (`:80-88`) **and a live `workspace_membership` row** (`:98-105`). An invite
   today is an immediate grant, not an offer.

   This has been survivable only because discovery is a `localStorage` array in
   one browser (`workspaceStore.ts:32,88-106`), so an unauthorized membership row
   is inert. **NW-01 and NW-02 are precisely what make it live.** The exposure is
   created by this wave's own success, which is why the guard cannot be deferred
   to the wave that "does security".

2. **The RLS claim is established on connection open, and fails closed.**
   `TenantRlsConnectionInterceptor.cs:22-35` sets `app.tenant_id` in
   `ConnectionOpened`, `RESET`s it in `ConnectionClosing` (`:37-49`), and leaves
   it unset when no scope is active (`:53-57`, `:66-70`, doc `:15-16`). With the
   claim unset, `tenant_id = nullif(current_setting('app.tenant_id', true),'')::uuid`
   is `NULL` → not true, so every policy in `identity-workspace.sql:143-182`
   denies the row. **A "list the workspaces I belong to" query therefore returns
   nothing under the policies that exist** — the caller's tenant is exactly the
   unknown the endpoint exists to discover. NW-01 is an RLS design decision, not
   an endpoint task.

3. **ADR-009's own interface contract expects one scope per request.**
   `ITenantContext.cs:20-26`: "ADR-009 expects exactly one scope per
   request/worker job; nested scopes are supported … but are not the expected
   usage." Discovery needs one scope per candidate tenant. That is not unsafe,
   but it is outside the documented expectation, so it must be a **named**
   exception or the next reviewer correctly reads the rule as soft.

## Decision drivers

- **Authorization before retrieval is not negotiable** (ADR-011 `:98-100`, spec
  §8.3/§14). Every rule below is ordered verify → scope → read.
- **RLS may be narrowed, never widened by convenience.** No `BYPASSRLS` (ADR-009
  `:57`, verbatim: "No query may run under `BYPASSRLS`/superuser in the
  application path"), no second un-RLS'd membership store, no policy a reviewer
  cannot hold in their head.
- **Revocation must be immediate**, because NW-58 says removal means removal and
  the pilot has no other gate.
- **An interim mechanism must have a named retirement**, not an intention.
- **Cheaper is safer here**: a design with no key material has no rotation story,
  no vault entry and no per-environment drift.

## Considered options

The load-bearing choice is *what an invitation is*.

1. **Invite writes a live membership** (today's behaviour) — an invitation is a
   grant.
2. **Invite writes the invitation row only**; accept provisions the
   `workspace_user` row.
3. **Invite writes `workspace_user` with no membership, plus the invitation row**;
   membership is written only at accept.

## Decision outcome

**Chosen: Option 3**, because it is the only shape that makes an invitation a
*pending offer* without loosening the invariant that sign-in never provisions a
user — `WorkspaceSignIn.ResolveSignedInUser` (`WorkspaceSignIn.cs:36-41`) fails
when no `workspace_user` row exists for the email, deliberately ("sign-in cannot
provision a new workspace user", invariant stated `:23-25`).

Option 1 is the defect NW-58 exists to fix: an address an Admin typos becomes a
member, and after NW-01 that member's picker shows the tenant. Option 2 looks
cleaner and **fails at runtime on every accept** — with no `workspace_user` row,
`LinkSignInAsync`/`ResolveSignedInUser` returns Failure; repairing it means
permitting self-provisioning at sign-in, which is a strictly weaker posture for
no product gain. Option 3 also gives NW-04 its two states for free: no membership
+ live invitation → `Invited`; membership → `Active` (ADR-026 §D3).

### Consequences

- **Good**: one grant, written once, at the moment consent is expressed. No dual
  write and no projection to drift.
- **Good**: revocation needs no cache invalidation and no token revocation list —
  membership is read from the database on every request
  (`WorkspaceRoleResolver.cs:85-119`), so removal takes effect on the **next**
  request.
- **Good**: no key material anywhere in the design (§C), so w14 adds no Key Vault
  secret, no environment variable, no Terraform change — which is what makes
  cloud-architect's zero-delta confirmation hold.
- **Bad**: an invited-but-never-accepted address exists as a row inside the
  tenant before consent. It is visible to that tenant's members only (§D.4d) and
  is exactly what the Members table is for, but it is a real disclosure of "this
  person was invited here" and is recorded as such.
- **Bad**: two writes must be transactionally consistent at accept (bind subject,
  insert membership, stamp `accepted_at`). Partial acceptance must be
  unreachable.
- **Neutral**: `workspace_user` rows outlive membership, by design — audit
  continuity and `LinkSignInAsync` both need them. That is safe **only** because
  §B.3d gates discovery on membership, never on user existence.

---

## §A. The identity seam (answers OQ-w14-001)

In w14 the membership key is the interim `X-User-Id` (the MSAL account username
the web already sends on every call — `client.ts:49-54`), resolved in **exactly
one place** so NW-05 (W15) replaces it with the token subject by editing one file.

The seam already half-exists: `WorkspaceRoleResolver.ResolveMembershipRoleAsync`
(`:85-119`) reads the header (`:88-97`) and matches `workspace_user.Email` **or**
`ExternalSubjectId` (`:111`) — exactly the shape that survives the swap, because
ADR-010's `sub`/`oid` lands in `ExternalSubjectId` via
`WorkspaceMembershipService.LinkSignInAsync:118-145`.

- **Rule A1.** One resolution point returns the caller's identity (naming is
  software-architect's `ICallerIdentity`). Create, invite, accept, revoke, remove
  and the resolver's membership branch all consume it. After the wave, `Grep` for
  `"X-User-Id"` in `backend/src` must find it in **one** file besides the
  document/conversation readers NW-05/NW-32 retire.
- **Rule A2.** The interim identity is trusted for one thing only: *which
  membership rows to look up*. It confers no role, no tenant and no scope. A
  request with any `X-User-Id` and no membership row for the target tenant is a
  non-member (§B).
- **Rule A3 (retirement).** `X-User-Id` is removed by **NW-05 (W15)**. The
  retirement test is written now and activated then: with a validated token
  present the header is **ignored entirely**, not merely overridden — token `A`
  plus `X-User-Id: B` acts as `A` and never as `B`.

**Recorded limitation of w14, not a defect of NW-58** (answers OQ-w14-po-02):
until ADR-010 lands, a caller can assert another identity in the header. The
Admin gate still resolves from the membership row, never from a client-supplied
role, so N3b-8 is honest. This limitation belongs in the epic story and is closed
by NW-05. **A reviewer must not reject NW-58 for it.**

---

## §B. The rejection contract

Endpoint *shapes* are ADR-026's; **which rejection and which status** is this
ADR's, so no task invents its own.

| Situation | Status | Why this one |
|---|---|---|
| No identity presented on create / invite / accept / revoke / remove | **401** | "Authenticate", not "fix your body" — the SPA must re-run MSAL. Matches `AuditEndpointExtensions.cs:33-34`, which already maps `Unauthenticated` → 401 |
| Identity presented but malformed | **400** | A well-formed refusal of a malformed input; no auth action would help |
| Caller is **not a member** of the target tenant | **404** | A 403 on a tenant id you do not belong to is a **tenant-existence oracle** |
| Caller **is** a member but lacks the role | **403** | The caller legitimately sees the tenant; only the action is denied. Preserves `DocumentsEndpointExtensions.cs:53-55` (R-DOC-07/R-DOC-10) |
| Any live member reads the roster | **200** | §D.4a |
| Removing the **last** Admin (including self) | **409** | Well-formed and authorized, but violates a tenant invariant |
| Accept: token malformed, unknown, revoked, or consumed by a **different** identity | **404** | One indistinguishable answer for every "this token is not yours" case |
| Accept: token valid but **expired** | **410** | The one disclosure worth making — the UI can say "expired, ask your Admin". Safe because reaching this branch already proves possession of a 256-bit secret |
| Accept: already accepted by **this** identity | **409** | Idempotency signal, not a user-facing error |
| Accept: signed-in email ≠ invited email | **403** | §D.3b. Reason text never echoes the invited address |
| Crafted `X-Tenant-Id` for a foreign tenant on a read | **404** | RLS yields no rows → not found. Satisfies acceptance **N5** |

- **Rule B1.** **No path in this wave returns 403 where the caller is not a
  member.** Non-membership is always 404. This is the single sentence a reviewer
  checks every new endpoint against.
- **Rule B2.** This does not contradict `GET /api/audit`, which returns 403
  (`AuditEndpointExtensions.cs:30-36`). Its tenant comes from the caller's own
  authorized claim, never from a route or header — its own comment (`:25-29`): "a
  `?tenantId=` here would be exactly the cross-tenant query path ADR-009
  forbids." With no caller-supplied tenant there is nothing to enumerate. Rule B1
  binds routes that **take a tenant id from the caller**; audit does not and
  stays as it is.

---

## §C. The token

**This ADR ratifies ADR-026 §D4's token shape `{tenantId:N}.{secret}`**, and the
ratification is not a courtesy — it is a correction of this seat's own lane
draft, which called a hash-to-tenant lookup "the one read that must happen before
a tenant scope exists". That read is not merely avoidable: **under the policy
this same section specifies it is impossible.** `workspace_invitation` carries
`FORCE ROW LEVEL SECURITY` and one `tenant_isolation` policy, so an unscoped
`SELECT … WHERE token_hash = …` returns **zero rows** (Context fact 2). Serving it
would have required either a third policy widening the invitation table on a
token-hash GUC, or `BYPASSRLS` — forbidden by ADR-009 `:57`. The tenant-prefixed
token removes the read entirely. It is adopted.

- **Rule C1.** The secret half is **256 bits from a CSPRNG**
  (`RandomNumberGenerator`), base64url. Never a GUID, never a counter, never
  derived from the email or the tenant.
- **Rule C2.** **Only a SHA-256 hash of the secret half is stored.** The token is
  not recoverable from the database, a backup, a log or an audit row. A read of
  `workspace_invitation` cannot mint an acceptance.
- **Rule C3 — the tenant prefix is a routing value, not a credential.** It
  discloses nothing the holder does not already obtain: tenant ids already appear
  in route paths, and the pre-accept read returns the workspace name anyway. The
  secret half remains the entire credential.
- **Rule C4 — parse before scope (normative, and the reason the shape is safe).**
  The prefix is caller-controlled text that ends up selecting a tenant scope, so
  it must be parsed with `Guid.TryParseExact(prefix, "N")` **before** it reaches
  `BeginScope`; a parse failure is **404** (§B), never 400, never 500, and never
  a scope entry. The type system already enforces this and must be kept that way:
  `ITenantContext.BeginScope` takes `TenantId` (`ITenantContext.cs:26`) and
  `TenantId` is `readonly record struct TenantId(Guid Value)` (`TenantId.cs:7`) —
  constructible **only** from a parsed `Guid`. **No string-taking overload or
  implicit string conversion may be added to `TenantId`.** This matters because
  `BuildSetCommandText` interpolates the tenant into the `SET` statement and
  justifies that *solely* by the Guid invariant
  (`TenantRlsConnectionInterceptor.cs:103-107`, comment `:104-106`): the token
  prefix is the first caller-controlled value in the product that reaches that
  code path, and the Guid parse is what keeps the justification true.
- **Rule C5 — first statement inside the scope is the hash match.** Having
  entered a caller-named tenant scope, the implementation reads **nothing** until
  `token_hash` matches: not the workspace name, not the role, not a count. A miss
  disposes the scope and returns 404. Without this rule the pre-accept endpoint
  becomes a workspace-name oracle for any guessed tenant id, which would hand
  back through the front door exactly what the token shape just closed.
- **Rule C6 — no constant-time comparison.** Lookup is an index seek on the hash;
  there is no secret-to-secret comparison. Recorded so nobody "hardens" it into a
  table scan.
- **Rule C7 — expiry 7 days**, stored absolute (`expires_at`), evaluated
  server-side against `IClock`.
- **Rule C8 — no server-held key, in w14 or after.** No HMAC, no signing key, no
  Key Vault entry for the token. A signed token would buy stateless verification
  this design cannot use (single use, revocation and expiry all require the row
  to exist anyway) while costing a vault secret, an environment variable, a
  rotation story and the property that **rotation invalidates every outstanding
  invitation**. ADR-011 `:95-97` reserves a slot for "signing config"; this design
  simply does not use it — a narrowing, not a contradiction. **This is the
  condition cloud-architect's zero-delta confirmation rests on, and it holds.**
- **Rule C9 — transport hygiene (answers OQ-w14-cl-01: the link carries the token
  in a URL *fragment*).** The token must never appear in a URL path or query
  string server-side and must never be logged. The accept link is
  `/invite/accept#<token>` — site-relative per ADR-005's w14 footer, and a
  **fragment**, which is never transmitted to any server. `staticwebapp.config.json`
  rewrites every non-asset path to `/index.html`, so the platform logs the accept
  path; a query string would be logged with it, would ride the `Referer` header to
  every third-party asset the accept page loads, and would persist in browser
  history. A fragment does none of that, costs nothing, and is compatible with
  `new URL(acceptUrl, window.location.origin)`. The SPA reads the fragment,
  clears it from the address bar, and sends the token in the
  **`X-Invitation-Token` header** (ADR-026 §D5), never as a query parameter.
- **Rule C10.** The token is **never** persisted client-side — not in
  `localStorage`, not in `sessionStorage`, not in any store. It is rendered once
  from the 201 response and is gone on reload. A token in `localStorage` is a
  token in every XSS payload's reach. The 201 body carries it exactly once;
  response-body logging must not be enabled for these routes.

---

## §D. The lifecycle

### D.1 — Issue (NW-58; closes the Context fact 1 hole)

- **Rule D.1a.** `POST /api/workspaces/{tenantId}/invites` requires a presented
  identity (else **401**), a live membership in the **route** tenant (else
  **404**), and the `Admin` role resolved per §E (else **403**). **This is the
  highest-priority security task of the wave.** If the decomposer cannot fit
  NW-58 whole, this guard still ships as its own task.
- **Rule D.1b.** The invited role comes from the body and may be any role in the
  catalog **including `Admin`** — an Admin may create another Admin. What is
  forbidden is a non-Admin reaching the endpoint at all, and any *self*-assignment:
  the caller's own role is never read from a body, anywhere in this wave.
- **Rule D.1c.** The tenant is the **route** value, verified against membership.
  `X-Tenant-Id` is not an input to this endpoint.
- **Rule D.1d.** Issuing writes `workspace_user` (if absent) **and the invitation
  row — never a membership**.

### D.2 — Bootstrap (NW-02)

- **Rule D.2a.** `POST /api/workspaces` requires a presented identity; absent →
  **401**. This changes today's deliberately anonymous signup step
  (`WorkspaceEndpointExtensions.cs:17-18`), because the endpoint now writes an
  identity-keyed grant.
- **Rule D.2b.** The creator is `Admin` **of the tenant they just created, by
  virtue of creating it**. A `role` field in the body is **ignored**.
- **Rule D.2c.** Creating a tenant grants no read of any existing tenant.
- **Rule D.2d.** `workspace`, the role catalog, `workspace_user` and
  `workspace_membership` land inside **one** `BeginScope(workspace.TenantId)` and
  **one** `SaveChangesAsync` — the scope `WorkspaceProvisioningService.cs:42`
  already opens. The workspace row *is* the first row of its own tenant (doc
  `:17-21`; `UNIQUE INDEX ix_workspace_tenant_id`, `identity-workspace.sql:69`),
  and RLS `WITH CHECK` (`:145-147`) accepts a write only under its own claim. A
  partial bootstrap is today's bug and must not be reachable by a failure path
  either. **This path stays strictly one-scope-per-request**; §F.2's exception is
  for discovery only.
- **Rule D.2e (backfill).** Existing `dev` tenants have no membership row. A
  "claim this workspace" endpoint is a **tenant-takeover primitive** — whoever
  calls first becomes Admin of a tenant holding another user's uploads — and is
  **refused**. The backfill is an operator/CI job inserting membership from
  `(tenant id, email)` pairs supplied at HITL, audited as
  `workspace.membership.backfilled` with the operator as actor. An unclaimed
  stale `dev` tenant is a smaller problem than a takeover path.

### D.3 — Accept

- **Rule D.3a.** Accept requires a presented identity (**401** without): there
  would otherwise be no subject to bind.
- **Rule D.3b.** The signed-in identity's email must match the invited email
  **case-insensitively**; mismatch is **403** with a reason that does not echo the
  invited address. An unrestricted forwardable link is an open door into a
  customer tenant and in the pilot there is no other gate. Risk and cost recorded
  in OQ-sec-002.
- **Rule D.3c.** On success, in **one transaction under the invitation's tenant
  scope**: (1) resolve the `workspace_user` row the invite wrote — it exists,
  which is exactly why `WorkspaceSignIn`'s "sign-in never provisions" invariant
  stays intact; (2) bind the Entra subject via the already-written
  `LinkSignInAsync:118-145` — **unchanged**; (3) insert the membership at the
  invited role; (4) stamp `accepted_at`. Partial acceptance must be unreachable.
- **Rule D.3d (single use).** A second accept → **409** for the same identity,
  **404** for any other. Race safety is **already schema-enforced**: the unique
  index `ix_workspace_membership_workspace_user_id_workspace_role_id`
  (`identity-workspace.sql:90`) means two concurrent accepts produce one row and
  one unique violation. **The task asserts this rather than re-implementing it,
  and must translate the violation to 409, not 500.**
- **Rule D.3e (pre-accept minimal disclosure).** Before acceptance the API
  returns **workspace name, offered role and expiry — nothing else**. Not the
  roster, not the inviter, not the member count, not any business datum, and
  **not the invited email** (echoing it turns a leaked link into an
  address-discovery tool). A token holder is not yet a member.

### D.4 — The roster (NW-04; answers OQ-w14-005)

- **Rule D.4a.** **Any live member** may read the roster. The design shows it to
  Procurement (`screens-v2.md` §10), and a team of five cannot operate if only
  Admins can see who is in the workspace.
- **Rule D.4b.** A non-member gets **404** — never 403, never an empty 200. An
  empty 200 is also an oracle ("that tenant exists and is empty").
- **Rule D.4c.** Only an **Admin** may invite, revoke or remove. Read ≠ write.
- **Rule D.4d (privacy).** Email addresses are personal data and are returned
  **only** to members of that tenant. No endpoint in this wave exposes an email to
  a non-member.
- **Rule D.4e.** `status` is **derived, never stored** (ADR-026 §D3), so the
  roster cannot disagree with the grant it describes.
- **Rule D.4f (verify, then scope, then read).** The route tenant is entered as a
  scope **after** the caller's membership in it is verified — not "scope and see
  what comes back", which converts an authorization question into an
  empty-result question and yields 200-with-nothing instead of 404.

### D.5 — Remove, revoke, re-invite

- **Rule D.5a.** `DELETE …/members/{membershipId}`: identity (401) → membership
  (404) → `Admin` (403) → **last-Admin guard (409)**. An Admin may remove
  themselves **unless** they are the last Admin. Invariant: at least one live
  Admin membership per tenant, always — a tenant with zero Admins is
  unrecoverable without operator intervention. The guard is a **pure domain
  function**, provable without a database.
- **Rule D.5b (revocation is immediate because nothing caches authorization).**
  Membership and role are resolved from the database on every request; NW-01/NW-03
  revalidate the current workspace against the caller's list. There is no session
  and no token to revoke, so removal takes effect on the **next** request — the
  strongest form available, depending on no expiry.
- **Rule D.5c (stale invitations).** Removing a member **revokes that email's
  unaccepted invitations in that tenant in the same transaction**. Without this a
  still-valid link re-admits them with no new invite, breaking NW-58's own
  "re-adding needs a new invite".
- **Rule D.5d (re-invite).** After removal the membership row is gone, so
  `InviteAsync`'s "already holds the {role} role" refusal (`:90-96`) no longer
  fires and a fresh invite succeeds. The `workspace_user` row **remains** (audit
  continuity; the external subject stays bound under `identity-workspace.sql:125`)
  and grants nothing on its own — which is true only because §F.1d gates
  discovery on membership.
- **Rule D.5e (RAG closes too).** A removed member's Ask calls for that tenant
  retrieve nothing. This is structural under ADR-011 (authz → tenant scope →
  retrieval) and w14 **strengthens** it: the tenant used for retrieval becomes a
  *verified membership* fact instead of a client-asserted `X-Tenant-Id`.
  "Removed from the workspace but Ask still answers about its contracts" is
  exactly the failure ADR-011 exists to prevent, and it gets its own test.

---

## §E. The Admin gate (NW-14) — a client-declared role is never an authorization source

Authorization for an Admin-only action resolves from, in order:

1. **Role claims** on an authenticated principal (`WorkspaceRoleResolver.cs:50-55`)
   — the ADR-010 end state;
2. **the `workspace_membership` row** for the caller's identity in the target
   tenant (`:85-119`).

**Nothing else.** `X-Role` and `X-Workspace-Role` (`:37-38`, read at `:70-83`) are
**no longer admissible for an authorization decision**.

- **Rule E1.** The header role is admissible **only** for non-authoritative UI
  shaping (`GET /api/capabilities`). It is never consulted by `IsAdminAsync`
  (`:66-68`) or by any endpoint guard.
- **Rule E2.** Where header and membership disagree, **membership wins in both
  directions**: a header claiming `Admin` never grants, and a header claiming
  `Procurement` never revokes a real Admin's rights.
- **Rule E3.** This executes NW-14's own instruction — "Do not 'fix' this by
  sending a spoofable `X-Role: Admin` from the SPA as the product solution."

This is cheap and breaks nothing. `Grep` over `web/src` for
`X-Role|X-Workspace-Role` returns **zero matches** — the SPA sends `X-Tenant-Id`
and `X-User-Id` only (`client.ts:49-54`), so **no client changes**. The one
surviving reader is `CapabilitiesEndpointExtensions.cs:54`, whose own doc comment
(`:44-48`) already draws this exact line: it "fails closed on the 'show admin
entries' axis specifically … never a hard 401/403 the way `AuditEndpointExtensions`
is, since the catalog itself is not sensitive, tenant data". **Rule E1 restates a
convention this codebase already holds**; letting the header drift into an
authorization decision would be the actual regression.

**What actually fixes N9**: no new backend authorization code. Once NW-02 writes
the creator's membership row, `ResolveMembershipRoleAsync`'s join (`:104-114`)
returns `Admin` and the existing gate passes. NW-14 is a **proof** task, not a fix
task. Demoting the header is a deletion: `ResolveAsync`'s middle branch (`:57-60`)
is removed so the order becomes claims → membership, leaving `TryResolveHeaderRole`
one caller.

---

## §F. RLS consequences

### F.1 — Discovery: one new policy, `SELECT`-only, fail-closed (NW-01)

Options weighed and **rejected**: a cross-tenant directory table without RLS (a
second source of truth RLS does not protect, drifting toward "still listed after
removal"); a `BYPASSRLS` role (ADR-009 `:57` forbids it outright); identity-keyed
policies on all four workspace tables (four widened tables and a policy on
`workspace_membership` that must subquery `workspace_membership` — clever RLS is
how holes happen).

**Chosen: one identity-keyed policy on `workspace_user` only, then one
tenant-scoped read per candidate.**

```sql
CREATE POLICY identity_self ON workspace_user
    FOR SELECT
    USING (
        nullif(current_setting('app.identity_subject', true), '') IS NOT NULL
        AND (
            lower(email) = lower(nullif(current_setting('app.identity_subject', true), ''))
            OR external_subject_id = nullif(current_setting('app.identity_subject', true), '')
        )
    );
```

- **Rule F.1a.** `FOR SELECT` only, **no `WITH CHECK`** → it can never authorize a
  write. Cross-tenant inserts remain impossible.
- **Rule F.1b.** The `IS NOT NULL` guard makes it **fail closed**: an unset or
  empty `app.identity_subject` widens nothing.
- **Rule F.1c.** One table, one column pair — the widening is auditable by reading
  six lines. `workspace`, `workspace_role` and `workspace_membership` keep exactly
  one policy each, unchanged.
- **Rule F.1d (the removed-member trap).** A candidate tenant is included **only
  if a live `workspace_membership` row is confirmed in the per-tenant read**. A
  removed member keeps their `workspace_user` row by design, so a
  `workspace_user`-driven list would still show them the tenant they were removed
  from. **Membership, not user existence, is the grant.**
- **Rule F.1e.** The discovery read returns **candidate tenant ids only**. Every
  tenant *fact* — name, role, count, NW-24's columns — is read in a per-tenant
  scoped read under that tenant's real claim.
- **Rule F.1f.** Cap the candidate set at **50** (memberships per identity are
  1–2 in the pilot). Beyond the cap: return the cap and audit
  `workspace.list.truncated`. An unbounded loop keyed on caller-controlled
  identity is a DoS shape.
- **Rule F.1g.** The response never contains a tenant the caller has no live
  membership in — not its id, name or count. A crafted `X-Tenant-Id` cannot widen
  it, because the list is keyed on **identity** and the tenant header is not an
  input to the endpoint at all (**N5** holds by construction, not by a guard that
  can be forgotten).

### F.2 — The GUC, and the injection sink both lanes found independently

- **Rule F.2a.** `app.identity_subject` is set by the same interceptor mechanism
  as `app.tenant_id` and is **`RESET` on connection close**, for the same
  pooled-connection reason (`TenantRlsConnectionInterceptor.cs:78-101`).
- **Rule F.2b (normative).** It must be set with
  **`SELECT set_config('app.identity_subject', @identity, false)` — a regular
  statement, with a real bind parameter — never by copying `BuildSetCommandText`.**
  The interceptor inlines the tenant and justifies it *solely* because `TenantId`
  wraps a Guid (`:103-107`, comment `:104-106`); an identity subject is
  caller-controlled text from `X-User-Id`, so the same pattern would let
  `X-User-Id: x'; SET app.tenant_id = '<victim>'; --` repoint the RLS claim
  mid-request. The precision matters: that comment also states **"Postgres
  `SET`/custom GUCs do not accept bind parameters"**, which is true — so the
  remedy is a *different statement form*, not a parameter added to `SET`. A task
  that reaches for `SET` will find it cannot bind and will interpolate.
  **`set_config` is the fix; interpolation is the vulnerability.** A negative test
  with `'`, `;` and `--` is mandatory.
- **Rule F.2c.** `app.identity_subject` is **not interim**: after NW-05 it carries
  the token's `sub`/`oid` instead of the header value. Only its *source* changes,
  at the §A seam — and the policy already matches `external_subject_id` as well as
  `email`, so the swap needs no policy change either. This is deliberate: the wave
  adds no mechanism that must later be torn out.

### F.3 — Scope discipline: two named exceptions, and only two

`ITenantContext.cs:23-24` expects one scope per request. w14 introduces exactly
two departures, both named here so they stay singular.

- **Exception 1 — discovery (NW-01).** `GET /api/workspaces` is the **only**
  request permitted to enter more than one tenant scope. Its rules: (1) scopes are
  entered **sequentially, never nested**, each disposed before the next; (2) each
  per-tenant read runs on its **own connection**, because the claim is
  established at connection open (`:22-35`) — no scope change inside an open
  transaction, ever; (3) the multi-scope region is confined to one named service
  method, so a reviewer finds every scope entry in one file; (4) **any other
  endpoint entering a second scope is a defect, not a precedent.**
- **Exception 2 — accept (NW-58).** Pre-accept and accept enter a scope from a
  **caller-supplied** tenant id with no membership check — the only place in the
  product that does so. It is bounded by Rule C4 (Guid parse before scope) and
  Rule C5 (the hash match is the first statement inside it, and a miss returns
  404 with nothing read). Both rules are the exception's justification, not
  decoration; remove either and it becomes a tenant oracle.
- **Rule F.3a (general, and it outlives this wave).** A change of tenant scope
  takes effect only on a **connection opened after** the scope was entered. Any
  path reading tenant A then tenant B must let the connection close between them
  (separate unit of work per tenant). A nested scope on an already-open connection
  keeps running under the **first** tenant's claim — a cross-tenant read RLS will
  not catch, because the claim it is enforcing is the stale one.

### F.4 — The new table

`workspace_invitation` is an **ordinary tenant table**: `tenant_id` not null and
indexed, `ENABLE` + `FORCE ROW LEVEL SECURITY`, and a `tenant_isolation` policy
with **both** `USING` and `WITH CHECK`, identical in shape to
`identity-workspace.sql:176-180`. It gets **no** `identity_self` policy — the
widening is confined to `workspace_user`. Columns and indexes are ADR-026 §D4;
the security-relevant constraints are: **no plaintext token column**, and a
partial unique index `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND
revoked_at IS NULL` so re-invites cannot accumulate valid links. The policy ships
**in the same migration as the table** — `TenantRlsDeployableScriptCheckTests`
fails the build otherwise, and this wave must not be the first tenant table to
skip it.

### F.5 — Cross-module reads

The validated-contract count comes from another module's RLS'd tables. **It must
be produced by a tenant-scoped read under that tenant's claim**, never by a
cross-tenant aggregate or a group-by over all tenants. A number is data: "3
contracts" about a tenant you do not belong to is a disclosure.

---

## §G. Audit (ADR-011)

`AuditEvent` is append-only and DB-enforced — the `AddAppendOnlyEnforcement`
migration rejects `UPDATE`/`DELETE` "regardless of which role or connection issues
it" (`AuditEvent.cs:8-13`) — tenant-scoped (`:22`), with
`Actor`/`Action`/`ResourceType`/`ResourceId`/`OccurredAt`/`Detail?` (`:27-55`).
`ResourceId` is deliberately a plain string so it can carry an id from outside
this module's scheme (`:41-44`). **No schema change.**

| Action | Actor | ResourceType / ResourceId |
|---|---|---|
| `workspace.created` | creating identity | `Workspace` / tenant id |
| `workspace.membership.granted` | creating identity (bootstrap) or accepting identity (accept) | `WorkspaceMembership` / membership id |
| `workspace.invitation.issued` | inviting Admin | `WorkspaceInvitation` / invitation id |
| `workspace.invitation.accepted` | accepting identity | `WorkspaceInvitation` / invitation id |
| `workspace.invitation.rejected` (email mismatch) | attempting identity | `WorkspaceInvitation` / invitation id |
| `workspace.invitation.revoked` | removing Admin, or system on removal (D.5c) | `WorkspaceInvitation` / invitation id |
| `workspace.membership.removed` | removing Admin | `WorkspaceMembership` / membership id |
| `workspace.membership.backfilled` | operator (D.2e) | `WorkspaceMembership` / membership id |
| `workspace.list.truncated` | listing identity | `Workspace` / cap value (F.1f) |

**Never recorded**: the token, the token hash, any raw `Authorization`/`X-User-Id`
header dump, any contract or business datum. `Detail` may carry the invited email
and the role — membership facts inside that tenant, visible to its members anyway
— and nothing else.

**Rule G1.** These endpoints all require an identity, so the `"unattributed"`
actor literal (`DocumentsEndpointExtensions.cs:71`, NW-32) **cannot occur on any
of them**. That closes NW-32 for this surface; the rest stays W15.

---

## §H. Tests every task in this wave must carry

Non-negotiable; each names the rule it defends.

- **T1 — another tenant never appears.** Identity `A` is a live member of `T1`
  only. (a) `GET /api/workspaces` as `A` returns exactly `[T1]`; `T2`'s id, name
  and count appear nowhere (F.1e/F.1g). (b) `GET …/{T2}/members` as `A` → **404**,
  body carries no tenant name (D.4b). (c) With `app.identity_subject` = `A` and no
  tenant claim, a direct read of `workspace_user` returns only `A`'s own rows
  (F.1a). (d) With the same claim, reads of `workspace`, `workspace_role` and
  `workspace_membership` return **zero** rows — proving the widening is confined
  to one table.
- **T2 — a spoofed header is rejected.** (a) A Procurement member of `T1` sending
  `X-Role: Admin`: `DELETE /api/documents/{id}` → 403; `POST …/reprocess` → 403;
  `POST …/invites` → 403; `DELETE …/members/{id}` → 403; repeat with
  `X-Workspace-Role: Admin` (§E). (b) `A` sending `X-Tenant-Id: {T2}` on every
  tenant read → 404/empty, no `T2` row read or written (B1, N5). (c) An
  `X-User-Id` with no membership in `T1` → 404 on every membership route (A2).
- **T3 — the invite-hole regression (the one that must exist).**
  `POST /api/workspaces/{T1}/invites` with **no** identity → **401** (today: 201
  Created with a real Admin membership row). Then non-member → 404; `T1`
  Procurement → 403; `T1` Admin → 201 (D.1a).
- **T4 — creator is Admin, and only that.** Create → rows in `workspace`,
  `workspace_user`, `workspace_membership`(Admin); delete a document as creator →
  204; reprocess → 200; a `role` field in the create body is ignored; create with
  no identity → 401 (D.2a-c, N1, N9).
- **T5 — RLS fail-closed on the new table.** Outside any scope,
  `workspace_invitation` reads return zero rows and an insert is rejected by
  `WITH CHECK` (F.4).
- **T6 — scope discipline.** In one unit of work: enter `T1`, read; enter `T2` on
  the **same open connection**, read → the second read must not return `T1` rows
  and must not silently run under `T1`'s claim. Paired: `GET /api/workspaces`
  enters its scopes sequentially, each on its own connection, and no other
  endpoint enters more than one (F.3).
- **T7 — removal is immediate and total.** After removing `B` from `T1`: (a) `B`'s
  next `GET /api/workspaces` no longer lists `T1`, although `B`'s `workspace_user`
  row still exists (F.1d, D.5b); (b) `B`'s next document read/write in `T1` → 404;
  (c) `B`'s unaccepted invitations in `T1` are revoked and the stale link → 404
  (D.5c); (d) **`B`'s Ask call scoped to `T1` retrieves nothing from `T1`'s
  corpus** (D.5e, ADR-011).
- **T8 — re-invite after removal** succeeds at the same role and the new token
  differs from the revoked one (D.5d).
- **T9 — last-Admin guard.** Sole Admin removing self → 409; sole Admin removing
  the only other Admin → 409; with two Admins either removal → 204 (D.5a).
- **T10 — token hygiene.** The stored column is a hash, not the token; the token
  appears in no audit row and no log sink; the pre-accept response contains only
  workspace name, role and expiry — no email, no roster, no counts; the accept
  request carries the token in a header, never a query string; the accept **link**
  carries it in a fragment (C2, C9, D.3e, §G).
- **T11 — accept binding.** Email mismatch → 403 with **no** membership row
  written; expired → 410; unknown/revoked/malformed → 404; second accept → 409;
  two concurrent accepts → exactly one membership row **and a 409, not a 500**
  (D.3b-d). Plus the end-to-end regression: an invitation issued through the new
  path is acceptable, i.e. `LinkSignInAsync` finds the `workspace_user` row the
  invite wrote.
- **T12 — token-prefix hardening.** A token whose prefix is not a valid Guid → 404
  and **no** scope entered; a token with a valid foreign tenant prefix and a wrong
  secret → 404 with **no** workspace name disclosed (C4, C5).
- **T13 — the GUC is not an injection sink.** An identity containing `'`, `;` and
  `--` sets no other GUC and does not repoint `app.tenant_id` (F.2b).
- **T14 — the ADR-010 retirement test (written now, activated in W15).** With a
  validated token present, `X-User-Id` is **ignored**, not overridden: token `A` +
  `X-User-Id: B` acts as `A`. A token carrying `roles`/`tenant_id` claims does
  **not** grant that role or tenant (A3, §I).

---

## §I. The forward binding this ADR places on ADR-010

When ADR-010's JWT lands, **the token carries identity only**. Tenant and role are
resolved from the database per request; a `tenant_id` or `roles` claim is **never**
the authorization source.

This is not merely forward-looking — it **amends a shipped contract**.
`WorkspacePrincipalAuthorization.cs` is built on the opposite premise today:
`TenantIdClaimType = "tenant_id"` (`:35`), `TryAuthorize` reads the tenant from
that claim (`:62-67`) and the role from `ClaimTypes.Role` (`:69-70`), and
`AuditEndpointExtensions.cs:30-31` consumes it. Left alone, W15 ships a
**stale-authorization window** (a removed member keeps access until token expiry)
and a **last-Admin-guard bypass**, silently undoing D.5a and D.5b.

The remedy is cheap and the code says so: `:32-33` records that "whichever task
adds it (ADR-010) is free to change this constant, **since every caller goes
through this one place**." NW-05/NW-08 (W15) change `TryAuthorize`'s tenant/role
resolution from claim-reading to database-reading **at that single seam**, keeping
its fail-closed posture (`:42-44`). **No w14 task edits that file** — recording
the rule now is what makes W15 a one-file change instead of a re-litigation.

---

## Implications for the decomposition

1. **The invite guard (D.1a) is the wave's highest-priority security task and
   must merge before NW-01.** This wave converts a dormant hole into a working
   cross-tenant join path; the guard is one resolver call plus a check.
2. Every new or changed endpoint consumes the **one** identity seam (A1). No
   endpoint added in this wave reads `HttpRequest.Headers` directly.
3. Every rejection uses §B. **No 403 for a non-member — 404 (B1).**
4. The `identity_self` policy (F.1) and the `workspace_invitation`
   `tenant_isolation` policy (F.4) ship **in the same migration** as the schema
   they protect.
5. `app.identity_subject` is set with `SELECT set_config(…, @p, false)` and
   `RESET` on close. **Copying `BuildSetCommandText` is a defect** (F.2b), and the
   negative test is mandatory.
6. The token's tenant prefix is `Guid.TryParseExact(…, "N")`-parsed before any
   scope entry; failure is 404 (C4). No string-taking overload may be added to
   `TenantId`.
7. Inside the accept scope, the token-hash match is the **first** statement (C5).
8. Discovery is gated on a **live membership row**, never on `workspace_user`
   (F.1d), and capped at 50 (F.1f).
9. Removal revokes that email's live invitations in the same transaction (D.5c).
10. The last-Admin guard is a pure domain function, unit-testable without a
    database (D.5a).
11. The accept link uses a **fragment**; the API call uses the
    `X-Invitation-Token` **header**; the token is never persisted client-side
    (C9, C10).
12. Nine audit actions (§G), no schema change. The token and its hash are never
    written to an audit row.
13. T1–T13 are w14 tasks; **T14 is written now and activated by NW-05/NW-08 in
    W15**.
14. No w14 task edits `WorkspacePrincipalAuthorization.cs` (§I).

## Assumptions

- **OQ-sec-002** — the accepting identity's email must match the invited address
  (D.3b). *In force*: yes, case-insensitively. Cost: an Entra account whose
  username differs from the invited mailbox (an alias) cannot accept and the Admin
  must re-invite the right address — which this wave makes work. Loosening it
  makes the link forwardable into a customer tenant; if the pilot hits the alias
  case, "invite by Entra UPN" is the fix, not dropping the check. The
  product-owner may overrule for the pilot, in which case the accept event must
  record both addresses.
- **OQ-sec-003** — expiry is **7 days**, server-side absolute. The product-owner
  may change the number; the mechanism does not change.
- **OQ-sec-004** — `POST /api/workspaces` stays effectively open (any presented
  identity may create a tenant). *In force*: acceptable for `dev`/`demo` — it is
  the signup step and D.2c means a new tenant grants nothing over an existing one.
  Rate limiting is a later-wave item, not a w14 blocker.
- **OQ-sec-006** — the §I code change touches `GET /api/audit`, which is **NW-08
  (W15)**. *In force*: w14 records the rule and writes T14 as a forward test; the
  code lands with NW-05/NW-08. If the table wants it earlier it is a W15 ordering
  note for delivery-manager, not a new w14 task.
- **OQ-w14-sa-01** (software-architect's, restated because the security ruling
  shapes it) — existing `dev` workspaces cannot be attributed retroactively; the
  operator supplies `(tenant id, email)` pairs at HITL or recreates them (D.2e).
- Line numbers are the wave base `1650213`; on `origin/main` (`25b10da`,
  post-rebrand) read every `Contigo.*` path as `Raffa.*` (W14-01, OQ-w14-003). The
  rename is mechanical and does not move lines within a file.
- RLS behaviour asserted here is **not** backed by a green local run: those suites
  need Postgres Testcontainers and Docker will not start on this machine. They are
  CI-only, which makes T1c/T1d/T5/T6/T13 gating in CI rather than locally.

## Amendment (2026-09-10, wave w14 — Rule C10 gets the mechanism that enforces it, and one cross-reference is made resolvable)

Serves **NW-58, NW-03**. Everything above is unchanged and in force. This footer
adds **no new rule**: it names the store that silently violates Rule C10 and the
ordering that makes C10's accepted remedy unreachable. Same class as F.2 — where
both lanes agreed on "bind the parameter" and the interceptor's own comment made
that literally impossible, so the statement form had to be named or the task would
have shipped the vulnerability the rule forbids.

**1. Rule-id concordance, so an accepted cross-reference resolves.** ADR-026
`:239` cites *"ADR-025 Rule 5.2f/5.2g"*. Those ids exist only in this seat's lane
draft (`draft/next/security-architect/w14.md:694`, `:704`); they were promoted into
this ADR as **Rule C9**, and the client-store rule as **Rule C10**. ADR-026 was
written in the same round from that draft, before this file was on disk, which is
why it is the sole outlier — ADR-005 `:246`, ADR-016 `:107`, ADR-018 `:185`,
ADR-020 `:411` and the INDEX all cite `Rule C9` correctly. **Read `5.2f`/`5.2g` as
`C9`.** Recorded here rather than by editing ADR-026, which this seat does not own;
ADR-026's clause is substantively identical and needs no change.

**2. Rule C10 is violated by the auth library, not by our code — verified on
disk.** C10 says the token is never persisted client-side, "not in `sessionStorage`
… not in any store". The accepted client composition defeats it in the one case
that matters:

- `msalConfig.ts:19-33` sets **no** `navigateToLoginRequestUrl`, and `:31` sets
  `cacheLocation: SessionStorage` — MSAL's temp cache is the exact store C10 names.
- `App.tsx:91-106` — `if (!account || !workspace)` renders `SignInRoute`
  **in place**. No router is mounted at that level (`routes/signin/index.tsx:19-21`:
  *"No client-side router is wired into this app yet"*), so **the address bar is
  unchanged**.
- `routes/signin/index.tsx:34` is the **only** `loginRedirect` call site in
  `web/src` (grep: one match).

So a signed-out invitee who opens `/invite/accept#<token>` is shown the sign-in
screen **at the accept URL**, and the auth redirect is initiated while
`window.location.href` still carries the token. MSAL's documented default for
`navigateToLoginRequestUrl` is `true` — it records the current href to navigate
back to it. That is the invitation token written into `sessionStorage` by the
library, for the whole redirect round-trip, and left behind if the round-trip is
abandoned.

**The accepted remedy cannot run.** Client-architect's ruling (ADR-012 w14,
ADR-020 `:411`) is that the accept route clears the fragment on mount. `App.tsx:91`
short-circuits **before** the accept route ever mounts, and the signed-out case is
precisely the case where `loginRedirect` fires. The rule and the gate are in the
wrong order. Neither lane could see this alone: C10 is this seat's, the route
composition is client-architect's, and the defect lives in the seam.

**Rule C10a — `navigateToLoginRequestUrl: false`, set explicitly** in
`buildMsalConfig`. Explicit because a rule that depends on a library default is not
a rule. It costs nothing already wanted: the `redirectUri` is single and configured
(`msalConfig.ts:24`), and the accepted flow is already "sign in, then open the link
again", so URL restoration is not a behaviour this wave relies on.

**Rule C10b — the fragment is captured into memory and cleared from the address
bar before any code path that can initiate authentication renders a control.** In
practice: the accept path is handled above `App.tsx`'s `!account || !workspace`
short-circuit, or excluded from it. This is an **ordering** constraint, not new UI
and not a new route.

Both, not either: C10a fails closed if the route ordering is later refactored;
C10b fails closed if the library default changes or another `loginRedirect` call
site appears. They fail differently, which is why one is not enough.

**3. T15 — the token is in no browser store (new, joins §H as non-negotiable).**
Land on `/invite/accept#<token>` **while signed out**, initiate sign-in, and assert
that **no key in `sessionStorage` or `localStorage` holds a value containing the
token**, at every step of the redirect round-trip and after it. Paired: after a
successful accept, the same assertion holds. This test asserts the *property*, so
it is correct whatever MSAL's default turns out to be and whoever writes the route
— it does not encode this seat's reading of a library. T15 is a **w14** test and
belongs to the NW-03/NW-58 client task, beside the fragment-clearing assertion
ADR-020 `:411` already requires.

**4. Ownership, stated so no task is invented.** The one-line config change and the
route ordering are **client-architect's** files (`msalConfig.ts`, `App.tsx`, the
accept route). This footer states the security rule and the test; it creates **no
new task**, adds no endpoint, no column, no migration and no infrastructure — it
constrains two tasks NW-03 and NW-58 already own. Cloud-architect's zero-delta
confirmation is untouched: no server-held key, no environment variable, no secret.

**5. What this footer does not do.** It does not edit `waves/w14.md` — the record
is not wrong here, only silent, and appending to the artifact the gate read back
would mean the approved record is not the one that was checked. It does not touch
ADR-026, ADR-012, ADR-018 or ADR-020, which this seat does not own. The decomposer
reaches this footer through the NW-58 and NW-03 rows, which already name ADR-025 as
governing, and through the INDEX note.
