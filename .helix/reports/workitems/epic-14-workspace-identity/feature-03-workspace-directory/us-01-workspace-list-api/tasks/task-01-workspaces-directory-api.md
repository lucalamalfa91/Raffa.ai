---
id: E14/F03/US01/T01
type: task
story: us-01-workspace-list-api
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-workspaces-directory-api — `GET /api/workspaces`, the validated-contract count, the workspace profile, and the phase-2 API contract

## Context

**Closes: NW-01, NW-09, NW-24** (backend halves).
Decision rows: `reports/architecture/waves/w14.md`, rows **NW-01**
(software-architect + security-architect), **NW-09** (software-architect) and
**NW-24** (product-owner + software-architect). ADRs in force: **ADR-026 §D1 /
§D2 / §D5 and implications 2, 5, 6, 7**, **ADR-025 §F.1 / §F.2 / §F.3**,
**ADR-009 w14 footer clauses 1–5**, **ADR-003 w14 footer clauses 1–5**.

Today `WorkspaceEndpointExtensions.cs:31-32` mapped only two POSTs;
`WorkspaceMembershipService.cs` has `InviteAsync` / `LinkSignInAsync` and no
"list workspaces for this user" query; the web substitutes a `localStorage`
array (`workspaceStore.ts:32,88-106`) and
`web/openapi/raffa-api.v1.json:53` documents the same gap in prose. Workspace
discovery lives in one browser, so a second browser lands on a new empty tenant
while the files sit on the old id.

**This task is phase 2's sole owner of the API contract files**
(`web/openapi/raffa-api.v1.json`, `web/src/api/generated/schema.ts`,
`web/src/api/client.ts`) and of `WorkspaceEndpointExtensions.cs`. `schema.ts` is
regenerated **wholesale** on every build (`generate-api-client.mjs:21-23`), so
two parallel edits produce a conflicting artefact, not a mergeable diff — that
is why one task holds them per phase.

**It writes the OpenAPI entry and the client method for
`GET /api/workspaces/{tenantId}/members` too**, whose handler its phase-2
sibling `E14/F04/US01/T01` implements in a different file
(`WorkspaceMembersEndpointExtensions.cs`). The contract for that operation is
fixed by ADR-026 §D3 and is reproduced below verbatim — do not invent it.

- **Architecture decisions in force**: ADR-026 (§D1 two-phase discovery, §D2 the
  count, §D5 contracts, implication 7 the generator's limits), ADR-025 (§F.1
  live-membership gating and the 50 cap, §F.3 the four bounds on the multi-scope
  exception), ADR-009 w14 footer, ADR-003 w14 footer.
- **Do not touch**: `backend/src/Raffa.SharedKernel/Tenancy/**` —
  `TenantRlsConnectionInterceptor.cs`, `ITenantContext.cs` and
  `CallerIdentityContext.cs` are `E14/F01/US01/T01`'s for the wave. This task
  **consumes** `ICallerIdentityContext` (it is on `main` by phase 2 via
  `depends_on: [rls-identity-guc]`); it never re-declares, moves or edits it, and
  it adds no DI registration for it — phase 1 already registered the ambient
  implementation; `Migrations/**` and `identity-workspace.sql`
  (`E14/F01/US01/T02` owns them for the wave — **this task adds no migration**);
  `WorkspaceMembersEndpointExtensions.cs` and
  `WorkspaceMembershipService.cs` (`E14/F04/US01/T01`, same phase);
  `WorkspaceRoleResolver.cs` (`E14/F02/US02/T01`, same phase);
  `WorkspaceInvitesEndpointExtensions.cs` (`E15/F01/US01/T01`, phase 3);
  `Program.cs` (nothing to add — `MapWorkspaceEndpoints()` is already registered
  at `:225`, ADR-026 implication 5); `WorkspacePrincipalAuthorization.cs`
  (ADR-025 §I); anything under `web/src` other than the three contract files.

## Coding objective

