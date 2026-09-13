# w14 — HITL review page

Wave **w14 — "Workspace is real"**. Written by `next-decomposer` on
2026-09-11. Source `inputs/next/next-waves-todo.md`; requirements
`reports/context/waves/w14-requirements.md`; council
`reports/architecture/waves/w14.md` (**approved**, nine decisions, all seven
seats). Caps 20 tasks / 5 phases → **11 live tasks in 5 phases, 10 stories, 2
new epics**, ~15.8M tokens estimated.

```
python scripts/register_wave.py --wave w14      -> exit 0
python scripts/check_single_writer.py --slice w14 -> exit 0
```

---

## 1. What to review

| Path | What it is |
|---|---|
| `reports/workitems/epic-14-workspace-identity/**` | 1 epic, 6 features, 8 stories, 9 tasks |
| `reports/workitems/epic-15-workspace-invitations/**` | 1 epic, 2 features, 2 stories, 2 tasks |
| `reports/workitems/BACKLOG.md` | two epic rows, the ADR coverage table, the `## Wave w14` section |
| `reports/workitems/epic-01-platform/.../tasks/task-02-membership-invite.md` | **the only existing work item touched**: `status: superseded` + a footer. Body byte-identical |
| `reports/plan/slices/w14.yaml` | the wave the fan-out walks |
| `reports/plan/slices/MANIFEST.yaml` | one appended row (`w14`, `previous: e13`) |
| `reports/plan/slices/INDEX-next.md` | refreshed by `register_wave.py` |
| `reports/open-questions.md` | one appended entry, **OQ-w14-dec-001** |

Nothing else under `reports/` was written. `slice.current.yaml`,
`wave-spec.*.yaml` and every existing `slices/*.yaml` are untouched.

---

## 2. Items in the wave

| Item | Kind | Task ids | Phase(s) |
|---|---|---|---|
| **W14-01** wave base is the post-rebrand `origin/main` | ops | **no task** (operator act at HITL) — its proof is recorded by `E14/F06/US01/T01` | — |
| **NW-02** creator membership (Admin) | bug | `E14/F02/US01/T01` | 1 |
| **NW-01** `GET /api/workspaces` | feature | `E14/F01/US01/T01`, `E14/F01/US01/T02`, `E14/F03/US01/T01`, `E14/F03/US02/T01`, `E14/F05/US01/T01` | 1, 2, 4 |
| **NW-14** Delete / Retry 403 for the creator | bug | `E14/F02/US02/T01`, `E14/F03/US02/T01` | 2, 4 |
| **NW-09** picker count frozen at 0 | bug | `E14/F03/US01/T01`, `E14/F03/US02/T01` | 2, 4 |
| **NW-04** `GET …/members` | feature | `E14/F04/US01/T01`, `E15/F02/US01/T01` | 2, 4 |
| **NW-58** invitation lifecycle | feature | `E14/F01/US01/T02`, `E14/F02/US01/T01` (the §D.1a guard), `E15/F01/US01/T01`, `E14/F03/US02/T01` (accept screen), `E15/F02/US01/T01` | 1, 3, 4 |
| **NW-03** current workspace is a server fact | change | `E14/F03/US02/T01`, `E14/F05/US01/T01` | 1, 4 |
| **NW-24** workspace currency / region | change | `E14/F01/US01/T02`, `E14/F03/US01/T01`, `E14/F03/US02/T01` | 1, 2, 4 |
| — final integration + acceptance runbook | — | `E14/F06/US01/T01` | 5 |

### The phase plan and why it is this shape

```
prereq (HITL, no task) : W14-01                 -> reports/plan/gates/w14.hitl-ok

P1  E14/F01/US01/T01  rls-identity-guc          app.identity_subject via set_config      deps: -
    E14/F01/US01/T02  workspace-schema          3 migrations -> identity-workspace.sql    deps: -
    E14/F02/US01/T01  workspace-bootstrap       creator membership + the invite guard
                                                + the route-group split                   deps: -
    E14/F05/US01/T01  membership-seed-and-backfill  demo seed rows + guard + backfill wf   deps: -

P2  E14/F03/US01/T01  workspaces-directory-api  GET /api/workspaces (+role +count +profile)
                                                + the identity scope (BeginIdentityScope)
                                                + the phase-2 OpenAPI/client contract     deps: P1 x3
    E14/F04/US01/T01  workspace-roster-api      GET …/{tenantId}/members                  deps: T02, T01(F02)
    E14/F02/US02/T01  admin-role-from-membership  header branch deleted + N9 proof         deps: T01(F02)

P3  E15/F01/US01/T01  invitation-lifecycle-api  invites / accept / DELETE x2 + last-Admin
                                                + IInvitationMailer + phase-3 contract    deps: T02, P2 x2

P4  E14/F03/US02/T01  web-workspace-resolution  App gate + picker + shell + /invite/accept deps: P2, P3
    E15/F02/US01/T01  web-members-and-invites   roster + honest copy + revoke/remove       deps: P2, P3

P5  E14/F06/US01/T01  w14-integration           build, no-drift, e2e, acceptance runbook   deps: all leaves
```

