---
wave: w14
source: inputs/next/next-waves-todo.md
source_sha256: e3fd34176de46d24f4c8fdb7526c29c91930fe97874cf75fd36c44554bacd23c
design_sources: [inputs/design/prototypes/contigo-v2/screens-v2.md, inputs/design/prototypes/contigo-v2/ia-v2.md, inputs/design/prototypes/contigo-v2/markup.html, inputs/design/prototypes/contigo-v2/app.jsx]
baseline: 1650213 (helix/next-wave-process) — 8 uncommitted web edits in the tree; origin/main is 25b10da (5 commits ahead, post-rebrand)
generated: 2026-09-10T16:05Z
previous_wave: e13
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w14 — normalized requirements

Written by `next-intake` from the raw file above. The raw file stays the
human's; this file is the process oracle for the council and the decomposer.
Items keep the source ids when the raw file has them (`NW-01`), otherwise
they get `W14-NN`. Every claim about "today" cites a file in `../`.

Theme, from the raw file's own §7 grouping: **W14 — Workspace is real**.

## 1. Oracles in force

- Product: `inputs/product-spec.md` §3.1 (roles table), §3.2 (multi-tenancy,
  "no cross-tenant query path is acceptable"), §20 Day 1 ("Create a workspace
  and invite Procurement users"); `inputs/percorso-pilota-v1.md` §2 (first-run
  onboarding: step 1 "Se non ha workspace: form nome / industria / paese. Se
  ne ha uno, entra"; step 4 "Invite (dopo, non prima)"; and its own
  **"Non è onboarding: … email"** exclusion — see OQ-w14-002);
  `inputs/requirements.md` §5.1 / R-DOC-07 / R-DOC-10 (Admin-only delete and
  reprocess), R-WEB-01.
- Locked: `reports/context/locked-decisions.md` — Azure only; `dev` + `demo`,
  no production; cheapest SKUs, nothing idle-expensive; HCP Terraform under
  `infra/`; OIDC / Entra ID, secrets in Key Vault, never in source or bundle;
  API-first. Cite, never re-open.
- ADRs touched by this wave: ADR-009 (RLS tenancy), ADR-010 (Entra/OIDC — the
  target, not yet on the host), ADR-022 (interim `X-Tenant-Id` posture, still
  in force), ADR-003 + ADR-021 (workspace columns and schema apply), ADR-005 +
  ADR-007 (a mail transport would be a new Azure resource + Terraform module),
  ADR-011 (invitation token secret in Key Vault), ADR-012 / ADR-018 / ADR-020
  (routes, screens 1 and 10), ADR-014 (wave base branch), ADR-016 (per-env
  config on promotion).
- Design: `inputs/design/prototypes/contigo-v2/screens-v2.md` §1 (workspace
  pick row `"{{ seededCount }} validated contracts · CHF · eu-west"`) and §10
  (Workspace & members: table `name · email · role · status`, invite form,
  Admin "Also uploads, deletes, manages members");
  `inputs/design/prototypes/contigo-v2/ia-v2.md` route map `/signin`,
  `/workspace/members`; anchors in `markup.html:63` and `markup.html:395,397`;
  `app.jsx:133-134,138-139,169-170` (`signCreate` / `signPick` / `enterWs`,
  `members`, `sendInvite`, `status: Invited`).
- Last wave: `reports/execution/wave-close-e13.md` — five of twenty e13 tasks
  delivered nothing; the operator implemented them directly and merged PR #67.
  Salvage tags `salvage/E13-F06-US01-T02/{1,3}` and
  `salvage/E13-F11-US01-T01/1` are recorded there. **No undelivered e13 task
  falls into this wave's scope** — every W14 item is a new gap the raw file
  found on `origin/main`, not a carry-over.
- Carry-over: `reports/workitems/BACKLOG.md` has **no** `status: queued` row
  from an earlier next-wave run (this is the first run of the process). No
  carry-over items.

### Baseline hazards (read before decomposing)

