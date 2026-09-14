---
id: us-01
type: user-story
parent: feature-01
wave: w15
status: active
---

# us-01-api-validates-the-token — The API validates the token and trusts nothing else

## Story

As the **owner of a workspace's data**, I want the API to derive who is calling
from a validated Entra token and to resolve their role from their membership row,
so that nobody can read or destroy my tenant's documents by typing a header.

## Acceptance criteria

- [ ] AC-1 A request with a forged `X-User-Id` / `X-Tenant-Id` and **no valid token** returns **401** on every authenticated route.
- [ ] AC-2 `ValidateAudience = true` and the audience is the **`api_client_id`**, never the `api://` identifier URI — at `requested_access_token_version = 2` the `aud` claim **is** the client id. `ValidateIssuer = true`, pinned to the concrete tenant issuer; **`common` / `organizations` / `consumers` are never accepted**. `ClockSkew ≤ 2 minutes`.
- [ ] AC-3 **S-T18** asserts those flags are `true`, so a later "temporary" `ValidateAudience = false` fails a test rather than a penetration test.
- [ ] AC-4 `oid` is the identity. `email` is a first-bind aid only; `tid` is the Entra directory and is **never** read as a Raffa tenant by any code path.
- [ ] AC-5 A missing scope denies (403); a **present scope grants nothing** — `Raffa.Read` / `Raffa.Write` say the browser was allowed to ask, never that the caller may touch a tenant.
- [ ] AC-6 `X-Tenant-Id` is **demoted, not deleted**: an authorized selector verified against the token subject's live membership before `BeginScope`, answering **404** on failure and never 403.
- [ ] AC-7 The ~19 copy-pasted tenant parse sites collapse onto one request-scoped seam, in **one task** — 74 occurrences across 17 files, measured. Two identity regimes must not run live at once.
- [ ] AC-8 The middleware **resolves and never rejects**; rejection happens at the point of use, because the public routes are real: `/health`, `GET /api/invites`, `POST /api/invites/accept`, the `POST /api/workspaces` bootstrap, and `GET /api/workspaces`, which must keep taking no tenant input.
- [ ] AC-9 **NW-06**: `WorkspaceRoleResolver.ResolveAsync` collapses to the membership read. The claims branch, the two header constants and the unreferenced `TryResolveHeaderRole` (zero call sites) go in the same edit — **and so does the doc comment that instructs a reader to keep the branch**.
- [ ] AC-10 The API **fails closed but does not crash closed**: with the four `AzureAd__*` keys absent the authenticated routes answer 401 and the API still **boots**. No header fallback is re-added, not even temporarily.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| — | Phase 1. The four `AzureAd__*` keys are **inert to any image that does not read them**, so the Terraform apply can land at the very start of the wave with zero observable effect — which is what makes the ordering cheap rather than risky, and why this story does not wait on it inside the wave graph |

## Architecture decisions in force

- **ADR-010** (w15 footer §1–§4) — the validation parameters, the identity claim, the claims-branch deletion, the rejection contract, the no-kill-switch co-sign.
- **ADR-022** (w15 footer) — `X-User-Id` deleted as an input; `X-Tenant-Id` demoted; the retirement schedule updated.
- **ADR-025 §I and Rule B1** — a `tenant_id` or `roles` claim is **never** the authorization source; 404 for a non-member.
- **ADR-009** (w15 §5) — only the *source* of `app.identity_subject` changes; the GUC stays **parameter-bound**. An `oid` is a GUID and therefore *looks* safe to interpolate, which is exactly the reasoning that would remove the binding.
- **ADR-026 §D1** — `GET /api/workspaces` takes no tenant input.
- **ADR-016** (w15 clause 15) — fail closed, never crash closed.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | JWT bearer, one identity seam, and the claims branch deleted | L | phase-1 |

## Council decisions carried into this story

**NW-06 is a deletion, not an enablement, and NW-05 is what makes it urgent.**
The claims branch returns `claimRole` without ever referencing its `tenantId`
parameter; only the membership read is scoped. Today that is harmless — this host
has no authenticated principal at all. **The moment NW-05 wires JWT, one Entra
app role assigned once in the directory resolves to that role in every workspace
the caller can name**, and `IsAdminAsync` gates `DELETE /api/documents/{id}` and
`POST …/reprocess`, so the failure is cross-tenant **destructive** access.
Deleting costs six lines; tenant-scoping a claims path costs a design, a Terraform
app role, an assignment story and a permanent second authorization source.

**Revocation stays immediate because nothing caches authorization** — no
membership cache, no role cache, no session cookie, no claims transformation that
materializes a role. A removed member's token still authenticates and gets 404
everywhere, which is the correct answer rather than a gap.

## Open questions

- **OQ-w15-ca-03** — resolved: the tenant **stays a header**, demoted to a membership-verified selector. Recorded as *answered* so no task re-derives it; moving selection into the path is a different task size and no rule requires it.
- **OQ-w15-sec-01** — the `email` optional claim is requested in Terraform; the design does not depend on it, because the `oid` is bound at invite time. Its absence degrades to a 403 and a re-invite, **never to a silent grant**.