**1. `WorkspaceDirectoryService`** — new, in
`backend/src/Raffa.Identity.Workspace/Infrastructure/`, exposing
`ListForIdentityAsync(string identity, CancellationToken ct)`. **Two phases:**
- *Discovery*: with `app.identity_subject` set — by the identity scope **this
  task opens**, see §2 — and **no tenant scope**, select `tenant_id` from
  `workspace_user` for the matching identity. It returns **candidate tenant ids
  and nothing else**. Cap at **50** candidates — an unbounded loop keyed on a
  caller-controlled identity is a DoS shape — and audit
  `workspace.list.truncated` if the cap is hit.
- *Projection*: for each candidate, `BeginScope(tenantId)` and read normally —
  the `workspace` row, the caller's role through the existing membership join
  (`WorkspaceRoleResolver.cs:101-103,116-118` precedence for a person holding
  two memberships), and the profile columns.
- **Inclusion is gated on a live `workspace_membership` row, never on the
  `workspace_user` row** (ADR-025 Rule F.1d). A removed member keeps their user
  row by design; a user-driven list would show them the tenant they were removed
  from.
- The multi-scope loop is a **named exception** (ADR-025 §F.3) bounded by four
  rules: **sequential, never nested**; **its own connection each**; **one named
  method**; **any other endpoint doing it is a defect, not a precedent**.
  Comment all four at the method.
- This service **does not know what a contract is** (ADR-026 §D2).

**2. `GET /api/workspaces`** in `WorkspaceEndpointExtensions.cs`:
```
GET /api/workspaces
  headers: X-User-Id (through ICallerIdentity; absent -> 401)
  200 -> { workspaces: [ { id, name, createdAt, role, contractCount,
                           country?, currency? } ] }
```
- **The endpoint accepts no `X-Tenant-Id`, ever**, so N5 holds by construction
  rather than by a guard that could be forgotten.
- Empty list is **200 + `[]`**, never 404. Order `createdAt` ascending, stable.
- **No `roleLabel`** on the row — the picker derives its tag from `role`.
- **This handler opens the wave's identity scope.** Resolve the caller through
  `ICallerIdentity` (`Raffa.Api/Infrastructure/CallerIdentity.cs`, phase 1), then
  wrap the `ListForIdentityAsync` call in
  `using var identityScope = callerIdentityContext.BeginIdentityScope(identity);`
  — `ICallerIdentityContext` from
  `backend/src/Raffa.SharedKernel/Tenancy/`, created **and DI-registered** by
  `E14/F01/US01/T01` in phase 1. Both halves of the seam are `depends_on` of this
  task, so both exist here; neither existed in phase 1, which is why no phase-1
  task opens this scope. Take the **call-site scope** shape the codebase already
  enforces for tenancy (`PortfolioEndpointExtensions.cs:156`,
  `WorkspaceRoleResolver.cs:99`, `AskCopilotService.cs:132`): **add no
  middleware, no `IHttpContextAccessor` pipeline step and no `Program.cs` line.**
  There is no `UseMiddleware` in `Raffa.Api` today and this task does not
  introduce the first one.
- The identity scope and the per-candidate `BeginScope(tenantId)` are
  **independent** (`E14/F01/US01/T01` bullet 5): the identity scope stays open
  across the whole two-phase read, each projection opens and disposes its own
  tenant scope inside it. Dispose the identity scope with the handler, never
  later.

**3. The validated-contract count (NW-09).** The definition already exists on
the server: *a contract is validated iff at least one linked document is
`ProcessingStatus.Completed`* (`PortfolioQueryService.cs:191`), already served
as `contractsAnalyzedCount` (`SavingsKpiEndpointExtensions.cs:93`). Add
`CountValidatedContractsAsync(CancellationToken)` to
`backend/src/Raffa.Documents.Contracts/Application/PortfolioQueryService.cs` as
a **real SQL `CountAsync` over distinct contract ids** — there is no `CountAsync`
over `Contracts` anywhere today (`:153,183-200` materialise) and this task must
**not** copy the materialising pattern. **Compose in the host**:
`Raffa.Identity.Workspace` may reference only `Raffa.SharedKernel`
(`DependencyDirectionTests.cs:62`, enforced), so `Raffa.Api`'s handler calls the
directory service and the Documents.Contracts query service and joins them in
the response projection, inside each tenant's own scope. Precedent:
`SavingsKpiEndpointExtensions.cs:73-88`. **A SharedKernel port is explicitly
rejected.**

