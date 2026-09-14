---
id: feature-01
type: feature
parent: epic-18
wave: w15
status: active
extends: epic-01 F05 (identity / workspace roles), epic-14 F02 (role from membership)
---

# feature-01-token-identity — The API validates the token, the SPA sends it

## Slice

One middleware and one seam on the server; one async token accessor and one
header helper on the client. The API stops reading `X-User-Id` and starts
resolving identity from a validated JWT's `oid`; `X-Tenant-Id` survives as a
**membership-verified selector** answering 404 when the token subject is not a
member. The SPA stops throwing away the token it already asks for — it requests
the API scopes today at `msalConfig.ts:80-84` and there is **no**
`acquireTokenSilent`/`Popup`/`Redirect` and **no** `Authorization: Bearer`
anywhere in `web/src`.

NW-06 rides the server task, because it is a **deletion** and NW-05 is what makes
it urgent: `WorkspaceRoleResolver`'s claims branch returns a role **without ever
referencing its `tenantId` parameter**, so wiring JWT would turn a dead branch
into cross-tenant **destructive** access on the path that gates document delete
and reprocess.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The API validates the token and trusts nothing else | w15 |
| us-02 | The SPA acquires the token and attaches it everywhere | w15 |

## Architecture decisions in force

- **ADR-010** (w15 footer §1–§4) — the validation parameters, which claim is the identity, the claims-branch deletion, the rejection contract.
- **ADR-022** (w15 footer) — the interim posture retires; the measured sizing; the w14 "recorded limitation" closed.
- **ADR-012** (w15 §1–§3) — one choke point; the superseded "headers are attached per method" sentence; the token is never stored by the SPA.
- **ADR-025 §I, Rule B1** — 404 for a non-member; the token carries identity only.
- **ADR-009** (w15 §5) — the GUC stays parameter-bound.
- **ADR-026 §D1** — `GET /api/workspaces` must keep taking no tenant input.

## Target repo

mixed — `raffa-backend` (us-01) and `raffa-web` (us-02), deliberately split
because the two halves deploy through **separate workflows on the same push**
with independent path filters and no cross-workflow dependency.
