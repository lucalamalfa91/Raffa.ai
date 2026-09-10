---
id: us-01
type: user-story
parent: feature-03
wave: w14
status: active
---

# us-01-workspace-list-api — the server tells a signed-in identity which workspaces it belongs to

## Story

As a **signed-in user**, I want the API to tell me which workspaces I belong to,
with the real number of validated contracts, my real role and the workspace's
country and currency, so that a second browser, a cleared profile or another
device finds my tenant instead of offering to create a new empty one.

## Acceptance criteria

- [ ] AC-1 (**N2**) `GET /api/workspaces` with identity `A` returns exactly the
      tenants `A` holds a **live `workspace_membership`** in — `200` with
      `{ workspaces: [ { id, name, createdAt, role, contractCount, country?,
      currency? } ] }`, ordered by `createdAt` ascending.
- [ ] AC-2 (**N5**, **T1a**) The endpoint accepts **no `X-Tenant-Id`, ever**. A
      crafted other-tenant header changes nothing; `T2`'s id, name and count
      appear nowhere in the response.
- [ ] AC-3 A caller who belongs to nothing gets **200 + `[]`**, never 404: "you
      have none, create one" and "the request failed" are two different screens.
      Absent identity → **401**.
- [ ] AC-4 (**N8**) `contractCount` is the number of **validated contracts** —
      contracts with ≥1 linked document in `ProcessingStatus.Completed` — the
      definition the server already holds and already serves as
      `contractsAnalyzedCount` (`SavingsKpiEndpointExtensions.cs:93`). No new
      definition is invented.
- [ ] AC-5 (**W14-A2**, backend half) `POST /api/workspaces` accepts
      `{ name, industry?, country? }`; `currency` is **derived from country and
      stored, never typed**; a missing value is stored and returned as **absent**,
      never as an invented `"CHF"`. Invalid `country` / `currency` → 400 through
      the existing `Result<T>` failure path, never a raw Postgres length error.
- [ ] AC-6 `web/openapi/raffa-api.v1.json` documents both w14 phase-2 read
      operations, `web/src/api/generated/schema.ts` is regenerated from it, and
      `web/src/api/client.ts` exposes `listWorkspaces()` and
      `getWorkspaceMembers(tenantId)`.
- [ ] AC-7 Discovery is capped at **50** candidate tenants, with
      `workspace.list.truncated` audited if the cap is ever hit.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `rls-identity-guc` (E14/F01/US01/T01) | phase 1 sets `app.identity_subject`; discovery reads under it |
| `workspace-schema` (E14/F01/US01/T02) | the `identity_self` policy and the three profile columns |
| `workspace-bootstrap` (E14/F02/US01/T01) | the membership row discovery is gated on, the `ICallerIdentity` seam, and the split route file this story writes into |

## Architecture decisions in force

- **ADR-026 §D1** — two-phase. (1) **Discovery**: with `app.identity_subject`
  set and **no** tenant scope, select `tenant_id` from `workspace_user` for the
  matching identity — candidate tenant ids **and nothing else**. (2)
  **Projection**: for each candidate, `BeginScope(tenantId)` and read normally.
  **Inclusion is gated on a live `workspace_membership` row, never on the
  `workspace_user` row** — that is what makes removal work: removal deletes the
  membership and keeps the user row for audit continuity, and the workspace
  disappears on the very next request, with no cache to invalidate and no token
  to expire (ADR-025 Rule F.1d).
- **ADR-025 §F.3** — the multi-scope loop is a **named exception** to
  `ITenantContext.cs:23-24` ("ADR-009 expects exactly one scope per request"),
  bounded by four rules: **sequential, never nested**; **its own connection
  each**; **one named method**; and **any other endpoint doing it is a defect,
  not a precedent**.
- **ADR-026 §D2** — the count is a field on the NW-01 row, not a second
  round-trip. **Composition happens in the host**: `Raffa.Identity.Workspace`
  may reference only `Raffa.SharedKernel` (`DependencyDirectionTests.cs:62` —
  enforced, not preferred), so `Raffa.Api` joins the directory service and the
  Documents.Contracts query service. Precedent:
  `SavingsKpiEndpointExtensions.cs:73-88`. **A SharedKernel port is explicitly
  rejected.**
- **ADR-026 implication 7** — the generator parses **only `responses`**
  (`generate-api-client.mjs:134-143`); request bodies and headers are
  hand-written in `client.ts`. There is no `components` section and no `$ref` /
  `oneOf` support. The generator checks `enum` **before** the nullable branch
  (`:63-65` precedes `:72-76`), so **a nullable enum silently loses its `null`**
  — therefore `role` and `status` are **non-nullable strings**.
- **ADR-003 (w14 footer)** — `country` is ISO 3166-1 alpha-2, `currency` ISO
  4217, both nullable; a workspace currency is a display default that **never**
  overrides a contract's own extracted currency, and that is enforced as a data
  rule, not only a UI rule.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | `GET /api/workspaces`, the validated-contract count, the workspace profile, and the phase-2 API contract | L | phase-2 |

## Council decisions carried into this story

- Row shape: `{ id, name, createdAt, role, contractCount, country?, currency? }`
  — **no `roleLabel`**: the picker derives its tag from `role` via the existing
  `WORKSPACE_ROLE_LABEL` map (`navItems.ts:32-35`).
- New `WorkspaceDirectoryService` in `Raffa.Identity.Workspace/Infrastructure/`,
  exposing `ListForIdentityAsync(identity, ct)`. **It does not know what a
  contract is.**
- The client method is `listWorkspaces()` — the name `client.ts:22-24` already
  reserves.
- The counting method must be a real SQL `CountAsync` over distinct contract
  ids. There is **no** `CountAsync` over `Contracts` today
  (`PortfolioQueryService.cs:153,183-200` materialise); this task introduces the
  first one and **must not copy the materialising pattern**.
- Known and accepted: `GET /api/workspaces` is **N+1 by construction** — one
  scoped count query per workspace. For the pilot that is 1–3, capped at 50.
  Collapsing it into a cross-tenant aggregate would reintroduce exactly the
  query path ADR-009 forbids, so **the N+1 is deliberate and recorded, not a
  defect to optimise away**.

## Open questions

- **OQ-w14-004** — which workspace profile fields. **Answered at the table**:
  `industry` and `country` are asked, from closed lists; `currency` is derived
  from country; "region" is a business region and does not touch ADR-006.
- **OQ-w14-005** — who may read the roster. **Answered**: any live member;
  a non-member gets **404**.