1. **`origin/main` is not this tree.** `origin/main` @ `25b10da` carries PR #77
   (`87b7976` "rebrand: mechanical Contigo -> Raffa rename across the
   monorepo", 1477 files, 840 path renames, + `266e86f`) and PR #78
   (`c7d1e18` infra import cleanup). This checkout is `1650213`, two commits
   ahead of a **pre-rebrand** base and five behind `origin/main`. Every path in
   this document is verified on **this** tree; on `origin/main` translate
   `backend/src/Contigo.X` → `backend/src/Raffa.X`, namespace `Contigo.*` →
   `Raffa.*`, `inputs/design/prototypes/contigo-v2/` → `raffa-v2/`. The rename
   is mechanical: every W14 gap below was re-checked against `origin/main` and
   is byte-identical there (`git show origin/main:backend/src/Raffa.Api/
   WorkspaceEndpointExtensions.cs` still maps only the two POSTs;
   `WorkspaceProvisioningService` still writes no membership;
   `WorkspacePickerScreen.tsx:82` still `contractCount: 0`). See W14-01.
2. **The tree is dirty.** `git status` shows 8 uncommitted web edits
   (`web/src/styles/semantics.ts`, `web/src/routes/contracts/review/*`,
   their tests, `web/README.md`) that move the auto-accept threshold to ≥90 %
   and rename the "Flagged" tag to "Review". They affect NW-64/NW-65/NW-66
   (queued, W17), not this wave. They are the operator's, uncommitted, and
   are not part of any w14 task.
3. **Operator hygiene, not a wave item**: `fed-cred-dev.json` and
   `fed-cred-demo.json` sit untracked at the repo root. Neither is referenced
   by `infra/` or a workflow. Delete or move them out of the tree before the
   next commit — they are not in scope for w14 and no task will touch them.

## 2. Items

| ID | Title | Kind | Priority | Area | Status today | Seats | ADR touchpoints | Design refs | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| W14-01 | Wave base is the post-rebrand `origin/main` (`Contigo.*` → `Raffa.*`) | ops | must | ci | OPEN | delivery-manager | ADR-014 | — | W14-A1 |
| NW-02 | Create workspace also writes the creator's membership (Admin) | bug | must | auth | OPEN | software-architect, security-architect | ADR-009, ADR-010, ADR-022 | — | N1 |
| NW-01 | `GET /api/workspaces` for the signed-in identity | feature | must | backend | OPEN | software-architect, security-architect, client-architect | ADR-009, ADR-010, ADR-022 | screens-v2 §1 | N2, N4, N5 |
| NW-14 | Delete and Retry upload 403 for the workspace creator | bug | must | auth | OPEN | security-architect, client-architect | ADR-022, ADR-010 | — | N9 |
| NW-09 | Workspace picker contract count is frozen at 0 | bug | must | web | OPEN | software-architect, client-architect | ADR-018, ADR-020 | screens-v2 §1, `markup.html:63` | N8 |
| NW-03 | Current workspace is a server fact, not `sessionStorage` | change | should | web | OPEN | client-architect | ADR-012, ADR-018 | ia-v2 route map `/signin` | N4 |
| NW-04 | `GET /api/workspaces/{tenantId}/members` | feature | should | backend | OPEN | software-architect, security-architect, client-architect | ADR-009, ADR-010 | screens-v2 §10 | N3 |
| NW-58 | Invites are email + link; login joins that workspace; Admin remove requires a new invite | feature | must | auth | OPEN | product-owner, software-architect, security-architect, cloud-architect, client-architect, ux-ui-designer, delivery-manager | ADR-001, ADR-005, ADR-007, ADR-010, ADR-011, ADR-016, ADR-020 | screens-v2 §10, `markup.html:395,397`, `app.jsx:133-134,169-170` | N3b |
| NW-24 | Workspace has no currency / region (HITL) | change | should | backend | OPEN | product-owner, software-architect, ux-ui-designer | ADR-003, ADR-018, ADR-020, ADR-021 | screens-v2 §1, `markup.html:63` | W14-A2 |
| NW-05 | API JWT (ADR-010) replaces spoofable headers | feature | must | auth | OPEN | queued — W15 | ADR-010, ADR-022 | — | N5 |
| NW-06 | Workspace role from membership / claims, not `?role=` | change | must | web | OPEN | queued — W15 | ADR-010 | — | N9 |
| NW-07 | Conversation `user_id` is the token subject | change | should | backend | PARTIAL | queued — W15 | ADR-010, ADR-024 | — | N5 |
| NW-08 | `GET /api/audit` works for a real Admin | bug | should | backend | OPEN | queued — W15 | ADR-010 | — | N5 |
| NW-31 | Dual role headers (`X-Role` vs `X-Workspace-Role`) until NW-05 | change | should | backend | PARTIAL | queued — W15 | ADR-022 | — | N5 |
| NW-32 | Unattributed actor on writes when `X-User-Id` is absent | change | should | backend | OPEN | queued — W15 | ADR-010, ADR-022 | — | N5 |
| NW-10 | Documents rail badge still reads `documentStore` (`sessionStorage`) | bug | should | web | OPEN | queued — W16 | ADR-012 | — | N6 |
| NW-11 | Renewal actions have no HTTP read-back | change | should | backend | OPEN | queued — W16 | ADR-012 | — | N6 |
| NW-12 | Quote GET + negotiation-outcome list | change | should | backend | OPEN | queued — W16 | ADR-012 | — | N6 |
| NW-13 | Contract 360 negotiation-step ticks (`sessionStorage`) | change | could | web | OPEN | queued — W16 | ADR-012 | — | N6 |
| NW-21 | Quote outcome does not update Savings | bug | should | backend | PARTIAL | queued — W16 | ADR-001 | — | N6 |
| NW-20 | Contract 360 `benchmark` and `activity` are always `[]` | change | should | backend | OPEN | queued — W17 | ADR-001, ADR-023/024 | — | N16 |
| NW-22 | Renewal insight `MarketPosition` stays null on real contracts | change | should | backend | OPEN | queued — W17 | ADR-001 | — | N16 |
| NW-23 | Portfolio has no `category` filter | feature | could | backend | OPEN | queued — W17 | ADR-020 | — | — |
| NW-25 | Savings list has no filters | feature | could | web | OPEN | queued — W17 | ADR-020 | — | — |
| NW-26 | Preview / evidence quality residuals | change | should | backend | OPEN | queued — W17 | ADR-017, ADR-020 | — | N17 |
| NW-62 | Contract 360 must answer "where you can save" / "when you must move" | design | must | web | OPEN | queued — W17 | ADR-020, ADR-024 | screens-v2 §5 | N16 |
| NW-63 | Document viewer: pages with OCR phrases highlighted and editable | feature | must | web | OPEN | queued — W17 | ADR-017, ADR-020 | screens-v2 §5 | N17 |
| NW-64 | Fields OCR did not recover appear in a section the user can fill | design | should | web | OPEN | queued — W17 | ADR-020 | screens-v2 §4 | N18 |
| NW-65 | Details: only officialized facts; drop "still need to decide" | design | should | web | OPEN | queued — W17 | ADR-019, ADR-020 | screens-v2 §5 | N19 |
| NW-66 | Why-clauses: specchietto on click, viewer link, leverage not confidence | design | should | web | OPEN | queued — W17 | ADR-019, ADR-020 | screens-v2 §5 | N20 |
| NW-27 | Upload HTTP returns as soon as the file is stored | change | should | backend | OPEN | queued — W18 | ADR-002, ADR-005, ADR-024 | — | N15 |
| NW-30 | Hand-authored OpenAPI gaps | change | should | backend | OPEN | queued — W18 | ADR-012 | — | — |
| NW-40 | Confirm HCP apply has CA env vars + Foundry RBAC | ops | should | infra | PARTIAL | queued — W18 | ADR-007, ADR-008 | — | — |
| NW-41 | Prove deployed API serves seeded `market_record` rows | ops | should | infra | PARTIAL | queued — W18 | ADR-021, ADR-022 | — | — |
| NW-50 | `web/e2e/day1.spec.ts` red against the V2 shell | bug | should | ci | PARTIAL | queued — W18 | ADR-014, ADR-020 | — | — |
| NW-55 | Ask citation cards: real page preview or a section link | design | must | web | OPEN | queued — W18 | ADR-020, ADR-024 | screens-v2 §2 | N10 |
| NW-56 | Contract 360 "Ask about it" must brief that contract | design | must | web | PARTIAL | queued — W18 | ADR-020, ADR-024 | screens-v2 §2, §5 | N11 |
| NW-57 | Quote check: market benchmark, not a manual savings worksheet | design | must | web | OPEN | queued — W18 | ADR-001, ADR-020 | screens-v2 §9 | N12 |
| NW-59 | Ask never dead-ends: every abstain/error has a clickable next step | design | must | web | OPEN | queued — W18 | ADR-024 | screens-v2 §2 | N13 |
| NW-60 | Hide the global Ask bar on Ask screens | bug | should | web | OPEN | queued — W18 | ADR-018, ADR-020 | ia-v2 §Navigation | N14 |
| NW-61 | Upload must feel instant; details only when the document is ready | change | must | web | PARTIAL | queued — W18 | ADR-020, ADR-024 | screens-v2 §3 | N15 |
| NW-51 | V2 satellite-screen design alignment | design | — | web | CLOSED-ON-MAIN | none | ADR-019, ADR-020 | — | — |
| NW-52 | Paid market-intelligence provider | feature | — | ai | DEFERRED | none | ADR-001 §1.2 | — | — |
| NW-53 | Mobile beyond scaffold | feature | — | web | DEFERRED | none | ADR-013 | — | — |
| NW-54 | Legal / Finance / Read-only as first-class nav | feature | — | web | DEFERRED | none | ADR-018 | — | — |

---

### W14-01 — Wave base is the post-rebrand `origin/main` (`Contigo.*` → `Raffa.*`)

- **Source**: no raw id — found by this intake auditing the checkout against
  `origin/main`.
- **Raw**: the raw file's own header says "checked against `origin/main` @
  `383020e`". `origin/main` has since moved to `25b10da`.
- **Today (evidence)**: `git log --oneline origin/main ^HEAD` returns
  `25b10da`, `c7d1e18`, `0dc9611`, `266e86f`, `87b7976`. `87b7976` is a
  mechanical rename of every module: `git ls-tree -d origin/main backend/src/`
  lists `Raffa.Api`, `Raffa.Identity.Workspace`, … — no `Contigo.*` directory
  survives. `.helix` itself was renamed (`contigo-process.yaml` →
  `raffa-process.yaml`, `inputs/design/prototypes/contigo-v2/` →
  `raffa-v2/`); this branch's own `contigo-next-process.yaml` (`54cbd27`) does
  not exist on `origin/main`, so a merge produces a mixed tree.
- **Gap**: every path this document cites resolves on `1650213` and on none of
  them on `origin/main`. A wave branch cut from `main` whose tasks name
  `backend/src/Contigo.*` fails at the first `Read`.
- **Seats**: delivery-manager (git flow, wave order, which commit the wave
  branches from — ADR-014).
- **ADR touchpoints**: amend ADR-014 only if the branch/base rule changes;
  otherwise `none — mechanical rename, no decision`.
- **Design refs**: none.
- **Acceptance**: **W14-A1** — the wave branch's base contains `87b7976`;
  `rg -n "Contigo\." backend/src web/src` returns nothing on the wave branch;
  `dotnet build` and `npm test` are green on the base before the first task.
- **Proposed epic**: epic-14-workspace-identity (ordering task, phase 1).
- **Task sketch**: one pre-flight task — rebase/merge `helix/next-wave-process`
  onto `origin/main` (operator, at HITL, not the fan-out), then re-run the path
  translation over `reports/context/waves/w14-requirements.md` and
  `reports/plan/slices/w14.yaml` before launch.

### NW-02 — Create workspace also writes the creator's membership (Admin)

- **Source**: NW-01/NW-02 block, `inputs/next/next-waves-todo.md` §1.
- **Raw**: "creating identity becomes Workspace Admin in `workspace_membership`.
  Without this row, NW-01 cannot return the workspace and NW-14 always 403s."
- **Today (evidence)**:
  `../backend/src/Contigo.Identity.Workspace/Infrastructure/WorkspaceProvisioningService.cs:40-46`
  builds `WorkspaceFactory.CreateWorkspaceWithDefaultRoles` and adds
  `db.Workspaces` + `db.WorkspaceRoles` only — no `WorkspaceMembership`, no
  `WorkspaceUser`. Its own doc comment (`:12-15`) and
  `../backend/src/Contigo.Api/WorkspaceEndpointExtensions.cs:14-20` call
  creation "the pre-authentication signup step (nobody has a tenant claim yet)".
  `CreateWorkspaceAsync` (`WorkspaceEndpointExtensions.cs:36-57`) takes only a
  name — no caller identity reaches the service. Same on `origin/main`.
- **Gap**: the creator of a workspace is a member of nothing. Every
  membership-backed read (NW-01, NW-04) and every Admin-only write (NW-14)
  fails for the one person who just created the tenant.
- **Seats**: software-architect (the provisioning service now needs a caller
  identity, a `workspace_user` row and a membership insert inside the same
  tenant scope + transaction); security-architect (which identity is trusted to
  become Admin while ADR-010 is not on the host — see OQ-w14-001 — and how RLS
  `WITH CHECK` accepts three inserts under one `BeginScope`).
- **ADR touchpoints**: amend ADR-009 (the membership row is now part of tenant
  bootstrap) and ADR-022 (name `X-User-Id` as the interim membership key with
  the ADR-010 swap seam). ADR-010 unchanged.
- **Design refs**: none.
- **Acceptance**: **N1** — create workspace → rows in `workspace` **and**
  `workspace_membership` (Admin, this identity), observable on `dev`.
- **Proposed epic**: epic-14-workspace-identity (extends epic-01 F05).
- **Task sketch**:
  - `POST /api/workspaces` reads the caller identity (interim `X-User-Id`) and
    400s without it; `CreateWorkspaceAsync(name, callerIdentity, …)`.
  - Insert `workspace` + role catalog + `workspace_user` + `workspace_membership`
    (Admin) in one `SaveChangesAsync` under one `tenantContext.BeginScope`.
  - Backfill note: existing `dev` workspaces have no membership row — an
    operator/CI backfill or a "claim this workspace" path is the council's call.

### NW-01 — `GET /api/workspaces` for the signed-in identity

- **Source**: NW-01, `inputs/next/next-waves-todo.md` §1.
- **Raw**: "list workspaces the caller belongs to (membership), RLS."
- **Today (evidence)**:
  `../backend/src/Contigo.Api/WorkspaceEndpointExtensions.cs:31-32` maps
  `POST /api/workspaces` and `POST /api/workspaces/{tenantId}/invites` and
  nothing else. `../backend/src/Contigo.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs`
  has `InviteAsync` / `LinkSignInAsync`, no "list workspaces for this user"
  query. The web substitutes
  `../web/src/routes/signin/workspaceStore.ts:32,88-106` — a `localStorage`
  array under `contigo.signin.knownWorkspaces.{homeAccountId}`, whose own
  header comment (`:1-30`) states the gap and names the exact backend types a
  future task must extend. `../web/openapi/contigo-api.v1.json:53` documents the
  same ("No `GET` (list-workspaces-for-caller) endpoint exists yet").
- **Gap**: workspace discovery lives in one browser. A second browser, a
  cleared profile or another device cannot find the tenant, so the user
  "creates again" and lands on a new, empty tenant while the files sit on the
  old id.
- **Seats**: software-architect (endpoint + membership query + OpenAPI entry +
  generated client); security-architect (membership *is* the authorization —
  the response must never be widened by a client-supplied `X-Tenant-Id`, and a
  crafted other-tenant id must 403/404, ADR-009); client-architect (the picker
  reads the server list; `workspaceStore.ts`'s `knownWorkspaces` is deleted,
  not doubled).
- **ADR touchpoints**: amend ADR-009 (a cross-tenant-safe list keyed on the
  caller, not on a tenant header) and ADR-022 (interim caller identity).
- **Design refs**: `inputs/design/prototypes/contigo-v2/screens-v2.md` §1 —
  pick row `"{{ seededCount }} validated contracts · CHF · eu-west"`;
  `app.jsx:138` (`signCreate` when nothing is seeded, `signPick` when it is).
- **Acceptance**: **N2** second browser / cleared storage → same workspace, no
  second create; **N4** close tab, reopen → same tenant when the user has one
  membership; **N5** crafted other-tenant header → 403/404.
- **Proposed epic**: epic-14-workspace-identity (extends epic-01 F05, epic-06 F03).
- **Task sketch**:
  - `GET /api/workspaces` → `[{ id, name, createdAt, role, contractCount, … }]`
    from `workspace_membership` for the caller; RLS-scoped per row.
  - Extend `web/openapi/contigo-api.v1.json` + regenerate
    `web/src/api/generated/schema.ts`; add `listWorkspaces` to `client.ts`.
  - `WorkspacePickerScreen` renders the server list; delete
    `knownWorkspaces` read/write from `workspaceStore.ts`.

### NW-14 — Delete and Retry upload 403 for the workspace creator

- **Source**: NW-14, `inputs/next/next-waves-todo.md` §1. Reproduced on `dev`
  by the operator on 2026-09-10 (`DELETE …/api/documents/{id}` → 403, 17 B,
  preflight 200).
- **Raw**: "after NW-02 + NW-06, the creator's Delete/Retry succeed (204 /
  200). … Do not 'fix' this by sending a spoofable `X-Role: Admin` from the SPA
  as the product solution."
