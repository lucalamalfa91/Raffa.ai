---
id: epic-18
type: epic
wave: w15
status: active
extends: [epic-01, epic-14]
---

# epic-18-api-authentication — The token subject is the only identity the API trusts

## Business capability

Today a forged HTTP header is full impersonation: `-H "X-User-Id: <a known
Admin's address>"` yields that Admin's real role, because the string is matched
straight against `workspace_user.email` / `external_subject_id`. This epic wires
the authentication half of ADR-010 that has been designed, provisioned in Entra
and left unconsumed since R0 — the API validates a JWT, the SPA acquires and
attaches one, and every tenant-scoped route resolves identity from the token
subject's membership rather than from anything the caller declares.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w15-todo.md` §3 | NW-05 — API JWT (ADR-010) replaces spoofable headers |
| `inputs/next/w15-todo.md` §3 | NW-06 — workspace role from membership / claims, never a client assertion |
| `inputs/next/w15-todo.md` §3 | NW-07, NW-08, NW-31, NW-32 — **queued to W16** (decomposed here, no task in the w15 wave file) |
| ADR-010 (w14 footer) | "the token carries identity only" — binds NW-05 / NW-08 by name |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | Token identity — the API validates it, the SPA sends it | w15 |
| feature-02 | Identity residuals — conversations and the audit read | **queued (W16)** |
| feature-03 | The interim posture retires — role headers and the unattributed actor | **queued (W16)** |

## Extends

- **epic-01 F05** (identity / workspace roles) — the authentication half it never delivered.
- **epic-14 F02** (role from membership) — its claims branch, dead since w14, is **deleted** rather than enabled.

## Success looks like

A request carrying a forged `X-User-Id` / `X-Tenant-Id` and no valid token
returns **401**. One interactive sign-in on deployed `dev` returns **200** on a
real tenant-scoped route, and the role that request gets is the one its
membership row says — no header and no query parameter changes it.

## Architecture decisions in force

- **ADR-010** (w15 footer §1–§4) — `ValidateAudience = true` with the **client id**, `ValidateIssuer = true` pinned to the concrete tenant issuer, `ClockSkew ≤ 2 minutes`, `oid` is the identity, `tid` is never the Raffa tenant, and the claims-branch deletion.
- **ADR-022** (w15 footer) — the interim identity is **retired**: `X-User-Id` deleted, `X-Tenant-Id` **demoted to a membership-verified selector** (404 on failure, never 403 — a 403 there is a tenant-existence oracle).
- **ADR-012** (w15 §1–§3) — the client choke point, the never-stored token, the no-role-claim guard, the tenant selector.
- **ADR-005** (w15 §4–§5) — four non-secret env vars, `$0.00`, and **no `Authentication__Enabled` kill-switch** in any environment.
- **ADR-009** (w15 §5) — only the *source* of `app.identity_subject` changes; the GUC stays **parameter-bound**.
- **ADR-016** (w15 clauses 15, 19–21) — fail closed, never crash closed; the rollback rehearsal; the ordering refusal with its evidence.
- **ADR-025 §I** — the token carries identity only; a `tenant_id` or `roles` claim is **never** the authorization source.

## Out of scope

- **An `Authentication__Enabled` kill-switch, in any environment.** A key that turns authentication off is a security control in the hands of whoever can run an apply, and flipping it back needs another operator-confirmed HCP apply — the slowest rollback in the repo. Reverting the image SHA is one `az containerapp update --image` and no apply.
- **Any header fallback, "temporarily" or otherwise.** That fallback *is* the vulnerability NW-05 removes.
- **Moving the tenant into the route path** — a change to every tenant-scoped method in `client.ts` plus every call site, a different task size, and no rule requires it (OQ-w15-ca-03).
- **Repairing `reprocess-tenant-documents.yml`**, which NW-05 takes out of service. It fails on a *read* before any write, so there is no partial reprocess and no unattributed audit row. **NW-31 owns that file in W16**, so one wave opens it once.
- **Reading a `roles` claim in the SPA.** A token is trivially decoded in a browser; the SPA keeps reading `role` from the `GET /api/workspaces` row.
