---
id: us-01
type: user-story
parent: feature-06
wave: w14
status: active
---

# us-01-final-integration — a human with a browser and `curl` can prove w14 on deployed `dev`

## Story

As the **operator**, I want one document that walks every w14 acceptance check
against deployed `dev`, and a green build on both sides, so that "the workspace
is real" is something I verify rather than something a green CI suite implies.

## Acceptance criteria

- [ ] AC-1 `dotnet restore/build/test` on `backend/Raffa.slnx` and
      `npm ci && npm run build && npm test` in `web/` all exit 0 on the
      integration branch.
- [ ] AC-2 **Rename assertion**: the wave base carries the post-rebrand tree —
      `rg -n "Contigo\." backend/src web/src` returns nothing.
- [ ] AC-3 **No-CI-drift assertion**:
      `git diff --stat origin/main -- .github/workflows` shows **exactly two**
      files (`backfill-workspace-membership.yml`, `seed-demo-fixture.yml`), and
      `git diff --stat origin/main -- infra` is **empty**. Any other diff fails
      the task.
- [ ] AC-4 `docs/waves/w14-acceptance.md` exists and gives numbered,
      operator-runnable steps against **deployed `dev`** for **N1, N2, N3, N3b,
      N4, N5, N8, N9, W14-A1 and W14-A2**, each naming the URL, the header
      posture and the expected status code. It must be runnable by a human with
      a browser and `curl`, **not** by CI.
- [ ] AC-5 `web/e2e/invite.spec.ts` exists: the two-account N3b path (Admin
      invites → a second browser context opens the link → signs in → lands in
      **that** workspace, not a create form → Admin removes them → the removed
      account's next load loses access). Until the second Entra account exists it
      is **authored and `test.skip`ped with a named reason**
      (`v2.spec.ts:107-112`'s own pattern) — **never a silent gap**.
- [ ] AC-6 `web/e2e/day1.spec.ts`'s invite step gains `await page.reload()`
      between the click (`:145`) and the assertions (`:146-147`) — a
      reload-surviving roster is the whole of **N3**.
- [ ] AC-7 The README sweep lands in the same commit: `backend/README.md`'s
      "interim header posture" (`:198-211`) now states that the **membership
      row** is the role source of truth and that `X-Role` / `X-Workspace-Role`
      are never the product answer; `web/README.md` covers the server-backed
      count and the `/invite/accept` route. `infra/README.md` only if infra
      changed — **it does not**.
- [ ] AC-8 A PR `integration → main` is open with CI green on the required
      checks (`backend / build + test`, `web / build`; `infra / terraform fmt +
      validate` only if `infra/**` was touched, which it is not).
- [ ] AC-9 **No `demo-v*` tag is cut.** Promotion is a separate operator act
      after the HITL gate. A wave that tags itself has skipped the gate.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `membership-seed-and-backfill` (E14/F05/US01/T01) | leaf — the operator path the acceptance doc instructs the operator to run |
| `web-workspace-resolution` (E14/F03/US02/T01) | leaf — screens 1 and 11 |
| `web-members-and-invites` (E15/F02/US01/T01) | leaf — screen 10 |

Those three are the wave's **leaf artifacts**: every other artifact is consumed
by a task in a later phase.

## Architecture decisions in force

- **ADR-014 (w14 footer)** — the wave bases on `origin/main` **merged** with the
  Helix process branch; tasks merge to `integration`, then one PR
  `integration → main`. **No unplanned CI YAML.**
- **ADR-016 (w14 footer)** — promotion ordering is a **HITL gate, never a
  `depends_on`**, because an infra change's effect is not observable by the wave
  that wrote it. **Seeds and backfills are data-plane acts and are never
  promoted.** The wave does not tag itself:
  "then `docs/ask-v2-acceptance.md` A1–A14 on `dev`, **then** `demo-v*`"
  (`wave-close-e13.md:105`).
- **ADR-025 §H** — T1–T13 are w14 tasks; **T14 is written now and activated by
  NW-05/NW-08 in W15**.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Build, assert no CI/infra drift, the two-account e2e, and `docs/waves/w14-acceptance.md` | L | phase-5 |

## Council decisions carried into this story

- **Why the acceptance doc earns its cost**:
  `infra/modules/containerapps/main.tf:117-121` records that a missing
  `ConnectionStrings__Suppliers`, which crashed the live API on boot, was found
  by E13/F11/US01/T01 **while writing the V2 acceptance runbook**, and `:89-95`
  records two more env vars missing in production since the tasks that needed
  them landed. Writing the acceptance doc is how that class of defect gets
  caught.
- **No workflow runs Playwright** (`web.yml:71-81`; NW-50 is queued to W18), and
  **N9** (Delete → 204 for the creator, 403 for Procurement, from a second
  browser) cannot be proven any other way. That is why the acceptance doc is
  manual and why it is not optional.
- **W14-01's five-point proof (W14-A1)** is the operator's, at HITL, **before**
  fan-out — this task records it in the acceptance doc, it does not perform it.
  Points (d) and (e) are the ones that matter: one throwaway `dev` deploy from
  the rebased base reaching `backend.yml`'s "Verify schema applied (ADR-021)"
  step, and **one interactive sign-in on deployed `dev` returning a token
  carrying the expected scope** — the Entra scopes
  `api://raffa-<env>-api/Raffa.{Read,Write}` (`web.yml:204-205`) are
  identity-plane and fail **in the browser, after CI is green**.

## Open questions

- **OQ-w14-002** — the mail transport. Deferred; `mailDelivered` is `false` by
  construction in w14, so the acceptance doc's N3b step uses the **copyable
  link**, never an inbox.
- **Prerequisite, not an open question**: a **second Entra account** is needed
  before `invite.spec.ts` can run. It is recorded in
  `reports/audit/w14-hitl.md` as an operator prerequisite.
