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
