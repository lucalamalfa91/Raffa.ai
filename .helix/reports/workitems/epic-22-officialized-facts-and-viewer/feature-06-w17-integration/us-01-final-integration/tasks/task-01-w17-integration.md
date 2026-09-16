---
id: E22/F06/US01/T01
type: task
story: us-01-final-integration
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-w17-integration — build, test, validate both roots, and write the w17 acceptance walk

## Context

**Closes: the wave — NW-72, NW-73, NW-71, NW-20, NW-22, NW-62, NW-26, NW-63, NW-64,
NW-65, NW-66 (integration and acceptance only).**

Decision row: `reports/architecture/waves/w17.md` — **every** in-wave row; the
verification list is **ADR-016** w17 clause 41 and the known-gaps table is clause 42.

ADRs in force: **ADR-016** w17 clauses 41, 42, 44, 45(a); **ADR-014** w17 clauses 1,
4, 5, 6, 8; **ADR-005** w17 §19, §24; **ADR-022** w17 clause 6a; **ADR-021** w17
clause 2.

⚠ **This task writes no product code.** If a check fails, the fix belongs to the task
that owns the file — not here. Its one deliverable is
`docs/waves/w17-acceptance.md`, plus standing README hygiene.

## Coding objective

In the wave's integration branch, prove w17 is deliverable and write the operator's
walk.

1. **Both trees build and test.**
   `dotnet build backend/Raffa.slnx --configuration Release` and
   `dotnet test backend/Raffa.slnx --configuration Release`, **unfiltered** —
   `backend.yml`'s `dotnet test Raffa.slnx --configuration Release --no-build` step
   (**match on that literal command, not on a line number** — it sat at `:72-74`
   when the council wrote this and is at `:83-85` today, shifted by `37dba62`'s
   pgvector pre-pull) does not filter, so the Postgres / Testcontainers suites gate
   here, and so does **`AuthenticationSeamAbsenceTests`** over the wave's new
   workflow (it scans `.github/workflows/**`, roots at
   `backend/tests/Raffa.ArchitectureTests/AuthenticationSeamAbsenceTests.cs:62-68`).
   ⚠ A `127.0.0.1:5432 refused` is a **fixture gap, never a re-run**: a fixture is
   missing a `ConnectionStrings` override for a module the test path now touches. A
   red `main` is no `dev` deploy (ADR-014 w15 clause 3).
2. **The web tree builds, type-checks and tests.**
   `cd web && npm ci && npm run build && npm test`. ⚠ `npm run build` **is** the
   type-check (`generate:api && tsc --noEmit && vite build`); `web/package.json:10-18`
   has **no** `lint` and **no** `typecheck` script — **do not invent one**. After the
   build, `git diff --stat web/src/api/generated/` must be **empty**: a non-empty diff
   means `web/openapi/raffa-api.v1.json` and the checked-in client have drifted.
3. **The browser paths that need no live Foundry.**
   `cd web && npm run test:e2e` against a locally served build: `day1.spec.ts`,
   `v2.spec.ts` and the wave's four new per-theme specs — **each created by the
   task that owns its theme**, per ADR-012 w17 clauses 39 and 41, so **this task
   creates none of them**:

   | Spec | Created by | Phase | Carries |
   |---|---|---|---|
   | `w17-savings.spec.ts` | `E20/F01/US01/T01` | 4 | A17-S1 |
   | `w17-answers.spec.ts` | `E21/F03/US02/T01` | 3 | N16 |
   | `w17-viewer.spec.ts` | `E22/F03/US01/T01` (extended p4 by `E22/F04/US01/T01`) | 3 | N17, N20, OQ-w17-cl-03's check |
   | `w17-review.spec.ts` | `E22/F05/US01/T01` | 4 | N18 |

   `invite.spec.ts` reports **skipped** with its passcode reason. ⚠ **No workflow runs
   Playwright**, so these are acceptance-runbook evidence and **must not be presented
   as a CI gate** (ADR-012 §12, clause 31). ⚠ If a spec is **missing** rather than
   failing, that is the owning task's defect and the fix belongs there — this task
   writes no product code (see the banner above) and **must not** author the spec to
   make its own check pass.
4. **Both Terraform roots, not only the changed one.**
   `terraform fmt -check -recursive` over `infra/`, then `terraform init -backend=false`
   + `terraform validate` in **`infra/environments/dev`** *and*
   **`infra/environments/demo`**. `infra.yml:65-75` validates only the changed root's
   path filter, so a dev-only wiring passes `dev` and breaks `demo` — the exact
   failure ADR-016 w17 clause 37 makes the variable **required** to prevent.