- **Today (evidence)**:
  `../backend/src/Contigo.Api/DocumentsEndpointExtensions.cs:85,87` maps
  `POST /api/documents/{id}/reprocess` and `DELETE /api/documents/{id}`, both
  Admin-only (`:52-55`, R-DOC-07/R-DOC-10).
  `../backend/src/Contigo.Api/Infrastructure/WorkspaceRoleResolver.cs:45-63`
  resolves in order claims → `X-Role`/`X-Workspace-Role` (`:70-83`) →
  `workspace_membership` by `X-User-Id` (`:85-119`). No auth is wired, the SPA
  sends no role header, and NW-02 never wrote the membership row — so the join
  at `:104-114` returns empty and `IsAdminAsync` is false. The SPA still shows
  the buttons: `../web/src/components/shell/workspaceRole.ts:36-46` defaults to
  `"admin"` for whoever picked the workspace in this browser.
- **Gap**: two row actions look live, fire real requests and always fail for
  the only person who can legitimately use them. The 403 surfaces as a `.hint`
  alert that is easy to miss.
- **Seats**: security-architect (the resolver's order stays claims → membership;
  the interim `X-Role` header must not become the product answer, ADR-022);
  client-architect (`isAdmin` comes from the server role on the NW-01 payload,
  not from `resolveWorkspaceRole()`; the button state follows the real role).
