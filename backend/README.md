# Raffa backend

.NET 10 modular monolith + background worker (ADR-002). One class-library
project per bounded context, a shared kernel, and three composition roots
(`Raffa.Api`, `Raffa.Worker`, `Raffa.Tools`). Domain modules never reference
a provider SDK or another domain's internals — `Raffa.ArchitectureTests`
fails the build if a project reference points the wrong way.

Honours ADR-003 (Postgres + pgvector, EF Core), ADR-009 (RLS as the
non-bypassable backstop), and ADR-005 (API + worker as Container Apps).

## Solution

```
backend/
  Raffa.slnx
  Directory.Build.props          # net10.0, nullable, TreatWarningsAsErrors
  src/
    Raffa.Api/                 # thin HTTP composition root (port 8080 in containers)
    Raffa.Worker/              # thin worker composition root
    Raffa.Tools/               # operator console (wave w17, NW-73, task E20/F02/US02/T01): third composition root, no table, no endpoint, no business rule; `dotnet run` on the GitHub runner via reprocess-tenant-documents.yml — see "Bulk whole-tenant reprocess" below
    Raffa.SharedKernel/        # TenantId, EntityId, Result<T>, IClock, IAuditWriter, IDocumentStorage
    Raffa.Identity.Workspace/  # workspace, membership, roles (live)
    Raffa.Documents.Contracts/ # upload + admission gate (task E13/F04/US01/T01), metadata, hybrid OCR pre-pass, staged extraction, portfolio list, Contract 360, contract correction + history (live)
    Raffa.Audit/               # append-only audit events (live)
    Raffa.AiGateway/           # IAiGateway (classify/extract/embed/answer/ocr): FixtureAiGateway + live FoundryAiGateway (Azure OpenAI-compatible + Document Intelligence, task E13/F01/US01/T02), always behind the LoggingAiGateway decorator
    Raffa.Benchmark/           # IBenchmarkService.GetBenchmarkAsync + normalized Contracts DTOs (E04/F01/US01/T01); BenchmarkAdapterRegistry + AddBenchmarkModule (E04/F01/US01/T02); FixtureBenchmarkAdapter registered as the default IBenchmarkProviderAdapter, incl. statistical weak-comparable abstain (E04/F01/US02/T01+T02) — no host calls AddBenchmarkModule yet (R3)
    Raffa.Suppliers.Products/  # Supplier entity, SupplierNameNormalizer, ISupplierResolver/ISupplierNameLookup impls, SuppliersDbContext + RLS (task E13/F03/US01/T01, ADR-024; live) - see "Supplier identity" below
    Raffa.Market/              # R-MKT-01/02/03/04 mock feed + benchmark projection + in-memory notes retrieval (E13/F02/US01/T01); market_record/market_embedding pgvector index + ingestion job + DB-backed retrieval/benchmark + GET /api/market/records/{id} (E13/F02/US01/T02, mapped by E13/F06/US01/T01) - see "Market Intelligence" below
    Raffa.Insights/            # criticality score, priced-line negotiation, strategy pack builder (E13/F07/US01/T01, ADR-024) + grounded NegotiationPointRanker (E31/F02/US01/T01, NW-96) - pure calculators fed by DTOs; InsightsEndpointExtensions mapped by task E13/F06/US01/T01 (ask-engine) - see "Insights" below
    Raffa.Renewals/            # renewal engine + opportunity + explainable priority score + threshold scheduler + dashboard pipeline + action (R2; live) — see "Renewal Intelligence" below
    Raffa.Savings/             # price normalization + percentile/target/savings-range calculator (R3; task E04/F02/US01/T01) + persisted, trackable SavingsOpportunity + GET/PATCH /api/savings (task E04/F02/US02/T01) — see "Savings Intelligence" below
    Raffa.Quotes/              # quote upload + hybrid-OCR-reused, schema-constrained line-item extraction (evidence + confidence; deterministic pricing) + POST /api/quotes (R4; task E05/F01/US01/T01) + SKU/edition normalization against a per-tenant canonical mapping, unmatched-SKU flagging (task E05/F01/US02/T01) + benchmark matching/above-in-line-below market assessment + GET /api/quotes/{id}/assessment, AddBenchmarkModule now wired (task E05/F02/US01/T01) + deterministic recommended target range/potential saving on that same endpoint (task E05/F02/US01/T02) + deterministic negotiation strategy (opening target/acceptable range/walk-away threshold + seven canonical levers with rationale, NegotiationStrategyService, no HTTP endpoint yet) (task E05/F03/US01/T01) + NegotiationOutcome capture (original/target/final/deterministic saving+discount/duration/levers used) + POST /api/negotiations/outcomes, append-only/audit-tracked (task E05/F03/US02/T01) + read-back: `QuoteQueryService` (stored fields only, computes nothing) backing GET /api/quotes (tenant list) and GET /api/quotes/{id} (the quote with its recorded negotiation outcomes embedded, newest first) — task E19/F02/US01/T01, quote-read-api, wave w16 NW-12, ADR-028 §D2 — see "Quote Check" / "Market Assessment" / "Negotiation Strategy" / "Negotiation Outcome" below
    Raffa.Chat/                # Ask Raffa structured-vs-semantic query router (R1, task E02/F04/US01/T01) + deterministic dates/spend query handlers (task E02/F04/US01/T02) + RagAnswerService (task E02/F04/US02/T01) + AbstainGuard no-fabrication guard (task E02/F04/US02/T02); AddChatModule wired into Raffa.Api by this last task; own ChatDbContext + Conversation/ConversationMessage under RLS + ConversationService (create/list/get/append) (task E13/F05/US01/T01) — see "Ask Raffa — conversations store" below
  tests/                         # per-module + architecture + R0-R4 integration
```

Hosts are composition roots only: they register modules via `AddXxxModule`
and map HTTP / hosted services. Business logic lives in the libraries.

**V2 scaffold (task E13/F01/US01/T01, ADR-024).** `Raffa.Market` and
`Raffa.Insights` entered the solution as scaffolds — a class library, an
`AddMarketModule()` / `AddInsightsModule()` stub that registered nothing, and
a matching test project with one placeholder test — so the epic-13 tasks that
fill them in would not also have to touch `Raffa.slnx` or the architecture
allow-list. Both are now real: `Raffa.Market` by tasks E13/F02/US01/T01+T02
(see "Market Intelligence" below), `Raffa.Insights` by task E13/F07/US01/T01
(see "Insights"). `Raffa.Suppliers.Products.Tests` was that scaffold task's
third new test project and got real coverage from task E13/F03/US01/T01 (see
"Supplier identity"). `Raffa.AiEval` (references `Raffa.Chat`,
`Raffa.AiGateway`, `Raffa.SharedKernel` — the golden-set eval harness of
story us-01-v2-foundation) is the one still-placeholder project, pending task
E13/F06/US01/T02. `Raffa.ArchitectureTests.DependencyDirectionTests`
allow-lists `Raffa.Market` → `[SharedKernel, AiGateway, Benchmark]` and
`Raffa.Insights` → `[SharedKernel, Benchmark]`, and covers both in its
domain-module direction / provider-SDK theories.

*(This section previously carried three interleaved copies of the project tree
and of this paragraph — one truncated mid-sentence — merged in by the epic-13
wave's phase barriers. Reconciled by task E13/F04/US01/T01 against the actual
`Raffa.slnx` and `DependencyDirectionTests` allow-list.)*

## Commands

Requires the .NET 10 SDK. Integration tests pull a `pgvector/pgvector:pg16`
Testcontainer (Docker must be running).

```bash
cd backend
dotnet restore Raffa.slnx
dotnet build Raffa.slnx --configuration Release
dotnet test Raffa.slnx --configuration Release
```

Local API (https://localhost:7109, http://localhost:5029 — matches
`web/public/config.json`):

```bash
dotnet run --project src/Raffa.Api/Raffa.Api.csproj --launch-profile https
```

`appsettings.Development.json` points at `localhost:5432` database
`raffa_dev` (user/password `raffa`) and Azurite
(`UseDevelopmentStorage=true`). There is no docker-compose in this repo
yet — bring your own Postgres (with `VECTOR` enabled) and Azurite, or rely
on Testcontainers inside `dotnet test`.

EF migrations live in each module that owns a DbContext
(`Raffa.Identity.Workspace`, `Raffa.Documents.Contracts`,
`Raffa.Audit`, `Raffa.Renewals`, `Raffa.Savings`, `Raffa.Quotes`,
`Raffa.Chat`, `Raffa.Suppliers.Products`). Apply them against
the same database the hosts use; RLS policies are added in those
migrations, not in Terraform.

**Deployable schema artifact (ADR-021):** every module above also checks in
`Migrations/Scripts/<module>.sql` — `identity-workspace.sql`,
`documents-contracts.sql`, `audit.sql`, `renewals.sql`, `savings.sql`,
`quotes.sql`, `chat.sql`, `suppliers.sql`, `market.sql` — generated with
`dotnet ef migrations script --idempotent` from that module's `src/`
folder. That checked-in script, applied with `psql` (or any plain Npgsql
client), is the actual `dev`/`demo` deploy path: CI applies all nine, in
ADR-021's fixed order (`chat.sql` appended seventh — task E13/F05/US01/T01
postdates ADR-021's own fixed list; `suppliers.sql` appended eighth — task
E13/F03/US01/T01; `market.sql` appended ninth — task E13/F02/US01/T02, same
reasoning as `suppliers.sql`: neither `market_record` nor `market_embedding`
carries a FK to or from any other module's tables, so `market.sql` has no
ordering dependency on the other eight and is simply appended last), after
both `az containerapp
update` steps (task E09/F02/US01/T02) — `Raffa.Api` and `Raffa.Worker`
deliberately never call `Database.MigrateAsync()`, so a replica boot never
mutates schema. Regenerate the script after adding or changing a
migration; `<Module>MigrationScriptStaleCheckTests` (task E09/F01/US01/T01,
not yet retrofitted onto `Raffa.Chat` — same pre-existing gap
`Raffa.Documents.Contracts` also has; `Raffa.Suppliers.Products` has
its own from the start, task E13/F03/US01/T01) fails `dotnet test` if a
script is missing or no longer matches a fresh idempotent generate, and
`<Module>MigrationScriptTests`
proves the checked-in script itself — not `MigrateAsync`, no DbContext —
applies (and re-applies) cleanly to a bare `pgvector/pgvector:pg16` server.
`.github/workflows/backend.yml`'s CI apply step (`scripts/pg_connection_string_env.py`
turns the Key Vault `postgres-connection` secret into `psql`'s `PG*`
environment variables; `scripts/schema_apply_verify.py` then proves every
migration_id all nine scripts declare landed in `raffa_<env>`'s own
`__EFMigrationsHistory`, failing the job by name otherwise) needs the CI
deploy principal to hold `Key Vault Secrets User` on that environment's
vault (`modules/keyvault` `ci_secrets_user`, applied by HCP).

**Demo fixture seed (task E10/F01/US01/T01, ADR-001, ADR-022):** ADR-021's
schema apply above creates empty tables — nothing populates the Day-1
Savings screen on a fresh `raffa_demo`. `backend/scripts/demo-fixture-seed.sql`
is the checked-in, idempotent fix: one demo `workspace` (tenant id
`00000000-0000-0000-0000-000000000001` — hard-code this as `X-Tenant-Id`
to exercise the seeded tenant directly), one supporting `contract` row, and
three `savings_opportunity` rows traceable to real
`Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter.Catalog` entries (AWS
EC2, Zoom, Snowflake) spanning `Raffa.Savings.Application
.SavingsProvenanceClassifier`'s own documented High/Medium/Low confidence
examples for those exact fixtures — never a fabricated number (ADR-001).
Every `INSERT` is `ON CONFLICT (id) DO NOTHING` against a fixed id, so
re-running is safe, and every RLS-guarded table is written the same way
the application itself writes one — `SET app.tenant_id = '<demo tenant
id>'` before the insert (see `Raffa.SharedKernel.Tenancy
.TenantRlsConnectionInterceptor`) — never a bypass role, never a disabled
policy (AC-4). Applied with `psql` (never `Database.MigrateAsync()`) by
`.github/workflows/seed-demo-fixture.yml` — `workflow_dispatch` for a
manual operator run, `workflow_call` for a future caller (for example a
`demo-v*` promotion runbook) — against either `dev` or `demo`
(`target_environment` input), *after* `backend.yml`'s own schema apply has
run against that environment; it reuses that same job's Key Vault fetch
(`scripts/pg_connection_string_env.py`, secret `postgres-connection`) and
the same per-env deploy service principal, so no new Azure role assignment
is needed. `Raffa.IntegrationTests.DemoFixtureSeedEndToEndTests` (via
`DemoFixtureSeedIntegrationFixture`) proves the checked-in script itself —
read from disk, applied to a real Postgres+RLS Testcontainer, never
re-typed into the test — makes `GET /api/savings` return the three seeded
opportunities for the demo tenant and nothing for any other tenant, and
that a second apply does not duplicate rows. The workflow's schema guard
also requires `workspace_membership` to exist (task E14/F05/US01/T01), so
seeding a `demo`/`dev` whose schema predates w14 fails honestly at that
guard instead of obscurely mid-script.

**Admin membership for the fixture tenant, and the backfill for everything
else (task E14/F05/US01/T01, ADR-025, ADR-026, w14):** once
`GET /api/workspaces` lists by membership (NW-01), the ADR-022 fixture
tenant above — seeded by SQL, never by `POST /api/workspaces` — can never
pick up a membership row from the ordinary create-workspace path.
`demo-fixture-seed.sql` now also inserts one Admin `workspace_user` (fixed
id `...0003`) and `workspace_membership` (`...0004`) for that tenant, and
the Admin `workspace_role` itself (`...0005`) if the tenant does not
already have one — same fixed-id / `ON CONFLICT (id) DO NOTHING`
convention as every other row in the file. The email is an optional `psql`
variable, `demo_admin_email`, falling back to the inert placeholder
`demo-admin@raffa.invalid`; set the `DEMO_ADMIN_EMAIL` GitHub Environment
variable for `dev`/`demo` before the first post-w14 seed to bind it to a
real address instead (`seed-demo-fixture.yml` passes it through only when
that variable is set — an unset variable leaves the script's own fallback
in force, never an empty psql variable).

Every **other** workspace already created on `dev` (or any future
environment) needs the same grant, and cannot get it from this file — a
real tenant has no fixed id. `.github/workflows/backfill-workspace-membership.yml`
is the operator path: `workflow_dispatch` (or `workflow_call`) with a
`target_environment` choice and a required `pairs` input, newline-separated
`<workspace id>,<admin email>`. Gated by the same GitHub Environment
approval a deploy already requires; reuses the same OIDC login and
`postgres-connection` secret (no new identity, no new Key Vault secret,
ADR-015/ADR-016 w14 footer — seeds and backfills are data-plane acts and
are never promoted, so this job is run again, unmodified, per environment,
never copied from `dev`). Per pair, one transaction scoped by
`SET app.tenant_id`, `gen_random_uuid()` for the new rows (no fixed id is
available for a real tenant), and a closing verification that fails the
job — not just prints — if any supplied pair still has no live Admin
membership afterwards. There is deliberately no "claim this workspace" API
endpoint (ADR-025 §2.3 / ADR-026): `CreateWorkspaceAsync` never recorded a
creator, so pairs come from the operator at HITL, not from a caller-trusted
request.

## HTTP surface today

