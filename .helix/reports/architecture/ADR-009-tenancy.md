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

## Amendment (2026-09-15, wave w17 — the first tenant read with no HTTP caller, and the trap that makes it fail silently green)

Seat: security-architect (owner). Serves **NW-73** (the bulk reprocess console)
and **NW-71** (the per-field decision state); ratifies one clause of **ADR-029**
(NW-26's per-page preview objects). The Decision outcome above is unchanged — RLS
on every tenant table, application scoping primary, RLS the **non-bypassable
backstop**, no `BYPASSRLS` in the application path. The w14, w15 and w16 footers
are unchanged and in force. **This wave creates no new tenant table and rewrites
no policy text.** Rule ids `S17-n` are this seat's w17 lane
(`reports/architecture/draft/next/security-architect/w17.md`).

### 1. S17-3 — a host with no HTTP caller must bind the tenant explicitly, and the product's own API makes the wrong way the easy way

NW-73's console is the **first product code path that enumerates a tenant with no
HTTP request**. Half of it is already safe by construction:
`DocumentReprocessService.ReprocessAsync` opens its own tenant scope (`:67`).
**The worklist query is not** — it lives in the console, outside any scope the
product opens, and nothing in the existing code covers it.

**The trap is in the signature, not in the implementer.**
`DocumentsContractsDbContextOptions.Configure(builder, connectionString,
ITenantContext? tenantContext = null)` wires `TenantRlsConnectionInterceptor`
**only when the third argument is supplied** (`:50-66`). The two-argument form is
what migrations and tests legitimately use, and is exactly what a reader copies
from `TenantRlsMigrationCheckTests.cs:32`.

Take the two-argument form and `app.tenant_id` is never set, so the policy's
`nullif(current_setting('app.tenant_id', true), '')::uuid` is NULL, every
comparison against it is NULL, and **every query returns zero rows**. It fails
**closed** — this ADR working exactly as designed. But on this path the failure is
**indistinguishable from an empty worklist**: the console reports "nothing to
reprocess" and **exits green having done nothing**. That is the same class of
honest-looking lie ADR-016 §30 names for psql-inserted jobs, and
`verify-tenant-corpus.yml` would not catch it, because a correct binding and a
missing one differ only in row count — and row count is data.

**Five rules bind any host that reads a tenant table without an HTTP caller:**

1. the DbContext is built with the **three-argument** `Configure`, and the
   worklist runs inside `tenantContext.BeginScope(tenantId)`;
2. **never** a raw `NpgsqlConnection`, a hand-written `SET app.tenant_id`, or
   `psql` — the GUC is the interceptor's to write
   (`TenantRlsConnectionInterceptor.cs:40`). A second writer of that setting is a
   second tenancy implementation;
3. **one tenant per run**, from an explicit `--tenant <guid>` parsed as a GUID.
   **No all-tenants mode, no wildcard, no "every tenant in the table" loop** — the
   process never holds more than one tenant's scope in a run, so a scoping bug
   cannot become a cross-tenant one;
4. **zero rows under a syntactically valid tenant is an error, not a success**:
   exit **non-zero** with "no documents visible for this tenant; check the tenant
   id and the RLS scope". This is the clause that converts a silent misbinding
   into a failed run, and it is the one a task is most likely to drop as
   over-strict;
5. the credential is the **same `postgres-connection` secret the application uses**
   (`verify-tenant-corpus.yml:112`) and nothing more privileged. `FORCE ROW LEVEL
   SECURITY` binds the table owner but **not** a `BYPASSRLS` role — the only
   remaining way to escape this ADR. **No superuser and no `BYPASSRLS` in any
   environment**, which is the body's rule restated for a host the body did not
   contemplate.

**1a — the test that pins it.** A test that builds the DbContext **the way the
console builds it** and asserts `app.tenant_id` **is set** on the open connection.
Asserting "the console returned rows" does not pin it, for the reason above.

**1b — the standing generalisation.** Every future non-HTTP host — a second
console, a Container Apps Job, a scheduled task — inherits clauses 1–5. The
product now has a named answer to "how does a process without a request bind a
tenant", so the question is not re-derived per tool.

### 2. S17-6 — NW-71's per-field decision adds **zero** new isolation surface, and the alternative shape is where the silent escape lives

ADR-003's w17 clause 1 lands `decision` + `decided_at` as two nullable columns on
the **existing** `extraction_evidence`. That table is already `ENABLE` + `FORCE
ROW LEVEL SECURITY` + `tenant_isolation` (`documents-contracts.sql:798-800`) and
already keyed per field (`ix_extraction_evidence_contract_id_field_name`, `:770`).
**A nullable column on an already-protected table needs no new policy, no new
guard, no new test and no new migration ordering.** Three seats reached this table
independently — this one from isolation, software-architect from the wire,
client-architect from the read-back — and that convergence is the reason it is
recorded rather than assumed.

**2a — the branch that did not fire, recorded as a standing rule because it is
one assertion away from being invisible.** Had a **new table** been chosen, w16
clause 2's requirements bind in the same migration — `tenant_id`,
`TenantScopedEntity`, `ENABLE`+`FORCE`+policy with both `USING` and `WITH CHECK`.
W17 adds the sharper half of clause 2a: `TenantRlsMigrationCheckTests.cs:37-44`
discovers its table list **from the EF model, by `TenantScopedEntity` subclass**,
and the "guards the guard" assert at `:46-49` only catches an **empty** list — not
a **missing member**. So an entity that does not derive from the base type is
**never checked and the suite stays green**. w16 clause 2a stated the positive
proof (the table count goes up); this clause states the failure mode it defends
against, which is the one a reviewer would otherwise have to infer.

### 3. NW-26's per-page objects stay inside the tenant prefix, and the page number is caller input

ADR-029 gives `DocumentStoragePath` a `BuildPreviewPage(tenantId, documentId,
page)` under the **same tenant prefix** and the **same `EnsureWithinTenant` guard**
(`:28-38`). **Ratified** — a per-page object is an ordinary tenant-scoped blob and
needs no new rule. One consequence is this seat's and is added rather than assumed:

- the page number reaches that path from `?page=n` on the URL, so it is
  **caller-influenced input entering a storage path**. It is parsed as a
  **positive integer** and bounded by the persisted `document.page_count`
  **before** any path is built; it is **never string-concatenated** into a blob
  path. ADR-029 already rules out-of-range **404 rather than a silent page 1**;
  this clause is why that ruling is an isolation rule and not only a correctness
  one.

### 4. The wave's RLS delta, stated so it can be checked

**Zero new tenant tables, zero policy edits, zero new guard tests, no `BYPASSRLS`
anywhere.** Every table NW-71, NW-72, NW-20, NW-22, NW-62 and NW-63 read or write
is already `ENABLE`+`FORCE`+`tenant_isolation`. The wave's entire isolation delta
is clause 1's binding rule for a new kind of host and clause 3's bounded path
input — neither of which changes a policy, and both of which change how a process
**enters** a scope. That is the part of this ADR the body always delegated to the
application, and w17 is the wave that writes it down for a host with no request.

### 5. Round 2 — clause 1's trap is not one context's, and this wave opens a second one on the item this seat itself permitted

Clause 1 named the optional-third-argument trap on
`DocumentsContractsDbContextOptions.Configure` and bound it to NW-73's console.
**Verified this round: the identical signature exists on the audit store.**
`AuditDbContextOptions.Configure(builder, connectionString, ITenantContext?
tenantContext = null)` (`:24-27`) wires `TenantRlsConnectionInterceptor` **only
when the third argument is supplied** (`:35-38`), and its own docstring offers the
two-argument form to "design-time tooling (migrations) and tests that do not
exercise tenancy" (`:18-19`) — the same legitimate escape, in a second module.

This matters now because **this seat permitted NW-20's `activity` projection
(ADR-011 clause 22) after writing clause 1, and did not notice the permitted
projection lands on the second context.** Software-architect then ruled the
projection is filled by **host composition in `Raffa.Api`** — a *new* composition,
which is precisely where the trap bites. The confirming fact arrived from another
seat: cloud-architect verified `ConnectionStrings__Audit` is **already bound** on
the API (`containerapps/main.tf:90`), so the API can already open that store with
no new key, and nothing external gates the wrong wiring.

**The audit store is genuinely protected** — `audit_event` carries `tenant_id uuid
NOT NULL` (`audit.sql:20`), `ENABLE` + `FORCE ROW LEVEL SECURITY` (`:54-55`) and
`tenant_isolation` with the same
`nullif(current_setting('app.tenant_id', true), '')::uuid` shape (`:56-58`). So it
fails **closed**, exactly as clause 1 describes: a mis-composed read returns
**zero rows**.

**5a — and on this surface the fail-closed result is the one three seats already
refused by name.** A mis-bound projection renders an **empty activity tab**, which
is indistinguishable from "nothing has happened to this contract" — the
unconditional `[]` that product-owner (clause 3), software-architect and this seat
each refused independently, because an empty array is a claim that nothing
happened. **The RLS misbinding and the forbidden `[]` are the same defect wearing
two names**, and that is what makes this checkable: the projection must be able to
tell "no whitelisted events" from "not wired", and only the first may render.

**5b — the binding rule, which resolves to an existing safe path rather than a new
one.** The activity read is a **method on `AuditQueryService`**, never a
host-composed query against `AuditDbContext`. Three reasons, all on the code:

1. `AuditQueryService` opens its **own** scope per call —
   `using var _ = tenantContext.BeginScope(tenantId)` (`:75`) — so it is safe by
   construction rather than by the caller's discipline;
2. the ordering it documents is subtle and a host composition would have to
   rediscover it: the interceptor reads `ITenantContext.Current` **only when the
   connection opens**, which EF Core does **lazily on first use**, so the scope
   must be open **before** the query is awaited (`:71-74`). A host that opens a
   scope after materialising a context has a scope that never applied;
3. the type already carries a bound — `MaxResults = 200` (`:66`). A hand-written
   host query inherits no bound at all, and an unbounded read behind a 360 tab is
   a second defect riding in on the first.

**This refines software-architect's composition ruling; it does not contradict
it.** The host still composes — it calls the audit module's service and merges the
result into the 360 payload, which is what `DependencyDirectionTests.cs:63`
requires, since `Raffa.Documents.Contracts` may not reference Audit. What the host
must not do is **author the query**. Clause 22's conditions 1 and 2
(contract-scoped, closed allow-list) are query predicates: they belong where they
can be tested, next to the scope that makes them safe.

### 6. Round 2 — a correction against this seat's own clause 3: the bound belongs in the builder, because the console has no endpoint

Clause 3 rules that `page` is "parsed as a positive integer and bounded by the
persisted `document.page_count` **before any path is built**", and cites ADR-029's
404. **Both of those are endpoint rules, and this wave ships a caller with no
endpoint.** NW-73's console re-runs the pipeline directly; after NW-26 the
pipeline rasterises; so `BuildPreviewPage` is reached on a path where no route,
no model binder and no 404 exist. An endpoint-side bound is not a bound on that
path — it is a bound on one of two callers.

**Rule**: `BuildPreviewPage` **validates `page >= 1` itself and throws**, exactly
as `DocumentStoragePath.Build` already does for `versionNumber`
(`ArgumentOutOfRangeException`, `:42-46`). The precedent is four lines away in the
same file and costs nothing; clause 3's endpoint bound stays, as the first of two
checks rather than the only one.

**6a — why the type of `page` is an isolation question and not a style one.**
`EnsureWithinTenant` is a **prefix test with no canonicalisation**:
`storagePath.StartsWith(TenantPrefix(tenantId), StringComparison.Ordinal)`
(`:32`). The `../` case in `DocumentStorageContractTests.cs:40` throws only
because `"../" + ownPath` fails that prefix test — a path shaped
`<tenantA>/../<tenantB>/…` **starts with** tenant A's prefix and would pass. On
Azure Blob that is harmless today because blob keys are opaque strings and `..` is
a literal segment, not a traversal. **The guard's safety therefore rests on no
path component ever carrying a separator** — and `Sanitize` (`:59-75`) is applied
to `fileName` in `Build` and to **nothing in the preview path**, because
`BuildPreview` has no caller-controlled component at all (`page-1.png` is a
literal, `:21`). `BuildPreviewPage` is the **first** caller-influenced component
in a preview path, so: `page` is an **`int`**, never a string, and never
interpolated from raw request text. Recorded because a later "just take the page
label as a string" change would be invisible to every existing test.

### 7. Round 2 — the deterministic-overwrite rule already has a home in this ADR's path contract

Corroborating cloud-architect's ADR-005 w17 §23 from the seat that owns the path,
and **without editing ADR-005 or ADR-029**. That seat's finding is that the
overwrite rule lives in the *cost* ADR while the render loop is written by someone
reading the *rendering* ADR, which accepts "storage grows per page per document"
with no overwrite rule attached.

**There is a third, closer home, and it is already written.** `BuildPreview`'s own
docstring states the property as the preview path's defining characteristic: a
preview is "a derived rendering, **replaced in place** whenever the document is
reprocessed, and must never be served as if it were the document"
(`DocumentStoragePath.cs:16-18`). The engineer writing `BuildPreviewPage` opens
that file — the new method goes *in* it — and the rule is four lines above the
method being copied.

**So the rule binds by construction if the key is deterministic**:
`BuildPreviewPage(tenantId, documentId, page)` derives its key from three
server-held values with no timestamp, no run id and no suffix, exactly as
`BuildPreview` does, and a reprocess therefore **overwrites**. This is an
isolation clause as well as a capacity one: a suffixed key would leave a
**previous tenant-scoped rendering of a document that has since been corrected**
readable at a path nothing reaps — `infra/modules/storage` has no lifecycle rule
(cloud-architect, verified) — which is stale tenant content surviving its own
correction. Same rule, two reasons, and now reachable from the file that builds
the path.

### 8. Round 3 — the reap software-architect adopted needs a boundary this ADR's own guard cannot give it

ADR-029's round-3 clause 2 closes the gap clause 7 left open: determinism
overwrites pages 1…N and **reaps nothing beyond N**, so a re-uploaded document
with fewer pages leaves surplus pages at exactly the keys the viewer serves. That
ruling — the render stage deletes `n > pageCount` in the same stage that writes
1…N — is **adopted whole, and ADR-029 is not edited**; it is software-architect's.
What this seat owes it is the **boundary**, because the guard an engineer would
reach for does not supply one.

**`EnsureWithinTenant` cannot bound this delete.** Its own docstring offers itself
as the "fail-closed guard for the read/**delete** side of `IDocumentStorage`"
(`DocumentStoragePath.cs:24-26`) — so the engineer writing a reap will read it as
*the* check. It is a **tenant** prefix test and nothing more (`:32`,
`StartsWith(TenantPrefix(tenantId))`): every key under `{tenant}/documents/**`
passes it. A reap that used the wrong document id, or enumerated the **tenant**
prefix instead of the **document's**, is therefore **legal under ADR-009's own
guard** — same tenant, wrong document, guard silent. This is **not** a cross-tenant
defect and must not be written up as one; it is the class of defect that guard is
routinely assumed to cover and does not.

**Rule — the reap is bounded by two things, and the guard is only the outer one:**

1. **prefix** — keys under `{TenantPrefix}documents/{documentId}/preview/` only,
   derived from `BuildPreviewPage`'s own inputs; never from a listing of a wider
   prefix, and never from a key echoed back by a caller;
2. **page number** — delete only `n > pageCount`, where `pageCount` is a
   **confirmed positive render result of this run**. A null, zero, defaulted or
   exception-path `pageCount` deletes **nothing**. Fail-closed, because the
   arithmetic that reaps "everything above 0" is the arithmetic that reaps the
   whole document.

**The precedent software-architect cites already obeys this**, which makes the
rule a ratification rather than an imposition: the chunk delete it points at is
`RemoveChunksAsync(tenantId, DocumentSourceType, documentId)`
(`DocumentReprocessService.cs:99-101`; signature
`EmbeddingRetrievalService.cs:163-167`) — **three** arguments, tenant *and* source
type *and* document. The blob reap carries the same scoping or it is a weaker copy
of its own precedent.

⚠ **Why this is an ADR clause and not a task note**: NW-73 multiplies it. A
single-document reprocess with a bad bound damages the one document an Admin is
looking at; the same code under a **whole-tenant loop** reaches every document in
the tenant from one operator dispatch — and clause 1's RLS scope, which is this
ADR's primary control, **does not see blob keys at all**.
