# ADR-022 — Day-1 `demo` uses fixture seed + `X-Tenant-Id` until ADR-010

- **Status**: accepted
- **Date**: 2026-09-05
- **Deciders**: security-architect (owner); product-owner (stakeholder
  Day-1); software-architect (host); delivery-manager (promote)
- **Locked citations**: ADR-001 (fixture adapter, first `demo`), ADR-010
  (Entra on API — still the target), ADR-016 (`demo-v*`), ADR-021 (schema
  apply, no `MigrateAsync`)

## Context and problem statement

Wave 9 defines schema apply (e09) and waves 6–8 define the browser surface.
Two residuals would otherwise look like Day-1 blockers:

1. The API host still authenticates the tenant via `X-Tenant-Id`. ADR-010
   (Authorization Code + PKCE, JWT to the API) is **not** on the host.
2. Savings / benchmark numbers for the first `demo` are specified as the
   **fixture adapter** (ADR-001). That adapter is proven in tests and is
   **not** seeded onto Flexible Server `contigo_demo`.

Opening `demo` for a stakeholder walk of spec §20 should not wait for a
full Entra-on-API retrofit.

## Considered options

1. **Block Day-1 until ADR-010 is on the API host** — correct end-state;
   delays the first `demo` behind a security epic that e01 already scoped
   and did not land on the host.
2. **Accept `X-Tenant-Id` + fixture seed for the first `demo-v*`** —
   SPA still uses Entra PKCE (public client in `config.json`); API tenancy
   stays the header; savings rows come from a checked-in seed job on
   `contigo_demo`. ADR-010 remains the post-Day-1 host target.
3. **Skip seed and rely on live extract + empty savings** — breaks the
   §20 “see prioritized savings opportunities” step on a fresh DB.

## Decision outcome

**Chosen: Option 2.**

- First `demo-v*` may keep `X-Tenant-Id` on the API. The SPA continues
  Entra PKCE. Do not treat missing API JWT as a Day-1 BLOCKER unless the
  operator overrides at HITL on `reports/audit/demo-readiness-gaps.md`.
- e10 seeds fixture benchmark + at least one savings/contract row on
  `contigo_demo` (optional same job on `contigo_dev`) **after** e09 has
  applied schema. Seed runs as a CI/operator job, not `MigrateAsync`.
- Foundry/OCR on Container Apps is a separate e10 residual: if CA env is
  still empty, wire endpoints/identities so extract/Ask are not fixture-
  only on `demo`. If the operator accepts fixture AI for a dry run, they
  mark that E10 row acceptable at HITL — the task still exists.

### Consequences

- **Good**: Day-1 path is executable after e09 + e06–e08 + e10 without
  reopening ADR-010.
- **Bad**: header-based tenancy is weaker than JWT; must not be copied
  into a later production ADR. Seed data is representative, not market.
- **Neutral**: Swagger UI stays out of scope.

## Implications for decomposition

Epic-10 / slice e10 only. Do not duplicate e05, e09, or e06–e08.

## Amendment (2026-09-10, wave w14 — the interim posture is narrowed, not extended)

Serves **NW-14, NW-01, NW-02, NW-04, NW-58**. The Decision outcome above is
unchanged and still in force: `demo` keeps `X-Tenant-Id` on the API and the SPA
continues Entra PKCE until ADR-010 lands. This footer **tightens** the interim —
it grants nothing new.

**1. A client-declared role header is not an authorization source.** `X-Role` and
`X-Workspace-Role` (`WorkspaceRoleResolver.cs:37-38`, read at `:70-83`) are
**demoted now**. Authorization for an Admin-only action resolves from role claims
on an authenticated principal, then from the `workspace_membership` row — and
nothing else. Where header and membership disagree, **membership wins in both
directions**: a header claiming `Admin` never grants, and a header claiming
`Procurement` never revokes a real Admin's rights. This executes NW-14's own
instruction: "Do not 'fix' this by sending a spoofable `X-Role: Admin` from the
SPA as the product solution."

**2. It costs nothing and breaks no client.** `Grep` over `web/src` for
`X-Role|X-Workspace-Role` returns **zero matches** — the SPA sends `X-Tenant-Id`
and `X-User-Id` only (`client.ts:49-54`). The single surviving reader is
`GET /api/capabilities`, which shapes UI affordances and grants no data access;
its own comment already draws this line (`CapabilitiesEndpointExtensions.cs:44-48`).
This footer restates a convention the codebase already holds; letting the header
drift into an authorization decision would be the regression.

**3. `X-Tenant-Id` is not the tenant of a membership route.** For every route in
this wave the tenant comes from the **route path** and the caller's membership in
it is verified. A crafted `X-Tenant-Id` for a foreign tenant yields **404** on a
read (acceptance N5) — RLS returns no rows, and a 403 there would be a
tenant-existence oracle.

**4. `X-User-Id` is the interim identity, trusted for one thing.** It selects
*which membership rows to look up* and confers no role, no tenant and no scope
(ADR-025 §A). It is read in **one** place so NW-05 (W15) retires it by editing one
file.

**5. Retirement schedule, so the interim stays interim.**

| Mechanism | w14 | Removed by |
|---|---|---|
| `X-Role` / `X-Workspace-Role` as authorization | **demoted now** — UI shaping only | NW-31 (W15) deletes the alias and the reader |
| `X-Tenant-Id` as the tenant of a membership route | not an input; route + membership | NW-05 (W15) |
| `X-User-Id` as identity | one seam (ADR-025 §A) | NW-05 (W15) |
| Fixture/demo auth of this ADR | unchanged this wave | out of w14 scope |

**Recorded limitation, not a defect.** Until ADR-010 lands a caller can still
assert another `X-User-Id`. The Admin gate resolves from the membership row rather
than from a client-supplied role, so NW-58's N3b-8 is honest; the residual belongs
in the epic story and is closed by NW-05. **A reviewer must not reject NW-58 for
it.**