Three constraints fixed this shape and each one is load-bearing:

1. **The invite guard must merge before NW-01** (ADR-025 OQ-sec-001): this wave
   converts a dormant cross-tenant hole into a working join path. It cannot ship
   alone earlier, because it resolves `Admin` **from the membership row** — so it
   folds into the phase-1 task that already owns
   `WorkspaceEndpointExtensions.cs`. Zero extra task cost.
2. **`web/openapi/raffa-api.v1.json` + `schema.ts` take one owner per phase**
   (`schema.ts` is regenerated wholesale, so parallel edits produce a conflicting
   artefact, not a mergeable diff). That allows exactly two backend contract
   phases inside the 5-phase cap: P2 (reads) and P3 (invitations).
3. **`App.tsx` cannot route to a component another task creates in the same
   phase** (the e13 union-merge defect). The accept screen therefore ships in the
   same task as the `BrowserRouter` hoist, and the members screen is its
   file-disjoint sibling.

**`E14/F05/US01/T01` is `deps: []` in phase 1 deliberately** — a correction to
the delivery-manager's own lane plan, which had it at phase 3. It has no schema
dependency (`workspace_user` and `workspace_membership` already exist) and it is
the wave's only deliverable a fan-out task cannot verify.

---

## 3. Single-writer table (`check_single_writer.py --slice w14` → exit 0)

State-carrying files, and who owns each in which phase:

| File | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 |
|---|---|---|---|---|---|
| `backend/src/Raffa.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs` | `E14/F01/US01/T01` | — | — | — | — |
| `backend/src/Raffa.SharedKernel/Tenancy/ITenantContext.cs` (the `ICallerIdentityContext` seam) + `CallerIdentityContext.cs` (new, P1) | `E14/F01/US01/T01` | — | — | — | — |
| `backend/src/Raffa.Identity.Workspace/Migrations/**` + `Scripts/identity-workspace.sql` | `E14/F01/US01/T02` | — | — | — | — |
| `backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs` | `E14/F02/US01/T01` (splits it) | `E14/F03/US01/T01` | — | — | — |
| `backend/src/Raffa.Api/WorkspaceMembersEndpointExtensions.cs` (new, P1) | `E14/F02/US01/T01` | `E14/F04/US01/T01` | `E15/F01/US01/T01` | — | — |
| `backend/src/Raffa.Api/WorkspaceInvitesEndpointExtensions.cs` (new, P1) | `E14/F02/US01/T01` | — | `E15/F01/US01/T01` | — | — |
| `backend/src/Raffa.Api/Program.cs` | `E14/F02/US01/T01` (**only if** the DI line cannot go elsewhere) | — | `E15/F01/US01/T01` (exactly one line) | — | — |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceProvisioningService.cs` | `E14/F02/US01/T01` | `E14/F03/US01/T01` | — | — | — |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs` | — | `E14/F04/US01/T01` | `E15/F01/US01/T01` | — | — |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs` | — | `E14/F03/US01/T01` | `E15/F01/US01/T01` | — | — |
| `backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs` | — | `E14/F02/US02/T01` | — | — | — |
| `web/openapi/raffa-api.v1.json` + `web/src/api/generated/schema.ts` + `web/src/api/client.ts` | — | `E14/F03/US01/T01` | `E15/F01/US01/T01` | — | — |
| `web/src/App.tsx`, `components/shell/WorkspaceShellApp.tsx`, `AppShell.tsx`, `navItems.ts`, `workspaceRole.ts`, `useValidatedContractCount.ts`, `routes/signin/**`, `routes/invite/**` | — | — | — | `E14/F03/US02/T01` | — |
| `web/src/routes/workspace/members/**` | — | — | — | `E15/F02/US01/T01` | — |
| `web/e2e/day1.spec.ts` | — | — | — | `E14/F03/US02/T01` | `E14/F06/US01/T01` |
| `backend/scripts/demo-fixture-seed.sql`, `.github/workflows/seed-demo-fixture.yml`, `.github/workflows/backfill-workspace-membership.yml` | `E14/F05/US01/T01` | — | — | — | — |
| `docs/waves/w14-acceptance.md`, `web/e2e/invite.spec.ts` | — | — | — | — | `E14/F06/US01/T01` |

**Never written by any w14 task**:
`backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
(ADR-025 §I — editing it would ship a stale-authorization window in W15),
`infra/**`, `.github/workflows/backend.yml` / `web.yml` / `infra.yml` /
`demo-promote.yml` / `demo-config-check.yml` / `mobile.yml` /
`reprocess-tenant-documents.yml` / `seed-market-intelligence.yml`,
`backend/Raffa.slnx`, `DependencyDirectionTests.cs`,
`web/src/components/shell/RequireRole.tsx`,
`navItems.ts`'s `getDocumentsBadge` (NW-10, W16), `web/src/routes/ask/**`
(NW-56, W18).

### Two same-phase couplings, deliberate and documented in both task files

1. **Phase 2** — `E14/F03/US01/T01` writes the OpenAPI entry and the
   `getWorkspaceMembers()` client method whose **handler** its sibling
   `E14/F04/US01/T01` implements in a different file. No file is shared and none
   is created-and-named across the pair; the contract is fixed verbatim by
   ADR-026 §D3 in both task texts. This is what the "one contract owner per
   phase" rule costs, and it is the cheaper half of the trade.
2. **Phase 4** — `E14/F03/US02/T01` adds `workspaceId` to
   `WorkspaceShellAppProps`, passes `workspaceId` + `role` into `MembersRoute`,
   and removes the `RequireRole` wrap from the members route;
   `E15/F02/US01/T01` consumes those two props and renders the read-only
   Procurement variant. Both task files state the contract and both say **HALT
   and name the sibling** rather than reaching into the other's files.

### A third coupling was found by the checker and removed, not accepted

`next-checker` caught a **phase-1** coupling neither the decomposition nor
`check_single_writer.py` had flagged: `E14/F02/US01/T01`'s Coding objective §1
told the implementer to call **`ICallerIdentityContext.BeginIdentityScope`**, a
type its phase-1 sibling `E14/F01/US01/T01` *creates*
(`Raffa.SharedKernel/Tenancy/ITenantContext.cs` + `CallerIdentityContext.cs`).
Both tasks are `depends_on: []`, so the fan-out runs them concurrently from the
same base and the barrier union-merges: `E14/F02/US01/T01` could not compile
without stubbing the interface. That is the **e13 defect verbatim** —
`MarketEndpointExtensions.cs` created by one task and `MapMarketEndpoints()`
written by a sibling of the same phase, 18 CI errors.

`check_single_writer.py` returned 0 because its Rule 2 matches a created
**file path** in sibling text; this task named the **type**, so the textual
check could not see it. **The script is not wrong and was not changed** — a
type-level dependency inside one phase is outside what a path-matcher can
detect, which is why the two couplings above are documented in prose too.

**The fix moved the consumer one phase later, at zero task cost:**

| | before | after |
|---|---|---|
| opens the identity scope | `E14/F02/US01/T01` (phase 1) | **`E14/F03/US01/T01` (phase 2)** |
| creates `ICallerIdentityContext` | `E14/F01/US01/T01` (phase 1) | unchanged |
| creates `ICallerIdentity` | `E14/F02/US01/T01` (phase 1) | unchanged |

`E14/F03/US01/T01` already carries `depends_on: [rls-identity-guc,
workspace-schema, workspace-bootstrap]`, so **both** halves of the seam are on
`main` before it starts, and it is the wave's only reader of `workspace_user`
outside a tenant scope — the one place `app.identity_subject` has to be set. Its
two phase-2 siblings do **not** need it and gain no new dependency:
`E14/F04/US01/T01` is verify-then-scope on the route tenant and its DoD already
forbids driving the roster from `workspace_user`; `E14/F02/US02/T01` resolves
role inside `WorkspaceRoleResolver.cs:99`'s own tenant scope.

The scope is opened as a **call-site `using`** in the `GET /api/workspaces`
handler — the shape `Raffa.Api` already uses everywhere for tenancy
(`PortfolioEndpointExtensions.cs:156`, `WorkspaceRoleResolver.cs:99`,
`AskCopilotService.cs:132`) — **not** as middleware: there is no `UseMiddleware`
in `Raffa.Api` today and this wave does not introduce the first one.
`Program.cs` therefore gains nothing in phase 2 and the table above is unchanged
for it. No phase moved, no task was added or split, `max_tasks` and
`max_phases` are untouched (still 11 / 5), and `w14.yaml` did not change.

Three task files were edited: `E14/F02/US01/T01` (the call deleted, replaced by
an explicit prohibition + a `rg` DoD proving its branch never names the sibling
type, and a build-without-siblings check), `E14/F03/US01/T01` (the scope added
to §2 with the independence rule, the T1a row named as the GUC↔policy proof,
two new DoD greps), and this page.

---

## 4. Queued tasks

**Zero queued task files were written**, on the requirements file's own
instruction: `reports/context/waves/w14-requirements.md`, §"Queued items" —
"**The decomposer writes no task for any item in this section.**" The items are
recorded there with evidence and in `BACKLOG.md` §"Wave w14 → Queued for the
next wave", so the next run's intake picks them up as carry-over.

- **W15 — API JWT (ADR-010)**: NW-05, NW-06, NW-07, NW-08, NW-31, NW-32, plus
  the never-delivered "with OIDC claims" half of the superseded
  `E01/F05/US01/T02`. ADR-025 §H **T14 is authored in w14 and `Skip`ped** by
  `E15/F01/US01/T01`, to be activated by NW-05/NW-08.
- **W16 — no session as source of truth**: NW-10, NW-11, NW-12, NW-13, NW-21.
- **W17 — domain completeness**: NW-20, NW-22, NW-23, NW-25, NW-26, NW-62,
  NW-63, NW-64, NW-65, NW-66.
- **W18 — contract / ops / Ask / Quote residuals**: NW-27, NW-30, NW-40, NW-41,
  NW-50, NW-55, NW-56, NW-57, NW-59, NW-60, NW-61.
- **Out**: NW-51 (closed on `main`), NW-52 / NW-53 / NW-54 (deferred).

Nothing the council decided for an in-wave item was dropped for the cap. NW-24
was named as "the first item to cut" and **was not cut** — 11 tasks fit inside
20. The pick row's **server role tag** is carried by `E14/F03/US02/T01` even
though it sits on NW-24's screen, exactly as the ux-ui-designer required.

---

## 5. Superseded items

| Work item | Ruling | Applied |
|---|---|---|
| `epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-02-membership-invite.md` | **superseded** by `E15/F01/US01/T01` (NW-58) | `status: superseded` + `## Superseded (2026-09-11, wave w14)`. **Body byte-identical** |
| `epic-06-web-foundation/feature-04-workspace-members-ui/us-01-workspace-members-invite/tasks/task-01-workspace-members-invite.md` | **not superseded** — no banner | untouched |

The intake's §6 recorded "none"; the council overrode one row of it
(`reports/architecture/waves/w14.md`, §"Work items this wave supersedes") and
this decomposition applied that ruling. **This is the one place where the wave's
work-item output diverges from the requirements file, and it is a council
instruction, not a decomposer decision.**

⚠ **Operator decision needed**: `reports/plan/slices/e01.yaml:52` and
`reports/plan/wave-spec.execution.yaml:87` still list `E01/F05/US01/T02` as
`status: live`. This process never edits historical wave files. The task shipped
long ago, so nothing will re-run it; if you want those files to agree with the
banner, that is a manual edit outside this run.

---

## 6. ADR actions taken at the table (nothing for the decomposer to do)

- **New**: `ADR-025` workspace membership authorization + invitation lifecycle;
  `ADR-026` workspace discovery, roster and invitations API contract.
- **Amended by w14 footer** (bodies untouched, every `Status: accepted`
  unchanged, every footer a narrowing): ADR-001, ADR-003, ADR-005, ADR-006,
  ADR-009, ADR-010, ADR-011, ADR-012, ADR-014, ADR-016, **ADR-018 (two
  footers)**, ADR-019, ADR-020, ADR-022.
- **`none — no change`**: ADR-002, ADR-004, ADR-013, ADR-015, ADR-017, ADR-021,
  ADR-023, ADR-024.
- `INDEX.md` complete at 26 rows, none removed.

**w14's cloud delta is zero** in both environments: no Azure resource, no
Terraform change, no new runtime config key, no new Key Vault secret, no new
federated credential. `E14/F06/US01/T01` asserts it
(`git diff --stat origin/main -- infra` empty; **exactly two** workflow files
differ).

---

## 7. Open questions and assumptions in force

| ID | Status | Assumption |
|---|---|---|
| **OQ-w14-001** | assumed | the interim `X-User-Id` (MSAL account username, lower-cased) keys `workspace_membership` in w14, behind **one** `ICallerIdentity` seam, so W15 (NW-05) swaps in the token `sub`/`oid` without touching callers |
| **OQ-w14-002** | **answered at the table** | **no mail transport in w14**. ACS Email + Azure Managed Domain is *decided and deferred* (ADR-005 w14 footer). w14 ships `IInvitationMailer` + `NullInvitationMailer`, `mailDelivered: false`, and the copyable accept link. The UI must never say "sent" |
| **OQ-w14-003** | **answered at the table** | the wave bases on `origin/main` **merged** with the Helix process branch. **See §8 — this already happened on this checkout** |
| **OQ-w14-004** | **answered at the table** | `industry` + `country` from closed lists; `currency` derived from country and stored, never typed; "region" is a business region, ADR-006's `northeurope` untouched; a workspace currency never overrides a contract's own currency |
| **OQ-w14-005** | **answered at the table** | any live member reads the roster; only an Admin invites, revokes or removes; the last Admin cannot be removed; removal takes effect on the next request |
| **OQ-w14-cl-01** | **answered at the table** | the accept token: fragment → memory for one mount → `X-Invitation-Token` header. Never persisted; MSAL `state` explicitly rejected |
| **OQ-w14-dec-001** | **opened by this decomposition** | which email the seeded `demo` Admin membership binds to: an optional `psql` variable `demo_admin_email` with a documented inert placeholder, supplied by the `DEMO_ADMIN_EMAIL` GitHub Environment variable when the operator sets it. Appended to `reports/open-questions.md` |

---

## 8. Operator prerequisites before `reports/plan/gates/w14.hitl-ok`

1. **W14-01's merge appears already done on this checkout.** `git log` shows
   `7aa7f9a` "Merge origin/main (Raffa rebrand) into helix/next-wave-process"
   and `fd9558e` "align the next-wave process and the w14 council outputs with
   the Raffa rebrand"; `git rev-list --left-right --count origin/main...HEAD`
   is `0 6`; `backend/src` holds `Raffa.*` and the design export is at
   `inputs/design/prototypes/raffa-v2/`. **Points (a) and (b) of W14-A1 are
   satisfied.** Points **(c), (d) and (e) are still yours** and (d)/(e) are the
   ones that matter:
   - (c) `rg -ni "raffa" .github/ infra/ scripts/ backend/scripts/` reviewed
     **line by line** against live Azure / HCP / **Entra**;
   - (d) **one throwaway `dev` deploy from this base is green**, reaching
     `backend.yml`'s "Verify schema applied (ADR-021)" step;
   - (e) **one interactive sign-in on deployed `dev` returns a token carrying
     the expected scope** (`api://raffa-<env>-api/Raffa.{Read,Write}`,
     `web.yml:204-205`). This is identity-plane: a renamed scope fails **in the
     browser, after CI is green**, and every w14 item is about the signed-in
     identity.
2. **A second Entra account** on the pilot tenant, or `web/e2e/invite.spec.ts`
   ships authored-and-skipped and **N3b cannot be proven**. The cross-domain
   invite rule is now a *warning*, not a block, so the account does **not** need
   to share the Admin's email domain.
3. **Set `DEMO_ADMIN_EMAIL`** (GitHub Environment variable, per environment)
   before the next `demo` seed — OQ-w14-dec-001.
4. **Run `backfill-workspace-membership.yml` against `dev`** for every
   pre-existing workspace **before anyone signs in on the post-w14 build**. The
   moment the picker reads the server list, every workspace without a membership
   row disappears for its creator, and the client is forbidden to soften that
   with a cache.
5. Confirm you accept **one naming divergence**: the council's
   §"What the final-integration task must run" names the acceptance doc
   `docs/workspace-identity-acceptance.md`; the process contract
   (`skills/decompose-next-workitems.md`) requires
   `docs/waves/<w>-acceptance.md`. The task writes
   **`docs/waves/w14-acceptance.md`** (same content, wave-scoped path;
   `docs/waves/` does not exist yet and the task creates it).

---

## 9. Launch

```
python scripts/check_slice_prereqs.py --slice w14
./run.ps1 -Max -Slice w14 -o execution-fanout        # or ./run-next.ps1 -Launch -Wave w14
```

`check_slice_prereqs.py` requires `reports/plan/gates/e13.hitl-ok` (the wave's
`previous`). Create `reports/plan/gates/w14.hitl-ok` only after §8 passes — that
gate file is **W14-01's entire enforcement**: it produces no task and no
`depends_on` edge points at it, so if the green-base proof fails, the wave must
not start.

Promotion is a **separate operator act after the wave**, never a `depends_on`:
acceptance on deployed `dev` first, then `demo`. **The wave does not tag
itself** — `E14/F06/US01/T01` asserts that no `demo-v*` tag points at its HEAD.