- **ADR touchpoints**: amend ADR-022 (the interim role source of truth is the
  membership row, never a client-declared header); `none` for ADR-010 — the
  JWT swap is NW-05 in W15.
- **Design refs**: none — no visual change beyond the button's enabled state.
- **Acceptance**: **N9** — workspace creator: Delete → 204 and the row is gone;
  Retry upload on a failed doc → 200 and processing resumes; a Procurement
  member still gets 403 on Delete; repeat from a second browser.
- **Proposed epic**: epic-14-workspace-identity.
- **Task sketch**:
  - No backend change beyond NW-02 is expected — prove it with an API test that
    creates a workspace and then deletes a document as the creator.
  - Web: drop the `?role=` inference for the Admin-only affordances; read the
    role from the workspace the user entered.
  - Keep a Procurement-role regression test (403 stays 403).

### NW-09 — Workspace picker contract count is frozen at 0

- **Source**: NW-09, `inputs/next/next-waves-todo.md` §1.
- **Raw**: "login shows **0 contracts** after uploads… count from the server;
  agree with Documents / Portfolio."
- **Today (evidence)**:
  `../web/src/routes/signin/WorkspacePickerScreen.tsx:82` writes a literal
  `contractCount: 0` into the `WorkspaceSummary` it persists, and `:173`
  renders `{workspace.contractCount} contracts`. Nothing refreshes it:
  `../web/src/routes/signin/workspaceStore.ts:39-45` documents the field as
  "Always 0 from this screen: it never calls the portfolio API". No call to
  `GET /api/contracts` or `GET /api/documents` exists on that route.
- **Gap**: the first number a user sees after signing in contradicts
  `/documents` and the rail. `percorso-pilota-v1.md` §2 explicitly calls
  "picker di due workspace finti con gli stessi dati" *not* onboarding.
- **Seats**: software-architect (the count is a field of the NW-01 payload —
  one query, not a second round-trip per row); client-architect (the picker
  renders the server number and the rail/Documents must agree).
- **ADR touchpoints**: `none — the count is a field on NW-01's contract`;
  ADR-020 screen 1 wording is unchanged.
- **Design refs**: `screens-v2.md` §1 and `markup.html:63` —
  `"{{ seededCount }} validated contracts · CHF · eu-west"`. Note the prototype
  says **validated** contracts, not all documents; the council fixes which.
- **Acceptance**: **N8** — upload ≥1 document, sign out, sign in → picker count
  non-zero; rail matches; `/documents` lists the same files.
- **Proposed epic**: epic-14-workspace-identity.
- **Task sketch**: count served by `GET /api/workspaces`; one definition
  ("validated contracts" per the prototype) reused by the picker and the rail.

### NW-03 — Current workspace is a server fact, not `sessionStorage`

