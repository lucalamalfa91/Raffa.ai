# ADR-009 — Tenant isolation (RLS + application authorization + retrieval scoping)

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: security-architect (owner); software-architect and product-owner concur at council-close
- **Locked citations**: `locked-decisions.md` row "Cloud/Environments" (two envs isolated), row "Auth/secrets" (OIDC, SSO-ready Entra), row "Backend" (modular monolith + worker). Product spec §3.2 (multi-tenancy), §14.1 (strict tenant isolation), Appendix C rule #4 ("Never include data in AI retrieval that the current user is not authorized to access").

## Context and problem statement

Raffa is a multi-tenant procurement intelligence product: many customer workspaces share one
relational store (PostgreSQL + pgvector) and one object store in each Azure environment. The product
spec §3.2 is unambiguous: *"Every business object must carry `tenant_id`. Tenant isolation must be
enforced at both application and database level. No cross-tenant query path is acceptable."* The same
section names the authorization chain and warns: *"Authorization must also constrain RAG retrieval;
inaccessible contracts must never enter the LLM context."*

The brief (§10 / §3) keeps a **shared** API + worker + database for `dev` and `demo` internally (each
environment isolated from the other, but tenants within one environment share infra). That means the
database itself — not just the application — must be the backstop against a tenant reading another
tenant's rows.

## Decision drivers

- **Database-level isolation is a hard product constraint**, not a nice-to-have (spec §3.2 verbatim).
- **Shared infra per environment** means a single application bug (forgotten `WHERE tenant_id`) must
  still be blocked by the database, not silently leak rows.
- **RAG retrieval** (Ask Raffa, spec §8.3) assembles contract sections/clauses into an LLM context.
  A retrieval path that is filtered only by role and not by tenant would exfiltrate cross-tenant content.
- Cost guideline: cheapest store that still satisfies isolation — so we cannot justify per-tenant
  dedicated databases in V1 (spec §3.2 "future enterprise option").

## Considered options

1. **PostgreSQL Row-Level Security (RLS) on every tenant-scoped table** — a `SET app.current_tenant_id`
   claim per connection, enforced by a `tenant_id = current_setting(...)` policy. Application still
   passes `tenant_id` explicitly; RLS is the backstop.
2. **Application-level scoping only** — every query filters by `tenant_id` in code; relied on developers
   to never miss a filter.
3. **Schema-per-tenant or database-per-tenant** — physical separation per workspace inside one env.

## Decision outcome

**Chosen: Option 1 — PostgreSQL Row-Level Security enforced on every tenant-scoped table, with the
application still passing `tenant_id` explicitly and the RLS policy acting as a non-bypassable
backstop.** Application-layer scoping is the primary, RLS is the guarantee. This satisfies the spec's
dual requirement (app **and** DB) at the cheapest SKU that supports RLS, without per-tenant databases.

### Consequences

- **Good**: A cross-tenant read is impossible even under developer error — RLS `FORCE ROW LEVEL
  SECURITY` + a policy keyed to `current_setting('app.tenant_id', true)` denies rows outside the
  connection's tenant. Every business table (`contract`, `document`, `supplier`, `renewal`,
  `savings_opportunity`, `quote`, `embedding`, `clause`, etc.) carries `tenant_id` (spec §3.2 entity
  tables).
- **Good**: Cost-neutral — no extra Azure resources vs a shared DB.
- **Bad**: RLS must be wired into the data-access path (a connection-scoped tenant claim set once per
  request/worker job, reset on connection close). No query may run under `BYPASSRLS`/superuser in the
  application path. Every new table added later must opt in to the same policy (a migration-time check).
- **Neutral**: The app still passes `tenant_id` on the query — this is belt-and-suspenders, not removal
  of the RLS layer.

## Pros and cons of the options

### Option 1 — RLS (chosen)
- Good: DB-level guarantee; cheap; standard PostgreSQL feature.
- Bad: discipline required to set/reset the tenant claim per connection and to never run as bypass.