| Method | Path | Notes |
|--------|------|-------|
| GET | `/health` | ASP.NET health checks |
| POST | `/api/workspaces` | Create workspace (task E14/F02/US01/T01, wave w14; ADR-025 §D.2). Requires the caller identity `Raffa.Api.Infrastructure.ICallerIdentity` resolves from `X-User-Id` — absent is **401**, not 400 (creating a tenant is no longer the anonymous pre-auth signup step it used to be: it now writes an identity-keyed grant). The creator becomes this tenant's Admin *by virtue of creating it*, so 201 carries `{ id, name, createdAt, role: "Admin" }` — a `role` field in the request body is never read, let alone honoured |
| POST | `/api/workspaces/{tenantId}/invites` | Invite; roles Admin / Procurement / Legal / Finance / ReadOnly. Guarded (task E14/F02/US01/T01, ADR-025 §D.1a — closed the wave's highest-priority security gap: this route previously had no authorization at all): caller identity required (else **401**), a live `workspace_membership` for that identity in the **route** tenant (else **404**, never 403 — a 403 on a tenant the caller does not belong to is a tenant-existence oracle), and that membership's role must be Admin (else **403**); an Admin may invite another Admin. The tenant is always the route value — `X-Tenant-Id` is not an input to this endpoint. **Response changed by task E15/F01/US01/T01 (wave w14; ADR-025 §C/§D.1, ADR-026 §D5):** this is now an offer, not a grant — the handler writes a `workspace_user` row (none existed yet) plus one `workspace_invitation` row, **never** a `workspace_membership`; the membership is written only at `POST /api/invites/accept` below. **Wave w15 (task E17/F01/US01/T01; ADR-025 §J, ADR-026 w15 footers §1–§9):** before any row is written the guest is provisioned in the company directory (`IGuestProvisioner` → `GraphGuestProvisioner`, `POST /invitations` with `sendInvitationMessage: false` as the workload identity's `User.Invite.All` application permission, gated by `Invitations__GuestProvisioning__Enabled`; the Graph object id is bound into `workspace_user.external_subject_id` so the accept matches on `oid`), then the rows, then the mail (`IInvitationMailer` → `AcsInvitationMailer`, gated by `Invitations__Mail__Enabled`, plain-text body per ADR-020 w15 §4, one recipient). `201 { id, email, role, expiresAt, acceptUrl, deliveryOutcome, mailDelivered, identityProvisioned }` — `acceptUrl` is `{Invitations__AcceptUrlBase}/invite/accept#<token>` when a base is configured (the token stays after the `#`, Rule C9) and the w14 site-relative form otherwise; `deliveryOutcome` is `sent` \| `mail_failed` \| `no_transport` and equals `mailDelivered` (`TrySendAsync`'s own bool — accepted for delivery, never a receipt) by construction; `identityProvisioned` is `true` for a created **or** an already-present guest and `false` when provisioning is not configured — never "failed". **502** `{ failureReason: consent_missing \| provisioning_failed \| directory_unavailable }` when the directory would not provision the guest: no invitation row, no token, no mail, one `workspace.guest.provisioning_failed` audit row, the slot free for a retry. A live (unaccepted, unrevoked) invitation for the same address is **replaced** — revoked and re-issued in one transaction (the link the Admin already shared stops working; two audit rows). **409** when the email already holds that role, when the tenant has reached its cap of 100 live invitations (`workspace.invitation.cap_reached` audited), or when two concurrent invites race the partial unique index on `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND revoked_at IS NULL` — translated to a clean conflict, never a 500; a removed person (see `DELETE .../members/{membershipId}` below) is not blocked by this |
| DELETE | `/api/workspaces/{tenantId}/invites/{id}` | Revoke a still-pending invitation, Admin only (task E15/F01/US01/T01, wave w14; ADR-025 §D.5, AC-11). Same identity → membership → Admin guard as the POST above (401 → 404 non-member → 403 non-Admin). Stamps `revoked_at`; the accept link stops working on the very next request — nothing caches it. **204**; idempotently **404** for an unknown id or one already accepted/revoked (no distinction disclosed either way) |
| GET | `/api/invites` | Pre-accept: read what an invitation offers, without accepting it (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.3e/C4/C5, ADR-026 §D5). **Not** parameterised on `{token}` — the token travels in the `X-Invitation-Token` header, never a path or query string (a query string lands in access logs, `Referer` headers and browser history, Rule C9). The token's tenant-id prefix is `Guid.TryParseExact`d **before** any tenant scope is entered (Rule C4); once scoped, the hash match is the first statement — nothing else is read until it succeeds, or this becomes a workspace-name oracle for any guessed tenant id. `200 { workspaceName, role, expiresAt }` and **nothing else, ever** — not the invited email (echoing it turns a leaked link into an address-discovery tool), no roster, no counts. **410** expired (safe only because reaching this branch already proves possession of the 256-bit secret); **404** unknown/revoked/accepted/malformed — one indistinguishable answer for every "not yours" case |
| POST | `/api/invites/accept` | Accept an invitation: bind the signed-in identity and grant the membership (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.3a-d, ADR-026 §D5). Same `X-Invitation-Token` header as the GET above, plus a validated bearer token (absent → **401** — there would otherwise be no subject to bind). **Wave w15 (task E17/F01/US01/T01, ADR-010 w15 §2.3, ADR-025 §J.3b)** the signed-in identity is matched in this order: the `oid` bound to the invited `workspace_user` row at invite time (the Graph guest's object id) → the token's own `email` claim against the invited address → **403** with a reason that never echoes the invited address, and no membership row written; the mangled `#EXT#` UPN is never parsed. On success, one transaction: binds the accepting identity's external subject onto the `workspace_user` row the invite already wrote (`WorkspaceSignIn.LinkSignInAsync`, unchanged), inserts the membership at the invited role, stamps `accepted_at`. `200 { workspaceId, workspaceName, role }`. A second accept by the same identity is **409** (idempotency signal); two concurrent accepts still produce exactly one membership row and one 409, never a 500 — the unique index `ix_workspace_membership_workspace_user_id_workspace_role_id` already enforces the row, this only translates the violation. **410** expired |
| GET | `/api/workspaces/{tenantId}/members` | The roster (task E14/F04/US01/T01, wave w14, story us-01-members-api; ADR-026 §D3, ADR-025 §D.4). Verify, then scope, then read (ADR-009 w14 footer clause 7): caller identity required (else **401**), a live `workspace_membership` for that identity in the **route** tenant (else **404**, never 403 — a tenant-existence oracle, never an empty 200 — also an oracle); once verified, **any** live member may read regardless of role (Read ≠ write — only an Admin may invite/revoke/remove). The tenant is always the route value; same posture as the invite route above, `X-Tenant-Id` is never read. Response `{ members: [ { id, email, name?, role, status, membershipId?, invitationId? } ] }` — `id` is the `workspace_user` id (stable across `Active`/`Invited`, never an action id); `membershipId` is set exactly when `status` is `Active` and is what `DELETE …/members/{membershipId}` below takes, `invitationId` exactly when `status` is `Invited` and is what `DELETE …/invites/{id}` takes (a person holding two memberships exposes the id of the row their displayed, highest role came from — fix `271c3ae`, wave w14); the roster is **live memberships ∪ live invitations**, never a scan of `workspace_user` (a removed member keeps that row for audit continuity and, because their `ExternalSubjectId` stays bound, would otherwise render `Active` — the defect ADR-026 §D3 exists to prevent); `status` is `Active` or `Invited`, derived, never stored — an accepted/revoked/expired invitation renders nothing; `name` maps to the stored `WorkspaceUser.DisplayName` and is `null` unless one is on file, never derived from the email; a person holding two memberships appears once, at the highest role (`WorkspaceRoleClaimResolver`'s own precedence, no second ordering); `role`/`status` are non-nullable strings, never an OpenAPI enum (role names are per-tenant rows the schema does not close over) |
| DELETE | `/api/workspaces/{tenantId}/members/{membershipId}` | Remove a member from a workspace, Admin only, with the last-Admin guard (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5a-c, AC-7/AC-9). Same identity → membership → Admin guard as the roster/invite routes above (401 → 404 non-member → 403 non-Admin); then **409** when the target membership is the tenant's sole live Admin — `WorkspaceMembershipRemoval.CanRemove`, a pure domain function provable without a database ("at least one live Admin per tenant, always"), refuses even removing yourself if you are that last Admin. Deletes the `workspace_membership` row only, never `workspace_user` (audit continuity — the FK is `ON DELETE CASCADE` from user to membership, so deleting the user would cascade), and revokes that email's live invitations in the same transaction (without that, a still-valid link would re-admit them, making AC-8's "re-adding needs a new invite" false). Immediate — nothing caches authorization, role and membership are read from the database on every request, so the removed identity's very next request in this tenant already reflects it. **204** |
| POST | `/api/documents` | multipart `file` + `X-Tenant-Id` header. **Wave w15 (task E16/F02/US03/T01, ADR-027 §D1): the request returns at the store.** Size → **413** and format by extension *and* magic bytes → **415** are still refused in-request (they need no AI); everything else is `201 { id, contractId: null, fileName, mimeType, processingStatus: "Uploaded", createdAt }` the moment the bytes are durable in blob storage and a `document` row plus a queued `extraction_job` exist — **no parse, no classification and no AI-gateway call on the request path**. The Worker (`Raffa.Worker`, ADR-027 §D2/§D3) claims the job from the Service Bus topic and runs the admission gate (parse/OCR → readable-text floor → `classify`) and then `DocumentProcessingPipeline` (staged extraction → RAG indexing), reusing the gate's own parse and classification so the `classify` role is still called once per upload. A content refusal is no longer a 422: it is a **`Rejected` row** with a `rejectionReason` code (`not_a_contract` \| `no_readable_text`) on `GET /api/documents`, its blob deleted, one `document.rejected` audit row attributed to `system:worker`. Poll `GET /api/documents/{id}` or `GET /api/documents` for the terminal status. See “Documents — admission gate” below |
| GET | `/api/documents/{id}` | metadata/status; same header; `documentType` is the widened `ContractDocumentType` (`Msa`, `OrderForm`, `Amendment`, `Sow`, `RenewalLetter`, `Quote`, `Invoice`, `PriceList`, `Nda`, `Dpa`, `Other`) — task E13/F04/US01/T01 added the last five so “the documents around a contract” keep their own kind |
| GET | `/api/documents` | Server-side Documents list (R-DOC-06/09; task E13/F04/US01/T02); `X-Tenant-Id` header; optional `status` (exact `DocumentProcessingStatus`, `Rejected` included since wave w15), `page` (default 1), `pageSize` (default 25, max 100); response `{ items, page, pageSize, totalCount, counts }`, each item `{ id, contractId, supplierName, fileName, documentType, processingStatus, stage, pageCount, createdAt, weakFactCount, rejectionReason }` — `stage` is one of R-DOC-09's six real names and is present **only** while `processingStatus` is `Processing`; `rejectionReason` is `not_a_contract` \| `no_readable_text` on a `Rejected` row and `null` otherwise; `supplierName` is resolved through `ISupplierNameLookup` when the Suppliers module is registered, `null` otherwise (never a raw id); `weakFactCount` counts this contract's distinct extracted fields whose latest evidence is missing or below 0.6. **`counts` (task E16/F02/US03/T01, ADR-027 §D7/§C5/§C9)** is tenant-wide and independent of `status`/`page`: `{ all, needsAttention, needsReview, processing, rejected }` — `all` excludes `Rejected`, `needsAttention` is *not Completed and not Rejected* (so it contains `processing`), `needsReview` is `NeedsReview` alone; five overlapping projections of one grouped query, never a partition |
| GET | `/api/documents/{id}/preview` | Page preview as `image/png` (R-DOC-08; wave w17 NW-26, task E22/F02/US01/T01, ADR-029); `X-Tenant-Id` header; optional 1-based `?page=n` (absent means page 1); 404 when the document does not exist for this tenant, has no stored preview, **or** `page` is out of the persisted `pageCount` — never a silent page 1. The client never receives a blob URL; the bytes are streamed under the caller's own tenant scope (ADR-009). See “Documents V2” below |
| POST | `/api/documents/{id}/reprocess` | **Admin only** (403 otherwise). **Wave w15 (ADR-027 §D1): a re-enqueue, `202 Accepted`** — drops the old page-aware chunks, puts the document back to `Uploaded`, queues a fresh classification `extraction_job` for the Worker and answers `{ documentId, extractionJobId, processingStatus: "Uploaded" }` with `Location: /api/documents/{id}`; the re-parse → page-aware embedding → staged extraction (R-DOC-07) happens on the Worker exactly like a first upload, and one `document.reprocessed` audit row records the queueing. Role resolution: the validated token's membership, and nothing else (task E14/F02/US02/T01, ADR-025 §E); see `Raffa.Api.Infrastructure.WorkspaceRoleResolver` and "Admin resolution" below |
| POST | `/api/documents/{id}/prioritise` | **Any live member** (not Admin-only — whoever opened the document is the one waiting for it). **Wave w15, built by hand 2026-09-14 (task E16/F03/US02/T01, ADR-027 w15 footer C12).** Stamps `extraction_job.prioritised_at` on the document's queued, unclaimed classification job(s); the next delivery any Worker replica handles in this tenant runs that job before its own — one per delivery, bounded by `ExtractionRequestedHandler.MaxPrioritisedPerDelivery`. Service Bus stays at **exactly one** subscription (OQ-w15-012 unchanged): there is no second message, the reordering happens entirely on the row. Idempotent — a job already claimed, already prioritised, or already terminal is a no-op that still answers `204`; one `document.prioritised` audit row only when something changed. `caller.Identity` is the audit actor. **204** always when the document exists; **404** when it does not exist for this tenant; **400** for a non-GUID id — never 403 (ADR-025 Rule B1). The web calls this once, blindly, every time a user opens a document that is not yet terminal (`web/README.md`'s own "Documents" section, Progress state) |
| DELETE | `/api/documents/{id}` | **Admin only** (403 otherwise): deletes every stored object (each version plus the preview), the retrieval chunks, the version and extraction-job rows and the document row, detaches the contract link and clears every `source_document_id` on the facts that survive; writes one `document.deleted` audit row; 204 (R-DOC-10). The contract and its extracted facts are deliberately kept |
| POST | `/api/documents/{id}/validate` | Review sign-off (product spec §7.1 "needs review → completed", ADR-020 screen 6 "Mark as validated"): optional body `{ acceptedFields: string[] }` + `X-Tenant-Id` header (`X-User-Id` names the actor); moves a `NeedsReview` document to `Completed` and writes one `document.validated` audit row naming the accepted fields (`Raffa.Documents.Contracts.Application.DocumentValidationService`); never rewrites the extraction evidence. Not Admin-only — reviewing is the Procurement role's own job, same posture as `PATCH /api/contracts/{id}`. 404 unknown/cross-tenant document; **409** with a named reason for a document still `Uploaded`/`Processing` or `Failed`; idempotent — an already `Completed` document answers 200 with `alreadyValidated: true`. Response `{ documentId, contractId, processingStatus, validatedAt, acceptedFields, alreadyValidated }` |
| PATCH | `/api/contracts/{id}` | `{ corrections: { <field>: <string\|null> }, reason? }` + `X-Tenant-Id` header; versioned correction (ADR-003 `ContractVersion`/`CorrectionHistory`, ADR-009 RLS) — see `Raffa.Documents.Contracts.Application.ContractCorrectionService.CorrectableFieldNames` for the accepted field list; also writes one `IAuditWriter` entry (`contract.corrected`) |
| GET | `/api/contracts/{id}/evidence` | `X-Tenant-Id` header; the latest `ExtractionEvidence` row per field for one contract (`Raffa.Documents.Contracts.Application.ContractEvidenceQueryService`), alphabetical by `fieldName`: `{ fieldName, value, confidence, decision, sourcePage, sourceSpan, box, sourceDocumentId, sourceFileName, passage, highlightStart, highlightLength, modelId, extractedAt }` plus once-per-response `autoAcceptThreshold` (0.90). **Wave w17 (NW-71):** `decision` is the server's persisted state (`auto_accepted` / `human_accepted` / `review_required`); the web renders it and never recomputes it. **Wave w18 (epic-23 feature-02, NW-63r):** `box` is `{ x, y, width, height }` — the phrase's pixel-space bounding rectangle on the rendered page image `sourcePage` names — or `null` for text-level highlight only. **Every row is `null` on every contract today, and stays that way until a later wave**: epic-23 feature-04 (`E23/F04/US01/T01`) shipped only the *viewer's* renderer for this field (`web/src/routes/documents/viewer/BoxOverlay.tsx`, degrading to the existing `SourceSpan` text highlight when `box` is `null`, epic-23 AC-4) — no task in wave w18 writes the `words`/`polygon` geometry feature-01 widened onto the OCR wire (`Raffa.AiGateway.Contracts.AiOcrPage`) into the `BoxX`/`BoxY`/`BoxWidth`/`BoxHeight` columns this feature-02 migration added; `Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService` has no reference anywhere to the gateway's geometry types (confirmed by grep). The columns, the migration and the read path are real and tested; only the write that would ever populate them from a real OCR call does not exist yet. A client-supplied decision on validate/PATCH is 400 and never persisted. 404 when the contract does not exist for the tenant, `{ autoAcceptThreshold, fields: [] }` when it exists but has no evidence |
| PATCH | `/api/contracts/{id}/evidence/{fieldName}` | Phrase-edit write (epic-23 feature-03, task E23/F03/US01/T01, closes half of NW-63r; ADR-029 footer clause 1); `{ overrideValue: string }` + `X-Tenant-Id` header. Writes the reviewer's corrected phrase onto the same `ExtractionEvidence` row's `OverrideValue` column **beside** `Value` (the model's original proposal, never mutated) — `Raffa.Documents.Contracts.Application.ContractPhraseEditService.EditAsync`, the same guard-clause ladder as `PATCH /api/contracts/{id}` (401 → 400 blank override → 404 unknown contract/field → 200). `GET /api/contracts/{id}/evidence` above reads `overrideValue` back from that same row regardless of which extraction row is newest for the field, so a later reprocess's fresh proposal can never silently drop a human's already-recorded override (ADR-027). Writes one `IAuditWriter` entry (`contract.phrase_edited`). **No web affordance calls this route yet**: `web/openapi/raffa-api.v1.json`, `schema.ts` and `client.ts` carry no entry for it (grepped: zero hits for this path in any of the three — the client wrapper feature-03 itself was meant to write, ADR-012 §3, was never committed), and `EvidencePane.tsx` (`web/src/routes/contracts/review/`) has no phrase-edit UI. The endpoint is real and tested (`backend/tests/Raffa.Api.Tests`); today it is reachable only by a direct API call, never from the product |
| GET | `/api/contracts/{id}/corrections` | `X-Tenant-Id` header; field-level correction history for one contract, newest first (`Raffa.Documents.Contracts.Application.ContractCorrectionHistoryQueryService`) — 404 if the contract does not exist for the tenant, `[]` if it exists but was never corrected |
| GET | `/api/audit` | Tenant audit trail, newest first, Admin only (story us-02-audit-read-for-a-real-admin AC-1..AC-5, task E01/F06/US02/T02; wave w16 NW-08, task E18/F02/US02/T01, ADR-025 §K). `X-Tenant-Id` header now required — the same `ICallerContext` → `WorkspaceRoleResolver` ladder `POST /api/documents/{id}/reprocess`/`DELETE /api/documents/{id}` already use: missing/non-GUID header → 400, a well-formed tenant with no live membership → 404 (never 403 — ADR-025 Rule B1, a 403 there would be a tenant-existence oracle), a live member who is not Admin → 403, a live Admin membership → 200, that tenant's own rows only. Bare array response, each row `{ id, actor, action, resourceType, resourceId, occurredAt, detail }`, capped at `AuditQueryService.MaxResults` (200), no caller-supplied paging/filters yet. The claims-based guard this route used before wave w16 (`WorkspacePrincipalAuthorization` — a `tenant_id` claim plus a `ClaimTypes.Role` claim, forbidden as an authorization source by ADR-010's w14 footer and unreachable by any real client since nothing in this host ever minted either claim) is deleted whole, not merely bypassed (ADR-025 §K.2) |
| GET | `/api/contracts/{id}/negotiation-steps` | `X-Tenant-Id` header; the ticked steps of Contract 360's negotiation tracker "4-step checklist" (task E19/F03/US01/T01, NW-13, us-01-step-ticks-api; ADR-028 §D3), as a bare array of step keys in canonical checklist order — `Raffa.Documents.Contracts.Domain.NegotiationStep`'s four members, `Notify`/`RequestRevisedPricing`/`CounterWithMarketBenchmark`/`SignOrSendNonRenewalNotice` (the design oracle's own order), never an array index and never the rendered label (that stays entirely client-side, two of the four parameterized). The row's presence **is** the tick — no `ticked` boolean exists to read. 404 when the contract does not exist for the tenant, `[]` when it exists but nothing has been ticked yet |
| PUT | `/api/contracts/{id}/negotiation-steps` | Writes the whole ticked-step set (task E19/F03/US01/T01, NW-13); `X-Tenant-Id` header; body `{ steps: string[] }` — a key absent from `steps` unticks it (the row is deleted, there is no boolean to flip), so sending the same set twice is a true no-op; no merge semantics. 400 for an unknown step name, with nothing written (`Raffa.Documents.Contracts.Application.NegotiationStepService.SetAsync` validates every name before it queries or mutates anything); 404 for an unknown contract. Response is the resulting set, same shape as the `GET` above; also writes one `IAuditWriter` entry (`contract.negotiation_steps_set`) with the validated caller identity as actor |
| GET | `/api/audit` | tenant-scoped; expects a claims principal (integration tests inject one) |
| GET | `/api/contracts` | portfolio list; spec §8.1 columns; `X-Tenant-Id` header; optional filters `supplierId`, `status`, `risk` (Low/Medium/High/Critical), `autoRenewal`, `minAnnualSpend`, `maxAnnualSpend`, `renewalFrom`/`renewalTo` (yyyy-MM-dd), `category` (task E24/F01/US01/T01, story us-01-portfolio-category-backend; closes NW-23/OQ-w17-007) — exact-match against `Supplier.Category`, resolved by a host join in `PortfolioEndpointExtensions` since `Raffa.Documents.Contracts` cannot reference `Raffa.Suppliers.Products` (ADR-002); blank/absent leaves the full tenant-scoped portfolio, a category no supplier carries narrows to an empty `items` array, never a fabricated one; optional paging `page` (default 1), `pageSize` (default 25, max 100); response is `{ items, page, pageSize, totalCount }`, not a bare array |
| GET | `/api/contracts/{id}` | Contract 360 aggregate; spec §8.2 header + tabs (overview, commercials, products, clauses, obligations, risks, documents, benchmark, renewal, activity); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant. **Wave w17 (NW-20, task E21/F01/US01/T01):** `tabs.benchmark` is host-composed from the same resolved `(supplier name, geography)` key NW-22 uses — a representative position or an explicit `insufficient_data` entry, never `[]`; `tabs.activity` is a contract-scoped provenance projection of allow-listed audit actions (`occurredAt`, `action`, `actorLabel`), never `AuditEvent.Detail`. Both members are filled in `Raffa.Api`, never inside `Contract360QueryService` |
| POST | `/api/chat/query` | Ask Raffa V2 (ADR-024 §6; task E13/F06/US01/T01, ask-engine); `{ question: string }` + `X-Tenant-Id` header + caller identity (see "Interim auth" below). Kept one release as a thin alias: creates a conversation, then delegates into the same `AskCopilotService`/`POST /api/conversations/{id}/messages` pipeline (see "Ask Raffa — conversations store" below) — the old direct `AskRaffaQueryRouter` → `RagAnswerService` → `{ question, intent, canDetermine, answer, citations, message }` shape this route used to return (task E02/F04/US02/T01) no longer exists; that router is now reused *inside* `AskCopilotService` instead. Response is the ADR-024 §6 reply contract, same as the messages endpoint below |
| GET | `/api/conversations` | Caller's last N conversations, most recently updated first (spec §7; R-CONV-02; story us-01-conversations AC-2, task E13/F05/US01/T02); `X-Tenant-Id` header + caller identity (see "Authentication" below); optional `take` (default 5, must be a positive integer); response is a bare array of `{ id, title, scopeContractId, updatedAt }`, never an `{ items, totalCount }` envelope — there is no paging concept for "my last N conversations" |
| POST | `/api/conversations` | Creates a conversation (AC-2); `X-Tenant-Id` header + caller identity; body `{ scopeContractId? }` — a GUID naming the contract "Ask about it" (Contract 360) was opened from, or omitted for the global Ask bar (ADR-024: "The global Ask bar always opens a new chat"); 201 with the same `{ id, title, scopeContractId, updatedAt }` shape as the list row above; `title` starts as `ConversationService.DefaultTitle` ("New chat") until the first message lands |
| GET | `/api/conversations/{id}` | The conversation plus its messages, oldest first (AC-2); `X-Tenant-Id` header + caller identity; 404 when `{id}` does not exist, belongs to another tenant, or belongs to another user of the same tenant — RLS backstops the tenant half (ADR-009), `Raffa.Chat.Application.Conversations.ConversationService` itself is the only thing enforcing the per-user half (RLS has no per-user predicate), and both read back as the identical 404, never a distinguishing 403; response `{ id, title, scopeContractId, createdAt, updatedAt, messages: [{ id, role, kind, markdown, citations, actions, modelId, promptVersion, inputHash, createdAt }] }` — `role` is `you`/`raffa`, `kind` is `answer`/`abstain`/`redirect`/`refusal` (ADR-024 §6 wire literals); `citations`/`actions` are real JSON arrays, never a JSON string nested inside JSON; never the raw retrieval pack (ADR-011) |
| POST | `/api/conversations/{id}/messages` | Ask Raffa V2 (ADR-024 §6; task E13/F06/US01/T01, ask-engine, AC-8); `{ question: string }` + `X-Tenant-Id` header + caller identity; 400 for a missing/invalid tenant or user header, an invalid `{id}`, or a blank `question` — all before any database call (see `Raffa.Api.Tests.ConversationsEndpointTests`). Runs the full engine (`AskCopilotService`: `Gate.DomainGate` →, for `in_domain` turns, `Planning.IntentPlanner` → per-intent context pack → guarded `answer` call → `Guards.GroundingGuard`/`NumericGuard`/`RegenerateOnce`), appends both the caller's question and Raffa's reply to the conversation via `ConversationService`, then returns the same ADR-024 §6 reply contract `GET /api/conversations/{id}` echoes back for one message: `{ kind, answerMarkdown, citations: [{ n, corpus, title, subtitle, snippet, documentId?, contractId?, page?, section?, previewUrl?, href?, recordId? }], actions: [{ label, href, kind }], provenance: { sources, modelId, promptVersion, inputHash }, followUps }` plus `conversationId`/`messageId` — never engineer chrome (a `Document:` guid, a "Structured query" line) in `answerMarkdown`. **Wave w18 additions:** an optional `scopeContractId` now threads from the conversation (`Conversation.ScopeContractId`, set at `POST /api/conversations` time) into `AskCopilotService.AskAsync`, resolved into the gate **before** the R-ASK-10 in-domain check so a scoped turn's own gate resolution never falls through to the generic `NeedsDocument` redirect and its pack/citations scope to that contract's own supplier (epic-25 feature-03, task E25/F03/US01/T01 + `ConversationsEndpointExtensions.AskAndAppendAsync`, closes NW-56). Every `abstain` reply this engine can produce — the guard-downgraded path (`CopilotReplyBuilder.FromGuardedResult`), the empty-pack path and the composer-failure path, `AskCopilotService.BuildInDomainReplyAsync` — now carries a non-empty `actions` array resolved from the real capability catalog, never model-authored (`ResolveAbstainRecoveryActions`, epic-25 feature-05, task E25/F05/US01/T01, closes NW-59): the Documents-upload action for a contract-free tenant, otherwise the Ask capability's own "ask about dates, spend, notice periods and clauses" hint action. **Known gap (found by task E23/F05/US01/T01, w18 final integration, not fixed there — out of that task's own file scope):** the contract-free branch resolves the upload action via `CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)`, which `CapabilityRouting.ResolveOne`'s generic `HowTo` case maps to `CopilotActionKind.Navigate` (label "Open Documents"), not `CopilotActionKind.Upload` — `backend/tests/Raffa.Api.Tests/AskAbstainRecoveryActionTests.Empty_pack_abstain_for_a_contract_free_tenant_offers_the_documents_upload_action` fails on this exact mismatch (`Expected: "upload", Actual: "navigate"`). The href still lands on `/documents` either way; only the action's `kind`/label are wrong. `CapabilityIntent.UnknownSupplier` (→ `UploadInDocuments()`, real `Upload` kind) is the fix, not yet applied. **Wave w19 additions (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl. 12; lock 4):** the w18 gate-level override above only carried the scoped contract's *name* forward, so two portfolio rows sharing one supplier's display name could still let a plain name lookup answer about the wrong one. `AskCopilotService.BuildInDomainReplyAsync` now also receives the scoped *id* itself and lets it win outright over that name lookup (never `FirstOrDefault`-by-name when a scope is set), and folds it into the routing context's own contract id so a follow-up action targets the real scoped contract too. A `scopeContractId` that does not resolve to a row in that turn's own freshly-fetched portfolio — wrong tenant, no linked document, deleted since the conversation was opened — now returns `kind: "refusal"` before any pack is assembled, rather than silently falling back to an unscoped answer. **Lock 4 exception:** a `PortfolioMarketPosition` question (`AskIntent.PortfolioMarketPosition`, NW-79/NW-86) is exempt from both rules — it is always portfolio-wide by construction, so it never depends on the scoped contract resolving and is never narrowed to it |
| GET | `/api/renewals` | Renewal pipeline + insight card (spec §9.3/§10.1); `X-Tenant-Id` header; auto-renewing contracts only, most urgent first; response is `{ items, totalCount }`, each item `{ contractId, supplierId, status, renewalDate, daysUntilRenewal, annualSpend, cancellationDeadline, daysUntilCancellationDeadline, autoRenewal, action, savedAction, insightCard: { facts, recommendations } }` — `insightCard.recommendations`' benchmark/savings fields (`annualUpliftPercent`, `marketPosition`, `potentialSavingsRange`) are honestly `null` until the Benchmark/Savings modules land (R3); `action`/`recommendedAction` is a deterministic urgency rule, not the full spec §9.2 Priority Score — see `Raffa.Renewals.Application.RenewalPipelineBuilder`'s own doc comment. **`savedAction` (task E19/F01/US01/T01, renewal-action-api; ADR-028 §D1)** is the persisted `POST .../action` row for that same contract — `{ contractId, owner, status, action, updatedAt }` — or `null` when nothing was ever recorded; resolved for the whole page in one batch call (`RenewalActionService.GetActionsAsync`), never a per-row query. Binding name: `savedAction` is never `action` — `action` stays the calculator's own `RecommendedAction`, unchanged, so the user's own saved state can never overwrite it |
| GET | `/api/renewals/{contractId}/priority` | Explainable priority-score breakdown for one contract (spec §9.2; story us-02-priority-score AC-1/AC-2, task E03/F01/US02/T02); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant (same rule as `GET /api/contracts/{id}`); response is `{ contractId, totalScore, components: { spendWeight, timeUrgency, benchmarkOpportunity, priceIncreaseRisk, contractRisk } }`, each component `{ score, explanation }` — component weights are configurable, see `Raffa.Renewals.Configuration.PriorityScoreWeightsOptions` below; `priceIncreaseRisk`/`benchmarkOpportunity` use their honest no-data default (minimum / neutral respectively) since no uplift or benchmark-position data is wired to real contracts yet |
| POST | `/api/renewals/{id}/action` | Updates owner/status/action for one renewal (spec Appendix A; story us-01-renewal-dashboard-api AC-3); `X-Tenant-Id` header; `{id}` is the same `contractId` the GET above returns per row, not a separate stored "renewal" id; body `{ owner, status, action }` — `status` is one of `NotStarted`/`InProgress`/`Completed`; upserts one row (never a second for the same contract) and writes one `IAuditWriter` entry (`renewal.action_updated`); 400 (not 404) for a missing/invalid tenant header or route id, or for an empty `owner`/`action`/unrecognized `status` — see `Raffa.Renewals.Application.RenewalActionService`'s own doc comment for the honest gap this leaves (no check that `{id}` names an existing, tenant-owned contract; `Raffa.Renewals` cannot reference `Raffa.Documents.Contracts` at all) |
| GET | `/api/renewals/{id}/action` | Reads back the row the POST above wrote (task E19/F01/US01/T01, renewal-action-api; ADR-028 §D1; parent story us-01-renewal-action-api AC-1); `X-Tenant-Id` header; `{id}` carries **exactly** the same contract-id meaning as the POST above (confirmed, not assumed — ADR-028 assumption 1); `200 { contractId, owner, status, action, updatedAt }` — the identical shape the POST already returns, via the same `RenewalActionService.GetActionAsync`/`ToActionResponse` — or **404** when nothing was ever recorded for this contract (never a default/placeholder body: absence of a row **is** the status `NotStarted`, which the caller renders itself, not one this route fabricates); no `DELETE` route exists or is planned — "Undo" is a `POST` of `NotStarted`, and the row survives it (the table's own upsert on `(tenant_id, contract_id)`). The identical row is also embedded under `savedAction` on every `GET /api/renewals` row above |
| GET | `/api/renewals/{id}/negotiation-todos` | Ask's ranked negotiation-point TODO list for one contract (task E29/F01/US01/T01, todo-entity-api; parent story us-01-todo-entity-api AC-2; wave w19 NW-85; ADR-028/ADR-009/ADR-011 w19 — see `Raffa.Renewals.Domain.RenewalNegotiationTodo`'s own doc comment); `X-Tenant-Id` header; `{id}` is the same `contractId` every other route in this file uses; any live tenant member may read — no extra role gate (same posture as `GET /api/renewals/{id}/action`). Bare array response (a small, unpaginated, per-contract list — same shape `GET /api/audit` already uses for the identical reason), ordered by `rank` then `pointKey`, each item `{ contractId, pointKey, topic, rank, current, target, rationale, citationKeys, source, status, createdAt, updatedAt }` — `status` is `Open`/`Done`/`Superseded`, `source` is always `"ask"` today; `[]`, never 404, when nothing was ever upserted for this contract (`Raffa.Renewals.Application.RenewalNegotiationTodoService.GetAsync`) |
| PUT | `/api/renewals/{id}/negotiation-todos` | Ticks one negotiation point `Done` (task E29/F01/US01/T01; parent story us-01-todo-entity-api AC-2/AC-3); `X-Tenant-Id` header; body `{ pointKey }`. Guard order: `ICallerContext` first (401 no identity, 400 missing/non-GUID tenant header, 404 a well-formed tenant with no live membership) — then, **before the route id is even parsed** ("authz before retrieval", waves/w19.md's own NW-85 row, security-architect), the caller's workspace role via `WorkspaceRoleResolver` (the same seam `GET /api/audit` uses for its own Admin-only gate, narrowed here to Admin **or** Procurement — parent story AC-2 "tick PUT mirrors `POST /api/renewals/{id}/action` roles (Procurement/Admin)"): any other live role is 403 — then the route id's GUID format (400) — then the tick. **404**, never a created row, when `pointKey` names nothing for this (tenant, contract): this route never invents a point (client-architect). Success returns the ticked row, same shape as the `GET` above, and writes one `IAuditWriter` entry (`renewal.negotiation_todos_written`) with the caller's resolved token subject as actor (never a default, never the model — ADR-011 w16 §15). The idempotent-upsert half of this table (same `pointKey` refreshes `current`/`target`/`rationale`/`rank`; a tick is never un-done by a later upsert; a `pointKey` absent from a later ranked set becomes `Superseded`) has no HTTP route yet — it is `RenewalNegotiationTodoService.UpsertAsync`, called in-process by `Raffa.Api.AskCopilotService` after ranking and before the answer (epic-29/feature-02, not yet wired) |
| GET | `/api/savings` | Lists the caller's tenant-scoped `SavingsOpportunity` rows, newest identified first (spec §4.3/§6; module-map.md "Savings \| SavingsOpportunity, RealizedSavings \| /api/savings"; story us-02-savings-opportunity AC-1, task E04/F02/US02/T01; story us-01-savings-kpis AC-2/AC-3, task E04/F03/US01/T02); `X-Tenant-Id` header; response `{ items, totalCount }`, each item also carrying `confidenceLevel` (`Low`/`Medium`/`High`, task E04/F03/US01/T02 — see `SavingsOpportunityResult.ConfidenceLevel`'s own doc comment); no filters yet — see `Raffa.Savings.Application.SavingsOpportunityService.ListAsync`'s own doc comment |
| PATCH | `/api/savings/{id}` | Updates `owner`, `status` (`Identified`/`InProgress`/`Realized`) and/or `realizedAmount` on one `SavingsOpportunity` (AC-1 "updates status/owner..."; AC-3 "realized value is captured and audit-tracked", task E04/F02/US02/T02); `X-Tenant-Id` header; body `{ owner?, status?, realizedAmount? }` — a genuine partial update, any subset of the three fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, a negative `realizedAmount`, a `realizedAmount` combined with an explicit `status` other than `Realized`, or none of the three fields supplied); writes one `IAuditWriter` entry per successful call — `savings_opportunity.updated`, or `savings_opportunity.realized` instead when `realizedAmount` was supplied (never both). Supplying `realizedAmount` also inserts a new, append-only `Raffa.Savings.Domain.RealizedSavings` row (in the opportunity's own `currency`) and finalizes `status` as `Realized` — either because the caller's own explicit `status` already said so, or automatically when `status` was omitted (see `SavingsOpportunityService.UpdateAsync`'s own doc comment). The response's `realizedAmount` field is non-`null` only on the call that just recorded one — it is not a rolled-up read of this opportunity's full realized-value history, see `SavingsOpportunityResult.RealizedAmount`'s own doc comment; the response also carries `confidenceLevel` (task E04/F03/US01/T02 — same field the `GET` row above documents, shared `ToResponse` wire-shaping) |
| PATCH | `/api/savings/{id}` | Updates `owner` and/or `status` (`Identified`/`InProgress`/`Realized`) on one `SavingsOpportunity` (AC-1 "updates status/owner..."); `X-Tenant-Id` header; body `{ owner?, status? }` — a genuine partial update, either or both fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, or neither field supplied); writes one `IAuditWriter` entry (`savings_opportunity.updated`) per successful call — setting `status` to `Realized` here does **not** yet create an audit-tracked realized-value record, see `Raffa.Savings.Domain.SavingsOpportunityStatus.Realized`'s own doc comment for the gap task E04/F02/US02/T02 (`RealizedSavings`) closes |
| POST | `/api/quotes` | New Purchase / Quote Check (spec §4.4/§11; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-quote-line-extraction AC-1/AC-2/AC-4, task E05/F01/US01/T01); multipart `file` + `X-Tenant-Id` header, same shape as `POST /api/documents`, plus four **optional** form fields task E05/F02/US01/T01 (market-assessment) added — `supplier`, `currency`, `geography`, `purchaseDate` (`yyyy-MM-dd`) — all absent by default and never required for the upload to succeed; nothing in this codebase auto-detects them from the document yet (spec §11.1's own "Identify supplier" workflow step has no task/UI of its own), so a quote uploaded without them simply is not matchable via `GET .../assessment` below until corrected (see `Quote`'s own doc comment; a malformed `purchaseDate` is the one new 400 this endpoint can return); synchronously reuses the epic-02 `HybridDocumentParsingService` (native text or the `ocr` gateway role — ADR-017, no 2-page cap) then runs one schema-constrained `extract` call for line items (quantity/SKU/edition/price/discount/term), persisting one `Raffa.Quotes.Domain.QuoteLine` row per item with source span/page/confidence; `unitPrice`/`extendedPrice` are derived deterministically in code when the model reports only `listPrice`/`discountPercent` (AC-3, Appendix C rule 6 — never asked of the model, see `QuoteLineJsonSchema`); immediately afterward, still the same unit of work, `Raffa.Quotes.Application.Normalization.QuoteLineNormalizationService` (task E05/F01/US01/T02, quote-normalization) sets `NormalizedAnnualUnitPrice`/`NormalizedTermMonths` when `term` matches its own small, fixed billing-cadence vocabulary (monthly/quarterly/semi-annual/annual and common synonyms; every other term deliberately leaves both `null` — spec §11.3's own "line-item normalization is unresolved" outcome, Appendix C rule 10), then `Raffa.Quotes.Application.Normalization.SkuNormalizationService` (task E05/F01/US02/T01, sku-normalization) sets `NormalizedSku`/`NormalizedEdition`/`MatchStatus`; response `{ id, fileName, mimeType, processingStatus, lineItemCount, normalizedLineItemCount, unresolvedNormalizationCount, unmatchedSkuCount, supplier, currency, geography, purchaseDate, createdAt }` — the last four echo exactly what was recorded, including a `null`; a pipeline failure still returns 201 (the upload itself succeeded) with the pre-processing counts all `0`, never an HTTP error. *(This row previously existed twice, one per sibling task's own addition, each missing the other's fields — task E05/F02/US01/T01 consolidated it into the one, accurate, combined shape above.)* |
| GET | `/api/quotes` | The tenant's quotes, newest first (parent story us-01-quote-read-api AC-1, task E19/F02/US01/T01, quote-read-api; wave w16 NW-12, ADR-028 §D2); `X-Tenant-Id` header; response `{ items }`, each item `{ id, fileName, mimeType, processingStatus, supplier, currency, geography, purchaseDate, createdAt }` — the same stored fields `GET /api/quotes/{id}` below echoes, minus `outcomes` (this is a tenant-scoped list, never an outcomes feed). Backed by `Raffa.Quotes.Application.QuoteQueryService.ListAsync`, modelled on `PortfolioQueryService`/`DocumentQueryService`: returns stored columns only, never re-runs `MarketAssessmentService` (ADR-028 §D2: "returns stored fields and computes nothing"). No filters/paging yet — this story names none |
| GET | `/api/quotes/{id}` | One quote for this tenant, with every recorded `NegotiationOutcome` embedded, newest first (parent story us-01-quote-read-api AC-2/AC-3/AC-4, task E19/F02/US01/T01, quote-read-api; wave w16 NW-12, ADR-028 §D2); `X-Tenant-Id` header; 404 when `{id}` does not name a quote for this tenant — including another tenant's quote (cross-tenant read is a 404 with zero leaked tenant-A fields anywhere in the body, never a 403 tenant-existence oracle); 400 for a non-GUID `{id}`. Response is the same quote shape as the `GET /api/quotes` row above plus `outcomes: [{ id, originalQuoteTotal, targetPrice, finalPrice, realizedSaving, discountPercent, negotiationDurationDays, leversUsed, capturedAt, savingsOpportunityId }]` — the same fields `POST /api/negotiations/outcomes` itself returns on capture, minus that call's own two capture-time-only propagation-attempt fields (`savingsPropagated`/`savingsPropagationError` report whether *that call's* propagation attempt succeeded, not a stored fact). A quote with no recorded outcome returns `outcomes: []`, never 404 (AC-2). `NegotiationOutcome` is keyed by `QuoteId` and is append-only, so its outcomes are a property of the quote, not a tenant-wide feed — **there is no `GET /api/negotiations/outcomes`** (ADR-028 §D2: no caller until NW-57, W18) and **the assessment endpoint below is deliberately not overloaded with this** either (`.../assessment/recalculate` returns that identical shape, so carrying the outcome there would make a recalculation appear to re-report a negotiation it never touched — one extra GET on mount is the cheaper of the two costs). Backed by `Raffa.Quotes.Application.QuoteQueryService.GetAsync` — stored fields only, same "computes nothing" posture as the list above |
| GET | `/api/quotes/{id}/assessment` | Quote assessment (spec §4.4/§11.2, Appendix A "Quote assessment"; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-market-assessment AC-1/AC-2 (both the "flag" half, task E05/F02/US01/T01, and the "recommended target range + potential saving" half, task E05/F02/US01/T02)/AC-3); `X-Tenant-Id` header; 404 when `{id}` does not name a quote for this tenant; one assessment per `Raffa.Quotes.Domain.QuoteLine` on the quote (creation order) — `{ quoteId, lines: [{ quoteLineId, status, position, unitPrice, quantity, benchmark, confidence, targetSaving, explanation }] }`. `status` is `Assessed`/`QuoteDataUnresolved`/`InsufficientBenchmarkData` (`Raffa.Quotes.Domain.MarketAssessmentStatus`); `position` (`BelowMarket`/`InLine`/`AboveMarket`) is populated only when `status` is `Assessed` — the market band is `[P25, P75]` of the matched `Raffa.Benchmark.Contracts.BenchmarkResult.Distribution`, `InLine` otherwise (see `MarketAssessmentCalculator`'s own doc comment); `benchmark`/`confidence`/`targetSaving` are `null` exactly when no Benchmark Service call was even attempted (`QuoteDataUnresolved`: the quote is missing `supplier`/`currency`/`geography`/`purchaseDate`, or the line itself has no usable product/quantity/term/price), never withheld just because the comparison itself abstained (spec §11.3's benchmark-trust rule — `InsufficientBenchmarkData` still carries real `source`/`sampleSize`/`comparisonDimensions` provenance, and a real `targetSaving` object whose `recommendedTargetLow`/`recommendedTargetHigh`/`savingsRangeLow`/`savingsRangeHigh`/`totalSavingsRangeLow`/`totalSavingsRangeHigh` are honestly `null` with a named `explanation` — see `TargetSavingCalculator`'s own doc comment) |
| POST | `/api/quotes/{id}/assessment/recalculate` | Manual product-mapping correction + recalculate (spec Appendix A "Re-run after product mapping correction"; story us-02-sku-normalization AC-2's "...and allow manual product mapping" half, AC-3, task E05/F01/US02/T02, sku-recalculate); `X-Tenant-Id` header; body `{ mappings?: [{ sku, edition?, canonicalSku, canonicalEdition?, canonicalProductName? }] }` — `mappings` may be omitted/empty (`{}` is a valid body) for a pure "what's still unmatched" refresh with no new correction. 404 when `{id}` does not name a quote for this tenant; 400 when a supplied correction's `sku`/`canonicalSku` is blank — validated before any write. For each valid correction, upserts (never duplicates) one tenant-scoped `Raffa.Quotes.Domain.SkuProductMapping` row keyed on the normalized SKU (`Raffa.Quotes.Application.Normalization.SkuNormalizer.Normalize` — same case/whitespace rule `POST /api/quotes`'s own upload-time normalization uses), then re-runs `SkuNormalizationService.NormalizeAsync` for every line on the quote (not just the corrected one — a mapping learned here also resolves any other quote for this tenant sharing the same normalized SKU, the next time that quote is itself (re)normalized) and `MarketAssessmentService.AssessAsync`; response `{ quoteId, mappingsAppliedCount, normalization: { lineCount, matchedCount, unmatchedCount, notApplicableCount }, unmatchedLines: [{ quoteLineId, sku, normalizedSku, edition, description }], assessment: { ...same shape as GET .../assessment... } }` — `unmatchedLines` is AC-2's "Show unmatched SKUs" half made queryable over HTTP (deliberately not a field on the `GET .../assessment` response itself, see `SkuMappingService`'s own doc comment for why); writes one `IAuditWriter` entry (`quote.sku_mapping_recalculated`) per successful call, even a pure refresh. |
| GET | `/api/quotes/benchmark-history` | Tenant-wide quote benchmark history (parent story us-01-quote-benchmark-backend AC-1/AC-2, task E25/F04/US01/T01, quote-benchmark-backend; ADR-024, ADR-028, ADR-001; closes NW-57); `X-Tenant-Id` header; the tenant's quotes, newest first (same order `GET /api/quotes` above uses), each carrying a freshly-recomputed per-line market-benchmark assessment; never 404s — a tenant with no quotes yet gets `200` with an empty `items` array. Response `{ items: [{ id, fileName, mimeType, processingStatus, supplier, currency, geography, purchaseDate, createdAt, lines }] }` — `lines` is the exact same per-line shape `GET /api/quotes/{id}/assessment` above returns, reused verbatim (`QuotesEndpointExtensions.BuildLineAssessmentResponse`), so a first-of-type quote reports the same honest `InsufficientBenchmarkData` cold start here as it does there — never a fabricated position (ADR-001). Backed by `Raffa.Quotes.Application.QuoteBenchmarkHistoryService.GetHistoryAsync`, which composes `MarketAssessmentService.AssessAsync` per quote rather than re-deriving it; nothing new is persisted — see "Quote benchmark history" below |
| GET | `/api/savings/kpis` | Procurement-homepage KPI row (spec §4.3/§10.1; story us-01-savings-kpis AC-1, task E04/F03/US01/T01; **wave w17 NW-72, task E20/F01/US01/T01**); `X-Tenant-Id` header; response `{ annualSpendAnalyzed: [{ currency, amount, contractCount }], contractsAnalyzedCount, savingsIdentified/savingsInProgress: [{ currency, low, high, count, averageConfidence }], savingsRealized: [{ currency, amount, count }], upcomingRenewalsCount }` — every money value is grouped by currency, never summed across currencies (no exchange-rate service exists anywhere in this codebase); `contractsAnalyzedCount` counts contracts whose linked document reached `DocumentProcessingStatus.Completed`; **`savingsRealized` is verified money from `RealizedSavings` rows** (currency / amount / count) — never the opportunity's pre-negotiation estimate band, and an outcome with `savingsPropagated: null` enters no total; `upcomingRenewalsCount` is the same auto-renewing-contract count `GET /api/renewals`'s own `totalCount` already reports — see `Raffa.Api.SavingsKpiEndpointExtensions` |
| GET | `/api/capabilities` | The versioned V2 capability catalog (R-SYS-01; story us-01-capability-catalog, task E13/F08/US01/T01; mapped by task E13/F06/US01/T01, ask-engine; wave w16 NW-31, task E18/F03/US01/T01 deleted the server-side role filter): no tenant header — static, tenant-agnostic metadata, not a per-tenant read; served whole, every row carrying its own `roleGate` as a presentation label, never as authorization; see "Ask Raffa — capability catalog" below |
| GET | `/api/insights/criticality` | Portfolio-wide criticality ranking (story insights-calculators, task E13/F07/US01/T01; mapped by task E13/F06/US01/T01); `X-Tenant-Id` header; the same `Raffa.Insights.Criticality.CriticalityScoreCalculator` output `AskCopilotService`'s own `PortfolioStrategy` intent narrates — see "Insights" below |
| GET | `/api/contracts/{id}/strategy` | One contract's renewal-strategy pack (when you must move, where you can push, targets, next steps; task E13/F07/US01/T01; mapped by task E13/F06/US01/T01); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant; the same `Raffa.Insights.Strategy.StrategyPackBuilder` output `AskCopilotService`'s own `RenewalStrategy` intent narrates — see "Insights" below |
| GET | `/api/market/records/{id}` | One market-feed record, for the citation panel (R-EVD-02; task E13/F06/US01/T01, ask-engine); no `X-Tenant-Id` — shared, tenant-agnostic market data (ADR-024); 404 when `{id}` does not name a record in the mock feed; response `{ recordId, supplier, category, product, geography, currency, title, snippet, provenance, updatedAt, unitPriceP25, unitPriceP50, unitPriceP75, sampleSize, source, representative }` — see `Raffa.Api.MarketEndpointExtensions` |

**Authentication (NW-05, task E18/F01/US01/T01 — applied to every route on 2026-09-14):** the
caller is the validated **bearer token**, nothing else. `Program.cs` wires `AddJwtBearer` against
the `AzureAd__*` keys (issuer, audience = the API client id, lifetime, signing key all validated,
clock skew two minutes); `Raffa.Api.Infrastructure.TokenCallerIdentity` reads the token's `oid`
as the identity and its `email` claim as a binding aid. The interim `X-User-Id` header is **no
longer read anywhere** in this host.

Every tenant-scoped endpoint above goes through one seam,
`Raffa.Api.Infrastructure.ICallerContext`, in this order: no validated identity → **401** before the
tenant header is even looked at; `X-Tenant-Id` missing or not a GUID → **400**; a well-formed tenant
the caller holds no live `workspace_membership` in → **404**, never 403 (a 403 there would be a
tenant-existence oracle, ADR-025 Rule B1); otherwise the tenant scope is entered and the handler
runs. `X-Tenant-Id` is therefore an *authorized selector* — a caller may belong to several
workspaces, so the token alone names no tenant — never a source of trust. The routes that carry
`{tenantId}` in their path (workspace members/invites) verify the same membership against the
route value and ignore the header. `GET /api/workspaces` takes no tenant input and lists what the
identity belongs to; `POST /api/workspaces` needs an identity and the token's `email` claim
(the creator row is keyed by both the address and the `oid`).

The audit actor and the per-user key of conversations are that same validated identity
(`CallerTenantResult.Identity`). Until 2026-09-14 the data-plane routes still resolved the tenant
from the header alone with no identity at all — `GET /api/documents` answered 200 to an
unauthenticated request that merely supplied a tenant GUID; `TokenGateDataPlaneTests` now pins
the 401/404/400 order on every route family, and the two `TokenIdentityRetirementTests` that had
been skipped since w14 are active.

The web client generates TypeScript types from
`web/openapi/raffa-api.v1.json`. The API does **not** yet self-publish
OpenAPI; that document is hand-authored and must grow with these routes.

## Documents — admission gate (task E13/F04/US01/T01)

`POST /api/documents` refuses anything that is not a contract-related
document **before** it writes a blob, a `document` row, an `embedding` or an
extraction job (ADR-024 “gate before persistence”, `inputs/requirements.md`
R-DOC-01/02/03). The endpoint lives in
`Raffa.Api.DocumentsEndpointExtensions`; the decision itself is
`Raffa.Documents.Contracts.Application.Admission.DocumentAdmissionGate`.

Order of checks, and what each one returns:

| Step | Failure | Body |
|------|---------|------|
| tenant header, multipart shape, non-empty `file` | 400 | plain string |
| `file.Length` ≤ `Documents:MaxFileBytes` | 413 | `Raffa accepts files up to 50 MB. This file is larger.` |
| extension **and** magic bytes agree (`DocumentFormatSniffer`) | 415 | `Raffa reads PDF, Word, Excel and scanned images` |
| readable text ≥ `Documents:MinReadableChars` | 422 | `{ rejected: true, detectedType, confidence: 0, reason: "no_readable_text", hint }` |
| `classify` role returns a contract kind with confidence ≥ `Documents:AdmissionThreshold` | 422 | `{ rejected: true, detectedType, confidence, reason: "not_a_contract", hint }` |

Accepted formats are exactly PDF, DOCX, XLSX, PNG and JPEG — checked by
extension *and* signature, so a `.zip` renamed `.pdf` is a 415 that never
reaches the AI gateway, and a PNG renamed `.pdf` is refused rather than
silently re-labelled. What gets stored as `mimeType` is the **sniffed**
canonical type, never the browser's declared `Content-Type`.

A rejection persists nothing and writes exactly one audit row:
`document.rejected`, `resourceType` `document-upload`, `resourceId` = the
SHA-256 of the uploaded bytes, `detail` = `{ detectedType, confidence, reason,
readableChars, bytes, mimeType }` — never the file name, never any document
text (ADR-011). A parse/OCR or `classify` failure is **not** a rejection: it
returns 400 with the underlying error, because “we could not read it” is not
“it is not a contract”, and it leaves no audit row.

An admitted document is uploaded, then processed by
`DocumentProcessingPipeline`'s pages-and-classification overload, so the
gate's own parse and `classify` result are reused instead of being computed a
second time.

Thresholds are configuration (defaults in `DocumentAdmissionOptions`, applied
when the section is absent):

| Key | Default | Meaning |
|-----|---------|---------|
| `Documents:MaxFileBytes` | `52428800` (50 MiB) | larger uploads get 413 |
| `Documents:MinReadableChars` | `200` | non-whitespace characters across all parsed pages |
| `Documents:AdmissionThreshold` | `0.6` | minimum `classify` confidence for an admitted type |

Admitted types are MSA, Order Form, SOW, Amendment, Quote, Invoice, Price
list, NDA and DPA (`ContractDocumentType` gained the last five in this task).
`Other` is never admitted, so no `document` row is stored with it any more.
`RenewalLetter` remains reachable only by correction — nothing in the classify
taxonomy names it.

With the fixture gateway (no Foundry endpoint configured, ADR-004/ADR-017)
the gate is fully testable: a recipe PDF classifies as `Other` and is
rejected, a document containing “MASTER SERVICES AGREEMENT” is admitted as
`Msa`, and a PNG/JPEG whose bytes are the signature followed by UTF-8 page
text takes the `ocr` path — see `Raffa.Api.Tests.DocumentUploadEndpointTests`
and `Raffa.Documents.Contracts.Tests.Admission`.

## Documents V2 — list, preview, reprocess, delete (task E13/F04/US01/T02)

Everything the Documents V2 screen and Ask's citation cards read
(`inputs/requirements.md` R-DOC-06…R-DOC-10, R-EVD-01). The endpoints live in
`Raffa.Api.DocumentsEndpointExtensions`; the work itself is in
`Raffa.Documents.Contracts.Application` (`DocumentQueryService.ListAsync`,
`DocumentReprocessService`, `DocumentDeleteService`, `Preview/*`).

**Page-aware chunks.** `embedding` gained `page` and `section`
(migration `AddDocumentPreviewAndEmbeddingPage`). Every chunk the pipeline
indexes now carries its 1-based page, and the section label when staged
extraction has already attributed a clause to that page. Both stay `null`
when genuinely unknown — an Ask citation prints “p.N” only when the page is
real. `document` gained `page_count` (what the parse produced) and
`preview_path`.

**Reprocess** (`POST /api/documents/{id}/reprocess`, Admin) loads the stored
bytes through `IDocumentStorage.LoadAsync`, deletes this document's existing
chunks, re-parses (native text or the `ocr` role, ADR-017) and re-indexes them
page-aware, then re-runs staged extraction so facts added by later tasks
back-fill onto documents uploaded before those tasks existed. It is a
delete-then-index, not an upsert: R-DOC-07 AC-1 requires that afterwards no
embedding row for the tenant starts with `%PDF`.

**Deletion** (`DELETE /api/documents/{id}`, Admin) removes the objects first,
then the rows. Clauses, obligations, risks, line items and evidence are
*detached* (their `source_document_id` set to null), never deleted: each of
those columns is an `ON DELETE RESTRICT` foreign key, and a fact whose source
file is gone is still a fact. The contract itself always survives.

**Preview — rasterised at pipeline time (wave w17 NW-26, task E22/F02/US01/T01,
ADR-029).** `DocumentPreviewService` renders **page by page** in the Worker after
admission (independent of extraction success) and stores each PNG under the
tenant prefix (`{tenant}/documents/{id}/preview/page-{n}.png`). Reprocessing
overwrites by that deterministic path; it never accumulates a suffix.
`PdfPageDocumentPreviewRenderer` (Docnet.Core / pdfium, MIT + BSD-3-Clause;
Linux native-dependency list: **empty**, so no Dockerfile `apt-get` layer) is
registered ahead of `PlaceholderDocumentPreviewRenderer`, which stays the
honest fallback for JPEG/DOCX/XLSX and for a renderer that returns null.
`GET /api/documents/{id}/preview?page=n` is bounded by the persisted
`document.page_count`: out of range is **404**, never a silent page 1. One
bitmap is disposed before the next is materialised (0.25 vCPU / 0.5 GiB shared
pair; `MaxConcurrentCalls = 4`).

**Per-field decision (wave w17 NW-71, task E22/F01/US01/T01).** One
`ExtractionConfidencePolicy` in `Raffa.Documents.Contracts` (raw stored
`confidence >= 0.90`, no rounding before the compare) is consumed by both
`StagedExtractionService.DetermineDocumentStatus` and
`DocumentQueryService.IsWeak`, so the Documents-row badge and `needs_review`
cannot disagree. The decision lands on the existing `extraction_evidence` row
as `decision` + `decided_at` (`auto_accepted` / `human_accepted` /
`review_required` — three states, no SQL enum, no new table). One audit row
per document per extraction run, actor `system:extraction`, names **field
names and confidence numbers, never a field value**. Critical fields use the
same bar; there is no always-review list. The word `officialized` is an ADR
word and never appears on a screen.

**Admin resolution** (`Raffa.Api.Infrastructure.WorkspaceRoleResolver`): a live
`workspace_membership` row for the caller's validated identity — **and nothing
else**. Two sources that used to sit ahead of it are both gone: a
client-declared role header (deleted wave w14, task E14/F02/US02/T01; ADR-022
w14 footer clause 1, ADR-025 §E) and, since wave w15 (NW-06, ADR-010 w15 footer
§3), role claims on an authenticated principal — deleting that second source is
what stops one Entra app-role assignment from resolving to that role in *every*
workspace the caller can name instead of only the ones they hold a real
membership in. **The membership row is the role source of truth**: a client-
asserted `Admin` never grants, and an asserted `Procurement` never revokes a
real Admin's rights (ADR-025 Rule E2). The identity itself is the validated
bearer token's `oid` (see "Authentication" above), never a header. Since wave
w16 (ADR-010 w16 footer S16-1, task E18/F02/US01/T01) the membership match is a
single key, `ExternalSubjectId` only — it used to also match `Email`, so a
`workspace_user` row whose `Email` happened to equal the caller's identity
conferred a role too; not exploitable while every identity is a GUID-shaped
`oid`, but `Email` is a column an Admin writes at invite time and must never be
an authorization input. No match means no role, and every Admin-only endpoint
answers 403. Wave w16 (NW-31, task E18/F03/US01/T01) also deleted the last
non-authoritative catalog filter on `GET /api/capabilities`: that route now
returns every catalog row, each carrying its own `roleGate` label.

**The identity is never lower-cased, anywhere in this chain** (ADR-010 w16
footer S16-2, task E18/F02/US01/T01): `oid` is an opaque, case-sensitive Entra
object id, so `Raffa.Api.Infrastructure.CallerContext`/`WorkspaceRoleResolver`
and `Raffa.Identity.Workspace.Infrastructure.WorkspaceDirectoryService` all
compare it ordinally, and `Raffa.SharedKernel.Tenancy.CallerIdentityContext`
(the seam `GET /api/workspaces` uses to set the `app.identity_subject` Postgres
session GUC the `identity_self` RLS policy reads) only trims it — it used to
also lower-case it, which silently broke that policy's own `external_subject_id
= guc` leg for any subject that was not already a canonical lowercase GUID.
Only the `Email` leg lower-cases (both sides, in SQL), matching how it is
always stored.

**Audit rows are written inside the tenant scope**, by the services rather than
by the endpoints: `audit_event` is itself RLS-protected, so a write with no
ambient tenant is rejected by Postgres (ADR-009/ADR-011).

## Worker

`Raffa.Worker` references the same application libraries as the API.
**Wave w15 (ADR-027, tasks E16/F02/US02/T01 + E16/F02/US03/T01): document
extraction runs here, not in the request.** `Raffa.Messaging` carries the
transport: `POST /api/documents` publishes one `ExtractionRequested`
pointer `{ tenantId, documentId, extractionJobId, schemaVersion }` to the
Service Bus topic `ServiceBus__TopicName` (`extraction-events`), and this
host's `ServiceBusExtractionConsumerHostedService` consumes the
`document-processing` subscription (PeekLock, manual completion, managed
identity via `DefaultAzureCredential`/`AZURE_CLIENT_ID`, `ServiceBus__*`
keys injected by Terraform). Each message goes through
`ExtractionRequestedHandler`: a compare-and-swap claim on the
`extraction_job` row (`claimed_at IS NULL`, ADR-027 §D3 — at-least-once
delivery becomes exactly-once work), then the admission gate, then
`DocumentProcessingPipeline`; a content refusal becomes a `Rejected` row
with its reason code and the blob deleted; a gateway that cannot be reached
releases the claim and abandons the message for redelivery, and after three
attempts the row itself goes `Failed` — the database, never the dead-letter
queue, owns the terminal state. With no `ServiceBus__FullyQualifiedNamespace`
configured both hosts fall back to an **in-process** channel
(`InMemoryExtractionQueue`), which is a single-process dev convenience and
never a bridge between the API and the Worker; `MessagingServiceCollectionExtensions`
logs which transport it picked at startup. Benchmark / quote handlers land
with those features; renewal threshold scheduling (task E03/F02/US01/T01)
is the first of the four `13.3 Background jobs` categories this host
actually runs — see "Renewal threshold scheduler" above for
`RenewalThresholdSchedulerHostedService` and its own honest gap (no real
cross-tenant contract source wired yet).

## AI Gateway

`Raffa.AiGateway` is wired into DI by `Raffa.Documents.Contracts`'s own
`AddDocumentsContractsModule` (so both the API and Worker hosts get a
working `IAiGateway` with no host-side change). `IAiGateway` is
`FoundryAiGateway` when `AiGateway:Endpoint` is set and `FixtureAiGateway`
— deterministic, provider-free — otherwise, always wrapped by
`LoggingAiGateway` (ADR-004/ADR-017/ADR-024); domain code depends only on
the interface. On Azure the endpoint and the per-role deployment names are
published by Terraform once `ai_gateway_wired = true` in
`infra/environments/<env>` (ADR-008 amendment 2026-09-09: the shared
`aisvc-raffa` account, the per-environment Foundry projects and the model
deployments `gpt-5.4-nano-dev` / `text-embedding-3-small-dev` on dev,
`gpt-5.4-demo` / `gpt-5.4-nano-demo` / `text-embedding-3-large-demo` on
demo, OCR `prebuilt-read` — see `infra/README.md`). Since ADR-017 w18,
`FoundryOcrClient` also calls Document Intelligence `prebuilt-layout` on the
same account for per-word `words`/`polygon` geometry (`AiOcrPage.Words`) —
config-selected `AiGateway:Models:Ocr` stays `prebuilt-read` (the text
path); `prebuilt-layout` is a fixed model id the client calls internally, no
new role/SKU/config. A `prebuilt-layout` failure never fails the OCR call:
every page just carries a null box instead (never an error). The fixture's `extract` role is no longer an
empty `{}` placeholder: `Raffa.AiGateway.Fixtures.FixtureContractFactExtractor`
reads the three scalar-fact stages (metadata, commercial terms, dates and
renewal terms) from the page-marked text with regular expressions — every
value quoted from the document, every `sourceSpan` the literal match, every
`sourcePage` resolved from the `[[PAGE n]]` markers, and a confidence that
states which rule fired (0.96 explicit cue, 0.9 a plain reading, 0.86
derived from other facts, 0.52 an ambiguity the text does not resolve: two
different annual amounts, a renewal clause that both affirms and denies
auto-renewal, two parties named without a supplier role). The four list
stages return an empty list. So on a fixture-backed host (local, CI, a
deployed environment whose root still has `ai_gateway_wired = false`) a plainly
written contract completes and an ambiguous one lands in `needs_review` with
real per-field evidence — for real reasons, never by default. Two pipeline
rules changed with it: an empty **list** stage (line items, clauses,
obligations, risks) now completes — an MSA legitimately has no priced line
items and "nothing to review" is not a state a reviewer can resolve — while an
empty **scalar** stage still goes to review; and the classification verdict
is recorded as the contract's `type` evidence row with its real confidence
(a verdict below 0.6 routes the document to review like any other weak fact). Per-role model ids/versions
(`classify`/`extract`/`embed`/`answer`/`ocr`) bind from the
`AiGateway:Models` configuration section (`AiGateway:Models:Extract:ModelId`,
etc. — env var form `AiGateway__Models__Extract__ModelId`; on Azure these
are the deployment names Terraform publishes) and default to ADR-004's
original candidate names when that section is absent (only meaningful on
the fixture path), so no config is required to run locally. The `ocr` role's page-count safety
budget (ADR-017: fail visibly, never silently truncate) is its own
`AiGateway:Ocr:MaxPagesPerDocument` section (default 300 — see
`AiGatewayOcrOptions`).

`Raffa.Documents.Contracts.Application.Extraction.HybridDocumentParsingService`
implements the hybrid OCR pre-pass (ADR-017): native text extraction
(`NativeDocumentTextExtractor` — real `DocumentFormat.OpenXml` for
DOCX/XLSX, a self-contained content-stream reader for PDF; no external PDF
library is referenced — see that class's own doc comment for why) when
sufficient, otherwise the full document (no 2-page cap) through the `ocr`
gateway role. `StagedExtractionService` runs product spec §7.2's
seven-stage pipeline (metadata → commercial terms → dates → price/SKU →
clauses → obligations → risk) over the resulting page-mapped text
(`DocumentPageText`) and persists every fact with source span/page +
confidence (spec §7.3) — directly on `ContractLineItem`/`Clause`/
`Obligation`/`Risk`, or via the `ExtractionEvidence` table for `Contract`'s
own scalar fields.

`Raffa.Documents.Contracts.Application.Extraction.DocumentProcessingPipeline`
(task E02/F06/US01/T01, r1-integration) is that caller: given the just-
uploaded bytes, it runs the hybrid parse, then `IAiGateway.ClassifyAsync`
over the resulting text (setting `Document.DocumentType` and completing
the `Classification` job `DocumentUploadService` queues at upload), then
`StagedExtractionService`, then indexes every parsed page into the
`embedding` table (see `EmbeddingRetrievalService` below) — one call proves
the whole spec §7.1 pipeline. `POST /api/documents` (`Raffa.Api.Program`)
runs it synchronously, in the same request, right after the upload itself
is durable — a deliberate interim choice (see `DocumentProcessingPipeline`'s
own doc comment): nothing in this codebase dispatches the queued
`Classification` job off a durable queue yet (`Raffa.Worker.Queue
.QueueConsumerHostedService` still never dispatches a received message to a
domain handler — see the Worker section below), so synchronous/in-request
is the smallest honest way to make R1's "upload → ... → Ask Raffa" promise
true on `dev`/`demo` today. A pipeline failure never turns an already-
successful upload into an HTTP error — it is recorded on the `Document`/
`ExtractionJob` rows and reported in the response, same as any other
per-stage failure in this pipeline.

`Raffa.Documents.Contracts.Application.EmbeddingRetrievalService`
(us-02-embedding-search-index) is the pgvector half of Ask Raffa RAG:
`IndexChunkAsync` embeds a text chunk via `IAiGateway.EmbedAsync` and
persists it to the `embedding` table; `SearchAsync` embeds a query the
same way and returns the tenant's nearest chunks by cosine distance
(`Vector.CosineDistance`), explicitly filtered by `tenant_id` on top of
that table's own RLS policy. Embedding generation never touches a
provider SDK directly — always through `IAiGateway`.
**Task E28/F02/US01/T01 (NW-81)** adds `SearchByContractAsync`, the
contract-scoped counterpart `AskCopilotService.BuildClausePackAsync` now
calls whenever a turn already names a contract: it resolves an
`EmbeddingSearchQuery.ContractId` to that contract's own `Document` rows
(`SourceType`/`SourceId`) for a "this contract" slice, plus a separate,
lower-`topK` "similar types" peer slice from other validated contracts of
the same `ContractDocumentType` — so a 37-contract tenant no longer gets
another supplier's MSA back for a question about one named contract.
`SearchAsync` remains the tenant-wide fallback for a fully unscoped
question (no contract named at all); its first caller is
`POST /api/chat/query` (task E02/F04/US02/T01, below) by way of
`AskCopilotService`. Neither method ever reaches the market-intelligence
feed — that stays behind `IMarketKnowledgeRetrieval`, a separate index,
never mixed into this tenant pgvector table (ADR-011).
`IndexChunkAsync`'s first production caller is `DocumentProcessingPipeline`
(task E02/F06/US01/T01, r1-integration, above) — one `Embedding` row per
parsed page, `SourceType="Document"`/`SourceId=<documentId>`, so a document
is retrievable for Ask Raffa immediately after it finishes processing. A
tenant that has never uploaded anything (or whose upload is still
processing/failed) still honestly returns "cannot determine" — there is
simply nothing indexed for it yet, not a bug.

**Task E13/F01/US01/T02 (foundry-gateway)** adds the live half of this
module: `Raffa.AiGateway.Foundry.FoundryAiGateway` implements all five
ADR-004/ADR-017 roles — `classify`/`extract`/`embed`/`answer` over an Azure
OpenAI-compatible chat-completions/embeddings surface, `ocr` over Azure AI
Document Intelligence's `documentModels/{model}:analyze` long-running
operation — against the one shared `AiGateway:Endpoint` (ADR-008's single
Cognitive Services account). `AddAiGatewayModule` now picks the
implementation at first resolution: `FoundryAiGateway` when
`AiGateway:Endpoint` is configured (Container Apps inject it on `dev`/
`demo`, see `infra/README.md` "AI Gateway / Foundry + Document
Intelligence"), `FixtureAiGateway` otherwise (local/CI, unchanged) — and
**always** wraps whichever one behind `Logging.LoggingAiGateway` (ADR-004/
ADR-011 "always log-wrapped"), which this module shipped as a class since
task E02/F01/US01/T02 but never actually wired into DI until now.
`IAiGateway` is therefore resolved Scoped, not Singleton, from this task
on — `LoggingAiGateway` depends on the Scoped `IAuditWriter`
(`Raffa.Audit`'s own registration), and every current `IAiGateway`
consumer (`DocumentProcessingPipeline`, `StagedExtractionService`,
`EmbeddingRetrievalService`, `HybridDocumentParsingService`,
`QuoteExtractionPipeline`, `RagAnswerService`) was already Scoped, so this
is a captive-dependency fix, not a behaviour change for any of them; see
`ServiceCollectionExtensions`'s own doc comment for the full reasoning.
Auth is `Azure.Identity.DefaultAzureCredential` (managed identity on
Container Apps, developer sign-in locally) against the
`https://cognitiveservices.azure.com/.default` scope — never a key in
config — acquired through one `FoundryTokenProvider` singleton and cached
until near expiry, not re-fetched per call. The `answer` role's request
body (`Foundry.Wire.ChatCompletionRequest`) has no `tools`/`tool_choice`/
`data_sources` property at all, so ADR-024's "no tools, no grounding"
compliance is a type-system guarantee rather than a remembered omission —
proved on a fake `HttpMessageHandler` in
`Raffa.AiGateway.Tests.Foundry.FoundryAnswerClientTests`, the same
fake-handler convention every `Foundry.*ClientTests` class uses so no unit
test ever calls live Azure. `AiAnswerRequest`/`AiAnswerResult` gained
ADR-024's structured-answer fields (`SystemPrompt`/`PackJson` on the
request; `AnswerMarkdown`/`CitationKeys`/`ActionKeys`/`AbstainReason`/
`FollowUps` on the result), all optional/nullable additions — the existing
`Answer`/`Citations` fields and every pre-existing call site
(`RagAnswerService`, `AbstainGuard`, and their own tests) keep compiling
and behaving unchanged; a later task ("F06") replaces
`RagAnswerService`'s own evidence-chunk-concat with the versioned persona
prompt + context pack ADR-024 describes. New root-level `AiGateway`
configuration keys (siblings of `AiGateway:Models`/`AiGateway:Ocr`, bound
by `Configuration.AiGatewayFoundryOptions`): `AiGateway:Endpoint`,
`AiGateway:ProjectName`, `AiGateway:DocumentIntelligenceConnection`
(non-secret — see `infra/README.md`), and `AiGateway:AnswerTemperature`
(default 0.2, ADR-024's own ceiling; a higher configured value is clamped,
never raised). `Raffa.AiGateway.Tests.SdkAllowListTests` proves the new
`Azure.Core`/`Azure.Identity` package references stay inside
`Raffa.AiGateway.csproj` — no other project in the solution may
reference `Azure.AI.*`/`Azure.Identity` (AC-3).

## Benchmark Service

`Raffa.Benchmark.IBenchmarkService.GetBenchmarkAsync` (task E04/F01/US01/T01)
is the normalized `getBenchmark` contract product spec §10.3 names — P25/P50/P75
plus metric/currency/confidence/source/updated/comparison, so Renewals/Savings/
Quotes never depend on a provider schema. Task E04/F01/US01/T02 adds
`Raffa.Benchmark.BenchmarkAdapterRegistry`, the pluggable
`IBenchmarkProviderAdapter` registry behind that interface, wired into DI by
`Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule` — it
config-selects the active adapter by name (`Benchmark:Adapter:ActiveAdapter`,
env var form `Benchmark__Adapter__ActiveAdapter`, default `"fixture"` —
`BenchmarkAdapterOptions`), the same "config-selected, swap without a code
change" convention `AiGatewayModelOptions` already uses for ADR-004.

Task E04/F01/US02/T01 (story us-02-fixture-adapter) added the first concrete
adapter as a class, but that task's own wave-spec phase ran alongside this
registry task (parallel, neither depends on the other), so it could not
register what it had just written — the adapter existed and was directly
unit-testable, but unreachable through `AddBenchmarkModule()`. Task
E04/F01/US02/T02 (fixture-confidence) closes that gap: only a concrete
adapter may ever reference a provider SDK — `Raffa.Benchmark`'s own project
file still carries none, and `Raffa.ArchitectureTests.DependencyDirectionTests
.Benchmark_module_must_not_reference_provider_sdks` fails the build if that
changes without an adapter to justify it — and now that adapter is actually
wired in. A host that calls `AddBenchmarkModule()` today gets a real,
resolvable `IBenchmarkService` (`BenchmarkAdapterRegistry`) whose default
configuration dispatches to a genuine, fixture-backed result; an unrecognized
configured adapter name (for example a `Benchmark:Adapter:ActiveAdapter`
naming a paid provider that has not been registered) still fails honestly
rather than fabricating one (ADR-001).
`Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter` (task E04/F01/US02/T01,
us-02-fixture-adapter) is that first `IBenchmarkService`/`IBenchmarkProviderAdapter`
implementation — deterministic and provider-free, backed by a hand-curated,
in-memory catalog of illustrative SaaS supplier/product comparables (never
Tropic, Vendr, or any paid market API — ADR-001, spec §10.2's "Strategic
requirement"). It registers under the name `"fixture"`
(`Configuration.BenchmarkAdapterOptions.DefaultAdapterName`), so
`BenchmarkAdapterRegistry` finds it with no separate name to keep in sync.
`GetBenchmarkAsync` requires a fixture to match on supplier, product,
geography, currency, contract term, quantity tier and a purchase-date
refresh window — seven of spec §10.4's eleven named comparison dimensions,
always more than supplier name alone — plus SKU as an optional,
confidence-boosting eighth. A fixture that clears every required dimension
*and* carries at least `FixtureBenchmarkAdapter.MinimumViableSampleSize`
comparables (task E04/F01/US02/T02: 10) returns P25/P50/P75 with a
sample-size-scaled confidence score (`Raffa`'s own score, spec §10.3 —
saturates at a sample size of 50); anything weaker — including a fixture that
matches every dimension but is too statistically thin to trust (task
E04/F01/US02/T02's own "weak-comparable abstain" objective) — returns the
explicit "insufficient market data" outcome (`Distribution: null`) instead of
a fabricated number (ADR-001; spec §10.4's benchmark-trust rule, verbatim: "a
precise-looking number from weak comparables is more dangerous than an
explicit 'insufficient market data' result"), falling back to a
same-supplier/same-product comparable's metric and sample size when one
exists so the caller still sees real (if insufficient) provenance.

`ServiceCollectionExtensions.AddBenchmarkModule` wires `IBenchmarkService` to
`BenchmarkAdapterRegistry`, which now dispatches to this adapter by default
(task E04/F01/US02/T02).

**Task E04/F04/US01/T01 (r3-integration)** closes the wiring gap this section
used to name here ("no host calls `AddBenchmarkModule` yet"):
`Raffa.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule`
now calls `AddBenchmarkModule` itself — the same "a module that depends on
another module's interface registers that dependency's own DI wiring
transitively" convention this host already uses for `AddDocumentsContractsModule`
-> `AddAiGatewayModule` (see "AI Gateway" above). `Raffa.Api` already calls
`AddSavingsModule`, so `IBenchmarkService` is now resolvable there with no
`Program.cs` change at all — proven end to end by
`Raffa.IntegrationTests.R3EndToEndTests` (see "R3 demo smoke test" below).
`Raffa.Worker` does not call `AddSavingsModule` (no worker job creates a
`SavingsOpportunity` today — see "Savings Intelligence" below), so it still
does not resolve `IBenchmarkService` either; that is the same, pre-existing
"wiring lands with the first real caller" gap, unrelated to this task's own
fix. `Raffa.Renewals`'s own
`RenewalPriorityInputs.BenchmarkMarketPositionPercent` (see "explainable
priority score" below) still has no real producer wired to it — a different
module, out of this task's own "do not touch unrelated wave artifacts" scope.

## Market Intelligence — mock feed, benchmark projection, in-memory notes

Task E13/F02/US01/T01 (market-feed-mock, ADR-024, R-MKT-01…04) fills in the
`Raffa.Market` scaffold with the "how companies actually close contracts"
side of Ask Raffa V2: a checked-in mock feed behind
`IMarketIntelligenceProvider`, projected into the existing `Raffa.Benchmark`
seam and into a searchable set of narrative notes — no paid third-party API
anywhere in this task or its project (ADR-001: "never a hard dependency of
the first V2 `demo`").

`Contracts/MarketDeal` is R-MKT-01's normalized record shape (supplier,
category, product, SKU, geography, currency, company-size band, term,
annual-value band, unit price P25/P50/P75, discount/uplift-cap/notice/payment
terms, negotiated clauses, closing period, sample size, source, updatedAt,
licence restrictions) — Raffa's own shape; a later live third-party client
(R-MKT-05) maps onto it, never the reverse (OQ-askv2-001).
`Mock.MockMarketIntelligenceProvider` reads the checked-in
`backend/fixtures/market-intelligence.mock.json` — **66 hand-written records
plus ~1,400 generated ones** (≥ 60, R-MKT-02). The hand-written rows are the
test/golden oracles (Salesforce, Microsoft, AWS, Snowflake, ServiceNow,
Slack, Zoom, Notion, HubSpot, Workday, SAP, Adobe, Atlassian, Google
Workspace, DocuSign, Okta, Allianz, AXA, Zurich, Swiss Re, facilities,
telco, logistics, professional services). The generated rows come from
`backend/scripts/generate_market_intelligence_mock.py` (deterministic seed):
a catalog of ~120 products / SKU editions anchored on 2026 public list
prices (Salesforce editions, Microsoft 365 E3/E5/F3, ServiceNow ITSM tiers,
Slack, Zoom, Atlassian, Workday, SAP, Oracle, Adobe, DocuSign, Okta, Google
Workspace, Datadog, Zendesk, GitHub, HubSpot, Snowflake credits, Databricks
DBUs, Tableau, AWS/Azure/GCP instance hours and storage, Big Four and IT
services day rates, telco sites and SIMs, insurance premiums, freight and
facility rates) spread across **US / EU / UK / CH / APAC** (USD / EUR / GBP /
CHF), 12/24/36-month terms and five company-size bands, with bands modelled
from the typical negotiated discount per category, size and term, plus
negotiated clauses, uplift caps, notice and payment terms. Generated ids
start with `MKT-ZZ-` (they sort after every hand-written id, so a tie never
shadows an oracle) and never add to a hand-written supplier/product pair.
Re-run the script after editing the catalog; it keeps the hand-written rows
byte-identical. 9 hand-written rows deliberately carry
`sampleSize < 5` so the abstain path below is exercised — every record
`source = "mock"` / `representative = true`. The JSON is **embedded** into
`Raffa.Market.dll` (not opened from a runtime file path) so every host
that loads the assembly — API, Worker, this project's own tests, a future
`seed-market-intelligence` job — reads the exact same bytes with no path
configuration and no dependency on a backend Dockerfile `COPY` step that
does not exist yet (see `MockMarketIntelligenceProvider`'s own doc comment).

**Benchmark projection:** `Benchmark.MarketFeedBenchmarkAdapter` implements
`Raffa.Benchmark.Adapters.IBenchmarkProviderAdapter` under the name
`"market-feed"`, matching on supplier + product always, plus geography /
currency / contract term (always present on both sides) and SKU (only when
both the query and a candidate deal name one — two deals disagreeing on SKU
never match each other). A match below `MinimumViableSampleSize` (5 — the
exact boundary the fixture's thin rows were built to cross) never publishes
a P25/P50/P75 distribution (spec §10.4 benchmark-trust rule, ADR-001);
`BenchmarkResult.Source` is always `"market-feed (representative, mock)"`.
`ServiceCollectionExtensions.AddMarketModule` registers this adapter into
the same `IBenchmarkProviderAdapter` enumerable
`Raffa.Benchmark.BenchmarkAdapterRegistry` resolves (`TryAddEnumerable` —
`FixtureBenchmarkAdapter` stays registered, still directly testable) **and**
makes it `BenchmarkAdapterOptions`'s active adapter by default — without
editing `Raffa.Benchmark` and regardless of whether a host calls
`AddBenchmarkModule()` or `AddMarketModule()` first. This does *not* use
`IServiceCollection.PostConfigure<BenchmarkAdapterOptions>`: that only takes
effect through the `Microsoft.Extensions.Options` `IOptions<T>` indirection,
and `Raffa.Benchmark`'s own registration never uses it (a plain singleton
factory instead) — `PostConfigure` here would be a silent no-op. Instead
`AddMarketModule` calls `IServiceCollection.Replace` with an otherwise
byte-for-byte copy of `Raffa.Benchmark`'s own factory (same configuration
section, same `Bind` call), which unconditionally wins the registration
slot regardless of call order — see
`ServiceCollectionExtensions.MakeMarketFeedTheDefaultActiveAdapter`'s own
doc comment for the full reasoning. An explicit
`Benchmark:Adapter:ActiveAdapter` configuration value still overrides the
default either way, unchanged.

**Two projections, one ingestion job (task E13/F02/US01/T02, market-index):**
R-MKT-03's "served from the persisted `market_record` rows, never from the
provider at question time" is now real. `Infrastructure.MarketDbContext`
(pgvector, **no** tenant interceptor and no RLS policy — ADR-024/ADR-011's
epic-13 amendment: shared, read-only, never a tenant row) owns `market_record`
(one row per `MarketDeal`, keyed by `RecordId`) and `market_embedding` (one
narrative chunk per record, `vector(1536)`, same convention
`Raffa.Documents.Contracts.Domain.Embedding` uses), plus the checked-in
idempotent `Migrations/Scripts/market.sql` (ADR-021 — see "Deployable schema
artifact" above for its place in the apply order); that script's own header
documents a conditional, self-activating grant (read for `raffa_app`,
read/write for `raffa_market_ingest`) that stays a harmless no-op until a
later infra task actually provisions those two roles.

`Ingestion.MarketIngestionService.IngestAsync` is the *only* caller of
`IMarketIntelligenceProvider` (ADR-024: "the provider is called only by that
job"): it upserts `market_record` by `RecordId`, composes one narrative per
record (`Retrieval.MarketNoteComposer.Compose`, unchanged from T01), embeds it
via `IAiGateway.EmbedAsync` (ADR-004 `embed` role, hash-logged), and replaces
that record's `market_embedding` row(s) — a second run against the same feed
version and payload changes zero rows and issues zero embed calls (parent
story AC-4). `Retrieval.PgVectorMarketKnowledgeRetrieval` (cosine distance,
top-k, optional category/geography filters) and `MarketFeedBenchmarkAdapter`'s
new `(IDbContextFactory<MarketDbContext>, IClock)` constructor then read only
this store, never the provider, so a throwing `IMarketIntelligenceProvider`
no longer affects either projection once ingestion has run
(`Raffa.Market.Tests.MarketModuleQuestionTimeIsolationTests`);
`Raffa.IntegrationTests.MarketIndexIsolationTests` proves AC-3's other half
— a tenant embedding search never returns a market note, a market search
never returns tenant chunks — against one shared Postgres database.
`ServiceCollectionExtensions.AddMarketModule(string? marketConnectionString)`
is the DI swap: `null` keeps T01's mock-feed/in-memory wiring (still the
default `Retrieval.IMarketKnowledgeRetrieval` — token-overlap over composed
notes, no index, no embedding call); a real `ConnectionStrings:Market`
instead registers `MarketDbContext` and switches both the notes-retrieval and
benchmark-adapter registrations to their DB-backed equivalents.

`Raffa.Worker.Program` now calls `AddMarketModule` (the connection string
stays optional — absent, T01's in-memory wiring stays) so its new one-shot
operator command, `Commands.IngestMarketCommand`
(`dotnet run --project backend/src/Raffa.Worker -- ingest-market --feed
backend/fixtures/market-intelligence.mock.json`; `--feed` is an
informational label only — the mock provider always ingests its one
checked-in feed version, see that type's own doc comment), has something to
call — still no scheduled/background ingestion job. `GET
/api/market/records/{id}` (`Raffa.Api.MarketEndpointExtensions`, parent
story AC-5 — one record with its provenance label and `updatedAt`) exists but
is deliberately not yet mapped from `Program.cs` — task F06/T01 (this same
phase) is expected to call `MapMarketEndpoints()`, the same "endpoint exists,
host wiring is a later task's job" shape `CapabilitiesEndpointExtensions`
already uses.
**Interim data source:** R-MKT-03 describes benchmark rows as "served from
the persisted `market_record` rows, never from the provider at question
time" once an ingestion job exists — this task adds no ingestion job and no
`market_record` table (that is T02's own scope: "Market index, ingestion,
DB-backed retrieval, record endpoint"). Until then,
`MarketFeedBenchmarkAdapter` calls `IMarketIntelligenceProvider.GetDealsAsync`
directly on every query — the only data source T01 has — an explicitly
interim shortcut T02 is expected to replace with the persisted-store read,
with no change to `Raffa.Benchmark.IBenchmarkService` or any domain-module
call site.

**In-memory notes retrieval (Projection 2, interface only in a later phase's
DB-backed form):** `Retrieval.MarketNoteComposer.Compose` turns one
`MarketDeal` into one narrative `Contracts.MarketNote` (e.g. "Companies of
500-2000 employees closing Salesforce Sales Cloud Enterprise in CH in
2026-Q1 paid P50 CHF 132 …, obtained a 4% uplift cap and 90-day notice…"),
labelled via `Contracts.MarketProvenance.Label` (`"representative market
data · mock feed · updated <yyyy-MM-dd>"`, R-MKT-04). `Retrieval
.InMemoryMarketKnowledgeRetrieval` — this task's default
`Retrieval.IMarketKnowledgeRetrieval` — scores every composed note by plain
token overlap against the query (no index, no embedding call) and returns
the top-K; task E13/F02/US01/T02 is expected to swap in a pgvector-backed
implementation over the shared, tenant-free `market_embedding` index behind
this same interface (R-MKT-03: "own table — never rows in the tenant
`embedding` table").

Task E13/F06/US01/T01 (ask-engine) is `AddMarketModule()`'s first real
caller (`Raffa.Api.Program`), the same "wiring lands with the first real
caller" sequencing this README already documents for `AddBenchmarkModule` /
`AddChatModule` above; that task also maps `GET /api/market/records/{id}`
(see the HTTP surface table above and `Raffa.Api.MarketEndpointExtensions`).

## Supplier identity

Task E13/F03/US01/T01 (story us-01-supplier-identity, ADR-024 "Supplier
identity") turns `Raffa.Suppliers.Products` from the bare scaffold task
E13/F01/US01/T01 left behind into this module's first real content:
`Domain.Supplier` (tenant-scoped: `Name`, `NormalizedName`, `Aliases`
(a Postgres `text[]`), `Category?`, `Country?`, `CreatedAt`, `UpdatedAt`)
under its own `Infrastructure.SuppliersDbContext` + Postgres RLS (same
`ENABLE`/`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy shape every
other module's own `AddTenantRowLevelSecurity` migration already uses).
`Application.SupplierNameNormalizer` lower-cases, strips the R-SUP-02 legal
suffixes (`Inc`/`Ltd`/`Limited`/`GmbH`/`AG`/`SA`/`SpA`/`S.r.l.`/`Srl`/`LLC`/
`Corp`/`Corporation`/`Co.` — a dotted and undotted spelling of the same
suffix fold onto the same token, e.g. `SpA`/`S.p.A.` both become `spa`),
strips punctuation and collapses whitespace — deterministic and pure, no
database. `Application.SupplierResolver` (the `ISupplierResolver`
implementation) matches an existing row by `NormalizedName` or by an entry
in `Aliases` before ever creating one, so "Salesforce, Inc." and
"salesforce" resolve to the same id (parent story AC-2); the unique index
on `(tenant_id, normalized_name)` is the database-level backstop against a
race between two concurrent first-seen resolutions.

The cross-module contract other modules get instead of referencing this
one directly (ADR-002: Documents/Renewals/the API may not reference
`Raffa.Suppliers.Products`) lives in
`Raffa.SharedKernel.Suppliers`: `ISupplierResolver.ResolveAsync(TenantId,
rawName, ct) → Result<SupplierRef>` and `ISupplierNameLookup.GetNamesAsync
(TenantId, ids, ct) → IReadOnlyDictionary<EntityId, string>` (batched, so a
list page resolves every row's supplier name in one call). Both are wired
by `Infrastructure.ServiceCollectionExtensions.AddSuppliersProductsModule
(string connectionString)` — a raw connection string the caller resolves
however it names its own configuration key; `Raffa.Api.Program` (task
E13/F06/US01/T01, ask-engine, its first real caller) reads it from
`ConnectionStrings:Suppliers` (env var form `ConnectionStrings__Suppliers`)
rather than the dots-stripped-full-module-name convention every other
module's own connection string uses (`DocumentsContracts`,
`IdentityWorkspace`) — a shorter key, since `SuppliersProducts` would
otherwise be the only three-word one. `Raffa.Worker` does not call
`AddSuppliersProductsModule` — nothing in the worker needs a supplier name
yet. Nothing in this codebase resolves a supplier name for a real contract
during extraction yet; that is task E13/F03/US01/T02's own job (the
`supplier` critical extraction fact, the pipeline's resolver call, and
reprocess back-fill) — `AskCopilotService` (see "Ask Raffa — conversations
store" below) is `ISupplierNameLookup`'s first real Ask-side caller, not
the extraction pipeline.

Tenant isolation is proved in
`Raffa.IntegrationTests.SupplierCrossTenantIsolationTests` — deliberately
not in `Raffa.Suppliers.Products.Tests` alongside the normalizer/resolver
unit tests, per this task's own file assignment — because `Raffa.Api`
does not reference this module yet, so unlike the `R0`–`R4` suites in that
same project it cannot go through `WebApplicationFactory<Program>`; it
drives `SuppliersDbContext` directly instead, the same shape
`Raffa.Renewals.Tests`' own per-module `*RlsCrossTenantIsolationTests`
already use.

### Supplier extraction, linking and names in read models (task E13/F03/US01/T02)

Supersedes the "nothing resolves a supplier name during extraction yet"
note above — that gap is what this task closes.

**The `supplier` critical fact (R-SUP-01).** `StagedExtractionService`'s
`metadata` stage now allow-lists a `supplier` field (the legal name exactly
as the document writes it, plus the same `sourcePage`/`sourceSpan`/
`confidence` evidence tail every other fact carries). It is the first entry
in `CriticalFields`, the set product spec §7.3 judges against
`CriticalConfidenceThreshold` (**0.8**) instead of the ordinary
`LowConfidenceThreshold` (0.6): a weak `currency` is a nuisance, a weak
`supplier` mis-attributes a whole contract to the wrong company. Below 0.8
the stage — and therefore the document — lands in `needs_review`. The
`ExtractionEvidence` row is written **either way**, so a rejected supplier
fact still reaches the review list with its page, span and confidence; only
`StagedExtractionSummary.AcceptedSupplierName` distinguishes accepted from
rejected. `ApplyMetadataFact` deliberately writes nothing onto `Contract`
for this field: `SupplierId` is a cross-module reference this module may not
resolve itself (ADR-002).

**Linking, and back-fill for free (R-SUP-02/R-SUP-03).**
`DocumentProcessingPipeline` takes an **optional** `ISupplierResolver?`
(defaulted to `null`, so the built-in container supplies it only where
`AddSuppliersProductsModule` was called, and a host or unit test without the
Suppliers module keeps extracting exactly as before). After a successful
extraction it resolves `AcceptedSupplierName` and sets `Contract.SupplierId`.
Four deliberate no-ops: no resolver, no accepted fact, a resolver failure
(this pipeline never fails an already-durable upload), and an unchanged
link. Because the call runs on **every** processing pass and reprocess
re-runs extraction, re-processing a contract stored before this feature
existed back-fills its supplier — no bespoke migration job, no schema change
(`Contract.SupplierId` has existed since the initial migration).

**Human correction re-resolves (R-SUP-03).** `ContractCorrectionService`
accepts `supplier` as a correctable field name — the same literal the review
list showed the reviewer. It is the one correctable field that is not a
plain `Contract` scalar: the caller sends a **name**, the service re-runs
`ISupplierResolver` over it and writes the resulting id. `CorrectionHistory`
records names on both sides (`ISupplierNameLookup` renders the previous
link), never guids. Resolution runs after every other field has passed
validation, so a rejected multi-field `PATCH` never leaves a stray supplier
row behind. Both ports are optional; where they are absent a `supplier`
correction is refused with `SupplierCorrectionUnavailableError` rather than
silently ignored. The no-op test is the *link*, not the rendered name, so
re-typing the supplier a contract already points at changes nothing.

**Names in read models (R-SUP-04, ADR-024).** `GET /api/contracts`,
`GET /api/contracts/{id}` (header) and `GET /api/renewals` (row **and**
insight card) all report `supplierName` alongside `supplierId`. The join can
only happen in `Raffa.Api` — neither Documents/Contracts nor Renewals may
reference the Suppliers module — so
`PortfolioEndpointExtensions.ResolveSupplierNamesAsync` is the single
scope-owning helper all three go through: one batched `ISupplierNameLookup`
call per page, wrapped in `ITenantContext.BeginScope(tenantId)`.
**That scope is not optional**: `SupplierNameLookup` reads an RLS-scoped
`SuppliersDbContext` and does not open a scope of its own, so calling it
without one returns an *empty* map — the failure would read as "this
contract has no supplier name" rather than as an error. `supplierId` stays
in every response, so a link whose supplier row has since disappeared shows
an id with a `null` name instead of losing both.

| Surface | Field | Resolved by |
|---------|-------|-------------|
| `GET /api/contracts` (each item) | `supplierName` | `PortfolioEndpointExtensions` |
| `GET /api/contracts/{id}` (`header`) | `supplierName` | `ContractsEndpointExtensions` |
| `GET /api/renewals` (item + `insightCard.facts`) | `supplierName` | `RenewalsEndpointExtensions` |

Proved by `Raffa.Documents.Contracts.Tests.StagedExtractionServiceTests`
(threshold + evidence), `DocumentProcessingPipelineSupplierTests` (link,
skip-when-weak, reprocess back-fill), `ContractCorrectionServiceTests`
(re-resolve, previous-name history, honest refusal),
`Raffa.Api.Tests.PortfolioEndpointTests`/`Contract360EndpointTests`/
`RenewalsEndpointTests` (`supplierName` on the wire) and
`Raffa.IntegrationTests.R1EndToEndTests` (the whole chain end-to-end
against real Postgres + RLS, including the back-fill).

## Ask Raffa — query router + deterministic queries + RAG citations

`Raffa.Chat.Application.AskRaffaQueryRouter` classifies a natural-language
question (product spec §8.3) as `Structured` (deterministic query/filter, no
LLM) or `Semantic` (needs RAG retrieval) — task E02/F04/US01/T01.
`DeterministicQueryPlanner` + `DeterministicQueryHandler` (task
E02/F04/US01/T02) turn a `Structured` decision into an actual answer for the
two families spec §8.3 names as "dates" and "spend": "which contracts renew
in the next N days" (a filter on `Contract.AutoRenewal`/`EndDate`) and
"what is our annual spend [with a supplier]" (a sum of `Contract.AnnualSpend`).
No supplier-name -> `SupplierId` resolution exists yet (Suppliers/Products is
still an empty scaffold — the same root cause as the portfolio list's missing
`category` filter above), so a question that names a specific supplier (for
example "What is our Microsoft annual spend?") is still summed across
**every** supplier today; `DeterministicQueryResult.SupplierScopeUnresolved`
is `true` whenever that happened, so a caller can tell "$700,000 total" apart
from "$700,000 with Microsoft" instead of presenting one as the other.
A structured question outside those two families (for example "total
contract value") is reported as `Unsupported` rather than answered against
the wrong field.

`Raffa.Chat.Application.RagAnswerService` (task E02/F04/US02/T01,
us-02-rag-citations, AC-1/AC-2/AC-3) turns a `Semantic` decision plus
already-retrieved, already-authorized evidence into a grounded answer with
citations via `IAiGateway.AnswerAsync` (ADR-004 `answer` role) — citations
or an explicit "cannot determine" (spec §8.4 "no evidence, no claim"), never
a fabricated answer. It also writes one `IAuditWriter` entry per successful
call (`chat.answered` — ADR-011 "audit of access"), never the raw
question/evidence/answer text.

`Raffa.Chat.Application.AbstainGuard` (task E02/F04/US02/T02, abstain-guard)
is the no-fabrication guard `RagAnswerService.AnswerAsync` runs on every
gateway result before it is audited or returned: a "cannot determine" result
passes straight through, but a "determined" result is only trusted when it
carries at least one citation, has non-empty answer text, and every citation's
`DocumentId` matches one of the evidence documents actually handed to the
gateway — otherwise the guard downgrades it to an honest "cannot determine"
(preserving the original `AiCallMetadata` for reproducibility) rather than let
an unsupported or hallucinated citation through (Appendix C rules 2 and 10).
`FixtureAiGateway` can never trigger this — it only ever echoes citations
built from its own input evidence — so today the guard is a no-op in practice;
it exists for the Foundry-backed `IAiGateway` implementation ADR-004
anticipates, which can hallucinate. The audit detail line gains one field,
`abstainGuardIntervened=true|false`, so an operator can see a caught
fabrication attempt without the guard silently discarding the signal — the
free-text reason itself is deliberately not logged (ADR-011: no model
output/content in audit rows).

`Raffa.Chat` cannot reference `Raffa.Documents.Contracts` (see
"Dependency direction" below), so neither `DeterministicQueryHandler` nor
`RagAnswerService` retrieves anything itself: both operate on caller-supplied
data (`ContractFact` / a pre-retrieved evidence list respectively) — small
DTOs/parameters the module owns or accepts, never the real `Contract`/
`Embedding` entities. `DocumentId` on an `AiEvidenceSnippet` built from an
`Embedding` hit is a `{SourceType}:{SourceId}` composite (not a bare id): a
row's `SourceId` only really identifies a document when `SourceType` is
`"Document"` — for `"Clause"`-sourced evidence it identifies the clause row,
and silently relabelling one as the other would misattribute the citation.

**Superseded by the V2 engine (task E13/F06/US01/T01, ask-engine):**
`Raffa.Api.ChatEndpointExtensions` (`POST /api/chat/query`) used to be the
composition root that closed the gap above directly — it resolved the
tenant, called `EmbeddingRetrievalService.SearchAsync` itself, and called
`RagAnswerService` for the `Semantic` branch only, with the `Structured`
branch left as an honest "not wired yet" (no `ContractFact` mapping existed).
`POST /api/chat/query` now instead delegates into `AskCopilotService`, the
new V2 pack-composition root that reuses this router/planner/handler trio as
one of several intents — see "Ask Raffa — conversations store" below for
where that composition now lives; `AskRaffaQueryRouter`/
`DeterministicQueryPlanner`/`DeterministicQueryHandler`/`RagAnswerService`/
`AbstainGuard` themselves are unchanged, still pure, and still directly
unit-tested exactly as this section describes.

## Ask Raffa — conversations store

Task E13/F05/US01/T01 (story us-01-conversations, ADR-024 "Conversations
(D5)") gives `Raffa.Chat` its own persistence, independent of the router/
RAG pieces above: `Infrastructure.ChatDbContext` (two tables,
`Domain.Conversations.Conversation` / `ConversationMessage`, both
`TenantScopedEntity` — this module's own copy, not a shared reference, of
`Raffa.Documents.Contracts.Domain.TenantScopedEntity`'s identical shape,
since `Raffa.Chat`'s ADR-002 allow-list is exactly `[SharedKernel,
AiGateway]`) under Postgres RLS (`FORCE ROW LEVEL SECURITY` + policy on
`app.tenant_id`, same shape as every other module — see
`ChatMigrationScriptTests`), and
`Application.Conversations.ConversationService` (create / list-recent /
get-with-messages / append-message). RLS has no per-user predicate, so
"another user of the same workspace cannot read this conversation"
(R-CONV-01 AC-1) is an *application-level* filter on
`Conversation.UserId` — every `ConversationService` method filters by both
tenant and user, not tenant alone (see that type's own doc comment).
Writes two audit rows: `conversation.created`, `conversation.message.appended`
— never the message markdown/citations content itself (ADR-011).

`Title` starts as `ConversationService.DefaultTitle` ("New chat") and is
derived from the first `you`-role message, truncated to
`ConversationService.TitleMaxLength` (48, R-CONV-01 "first question, <= 48
chars") the moment it lands — never re-derived from a later message.
`ConversationMessage.Role`/`Kind` are C# enums stored as strings (PascalCase
column values, e.g. `"You"`/`"Answer"`); mapping them onto ADR-024 §6's
lowercase wire literals (`you`/`raffa`, `answer`/`abstain`/`redirect`/
`refusal`) is the HTTP layer's job, not this module's.

`Infrastructure.ServiceCollectionExtensions.AddChatModule` gained an
optional `chatConnectionString` parameter (AC-4): called with none, it
registers exactly what it always has — the query router/RAG services
above, no database — so nothing that already resolves them without a
connection string breaks (`Raffa.Chat.Tests.ServiceCollectionExtensionsTests`
proves this). Called with one, it additionally registers `ChatDbContext` +
`ConversationService`, keyed by this story's own council-decided
`ConnectionStrings:Chat` (`ConnectionStrings__Chat` env var form, same
`Raffa.Chat.Infrastructure.ChatDbContextFactory` design-time fallback
shape as every other module's `<Module>DbContextFactory`).

**Task E13/F05/US01/T02 (conversations-api)** is that first real caller:
`Raffa.Api.Program` now reads `ConnectionStrings:Chat` and calls
`AddChatModule(chatConnectionString)` — the same fail-fast shape (throws a
named `InvalidOperationException` when the key is missing) as every other
required connection string in that file — and
`Raffa.Api.ConversationsEndpointExtensions.MapConversationsEndpoints()`
maps `GET/POST /api/conversations` and `GET /api/conversations/{id}` (see
the HTTP surface table above for the exact request/response shapes). The
composition root resolves caller identity and tenant through
`ICallerContext.ResolveTenantAsync` — the validated token's `oid`, never a
header, with no fallback (NW-05, task E18/F01/US01/T01; see "Authentication"
above) — then calls straight into `ConversationService`; that service's own
`tenantId`/`userId` parameters already do all the RLS/application-level
scoping, so this file has no scoping logic of its own to get wrong. A
conversation created under the pre-w15 `X-User-Id`/email posture is keyed by
that email and stays unreachable by any `oid` — recorded, never remapped,
never deleted (ADR-001 w16 footer clause 1, task E18/F02/US01/T01).

### The V2 engine (task E13/F06/US01/T01, ask-engine, ADR-024)

`POST /api/conversations/{id}/messages` — deliberately left unmapped by
T02 above until an engine existed to produce a turn worth persisting — is
now mapped in this same file, and `POST /api/chat/query` (see "Ask Raffa
— query router..." above) becomes a thin alias that creates a conversation
and delegates into the identical pipeline. Both routes share one
composition root, `Raffa.Api.AskCopilotService` (`AskAsync`) — the pack
-composition root ADR-024 calls for: everything `Raffa.Chat`'s ADR-002
allow-list (`[SharedKernel, AiGateway]`) forbids that module from doing
itself (querying `PortfolioQueryService`/`Contract360QueryService`,
`EmbeddingRetrievalService.SearchByContractAsync`/`SearchAsync`, `RenewalEngine`/
`PriorityScoreCalculator`/`CriticalityScoreCalculator` (Insights),
`SavingsOpportunityService`, `IBenchmarkService`/`IMarketKnowledgeRetrieval`
(Market), `ISupplierNameLookup`) happens here, then gets handed to
`Raffa.Chat`'s own gate/planner/guards/reply pipeline:

1. **Gate** (`Raffa.Chat.Application.Gate.DomainGate.Classify`) — six
   labels, deterministic lexicons first (greeting, off-domain small talk,
   legal-advice, capability/how-to, then an unresolved named-supplier
   check), an `in_domain` default on ambiguity (no live classify call yet —
   see that type's own doc comment for the honest gap). `greeting` /
   `off_domain` / `legal` / `capability` / `needs_document` are all
   answered directly (`Reply.RedirectReplyBuilder`, real
   `CapabilityRouting`-resolved actions) — **zero retrieval, zero model
   call** — only `in_domain` reaches the planner (R-ASK-02).
2. **Planner** (`Application.Planning.IntentPlanner.Plan`) — ten fixed
   intents (structured fact, clause, market compare, renewal strategy,
   portfolio strategy, portfolio market position, savings, document status,
   quote route, navigate), reusing `AskRaffaQueryRouter`/
   `DeterministicQueryPlanner` for the legacy structured/clause split
   (`portfolio market position` — task E27/F01/US01/T01, NW-79, ADR-024 w19
   cl. 13 — always ranks the workspace portfolio, never Quote check, even
   when a supplier is already in scope). `AskCopilotService` composes one
   `Pack.PackItem` list per intent (tenant facts, clause chunks, market
   notes, calculator output — every item citable, tagged `tenant`/
   `market`/`raffa`/`calc`).
3. **Answer** (`Application.Answering.AnswerComposer`, persona prompt
   `Prompts/answer/v2.1.md`) calls `IAiGateway.AnswerAsync` with the pack +
   last N turns; `Fixtures.FixtureAiGateway.AnswerAsync` gives a
   deterministic v2 behaviour when a pack is supplied (cites the first N
   pack keys, copies their values verbatim — no chunk concatenation), so
   every test below runs without Foundry.
4. **Guards** — `Application.Guards.GroundingGuard` (every citationKey /
   inline `[n]` marker / actionKey must resolve), `Guards.NumericGuard`
   (every currency amount, percentage and date in the answer must equal a
   pack value — currency-aware — or appear verbatim in a cited snippet),
   `Guards.RegenerateOnce` (one retry naming the violation, then downgrade
   to an honest abstain naming the pack's own facts, metadata preserved for
   ADR-011 auditability) — never shown or persisted unguarded.
5. **Reply** (`Application.Reply.CopilotReply`) — the one shape every gate
   label / guard outcome produces: `{ kind, answerMarkdown, citations[],
   actions[], provenance: { sources, modelId, promptVersion, inputHash },
   followUps[] }`, `kind` one of `answer`/`abstain`/`redirect`/`refusal`
   (see the HTTP surface table above for the full citation/action field
   list) — never a `Document:` guid or a "Structured query" line in
   `answerMarkdown` (R-ASK-08).

`AskAsync` writes exactly one audit row per turn (`chat.answered`/
`chat.redirected`/`chat.refused`/`chat.abstained` — counts + a pack hash,
never text, ADR-011), with one further field on every row:
`abstainGuardIntervened=true|false` (AC-7) — `true` only when
`Answering.AnswerComposer`'s own guard pipeline actually rejected the first
attempt and forced `Guards.RegenerateOnce`'s retry-then-downgrade path, never
just because the reply happens to be `abstain` (an empty pack or a failed
gateway call both also produce `kind=abstain` but leave this field `false` —
the same field name/shape `RagAnswerService`'s older, evidence-only audit
entry already uses; see this file's "Ask Raffa — query router" section
above). The context pack's token budget is
`Pack.PackBudget`, optionally configured via `Chat:PackTokenBudget`
(`Chat__PackTokenBudget` env var form) and registered in `Program.cs`
*before* `AddChatModule`'s own always-usable default so a configured value
wins; absent configuration, `PackBudget.DefaultMaxTokens` applies.
Cross-tenant isolation over this new endpoint (parent story AC-9) is
proven the same way as `POST /api/chat/query`'s — see
`Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests`.

### Ask Raffa V2 — savings consultant (levers, negotiation council, playbook)

A savings or negotiation question ("quali leve posso usare per risparmiare 20k
sul rinnovo", "come posso salvare 40K sul prossimo quarterly basandomi sui
contratti attivi?", "how do I cut costs by 40k this quarter?") rides the same
`POST /api/conversations/{id}/messages` route and the same
gate → planner → pack → answer → guards pipeline, with four additions:

1. **Understanding the question.** `DomainGate` blanks money shorthand and
   currency codes (`40K`, `€ 20k`, `EUR 20000`) before extracting a supplier
   name, so the `K` of `40K` is never a supplier; a how-to phrasing with a
   money or savings signal stays in domain instead of the capability tour.
   `IntentPlanner` carries a `SavingsGoal` (amount, percentage, window —
   `SavingsGoalParser`, regex only, IT + EN) on every `IntentPlanResult`; the
   savings lexicon covers `risparmiare / salvare / tagliare / ridurre / costi
   / budget / leve / save / cut costs`. A quantified goal with no supplier in
   scope plans to the eleventh intent, `AskIntent.PortfolioSavingsTarget`. A
   bare follow-up ("non mi hai risposto", "e quindi?") is planned on the
   previous user question, so it inherits that turn's intent and goal.
2. **Evidence.** `AskCopilotService.Savings.cs` composes the `Savings` pack
   from `Raffa.Insights.Savings.SavingsLeverCalculator` (pure: market
   discount, above-band re-pricing, multi-year term, uplift cap, notice
   timing, payment terms, volume flexibility — every lever grounded and cited,
   amounts and percentages in the guard's own form), the target/coverage
   verdict (`calc:savings-target`), the ranked negotiation points, the
   supplier's market deals as numbers (`IMarketDealLookup.GetBySupplierAsync`
   — Postgres or the feed), hand-recorded savings opportunities, clause
   evidence retrieved from the contract's own pages for three fixed lever
   probes, and the playbook. The `PortfolioSavingsTarget` pack runs the lever
   calculator per contract and `PortfolioSavingsTargetCalculator` ranks the
   contracts that can be acted on inside the window (notice deadline inside
   it, or no fixed date) with a running cumulative against the target; a
   second currency is listed, never FX-summed. A scoped `RenewalStrategy` turn
   carries the lever addendum (evidence only). On a `Savings` turn every
   quantified lever is upserted as a savings opportunity keyed by the lever (`savings_opportunity.opportunity_key`,
   `SavingsOpportunityService.UpsertGeneratedAsync`), never touching a row a
   person owns — the Savings page fills from real use of Ask.
3. **The negotiation council** (`Raffa.Chat.Application.Council`). For
   `Savings`, `PortfolioSavingsTarget` and scoped `RenewalStrategy` turns over
   a pack of at least three items: the contract analyst and the market analyst
   run in parallel over disjoint slices of the pack (`IAiGateway.AnalyzeAsync`,
   strict JSON, `council-v1`), then the lever strategist reads both sets of
   findings, the calculators' items and the playbook and returns ranked plays
   plus a verdict on the goal. Plays are validated locally — citation keys must
   be pack keys, `NumericGuard` runs over each play's text, cited values are
   copied from the pack — and inserted as `calc:council:play[n]` items right
   after the target verdict, so the answer role narrates them under the same
   grounding and numeric guards as everything else. A failed agent degrades
   the council, never the turn. `Chat:Council` (`Enabled`, `MinPackItems`,
   `MaxItemsPerAgent`, `MaxPlays`) is the kill switch; the `analyst` role runs
   on `AiGateway:Models:Analyst` when set, else on the `answer` deployment.
4. **Persona `answer-v2.2`** (`Prompts/answer/v2.2.md`, drift-tested against
   `AnswerPromptV2.SystemPrompt`): a senior negotiation consultant; a savings
   question is answered with the verdict on the goal first, then Diagnosi →
   Leve in ordine di valore → Piano e timing → Cosa chiedere al fornitore →
   Rischi e cosa manca; a follow-up advances instead of restating; only bold
   and lists, which is all the web renderer supports. `NegotiationPlaybook`
   (`raffa:playbook:*`, digit-free by test) supplies tactics and wording,
   never numbers.

Golden cases `seeded-savings-leve-20k-salesforce-it`,
`seeded-savings-levers-20k-salesforce-en`,
`seeded-portfolio_savings_target-40k-quarterly-it` and
`seeded-portfolio_savings_target-cut-costs-en` pin the two motivating
questions; `Raffa.Api.Tests.AskSavingsConsultantTests` runs them end to end
over HTTP with the fixture gateway.

### Ask Raffa V2 — the notice pack (tasks E30/F01/US01/T01 + E30/F02/US01/T01, NW-91/NW-92/NW-94, ADR-024 w19 cl. 22)

A notice/preavviso/disdetta/cancellation-deadline question rides the identical
`POST /api/conversations/{id}/messages` route above — no new endpoint — but is
detected and answered entirely server-side, before any pack ever reaches
`AnswerComposer`/the AI gateway. `AskCopilotService.NoticeQuestionPattern`
(`notice|preavviso|disdetta(\s+period)?|cancellation\s+deadline`, English +
Italian) is checked inside the planner's existing `AskIntent.StructuredFact`
branch — deliberately **not** an eleventh `AskIntent` (this file's own doc
comment: `IntentPlanner` reuses the one intent for both rather than adding
one, so "The V2 engine"'s fixed intents above are unchanged — the eleventh,
`PortfolioSavingsTarget`, is the savings consultant's own, see the section
above).

**The pack (feature-01, NW-91/NW-92).** `BuildNoticePackAsync` composes, in
order: (1) the scoped fact itself — `endDate`/`cancellationDeadline`/
`renewalTermMonths` read straight off `Contract360Renewal`, every date a
`PackValueKind.Date` — with one of three honest snippets keyed on
`autoRenewal`: `false` states "no notice window applies, the contract ends on
`endDate`" (never a fabricated deadline); a known deadline states the date
plus, only when a `renewalTermMonths` is on file, "if missed, renews for N
month(s)"; neither known states the honest gap (Appendix C rule 10); (2)
`StrategyPackBuilder`'s own "when you must move" explanation (the identical
narration `BuildRenewalStrategyPackAsync` cites below), whose
`daysUntilNotice` pack value is `WhenYouMustMove.DaysLeft` — signed, never
floored to zero, so a passed deadline reads "N days ago"; (3) an optional
matching-clause evidence item, the first extracted clause whose type or text
names notice/cancellation/termination/auto-renewal
(`BuildMatchingClauseItem`), via the same `ResolveTenantClauseLinks` tier-1
resolution `BuildClausePackAsync` uses. **"N days" is never `EndDate −
CancellationDeadline`** (the task's own forbidden shortcut) — the day count
is always a calculator output, and RAG (epic-28's contract-scoped
`SearchByContractAsync`) is fallback only, never called from this path at all
(the InMemory EF provider cannot translate `CosineDistance`, the same
constraint the Q3 pack below documents).

**The fallbacks (feature-02, NW-94) short-circuit before feature-01's own
pack wrapper is ever reached** — `BuildInDomainReplyAsync` checks
`NoticeQuestionPattern` directly, ahead of the `packItems` switch, and
decides one of five server-owned outcomes, never falling through to
`AnswerComposer`: (1) a known deadline **and** a spanned clause → `answer`,
citing the fact and the deep-linked clause (page+documentId real, NW-83 — so
the client's two-CTA card, "Ask Raffa" in `web/README.md`, renders itself
from the citation alone); (2) a known deadline, no matching span → `answer`,
citing the fact alone (never a fabricated page); (3) no deadline, a clause
names one in its own words → `answer`, quoting the clause text verbatim as
the whole answer; (4) neither → `abstain` naming this contract's own
supplier, with a 360 **Review** recovery action
(`CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)`) — never the
generic ask-hint recovery, and never "which supplier" even though nothing
grounded; (5) no contract in scope at all (no conversation scope, no
resolvable named supplier) → `abstain` with a **Portfolio** recovery action
(`/contracts`), never a guessed contract. A multi-contract disambiguation
item (NW-80, "never silently merge") is prepended to every one of the four
answering/abstaining cases' own citations and answer text, never folded into
case 4's abstain. Zero gateway calls on any of the five paths — proved
directly (`RecordingAiGateway.Calls` empty) by
`Raffa.Api.Tests.NoticeFallbackEndpointTests`, the host-level test for all
five cases (a real clause seeded via `InMemoryAskEngineFactory
.SeedClauseAsync`, this task's own addition to the shared InMemory fixture —
no Postgres needed for a "matching clause" scenario, since
`Contract360QueryService.GetByIdAsync`'s `Clauses` read is a plain EF query).

## Ask Raffa — capability catalog

Task E13/F08/US01/T01 (story us-01-capability-catalog, ADR-024 "Capability
catalog (R-SYS)") adds `Raffa.Chat.Application.Capabilities`: a static,
versioned (`CapabilityCatalog.Version`, `"capabilities-v2.0"`) catalog of
the ten V2 capabilities (`ask`, `documents`, `documents-attention`,
`documents-review`, `portfolio`, `contract-360`, `renewals`, `savings`,
`quote-check`, `workspace-members` — R-SYS-01, `raffa-v2/ia-v2.md`'s own
route map), each a `Capability` record (key/title/route pattern/
description/example questions/role gate/availability/how-to steps).
`CapabilityRouting` (registered `AddScoped` by `AddChatModule` — the same
"stateless router, still an injected instance" convention
`AskRaffaQueryRouter` above already uses) turns a planner intent
(`CapabilityIntent` — benchmark, unknown supplier, deadline, savings,
how-to, capability list) plus a `RoutingContext` (validated-contract count,
caller role, known contract/quote/document id) into `CopilotAction`s built
only from catalog patterns and known object ids (R-SYS-02) — and, per
R-SYS-04, replaces any action whose target capability is
`needsValidatedContract` with the Documents upload action and the
prototype's own empty-state copy (`CapabilityRouting
.ValidatedContractsEmptyStateCopy`, `markup.html` "The portfolio lights up
from validated contracts. Upload one to start.") when the caller has zero
validated contracts. `FeatureCitation.For(capability)` builds the R-SYS-03
feature-card shape (`corpus: raffa`); `CapabilityCatalog.SuggestionsFor`
reproduces `app.jsx`'s per-screen `chipsFor`/`c360Chips` suggestion chips.

`Raffa.Api.CapabilitiesEndpointExtensions` maps `GET /api/capabilities`
(wave w16 NW-31, task E18/F03/US01/T01: the catalog is served whole; each
row carries its own `roleGate` as a presentation label, never as
authorization). Task E13/F06/US01/T01 (ask-engine) is this endpoint's first-mapped
caller in `Program.cs`, the same "endpoint exists, host wiring is a later
task's job" shape already used above for `AddChatModule`'s
`chatConnectionString` overload. Unlike every other endpoint in this file,
it takes no tenant header — the catalog is static, tenant-agnostic
metadata, not a per-tenant read.

## Renewal Intelligence — deterministic renewal engine

`Raffa.Renewals.Application.RenewalEngine` (task E03/F01/US01/T01,
us-01-deterministic-dates) is product spec §9.1's "calculate renewal date,
calculate cancellation deadline, calculate days remaining" made concrete:
pure, synchronous arithmetic over a `ContractRenewalTerms` snapshot — no
database, no HTTP call, no LLM call (Appendix C rule 6) — returning a
`RenewalCalculationResult` with a three-way `RenewalCalculationStatus`:

- `Determined` — `RenewalDate` equals `EndDate` when `AutoRenewal` is true
  (the same convention `PortfolioListItem.RenewalDate` /
  `Contract360Header.RenewalDate` already use, reproduced here on purpose).
  `CancellationDeadline` additionally needs `CancellationNoticeDays`
  (`EndDate` minus that many days) and can stay null even inside a
  `Determined` result when that one input is missing or negative — a
  renewal date and its cancellation deadline are independently
  determinable.
- `NoRenewal` — `AutoRenewal` is false: a known fact, not a data gap, so it
  is not folded into `CannotDetermine`.
- `CannotDetermine` — `EndDate` itself is unknown: nothing can be computed
  without fabricating it (Appendix C rule 10; parent story AC-3).

`DaysUntilRenewal`/`DaysUntilCancellationDeadline` are signed, unclamped
day counts relative to `IClock.UtcNow` — a negative value honestly means
the date already passed, rather than being hidden behind a floor of zero.
`RenewalEngine.CalculateMany` is the batch form for spec §9.1's "daily
scheduler for each active contract" shape; deciding which contracts are
"active" (in scope to call it with) is the caller's job, not the engine's.

`ContractRenewalTerms` deliberately does not reference
`Raffa.Documents.Contracts.Domain.Contract` — ADR-002 forbids
`Raffa.Renewals` from referencing `Raffa.Documents.Contracts` at all
(same reason `Raffa.Chat.Application.ContractFact` is its own small DTO,
not the real `Contract` entity). Two honest gaps follow, both deliberately
out of this task's file scope:

1. No host endpoint or worker job calls `RenewalEngine` yet.
   `AddRenewalsModule` exists (`Infrastructure/ServiceCollectionExtensions.cs`)
   so the remaining tasks that depend on `renewal-engine` in the wave-spec DAG
   (priority score, the cancellation-alerts threshold scheduler) can resolve
   it from a container, but `Raffa.Api`/`Raffa.Worker`'s `Program.cs` do
   not call it yet — the same "wiring lands with the first real caller"
   sequencing `AddChatModule` followed before `Raffa.Chat` had one (see
   that section above).
2. `Contract` has no persisted `CancellationNoticeDays` column — its "dates"
not the real `Contract` entity). One of the two gaps this section used to
describe is now closed (see "Renewal threshold scheduler" below); the other
remains, deliberately out of that task's file scope too:

1. `Contract` has no persisted `CancellationNoticeDays` column — its "dates"
   extraction stage (`StagedExtractionService.ApplyDatesFact`) still writes
   a raw `cancellationDeadline` date directly from extraction instead of a
   notice-period day count (product spec §7.3's own extraction-evidence
   example names `cancellation_notice_days`, not a computed date). Mapping
   a real `Contract` row onto `ContractRenewalTerms` — and giving
   extraction a real `CancellationNoticeDays` field to populate — is
   follow-up work in `Raffa.Documents.Contracts`, a different module and
   a different task's file scope.
not the real `Contract` entity).

`Raffa.Renewals.Application.RenewalPipelineBuilder` (task E03/F03/US01/T01,
us-01-renewal-dashboard-api) is `RenewalEngine`'s first real caller and backs
`GET /api/renewals` (see the HTTP surface table above): it turns a batch of
`RenewalDashboardCandidate` (another small DTO, the same dependency-direction
shape as `ContractRenewalTerms`) into a pipeline row plus a facts/
recommendations insight card (spec §9.3), ordered most-urgent-first by days
until the relevant date. `Raffa.Api.RenewalsEndpointExtensions` is the
composition root that maps a real, tenant-scoped `PortfolioListItem`
(Documents/Contracts) onto `RenewalDashboardCandidate` — the one mapping
neither module may do itself, same pattern `ChatEndpointExtensions` already
uses for `EmbeddingSearchResult` → `AiEvidenceSnippet`. `AddRenewalsModule`
is now called by `Raffa.Api`'s `Program.cs` — the same "wiring lands with
the first real caller" sequencing `AddChatModule` followed before
`Raffa.Chat` had one. `Raffa.Worker`'s `Program.cs` still does not call
it — no worker job (the renewal-opportunity generation / cancellation-alerts
threshold scheduler wave-spec tasks) depends on `renewal-engine` yet.

One honest gap remains, deliberately out of this task's file scope:
`Contract` has no persisted `CancellationNoticeDays` column — its "dates"
extraction stage (`StagedExtractionService.ApplyDatesFact`) still writes a
raw `cancellationDeadline` date directly from extraction instead of a
notice-period day count (product spec §7.3's own extraction-evidence example
names `cancellation_notice_days`, not a computed date), so `RenewalEngine`
itself still cannot derive a cancellation deadline for any real contract.
`RenewalPipelineBuilder` works around this for the dashboard specifically by
carrying `Contract.CancellationDeadline` (the already-extracted raw fact)
straight through as its own field, independent of `RenewalEngine`'s
notice-day derivation — see `RenewalDashboardCandidate.CancellationDeadline`'s
own doc comment. Giving extraction a real `CancellationNoticeDays` field (so
`RenewalEngine.Calculate` itself can derive the deadline, the way it already
derives `RenewalDate`) is follow-up work in `Raffa.Documents.Contracts`, a
different module and a different task's file scope.

`Raffa.Renewals.Application.RenewalOpportunityGenerator` (task
E03/F01/US01/T02, us-01-deterministic-dates, the wave-spec's
`renewal-opportunity` artifact) is the next daily-scheduler step from spec
§9.1: "create/update renewal opportunity", built directly on top of
`RenewalEngine.Calculate`'s output. `Generate`/`GenerateMany` take the same
`ContractRenewalTerms` shape `RenewalEngine` does (constructor-injected, so
`AddRenewalsModule` resolves both from one container); the static
`FromCalculation` exposes the mapping rule alone for a caller that already
ran the engine itself. Three-way `RenewalOpportunityStatus` mirrors
`RenewalCalculationStatus` case-for-case (`NoRenewal`/`CannotDetermine` keep
the same names; `Determined` becomes `Open` — an opportunity Procurement has
something to act on) so a `CannotDetermine` calculation never turns into a
fabricated opportunity — it abstains the same way, per parent story AC-3.
Deliberately out of scope here, each a later task's own file: a priority
score/component breakdown (us-02-priority-score), a threshold-alert flag
(feature-02-cancellation-alerts), an owner/status/action
(feature-03-renewal-dashboard's renewal-action task, spec Appendix A `POST
/api/renewals/{id}/action`), and persistence — spec §9.1 says "create/update"
(upsert semantics) but no task has given `Raffa.Renewals` a `DbContext` yet,
so today `RenewalOpportunity` is an in-memory value, not a stored row.
## Renewal Intelligence — explainable, tunable priority score
score/component breakdown (us-02-priority-score) and a threshold-alert flag
(feature-02-cancellation-alerts) remain follow-up work. The other two gaps
this paragraph used to list here are now closed by task E03/F03/US01/T02
(renewal-action, feature-03-renewal-dashboard): an owner/status/action —
`POST /api/renewals/{id}/action`, spec Appendix A, see the HTTP surface
table above — and `Raffa.Renewals`'s first `DbContext`
(`RenewalsDbContext`), which backs that endpoint's
`Raffa.Renewals.Domain.RenewalAction` row. That `DbContext` does not,
though, give `RenewalOpportunity` itself a persisted identity: spec §9.1's
"create/update renewal opportunity" upsert semantics land on the separate
`RenewalAction` (owner/status/action) row, keyed by `ContractId` alone, not
on a stored "renewal" entity — see `RenewalAction`'s own doc comment.
`RenewalOpportunity` remains an in-memory value, not a stored row.
## Renewal Intelligence — explainable priority score

`Raffa.Renewals.Application.PriorityScoreCalculator` (task E03/F01/US02/T01,
us-02-priority-score) is product spec §9.2's formula made concrete: `"Priority
Score = Spend Weight + Time Urgency + Benchmark Opportunity + Price Increase
Risk + Contract Risk"`. Same determinism convention as `RenewalEngine` (pure,
synchronous, no database/HTTP/LLM call) — `Calculate` takes one
`RenewalCalculationResult` (so "days until renewal" is always
`RenewalEngine`'s own arithmetic, never a second copy of it) plus one
`RenewalPriorityInputs` (the raw spend/uplift/contract-risk/benchmark-position
facts `RenewalEngine` does not compute) and returns a `PriorityScoreResult`:
a `TotalScore` (0–100 under the spec-default weights) plus each of the five
components as its own named, explained `PriorityScoreComponent` — spec
§9.2's "Store both total score and component scores so the recommendation is
explainable and tunable" (AC-2), not a single opaque number.

A component whose raw input is unknown never fabricates a guess (Appendix C
rule 10): every component except benchmark opportunity defaults to the
minimum (0); benchmark opportunity defaults to the documented neutral
midpoint (`PriorityScoreCalculator.NeutralComponentScore`, 10 under the spec
default) specifically, because parent story AC-3 names that exact rule —
`"Benchmark-opportunity component reads the R3 benchmark only when available
(else neutral)"`. Today that is *always* the neutral case:
`Raffa.Benchmark.IBenchmarkService` now defines the normalized
`GetBenchmarkAsync` contract (task E04/F01/US01/T01), and
`Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter` is now registered
behind it via `AddBenchmarkModule` (task E04/F01/US02/T01, see "Benchmark
Service" below), but nothing wires
`RenewalPriorityInputs.BenchmarkMarketPositionPercent` to a real
`GetBenchmarkAsync` call yet — the same "caller supplies it however it likes
today, a real mapping lands later" gap `ContractRenewalTerms` already
documents for this module. Every tier boundary (spend, uplift %,
benchmark %) and the time-urgency tiers (aligned to spec §9.1's own
365/270/180/120/90/60/30-day windows) are fixed, product-spec-cited defaults
— task-02 deliberately did not re-derive that tiering (see next paragraph
for what it did make tunable).

**Task E03/F01/US02/T02 (priority-explainability)** closed both gaps the
paragraph above used to name. *Tunable*: each of the five components' own
*maximum* contribution is now
`Raffa.Renewals.Configuration.PriorityScoreWeightsOptions` (config section
`Renewals:PriorityWeights`, `SpendWeightMax`/`TimeUrgencyMax`/
`BenchmarkOpportunityMax`/`PriceIncreaseRiskMax`/`ContractRiskMax`, each
defaulting to 20 — the untouched spec default) — `PriorityScoreCalculator` rescales every tier's
fixed contribution proportionally (the tier's fraction of the spec-default
20, times the configured maximum), so the tiering itself is unchanged but
each term's weight in the sum is an operator decision, not a compile-time
literal. *Explainable, queryable*: `Raffa.Api.RenewalsEndpointExtensions`
now maps `GET /api/renewals/{contractId}/priority` (see the HTTP surface
table above) — `PriorityScoreCalculator`'s first real host caller, composing
`Contract360QueryService`'s tenant-scoped contract lookup (annual spend, end
date, auto-renewal, risk) the same way `GET /api/renewals` composes
`PortfolioQueryService`; `AnnualUpliftPercent`/`BenchmarkMarketPositionPercent`
stay honestly `null` for the same reason `GET /api/renewals`'s own insight
card does (neither has a real producer yet).

`AddRenewalsModule` registers `PriorityScoreWeightsOptions` the same
"bind lazily from `IConfiguration`, property initializers supply the spec
default" way as `ThresholdWindowOptions` (see below), then registers
`PriorityScoreCalculator` as before — its one constructor parameter is now
that options singleton, injected automatically.

### Renewal threshold scheduler

`Raffa.Renewals.Application.RenewalThresholdScheduler` (task
E03/F02/US01/T01, us-01-threshold-scheduler AC-1/AC-2) is product spec
§9.1's "daily scheduler ... emit threshold events if applicable" made
concrete: it runs `RenewalEngine.CalculateMany` over a tenant's
`ContractRenewalTerms`, then checks each result's `DaysUntilRenewal`/
`DaysUntilCancellationDeadline` against `Raffa.Renewals.Configuration
.ThresholdWindowOptions.DaysBeforeDeadline` (config section
`Renewals:Thresholds`, default 365/270/180/120/90/60/30 days — AC-1,
"configurable"). An exact day-count match raises a `RenewalApproachingEvent`
(`RenewalMilestoneKind.RenewalDate` or `.CancellationDeadline` — a contract
can raise one, both, or neither on a given run) and writes it through
`IAuditWriter` as one `renewal.approaching` entry (spec Appendix B; same
"an audit entry is this codebase's actual event mechanism" convention as
`document.uploaded`/`contract.corrected` — no in-process mediator exists
yet, and picking one is council-owned, not this task's call) — durable and
queryable via `GET /api/audit` even before a real consumer exists.

`Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService` is this
module's first real host caller: `WorkerServiceCollectionExtensions
.AddWorkerHost` now calls `AddRenewalsModule` (closing gap 1 that used to
be listed above) and registers this `BackgroundService`, which ticks every
`Worker:RenewalThresholdScheduler:Interval` (default 24h) and, per tenant
batch, calls `RenewalThresholdScheduler.EvaluateThresholdsAsync` from a
fresh DI scope (it must be Scoped, not injected directly into the Singleton
hosted service — it depends on the Scoped `IAuditWriter`). Honest gap: its
`IActiveRenewalContractsSource` port has no real implementation yet — the
default `NoActiveRenewalContractsSource` always returns zero tenants.
Enumerating every tenant's active contracts needs a cross-tenant workspace
listing (`Raffa.Identity.Workspace`, not referenced by `Raffa.Worker`
today) plus a per-tenant RLS-scoped contract query
(`Raffa.Documents.Contracts`) — wiring a real adapter is follow-up
composition work, the same category of gap this section's remaining item
above describes. The timer loop itself is real and proven end to end
(`Raffa.Worker.Tests.RenewalThresholdSchedulerHostedServiceTests`); AC-3
("Scheduler recomputes when a contract/term is corrected") is parent story
task-02's scope ("Alert creation + re-compute on correction"), not this
task's.

**Task E03/F04/US01/T01 (r2-integration) fix:** `RenewalThresholdScheduler
.EvaluateThresholdsAsync` wrote its `renewal.approaching` audit entry
without ever opening an `ITenantContext` scope, so
`TenantRlsConnectionInterceptor` left `app.tenant_id` unset and the Audit
module's own `AddTenantRowLevelSecurity` `WITH CHECK` policy rejected the
insert outright — a real threshold crossing would throw instead of being
recorded. Neither `RenewalThresholdSchedulerTests` (a `RecordingAuditWriter`,
no database) nor `RenewalThresholdSchedulerHostedServiceTests` (a
syntactically-valid-but-never-dialled connection string, by design) ever
exercised a real RLS-enforced connection on this path, so this went
undetected until r2-integration's own real-Postgres proof
(`Raffa.IntegrationTests.R2EndToEndTests`) surfaced it. The method now
opens its own scope before writing, the same convention
`RenewalActionService.SetActionAsync` already follows.

### Renewal alerts

`Raffa.Renewals.Application.RenewalAlertService` (task E03/F02/US01/T02,
the wave-spec's `renewal-alerts` artifact; parent story
us-01-threshold-scheduler AC-2/AC-3) closes the gap the section above
named: a persisted, de-duplicated `Raffa.Renewals.Domain.RenewalAlert` row
per raised `renewal.approaching` event, plus recompute-on-correction.

- **Creation (AC-2)** — `CreateFromEventsAsync` de-duplicates every raised
  `RenewalApproachingEvent` against this contract's own currently-`Active`
  alerts (keyed by tenant/contract/milestone/thresholdDays — a filtered
  unique index on that tuple, `WHERE status = 'Active'`, is the
  database-level backstop) and persists exactly one new row per genuinely
  new match, each writing one `renewal.alert_created` `IAuditWriter` entry.
  `Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService` calls
  this immediately after every scheduler tick's own
  `EvaluateThresholdsAsync`, in the same DI scope.
- **Recompute (AC-3)** — `RecomputeForContractAsync` re-derives a contract's
  renewal date/cancellation deadline via `RenewalEngine.Calculate` and
  resolves (`renewal.alert_resolved`, status flips to `Resolved` — never
  deleted, Appendix C rule 5) any `Active` alert whose own `MilestoneDate`
  no longer matches, then re-runs `RenewalThresholdScheduler
  .EvaluateThresholdsAsync` for that one contract against the corrected
  terms so a correction landing exactly on a configured threshold today
  raises the same `renewal.approaching` event (and alert) a scheduled tick
  would raise tomorrow. `Raffa.Api.RenewalAlertRecomputeService` — the
  composition-root orchestrator ADR-002 requires for any code that touches
  both `Raffa.Documents.Contracts` and `Raffa.Renewals` (mirrors
  `NegotiationOutcomePropagationService`) — calls this from `PATCH
  /api/contracts/{id}` (see `ContractsEndpointExtensions`), but only when
  the correction actually touched `endDate` or `autoRenewal` (the only two
  `Contract` fields `ContractRenewalTerms` consumes today — a
  `cancellationDeadline`-only correction is a no-op for this purpose, since
  `RenewalEngine` derives its own deadline from `CancellationNoticeDays`,
  always `null` today, never from that raw field).

This module's second table, `renewal_alert`, and its RLS policy land in one
migration (`AddRenewalAlert` — the same "table doesn't pre-exist, so RLS is
not a retrofit" convention `Raffa.Quotes`'s own `AddSkuProductMapping`/
`AddNegotiationOutcome` migrations already established). No HTTP read
endpoint exists for alerts yet (no AC/task names one) — proven instead via
`Raffa.Renewals.Tests.RenewalAlertServiceTests`/
`RenewalAlertRlsCrossTenantIsolationTests` and
`Raffa.IntegrationTests.R2EndToEndTests`' own
`Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction`.

## R1 demo smoke test

The automated proof of task E02/F06/US01/T01 (r1-integration) is
`dotnet test` — `Raffa.IntegrationTests.R1EndToEndTests` (AC-1/AC-2/AC-4:
upload → parse/OCR → classify → extract → portfolio → 360 → Ask Raffa
with citations → correction, plus a scanned/image fixture through the
`ocr` gateway role) and `R1CrossTenantIsolationTests` (AC-3, across the
whole path). To manually smoke-test the same path against a running
`dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -H 'X-User-Id: smoke-test@acme.example' -d '{"name":"Smoke Test Co"}' | jq -r .id)

# 201 only for an admitted contract-related document: a non-contract PDF
# gets 422 (reason not_a_contract | no_readable_text), an unsupported or
# mismatched format 415, a file over Documents:MaxFileBytes 413 -- see
# "Documents -- admission gate" above.
DOC=$(curl -s -X POST "$API/api/documents" -H "X-Tenant-Id: $TENANT" \
  -F "file=@contract.pdf;type=application/pdf" | jq -r .id)

# processingStatus/contractId reflect DocumentProcessingPipeline's own run
# (classify -> hybrid parse -> staged extraction -> RAG indexing) -- POST
# /api/documents runs it synchronously before responding.
curl -s "$API/api/documents/$DOC" -H "X-Tenant-Id: $TENANT" | jq .
CONTRACT=$(curl -s "$API/api/documents/$DOC" -H "X-Tenant-Id: $TENANT" | jq -r .contractId)

curl -s "$API/api/contracts" -H "X-Tenant-Id: $TENANT" | jq .
curl -s "$API/api/contracts/$CONTRACT" -H "X-Tenant-Id: $TENANT" | jq .

curl -s -X POST "$API/api/chat/query" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"question":"What does this contract cover?"}' | jq .
```

Honest caveat: on an environment whose root still has `ai_gateway_wired =
false` (see `infra/README.md`) `IAiGateway` binds to `FixtureAiGateway`,
whose `extract` role is the regex `FixtureContractFactExtractor` — a real
upload there proves the *pipeline wiring* end-to-end (every stage runs,
links, and is queryable), not model-grade extraction; `R1EndToEndTests`
proves the persistence/HTTP contract against a scripted gateway that returns
real, schema-shaped facts. With the flag `true` the same path runs on the
Foundry deployments.

## R2 demo smoke test

The automated proof of task E03/F04/US01/T01 (r2-integration) is
`dotnet test` — `Raffa.IntegrationTests.R2EndToEndTests` (AC-1/AC-2: every
active contract gets a deterministic renewal date/cancellation deadline
where data exists, an explainable component-scored priority via `GET
/api/renewals/{id}/priority`, a `renewal.approaching` threshold event that
is durably recorded — never fabricated for a contract with an unknown end
date — and a `POST /api/renewals/{id}/action` upsert) and
`R2CrossTenantIsolationTests` (AC-3, across the whole `GET
/api/renewals` / `GET /api/renewals/{id}/priority` / `POST
/api/renewals/{id}/action` surface). Contracts are seeded directly against
the real, RLS-enforced `DocumentsContractsDbContext` (see
`R2IntegrationFixture.SeedContractAsync`) rather than through the R1 upload
path — R2's own leaf artifacts all take already-validated contract data as
an input, never produce it.

**Updated by task E03/F02/US01/T02 (renewal-alerts):** this task's own
wave-spec `depends_on` named `renewal-alerts`, which had not landed any code
as of this task's original run — only the `renewal.approaching` threshold
event (task E03/F02/US01/T01) existed then. `R2EndToEndTests` proved that
literal event and no more; a persisted, de-duplicated `RenewalAlert` row
with recompute-on-correction was still open. That task has since landed:
see "Renewal alerts" above, and `R2EndToEndTests`' own
`Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction`
for the added proof (alert creation composed with the scheduler tick, then
`PATCH /api/contracts/{id}` resolving/re-raising alerts through the real
`Raffa.Api.RenewalAlertRecomputeService` wiring).

## Savings Intelligence — deterministic price normalization

`Raffa.Savings.Application.PriceNormalizationCalculator` (task E04/F02/US01/T01,
us-01-price-normalization, the wave-spec's `savings-normalization` artifact) is product spec
§4.3/§10's "Normalize current unit price and compare with benchmark P25/P50/P75... Calculate
current percentile, recommended target and savings range" made concrete: pure, synchronous
arithmetic over a `PriceComparisonRequest` (an already-fetched `Raffa.Benchmark.Contracts
.BenchmarkResult` plus the current total cost) — no database, no HTTP call, no LLM call (Appendix C
rule 6) — returning a `PriceComparisonResult` with a four-way `PriceComparisonStatus`:

- `Compared` — the benchmark had a well-ordered distribution and currencies matched: normalized
  unit price, percentile rank (0-100, linearly interpolated between P25/P50/P75 and clamped at the
  ends — never extrapolated beyond the last known marker), a recommended target range
  (`[min(P25, price), min(P50, price)]` — never above the current price) and a per-unit + total
  savings range are all populated.
- `InvalidQuantity` — `BenchmarkQuery.Quantity` is zero or negative: nothing is computed, not even
  the normalized unit price (division would be meaningless).
- `CurrencyMismatch` — `BenchmarkQuery.Currency` does not equal `BenchmarkResult.Currency`; this
  codebase has no exchange-rate service, so converting would fabricate a rate it does not actually
  know (Appendix C rule 10) — the normalized unit price is still reported in its own currency, but
  no comparison is attempted.
- `InsufficientBenchmarkData` — either `BenchmarkResult.Distribution` is null (ADR-001's explicit
  "insufficient market data" outcome) or it is present but not well-ordered (`P25 <= P50 <= P75`
  does not hold, a data-quality problem this calculator refuses to silently paper over rather than
  fail on).

`PriceComparisonRequest` deliberately reuses `Raffa.Benchmark.Contracts.BenchmarkQuery` (rather
than re-declaring supplier/quantity/term/currency on a second type) for the exact query a caller
already built to fetch the `BenchmarkResult` in the first place — so currency/quantity are
guaranteed to be the values the benchmark lookup itself used, and term alignment (comparing a
12-month contract against 12-month comparables, not 36-month ones) stays the Benchmark Service's
own matching responsibility (product spec §10.4), never re-derived here. `PriceComparisonResult`
echoes the original `BenchmarkResult` unchanged on every outcome, so `Confidence`/`Source`/
`ComparisonDimensions`/`SampleSize`/`UpdatedAt` are always reachable from one result without this
task re-declaring or guessing at task-02's (confidence + provenance propagation) own output shape.

Same "benchmark data only ever arrives as an already-known value, never a live call" convention
`Raffa.Renewals.Application.PriorityScoreCalculator` already established: this calculator's
public API structurally cannot accept a live `Raffa.Benchmark.IBenchmarkService`, so Appendix C
rule 3 ("never call a benchmark provider directly from renewal, savings or quote business logic")
can never become an accidental provider call from this module — proven by
`Raffa.Savings.Tests.PriceNormalizationCalculatorTests.Calculator_never_depends_on_the_live_Benchmark_Service_interface`.

`Raffa.Savings.Application.SavingsProvenanceClassifier` (task E04/F02/US01/T02, us-01-price-
normalization task-02, the wave-spec's `savings-provenance` artifact) closes AC-3 ("Show confidence
+ provenance on the comparison"): `PriceComparisonResult.Provenance` is a computed property — not a
constructor argument, so task-01's own tested shape is unchanged — that derives a
`Raffa.Savings.Application.SavingsProvenance` view from `PriceComparisonResult.Benchmark` on every
access. It carries a `Raffa.Savings.Domain.SavingsConfidenceLevel` (`Low`/`Medium`/`High`, spec's
own UI vocabulary for "Benchmark confidence") alongside the raw `[0, 1]` confidence score,
source/comparison-dimensions/sample-size/updated-at (all echoed unchanged from `BenchmarkResult`),
and a deterministic one-line `Summary`. `Classify`'s thresholds (`HighConfidenceThreshold` = 0.7,
`MediumConfidenceThreshold` = 0.4) are this classifier's own documented, adjustable heuristic — not
a council-locked figure — chosen so `FixtureBenchmarkAdapter`'s own catalog already spans all three
tiers (full-sample matches are High; Zoom's thinner 30-of-50 sample is Medium; Snowflake's 18-of-50
sample, and any supplier+product-only weak match, are Low). `Provenance` is available regardless of
`PriceComparisonResult.Status` — `BenchmarkResult`'s own provenance fields are always populated, so
a caller can show confidence/provenance even for an insufficient-data or currency-mismatch result,
never just the `Compared` case.

Deliberately out of this task's file scope, each a later task's own: a persisted, trackable
`SavingsOpportunity` with status/owner/realized outcome (us-02-savings-opportunity), and any
host/worker wiring that calls `PriceNormalizationCalculator` against real contracts — the same
"wiring lands with the first real caller" sequencing this README's other modules already follow
(see "Renewal Intelligence" above). `AddSavingsModule`/DI registration does not exist yet for the
same reason: nothing calls this calculator from a host yet.

**Incidental fix, task E04/F02/US01/T02:** `backend/tests/Raffa.Benchmark.Tests/ServiceCollectionExtensionsTests.cs`
failed to compile (a prior merge had spliced one test method's closing brace together with a second,
differently-named test's signature line, discarding that second method's body) — fixed to restore
`dotnet build Raffa.slnx`, since a broken build blocks every task, not just this one. The
recovered test body is verified against this module's own current source, not guessed; the
unrecoverable second test is not reinvented. That repair surfaced a separate, still-open
`Raffa.Benchmark` wiring gap, left exactly as found (not this task's module or file scope):
`AddBenchmarkModule` registers `FixtureBenchmarkAdapter` directly as `IBenchmarkService` via
`TryAddSingleton`, but the preceding `TryAddSingleton<IBenchmarkService, BenchmarkAdapterRegistry>`
call already claims that slot (first registration wins), and `FixtureBenchmarkAdapter` does not
implement `IBenchmarkProviderAdapter`, so `BenchmarkAdapterRegistry` can never reach it either — a
container built from `AddBenchmarkModule()` resolves `IBenchmarkService` to an always-adapter-less
`BenchmarkAdapterRegistry` today, not `FixtureBenchmarkAdapter`, contradicting
`Resolved_service_fails_honestly_when_no_adapter_is_registered_yet`'s own
`Assert.IsType<FixtureBenchmarkAdapter>` (that test now fails, honestly, instead of the file
silently not compiling). Fixing the wiring itself is us-02-fixture-adapter's/the adapter-registry
task's own module to redesign, not a Savings-module task's file scope.
Deliberately out of this task's file scope, each a later task's own: confidence + provenance
propagation into whatever surface displays this result (us-01-price-normalization task-02), and any
host/worker wiring that calls `PriceNormalizationCalculator` against real contracts — the same
"wiring lands with the first real caller" sequencing this README's other modules already follow (see
"Renewal Intelligence" above). This calculator itself still has no DI registration for the same
reason: nothing calls it from a host yet — see the next section for what `AddSavingsModule` *does*
now register.

## Savings Intelligence — trackable SavingsOpportunity

`Raffa.Savings.Domain.SavingsOpportunity` (task E04/F02/US02/T01, savings-opportunity, the
wave-spec's `savings-opportunity` artifact; parent story us-02-savings-opportunity AC-2) is this
module's first persisted entity — product spec §6's core data model row "SavingsOpportunity |
supplier, contract/quote, type, current_spend, estimated savings range, confidence, status, owner"
(module-map.md: "Savings | SavingsOpportunity, RealizedSavings | `/api/savings`") made concrete:
`SupplierId`/`ContractId` are cross-module references by id only, deliberately no foreign key (same
treatment `Raffa.Renewals.Domain.RenewalAction.ContractId` already gives its own cross-module
reference — ADR-002 forbids this module from referencing `Raffa.Suppliers.Products` or
`Raffa.Documents.Contracts` at all); `Type` is free text (no ADR/spec fixes a vocabulary);
`CurrentSpend`/`EstimatedSavingsLow`/`EstimatedSavingsHigh` carry an explicit `Currency` (this
codebase has no currency-conversion service anywhere); `Confidence` echoes
`Raffa.Benchmark.Contracts.BenchmarkResult.Confidence` (spec §4.3 "Show benchmark confidence and
provenance"). `Status` (`Identified` / `InProgress` / `Realized`) is read directly off spec §4.3's
own three dashboard KPI buckets ("savings identified" / "savings in progress" / "savings realized")
— see `SavingsOpportunityStatus`'s own doc comment for why no fourth "rejected/dismissed" state
exists yet.

`Raffa.Savings.Application.SavingsOpportunityService` backs `GET /api/savings` (list, newest
identified first) and `PATCH /api/savings/{id}` (a genuine partial update of `owner`/`status`, either
or both — see the HTTP surface table above), tenant-scoped via `ITenantContext.BeginScope` the same
way `Raffa.Renewals.Application.RenewalActionService` is, and writes one `IAuditWriter` entry per
successful mutation (`savings_opportunity.identified` / `savings_opportunity.updated` — spec §14.1).
Also exposes `CreateAsync` ("identify"), proven by `Raffa.Savings.Tests
.SavingsOpportunityServiceTests` but not yet wired to an HTTP route — this task's own AC-1 names only
`GET`/`PATCH`, and nothing in this codebase yet maps a real `PriceComparisonResult` against a real
contract into a `CreateSavingsOpportunityRequest`; that composition (in `Raffa.Api`, "the one
project allowed to reference every module") is a follow-up, the same "wiring lands with the first
real caller" gap the previous section names for `PriceNormalizationCalculator` itself.

**Task E04/F04/US01/T01 (r3-integration)** proves the whole chain this gap still leaves manual —
`IBenchmarkService.GetBenchmarkAsync` -> `PriceNormalizationCalculator.Compare` ->
`SavingsOpportunityService.CreateAsync` -> `PATCH .../{id}` (owner, then a realized value) ->
`GET /api/savings`/`GET /api/savings/kpis` — end to end against the real host and a real, migrated,
RLS-enforced database: `Raffa.IntegrationTests.R3EndToEndTests` resolves `IBenchmarkService`/
`SavingsOpportunityService` directly from the host's own container (the same "no dedicated route
exists yet, exercise the service the host resolves" convention `R2EndToEndTests` already established
for `RenewalActionService`), since no real caller maps a contract's line items into a
`BenchmarkQuery` yet either (no supplier-name/geography field exists on `Contract` today). See "R3
demo smoke test" below.

`AddSavingsModule` (task E04/F02/US02/T01) gives this module its first `DbContext`
(`SavingsDbContext`) and is now called by `Raffa.Api`'s `Program.cs` — RLS is wired the same
`AddTenantRowLevelSecurity` migration + `TenantRlsConnectionInterceptor` mechanism every other
tenant-scoped module uses (ADR-009), proven by `Raffa.Savings.Tests
.SavingsOpportunityRlsMigrationCheckTests`/`SavingsOpportunityRlsCrossTenantIsolationTests`.
`Raffa.Worker` is not wired to this module yet (no worker job creates opportunities today) — the
same "wiring lands with the first real caller" gap, not attempted by this task.

**Task E04/F02/US02/T02 (realized-savings)** closes the gap the paragraph above used to name:
`Raffa.Savings.Domain.RealizedSavings` (module-map.md's own second named entity for this module,
"Record realized value + audit event", parent story AC-3) is this module's second tenant-scoped
table — one append-only row per captured realized value (never a destructive overwrite, the same
"never destructively overwrite" spirit `Raffa.Documents.Contracts.Domain.ContractVersion`/
`CorrectionHistory` already apply to their own history), in the opportunity's own `Currency` (no
per-row currency — this codebase has no currency-conversion service anywhere). `PATCH
/api/savings/{id}`'s `realizedAmount` field (see the HTTP surface table above) is the only writer,
via `SavingsOpportunityService.UpdateAsync`: a non-negative `realizedAmount` always finalizes
`Status` as `Realized` (either because the caller's own explicit `status` already said so, or
automatically when `status` was omitted — the two facts are not independent, see
`SavingsOpportunityStatus.Realized`'s own doc comment) and inserts one new `RealizedSavings` row,
still exactly one `IAuditWriter` entry per call (`savings_opportunity.realized` takes the place of
`savings_opportunity.updated` for that call, never both). RLS is wired the same
`AddRealizedSavingsRowLevelSecurity` migration + `TenantRlsConnectionInterceptor` mechanism as
every other tenant-scoped table (ADR-009) — proven by `Raffa.Savings.Tests
.SavingsOpportunityRlsMigrationCheckTests` (dynamic per-table discovery, no test change needed) and
the new `RealizedSavingsRlsCrossTenantIsolationTests`. Honest gap, deliberately out of this task's
own file scope: `GET /api/savings`'s list response does not surface any opportunity's realized-value
history — only the `PATCH` response that just recorded one does (see
`SavingsOpportunityResult.RealizedAmount`'s own doc comment) — a rolled-up read (e.g. for the
dashboard's own "savings realized" KPI, spec §4.3) is a follow-up, the same "wiring lands with the
first real caller" gap this section's other paragraphs already document.

## Savings Intelligence — procurement homepage KPIs

Task E04/F03/US01/T01 (savings-kpis, the wave-spec's `savings-kpis` artifact; parent story
us-01-savings-kpis AC-1) adds `GET /api/savings/kpis` — see the HTTP surface table above for the
response shape. Two new pure calculators do the actual arithmetic, each unit-tested independently
of any database (same convention `Raffa.Renewals.Application.RenewalPipelineBuilder`/
`PriorityScoreCalculator` already establish):

- `Raffa.Savings.Application.SavingsKpiCalculator` groups every tenant-scoped
  `SavingsOpportunity` by `Status` then `Currency` for the "Savings Identified"/"Savings In
  Progress"/"Savings Realized" thirds (`SavingsKpiQueryService` is its thin EF-backed fetch half).
- `Raffa.Documents.Contracts.Application.PortfolioAnalysisCalculator` computes "Contracts
  Analyzed"/"Annual Spend Analyzed" from every tenant-scoped `Contract`, flagged by whether any
  linked `Document` reached `DocumentProcessingStatus.Completed` — a `Contract` row alone is not
  "analyzed" (`StagedExtractionService.EnsureContractAsync` creates one as a bootstrap shell before
  extraction even starts) — see that calculator's own doc comment.
  (`PortfolioQueryService.GetAnalysisSummaryAsync` is its fetch half.)

Every money value in the response is grouped by currency, never summed across currencies — the
same "no exchange-rate service anywhere in this codebase" reasoning
`Raffa.Savings.Domain.SavingsOpportunity.Currency`'s own doc comment already gives. "Upcoming
Renewals" adds no dependency on `Raffa.Renewals` at all: `Raffa.Api.SavingsKpiEndpointExtensions`
reuses the exact same auto-renewing-contract query `GET /api/renewals` already runs for its own
`totalCount`, so the homepage KPI and the renewal pipeline list can never silently disagree.

Honest gap, deliberately out of this task's own file scope: `savingsRealized` is computed from each
`SavingsOpportunity`'s own `EstimatedSavingsLow`/`EstimatedSavingsHigh` range, not the separate,
audit-tracked `RealizedSavings` entity — this task's wave-spec dependency is `savings-opportunity`
only (`RealizedSavings` is task E04/F02/US02/T02's own deliverable, scheduled the same wave-spec
phase, so it is not a dependency this task can assume has landed).

## Savings Intelligence — opportunity list confidence tier

Task E04/F03/US01/T02 (savings-list, the wave-spec's own artifact of that name; parent story
us-01-savings-kpis AC-2/AC-3) closes the one part of `GET /api/savings` (and, via the shared
`ToResponse` wire-shaping, `PATCH /api/savings/{id}`) that AC-3 ("Returns provenance + confidence,
never fabricated precision") still left open: tenant scoping (AC-2) and a raw `confidence` score
already existed from task E04/F02/US02/T01, but nothing paired that decimal with an honest,
interpretable signal. `SavingsOpportunityResult.ConfidenceLevel` — a computed property, not a
constructor argument, the same "cannot drift from its one source of truth" shape
`PriceComparisonResult.Provenance` already established — applies the existing
`Raffa.Savings.Application.SavingsProvenanceClassifier.Classify` (task E04/F02/US01/T02,
`savings-provenance`) to each opportunity's own `Confidence`, so both call sites now report the same
`Low`/`Medium`/`High` tier a live benchmark comparison would.

Deliberately does **not** attempt the fuller `Raffa.Savings.Application.SavingsProvenance` shape
(source, comparison dimensions, sample size, benchmark updated-at) on `SavingsOpportunity`: those
fields describe a specific `BenchmarkResult` comparison, and nothing in this codebase persists one
against a `SavingsOpportunity` row today — `CreateSavingsOpportunityRequest` only ever receives the
already-reduced `Confidence` score (see that request's own doc comment on why no host wires a real
caller yet). Fabricating a source/sample-size/updated-at this entity does not actually have on file
would be exactly the imprecision AC-3 forbids (Appendix C rule 10); a caller that needs the full
`SavingsProvenance` for a live comparison still reaches it via `PriceComparisonResult.Provenance` at
comparison time. Persisting real per-opportunity provenance is a follow-up for whichever future task
first wires `PriceNormalizationCalculator`'s output into `SavingsOpportunityService.CreateAsync` —
the same "wiring lands with the first real caller" gap this README's other Savings sections already
document.

## R3 demo smoke test

The automated proof of task E04/F04/US01/T01 (r3-integration) is `dotnet test` —
`Raffa.IntegrationTests.R3EndToEndTests` (AC-1: a "matched contract" benchmark comparison reports
current price + P25/P50/P75 + percentile/target/saving/confidence/provenance for a confident fixture
match, and honestly abstains — still with confidence/provenance, never a bare failure — when the
matched comparable is dimensionally strong but statistically too thin (`fixture-confidence`, task
E04/F01/US02/T02); AC-2: a `SavingsOpportunity` is identified from that comparison, owned via `PATCH
/api/savings/{id}`, listed with its confidence tier (`savings-list`), and marked realized
(`realized-savings`) — with `GET /api/savings/kpis` reflecting each move; AC-3: the only
`IBenchmarkProviderAdapter` registered anywhere in the composed host is `FixtureBenchmarkAdapter`) and
`R3CrossTenantIsolationTests` (the same AC-2 surface proven isolated across two tenants, the same
"drive the whole path across two tenants through the real host" value-add
`R1CrossTenantIsolationTests`/`R2CrossTenantIsolationTests` already established). Run just these:

```bash
cd backend
dotnet test Raffa.slnx --configuration Release --filter "FullyQualifiedName~R3"
```

To manually smoke-test the parts of this path that already have a public HTTP surface, against a
running `dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -H 'X-User-Id: smoke-test@acme.example' -d '{"name":"Smoke Test Co"}' | jq -r .id)

# A fresh tenant honestly starts at all-zero KPIs — no fabricated baseline.
curl -s "$API/api/savings/kpis" -H "X-Tenant-Id: $TENANT" | jq .
curl -s "$API/api/savings" -H "X-Tenant-Id: $TENANT" | jq .

# Once an opportunity id exists for this tenant (see honest caveat below), its lifecycle is fully
# curl-able: own it, then realize it, then watch the KPI bucket move.
OPPORTUNITY=<opportunity-id>
curl -s -X PATCH "$API/api/savings/$OPPORTUNITY" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"owner":"procurement@acme.example","status":"InProgress"}' | jq .
curl -s -X PATCH "$API/api/savings/$OPPORTUNITY" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"realizedAmount":20000}' | jq .
curl -s "$API/api/savings/kpis" -H "X-Tenant-Id: $TENANT" | jq .
```

Honest caveat: identifying a *new* `SavingsOpportunity` from a live benchmark comparison
(`IBenchmarkService.GetBenchmarkAsync` -> `PriceNormalizationCalculator.Compare` ->
`SavingsOpportunityService.CreateAsync`) has no public HTTP route yet — `CreateSavingsOpportunityRequest`'s
own doc comment names why, and this task deliberately did not invent a contract-to-`BenchmarkQuery`
mapping to close it (no supplier-name/geography field exists on a real `Contract` yet; fabricating one
would misrepresent data this codebase does not actually have, Appendix C rule 10). This smoke path
proves the *lifecycle* HTTP surface end to end (own -> list -> realize -> KPI rollup, all tenant-scoped
and RLS-enforced); `R3EndToEndTests` proves the benchmark-comparison half — and the identify step that
bridges the two — against the real host directly, the same "no dedicated route yet, exercise the
service the host resolves" convention `R2EndToEndTests` already established for `RenewalActionService`.

## Quote Check — quote upload + line-item extraction

`Raffa.Quotes` (task E05/F01/US01/T01, quote-extraction; parent story
us-01-quote-line-extraction) is the first Quotes-module task: `POST
/api/quotes` (see the HTTP surface table above) uploads a supplier quote
and runs schema-constrained line-item extraction synchronously before
responding — the same "read the bytes once, run the pipeline inline"
shape `POST /api/documents`/`DocumentProcessingPipeline` already
established for contracts (task E02/F06/US01/T01).

- `Raffa.Quotes.Domain.Quote`/`QuoteExtractionJob`/`QuoteLine` are this
  module's own entities — deliberately **not** a reference to
  `Raffa.Documents.Contracts.Domain.Document`/`Contract`: ADR-002 forbids
  `Raffa.Quotes` from referencing `Raffa.Documents.Contracts` at all
  (its allowed Raffa references are exactly `[SharedKernel, Benchmark]`
  — see "Dependency direction" below), and a quote is not a contract (spec
  §11's own Quote → Benchmark → Assessment → Negotiate → **Contract** flow
  treats "becomes a contract" as a later, explicit step).
- `Raffa.Api.QuoteExtractionPipeline` (internal — host-composition
  wiring, the same treatment `Raffa.Worker.Queue.QueueConsumerHostedService`
  already gets from `Raffa.ArchitectureTests
  .DependencyDirectionTests.Host_must_not_contain_domain_types`) is the one
  place that calls both `Raffa.AiGateway` and `Raffa.Quotes`: it reuses
  the epic-02 `Raffa.Documents.Contracts.Application.Extraction
  .HybridDocumentParsingService` verbatim (native text extraction, or the
  `ocr` gateway role — Azure AI Document Intelligence, ADR-017 — for
  scanned/image/low-text quote PDFs; full document, no 2-page cap; AC-4),
  then runs one `extract` call against `Raffa.Quotes.Application
  .Extraction.QuoteLineJsonSchema.LineItems()` and hands the raw payload to
  `Raffa.Quotes.Application.Extraction.QuoteLineExtractionService` to
  persist.
- AC-3 ("Separate arithmetic from LLM language", Appendix C rule 6): the
  line-item schema has **no** computed-total property at all — the model
  reports only `quantity`/`sku`/`edition`/`unitPrice`/`listPrice`/
  `discountPercent`/`term`. `QuoteLineExtractionService.ComputePricing`
  derives `QuoteLine.UnitPrice` (from `listPrice`/`discountPercent` when
  the model did not report a unit price directly) and
  `QuoteLine.ExtendedPrice` (`quantity × unitPrice`) in plain C# decimal
  arithmetic — proved directly by
  `Raffa.Quotes.Tests.QuoteLineExtractionServiceTests` and end-to-end by
  `Raffa.IntegrationTests.QuoteEndToEndTests`.
- Every line carries the same evidence + confidence tail as every other
  extraction pipeline in this codebase (`sourceSpan`/`sourcePage`/
  `confidence`, Appendix C rule 2) directly on the `QuoteLine` row — one
  row is already one fact, the same shape
  `Raffa.Documents.Contracts.Domain.ContractLineItem` uses (no separate
  evidence side-table).
- Deliberately out of task-01's own scope (not silently absorbed): the
  `Quote`-level aggregate fields spec §6 also names ("supplier, dates,
  currency, values, status") and benchmark matching/assessment/negotiation
  (spec §11's later Quote Check steps, `GET /api/quotes/{id}/assessment`,
  `POST /api/negotiations/outcomes`) — task-01's own coding objective was
  "Quote upload + line-item extraction". See below for task-02
  ("Line-item normalization + evidence/confidence"). **Task E05/F02/US01/T01
  (market-assessment) closed the supplier/currency/geography/purchase-date
  and benchmark-matching/assessment half of this gap** — see "Market
  Assessment" below; negotiation (`POST /api/negotiations/outcomes`)
  remains future work no task has picked up yet.

**Task E05/F01/US01/T02 (quote-normalization)** adds spec §11.1's next
pipeline step, "Normalize unit economics" (between "Extract" and "Match
benchmark"), right after line-item extraction inside the same
`QuoteExtractionPipeline.ProcessAsync` unit of work — before the one
shared `SaveChangesAsync`, so extraction and normalization persist
together or not at all. No new AI Gateway role and no new project
reference: `Raffa.Quotes.Application.Normalization
.QuoteLineNormalizationService.NormalizeUnitEconomics` is a second pure,
deterministic calculator alongside task-01's own `ComputePricing` — same
Appendix C rule 6 discipline, applied to a second pipeline stage.
`QuoteLine` gains two columns: `NormalizedAnnualUnitPrice` (`UnitPrice`
rescaled to an annual rate) and `NormalizedTermMonths` (the recognized
cadence length, in months, that produced it — kept as evidence, the same
"never a consequential derived fact without a way to see why" spirit
`SourceSpan`/`SourcePage` already give the raw extraction).
`Raffa.Quotes.Application.Normalization.QuoteBillingCadence
.RecognizeMonths` deliberately recognizes only a small, fixed,
unambiguous vocabulary (`monthly`/`quarterly`/`semi-annual`/`annual` and
their common synonyms — 1/3/6/12 months respectively); a numeric
commitment length ("36 months", "3 years"), "one-time"/"perpetual", a
blank term, or any other free text `QuoteLine.Term` may legitimately hold
(no ADR or spec fixes a closed vocabulary — see that property's own doc
comment) is left honestly unresolved (both new columns stay `null`)
rather than guess a billing-period relationship this codebase does not
actually know — the same restraint
`Raffa.Savings.Application.PriceComparisonRequest`'s own doc comment
already documents for cross-module term alignment (Appendix C rule 10).
A `null` `NormalizedAnnualUnitPrice` on any line **is** spec §11.3's own
"Do not generate a savings target if line-item normalization is
unresolved" guardrail made checkable — this task does not itself gate
anything (no savings target exists yet for a quote to gate), it only
produces the honest, queryable signal for whatever future benchmark-match
task reads it. `POST /api/quotes`'s response gains
`normalizedLineItemCount`/`unresolvedNormalizationCount` (see the HTTP
surface table above) so the same outcome is visible over HTTP, not just
in the database — proved directly by
`Raffa.Quotes.Tests.QuoteLineNormalizationServiceTests` and, for the
already-recognized-cadence common case, end-to-end by the existing
`Raffa.IntegrationTests.QuoteEndToEndTests` fixture (`"term":"Annual"`).

**Task E05/F01/US02/T01 (sku-normalization)** adds story
us-02-sku-normalization's own AC-1 ("Normalize SKU/edition to the
canonical product mapping") and the "show unmatched SKUs" half of AC-2:

- `Raffa.Quotes.Domain.SkuProductMapping` is this module's own,
  self-contained "canonical product mapping" — a tenant-scoped
  raw-normalized-SKU → canonical-SKU/edition/product-name table, **not** a
  reference into `Raffa.Suppliers.Products` (still an empty scaffold, and
  ADR-002 forbids `Raffa.Quotes` from referencing it or any other domain
  module's internals at all). `Raffa.Quotes.Application.Normalization
  .SkuNormalizer.Normalize` is the pure, deterministic text rule (trim,
  collapse whitespace, uppercase; punctuation is left untouched on purpose
  — see that type's own doc comment) both sides of the lookup share.
  `SkuNormalizationService.NormalizeAsync` re-reads a quote's own lines from
  the database and sets each one's `NormalizedSku`/`NormalizedEdition`/
  `MatchStatus` (`NotApplicable`/`Unmatched`/`Matched` —
  `Raffa.Quotes.Domain.SkuMatchStatus`); `Raffa.Api.QuoteExtractionPipeline`
  calls it right after persisting a quote's freshly-extracted lines, so
  every upload gets a real match status, not just a later explicit
  recalculate call.
- Honest gap, by construction: nothing writes a `SkuProductMapping` row yet
  (task E05/F01/US02/T02, "Manual product mapping + recalculate trigger",
  is its intended first writer), so every tenant starts with zero mappings
  and a line with a present SKU is always `Unmatched` today. This is spec
  §11.3's own guardrail ("Do not generate a savings target if line-item
  normalization is unresolved") made concrete rather than a limitation of
  this task: no benchmark/assessment step for quotes exists yet either for
  a resolved mapping to unblock.
- Proved directly by `Raffa.Quotes.Tests.SkuNormalizationServiceTests`
  (pure normalization, pure per-line matching, and a real-Postgres+RLS
  persistence/re-run/cross-tenant proof). `POST /api/quotes`' response now
  also carries `unmatchedSkuCount` (see the HTTP surface table above) —
  `Raffa.IntegrationTests.QuoteEndToEndTests` still passes unchanged with
  it present (that test's own fixture quote has no seeded mapping, so it is
  `1`), but no test yet asserts that field's value over real HTTP
  specifically; the persistence-level proof above is this task's own
  Definition of Done.

**Task E05/F01/US02/T02 (sku-recalculate)** closes story us-02-sku-normalization's
own AC-2 "...and allow manual product mapping" half and AC-3 "Re-run assessment
after mapping correction" — the intended first writer of `SkuProductMapping`
task-01's own doc comment named but never itself wrote:

- `Raffa.Quotes.Application.Normalization.SkuMappingService.RecalculateAsync`
  backs `POST /api/quotes/{id}/assessment/recalculate` (see the HTTP surface
  table above for the full request/response shape). For each caller-supplied
  correction it upserts one `SkuProductMapping` row (update in place when one
  already exists for that tenant+normalized-SKU — never a duplicate insert,
  the unique index would reject one anyway), then re-runs
  `SkuNormalizationService.NormalizeAsync` for every line on the quote and
  `MarketAssessmentService.AssessAsync` — composing both already-accepted
  services rather than re-deriving their logic, the same "reuse, do not
  re-implement" posture `NegotiationStrategyService` already takes for
  `MarketAssessmentService`.
- `mappings` is optional — a caller may POST `{}` to re-read the current
  unmatched-line list and a fresh assessment with no correction at all (e.g.
  right after `POST /api/quotes` reports a non-zero `unmatchedSkuCount`,
  before any correction has been decided).
- AC-2's "Show unmatched SKUs" half is deliberately **not** a field on `GET
  /api/quotes/{id}/assessment` itself: `LineMarketAssessment` is task
  E05/F02/US01/T01's own already-accepted file, and
  `NegotiationStrategyService`'s own doc comment already declined to extend
  it for the identical "do not touch unrelated wave artifacts" reason. This
  task follows that same precedent — `unmatchedLines` on the recalculate
  response is its own small, independent read instead
  (`SkuMappingService.GetUnmatchedLinesAsync`).
- A `SkuProductMapping` is tenant-scoped, not quote-scoped (see that type's
  own doc comment) — a correction made while looking at one quote also
  resolves every other quote for the same tenant sharing the same normalized
  SKU, the next time *that* quote is itself (re)normalized (a fresh upload,
  or its own recalculate call) — proved directly by
  `Raffa.Quotes.Tests.SkuMappingServiceTests`
  `RecalculateAsync_a_mapping_learned_on_one_quote_resolves_a_different_quote_on_its_own_next_refresh`.
- Owns its own tenant scope (`ITenantContext.BeginScope`) from day one — the
  always-404-in-production class of bug task E05/F04/US01/T01 (r4-integration)
  found and fixed for `MarketAssessmentService`/`NegotiationStrategyService`
  (see "Market Assessment" below) is not repeated here.
- Proved directly by `Raffa.Quotes.Tests.SkuMappingServiceTests` (pure
  per-correction upsert rule, and a real-Postgres+RLS persistence proof:
  create, update-in-place, validation-before-any-write, cross-tenant 404,
  cross-quote reuse, and a full correct-then-assess chain against the real
  `FixtureBenchmarkAdapter`) and end to end by
  `Raffa.IntegrationTests.R4EndToEndTests`, which now drives this real
  endpoint over real HTTP for AC-2 instead of the direct-service-call
  workaround that class's own doc comment used to describe.

## Market Assessment — benchmark matching + above/in-line/below

Task E05/F02/US01/T01 (market-assessment; parent story us-01-market-assessment
AC-1 "Match normalized line items to the Benchmark Service
(multi-dimensional)", AC-2's own "flag" half, AC-3 "`GET
/api/quotes/{id}/assessment` returns the assessment with
confidence/provenance") closes the gap this section's own task-01 paragraph
used to name ("benchmark matching/assessment/negotiation remain future work
no task has picked up yet") and the gap `Raffa.Quotes.Infrastructure
.ServiceCollectionExtensions.AddQuotesModule`'s own doc comment used to name
("deliberately does not call `AddBenchmarkModule`... nothing this task adds
resolves `IBenchmarkService` yet").

- **`Quote` gains its own benchmark-matching fields**: `Supplier`,
  `Currency`, `Geography`, `PurchaseDate` — spec §6's "Quote-level aggregate
  fields" that task-01 deliberately deferred. Unlike the identical-looking
  gap `Raffa.IntegrationTests.R3IntegrationFixture`'s own doc comment left
  open for `Raffa.Documents.Contracts.Domain.Contract` (ADR-002 forbids
  `Raffa.Savings` from reaching into that module at all), `Raffa.Quotes`
  owns both `Quote` and `QuoteLine` itself — no cross-module reference is
  involved — so there was no architectural reason to leave this one open
  once a task actually needed it. All four are populated by explicit,
  **optional** `POST /api/quotes` form fields (see the HTTP surface table
  above), never inferred from the document text (Appendix C rule 10):
  nothing in this codebase extracts a document-level supplier/geography/
  currency, and spec §11.1's own "Identify supplier" workflow step has no
  task/UI of its own yet. A quote uploaded without them is simply not
  matchable yet — an honest, expected state
  (`Raffa.Quotes.Application.Assessment.MarketAssessmentQueryBuilder`
  reports that per line, naming exactly which dimension is missing), not a
  validation error at upload time.
- **`AddQuotesModule` now also calls `Raffa.Benchmark
  .ServiceCollectionExtensions.AddBenchmarkModule`** — the same "a module
  that depends on another module's interface registers that dependency's
  own DI wiring transitively" convention `Raffa.Savings
  .Infrastructure.ServiceCollectionExtensions.AddSavingsModule`'s own doc
  comment already established for this exact call (and explicitly
  anticipated a future `Raffa.Quotes` caller doing the same).
  `Raffa.Quotes.csproj`'s own `ProjectReference` to `Raffa.Benchmark`
  pre-dated this task (an R4 scaffold anticipating this exact step) — this
  is that compile-time dependency's first runtime DI registration.
- **`Raffa.Quotes.Application.Assessment.MarketAssessmentQueryBuilder`**
  builds a `Raffa.Benchmark.Contracts.BenchmarkQuery` per line: `Product`
  from `QuoteLine.Description`, `Sku` from `NormalizedSku` (falling back to
  the raw `Sku`), `Quantity`/`Term` from the line, `Supplier`/`Geography`/
  `Currency`/`PurchaseDate` from the quote. Pure, honest, never fabricates a
  missing dimension. **Deliberately compares the line's raw `UnitPrice`, not
  `NormalizedAnnualUnitPrice`**: that annualized figure only exists for a
  term `QuoteBillingCadence` recognizes (a word vocabulary — "annual",
  "monthly", ...), a different, narrower vocabulary than
  `Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter`'s own catalog `Term`
  values ("12 months", "36 months") — mirrors `Raffa.Savings.Application
  .PriceComparisonRequest`'s own "term alignment is the Benchmark Service's
  own matching responsibility, no additional term-arithmetic here" doc
  comment.
- **`Raffa.Quotes.Application.Assessment.MarketAssessmentCalculator`**
  flags the line's price `BelowMarket`/`InLine`/`AboveMarket` against the
  matched `BenchmarkResult.Distribution`'s `[P25, P75]` band (at-or-below
  P25 is below market; at-or-above P75 is above; anything else, including
  exactly P50, is in line) — or the honest
  `Raffa.Quotes.Domain.MarketAssessmentStatus.InsufficientBenchmarkData`
  when the benchmark has no usable distribution (ADR-001), never a
  fabricated flag (Appendix C rule 10).
- **`Raffa.Quotes.Application.Assessment.MarketAssessmentProvenanceClassifier`**
  mirrors `Raffa.Savings.Application.SavingsProvenanceClassifier` field-
  for-field and threshold-for-threshold (High ≥ 0.7, Medium ≥ 0.4) —
  duplicated, not shared: ADR-002's allowed-reference set for
  `Raffa.Quotes` is exactly `[SharedKernel, Benchmark]`.
  `Raffa.Quotes.Application.Assessment.MarketAssessmentService.AssessAsync`
  is the one place in this module that actually calls
  `IBenchmarkService.GetBenchmarkAsync` — Appendix C's benchmark rule names
  the provider *adapter*, not this abstraction (`IBenchmarkService`'s own
  doc comment: "Domain modules depend on this abstraction only").
- Proved directly by `Raffa.Quotes.Tests.MarketAssessmentCalculatorTests`/
  `MarketAssessmentQueryBuilderTests` (pure, no database) and end to end by
  `Raffa.Quotes.Tests.MarketAssessmentServiceTests` against a real
  Postgres+RLS database and the real `FixtureBenchmarkAdapter` (never a
  stub) — one quote, three lines, demonstrating `Assessed`/
  `QuoteDataUnresolved`/`InsufficientBenchmarkData` together, the same
  "build a query by hand that matches a real fixture catalog row" convention
  `Raffa.IntegrationTests.R3EndToEndTests` already established for the
  analogous Savings comparison.
- **Task E05/F02/US01/T02 (target-saving)** closes the gap this section's own
  task-01 paragraph used to name ("recommended target range and potential
  saving... are task-02's own, separate `target-saving` wave-spec artifact"):
  `Raffa.Quotes.Application.Assessment.TargetSavingCalculator.Compute`
  computes spec §11.2's "Recommended target"/"Potential saving" rows —
  `RecommendedTargetLow/High = min(P25/P50, unitPrice)` (never above the
  current price) and `SavingsRangeLow/High` (per-unit) +
  `TotalSavingsRangeLow/High` (scaled by `QuoteLine.Quantity` — the
  `CHF 80-110k`-shaped total spec §11.2's own example shows, not a per-unit
  rate). Mirrors `Raffa.Savings.Application.PriceNormalizationCalculator`'s
  own target/savings-range formula exactly — duplicated, not referenced,
  the same `[SharedKernel, Benchmark]`-only reference rule
  `MarketAssessmentProvenanceClassifier` already follows. Never fabricates: a
  benchmark with no usable distribution returns a `LineMarketAssessment
  .TargetSaving` with every numeric field `null` plus a named reason —
  still a real object, never silently withheld, the same benchmark-trust
  posture `Provenance` already takes for `InsufficientBenchmarkData` (spec
  §11.3). `LineMarketAssessment` gained a `Quantity` field (echoed from
  `QuoteLine.Quantity`, the same "caller never has to re-fetch the line"
  posture `UnitPrice` already has) so `TargetSaving` can scale its total
  figures without a second database round-trip. `GET
  /api/quotes/{id}/assessment`'s response gained a `targetSaving` object per
  line (see the HTTP surface table above) alongside the existing
  `benchmark`/`confidence` objects. Proved directly by
  `Raffa.Quotes.Tests.TargetSavingCalculatorTests` (pure, no database,
  mirroring `MarketAssessmentCalculatorTests`'s own shape) and end to end by
  the same `MarketAssessmentServiceTests` fixture above — the parent story
  us-01-market-assessment Definition of Done in full ("`dotnet test` proves
  assessment + target/saving from fixture benchmark"). Negotiation strategy
  generation is task E05/F03/US01/T01's own scope — see "Negotiation
  Strategy" below; outcome capture (`POST /api/negotiations/outcomes`,
  feature-03's us-02) remains future work no task has picked up yet.
- **Incidental fix, required for this task's own `dotnet build` to succeed
  at all**: `Raffa.Api.QuoteExtractionPipeline.ProcessAsync` (touched by
  both task E05/F01/US01/T02 and task E05/F01/US02/T01 in parallel
  wave-spec phases) had a duplicate local-variable declaration
  (`normalizationOutcome` declared twice, `CS0128`) and two stray, dangling
  duplicate lines (inside the method's own `return` statement and inside
  `QuoteProcessingSummary`'s record declaration) — each sibling task had
  appended its own new field/parameter without reconciling with the other's
  identical-shaped addition, so the whole `Raffa.Api` project (and every
  test depending on it — `Raffa.Api.Tests`, `Raffa.IntegrationTests`)
  could not compile. Renamed the two outcomes to their own distinct names
  (`lineNormalizationOutcome`/`skuNormalizationOutcome`) and removed the
  duplicate lines; no behavioural change to either sibling task's own
  already-landed logic. The `POST /api/quotes` HTTP-surface-table row above
  had the identical duplicate-row shape (two rows, each missing the other's
  fields) — consolidated into the one row above for the same reason.
- **Task E05/F04/US01/T01 (r4-integration) fixes**: `MarketAssessmentService`
  never opened its own `ITenantContext.BeginScope` — unlike every other
  tenant-scoped application service in this codebase — and neither did
  `Raffa.Api.QuotesEndpointExtensions.GetAssessmentAsync` upstream of it.
  Against a real, RLS-enforced, non-superuser connection (every deployed
  environment), `GET /api/quotes/{id}/assessment` would 404 for every real
  quote, always — undetected because `MarketAssessmentServiceTests` calls
  this method from inside a test-provided scope, and no integration test had
  yet driven this endpoint over real HTTP against an unprivileged Postgres
  role. Fixed the same way every sibling service already does it (see that
  type's own doc comment) — no caller-side change required. Separately,
  `GetAssessmentAsync` never actually serialized `quantity` on the response
  despite `LineMarketAssessment.Quantity` existing exactly to be echoed here
  and despite this very HTTP-surface-table row documenting it since task
  E05/F02/US01/T02 — also fixed, so the wire response now matches its own
  already-published contract. Both surfaced by, and proved fixed by,
  `Raffa.IntegrationTests.R4EndToEndTests`/`R4CrossTenantIsolationTests` —
  see "R4 demo smoke test" below.

## Negotiation Strategy — opening target/range/walk-away + levers

Task E05/F03/US01/T01 (negotiation-strategy; parent story
us-01-negotiation-strategy AC-1 "Generate opening target, acceptable range,
walk-away threshold, levers, rationale", AC-3 "Arithmetic (target/saving) is
deterministic; only language is LLM") closes the gap the "Market Assessment"
section above used to name ("Negotiation ... remains future work no task has
picked up yet").

- **`Raffa.Quotes.Application.Strategy.NegotiationStrategyCalculator`**
  is a pure, synchronous calculator (no database/HTTP/LLM call) that turns
  an already-computed `LineMarketAssessment.TargetSaving` (task
  E05/F02/US01/T02) into `LineNegotiationStrategy.{OpeningTarget,
  AcceptableRangeLow/High, WalkAwayThreshold}`: the acceptable range echoes
  `RecommendedTargetLow/High` verbatim (spec §12.1's "Acceptable target
  range" row is §11.2's own "Recommended target" row carried forward, not a
  second computation), opening target steps one range-width below the low
  end (floored at zero) and walk-away steps one range-width above the high
  end, clamped to the line's own current `UnitPrice` (never recommend
  escalating past what is already quoted — the same clamp
  `TargetSavingCalculator` already applies to `RecommendedTargetHigh`).
  Never fabricates: no usable target range, or no current `UnitPrice`,
  returns every numeric field `null` plus an empty lever list and a named
  reason (Appendix C rule 10) — the same honest-abstain shape
  `TargetSavingCalculator.Compute` already established.
- **Levers are always the full, fixed, spec §12.1-named set of seven**
  (`NegotiationLeverType`: `Volume`, `Term`, `Utilization`, `Alternatives`,
  `QuarterEnd`, `Bundle`, `PaymentTerms`) — never a variable-length subset —
  so a caller always sees the complete playbook. `Volume`/`Term`/`Bundle`
  ground themselves in this line/quote's own recorded data when it exists
  (`QuoteLine.Quantity`/`Term`, and how many `QuoteLine` rows share this
  line's own quote); `QuarterEnd` is date-derived (within 14 days of a
  calendar quarter-end, evaluated as of the caller's own `IClock`-derived
  "today", never a historical quote date); `Utilization`/`Alternatives`/
  `PaymentTerms` have no source field anywhere in this module's schema
  today, so their rationale says so honestly rather than inventing a
  this-quote-specific fact.
- **Deterministic language, not yet an AI Gateway `answer`-role call**: AC-3's
  "only language is LLM" is honoured by keeping every number in the pure
  calculator above; the per-lever `Rationale` text is V1 deterministic
  language, the same "`Explanation` is a computed string, never a model
  call" convention `TargetSavingCalculator`/`MarketAssessmentCalculator`
  already follow. `Raffa.ArchitectureTests.DependencyDirectionTests`'
  allowed-reference set for `Raffa.Quotes` is exactly `[SharedKernel,
  Benchmark]` (see "Dependency direction" below) — unchanged by this task.
  A future task wiring the `answer` role would do it the same way
  `Raffa.Api.QuoteExtractionPipeline` already does for the `extract`
  role: from the composition root, feeding this calculator's own facts in
  as evidence, never asking the model to invent them.
  `Raffa.AiGateway.Fixtures.FixtureAiGateway.AnswerAsync` would today only
  echo those facts back verbatim (no live grounded-generation model exists
  yet), so deferring that wiring loses no real capability now. Evidence
  *citations* per lever (AC-2, Appendix C rule 2) were task-01's own,
  separate, deferred scope (strategy-evidence) — closed below by task
  E05/F03/US01/T02.
- **Structured evidence per lever (task E05/F03/US01/T02, strategy-evidence;
  AC-2 "Rationale cites explicit evidence per lever", Appendix C rule 2
  "never show a consequential... fact without source evidence and
  confidence metadata")**: `NegotiationLever` gained an `Evidence` field —
  `IReadOnlyList<Raffa.Quotes.Application.Strategy.NegotiationLeverEvidence>`,
  each a `FieldName`/`Value`/`SourceSpan`/`SourcePage`/`Confidence` tuple.
  Mirrors `Raffa.Documents.Contracts.Domain.ExtractionEvidence`'s own
  "which field, what value, from where, how confident" addressing scheme,
  kept as its own `Raffa.Quotes`-local record rather than
  `Raffa.AiGateway.Contracts.AiCitation`/`AiEvidenceSnippet` (those are
  document-citation-shaped — `DocumentId`/`Page`/`Section` — for RAG
  answers over unstructured text, and `Raffa.Quotes`' own
  allowed-reference set, `[SharedKernel, Benchmark]`, cannot reach
  `Raffa.AiGateway` anyway). `Volume`/`Term` cite `QuoteLine.Quantity`/
  `Unit`/`Term` carrying this same line's own extraction `SourceSpan`/
  `SourcePage`/`Confidence` (fields the AI Gateway `extract` role
  originally proposed for the row — a `QuoteLine` row is one extraction
  event covering the whole row); `QuoteLine.NormalizedTermMonths` cites
  alongside `Term` but with no provenance of its own, since it is derived
  deterministically from `Term` (Appendix C rule 6), not a second,
  independently-extracted fact. `Bundle`/`QuarterEnd` cite the sibling-line
  count / negotiation-timing as-of date — always populated (never empty,
  unlike `Volume`/`Term`), with no span/page/confidence, since neither is a
  `QuoteLine` field or a document extraction. `Utilization`/`Alternatives`/
  `PaymentTerms` stay evidence-empty, the same "no source field exists"
  reason their `Rationale` already gives (Appendix C rule 10 — never
  fabricate a citation for a fact that is not actually there). The cited
  `Value` always renders exactly as `Rationale` itself renders it, so the
  structured citation and the prose can never silently disagree.
- **`Raffa.Quotes.Application.Strategy.NegotiationStrategyService`**
  composes on top of `MarketAssessmentService.AssessAsync` (reused, not
  re-derived) plus one extra `QuoteLine` read (for `Term`/
  `NormalizedTermMonths`/`Unit`, which `LineMarketAssessment` does not echo)
  and returns one `LineNegotiationStrategy` per line — the same per-line,
  no-quote-level-rollup shape `QuoteMarketAssessment` already established,
  and the same "computed fresh on every call, nothing persisted" posture
  `MarketAssessmentService` already takes. Not yet wired to an
  `AddQuotesModule`-registered HTTP endpoint: parent story
  us-01-negotiation-strategy's own acceptance criteria name no `GET
  /api/quotes/{id}/...` route (unlike us-01-market-assessment's AC-3), so
  none was added — `AddQuotesModule` registers the service so a future
  task/feature-04 (r4-integration) can call it. **Task E05/F04/US01/T01
  (r4-integration) is that caller**: `Raffa.IntegrationTests.R4EndToEndTests`
  resolves this service directly from the real host's own container (the
  same "no dedicated route exists yet" convention `R2EndToEndTests`/
  `R3EndToEndTests` already established), still with no dedicated HTTP route
  of its own — that remains open, un-picked-up scope. That same task also
  gave this service its own `ITenantContext.BeginScope` (it never opened one
  either, for the identical reason and with the identical real-HTTP
  consequence the "Market Assessment" section above now documents for
  `MarketAssessmentService`) — see this type's own doc comment.
- Proved directly by `Raffa.Quotes.Tests.NegotiationStrategyCalculatorTests`
  (pure, no database — range/walk-away arithmetic, all seven levers, every
  honest-abstain branch, determinism) and end to end by
  `Raffa.Quotes.Tests.NegotiationStrategyServiceTests` against a real
  Postgres+RLS database and the real `FixtureBenchmarkAdapter`, reusing
  `MarketAssessmentServiceTests`' own Salesforce/Sales-Cloud-Enterprise
  fixture comparable (P25/P50/P75 = 1500/1800/2100 per seat/year) so both
  tests agree on what the numbers mean. Task E05/F03/US01/T02
  (strategy-evidence) extends the same calculator test class with AC-2's
  own coverage — per-lever evidence content, `SourceSpan`/`SourcePage`/
  `Confidence` pass-through for `Volume`/`Term`, the no-provenance case for
  `NormalizedTermMonths`/`Bundle`/`QuarterEnd`, the honest-empty case for
  `Utilization`/`Alternatives`/`PaymentTerms`, and a citation-vs-`Rationale`
  cross-check — plus one end-to-end assertion in
  `NegotiationStrategyServiceTests` proving `Evidence` also comes back
  populated through the real database round trip, not just the pure
  calculator. The determinism test itself now asserts each lever's
  `LeverType`/`Rationale`/`Evidence` as its own sequence rather than via
  `NegotiationLever`'s own record-generated `Equals`: `Evidence` is an
  `IReadOnlyList<T>`, which has no structural equality of its own, so two
  independently-built lever lists that are otherwise identical would
  compare unequal two levels deep inside a containing record's `Equals`.

## Negotiation Outcome — capture + append-only + audit

Task E05/F03/US02/T01 (negotiation-outcome; parent story
us-02-outcome-capture AC-1 "records original/target/final/saving/discount/
duration/levers", AC-3 "Outcome is versioned + audit-tracked") closes spec
§12.2 ("Negotiation outcome capture") — the "Negotiation Strategy" section
above recommends a target; this is where what actually happened gets
recorded as permissioned proprietary learning data (spec §12.3's data
flywheel).

- **`POST /api/negotiations/outcomes`** (module-map.md "Quotes | ... |
  /api/quotes, /api/negotiations/outcomes"; `X-Tenant-Id` header; plain
  JSON body, unlike `POST /api/quotes`'s multipart upload) —
  `{ quoteId, originalQuoteTotal, targetPrice?, finalPrice,
  negotiationDurationDays, leversUsed: [<NegotiationLeverType name>, ...],
  savingsOpportunityId? }` (`savingsOpportunityId` added by task
  E05/F03/US02/T02, outcome-propagation — see the dedicated bullet below).
  404 (`Raffa.Quotes.Application.Outcome.NegotiationOutcomeService
  .QuoteNotFoundError`) when `quoteId` does not name a quote for this
  tenant; 400 for every validation failure (non-positive
  `originalQuoteTotal`/`finalPrice`, a negative `targetPrice`, a negative
  `negotiationDurationDays`, an empty or unrecognized `leversUsed`).
  Response `{ id, quoteId, originalQuoteTotal, targetPrice, finalPrice,
  realizedSaving, discountPercent, negotiationDurationDays, leversUsed,
  capturedAt, savingsOpportunityId, savingsPropagated,
  savingsPropagationError }` — `savingsOpportunityId` on the response
  echoes only what the caller supplied (`null` when they supplied none,
  even when task E19/F04/US01/T01's own server-side resolution below
  links the outcome anyway: no column and no contract write that fact
  back onto the persisted `NegotiationOutcome` row). **Corrected rule
  (ADR-028 w16 round-2 footer)**: `savingsPropagated` is `null` exactly
  when no opportunity was linked — because none was named **and** none
  resolved unambiguously; it is always `true`/`false` whenever a link was
  attempted, by either route (see the propagation bullet below), and
  `savingsPropagationError` is set only when `savingsPropagated` is
  `false` — never a distinct HTTP status for a propagation failure.
- **`Raffa.Quotes.Application.Outcome.NegotiationOutcomeCalculator`** is a
  pure, synchronous calculator (no database/HTTP/LLM call, Appendix C rule
  6) — `realizedSaving = originalQuoteTotal - finalPrice`,
  `discountPercent = realizedSaving / originalQuoteTotal * 100`. Never
  clamped at zero: a `finalPrice` above `originalQuoteTotal` is an honest
  negative saving, not a fabricated floor (Appendix C rule 10). Reproduces
  spec §12.2's own worked example exactly (520k / 435k -> 85k saved,
  ~16.3%).
- **`targetPrice` is nullable** — echoes `LineNegotiationStrategy
  .OpeningTarget`'s own nullability for the identical reason (no usable
  target range was ever available, e.g. insufficient benchmark data):
  outcome capture is never blocked on a fact this module honestly never
  had (Appendix C rule 9 "from day one").
- **`leversUsed` reuses `NegotiationStrategyCalculator`'s own closed
  `NegotiationLeverType` vocabulary** (seven canonical levers), not free
  text — parsed case-insensitively from the wire string list by
  `NegotiationOutcomeService.CaptureAsync` itself (this codebase has no
  global `JsonStringEnumConverter`; every enum-accepting endpoint parses
  its own wire strings, e.g. `SavingsOpportunityPatchRequest.Status`), so
  which levers actually work stays a queryable, aggregable dimension for
  spec §12.3's "better recommendation" loop, not prose a later task would
  have to re-parse. At least one entry is required.
- **"Versioned" (AC-3) means append-only, never a `PATCH`/update** — spec
  Appendix A names only `POST` for this resource.
  `NegotiationOutcomeService.CaptureAsync` only ever `Add`s a new
  `Raffa.Quotes.Domain.NegotiationOutcome` row; a second capture for the
  same `quoteId` (a renegotiation, or a correction to an earlier capture)
  is simply another row, ordered by `capturedAt` — the same "never
  destructively overwrite" convention (Appendix C rule 5)
  `Raffa.Savings.Domain.RealizedSavings` already establishes for the
  identical App C #5/#9 pairing on a sibling "capture a final,
  consequential figure" entity.
- **Audit-tracked (AC-3)**: writes one `IAuditWriter` entry
  (`negotiation_outcome.captured`) per successful capture, still inside
  the same call's tenant scope — same placement as `QuoteUploadService
  .UploadAsync`'s own "persist -> audit" write.
- **Realized-savings propagation (task E05/F03/US02/T02,
  outcome-propagation; parent story AC-2 "Realized savings surface on the
  savings dashboard (cross-wave)")**: when the caller supplies
  `savingsOpportunityId`, `Raffa.Api.NegotiationsEndpointExtensions`
  also calls `Raffa.Api.NegotiationOutcomePropagationService
  .PropagateAsync` right after the capture itself is already durable,
  still in the same request. That type is `internal`, host-composition-
  root-only wiring (ADR-002: `Raffa.Quotes` and `Raffa.Savings` cannot
  see each other; only `Raffa.Api` may reference both — the same
  treatment `QuoteExtractionPipeline` already gets, see "Dependency
  direction" below), and it reuses the exact same, already-audited write
  path a human `PATCH /api/savings/{id}` call already uses
  (`SavingsOpportunityService.UpdateAsync` with `realizedAmount` set — see
  "Savings Intelligence — trackable SavingsOpportunity" above): that call
  finalizes the opportunity's own `status` as `Realized` and inserts a new
  `RealizedSavings` row, in the opportunity's own currency. This service
  then writes one more, distinct `IAuditWriter` entry
  (`negotiation_outcome.propagated`) recording the link between the two
  aggregate ids — the one fact neither the `negotiation_outcome.captured`
  nor the `savings_opportunity.realized` entry captures alone. **Never
  fails an already-durable capture**: an unknown `savingsOpportunityId`
  (or any other `UpdateAsync` validation failure) is reported honestly as
  `savingsPropagated: false` + `savingsPropagationError` on the same 201
  response, never a 4xx/5xx — the outcome capture itself already succeeded
  and is already audit-tracked (AC-3) before propagation is even
  attempted. No currency reconciliation: `NegotiationOutcome` carries no
  currency of its own, and `UpdateAsync`'s own `realizedAmount` parameter
  has never reconciled a caller-supplied figure against another currency
  either — the same, already-accepted trust assumption an automated
  caller now shares with a human PATCHing directly. `GET
  /api/savings/kpis`'s own `savingsRealized` bucket does **not** yet read
  this `RealizedSavings` row (the honest gap the "Savings Intelligence —
  trackable SavingsOpportunity" section above already names, task
  E04/F02/US02/T02's own follow-up, not this task's) — "surfaces on the
  savings dashboard" (AC-2) today means the opportunity's own `status` and
  realized-value row are real and queryable, not yet that every KPI number
  reflects them.
- **Server-side resolution when no id is supplied (task E19/F04/US01/T01,
  outcome-resolves-the-opportunity; ADR-028 §D5 clause 2, ratified by the
  w16 round-2 footer)**: the single production caller
  (`web/src/routes/quotes/index.tsx`) never sends `savingsOpportunityId`
  at all, so clause 1 alone left this path unreachable from the UI.
  `Raffa.Api.NegotiationOutcomePropagationService
  .ResolveSavingsOpportunityIdAsync` normalizes the captured outcome's own
  quote's `Supplier` name
  (`Raffa.Suppliers.Products.Application.SupplierNameNormalizer
  .Normalize`), looks it up **read-only** against the
  `(tenant_id, normalized_name)` unique index
  (`Raffa.Suppliers.Products.Application.ISupplierNameLookup
  .FindByNormalizedNameAsync` — the module's existing `SupplierResolver`,
  which resolves *or creates*, is never called on this path: recording an
  outcome must never mint a supplier row), then the tenant's own **open**
  (not yet `Realized`) `SavingsOpportunity` rows carrying that supplier
  id. **Exactly one is linked**, the same way an explicit id is; **zero or
  two-or-more declines** — `PropagateAsync` is never called at all, so no
  opportunity row updates, `status` stays `Identified`/`InProgress` and no
  `RealizedSavings` row is inserted (Fence 2, structural by construction,
  not a rendering promise: `SavingsKpiCalculator` reads opportunity rows,
  so a total cannot absorb a write that never happened). The quote naming
  no supplier, or naming one this tenant has never resolved before, is an
  equally honest decline — resolved, never guessed (ADR-001 w16 clause 4).
  A16-8's own acceptance walk still depends only on the explicit-id path
  (Fence 1): the pilot corpus may contain no quote whose supplier name
  resolves unambiguously, and that is expected and harmless, never a
  failed acceptance.
- Proved directly by `Raffa.Quotes.Tests.NegotiationOutcomeCalculatorTests`
  (pure, no database — the spec §12.2 worked example, the negative-saving
  honesty case, determinism) and end to end by
  `Raffa.Quotes.Tests.NegotiationOutcomeServiceTests` against a real
  Postgres+RLS database (persistence, the audit entry, the "second capture
  does not overwrite the first" append-only proof, quote-not-found/
  cross-tenant/every validation failure, and — task E05/F03/US02/T02 —
  that a caller-supplied `savingsOpportunityId` persists unvalidated) plus
  `Raffa.Api.Tests.NegotiationsEndpointTests` for the host-level
  tenant-header guard clause. Realized-savings propagation itself (task
  E05/F03/US02/T02) is proved end to end by
  `Raffa.IntegrationTests.NegotiationOutcomePropagationEndToEndTests`
  against the real, composed `Raffa.Api` host and a real, migrated
  Postgres+RLS database spanning both `Raffa.Quotes` and
  `Raffa.Savings` — a real `SavingsOpportunity` realized (`status`,
  the `RealizedSavings` row, the `negotiation_outcome.propagated` audit
  entry, and all three response fields) and an unknown
  `savingsOpportunityId` (the outcome still persists and the call still
  returns 201; `savingsPropagated: false` + `savingsPropagationError`
  reported honestly instead of an HTTP failure). The clause-2 resolution
  path (task E19/F04/US01/T01) is proved end to end by
  `Raffa.IntegrationTests.NegotiationOutcomeResolutionTests` against that
  same real, composed host — now also migrated with `Raffa.Suppliers
  .Products`'s own schema — over all three shapes: explicit id (clause 1,
  unchanged), one unambiguous supplier-name match (clause 2, linked), and
  zero/two-or-more matches (clause 2, declined — including the
  byte-identical `GET /api/savings` assertion that proves Fence 2 and a
  cross-tenant negative that an opportunity of tenant B is never linked to
  an outcome of tenant A). The name → id lookup itself
  (`SupplierNameLookup.FindByNormalizedNameAsync`) is proved read-only —
  a miss creates no `Supplier` row — by
  `Raffa.Suppliers.Products.Tests.SupplierNameLookupTests`.

## Quote and outcome read-back

Task E19/F02/US01/T01 (quote-read-api; parent story us-01-quote-read-api; ADR-028 D2,
wave w16 NW-12) closes the gap the "Quote Check" section above and "Negotiation
Outcome" section left open: until this task, nothing in this backend could read a
quote or its recorded negotiation outcomes back over HTTP -- a reload or a second
colleague opening the same quote had no way to see what was already recorded.

- **`GET /api/quotes`** -- the tenant's quotes, newest first, and only the
  tenant's. **`GET /api/quotes/{id}`** -- one quote **with its recorded
  `NegotiationOutcome` rows embedded, newest first**; a quote with none returns an
  empty `outcomes` array, never 404. Both routes are documented in the HTTP surface
  table above.
- **New `Raffa.Quotes.Application.QuoteQueryService`** (`ListAsync`/`GetAsync`),
  modelled on `Raffa.Documents.Contracts.Application.PortfolioQueryService`/
  `DocumentQueryService` -- same "open its own tenant scope, nothing upstream does
  it" shape. It **returns stored fields and computes nothing**: it takes no
  dependency capable of recomputing a `Raffa.Quotes.Application.Assessment
  .QuoteMarketAssessment` (no `MarketAssessmentService`, no `IBenchmarkService`),
  so a `GET /api/quotes/{id}` can never re-report a negotiation it did not touch --
  a structural guarantee, not a documented convention.
- **The outcome is a property of the quote, not a tenant-wide feed.**
  `NegotiationOutcome` is keyed by `QuoteId` and is append-only (see that type's own
  doc comment), so this task deliberately does **not** add a
  `GET /api/negotiations/outcomes`: a bare tenant-wide *outcome* list still has no
  caller. **Update (task E25/F04/US01/T01, W18):** NW-57 landed instead as a
  narrower, different surface — `GET /api/quotes/benchmark-history` (see "Quote
  benchmark history" below) — the market-*benchmark* half of NW-57's ask, not a
  negotiation-outcome list. The cross-quote levers/Ask-citable outcome feed
  (`inputs/design/prototypes/raffa-v2/screens-v2.md:139-145`) remains unbuilt with
  no scheduled closer, and publishing `GET /api/negotiations/outcomes` now would
  still pre-empt that item's own product shape.
- **Why `GET /api/quotes/{id}/assessment` is not overloaded with the outcome**
  instead (ADR-028 D2, declining an earlier client-architect shape preference):
  `POST /api/quotes/{id}/assessment/recalculate` returns that exact same assessment
  shape, so carrying the outcome there would make a recalculation appear to
  re-report a negotiation record it never touched. One extra `GET` on mount is the
  cheaper of the two costs.
- **No migration** -- `quote` and `negotiation_outcome` were already
  `ENABLE`+`FORCE ROW LEVEL SECURITY` (this module's own `AddTenantRowLevelSecurity`
  and `AddNegotiationOutcome` migrations); this task only reads what already exists.
- Proved directly by `Raffa.Quotes.Tests.QuoteQueryServiceTests` (tenant isolation,
  newest-first ordering for both the list and the embedded outcomes, the empty-vs-404
  distinction, and a stored-value-verbatim check that a deliberately "wrong"
  `RealizedSaving`/`DiscountPercent` comes back unchanged -- proving no
  recomputation happens) and end to end by
  `Raffa.IntegrationTests.QuoteReadBackTests` against a real Postgres+RLS database
  and the real host: the embedding order, a second-caller read-back (record in one
  session, read in a different one), the cross-tenant negative on both routes, and
  the 404/400 id-validation ladder.

## Quote benchmark history

Task E25/F04/US01/T01 (quote-benchmark-backend; parent story
us-01-quote-benchmark-backend AC-1/AC-2; ADR-024, ADR-028, ADR-001; closes NW-57)
re-focuses Quote check's assessment on the market-benchmark job itself: a
tenant's quotes read back as durable server state, each carrying its
market-benchmark position, so the job-to-be-done is a comparison a colleague can
reopen later, not a worksheet that lives only in one browser tab. AC-3 ("every
request is kept") needed no code change — this task adds a read, and the
existing "See it in Savings →" CTA already never replaced the benchmark result.

- **`GET /api/quotes/benchmark-history`** — the tenant's quotes, newest first,
  each carrying a freshly-recomputed per-line market-benchmark assessment;
  documented in the HTTP surface table above.
- **New `Raffa.Quotes.Application.QuoteBenchmarkHistoryService.GetHistoryAsync`**
  composes rather than re-derives: for every quote this tenant has, it calls the
  already-accepted `MarketAssessmentService.AssessAsync` — the same cold-start
  rule governs both routes, so a first-of-type quote (no fixture comparable yet
  for its supplier/product) reports the honest `InsufficientBenchmarkData` status
  here exactly as it does on `GET /api/quotes/{id}/assessment` — never a
  fabricated position (ADR-001, Appendix C rule 10).
- **Nothing new is persisted and no migration exists for this task.** Like
  `MarketAssessmentService` itself, every line's assessment is computed fresh on
  every call; "history" means the `Quote`/`QuoteLine` rows themselves are the
  durable server state (already RLS-enabled) — a prior quote's benchmark "reads
  back" because the quote that produced it is still in the database, not because
  a computed position was snapshotted at upload time (ADR-028).
- **What this does and does not close**, relative to the "Quote and outcome
  read-back" section above: NW-57 asked for the *benchmark* half (history +
  benchmark-first UX); the *negotiation-outcome* half that section describes —
  `GET /api/negotiations/outcomes` — stays open; see that section's own updated
  note.
- `QuotesEndpointExtensions.BuildAssessmentResponse`'s per-line projection was
  extracted into a standalone `BuildLineAssessmentResponse` so this route's own
  `BuildHistoryEntryResponse` reuses the identical shape rather than a third,
  drifting copy — `GetAssessmentAsync`/`RecalculateAssessmentAsync` still call
  `BuildAssessmentResponse` exactly as before and are otherwise unchanged.
- Proved by `Raffa.Api.Tests.QuoteBenchmarkHistoryEndpointTests` over a real HTTP
  round trip (`InMemoryQuotesFactory`, an InMemory-swapped `QuotesDbContext`, same
  shape `InMemoryAskEngineFactory` already establishes): the tenant-header guard
  clauses, an empty-tenant `200`, the honest `InsufficientBenchmarkData` cold
  start, a prior quote's benchmark reading back identically across two separate
  requests/`HttpClient`s, and newest-first ordering.

## Insights — criticality score, priced-line negotiation, strategy pack

Task E13/F07/US01/T01 (insights-calculators; ADR-024; parent story
us-01-insights) fills in `Raffa.Insights` (scaffolded by
E13/F01/US01/T01) with three pure calculators, fed by DTOs only — the
same determinism convention (Appendix C rule 6) every calculator in this
backend already follows. Task E31/F02/US01/T01 (point-ranker; NW-96; ADR-024
w19 cl. 23) adds a fourth:

- `Criticality.CriticalityScoreCalculator.Calculate` — product spec §12.1/
  R-PORT-01's deterministic, explainable 0-100 portfolio-criticality
  score: five weighted components (renewal urgency, risk severity, spend
  weight, savings potential, open critical facts), each its own `Score`/
  `Weight`/`Explanation`, summing to the total (AC-1). Weights are
  `Raffa.Insights.InsightsOptions` (config section
  `Insights:Criticality`, council default 0.30/0.20/0.20/0.20/0.10,
  validated to sum to 1.0 at construction). A contract whose tracked
  critical facts (recorded risks + priced lines with a unit price) are
  all below the 0.8 confidence threshold is flagged "validate first" in
  its own component explanation and its score is raised, not hidden
  (AC-2).
- `Negotiation.PricedLineNegotiationCalculator.Compute` — generalizes
  `Raffa.Quotes.Application.Strategy.NegotiationStrategyCalculator` from
  a quote line to any `Raffa.Benchmark.Contracts.PricedLine` (a contract
  line item included), producing the same opening target/acceptable
  range/walk-away threshold plus the same seven canonical levers (AC-3).
  Unlike the Quotes calculator, levers are never empty: a priced line with
  no benchmark match still gets the levers that do not need one (volume,
  term, quarter-end, bundle), with "insufficient market data" stated for
  the numeric targets (AC-4) — mirrors R-CMP-01 AC-2's identical rule for
  the benchmark-comparison case.
- `Strategy.StrategyPackBuilder.Build` — the renewal-strategy pack for one
  contract, in the council-decided section order: **When you must move**
  (dates, a passed deadline stated as passed, never hidden) → **Where you
  can push** (levers across every priced line) → **Targets** (opening/
  range/walk-away per priced line, "insufficient market data" when no
  band exists) → **Next steps** (the four tracker steps verbatim from
  `raffa-v2/app.jsx`'s own `stepDefs`, mirroring the Contract 360
  tracker) — plus `openWeakFacts` and a citation key for every number
  (`fact:<contractId>:<field>` / `market:<recordId>` / `calc:<name>`,
  `Raffa.Insights.Contracts.InsightsCitationKeys`).
- `Application.NegotiationPointRanker.Rank` — the six canonical negotiation
  points (above-band price, uncapped/high liability, auto-renew+short
  notice, SLA/credits, term/volume, payment terms; AC-2 order), emitting a
  point **only** when it is grounded in a stored fact, a clause, an
  assessed risk or a benchmark band — never the generic seven-lever dump
  the calculator above still produces for the older `RenewalStrategy`
  pack (epic-31's own "Out of scope: no ungrounded '7 lever' dump"). Fed
  by `NegotiationPointInputs` (contract id, priced lines, auto-renewal +
  dates, clause/risk snapshots, payment terms) built by
  `Raffa.Api.AskCopilotService.BuildNegotiationPointsPackAsync`, the
  shared host helper that also caps the chat pack at the top three points
  ("chat top-3") while upserting the whole ranked set to
  `Raffa.Renewals.Application.RenewalNegotiationTodoService` when asked
  ("persist-all"). Proved in isolation (chat pack capped at three, the
  full five-point ranked set readable back via
  `RenewalNegotiationTodoService.GetAsync` when `persistTodos` is set,
  and a repeat call never duplicating rows) by
  `Raffa.Api.Tests.AskNegotiationPointsPackTests`, the same
  test-reachability precedent `BuildRenewalStrategyPackAsync`/
  `BuildMarketComparePackAsync` already establish. **Wired into a live
  turn by task E29/F02/US01/T01 (todo-host-upsert; NW-85/NW-97; ADR-028/
  ADR-024 w19 cl. 21)**: `AskIntent.RenewalStrategy`'s named-contract
  branch reaches it through `BuildRenewalStrategyPackAsync`
  (`persistTodos: true`), itself called by the composition
  `BuildRenewalStrategyWithEvidenceAsync` — so a real Q3 ask durably
  upserts before the answer is composed, proved end to end by
  `Raffa.Api.Tests.AskRenewalStrategyTodoUpsertTests` (upsert-before-
  answer, idempotent re-ask, a ticked Done row surviving a repeat ask).
  Epic-31/feature-01 (q3-route, NW-95) built that switch arm's final
  corpus shape (tenant/market/raffa corpora). **Feature-03 (q3-persist,
  NW-97; ADR-024 w19 cl. 21/ADR-028) adds the server-injected
  `/renewals?select={contractId}` navigate action** on top of it —
  `AskCopilotService.BuildInDomainReplyAsync`'s own `isQ3PersistTurn`
  branch, built through `CapabilityRouting.ResolveActions` /
  `BuildHref`'s `RenewalsKey` case (never from the model's own
  `composed.Value.Result.ActionKeys` —
  `Raffa.AiGateway.Fixtures.FixtureAiGateway.AnswerFromPack` never
  populates one for a pack-JSON turn in the first place), so the reply's
  own `actions[]` carries the deep-link the moment ranking upserts,
  never twice on a repeat ask (`Concat(...).Distinct()`, the same
  "Record equality" de-dup `ResolveActions` already performs
  internally). Proved by
  `Raffa.Api.Tests.AskQ3RenewalsDeepLinkActionTests`.

**Where the shared `PricedLine` input lives, and why**: R-STR-02
generalizes `NegotiationStrategyCalculator` to a shared priced-line input.
`Raffa.Insights`' own allow-list is `[SharedKernel, Benchmark]` — the
same one `Raffa.Quotes` already has — so neither module can reference
the other (`Raffa.ArchitectureTests.DependencyDirectionTests`); the one
project both already see is `Raffa.Benchmark`. `PricedLine` therefore
lives at `Raffa.Benchmark.Contracts.PricedLine`, next to
`BenchmarkDistribution` — the only new file this task adds to
`Raffa.Benchmark`. The opening-target/walk-away-threshold "step an
already-known range" arithmetic both calculators must reproduce
bit-for-bit also lives there, as
`Raffa.Benchmark.Contracts.PricedLineNegotiationMath.StepRange` — the
smallest possible shared surface: `NegotiationStrategyCalculator.Compute`
now calls it too (its own public signature, levers and abstain conditions
are otherwise unchanged — every existing
`Raffa.Quotes.Tests.NegotiationStrategyCalculatorTests` assertion still
holds, decimal arithmetic being exact). The earlier "raw distribution ->
recommended range" step stays each calculator's own independent
arithmetic (`PricedLineNegotiationCalculator` mirrors, rather than calls,
`Raffa.Quotes.Application.Assessment.TargetSavingCalculator`'s formula)
because `NegotiationStrategyCalculator.Compute`'s own signature still
takes a pre-computed `LineTargetSaving`, not a raw
`BenchmarkDistribution`, and no task has changed that.

`Raffa.Api.InsightsEndpointExtensions` composes `GET
/api/insights/criticality` and `GET /api/contracts/{id}/strategy` from
`PortfolioQueryService`/`Contract360QueryService` (Documents/Contracts),
`RenewalEngine`/`PriorityScoreCalculator` (Renewals) and
`SavingsOpportunityService` (Savings) — the one project allowed to
reference every module. Task E13/F06/US01/T01 (ask-engine) maps both routes
in `Program.cs` (see the HTTP surface table above); every
composition/mapping method on that class is also `public static` so it can
be (and is) unit-tested directly with hand-built fakes from
`Raffa.Insights.Tests` — no database, no `WebApplicationFactory` — which
is why that test project also references `Raffa.Api` (a test-project
reference is not constrained by `DependencyDirectionTests`, which only
inspects `src/` projects). `AskCopilotService`'s own `PortfolioStrategy`/
`RenewalStrategy` intents narrate the identical `CriticalityScoreCalculator`/
`StrategyPackBuilder` output these two HTTP routes return — one calculation,
reachable both ways. **Per-contract benchmark matching is wired** (task
E21/F03/US01/T01, NW-62, for `GET /api/contracts/{id}/strategy`; task
E28/F01/US01/T01, NW-82, for `AskCopilotService.BuildRenewalStrategyPackAsync`/
`BuildMarketComparePackAsync`; task E31/F02/US01/T01, NW-96, for
`BuildNegotiationPointsPackAsync`): `Raffa.Api.BenchmarkKeyResolution` resolves
the one `(supplier name, geography)` key every path queries with — supplier
name through `ISupplierNameLookup`, geography from the caller's own
`Raffa.Identity.Workspace.Domain.WorkspaceTenant.Country` (ISO 3166-1
alpha-2) — then the async `InsightsEndpointExtensions.ToPricedLines`
overload calls `IBenchmarkService.GetBenchmarkAsync` per priced line and
fills `PricedLine.Benchmark`/`SampleSize`/`AdapterName`/`AsOf` from a
sufficient result (ADR-024 w17 clause 7, "one resolution per screen", now
also Ask's own rule). `Contract` still carries no dedicated geography
column — the workspace country is the honest proxy, not a per-contract
one (ADR-024 w17 clause 8) — and an incomplete key (no `SupplierId`, an
unresolved name, or no workspace country) still leaves `PricedLine.Benchmark`
`null`, so the pack states "insufficient market data" rather than
fabricating a number; the two HTTP routes and every Ask call site narrate
identically for the same contract because they resolve the same key and
call the same overload.

## R4 demo smoke test

The automated proof of task E05/F04/US01/T01 (r4-integration) is `dotnet test` —
`Raffa.IntegrationTests.R4EndToEndTests` (AC-1 "Upload quote -> line items -> benchmark match ->
market assessment -> target range -> negotiation strategy", AC-2 "User can correct SKU matching
before accepting assessment", AC-3 "Record final outcome -> realized savings tracked" — the whole
Quote Check Day-1 chain, driven against one real, uploaded quote through the real host, for the
first time; every earlier Quote Check task only proved its own segment in isolation) and
`R4CrossTenantIsolationTests` (the same AC-1/AC-3 surface — `GET /api/quotes/{id}/assessment`,
`POST /api/negotiations/outcomes` — proven isolated across two tenants, the same "drive the whole
path across two tenants through the real host" value-add `R1CrossTenantIsolationTests`/
`R2CrossTenantIsolationTests`/`R3CrossTenantIsolationTests` already established). Run just these:

```bash
cd backend
dotnet test Raffa.slnx --configuration Release --filter "FullyQualifiedName~R4"
```

Running this test end to end (rather than each Quote Check task's own narrower, per-segment test)
surfaced two real gaps — see "Market Assessment" and "Negotiation Strategy" above for the full
account: `MarketAssessmentService`/`NegotiationStrategyService` never opened their own
`ITenantContext.BeginScope`, so `GET /api/quotes/{id}/assessment` would 404 for every real quote
against a real, unprivileged-role Postgres connection (every deployed environment); and that same
endpoint never actually serialized `LineMarketAssessment.Quantity` as `quantity`, despite
backend/README.md's own HTTP surface table documenting it since task E05/F02/US01/T02. Both are
fixed; both are now covered by this task's own tests.

To manually smoke-test the same path against a running `dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -H 'X-User-Id: smoke-test@acme.example' -d '{"name":"Smoke Test Co"}' | jq -r .id)

QUOTE=$(curl -s -X POST "$API/api/quotes" -H "X-Tenant-Id: $TENANT" \
  -F "file=@quote.pdf;type=application/pdf" \
  -F "supplier=Salesforce" -F "currency=USD" -F "geography=US" -F "purchaseDate=2026-07-01" \
  | jq -r .id)

# processingStatus/lineItemCount/unmatchedSkuCount reflect QuoteExtractionPipeline's own run
# (hybrid parse -> extract -> normalize -> SKU-match) -- POST /api/quotes runs it synchronously.
curl -s "$API/api/quotes/$QUOTE/assessment" -H "X-Tenant-Id: $TENANT" | jq .
```

Honest caveats: `IAiGateway` still binds to `FixtureAiGateway` (no live Foundry endpoint exists yet,
ADR-004), whose `ExtractAsync` always returns an empty `{}` — a real `demo` upload lands zero line
items, so nothing on it will ever match a benchmark. This smoke path proves the *pipeline/endpoint
wiring* end to end (upload responds, the assessment route resolves and returns 200 for a real,
owned quote); `R4EndToEndTests` proves the actual matching/target-saving/negotiation-strategy/
outcome-capture arithmetic against a scripted gateway that returns real, schema-shaped facts, the
same division of labour "R1 demo smoke test" above already documents for contracts. Negotiation
strategy generation (`NegotiationStrategyService.GenerateAsync`) and identifying a new
`SavingsOpportunity` (`SavingsOpportunityService.CreateAsync`) both still have no public HTTP route
— see "Negotiation Strategy"/"Savings Intelligence — trackable SavingsOpportunity" above for why —
so neither is curl-able yet; `R4EndToEndTests` proves both directly against the real host's own
container instead, the same "no dedicated route exists yet" convention this backend has used since
R2.

## Containers and CI

`.github/workflows/backend.yml` (path-filtered to `backend/**`):

1. `dotnet restore / build / test` on `Raffa.slnx` (required status check).
2. On merge to `main` (or `workflow_call` for demo): Azure login via OIDC
   (`raffa-sp-<env>`), `az acr build` of
   `src/Raffa.Api/Dockerfile` and `src/Raffa.Worker/Dockerfile`, then
   `az containerapp update` of `ca-raffa-<env>-api` / `-worker`.

Images are tagged with `github.sha`. Container Apps listen on **8080**
(`ASPNETCORE_URLS=http://+:8080`). Deployed connection strings are
environment variables (`ConnectionStrings__IdentityWorkspace`,
`DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Storage`) — never
committed.

Image pull uses this environment's workload identity (`AcrPull` on
`modules/acr`, `registry {}` on `modules/containerapps`). Confirm the
HCP VCS apply on `raffa-<env>` before the first `az containerapp update`
to that registry, or the revision fails with ACR `UNAUTHORIZED`.

## Dependency direction (ADR-002)

Allowed Raffa project references (enforced by
`tests/Raffa.ArchitectureTests`):

| Module | May reference |
|--------|----------------|
| Domain modules | `SharedKernel` only, plus `AiGateway` (Documents, Chat, Market) or `Benchmark` (Renewals, Savings, Quotes, Insights); `Market` is the one module allowed both `AiGateway` and `Benchmark` |
| `AiGateway` / `Benchmark` implementations | provider SDKs — when they exist; domain modules see the interface only |
| `Raffa.Api` / `Raffa.Worker` / `Raffa.Tools` | all modules (composition roots). Azure Blob SDK is host-only. `Raffa.Tools` joins `AllRaffaProjects` so a later domain module referencing the console is a violation, not silently legal; it is **not** in the domain-module array or the allow-list dictionary |

Do not add a domain → domain or domain → Azure SDK project/package
reference to make a task compile. Put the adapter in the host or behind
the gateway/service project. When two domain modules with no shared
reference need the identical shared input/arithmetic (`Raffa.Quotes` and
`Raffa.Insights` both need a priced-line negotiation calculation, task
E13/F07/US01/T01), put the shared DTO/arithmetic in a module both already
allow-list — see "Insights" above for the worked example
(`Raffa.Benchmark.Contracts.PricedLine` / `PricedLineNegotiationMath`).

## Ask Raffa V2 — operator jobs, golden set and acceptance (task E13/F11/US01/T01)

Everything a new engineer needs to run the V2 flows end to end against a
deployed environment. The screen-by-screen acceptance list lives in
[`../docs/ask-v2-acceptance.md`](../docs/ask-v2-acceptance.md) (A1–A14, one
exact command or click-path and one observable pass condition per row).

### Order of operations on a fresh environment

| # | Step | How |
|---|------|-----|
| 1 | Deploy + apply schema (ADR-021) | merge to `main` (`dev`), or `git tag demo-v<N> && git push` (`demo`) |
| 2 | Seed the Day-1 fixture rows | Actions → **seed-demo-fixture** (`target_environment`) |
| 3 | Seed the shared market corpus | Actions → **seed-market-intelligence** (`target_environment`) |
| 4 | Verify a tenant's corpus (report; does not mutate) | Actions → **verify-tenant-corpus** (`target_environment`, `tenant_id`) |
| 5 | Walk A1–A14 | `docs/ask-v2-acceptance.md`, plus `web/e2e/v2.spec.ts` |

### `seed-market-intelligence` — the market feed ingestion job (R-MKT-03)

`.github/workflows/seed-market-intelligence.yml`, `workflow_dispatch` /
`workflow_call`, input `target_environment` (`dev` | `demo`). Same OIDC login,
resource-group resolution and `postgres-connection` Key Vault fetch as
`seed-demo-fixture.yml`; the `demo` GitHub Environment's required reviewers
gate it exactly like a deploy.

It runs this host's own one-shot operator command
(`Raffa.Worker/Commands/IngestMarketCommand.cs`) on the runner, against that
environment's database:

```bash
ConnectionStrings__DocumentsContracts=<npgsql> \
ConnectionStrings__Audit=<npgsql> \
ConnectionStrings__Renewals=<npgsql> \
ConnectionStrings__Market=<npgsql> \
  dotnet run --project backend/src/Raffa.Worker -- \
    ingest-market --feed backend/fixtures/market-intelligence.mock.json
# Ingested feed 'mock-2026.09.2': 1456 inserted, 0 updated, 0 unchanged.
```

The first three connection strings are what `Raffa.Worker/Program.cs`
fail-fasts on when it builds the host; only the fourth is what the command
itself needs. All four are the same database (ADR-003: separate schemas, one
server). `--feed` is an informational label — the mock provider reads its
fixture from an embedded resource, not from that path (see
`IngestMarketCommand`'s own doc comment).

The job then **runs the command a second time and fails unless it reports
`0 inserted, 0 updated`** (R-MKT-03 AC-1, "re-running the job with the same file
changes nothing"), and verifies that `market_record` has ≥ 60 rows (R-MKT-02),
that `market_embedding` is non-empty, and that neither table has a `tenant_id`
column (R-MKT-03 AC-2 / ADR-011's epic-13 amendment — the market corpus is
shared and read-only for every tenant, and would be a defect if it were
tenant-scoped).

`AiGateway__Endpoint` is deliberately **not** set on the job: with it unset the
gateway DI swap keeps the fixture `embed` role, which is deterministic and
free, and the numbers the job seeds (`market_record`) never come from a model
anyway. Set `AiGateway__Endpoint` / `AiGateway__ProjectName` on the job to embed
the market notes with Foundry instead.

### `verify-tenant-corpus` — report documents, %PDF embeddings, supplier gaps (R-DOC-07, R-SUP-03)

`.github/workflows/verify-tenant-corpus.yml`, `workflow_dispatch` only, inputs
`target_environment` and `tenant_id`. Wave w16 (NW-31, task E18/F03/US01/T01)
deleted the previous mutating `reprocess-tenant-documents.yml` (it called the
API with client-asserted role headers and 401'd after NW-05). This job
**reports; it does not mutate**. Wave w17 (NW-73) **re-adds**
`reprocess-tenant-documents.yml` as a runner console — see the next section.
An Admin can still resubmit **one** document through the product
(`POST /api/documents/{id}/reprocess` → `202` → Worker).

What the job does, in order:

1. Detects whether that environment runs a live Foundry gateway (reads
   `AiGateway__Endpoint` off the Container App) — this decides how strict the
   OCR check can be.
2. Enumerates the tenant's `document` rows (`id`, `file_name`, `document_type`,
   `processing_status`) over psql with `SET app.tenant_id` (RLS stays on).
3. Prints a **named worklist** of documents still needing a reprocess (`Failed`,
   or at least one embedding still starting with `%PDF`). That predicate is
   **also** the console's input set — the two jobs must not drift.
4. Verifies by SQL:
   - **`left(chunk_text, 4) = '%PDF'` must be 0** — R-DOC-07 AC-1. This is a
     hard failure: an embedding that is raw bytes means Ask would cite bytes as
     evidence.
   - The `[fixture-ocr: …]` placeholder count is a **failure** when the
     environment has `AiGateway__Endpoint` set (the `ocr` role fell back), and a
     **warning** otherwise (expected fixture behaviour).
5. Reports the tenant's contracts that still have `supplier_id IS NULL`
   (R-SUP-03). A *report*, not a gate: SQL cannot know whether a given document
   actually names a supplier, so failing here would fail honestly supplier-less
   contracts (an unsigned SOW, a price list). Name the supplier through the
   review screen (`/documents?review=<documentId>`) for each one that should
   have had one.

### Bulk whole-tenant reprocess (`Raffa.Tools`, wave w17 NW-73)

`.github/workflows/reprocess-tenant-documents.yml`, `workflow_dispatch` only,
required `tenant_id`, `target_environment` **`dev` only** (ADR-022 w17 clause
6b). It runs `dotnet run --project backend/src/Raffa.Tools` on the GitHub
runner — not a Container Apps Job, not an API POST loop. The console is a
**third composition root**: no table, no endpoint, no business rule,
referenced by nothing. It calls `DocumentReprocessService.ReprocessAsync` per
document on the **same worklist predicate** `verify-tenant-corpus` already
computes (`Failed`, or an embedding still starting with `%PDF`).

Credential and tenancy rules (ADR-009 w17 clause 1): three-argument
`DocumentsContractsDbContextOptions.Configure` inside `BeginScope` so
`app.tenant_id` is set; never a raw `NpgsqlConnection` or a hand-written
`SET`; one tenant per run from an explicit GUID; zero rows under a valid
tenant exits non-zero; the application's own `postgres-connection` secret,
never a superuser or `BYPASSRLS`. Actor is the fixed literal
`system:bulk-reprocess`; `--requested-by` rides in `Detail`, never in
`Actor`. `AZURE_CLIENT_ID` must **not** be set on the runner.

⚠ Confirm the `raffa-dev` HCP VCS apply of the topic-scoped
`Azure Service Bus Data Sender` grant (PR #141) **in the HCP UI** before the
first dispatch. A run against a missing Send grant deletes chunks, commits,
requeues nothing and writes no audit row (ADR-016 w17 clause 44). Walk
A17-S2 in `docs/waves/w17-acceptance.md`. The 20-file page-render
measurement on `dev` runs **before** the first whole-tenant reprocess
(ADR-005 w17 §24).

### AI golden set (`Raffa.AiEval`) — how it runs and how to filter it

`backend/tests/Raffa.AiEval/Raffa.AiEval.csproj` is a member of
`Raffa.slnx`, so **`.github/workflows/backend.yml` already runs it** through
its existing `dotnet test Raffa.slnx` step. There is no `--filter` step in
CI and none is wanted: a numeric-guard intervention or a kind mismatch fails
the `build + test` job like any other test failure, which is the required
status check on `main` (ADR-014).

Locally, the filters an engineer actually needs:

```bash
# The whole solution, golden set included — what CI runs.
cd backend && dotnet test Raffa.slnx --configuration Release

# Only the golden set, by project (works whatever traits the suite carries).
dotnet test backend/tests/Raffa.AiEval/Raffa.AiEval.csproj

# Only the golden set, by trait, from the solution — the suite marks its cases
# [Trait("Category","AiEval")] (task E13/F06/US01/T02).
dotnet test backend/Raffa.slnx --filter "Category=AiEval"

# Everything except the golden set — the fast inner loop.
dotnet test backend/Raffa.slnx --filter "Category!=AiEval"

# By fully-qualified name, if the trait is not there yet.
dotnet test backend/Raffa.slnx --filter "FullyQualifiedName~Raffa.AiEval"
```

The set runs against the **fixture** gateway so it is reproducible and free;
the on-demand Foundry run is manual (`AiEval__UseFoundry=true` with
`AiGateway__Endpoint` set) and never part of CI.

### Known gap that blocks the first V2 promotion

`Raffa.Api/Program.cs` fail-fasts on `ConnectionStrings:Suppliers` (task
E13/F06/US01/T01 wired `AddSuppliersProductsModule`), but
`infra/modules/containerapps/main.tf` injects `IdentityWorkspace`,
`DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Chat` and
`Storage` — **not** `Suppliers`. The deployed API will not boot until that env
block is added (same `pg-cs` secret as its neighbours). Recorded in
`infra/README.md` and in `docs/ask-v2-acceptance.md`'s "Known gaps" table; it is
an `infra/` change, outside this task's file scope.

## Ask Raffa — AI evaluation set (golden set, task E13/F06/US01/T02)

`tests/Raffa.AiEval` is the golden set `inputs/requirements.md` R-EVD-03
and spec §15.3 call for: **70 questions** (Italian and English) across
**three tenant fixtures**, each running the whole V2 Ask engine end to end
over HTTP — `POST /api/chat/query` → `Raffa.Api.AskCopilotService` →
domain gate → intent planner → context pack → persona prompt → both guards
→ the §6 reply — and each asserting the reply's `kind`, its citation
corpora, the calculator/pack numbers **verbatim**, the absence of engineer
chrome, and that every action href resolves to a real capability-catalog
route.

```bash
# The whole set (this is also what `dotnet test Raffa.slnx` runs).
dotnet test backend/tests/Raffa.AiEval

# Just the eval, from a full-solution run.
dotnet test backend/Raffa.slnx --filter "Category=AiEval"

# One question, by its golden-case id.
dotnet test backend/tests/Raffa.AiEval \
  --filter "DisplayName~seeded-structured_fact-120-days-en"
```

**Deterministic by construction.** No Foundry endpoint, no network, no
Docker: the run substitutes `FixtureAiGateway` (whose pack-aware
`AnswerAsync` echoes the first five pack keys and copies their values
verbatim), pins `IClock` to `2026-09-09T12:00:00Z`, and swaps the four
DbContexts the engine touches — `DocumentsContractsDbContext`,
`SuppliersDbContext`, `SavingsDbContext`, `ChatDbContext` — onto EF Core
InMemory (`TenantFixtures/AskEvalHost.cs`). Everything else in the request
path is the shipped host, unchanged. Market data comes from the real
checked-in mock feed (`backend/fixtures/market-intelligence.mock.json`),
so the market numbers in the expectations are the ones a demo would show.

**The cases are data.** They live in `tests/Raffa.AiEval/golden/*.json`,
one file per tenant fixture, and are extended by editing JSON — no C#:

| Fixture | What it holds | What it is for |
|---|---|---|
| `empty` | nothing at all | the upload-invite half of every gate label, R-SYS-04's availability replacement, R-PORT-02 AC-2 |
| `seeded` | Salesforce / Microsoft / AWS / DocuSign, validated, mirroring the V2 prototype's own `CONTRACTS` | every "answer" case: dates, spend, renewal windows, market comparison, strategy, criticality |
| `needs-review` | one Salesforce MSA still `needs_review`, its end date / notice date / spend not yet accepted | R-CMP-03, document status, and the proof that an un-validated fact is never quoted |

Each case names its intent (the fixed R-ASK-02 / R-ASK-03 vocabulary), its
question, its expected kind, and the citations or capability route it must
produce. Five further tests assert set-wide invariants: at least 40
questions across all three fixtures in both languages, all four reply kinds
and all fourteen intents covered, **zero guard interventions anywhere**
(R-EVD-03's headline — read from the engine's own
`abstainGuardIntervened` audit field, not inferred from the reply kind),
and that the report was written.

### Reading the report

Every run writes `tests/Raffa.AiEval/reports/last-run.md` (git-ignored
via `backend/.gitignore`, regenerated each time, written **before** the
first assertion so it exists even when the suite is red). Read it top to
bottom:

1. **Header** — which mode the run used and the pinned clock, then the
   counters that matter: cases, passed, *passed against a recorded engine
   gap*, failed, and **guard interventions (must be 0)**.
2. **Coverage by intent / by tenant fixture and language** — where the set
   is thin. A new intent with one case is visible here.
3. **Tenant fixtures** — the seeded portfolios, so an expected number can
   be traced to the row it came from without opening any code.
4. **Per-case verdicts** — one row per question: expected vs observed kind,
   citation count and corpora, AI Gateway calls made (a greeting must show
   `0`), guard status, verdict.
5. **Failures** — every failed check plus the reply that produced it.
6. **Engine gaps this run exposed** — see below.
7. **Every reply, verbatim** — each reply in full in a collapsed block with
   its citations, actions and audit action. This is the section to read
   before signing off a demo: the tables prove the replies are *grounded*,
   this one shows whether they are *worth reading*.

### `pass (known gap)` — what it means

A case whose reply matched neither its requirement nor a defect would fail.
A case may instead **declare** one named, already-reported divergence
(`knownGap` in its JSON, with an id from `GoldenSetKnownGaps`), and then it
passes if the reply matches *either* the requirement (the gap has been
fixed — it simply stops appearing in the report, no JSON edit needed) *or*
exactly that recorded deviation. Any third behaviour still fails, so a
declared gap tolerates one named deviation and never hides a regression.
The always-applied checks — zero guard interventions, no engineer chrome,
audit shape, catalog-only hrefs — are **never** forgiven by a gap.

This exists because the golden set is owned by a task that must not change
the engine: a defect it finds is reported, not absorbed into the
expectation as though it were the requirement. `GoldenSetKnownGaps` (in
`GoldenSet.cs`) documents each one against the requirement it misses, and
`GoldenSetTests.Every_known_gap_is_a_documented_gap` refuses any id that is
not listed there — so nobody can quietly widen what the set forgives.

### Running it against Foundry (manual, OQ-askv2-009)

```bash
# az login as an identity listed in infra/environments/dev ai_operator_principal_ids
# (DefaultAzureCredential picks the CLI token up); deployment names = the
# ModelId values Terraform publishes for that environment.
AiEval__UseFoundry=true \
AiGateway__Endpoint=https://aisvc-raffa.cognitiveservices.azure.com/ \
AiGateway__Models__Classify__ModelId=gpt-5.4-nano-dev \
AiGateway__Models__Extract__ModelId=gpt-5.4-nano-dev \
AiGateway__Models__Answer__ModelId=gpt-5.4-nano-dev \
AiGateway__Models__Embed__ModelId=text-embedding-3-small-dev \
dotnet test backend/tests/Raffa.AiEval
```

With `AiEval__UseFoundry=true` the harness leaves the host's own
`IAiGateway` registration alone, so `AddAiGatewayModule` resolves
`FoundryAiGateway` when `AiGateway:Endpoint` is set and the fixture
otherwise (R-AI-01) — the tenant stores stay in-memory either way. A live
model does not restate a number identically twice, so the verbatim-number
checks become **advisory** in this mode; everything that must hold
regardless of the model does not move: zero guard interventions, the reply
kind, no engineer chrome, catalog-only action hrefs, one audit row per
turn. The report header names the mode, so a reader never has to guess
which run produced the numbers in front of them. Anything other than a
literal `true` (unset, blank, `0`, a typo) keeps the deterministic fixture
run — CI can never be turned into a billed model run by accident.