- **Source**: NW-03, `inputs/next/next-waves-todo.md` §1.
- **Raw**: "one membership → enter it; several → picker from NW-01."
- **Today (evidence)**:
  `../web/src/routes/signin/workspaceStore.ts:33,117-145` keeps the selection
  in `sessionStorage` under `contigo.signin.currentWorkspace`;
  `../web/src/App.tsx:37,43` notes that `loadCurrentWorkspace()` plus MSAL's own
  `sessionStorage` account is the whole gate. Deliberate at the time ("switching
  tabs/reopening the browser should not silently resume a previous tenant") —
  but with NW-01 absent it is also the only record of which tenant the user is
  in.
- **Gap**: reopening the browser drops the user into a picker built from
  `localStorage`, so a single-membership user re-picks (or re-creates) instead
  of entering. `percorso-pilota-v1.md` §2 step 1: "Se ne ha uno, entra."
- **Seats**: client-architect (routing + state rule: one membership → enter,
  several → picker; `sessionStorage` may stay as a *cache* of the server
  answer, never as the source of truth).
- **ADR touchpoints**: amend ADR-012 or ADR-018 only if the routing rule changes
  the `/signin` contract; otherwise `none — client state rule, no ADR`.
- **Design refs**: `ia-v2.md` route map `/signin` — "Entra sign-in → workspace
  create/pick → lands on Ask"; `app.jsx:138-139` `signPick`/`enterWs`.
- **Acceptance**: **N4** — close tab, reopen → same tenant when the user has one
  membership; with two memberships the picker appears and the choice sticks for
  the session.
- **Proposed epic**: epic-14-workspace-identity.
- **Task sketch**: `/signin` resolves from `GET /api/workspaces`; keep the
  session cache but revalidate it against the list on every mount; a cached id
  the caller no longer belongs to is discarded (this is also the "removed
  member loses access immediately" half of NW-58).

### NW-04 — `GET /api/workspaces/{tenantId}/members`

- **Source**: NW-04, `inputs/next/next-waves-todo.md` §1.
- **Raw**: "invite POST is real; table is `memberStore.ts` session cache."
- **Today (evidence)**:
  `../backend/src/Contigo.Api/WorkspaceEndpointExtensions.cs:31-32` — no member
  list route. `../web/src/routes/workspace/members/memberStore.ts:57,77` reads
  and writes `sessionStorage`; `memberViewModel.ts:39` defines
  `INVITATION_SENT_MESSAGE = "Invitation sent."` and
  `InvitePane.tsx:93,103` renders it after the POST.
- **Gap**: the Members table is this session's optimistic echo of invites made
  in this tab. Another Admin, another browser, or a reload sees a different
  roster than Postgres holds.
- **Seats**: software-architect (endpoint + OpenAPI + client); security-architect
  (who may read the roster and what a non-Admin sees — screens-v2 §10 has a
  "request access" state for Procurement); client-architect (`memberStore.ts`
  is deleted, the table is a server read).
- **ADR touchpoints**: amend ADR-009 (member list is tenant-scoped) — ADR-010
  unchanged.
- **Design refs**: `screens-v2.md` §10 — table columns `name · email · role ·
  status`; `app.jsx:133-134` shows the `Invited` vs `Active` status values the
  wire must carry.
- **Acceptance**: **N3** — invite Procurement → the other account's picker lists
  the workspace; Members matches Postgres after a reload and from a second
  browser.
- **Proposed epic**: epic-14-workspace-identity.
- **Task sketch**: `GET /api/workspaces/{tenantId}/members` →
  `[{ name?, email, role, status }]` where `status` is `Invited` until
  `LinkSignInAsync` has run; OpenAPI + generated client; `MembersTable` reads it.

### NW-58 — Invites are email + link; login joins that workspace; Admin remove requires a new invite

- **Source**: NW-58, `inputs/next/next-waves-todo.md` §1 (observed 2026-09-10).
  The largest item in the wave and the one with a real scope conflict — see
  OQ-w14-002.
- **Raw**: "They must go **by email**: the invitee receives a mail with a
  **link**, clicks it, **signs in**, and **joins that workspace**. They stay a
  member until an Admin **removes** them. Re-adding them needs a **new
  invite**." Plus: "UI must not say 'sent' unless the mail left."
- **Today (evidence)**:
  - `../backend/src/Contigo.Api/WorkspaceEndpointExtensions.cs:59-97` —
    `POST …/invites` validates the email and role and calls
    `WorkspaceMembershipService.InviteAsync`. Nothing else.
  - `../backend/src/Contigo.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs:56-108`
    writes `workspace_user` (`:80-88`) + `workspace_membership` (`:98-105`).
    No token, no expiry, no accept URL. A repeat invite of the **same** role
    fails at `:90-96` ("already holds the {role} role in this workspace").
  - **No mail transport exists anywhere**: a repo-wide grep for
    `smtp|sendgrid|MailKit|Graph…sendMail|IEmailSender|communication.services`
    over `backend/src` and `infra` returns **zero** files.
  - `../backend/src/Contigo.Identity.Workspace/Domain/WorkspaceSignIn.cs:28-44`
    and `WorkspaceMembershipService.LinkSignInAsync:118-145` implement the
    "first sign-in after being invited" link — but **no host endpoint calls
    them** (they appear only in
    `../backend/tests/Contigo.Identity.Workspace.Tests/WorkspaceSignInTests.cs`).
  - **No membership DELETE**: `grep -n "MapDelete" backend/src/Contigo.Api/`
    finds only `DocumentsEndpointExtensions.cs:87`.
  - `../web/src/routes/workspace/members/InvitePane.tsx:93,103` says
    "Sending…" then "Invitation sent." unconditionally on a 201.
- **Gap**: the invitee is never told. Even if they sign in, discovery is
  `localStorage` (NW-01), so they land on "create a workspace" instead of the
  one they were invited to. An Admin cannot remove anyone, and a removed
  person could not be re-invited at the same role even if they could.
- **Seats**:
  - product-owner — the scope conflict: `percorso-pilota-v1.md` §2 lists
    **"email"** under *"Non è onboarding"*, while spec §20 Day 1 says "Create a
    workspace and invite Procurement users". The PO fixes what "invited" means
    for the pilot and the acceptance wording of N3b.
  - software-architect — invitation entity (token, role, expiry, single use),
    `POST …/invites` returns the accept URL, `POST /api/invites/{token}/accept`,
    `DELETE …/members/{id}`, re-invite after removal; OpenAPI + client.
  - security-architect — a signed, single-use, expiring token (secret in Key
    Vault, ADR-011); accept links the Entra subject to the invited email via
    `LinkSignInAsync`; removal revokes access immediately (RLS + NW-01/NW-03);
    an Admin may not remove the last Admin; a token must not leak tenant data
    before acceptance.
  - cloud-architect — if a real mail transport is in scope, it is a **new Azure
    resource** (ADR-005 lists none) plus a Terraform module (ADR-007), a
    managed identity or Key Vault secret, and a per-env cost line. Locked rule:
    cheapest tier that satisfies the spec, nothing idle-expensive.
  - client-architect — the accept route (`/invite/accept?token=…`) and its
    unauthenticated → sign-in → join → land-in-workspace flow; Members table
    writes go to the server.
  - ux-ui-designer — copy: "Invitation sent." only when it left; the copyable
    link fallback; `Invited` vs `Active` vs removed states; the Procurement
    "request access" state (screens-v2 §10).
  - delivery-manager — Terraform/CI must run before the feature can work on
    `dev`; per-env config and secret plumbing across `dev` → `demo` (ADR-016).
- **ADR touchpoints**: **new ADR** for the invitation lifecycle (token, accept,
  removal, transport) is likely; amend ADR-005 + ADR-007 if a mail resource
  lands; amend ADR-011 (token signing key); amend ADR-020 (screen 10 copy);
  amend ADR-001 if the PO rules email in or out of the pilot.
- **Design refs**: `screens-v2.md` §10; `markup.html:395` (role radio "Also
  uploads, deletes, manages members") and `:397` ("Send invitation");
  `app.jsx:133-134,169-170` (`sendInvite`, `status: Invited`, `tag-accent`).
- **Acceptance**: **N3b** — invite sends an email with a link; the invitee
  clicks, signs in, enters **that** workspace (not a new one); an Admin removes
  them → access gone; re-join only after a **new** invite. If the council rules
  out a mail transport for w14 (OQ-w14-002), N3b's first clause becomes "the
  Admin is given a copyable accept link and the UI does **not** claim a mail was
  sent" — everything after it is unchanged and still in the wave.
- **Proposed epic**: epic-15-workspace-invitations.
- **Task sketch**:
  - `workspace_invitation` (token hash, email, role, expiry, `accepted_at`,
    `revoked_at`) + migration under ADR-021's checked-in idempotent SQL.
  - `POST …/invites` issues the token and returns the accept URL;
    `GET /api/invites/{token}` (pre-accept, tenant name only);
    `POST /api/invites/{token}/accept` calls `LinkSignInAsync` + membership.
  - `DELETE /api/workspaces/{tenantId}/members/{membershipId}` with the
    last-Admin guard; a removed email may be invited again at the same role.
  - `IInvitationMailer` seam + the transport the council picks (or the copyable
    link fallback).
  - Web: `/invite/accept` route; Members table + invite copy from the server.

### NW-24 — Workspace has no currency / region (HITL)

- **Source**: NW-24, `inputs/next/next-waves-todo.md` §3 — the raw file marks it
  `(HITL)`.
- **Raw**: "Workspace has no currency / region (HITL)".
- **Today (evidence)**:
  `../backend/src/Contigo.Identity.Workspace/Domain/WorkspaceTenant.cs:24-28` —
  the entity has `Name` and `CreatedAt` and nothing else.
  `../web/src/routes/signin/WorkspacePickerScreen.tsx:60-90` — the create form
  submits `{ name }` only. `../web/src/routes/signin/workspaceStore.ts:47-55`
  carries an optional `currencyRegion?` that is never populated, with a comment
  naming the missing backend column.
- **Gap**: the prototype's pick row reads "3 validated contracts · **CHF ·
  eu-west**" and `percorso-pilota-v1.md` §2 step 1 asks for "form nome /
  industria / paese". Neither exists. Currency also has downstream effects
  (savings and quote figures are currency-tagged today per contract, not per
  workspace).
- **Seats**: product-owner (which fields the pilot actually needs, and whether
  a workspace currency overrides or only defaults a contract's own currency);
  software-architect (columns + idempotent migration + API surface, ADR-003 /
  ADR-021); ux-ui-designer (create form and picker row copy, screens-v2 §1).
- **ADR touchpoints**: amend ADR-003 (new columns) and ADR-020 (screen 1 shows
  them); `none` for ADR-005 — "region" here is a business region, not an Azure
  region (ADR-006 stays `northeurope`).
- **Design refs**: `screens-v2.md` §1; `markup.html:63`.
- **Acceptance**: **W14-A2** — create a workspace with country and currency;
  the picker row shows "N validated contracts · {currency} · {region}" and the
  values survive a reload and a second browser.
- **Proposed epic**: epic-14-workspace-identity.
- **Task sketch**: `country` + `currency` (+ optional `industry`) on
  `workspace`; create form fields; NW-01 payload carries them; picker row
  renders them.

---

### Queued items (no task in w14) — status and evidence only

Recorded so a later wave's intake can pick them up as carry-over. **The
decomposer writes no task for any item in this section.** Grouped by the raw
file's own §7 proposal.

**W15 — API JWT (ADR-010)**

- **NW-05** — OPEN. No `AddJwtBearer` / `AddAuthentication` /
  `JwtBearerDefaults` call exists in any `.cs` under `../backend/src`; tenant is
  `X-Tenant-Id` and actor `X-User-Id`, read straight off `HttpRequest.Headers`
  (`../backend/src/Contigo.Api/DocumentsEndpointExtensions.cs:69-70`;
  `ConversationsEndpointExtensions.cs:360`). `../backend/README.md:198-211` is
  the canonical prose statement of the interim posture.
- **NW-06** — OPEN. `../web/src/components/shell/workspaceRole.ts:40-46` reads
  `?role=` (only `admin`/`procurement`), mirrors it into `sessionStorage` and
  defaults to `"admin"`. *Partly relieved by w14*: once NW-01 returns the
  caller's role per workspace, the Admin-only affordances stop guessing — see
  NW-14. The `?role=` override itself dies with NW-05.
- **NW-07** — PARTIAL. `../backend/src/Contigo.Api/ConversationsEndpointExtensions.cs:372-399`
  tries `ClaimsPrincipal` `NameIdentifier`/`sub` first (never authenticated
  today), then the required `X-User-Id` header; a missing header is a 400.
  Conversations *are* stored per user (`../backend/src/Contigo.Chat/Domain/Conversations/Conversation.cs:16`),
  just keyed on a non-authoritative header (OQ-askv2-005).
- **NW-08** — OPEN. `../backend/src/Contigo.Api/AuditEndpointExtensions.cs:22-36`
  binds a `ClaimsPrincipal` and requires Admin via
  `WorkspacePrincipalAuthorization.TryAuthorize`; with no auth wired it always
  401s. Absent from `../web/openapi/contigo-api.v1.json` (28 paths).
- **NW-31** — PARTIAL. `WorkspaceRoleResolver.cs:37-38,72` reads both `X-Role`
  and `X-Workspace-Role`; `../backend/src/Contigo.Api/CapabilitiesEndpointExtensions.cs:54`
  reads only `X-Role`. Two spellings, two readers.
- **NW-32** — OPEN. `DocumentsEndpointExtensions.cs:71,569-574` writes the
  literal `"unattributed"` as the audit actor when `X-User-Id` is absent; the
  same constant is used by
  `../backend/src/Contigo.Savings/Application/SavingsOpportunityService.cs:95,165,316`.

**W16 — No session as source of truth**

- **NW-10** — OPEN. `../web/src/components/shell/RailNav.tsx:12,67` is the only
  remaining reader of `loadTrackedDocuments()`
  (`../web/src/routes/documents/documentStore.ts:31,67`,
  `sessionStorage["contigo.documents.readback"]`); nothing writes that key any
  more, so the badge reads empty while `/documents` lists files.
- **NW-11** — OPEN. `../backend/src/Contigo.Api/RenewalsEndpointExtensions.cs:94-96`
  maps `GET /api/renewals`, `GET …/{contractId}/priority`,
  `POST …/{id}/action`. `RenewalActionService.GetActionAsync` exists but is
  unrouted; `../web/src/routes/renewals/renewalActionStore.ts:8-19` mirrors
  posted actions in `sessionStorage`.
- **NW-12** — OPEN. `../backend/src/Contigo.Api/QuotesEndpointExtensions.cs:35-39`
  maps upload / assessment / recalculate only — no `GET /api/quotes`, no
  `GET /api/quotes/{id}`; `../web/src/routes/quotes/quoteOutcomeStore.ts:31`
  keeps outcomes in `sessionStorage`.
- **NW-13** — OPEN (**no longer this-branch-only**: PR #75 `27dfe75` is an
  ancestor of HEAD). `../web/src/routes/contracts/contract360/negotiationStepsStore.ts:10-38`
  stores four booleans per contract under
  `sessionStorage["contigo.contract360.steps.<id>"]`; no endpoint records them.
- **NW-21** — PARTIAL. `../backend/src/Contigo.Api/NegotiationsEndpointExtensions.cs:86-95`
  calls `NegotiationOutcomePropagationService.PropagateAsync` **only** when the
  body carries `savingsOpportunityId`; the web never sends one
  (`../web/src/routes/quotes/quoteOutcomeStore.ts:16`), so a recorded outcome
  never moves the Savings KPI.

**W17 — Domain completeness (Contract 360)**

- **NW-20** — OPEN. `../backend/src/Contigo.Api/ContractsEndpointExtensions.cs:298-310`
  emits `benchmark = Array.Empty<object>()` and `activity = Array.Empty<object>()`
  unconditionally.
- **NW-22** — OPEN. `../backend/src/Contigo.Renewals/Application/RenewalPipelineBuilder.cs:89-92`
  hardcodes `MarketPosition: null` (with `AnnualUpliftPercent` and
  `PotentialSavingsRange`) for every item.
- **NW-23** — OPEN. `../backend/src/Contigo.Documents.Contracts/Application/PortfolioFilter.cs:11-18`
  has no `Category`; `../backend/src/Contigo.Api/PortfolioEndpointExtensions.cs:170`
  parses supplier/status/risk/autoRenewal/spend/renewal dates only.
- **NW-25** — OPEN. `../web/src/routes/savings/index.tsx` renders header + KPI
  band + a flat `OpportunitiesTable`; no filter, search or sort control exists
  in that folder.
- **NW-26** — OPEN. `PlaceholderDocumentPreviewRenderer` is the only
  `IDocumentPreviewRenderer` in the tree
  (`../backend/src/Contigo.Documents.Contracts/Infrastructure/ServiceCollectionExtensions.cs:125-127`)
  and `Preview/DocumentPreviewService.cs:54` falls back to
  `RenderPlaceholder("FILE")`.
- **NW-62** — OPEN. `../web/src/routes/contracts/contract360/contract360ViewModel.ts:150-152,160-199`
  reads `renewals[].insightCard.recommendations.potentialSavingsRange` /
  `marketPosition` (null — NW-20/NW-22) and `header.cancellationDeadline`, else
  the literal `"Not determined"`; `AnswersBand.tsx:50-71` prints
  `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE`; `WhyClauses.tsx:39-61`
  is an unfiltered dump of `tabs.clauses`.
- **NW-63** — OPEN. No document viewer exists in `../web/src` (no iframe /
  pdfjs / canvas / react-pdf anywhere); `getDocumentPreviewUrl`
  (`../web/src/api/client.ts:966-970,1393-1405`) is called by zero components;
  highlighting is text-level `<mark>` (`ClauseHighlight.tsx:19-36`,
  `EvidencePane.tsx:141-160`) and the persisted model keeps only `SourceSpan`
  (string) + `SourcePage` (int) —
  `../backend/src/Contigo.Documents.Contracts/Domain/ExtractionEvidence.cs:46-48` —
  no bounding boxes.
- **NW-64** — OPEN. `../web/src/routes/contracts/review/reviewViewModel.ts:245`
  `continue`s when both the value and the proposal are empty, so an
  un-extracted field vanishes from Review;
  `../web/src/routes/contracts/contract360/contract360ViewModel.ts:500-508`
  (`computeNeedsAttention`) returns early on `confidence === null`.
- **NW-65** — OPEN. `../web/src/routes/contracts/contract360/DetailsSection.tsx:51,77-93`
  is the two-column grid with "Facts you still need to decide" + "Review all →";
  `NO_ATTENTION_MESSAGE` still says "above 95 %" while the (uncommitted)
  `../web/src/styles/semantics.ts:29-52` auto-accepts at ≥90 %.
- **NW-66** — OPEN. `../web/src/routes/contracts/contract360/WhyClauses.tsx:43-64`
  puts the source string (`p.N · §span`) and a raw-`riskLevel` tag plus a
  confidence tag on each row; the quote itself is in the `ClauseHighlight` card
  below; there is no "open in viewer" affordance.

**W18 — Contract, ops, and the Ask/Quote product residuals**

- **NW-27** — OPEN. `../backend/src/Contigo.Api/DocumentsEndpointExtensions.cs:265-280`
  awaits `processingPipeline.ProcessAsync(...)` inline before returning 201;
  `../backend/src/Contigo.Worker/Queue/InMemoryQueueConsumer.cs:5-16` is an
  in-process `ConcurrentQueue` with nothing enqueuing document work.
- **NW-30** — OPEN. `../web/openapi/contigo-api.v1.json` `/api/conversations`
  `post` declares parameters but no `requestBody`; `/api/audit` is absent.
- **NW-40** — PARTIAL. `../infra/environments/dev/main.tf:121-147` passes
  `ai_gateway_endpoint` / `project_name` / `document_intelligence_connection` /
  `model_env` from `module.foundry`, and three `azurerm_role_assignment` blocks
  exist (`../infra/modules/foundry/main.tf:244,261,281`) — but publication is
  still gated by `ai_gateway_wired` (default `false`).
- **NW-41** — PARTIAL. `../.github/workflows/seed-market-intelligence.yml:1-40`
  runs `ingest-market --feed backend/fixtures/market-intelligence.mock.json`,
  but is `workflow_dispatch` only.
- **NW-50** — PARTIAL. `../web/e2e/day1.spec.ts:124-228` exists and drives V2
  affordances, but `../.github/workflows/web.yml:71-81` runs only
  `npm ci` / `npm run build` / `npm test` — no Playwright step in any workflow,
  so "red" is unproven either way.
- **NW-55** — OPEN. `../web/src/routes/ask/reply/CitationCard.tsx:38-44` renders
  an `<img>` or the "No page preview available" block for every citation kind,
  and all twelve `new PackItem(...)` constructions in
  `../backend/src/Contigo.Api/AskCopilotService.cs` pass `null` for
  `PreviewUrl` — the placeholder ships 100 % of the time.
- **NW-56** — PARTIAL. `../web/src/routes/contracts/contract360/Contract360Header.tsx:49`
  links to `/ask?scope=<id>` with no `state.query`; the scope *is* honoured for
  supplier-named chips (`askViewModel.ts:338-341`) and for
  `createConversation({scopeContractId})`, but the `!kbReady` early return at
  `../web/src/routes/ask/index.tsx:256` renders `AskOffState` before `?scope=`
  is ever consulted.
- **NW-57** — OPEN. `../web/src/routes/quotes/TargetStep.tsx:62-74` are editable
  free-text target / walk-away numbers; `NegotiationStep.tsx:85-89,191-193` ends
  on "See it in Savings →"; there is no quote history route or list endpoint;
  with no fixture match
  `../backend/src/Contigo.Benchmark/Fixtures/FixtureBenchmarkAdapter.cs:207,312`
  returns a null distribution which `MarketAssessmentCalculator.cs:45-54`
  classifies `InsufficientBenchmarkData`.
- **NW-59** — OPEN. `../web/src/routes/ask/reply/replyTypes.ts:102-116` types
  `AbstainReply`/`ErrorReply` as `{kind, reason}` only and
  `ReplyBody.tsx:72-79` renders the block with no CTA — and
  `askViewModel.ts:137-142` **discards** actions the backend does attach on the
  zero-portfolio abstain path (`AskCopilotService.cs:287-295`).
- **NW-60** — OPEN. `../web/src/components/shell/AppShell.tsx:43` renders
  `GlobalAskBar` above the outlet on every shell route;
  `GlobalAskBar.tsx:58-63` navigates with `state.query`; `ask/index.tsx:89,212-219`
  sets `askedInitialQuery` once and never resets it, so a second bar submit
  while `AskRoute` is mounted lands on a blank chat and drops the question.
- **NW-61** — PARTIAL. The optimistic row, real server stages and 2 s polling
  all work (`../web/src/routes/documents/DocumentStatusTable.tsx:86-110`,
  `useDocumentsList.ts:105-119,131-141`); what is missing is downstream gating —
  `../web/src/routes/contracts/contract360/index.tsx:72-115` renders whatever
  the aggregate returns with no completeness check.

### Out

- **NW-51** — **CLOSED-ON-MAIN**. `27e09cb`, `5ab160a` and `57a82e6` (PRs #75,
  #76) are ancestors of HEAD; every aligned route folder is on disk and
  `web/e2e/v2.spec.ts` exists. Do not re-open.
- **NW-52** — **DEFERRED** and out of scope: ADR-001 §1.2 / INDEX one-liner —
  "R3/R4 on fixture adapter, **never a paid API for first `demo`**".
- **NW-53** — **DEFERRED**, ADR-013 (mobile is a non-gating lane, no store
  release for R0–R4).
- **NW-54** — **DEFERRED** by the raw file; `ia-v2.md` §"Roles on the pilot
  path" keeps Legal / Finance / Read-only in the model, not in the nav.

## 3. Seat roster for this wave

| Seat | Involved | Items |
|---|---|---|
| product-owner | yes | NW-58 (email is under *"Non è onboarding"* in `percorso-pilota-v1.md` §2 while spec §20 Day 1 says "invite Procurement users" — the PO fixes what "invited" means and the wording of N3b), NW-24 (which workspace profile fields the pilot needs) |
| software-architect | yes | NW-01, NW-02, NW-04, NW-09, NW-24, NW-58 — three new endpoints, an invitation entity + migration, a membership write inside tenant bootstrap, and the OpenAPI/generated-client contract that carries them |
| cloud-architect | yes | NW-58 only — a real mail transport is a **new** Azure resource (ADR-005 lists none), a Terraform module (ADR-007), an identity or Key Vault secret, and a per-env cost line under the locked "cheapest tier, nothing idle-expensive" rule |
| security-architect | yes | NW-01, NW-02, NW-04, NW-14, NW-58 — every item in this wave decides who may see or change a tenant: membership as authorization, the interim identity key, a signed single-use invitation token, immediate revocation on removal, last-Admin guard (ADR-009/010/011/022) |
| client-architect | yes | NW-01, NW-03, NW-04, NW-09, NW-14, NW-58 — three client stores (`workspaceStore`, `memberStore`, role inference) stop being the source of truth, a new `/invite/accept` route appears, and the read-back contract for picker/rail counts is fixed |
| ux-ui-designer | yes | NW-24, NW-58 — screen 1's pick row (`currency · region`) and screen 10's invite copy and member states; "Invitation sent." must not be said unless it was |
| delivery-manager | yes | W14-01 (the wave must branch from the post-rebrand `origin/main`), NW-58 (Terraform/CI and the per-env secret must land before the feature works on `dev`; promotion to `demo` per ADR-016) |

No seat is listed "just in case": every row above names the decision it owns.

## 4. Proposed epics (append-only, next free numbers)

`Glob reports/workitems/epic-*` → `epic-01` … `epic-13` exist. Next free: 14, 15.

| Epic | Slug | Theme | Items | Extends |
|---|---|---|---|---|
| epic-14 | workspace-identity | The workspace is a server fact: membership, list, current, members, count, role, profile | W14-01, NW-02, NW-01, NW-14, NW-09, NW-03, NW-04, NW-24 | epic-01 F05 (workspace roles), epic-06 F03 (sign-in / picker) |
| epic-15 | workspace-invitations | Invitation lifecycle: token, email/link, accept-and-join, remove, re-invite | NW-58 | epic-01 F05, epic-06 F04 (members & roles) |

## 5. Selection for this wave (cap 20 tasks / 5 phases)

- **In wave** (priority order, dependencies first):
  1. **W14-01** — base the wave on the post-rebrand `origin/main` (blocks
     everything; an operator step at HITL, not a fan-out task).
  2. **NW-02** — creator membership (unblocks NW-01, NW-04, NW-14).
  3. **NW-01** — `GET /api/workspaces` (unblocks NW-03, NW-09, and NW-58's
     "the invitee lands in that workspace").
  4. **NW-14** — Delete / Retry 403 (demo-blocking, reproduced on `dev`).
  5. **NW-09** — picker count from the server.
  6. **NW-04** — `GET …/members`.
  7. **NW-58** — invitation lifecycle (largest; may span two phases).
  8. **NW-03** — current workspace from the server list.
  9. **NW-24** — workspace currency / region.
- **Queued** (recorded above with evidence; **no task in `w14`**): NW-05, NW-06,
  NW-07, NW-08, NW-31, NW-32 (W15) · NW-10, NW-11, NW-12, NW-13, NW-21 (W16) ·
  NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66 (W17) ·
  NW-27, NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60, NW-61
  (W18).
- **Out**: CLOSED-ON-MAIN: NW-51. DEFERRED: NW-52, NW-53, NW-54. Out of scope
  (ADR-001 §1.2 non-goals / "never a paid API for first `demo`"): NW-52.
- **Order constraints**:
  - The raw file's §7 says "**Order:** W15 (token) then W14 (create membership
    from `sub`) then W16", with its own fallback: "W14 can land membership
    using `X-User-Id` as an interim if JWT slips." This run **is** w14 and
    NW-05 is not in it, so the fallback is in force — see **OQ-w14-001**. Every
    identity read must go through one resolver seam so W15 swaps the token
    subject in without touching callers.
  - **NW-02 before NW-01, NW-04, NW-14** — without the membership row all three
    return empty or 403.
  - **NW-01 before NW-03, NW-09, and NW-58's accept step** — the list is what
    "enter the workspace" and "the invitee lands there" both read.
  - **W14-01 before every task** — paths otherwise do not resolve.
  - **NW-58's infra (if a mail transport is chosen) before NW-58's feature** —
    delivery-manager: Terraform + per-env secret land first, in their own phase.
- **Budget note**: nine items against a 20-task cap. NW-58 is the risk (an
  entity + migration + three endpoints + a web route + possibly a Terraform
  module). If the decomposer must cut to stay inside the cap, drop **NW-24**
  first (smallest product value, explicitly HITL in the raw file), then NW-03
  (its N4 is partly covered by NW-01). Never cut NW-02, NW-01 or NW-14 — the
  wave's own success sentence depends on them.

## 6. Superseded work items

| Existing item | Superseded by | Why |
|---|---|---|
| none | | The raw file contains no "cancels / replaces" statement. Its §0 "Already on `origin/main` (do not re-open)" list is a *do-not-reopen* note, not a cancel: it names e13 work that already shipped, and no existing epic or task file is made obsolete by any w14 item. The backlog is append-only; no status banner is written this wave. |

## 7. Open questions and assumptions in force

- **OQ-w14-001** — Which identity keys `workspace_membership` in w14, given that
  ADR-010's JWT (NW-05) is queued for W15 and the raw file's own order puts W15
  first? **Assumption**: w14 uses the interim `X-User-Id` (MSAL account
  username, ADR-022 / OQ-askv2-005) as the membership key, behind **one**
  resolver seam (`WorkspaceRoleResolver`'s membership branch is already that
  seam, `WorkspaceRoleResolver.cs:85-119`), so W15 replaces the header with the
  token `sub`/`oid` without touching any caller. `X-Role` / `X-Workspace-Role`
  must never become the product answer for an Admin-only action.
- **OQ-w14-002** — Is a real email transport in scope for w14?
  `inputs/percorso-pilota-v1.md` §2 lists **"email"** under *"Non è
  onboarding"*, while `inputs/product-spec.md` §20 Day 1 requires "invite
  Procurement users" and NW-58 requires a mail with a link. A transport is a new
  Azure resource (none in ADR-005), a Terraform module (ADR-007) and a per-env
  secret. **Assumption**: w14 lands the **full token → accept → join → remove →
  re-invite** path behind an `IInvitationMailer` seam; the transport itself is
  the council's call at the table (cloud-architect + product-owner). If no
  transport lands in w14, the Members UI must show the **copyable accept link**
  and must **not** say "Invitation sent." (NW-58's own must #1), and N3b is
  read with that substitution.
- **OQ-w14-003** — Which commit does the wave branch from?
  `origin/main` @ `25b10da` renamed every `Contigo.*` module to `Raffa.*`
  (PR #77, `87b7976`); this checkout `1650213` is pre-rebrand and carries the
  `.helix` next-wave process that `origin/main` does not.
  **Assumption**: the operator rebases/merges `helix/next-wave-process` onto
  `origin/main` **before** fan-out; every path in this document is then read
  with `Contigo.X` → `Raffa.X` and `contigo-v2/` → `raffa-v2/`. Every w14 gap
  was re-verified on `origin/main` and is unchanged by the rename.
- **OQ-w14-004** — Which workspace profile fields (NW-24)?
  `percorso-pilota-v1.md` §2 step 1 asks for "nome / industria / paese";
  `contigo-v2/markup.html:63` shows "CHF · eu-west".
  **Assumption**: `country` and `currency` are added as first-class columns and
  drive the picker row; `industry` is optional free text; "region" is a business
  region and does **not** touch ADR-006 (`northeurope` stays). A workspace
  currency is a *default* for display, never an override of a contract's own
  stored currency.
- **OQ-w14-005** — Who may list and remove members (NW-04, NW-58)?
  **Assumption**: any member may read the roster (Procurement sees it read-only
  with the "request access" state of `screens-v2.md` §10); only an Admin may
  invite or remove; an Admin may not remove the last Admin; a removed member
  loses access on the next request (NW-01 / NW-03 revalidation), not at token
  expiry.
