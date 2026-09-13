---
id: E14/F06/US01/T01
type: task
story: us-01-final-integration
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-w14-integration — build, prove no CI/infra drift, add the two-account invitation e2e, and write the w14 acceptance runbook

## Context

**Closes: W14-01 (recording its proof), NW-01, NW-02, NW-03, NW-04, NW-09,
NW-14, NW-24, NW-58** — the acceptance half of every item in the wave.
Decision rows: `reports/architecture/waves/w14.md`, §"What the final-integration
task must run" (delivery-manager) and row **W14-01**. ADRs in force:
**ADR-014 w14 footer**, **ADR-016 w14 footer**, **ADR-025 §H**, **ADR-024 /
ADR-020** (the acceptance-doc shape; `docs/ask-v2-acceptance.md` is the
precedent).

This is the last phase and it depends on every leaf artifact of the wave. **The
PR is not green until this task passes** — that is what makes w14 "immediately
working".

- **Architecture decisions in force**: ADR-014 (integration branch, no unplanned
  CI YAML), ADR-016 (promotion is a HITL gate, never a `depends_on`; the wave
  does not tag itself), ADR-025 §H (T1–T13 in this wave, T14 skipped until W15).
- **Do not touch application source** under `backend/src` or `web/src`. If
  something is broken, **HALT** and name the task that owns it. This task's only
  source-adjacent change is `web/e2e/**`.
- **Do not touch** `infra/**` or any `.github/workflows/*.yml` — the wave's only
  two workflow changes are `E14/F05/US01/T01`'s, and this task *asserts* that.
- **Do not cut a `demo-v*` tag.**

## Coding objective

**1. Build and test on the wave base.**
```
dotnet restore backend/Raffa.slnx
dotnet build   backend/Raffa.slnx
dotnet test    backend/Raffa.slnx
cd web && npm ci && npm run build && npm test
```
`web/package.json` has **no `lint` or `typecheck` script** — type checking is
folded into `build` (`generate:api && tsc --noEmit && vite build`). Do not
invent scripts; if a check is genuinely missing, say so in the acceptance doc
rather than adding one (that would be unplanned CI change).

**2. Rename assertion (W14-A1 point a).**
`rg -n "Contigo\." backend/src web/src` returns nothing.

**3. No-drift assertions.**
- `git diff --name-only origin/main -- .github/workflows` lists **exactly two**
  files: `backfill-workspace-membership.yml` and `seed-demo-fixture.yml`. **Any
  other workflow diff fails this task.**
- `git diff --stat origin/main -- infra` is **empty** — w14's Terraform and
  Azure delta is zero in both environments, confirmed reciprocally by
  cloud-architect and delivery-manager.
- `git diff origin/main -- backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
  is **empty** (ADR-025 §I — no w14 task edits it; editing it would ship a
  stale-authorization window in W15).

**4. `web/e2e/invite.spec.ts`** (new, next to `day1.spec.ts` and `v2.spec.ts`) —
the two-account **N3b** path: an Admin invites an address → a **second browser
context** opens the accept link → signs in → lands in **that** workspace, **not a
create form** → the Admin removes them → the removed account's next load loses
access (through the sign-in revalidation, no new endpoint). It needs a **second
Entra account**; until it exists, author the spec and `test.skip` it with the
**named** reason "requires a second Entra account on the pilot tenant"
(`v2.spec.ts:107-112`'s own pattern). **Never a silent gap.** Note in the spec
that the cross-domain invite restriction is a **warning, not a block** after
w14, so the second account does **not** have to share the Admin's email domain.

**5. `web/e2e/day1.spec.ts`** — add `await page.reload()` between the invite
click (`:145`) and the assertions (`:146-147`). Both assertions pass on
`sessionStorage` alone today, which is the defect; a reload-surviving roster is
the whole of **N3**. Change nothing else in that file — `E14/F03/US02/T01`
already rewrote its header comment and added N2 / N4 / N8 in phase 4.

**6. `docs/waves/w14-acceptance.md`** (new; `docs/waves/` does not exist yet —
create it). Same shape as `docs/ask-v2-acceptance.md`. Numbered,
operator-runnable steps against **deployed `dev`**, for **N1, N2, N3, N3b, N4,
N5, N8, N9, W14-A1 and W14-A2**, each naming the URL, the header posture and the
expected status code. Runnable by a human with a browser and `curl`, **not** by
CI — no workflow runs Playwright (`web.yml:71-81`; NW-50 is queued to W18) and
N9 cannot be proven any other way. Each entry states what to click and what to
expect:

| Check | What the operator does | Expected |
|---|---|---|
| **W14-A1** | record the five-point green-base proof the operator ran at HITL, including (d) one throwaway `dev` deploy reaching `backend.yml`'s "Verify schema applied (ADR-021)" step and (e) **one interactive sign-in on deployed `dev` returning a token carrying the expected scope** (`api://raffa-<env>-api/Raffa.{Read,Write}`, `web.yml:204-205` — identity-plane, so it fails in the browser after CI is green) | all five points pass before the gate file is created |
| **N1** | create a workspace on `dev`; `curl` `GET /api/workspaces` with the same `X-User-Id` | rows in `workspace`, `workspace_user`, `workspace_membership`(Admin); the list contains it with `role: "Admin"` |
| **N2** | sign in from a **second browser** with cleared storage | the same workspace is listed; **no second create** |
| **N3** | invite a Procurement address, reload `/workspace/members` | the roster still lists the invitee as `Invited` |
| **N3b** | copy the accept link from the invite result (**`mailDelivered` is `false` in w14 — there is no inbox step**), open it in a second browser, sign in, then have the Admin remove the member | the invitee lands in **that** workspace; after removal their next load loses access; the stale link is 404; a **new** invitation is needed to re-join |
| **N4** | close the tab and reopen with one membership | the shell mounts on `/ask`, no picker |
| **N5** | `curl` `GET /api/workspaces` with a crafted `X-Tenant-Id` for another tenant; `curl` `GET /api/workspaces/{otherTenant}/members` | the header changes nothing; the members call is **404** |
| **N8** | upload ≥1 document, sign out, sign in | the picker count is non-zero and the rail's secondary badge shows the same number; `/documents` lists the same files |
| **N9** | as the creator: Delete a document, Retry a failed upload; then as a Procurement member | 204 and 200; the Procurement member gets 403 and sees no Delete affordance |
| **W14-A2** | create a workspace with Industry and Country | the picker row reads "N validated contracts · {currency} · {country name}" and survives a reload and a second browser |