### Option 2 — app-only
- Good: simplest to code.
- Bad: violates spec §3.2 "at both application and database level"; a single missed filter leaks.

### Option 3 — schema/database-per-tenant
- Good: strongest physical isolation.
- Bad: explicitly out of scope in V1, more cost/complexity, breaks shared-worker embeddings/index model.

## Implications for the decomposition

- Every migration for a tenant-scoped table **must** include `tenant_id` (not-null, indexed, FK to
  `workspace`/`tenant`) and an RLS policy. A CI migration check rejects any tenant-scoped table without
  one.
- The data-access layer must establish a per-request/per-job `SET app.tenant_id = <id>` before any
  query and clear it when the connection is returned to the pool.
- Background worker jobs (extraction, embeddings, renewal calc) must carry `tenant_id` from the queue
  message and set the same connection claim — a job derived from tenant A's document must never run in
  tenant B's context.
- Object-storage paths must be tenant-prefixed (`<tenant_id>/...`) and object access issued through a
  server-side path governed by the same tenant claim, never a client-supplied raw blob URL.

## Assumptions

- The chosen relational store (PostgreSQL + pgvector, software-architect's ADR) supports `FORCE ROW
  LEVEL SECURITY` on the cheapest managed SKU that meets product constraints (embedded in the
  software-architect SKU choice; if the SKU does not, this ADR forces a SKU that does).

## Amendment (2026-09-10, wave w14 — discovery, scope discipline, bootstrap)

Serves **NW-01, NW-02, NW-04, NW-58**. The Decision outcome above is unchanged:
RLS on every tenant table, application scoping primary, RLS the non-bypassable
backstop, **no `BYPASSRLS` in the application path**. This footer records what
w14 adds. Detail lives in **ADR-025**; where the two touch one mechanism,
ADR-025 governs.

**1. One new policy, and only one.** `GET /api/workspaces` must answer "which
tenants do I belong to" for a caller who has no tenant claim yet — and with the
claim unset every existing policy denies every row (fail closed,
`TenantRlsConnectionInterceptor.cs:53-57,66-70`). The answer is **one
identity-keyed policy on `workspace_user`, `FOR SELECT` only, permissive**,
keyed on a new GUC `app.identity_subject` and guarded by
`nullif(current_setting(...),'') IS NOT NULL` so an absent claim widens nothing.
`workspace`, `workspace_role` and `workspace_membership` keep exactly one policy
each, unchanged. A cross-tenant directory table, per-table identity policies and
a `BYPASSRLS` role were all considered and **rejected** (ADR-025 §F.1).

**2. Membership is the grant, not user existence.** A candidate tenant enters the
discovery result **only** when a live `workspace_membership` row is confirmed in
that tenant's own scoped read. `workspace_user` rows outlive membership by design
(audit continuity, and sign-in needs them), so a `workspace_user`-driven list
would show a removed member the tenant they were removed from.

**3. The identity GUC is parameter-bound, never interpolated.** Set it with
`SELECT set_config('app.identity_subject', @identity, false)` — a regular
statement that accepts bind parameters — and `RESET` it on connection close.
**Do not copy `BuildSetCommandText`** (`TenantRlsConnectionInterceptor.cs:103-107`):
it inlines the tenant and justifies that *solely* because `TenantId` wraps a Guid,
whereas an identity subject is caller-controlled text. Its own comment notes that
`SET` cannot take bind parameters — which is why the remedy is a different
statement form, not a parameter added to `SET`. A negative test with `'`, `;` and
`--` is mandatory.

**4. Two named exceptions to "one scope per request", and only two.**
`ITenantContext.cs:23-24` states the expectation; w14 departs from it twice, and
both are bounded:
- **Discovery** may enter one scope per candidate tenant — sequentially, never
  nested, each on its own connection, confined to one named service method, capped
  at 50 candidates.
- **Invitation accept** may enter a scope from a **caller-supplied** tenant id
  with no membership check — the only place in the product that does — bounded by
  a `Guid.TryParseExact` parse before scope entry (a failure is 404, never a scope)
  and by the rule that the token-hash match is the **first** statement inside the
  scope, so a miss discloses nothing.

**Any other endpoint entering a second scope, or entering a scope from
caller-supplied input, is a defect and not a precedent.**

**5. A scope change takes effect only on a connection opened after it.** The claim
is established in `ConnectionOpened` (`:22-35`), so a nested scope on an
already-open connection keeps running under the **first** tenant's claim — a
cross-tenant read RLS cannot catch, because the claim it enforces is the stale
one. Any path reading tenant A then tenant B must let the connection close
between them. This is general and outlives w14.

**6. Bootstrap.** `workspace` + the role catalog + `workspace_user` +
`workspace_membership`(Admin) are written in **one** scope and **one**
`SaveChangesAsync` under the new tenant's own claim — the workspace row is the
first row of its own tenant. A partial bootstrap must not be reachable by a
failure path. This path stays strictly one-scope-per-request.

**7. Verify, then scope, then read.** For any route carrying a tenant id, the
caller's membership is verified **before** the scope is entered. "Scope and see
what comes back" turns an authorization question into an empty-result question
and yields 200-with-nothing where the answer must be **404** (a 403 on a tenant
you do not belong to is a tenant-existence oracle).

**8. New tenant table.** `workspace_invitation` is ordinary: `tenant_id` not null
and indexed, `ENABLE` + `FORCE ROW LEVEL SECURITY`, a `tenant_isolation` policy
with both `USING` and `WITH CHECK`, shipped **in the same migration as the table**.
It gets no identity policy. Cross-module reads (the validated-contract count) run
under that tenant's own claim — never a cross-tenant aggregate.

## Amendment (2026-09-13, wave w15 — the Worker's tenant scope is caller-derived input)

Serves **NW-27**, and records the identity-swap consequence of **NW-05 / NW-06**.
The Decision outcome above is unchanged: RLS on every tenant table, application
scoping primary, RLS the non-bypassable backstop, **no `BYPASSRLS` in the
application path**. The w14 footer's eight clauses are unchanged and in force.
This footer gives the Implications section's third bullet — *"Background worker
jobs must carry `tenant_id` from the queue message and set the same connection
claim — a job derived from tenant A's document must never run in tenant B's
context"* — the mechanism it has never needed until now, because **w15 is the first
wave that actually builds that path**.

The NW-27 decision row is software-architect's; this footer is the ADR-009 rule it
must satisfy. Software-architect independently reached the same conclusion from the
other direction: `extraction_job` carries `ENABLE` **and `FORCE ROW LEVEL
SECURITY`** (`documents-contracts.sql:476-477`), `FORCE` binds the table owner too,
so "find every queued job across tenants" returns **zero rows** — which is why the
design publishes before it commits rather than sweeping. **A cross-tenant recovery
sweep is not available to this product, by construction. That is the constraint, not
a defect to be worked around.**

### 1. A queue message is untrusted input

**1a.** Anyone holding Send rights on the namespace can enqueue a message. Its
tenant id therefore gets **exactly the ADR-025 Rule C4 treatment**:
`Guid.TryParseExact(…, "N")` **before** `ITenantContext.BeginScope`. A parse
failure dead-letters the message and **enters no scope**. This keeps
`TenantRlsConnectionInterceptor.BuildSetCommandText`'s own justification — "the
tenant is always a parsed Guid" — true for the second caller-controlled value that
now reaches it.

**1b — the first statement inside the scope is the job-row match.** The Worker
reads **nothing** — not the document, not the blob, not a count — until the
`ExtractionJob` row named by the message is found **in that tenant**. A miss
disposes the scope and dead-letters. Without this ordering a forged message is a
read primitive against an arbitrary tenant. This is Rule C5's discipline (the
token-hash match is the first statement inside the invitation scope), reused
verbatim for the same reason.

