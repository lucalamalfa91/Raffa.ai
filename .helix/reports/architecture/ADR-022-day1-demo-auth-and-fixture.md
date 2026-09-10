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
   **not** seeded onto Flexible Server `raffa_demo`.

Opening `demo` for a stakeholder walk of spec §20 should not wait for a
full Entra-on-API retrofit.

## Considered options

1. **Block Day-1 until ADR-010 is on the API host** — correct end-state;
   delays the first `demo` behind a security epic that e01 already scoped
   and did not land on the host.
2. **Accept `X-Tenant-Id` + fixture seed for the first `demo-v*`** —
   SPA still uses Entra PKCE (public client in `config.json`); API tenancy
   stays the header; savings rows come from a checked-in seed job on
   `raffa_demo`. ADR-010 remains the post-Day-1 host target.
3. **Skip seed and rely on live extract + empty savings** — breaks the
   §20 “see prioritized savings opportunities” step on a fresh DB.

## Decision outcome

**Chosen: Option 2.**

- First `demo-v*` may keep `X-Tenant-Id` on the API. The SPA continues
  Entra PKCE. Do not treat missing API JWT as a Day-1 BLOCKER unless the
  operator overrides at HITL on `reports/audit/demo-readiness-gaps.md`.
- e10 seeds fixture benchmark + at least one savings/contract row on
  `raffa_demo` (optional same job on `raffa_dev`) **after** e09 has
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