Also record, as operator prerequisites the wave depends on: run
`backfill-workspace-membership.yml` against `dev` for every pre-existing
workspace **before** anyone signs in on the new build, and set
`DEMO_ADMIN_EMAIL` before the next `demo` seed.

**7. README sweep** (standing implementer scope, in the same commit):
`backend/README.md`'s "interim header posture" (`:198-211`) now states that the
**membership row** is the role source of truth and that `X-Role` /
`X-Workspace-Role` are never the product answer; `web/README.md` covers the
server-backed count and the `/invite/accept` route. `infra/README.md` only if
infra changed — **it does not**.

**8. PR `integration → main`** with CI green on the required checks
(`backend / build + test`, `web / build`; `infra / terraform fmt + validate`
only if `infra/**` was touched, which it is not). **Do not cut a `demo-v*`
tag** — promotion is a separate operator act after the HITL gate.

## Parent story AC covered

- AC-1 … AC-9

## Files to create or modify

| Path | Change |
|------|--------|
| `docs/waves/w14-acceptance.md` | new — the operator runbook above (create `docs/waves/`) |
| `web/e2e/invite.spec.ts` | new — the two-account N3b path, `test.skip` with a named reason until the second Entra account exists |
| `web/e2e/day1.spec.ts` | add `await page.reload()` between `:145` and `:146-147` — and nothing else |

## Context the implementer needs

- Precedent: `docs/ask-v2-acceptance.md` (the only file in `docs/` today besides
  `docs/architecture/ask-raffa-v2-data-flow.md`) and epic-13's
  `E13/F11/US01/T01`.
- `.github/workflows/` currently holds nine files: `backend.yml`,
  `demo-config-check.yml`, `demo-promote.yml`, `infra.yml`, `mobile.yml`,
  `reprocess-tenant-documents.yml`, `seed-demo-fixture.yml`,
  `seed-market-intelligence.yml`, `web.yml`. After w14 there are ten, and
  exactly two differ from `origin/main`.
- `scripts/check_demo_swa_config.py` asserts on the brand substring
  `raffa-<env>-api`; it is a **standing** promotion check and this wave does not
  change it. Name it in the acceptance doc's promotion section.
- Docker Desktop does not start on the operator's machine, so the Postgres
  Testcontainers suites are CI-only. `dotnet test backend/Raffa.slnx` locally may
  skip them; say so rather than reporting a green that did not happen.
- Use `python`, not `python3`.
- **Do not touch**: `backend/src/**`, `web/src/**`, `infra/**`,
  `.github/workflows/**`, `reports/**`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0 and `dotnet test backend/Raffa.slnx` exits 0
- [ ] `cd web && npm ci && npm run build && npm test` exits 0
- [ ] `rg -n "Contigo\." backend/src web/src` returns nothing
- [ ] `git diff --name-only origin/main -- .github/workflows` lists exactly
      `backfill-workspace-membership.yml` and `seed-demo-fixture.yml`
- [ ] `git diff --stat origin/main -- infra` is empty
- [ ] `git diff origin/main -- backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs` is empty
- [ ] `npx playwright test web/e2e/day1.spec.ts web/e2e/invite.spec.ts` exits 0
      against `dev`, with every skipped case listing its reason
- [ ] `docs/waves/w14-acceptance.md` covers N1, N2, N3, N3b, N4, N5, N8, N9,
      W14-A1 and W14-A2, each with a URL, a header posture and an expected status
- [ ] `git tag --points-at HEAD | rg "^demo-v"` returns nothing

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| e2e | **N3b** — invite → accept in a second context → lands in that workspace → removal ends access → the stale link 404s | `web/e2e/invite.spec.ts` (skipped with a named reason until the second Entra account exists) |
| e2e | **N3** — the roster survives `page.reload()` | `web/e2e/day1.spec.ts` |
| build | both suites green on the integration branch | the DoD commands above |
| static | exactly two workflow files differ; `infra` and `WorkspacePrincipalAuthorization.cs` do not | the DoD commands above |
| manual | N1, N2, N4, N5, N8, N9, W14-A1, W14-A2 on deployed `dev` | `docs/waves/w14-acceptance.md` |

## Open questions blocking this task

- **A second Entra account** is a prerequisite of running `invite.spec.ts`. Not
  blocking: the spec is authored and skipped with a named reason, and it is
  recorded in `reports/audit/w14-hitl.md` as an operator prerequisite.
- **OQ-w14-002** — no mail transport in w14, so N3b's first clause is the
  **copyable accept link** and the UI does **not** claim a mail was sent.

## Wave-spec entry
```yaml
- id: E14/F06/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-06-w14-integration/us-01-final-integration/tasks/task-01-w14-integration.md
  produces: [w14-integration]
  depends_on: [membership-seed-and-backfill, web-workspace-resolution, web-members-and-invites]
  effort: L
  layer: backend
  status: live
```