5. **The `infra/` delta is exactly clause 37's set.**
   `git diff --name-only origin/main -- infra/` lists exactly
   `infra/modules/servicebus/main.tf`, `infra/modules/servicebus/variables.tf`,
   `infra/environments/dev/main.tf`, `infra/environments/demo/main.tf` and
   `infra/README.md`. Anything else is scope creep into `infra/`. ⚠ Those files land
   in **PR 1**, merged and applied before this wave's PR (ADR-014 w17 clause 2).
6. **The CI-YAML delta is one added file plus one comment-only correction.**
   `git diff --name-only origin/main -- .github/workflows` lists **exactly two
   entries**: the **added** NW-73 console workflow
   (`reprocess-tenant-documents.yml`), and
   `backfill-workspace-membership.yml` **modified in comments only** — the
   cross-reference correction at `:168`/`:221` that ADR-021 w17 clause 3 assigns
   to NW-73 because `.github/workflows/**` is NW-73's alone. ⚠ **Both are
   `E20/F02/US02/T01`'s and no other task may touch either.** Confirm the second
   is comment-only: `git diff -U0 origin/main --
   .github/workflows/backfill-workspace-membership.yml` shows changed lines that
   are **all** comments — any executable line in that diff is a defect.
   `backend.yml`, `web.yml`, `infra.yml`, `demo-promote.yml`,
   `seed-demo-fixture.yml`, `seed-market-intelligence.yml` and
   `verify-tenant-corpus.yml` are **unchanged**.
   ⚠ **Reconciling ADR-014 w17 clause 1 with ADR-021 w17 clause 3**: clause 1's
   "exactly one added file … the seed/backfill jobs are untouched" forbids an
   **unplanned** diff. This one is planned, single-owned and comment-only, and
   clause 1's own sibling (ADR-021 clause 3) requires it — leaving
   `backfill-workspace-membership.yml` citing a workflow as deleted while this
   wave re-adds it would ship a CI comment that is false on merge. ⚠ A `backend.yml` diff from NW-71's migration is a **defect**:
   `Raffa.Documents.Contracts` is already in both `SCRIPTS=(…)` arrays, so no array
   moves (ADR-021 w17 clause 2). **Match on the literal script path, never on a line
   number** — the arrays have drifted since the ADRs were written.
7. **No refused shortcut shipped.**
   `grep -rn "authorization-rule\|SharedAccessKey\|ServiceBus__ConnectionString"
   .github/ backend/src/` returns **nothing** (ADR-016 w16 clause 30, restated w17).
8. **README sweep** — standing implementer scope, so not listed in the files table:
   `backend/README.md` for the new console project and the per-field decision;
   `infra/README.md:331-336` and `:417-421` are falsified by the new grant and are
   updated **in PR 1, not here** — this task **verifies** they no longer claim the CI
   principal holds only Key Vault Secrets User, nor that "the two new V2 operator
   workflows need no new Azure grant". Verify too that the stale
   `reprocess-tenant-documents.yml` prose is gone from `web/e2e/v2.spec.ts:117`,
   `:431`, `docs/ask-v2-acceptance.md` and
   `.github/workflows/backfill-workspace-membership.yml` — those edits are
   `E20/F02/US02/T01`'s in phase 2; **do not re-edit them here**.
9. **Write `docs/waves/w17-acceptance.md`** in the w14/w15/w16 shape: one `>`
   blockquote per item carrying its id, its ADR and its task, then **Click path** →
   **Pass when:** → `curl` with exact status codes → `psql` where only SQL proves it →
   **Automated:** naming the test classes → a closing **known gaps** table. Cover:

   | Check | Item | The walk, in one line |
   |---|---|---|
   | **A17-1…A17-4** | NW-71 | upload a document with fields on both sides of the bar → every field ≥ 0.90 is **already accepted** on first open of Review with no click, **including a critical one**; every field below is queued; reload **and a second browser** agree; one audit row per document names the auto-accepted **fields, never their values**; no screen shows a percentage that contradicts its own decision state |
   | **A17-5** | NW-71 | a field the rule accepted and a field the user accepted are **told apart on screen without hovering**; nothing claims the user decided a field the rule decided |
   | **A17-S1** | NW-72 | record an outcome that realizes an opportunity → the Savings band shows money for realized, **one line per currency**, labelled **"Savings verified"**; reload and a second browser agree; an unlinked outcome (`savingsPropagated: null`) moves no figure; the pre-negotiation estimate appears nowhere in that cell |
   | **A17-S2** | NW-73 | ⚠ **step 0: confirm the `raffa-dev` HCP apply has landed in the HCP UI.** Then: run `verify-tenant-corpus.yml` (worklist N) → dispatch the console against that `dev` tenant → run verify again → worklist **0** and `%PDF` gone; every document reaches a terminal state; A16-3's single-document Admin path stays green |
   | **N16** | NW-20, NW-22, NW-62 | **Where you can save** and **When you must move** are concrete with citations; `benchmark` is non-empty **or** carries the explicit insufficient-data entry; every `activity` entry traces to a persisted event; no market claim without its source; neither answer reads "Not yet available" while the facts it needs are in the file |
   | **N17** | NW-26, NW-63 | opening the preview of a validated Northwind PDF shows **that file's page**, not `PREVIEW NOT RENDERED`; `?page=2` shows page 2; a deep link beyond `pageCount` renders **not found** with the URL **still reading `?page=N`** — never clamped, never redirected |
   | **N18** | NW-64 | a document where OCR missed end date and cancellation deadline shows both as **empty fillable rows**; filling one persists across a reload and a second browser |
   | **N19** | NW-65 | Details carries no "facts still to decide" list and no sentence claiming a fact was "signed off by you"; at zero undecided facts the trailing line does not render |
   | **N20** | NW-66 | a Why row shows type, normalized value, page · section and exactly one leverage tag in those words; **no percentage and no raw enum anywhere on Contract 360**; the quote is one click away; the viewer link **lands on the cited page** |