**1c — one scope per message, and a message is one job.** Clause 4 of the w14
footer ("two named exceptions, and only two") is **unchanged**: the Worker is
**not** a third exception. One message → one tenant → one scope → one unit of
work, and the connection closes between messages. Clause 5 is why that last part
matters: a scope change takes effect only on a connection opened after it, so a
pooled connection carrying a stale claim is a cross-tenant read **RLS cannot
catch**, because the claim it enforces is the stale one.

### 2. The storage path must never come from the message

This is the sharpest consequence of ADR-027 §D11 and it is not visible from either
ADR alone. `DocumentStoragePath.EnsureWithinTenant`
(`SharedKernel/Storage/DocumentStoragePath.cs:28-38`) is the blob-side tenant
guard, called on the read and delete sides
(`AzureBlobDocumentStorage.cs:45`, `:64`). Its mechanism is
`storagePath.StartsWith(TenantPrefix(tenantId))` — **it compares two values the
caller supplies**. In the API both come from a verified request. In the Worker,
**if the path and the tenant both came from the message, the guard would compare
an attacker's tenant against an attacker's path and pass.**

**Rule: the Worker passes only the parsed tenant id (1a) and a storage path read
from the `document` row *inside that tenant's own scope* (1b).** The message
carries ids, never a path. The guard then means what it says. Promoting the
adapter into `Raffa.Storage` (ADR-027 §D11) is the right call for this reason too
— ADR-027 rejects duplicating it into the Worker because "two copies of it will
diverge", and a diverged copy of this particular guard is a cross-tenant blob read.

