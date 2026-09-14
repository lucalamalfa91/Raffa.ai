---
id: E18/F01/US01/T01
type: task
story: us-01-api-validates-the-token
wave: w15
status: live
target_repo: raffa-backend
---

# task-01-api-validates-the-token — JWT bearer, one identity seam, and the claims branch deleted

## Coding objective

Wire the authentication half of ADR-010 that has been designed and provisioned
since R0 and never consumed. Add `Microsoft.Identity.Web` / JWT bearer to
`Raffa.Api`, bind the four `AzureAd__*` keys the Terraform task publishes, and
add **one** middleware between `builder.Build()` (`Program.cs:230`) and the first
map (`:232`) — there is currently **no `app.Use*` call at all** in that gap, so
nothing is reordered. The middleware **resolves and never rejects**.

Then collapse the identity surface. `ICallerIdentity`
(`Infrastructure/CallerIdentity.cs:58-73`) is already the only reader of
`X-User-Id` and all eight of its consumers already map absence to 401, so it
becomes the seam that returns the validated `oid`. The ~19 `X-Tenant-Id` parse
sites — the copy-pasted block whose canonical form is
`DocumentsEndpointExtensions.cs:556-567`, repeated across a dozen endpoint files
— collapse onto one request-scoped `ICallerContext` that verifies the selector
against the token subject's **live membership** and answers **404** on failure.

Finally delete NW-06's residue: `WorkspaceRoleResolver.ResolveAsync` collapses to
`ResolveMembershipRoleAsync`, and the claims branch, the two header constants
(`:48-49`), the unreferenced `TryResolveHeaderRole` (`:89-102`, zero call sites)
**and the doc comment at `:20-22`** go together.

## Parent story AC covered

- AC-1 … AC-10 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/Raffa.Api.csproj` | the JWT bearer / `Microsoft.Identity.Web` package |
| `backend/src/Raffa.Api/Program.cs` | `AddAuthentication` + `AddJwtBearer` with the ADR-010 §1 parameters; `UseAuthentication` / `UseAuthorization`; the identity middleware. **Single-writer of this file in phase 1** |
| `backend/src/Raffa.Api/appsettings.json`, `appsettings.Development.json` | the `AzureAd` section shape; **every key optional at bind time** so the API boots with them absent |
| `backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs` | returns the validated `oid`; the `X-User-Id` read (`:58-73`) is **deleted**, not made conditional |
| `backend/src/Raffa.Api/Infrastructure/CallerContext.cs` | **new** — the request-scoped tenant selector: membership-verified, 404 on failure, `BeginScope` after the check |
| `backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs` | `ResolveAsync` → `ResolveMembershipRoleAsync`; delete the claims branch (`:60-65`), the header constants (`:48-49`), `TryResolveHeaderRole` (`:89-102`) **and the doc comment at `:20-22`** |
| `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs`, `ContractsEndpointExtensions.cs`, `RenewalsEndpointExtensions.cs`, `QuotesEndpointExtensions.cs`, `SavingsEndpointExtensions.cs`, `SavingsKpiEndpointExtensions.cs`, `PortfolioEndpointExtensions.cs`, `InsightsEndpointExtensions.cs`, `NegotiationsEndpointExtensions.cs`, `ConversationsEndpointExtensions.cs`, `MarketEndpointExtensions.cs`, `ChatEndpointExtensions.cs`, `WorkspaceEndpointExtensions.cs`, `WorkspaceMembersEndpointExtensions.cs`, `WorkspaceInvitesEndpointExtensions.cs`, `InvitationsEndpointExtensions.cs`, `AuditEndpointExtensions.cs`, `CapabilitiesEndpointExtensions.cs` | the copy-pasted tenant parse block is replaced by the seam. **All `*EndpointExtensions.cs` are single-writer to this task in phase 1** |
| `backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs` | the shipped contract ADR-010's w14 footer `:109-112` names as the seam to amend |
| `backend/tests/Raffa.Api.Tests/`, `backend/tests/Raffa.IntegrationTests/` | the tests below, including **S-T17(c)** and **S-T18** |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-05 (backend half), NW-06`.

Decision rows: `reports/architecture/waves/w15.md` — **NW-05**
(security-architect, software-architect, cloud-architect and delivery-manager
cells) and **NW-06** (security-architect and client-architect cells).

- **Architecture decisions in force**: **ADR-010** w15 §1–§4; **ADR-022** w15
  footer; **ADR-025 §I** and Rule B1; **ADR-009** w15 §5; **ADR-026 §D1**;
  **ADR-016** w15 clause 15.
- **The audience is the client id.** `identity/main.tf:75` sets
  `requested_access_token_version = 2`, so `aud` **is** the client id —
  `outputs.tf:13` says so verbatim, while `:18` calls `api_identifier_uri` the
  *alternate* identifier a client **requests**. A task that wires the URI as the
  audience sees **every** token rejected, and the tempting repair is
  `ValidateAudience = false`. It is forbidden, and **S-T18 asserts the flags are
  `true`**.
- **Defence in depth on the issuer is mandatory here, not optional**: the SPA's
  runtime-config validator (`scripts/write_web_runtime_config.py:80-83`) checks
  the **prefix only**, so `…/common` passes it today.
