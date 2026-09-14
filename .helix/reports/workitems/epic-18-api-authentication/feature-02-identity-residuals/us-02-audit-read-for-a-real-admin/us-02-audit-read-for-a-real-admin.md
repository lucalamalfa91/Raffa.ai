---
id: us-02
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-02-audit-read-for-a-real-admin — `GET /api/audit` works for a real Admin

## Story

As a **workspace Admin**, I want to read my tenant's audit trail, so that I can
see who invited whom, what was deleted and when — which is the whole point of
keeping the trail.

## Acceptance criteria

- [ ] AC-1 A workspace Admin, authenticated with a valid token, can read their own tenant's audit rows.
- [ ] AC-2 The guard's authorization source is **membership**, not a `tenant_id` or `roles` claim — ADR-010's w14 footer is explicit that a claim is never the authorization source.
- [ ] AC-3 A non-Admin member is refused, and a non-member gets **404**, never 403.
- [ ] AC-4 The route joins the **published contract**: it is absent from `web/openapi/raffa-api.v1.json` today (34 paths, no `/api/audit`), so no generated client can reach it.
- [ ] AC-5 The read is tenant-scoped, and the scoping is proven by a test that asserts a second tenant's rows are not returned.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token (`E18/F01/US01/T01`, w15) | with no auth middleware `HttpContext.User` is always anonymous, so **every** call returns 401 today and no Admin can read anything |

## Architecture decisions in force

- **ADR-010** — the seam its w14 footer names; a claim is never the authorization source.
- **ADR-026** — a route joins the contract when it becomes reachable.
- **ADR-011** — the audit read surface and who may see it.
- **ADR-025 Rule B1** — 404 for a non-member.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Swap the guard to membership; publish the route | M | **queued — W16** |

## Council decisions carried into this story

Not at the w15 table — **queued**. Evidence in
`reports/context/waves/w15-requirements.md` §2, NW-08 row:
`AuditEndpointExtensions.cs:22-36` binds a `ClaimsPrincipal` and calls
`WorkspacePrincipalAuthorization.TryAuthorize(user, Admin, …)`, which requires an
authenticated principal, a custom `tenant_id` claim and `ClaimTypes.Role`
(`WorkspacePrincipalAuthorization.cs:56-74`). `backend/README.md:243` says as
much.

## Open questions

- Whether any non-Admin role may read a filtered view of the trail. **Assumption in force**: no — Admin only, matching the current guard's intent. W16 confirms with security-architect.