### 3. The message carries ids only

No contract text, no extracted facts, no personal data, no file bytes, no blob
SAS, no storage path (§2). A queue — and especially its **dead-letter queue**,
which retains bodies for operator inspection — is a log-like sink readable by
anyone with Listen rights, and ADR-011 forbids raw contract content in a log sink.
**ADR-011's w15 footer §2c depends on this clause**: it accepts a bounded
`listen`-only scaler credential as a fallback *because* a leaked listen key would
disclose identifiers rather than contract content.

### 4. Any new table this wave adds is an ordinary tenant table — and the guard that proves it is conditional

**4a — the rule, unchanged from clause 8.** If NW-27 adds an outbox, a job-claim
table or a rejection record, it carries `tenant_id` not null and indexed, `ENABLE`
+ `FORCE ROW LEVEL SECURITY`, and a `tenant_isolation` policy with **both**
`USING` and `WITH CHECK`, shipped **in the same migration as the table**. No
identity-keyed policy — the §F.1 widening stays confined to `workspace_user`.

**4b — the guard's real name.** The CI check the Implications section promises
("a CI migration check rejects any tenant-scoped table without one") is
**`backend/tests/Raffa.Tenancy/TenantRlsMigrationCheckTests.cs`** —
`Every_tenant_scoped_table_has_forced_row_level_security_and_a_policy` (`:29`). Its
own doc comment (`:9-12`) describes the mechanism: it runs in the normal
`dotnet test` step of `backend.yml`, migrates a disposable Postgres instance, and
fails the build if any tenant-scoped table is missing `ROW LEVEL SECURITY`,
`FORCE ROW LEVEL SECURITY`, or an actual policy. Recorded because a task told to
satisfy a **mis-named** test finds nothing, concludes the guard is absent, and
ships the table without the policy.