**4. The workspace profile (NW-24).** `POST /api/workspaces` body becomes
`{ name, industry?, country? }`. `currency` is **derived from country and
stored, never typed** — the design export's country list is exactly
Switzerland · Italy · Germany · Austria (`markup.html:56`), so the derivation is
**total over that list** and needs no fallback branch and no "unknown country"
state. Store `country` as ISO 3166-1 alpha-2 and `currency` as ISO 4217
(CH→CHF, IT→EUR, DE→EUR, AT→EUR). Validate 2/3-letter uppercase and return
**400 through the existing `Result<T>` failure path**
(`WorkspaceProvisioningService.cs:35-38`), never a raw Postgres length error. A
missing value is stored and returned **absent**, never as an invented `"CHF"`.
Enforce the product-owner's guard as a **data rule**: a workspace currency never
overrides a contract's own extracted currency — no existing contract read may
start substituting it.

**5. The phase-2 API contract.** Extend
`web/openapi/raffa-api.v1.json` with `GET /api/workspaces`, the changed
`POST /api/workspaces` response, **and** `GET /api/workspaces/{tenantId}/members`
(ADR-026 §D3, verbatim):
```
GET /api/workspaces/{tenantId}/members
  headers: X-User-Id            // NOT X-Tenant-Id: the route already carries the tenant
  200 -> { members: [ { id, email, name?, role, status } ] }
```
Then regenerate with `npm run generate:api` from `web/` and hand-write the
client methods `listWorkspaces()` and `getWorkspaceMembers(tenantId)` in
`web/src/api/client.ts`. **Constraints that are not optional** (ADR-026
implication 7): the generator parses only `responses`; there is no `components`
section and no `$ref`/`oneOf` support, so response schemas are flat inline
objects; and it checks `enum` **before** the nullable branch (`:63-65` precedes
`:72-76`), so a nullable enum silently loses its `null` — **`role` and `status`
are non-nullable strings**. Headers are attached **per method** in `client.ts`,
never by a global wrapper, and methods already omit `X-Tenant-Id` deliberately
(`:1139`, `:1147`): **send `X-User-Id` only** on the members route.

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4, AC-5, AC-6, AC-7

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceDirectoryService.cs` | new — two-phase `ListForIdentityAsync`, the 50 cap, the four bounds commented |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs` | register `WorkspaceDirectoryService` (module DI, never `Program.cs` — ADR-026 implication 5) |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceProvisioningService.cs` | accept `industry` / `country`, derive and store `currency`, validate through `Result<T>` |
| `backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs` | map `GET /api/workspaces`; **open the wave's identity scope at the call site** (`BeginIdentityScope`, §2); extend the create body; join the count in the host |
| `backend/src/Raffa.Documents.Contracts/Application/PortfolioQueryService.cs` | add `CountValidatedContractsAsync` — a real `CountAsync` over distinct contract ids |
| `web/openapi/raffa-api.v1.json` | `GET /api/workspaces`, the changed `POST /api/workspaces` 201, `GET /api/workspaces/{tenantId}/members` |
| `web/src/api/generated/schema.ts` | regenerated with `npm run generate:api` |
| `web/src/api/client.ts` | `listWorkspaces()` and `getWorkspaceMembers(tenantId)`, hand-written headers, `X-User-Id` only |
| `backend/tests/Raffa.Api.Tests/WorkspaceDirectoryEndpointTests.cs` | new — T1a, T2b, the empty-200, the 401, the profile round-trip |
| `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceDirectoryServiceTests.cs` | new — live-membership gating, the 50 cap, sequential scopes |
| `backend/tests/Raffa.Documents.Contracts.Tests/ValidatedContractCountTests.cs` | new — the count matches `contractsAnalyzedCount` |

## Context the implementer needs

- **`Contract.Status` can never define "validated"** — it is free text
  bootstrapped to the literal `"processing"` (`Contract.cs:22`,
  `StagedExtractionService.cs:157`). Use the linked-document rule, and only that.
- The `identity_self` predicate compares `lower(email)`, so **normalise the
  identity to lower case** before it reaches the GUC or the query. Discovery is
  a sequential scan at pilot scale; **do not "fix" it** with an index change or
  a policy change here (ADR-026 Consequences records why).
- The design oracle for the pick row is
  `inputs/design/prototypes/raffa-v2/screens-v2.md` §1 (`:13-21`) and
  `markup.html:63` — `"{{ seededCount }} validated contracts · CHF · eu-west"`.
  **This task only supplies the fields**; the rendering rule (segments, singular
  / zero forms, the business country name instead of `eu-west`) is
  `E14/F03/US02/T01`'s.
- `Grep` `client.ts:22-24` before writing: the name `listWorkspaces` is already
  reserved there in a comment.
- Docker Desktop does not start on the operator's machine; Postgres
  Testcontainers suites run in CI only.
- **Do not touch**: `Migrations/**`, `TenantRlsConnectionInterceptor.cs`,
  `WorkspaceMembersEndpointExtensions.cs`, `WorkspaceMembershipService.cs`,
  `WorkspaceRoleResolver.cs`, `WorkspaceInvitesEndpointExtensions.cs`,
  `Program.cs`, `WorkspacePrincipalAuthorization.cs`, any `web/src` file other
  than `api/client.ts` and `api/generated/schema.ts`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests/Raffa.ArchitectureTests.csproj`
      exits 0 — `DependencyDirectionTests` proves `Raffa.Identity.Workspace`
      still references only `Raffa.SharedKernel`
- [ ] `cd web && npm ci && npm run generate:api` exits 0 and leaves
      `src/api/generated/schema.ts` byte-identical to the committed file
- [ ] `cd web && npm run build` exits 0 (`generate:api && tsc --noEmit && vite build`)
- [ ] `cd web && npm test` exits 0
- [ ] `rg -n "X-Tenant-Id" backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs`
      returns nothing — the directory endpoint takes no tenant input
- [ ] `rg -n "BeginIdentityScope" backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs`
      shows **exactly one** call, inside the `GET /api/workspaces` handler
- [ ] `rg -n "UseMiddleware|IHttpContextAccessor" backend/src/Raffa.Api/Program.cs`
      shows nothing added by this task — the identity scope is a call-site
      `using`, not a pipeline step, and `Program.cs` stays untouched
- [ ] `rg -n "ToListAsync\(\)\.Count|\.ToList\(\)\.Count" backend/src/Raffa.Documents.Contracts/Application/PortfolioQueryService.cs`
      shows the new count method is not among them

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | **T1a** — identity `A`, member of `T1` only, gets exactly `[T1]`; `T2`'s id, name and count appear nowhere. **This is also the wave's end-to-end proof that the GUC and the `identity_self` policy agree** (`E14/F01/US01/T01` §Context defers it here): drop the `BeginIdentityScope` and RLS denies every row, so T1a returns `[]` and fails | `backend/tests/Raffa.Api.Tests/WorkspaceDirectoryEndpointTests.cs` |
| integration | **T2b** — `A` sending `X-Tenant-Id: {T2}` changes nothing; **N5** | `backend/tests/Raffa.Api.Tests/WorkspaceDirectoryEndpointTests.cs` |
| integration | 200 + `[]` for a caller with no membership; 401 with no identity | `backend/tests/Raffa.Api.Tests/WorkspaceDirectoryEndpointTests.cs` |
| integration | create with `industry` + `country` → `currency` derived and stored; a bad country → 400, not a Postgres error; absent values round-trip absent | `backend/tests/Raffa.Api.Tests/WorkspaceDirectoryEndpointTests.cs` |
| unit | a person with two memberships appears once, at the highest role; the 50 cap; scopes are sequential and never nested | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceDirectoryServiceTests.cs` |
| unit | **N8** — the count equals `contractsAnalyzedCount` for the same tenant and is a SQL `CountAsync` | `backend/tests/Raffa.Documents.Contracts.Tests/ValidatedContractCountTests.cs` |

## Open questions blocking this task

- **OQ-w14-004** — workspace profile fields. **Answered at the table** (see the
  story). No blocker.

## Wave-spec entry
```yaml
- id: E14/F03/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-03-workspace-directory/us-01-workspace-list-api/tasks/task-01-workspaces-directory-api.md
  produces: [workspaces-directory-api]
  depends_on: [rls-identity-guc, workspace-schema, workspace-bootstrap]
  effort: L
  layer: backend
  status: live
```