- **Sizing, measured — do not split this task.** 19 parse sites across 12 files
  and **74 `X-Tenant-Id` occurrences across 17 files**. The intake's "~12 blocks"
  is an undercount. One task, or two identity regimes run live at once. The
  blocks are copy-pasted, so they are one edit, not seventeen — this is the
  narrowing the intake pre-authorised (§5, narrowing #2).
- **Fail closed, never crash closed.** With the keys absent the authenticated
  routes answer 401 and **no header fallback is re-added** — that fallback *is*
  the vulnerability NW-05 removes. But the API must still **boot**: a boot-crash
  loop costs the wave its only acceptance environment and is indistinguishable
  from a bad image, while a 401 window self-heals the moment Terraform rolls the
  revision.
- **No `Authentication__Enabled` kill-switch**, in any environment. A key that
  turns authentication off is a security control in the hands of whoever can run
  an apply, and flipping it back needs another operator-confirmed HCP apply.
  Rollback is a container image revert.
- **This is the change that can lock everyone out of `dev`.** Web-before-backend
  ordering was **refused with evidence** (two workflows, one push, no
  cross-workflow dependency; the window is symmetric; and the requested direction
  is the one that can land writes attributed to nobody). The window is named and
  bounded, backend-first is the cleaner direction, and the acceptance walk starts
  only when **both** deploys are green.
- **Known breakage, deliberately neither repaired nor edited**:
  `reprocess-tenant-documents.yml` authenticates with `X-User-Id` + both role
  headers (`:203-206`, `:254-257`), so from this merge its first authenticated
  call — `GET /api/documents` (`:202-207`) — returns 401 and the step exits 1
  with `::error::` (`:208-211`) **before any write**. No partial reprocess, no
  unattributed audit rows. **NW-31 owns that file in W16.** It is a row in the
  acceptance doc's known-gaps table.
- **Do not touch**: `.github/workflows/**` — w15's CI-YAML set is **zero files**,
  and `web.yml:204-205`'s hardcoded scopes are an **equality to preserve, not an
  edit to make**. Do not add a scope and do not rename one. Do not touch
  `web/src/**` — the client half is `E18/F01/US02/T01`. Do not touch
  `Raffa.Documents.Contracts` or `Raffa.Identity.Workspace` business logic. Do
  not interpolate the `oid` into the RLS GUC.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exit 0 — a forged `X-User-Id`/`X-Tenant-Id` with no token is **401** on every authenticated route; a valid token whose subject is not a member of the named tenant is **404**, never 403; the five public routes still answer without a token
- [ ] **S-T18**: a test asserting `ValidateAudience`, `ValidateIssuer`, `ValidateLifetime` are `true`, the audience equals the configured client id, and `ClockSkew ≤ 00:02:00`
- [ ] **S-T17(c)**: a test proving the claims branch is **gone**, not merely unreachable — a principal carrying a `roles` claim for tenant A resolves **no** role in tenant B
- [ ] `dotnet test backend/Raffa.slnx` exit 0
- [ ] `grep -rn "X-User-Id" backend/src/` returns **no match**
- [ ] `grep -rn "TryResolveHeaderRole\|X-Workspace-Role" backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs` returns **no match**
- [ ] With all four `AzureAd__*` keys unset, the API **starts** and answers 401 on an authenticated route (not a crash loop)
- [ ] `git diff --stat .github/` is **empty**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| API | forged headers + no token ⇒ 401 on every authenticated route | `backend/tests/Raffa.Api.Tests/` |
| API | the tenant selector is membership-verified; a non-member gets 404, never 403 | `backend/tests/Raffa.Api.Tests/` |
| unit | **S-T18** — the validation flags are `true` and the audience is the client id | `backend/tests/Raffa.Api.Tests/` |
| unit | **S-T17(c)** — the claims branch is deleted, so a `roles` claim grants nothing in any tenant | `backend/tests/Raffa.Api.Tests/` |
| integration | the RLS GUC is still parameter-bound with the `oid` as its value | `backend/tests/Raffa.IntegrationTests/` |
| boot | the API starts with the four keys absent and answers 401 rather than crashing | `backend/tests/Raffa.Api.Tests/` |

**A15-8's second half is not provable in CI.** Wiring `api_identifier_uri` where
the client id belongs applies cleanly, deploys cleanly and then 401s every
request in the browser. One interactive sign-in on deployed `dev` naming the
claims actually observed (`ver`, `aud`, `iss`, `oid`, `scp`, `email`) is
`E16/F04/US01/T01`'s numbered step.

## Open questions blocking this task

- **OQ-w15-ca-03** — resolved: the tenant stays a header, demoted to a selector. Not blocking.
- **OQ-w15-sec-01** — the `email` claim's presence is proved by one sign-in on `dev`, never asserted from Terraform state; the design does not depend on it. Not blocking.

## Wave-spec entry
```yaml
- id: E18/F01/US01/T01
  prompt: reports/workitems/epic-18-api-authentication/feature-01-token-identity/us-01-api-validates-the-token/tasks/task-01-api-validates-the-token.md
  produces: [api-jwt-identity]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