**4c — the coverage is conditional, and "the migration check covers it" is false
half the time.** The check discovers its table list **dynamically from the EF
model — every `TenantScopedEntity` subclass — of `DocumentsContractsDbContext`
alone** (`:31-44`). Automatic coverage therefore holds **only** when the new
entity subclasses `TenantScopedEntity` **and** lives in that context. The tree
already proves the gap: `workspace_invitation` (w14) lives in
`IdentityWorkspaceDbContext`, so w14 had to hand-write
**`WorkspaceInvitationRlsTests.cs`** for it. **So NW-27's task must either put the
new table in `DocumentsContractsDbContext` as a `TenantScopedEntity`, or ship a
hand-written per-table RLS test on the `WorkspaceInvitationRlsTests` pattern.**
A decomposer will otherwise assume coverage for free.

**4d — and a hand-written RLS test written the obvious way is green and
worthless.** `WorkspaceInvitationRlsTests:23-25` states the trap outright: its
assertions run "through a dedicated, deliberately unprivileged Postgres role …
a superuser connection would make this pass vacuously" (`:29-30`,
`raffa_invitation_app`). **Testcontainers hands you a superuser by default, and
RLS constrains neither a superuser nor a table owner.** Any test written under 4c
must create its own unprivileged role. This binds NW-27's task and is not visible
from this ADR's text alone — which is why it is now in it.

**4e — a `Rejected` document row is a tenant row like any other.** It needs **no
DDL** (`processing_status` is `character varying(30)` with no CHECK and no enum
type — software-architect verified it at `documents-contracts.sql:110`), so
ADR-021 is untouched. Two consequences are this seat's: it must produce **no
embedding rows in either corpus** (ADR-011 w15 footer §3b), and until its blob is
deleted the stored bytes obey the tenant-prefixed path rule above like any other
object.

### 5. The identity swap changes a value, not a policy

**5a — no policy changes in this wave.** NW-05 / NW-06 change the *source* of
`app.identity_subject` from a header string to the token `oid`. Rule F.2c already
guarantees the `identity_self` policy needs no edit: it matches
`external_subject_id` **and** `email`. `workspace`, `workspace_role` and
`workspace_membership` keep exactly one policy each, unchanged, and the single
identity-keyed policy on `workspace_user` (clause 1) is untouched.

**5b — the one change this wave must *not* make.** `app.identity_subject` is still
set with `SELECT set_config('app.identity_subject', @identity, false)` — a **bound
parameter**, never `BuildSetCommandText`'s interpolation (clause 3). An `oid` is a
directory GUID and therefore *looks* safe to interpolate; that is exactly the
reasoning that would remove the binding and leave the sink open for the next value
that is not a GUID. **The negative test with `'`, `;` and `--` stays mandatory and
stays green.**

**5c — verify, then scope, then read, is unchanged and now covers every route.**
Clause 7 applied to routes carrying a tenant id. After NW-05 a caller-supplied
tenant — in a route segment or a header — is an **authorized selector**: the token
subject's membership in it is verified **before** the scope is entered, and
failure is **404**, never 403 (ADR-022 w15 footer clause 2, ADR-025 Rule B1).
"Scope and see what comes back" turns an authorization question into an
empty-result question.

### 6. Forward, for the W16 head (recorded so W16 does not re-audit)

- **NW-08 — `GET /api/audit` after NW-05 fails closed, and that is correct.**
  `WorkspacePrincipalAuthorization.TryAuthorize` demands an authenticated
  principal (`:56-60`), a **`tenant_id`** claim (`:35`, `:62-67`) and a
  `ClaimTypes.Role` claim (`:69-74`). A real Entra token supplies neither claim,
  so the endpoint stays 401/403. **The danger is the repair**: mapping `tid` →
  `tenant_id` or minting a role claim would ship the stale-authorization window
  ADR-025 §I names by id. NW-08's fix is the **§I seam swap** at
  `WorkspacePrincipalAuthorization.cs:32-33` — tenant and role resolution move from
  claim-reading to membership-reading, and `TenantIdClaimType` is deleted with it.
  Who may read a tenant's audit trail: **a live `Admin` membership in that tenant,
  and nobody else.** The route gains no `?tenantId=`.
