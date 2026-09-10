---
id: E14/F02/US02/T01
type: task
story: us-02-admin-role-from-membership
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-admin-role-from-membership — delete the header branch from the role resolver and prove Delete / Retry for the creator

## Context

**Closes: NW-14.** Decision row: `reports/architecture/waves/w14.md`, row
**NW-14** (security-architect + client-architect). ADRs in force:
**ADR-022 w14 footer (five clauses + the retirement table)**, **ADR-025 §E**,
**ADR-010 — `none` for w14** (the JWT swap is NW-05, W15).

Reproduced on `dev` by the operator on 2026-09-10: `DELETE …/api/documents/{id}`
→ 403, 17 B, preflight 200. `DocumentsEndpointExtensions.cs:85,87` maps
reprocess and delete, both Admin-only (`:52-55`, R-DOC-07 / R-DOC-10);
`WorkspaceRoleResolver.cs:45-63` resolves claims → `X-Role`/`X-Workspace-Role`
(`:70-83`) → `workspace_membership` by `X-User-Id` (`:85-119`). No auth is
wired, the SPA sends no role header, and NW-02 never wrote the membership row —
so the join at `:104-114` returned empty and `IsAdminAsync` was false.

**This is a proof task, not a fix task.** Once `E14/F02/US01/T01` writes the
creator's membership row, the existing join returns `Admin` and the gate passes.
What this task *changes* is one deletion: the middle branch of `ResolveAsync`,
so a client-declared role can never be an authorization source.

- **Architecture decisions in force**: ADR-022 w14 footer (the header is
  demoted, not removed — `/api/capabilities` keeps it), ADR-025 §E, R-DOC-07 /
  R-DOC-10.
- **Do not touch**: `WorkspaceEndpointExtensions.cs`,
  `WorkspaceMembersEndpointExtensions.cs`, `WorkspaceInvitesEndpointExtensions.cs`
  (`E14/F03/US01/T01` and `E14/F04/US01/T01` own them this phase);
  `web/openapi/raffa-api.v1.json`, `web/src/api/generated/schema.ts`,
  `web/src/api/client.ts` (`E14/F03/US01/T01` is the phase-2 contract owner);
  `CapabilitiesEndpointExtensions.cs` (its `X-Role` read stays exactly as it is);
  `backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
  (ADR-025 §I); anything under `web/src`.

## Coding objective

1. In
   `backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs`, **delete the
   header branch** of `ResolveAsync` (`:57-60`) so the resolution order becomes
   **claims → `workspace_membership`**, and nothing else. `TryResolveHeaderRole`
   (`:70-83`) stays in the file with exactly one remaining caller,
   `/api/capabilities`; add a comment naming ADR-022's w14 footer and stating
   that it is **non-authoritative UI shaping only** and must never be reached
   from an authorization decision again.
2. Where the header and the membership row disagree, **membership wins in both
   directions** — a header claiming `Admin` never grants, and one claiming
   `Procurement` never revokes a real Admin. After the deletion this is true by
   construction; assert it with a test rather than a comment.
3. Make the membership branch consume the `ICallerIdentity` seam
   (`backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs`, created in phase 1)
   instead of reading `X-User-Id` off `HttpRequest.Headers` directly (ADR-025
   §A2). W15 retires the header in that one file.
4. Prove **N9** at the API level, both halves.

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4, AC-5

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs` | delete the header branch from `ResolveAsync`; membership branch reads `ICallerIdentity`; comment the demotion |
| `backend/tests/Raffa.Api.Tests/WorkspaceRoleResolverTests.cs` | new — T2a and the "membership wins in both directions" cases |
| `backend/tests/Raffa.Api.Tests/DocumentAdminActionsAuthorizationTests.cs` | new — N9: creator deletes → 204, reprocesses → 200; Procurement → 403 on both |

## Context the implementer needs

- **Build the Procurement-member fixture directly through the `DbContext`, not
  through `WorkspaceMembershipService.InviteAsync`.** `InviteAsync` writes a live
  membership at invite time **today**, but `E15/F01/US01/T01` (phase 3) moves
  membership to accept time. A fixture built on `InviteAsync` passes in phase 2
  and breaks in phase 3. This is a recorded ordering correction from the w14
  table (NW-14 row): "the chain NW-02 → NW-01 → NW-14 holds only while
  `InviteAsync` still grants on invite".
- `Grep` over `web/src` for `X-Role|X-Workspace-Role` returns **zero** matches
  (`client.ts:49-54` sends `X-Tenant-Id` + `X-User-Id` only), so the demotion
  breaks no client. Verify this yourself before assuming it.
- `CapabilitiesEndpointExtensions.cs:44-48` already documents the line this task
  makes normative ("never a hard 401/403 the way `AuditEndpointExtensions` is,
  since the catalog itself is not sensitive, tenant data"). Leave that file
  alone.
- **Do not touch**: `web/**`, any `*EndpointExtensions.cs` other than the tests
  above, `WorkspacePrincipalAuthorization.cs`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests/Raffa.Api.Tests.csproj` exits 0
- [ ] `rg -n "TryResolveHeaderRole" backend/src/Raffa.Api` shows exactly **one**
      call site and it is reached only from the capabilities path
- [ ] `rg -n "X-Role|X-Workspace-Role" backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs`
      shows the constants and `TryResolveHeaderRole` only — no reference from
      `ResolveAsync`

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | T2a — a Procurement member sending `X-Role: Admin` (and `X-Workspace-Role: Admin`) resolves to Procurement | `backend/tests/Raffa.Api.Tests/WorkspaceRoleResolverTests.cs` |
| unit | a header claiming `Procurement` does not revoke a real Admin | `backend/tests/Raffa.Api.Tests/WorkspaceRoleResolverTests.cs` |
| integration | **N9** — creator: `DELETE /api/documents/{id}` → 204, `POST …/reprocess` → 200; Procurement member: 403 on both, with and without a spoofed header | `backend/tests/Raffa.Api.Tests/DocumentAdminActionsAuthorizationTests.cs` |

## Open questions blocking this task

- none.

## Wave-spec entry
```yaml
- id: E14/F02/US02/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-02-workspace-bootstrap-and-admin-authority/us-02-admin-role-from-membership/tasks/task-01-admin-role-from-membership.md
  produces: [admin-role-from-membership]
  depends_on: [workspace-bootstrap]
  effort: M
  layer: backend
  status: live
```