10. ⚠ **The order constraint that is not a build dependency** (ADR-005 w17 §19, §24):
    **NW-26's 20-file batch on `dev` with zero Worker restarts runs BEFORE the first
    whole-tenant reprocess**, never after. w17 ships a page renderer **and** a bulk
    re-render trigger in the same wave, so the console is the largest concurrent
    render this product will have run. State it in the acceptance doc as the ordering
    of the walk itself.
11. **The known-gaps table states facts, not assumptions**: the **two** outstanding
    HCP applies (ADR-016 w16 clause 33's PR #118, and this wave's clause 37) as
    landed-or-not, each **read in the HCP UI**; the `demo` promotion outcome with
    `git tag -l "demo-v*"` **read and written down**; ⚠ that clearing the promotion
    backlog **does not** clear the invitation walk — the three `demo` flags
    (`invitation_mail_enabled`, `guest_provisioning_enabled`,
    `guest_role_assignment_managed`) stay `false` and w17 flips none; ⚠ that from PR
    1's merge **`demo` holds a Send grant nothing can target** (the workflow offers
    `dev` only), kept because the variable is required and `raffa-demo`'s plan must
    stay green; the **W18 remainder of NW-63** (bounding boxes, `prebuilt-layout` and
    the widened gateway contract, the phrase-edit write path); **termination and price
    uplift have no correctable field** (NW-64's bounded gap); and the inherited w15/w16
    rows that have not closed.
12. ⚠ **Two things the doc may not say** (they are the defects this wave filed):
    **no line may describe the console workflow's `environment:` key as a reviewer
    gate on `dev`** — with `options: [dev]` the expression can only resolve to `dev`
    and `dev` carries no required reviewers (ADR-016 w17 clause 45(a)); and **no line
    may cite `infra.yml`'s apply-job approval as the control over `demo`'s infra** —
    that job's only step writes a step summary, so the recorded approval attests to
    nothing (ADR-022 w17 clause 6a).

## Parent story AC covered

- AC-1 … AC-12 — all of them; this is the story's only task.

## Files to create or modify

| Path | Change |
|------|--------|
| `docs/waves/w17-acceptance.md` | **new** — the per-item walk on `dev` in the w14/w15/w16 shape, with the ordering of §24 and the known-gaps table of ADR-016 w17 clause 42 |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

README hygiene (`backend/README.md`, `infra/README.md`) is standing implementer scope
and is deliberately **not** listed above.

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-016 w17 clause 41** — the verification list above, inverted for a wave that
    **does** touch `infra/`: both roots validated, the `infra/` diff bounded, the CI
    drift asserted, the SAS grep, the acceptance doc, the README sweep.
  - **ADR-016 w17 clause 42** — the known-gaps table's required rows.
  - **ADR-016 w17 clause 44** — the walk's first act is the `raffa-dev` apply confirm;
    a premature dispatch is **destructive, not merely failed**.
  - **ADR-014 w17 clause 4** — W17-A1, the gate's own checklist (a)–(h). ⚠ **It is the
    operator's, not this task's**: the base SHA read at the gate, the `integration`
    re-create, `--record-hitl w16`, `git tag -l "demo-v*"`, the three HCP runs across
    two workspaces, and the running image tag checked against `main`.
  - **ADR-014 w17 clause 5** — `reports/execution/wave-close.md` is the engine's
    fan-out report, **not** the wave record, and is never cited as current.
  - **ADR-021 w17 clause 2** — no `backend.yml` edit is owed and one would be a
    defect.
- ⚠ **Do not "helpfully" fix** the two stale counts inside `backend.yml` (the
  "all eight module scripts" message and the "same six checked-in files" comment —
  both describe **nine**-entry arrays). `.github/workflows/**` is NW-73's alone this
  wave, and the sweep is recorded against that task; correcting them here produces
  exactly the `backend.yml` diff clause 2 calls a defect.
- **Do not touch**: any file another w17 task owns — in particular
  `web/e2e/v2.spec.ts`, `docs/ask-v2-acceptance.md` and
  `.github/workflows/backfill-workspace-membership.yml` (`E20/F02/US02/T01`'s, phase
  2), and everything under `infra/` (PR 1's).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/Raffa.slnx --configuration Release` exits 0 (unfiltered — Postgres/Testcontainers suites included)
- [ ] `cd web && npm ci && npm run build && npm test` exits 0
- [ ] `git diff --stat web/src/api/generated/` is **empty** after the build
- [ ] `cd web && npm run test:e2e` exits 0 — `day1`, `v2`, `w17-savings`, `w17-answers`, `w17-review`, `w17-viewer` run; `invite` is **skipped** with its reason
- [ ] `terraform fmt -check -recursive infra/` exits 0
- [ ] `terraform init -backend=false && terraform validate` exits 0 in **`infra/environments/dev`** *and* in **`infra/environments/demo`**
- [ ] `git diff --name-only origin/main -- infra/` lists **exactly** the four Terraform files of ADR-016 w17 clause 37 plus `infra/README.md`
- [ ] `git diff --name-only origin/main -- .github/workflows` lists **exactly two entries**: the added `reprocess-tenant-documents.yml` and the comment-only `backfill-workspace-membership.yml` — nothing else
- [ ] `git diff -U0 origin/main -- .github/workflows/backfill-workspace-membership.yml` shows **only comment lines** changed (no executable YAML)
- [ ] `git diff --stat origin/main -- .github/workflows/backend.yml` is **empty**
- [ ] `grep -rn "authorization-rule\|SharedAccessKey\|ServiceBus__ConnectionString" .github/ backend/src/` returns **nothing**
- [ ] `grep -rn "reprocess-tenant-documents" web/e2e/ docs/ .github/workflows/backfill-workspace-membership.yml` returns only **current** statements (the workflow exists again; no line claims it was deleted)
- [ ] `test -f docs/waves/w17-acceptance.md` and it carries a blockquote for **A17-1…A17-5, A17-S1, A17-S2, N16, N17, N18, N19, N20** and a **known gaps** table
- [ ] `grep -n "reviewer gate\|required reviewers" docs/waves/w17-acceptance.md` returns **nothing** about `dev`
- [ ] `grep -n "20-file\|20 file" docs/waves/w17-acceptance.md` shows the render measurement ordered **before** the first whole-tenant reprocess
- [ ] `grep -n "demo-v" docs/waves/w17-acceptance.md` shows the tag **read**, not assumed

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| build | both trees compile and the contract has not drifted | `dotnet build backend/Raffa.slnx`, `cd web && npm run build` |
| suite | every backend suite passes unfiltered, including the architecture and auth-seam scanners over the wave's new workflow | `dotnet test backend/Raffa.slnx` |
| suite | every vitest suite passes, including the rewritten locks in `semantics.test.ts`, `reviewViewModel.test.ts` and `contract360ViewModel.test.ts` | `cd web && npm test` |
| e2e | the browser paths that need no live Foundry still pass | `cd web && npm run test:e2e` |
| infra | both roots are formatted and valid, and the `infra/` delta is bounded | `terraform fmt -check -recursive`, `terraform validate` in both roots |
| manual (`dev`) | the full per-item walk | `docs/waves/w17-acceptance.md` (**written by this task**) |

## Open questions blocking this task

- **none blocking.**
- ⚠ **Two operator reads travel with the gate, not with this task** — **OQ-w17-ca-05**
  (both HCP workspaces' auto-apply setting; assumed **off**) and **OQ-w17-dm-04**
  (`dev`'s and `demo`'s GitHub environment protection rules; assumed **`dev` carries
  no required reviewers**). **Write no DoD line for either**: a task handed an
  unrunnable assertion weakens it into the YAML-shape check ADR-022 clause 6a exists
  to forbid, manufacturing the defect the clause was written against.

## Wave-spec entry

```yaml
- id: E22/F06/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-06-w17-integration/us-01-final-integration/tasks/task-01-w17-integration.md
  produces: [w17-integration]
  depends_on: [servicebus-ci-send-grant, bulk-reprocess-console, auto-accept-server, savings-verified-kpi, contract360-officialized-facts, review-unrecovered-fields]
  effort: L
  layer: backend
  status: live
```