- **NW-07 — the conversation key becomes the token `oid`**, which is
  case-invariant by construction and dissolves the live normalization bug
  (`CallerIdentity.cs:71` lower-cases; `ConversationsEndpointExtensions.TryResolveUserId`
  does not, so one person splits across two keys). Rows written under the old
  header-derived key are **not silently re-keyed**: a migration that re-points
  conversation ownership by string matching is a cross-user data move. Either
  migrate on an explicit `(old key, oid)` pair supplied at HITL (the Rule D.2e
  backfill pattern) or leave them orphaned. **Orphaned pilot history is a smaller
  problem than mis-attributed history.**
- **NW-32 — the `"unattributed"` actor becomes a deliberate deletion.** After
  NW-05 an absent identity is 401 everywhere, so the three divergent behaviours
  collapse; but the nine service-layer defaults would survive as the value written
  to `CreatedBy` / `CorrectedBy` and to audit rows. The constant goes, the resolved
  identity is threaded in, and **an audit row that cannot name its actor becomes
  unwritable rather than unattributed**. ADR-011's audit posture requires it.

## Amendment (2026-09-14, wave w16 — the three forward bullets are discharged, one of them is corrected, and the wave's one new tenant table gets the guard that actually fires)

Seat: security-architect (owner). Serves **NW-07, NW-08, NW-32**; binds **NW-13**'s
new table. The Decision outcome above is unchanged — RLS on every tenant table,
application scoping primary, RLS the **non-bypassable backstop**, no `BYPASSRLS`
in the application path. The w14 footer's eight clauses and the w15 footer's
clauses 1–5 are unchanged and in force. **No policy text is rewritten by this
wave.** Rule ids `S16-n` are this seat's w16 lane
(`reports/architecture/draft/next/security-architect/w16.md`).

### 1. The w15 §6 forward bullets are discharged — and one of them was wrong

§6 was written so W16 would not re-audit. It is now spent, and honesty about it
is worth more than the convenience of leaving it standing:

- **NW-08 — upheld in full.** The seam swap is ADR-025 §I's, `TenantIdClaimType`
  dies, and *"a live `Admin` membership in that tenant, and nobody else"* is the
  ruling. The ladder is ADR-011's w16 footer clause 14 (S16-4); the type is
  **deleted whole**, not stripped (S16-5, ADR-025 w16 footer).
- **NW-32 — upheld in full**, with the security framing corrected by the intake
  and re-verified by this seat: since PR #117 every endpoint reaching the nine
  services 401s first, so `"unattributed"` is **not** an identity-absent branch —
  it is an unconditional hardcode that falsifies the trail of an *authenticated*
  caller. A falsified audit trail, not an authentication bypass. ADR-011 w16
  clauses 15–18.
- **NW-07 — the bullet's parenthetical is false on this tree, and this seat wrote
  it.** §6 states *"`CallerIdentity.cs:71` lower-cases; `ConversationsEndpointExtensions.TryResolveUserId`
  does not, so one person splits across two keys."* **`TryResolveUserId` does not
  exist anywhere under `backend/src`** — it was deleted in w15; only a dangling
  `<see cref="TryResolveUserId"/>` survives at
  `ConversationsEndpointExtensions.cs:24`. The **conclusion** stands unchanged and
  is re-derived in ADR-010's w16 footer from the sites that *do* exist
  (`CallerContext.cs:143`, `WorkspaceRoleResolver.cs:80`,
  `WorkspaceDirectoryService.cs:100,104,144`, `identity-workspace.sql:203-204`);
  only its evidence line was stale. Recorded rather than quietly re-worded,
  because a W16 task sent to `TryResolveUserId` finds nothing, concludes the
  defect is imaginary, and closes the item — which is the same failure mode as the
  stale doc comments NW-31 deletes, one level up in our own records.

### 2. NW-13 adds one new tenant table, and clause 4c's free branch is available — take it deliberately

`contract_negotiation_step` (ADR-028 §D3, owned by `Raffa.Documents.Contracts`)
is an **ordinary tenant table** under w14 clause 8 and w15 clause 4a: `tenant_id`
not null and indexed, `ENABLE` + `FORCE ROW LEVEL SECURITY`, a `tenant_isolation`
policy with **both** `USING` and `WITH CHECK`, shipped **in the same migration as
the table**, and **no identity-keyed policy** (the §F.1 widening stays confined to
`workspace_user`). The template is three lines away in the file the migration
regenerates — `documents-contracts.sql:421-423` (`contract`) — and every one of
that script's eleven tenant tables carries all three statements. A table that
ships with `ENABLE` but no `FORCE` would be the **only** one, and `FORCE` is the
clause that binds the table owner.

**The part that is not automatic, and the reason this clause exists.** Clause 4c
makes the CI guard conditional: `TenantRlsMigrationCheckTests` discovers its table
list **dynamically from every `TenantScopedEntity` subclass of
`DocumentsContractsDbContext`**. NW-13's owning module is
`Raffa.Documents.Contracts` — **so the free branch is available here, and w14's
`workspace_invitation` escape hatch is not needed.** It is available **only if the
entity actually subclasses `TenantScopedEntity` in that context**. Therefore:

- **2a.** `ContractNegotiationStep` subclasses `TenantScopedEntity` and is mapped
  in `DocumentsContractsDbContext`. The existing check then covers it with no new
  test, and **the proof that it is covered is that the check's table count goes
  up** — a task claiming coverage without that is claiming it for free.
- **2b.** If any task instead places it outside that context, clause 4c's second
  branch fires and it **owes a hand-written per-table RLS test** on the
  `WorkspaceInvitationRlsTests` pattern — including clause 4d's trap: the test
  creates its **own unprivileged Postgres role**, because Testcontainers hands you
  a superuser and RLS constrains neither a superuser nor a table owner. A
  hand-written RLS test written the obvious way is green and worthless.
- **2c.** Either way the task carries the negative: **a second tenant's ticks
  never appear** — same contract id, other tenant, zero rows and no 500.

### 3. Nothing else in the wave touches a policy

NW-07 changes a comparison rule and NW-08 changes where a tenant id comes from;
**neither edits policy text** (w15 clause 5a is unchanged and still governs).
NW-11, NW-12 and NW-21 read and write tables that are already
`ENABLE`+`FORCE`+`tenant_isolation` (`renewals.sql:54`, `quotes.sql:120,336`,
`savings.sql:61,123`) and NW-21 adds **no column and no migration** (ADR-028 §D5),
so this wave's entire RLS delta is clause 2's single table.

**3a — the audit read runs inside the verified scope, and this is what the swap
must preserve.** `audit_event` is already `ENABLE`+`FORCE`+`tenant_isolation`
(`Raffa.Audit/Migrations/Scripts/audit.sql:54-56`). Today `/api/audit` hands
`IAuditQueryService` a **claim-derived** tenant; after NW-08 it hands it a
**membership-verified** one. The scope is entered **after** the membership check
and the read happens inside it — w15 clause 5c's *verify, then scope, then read*,
unchanged. RLS is the backstop; membership is the gate; neither substitutes for
the other.

**3b — NW-21's resolver is a tenant-scoped read and must stay one.** ADR-028 §D5
clause 2 resolves an outcome to an opportunity through the `(tenant_id,
normalized_name)` unique index. That index is tenant-keyed by construction and the
lookup runs inside the request's own scope, so it is an ordinary read, **not** a
cross-tenant aggregate (w14 clause 8's last sentence). Recorded because "resolve
the supplier" is the shape of question that invites a global lookup: **there is no
cross-tenant read in this product, and a resolver that found an opportunity in
another tenant would be the defect, not the feature.**
