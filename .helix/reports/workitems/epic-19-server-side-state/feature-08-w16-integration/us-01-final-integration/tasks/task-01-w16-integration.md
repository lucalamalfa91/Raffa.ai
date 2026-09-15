---
id: E19/F08/US01/T01
type: task
story: us-01-final-integration
wave: w16
status: live
target_repo: raffa-backend
---

# task-01-w16-integration — Build, test, assert the wave's three negatives, sweep, and write the acceptance walk

## Coding objective

Prove w16 is green and that it changed **only** what it said it would, then hand
the operator a per-item manual walk on `dev`.

Build and test **both** trees. Assert the wave's three negatives: zero `infra/`
delta, exactly two changed `.github/workflows` files, and the **paired**
retirement grep. Sweep the stale prose the wave's own changes leave behind. Write
`docs/waves/w16-acceptance.md` in the w14/w15 shape.

`.github/workflows/backend.yml`'s build+test job is unfiltered, so the Postgres /
Testcontainers suites gate here. **A `127.0.0.1:5432 refused` is a fixture gap to
fix, never a flake to re-run** — a fixture lacks a `ConnectionStrings` override
for a module the test path now touches.

## Parent story AC covered

- AC-1 … AC-8 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `docs/waves/w16-acceptance.md` | new — per item: a `>` blockquote naming id / ADR / task, **Click path** → **Pass when:** → `curl` with exact status codes → `psql` where only SQL proves it → **Automated:** naming the test classes; closing **known gaps** table |
| `docs/ask-v2-acceptance.md` | `:601`'s gap #6 ("`/api/audit` is the one mapped route with no entry in `web/openapi/raffa-api.v1.json`") is now false and is retired |
| `docs/architecture/ask-raffa-v2-data-flow.md` | the reprocess-workflow citation points at the surviving verification job |

Passata 2 cwd is the per-task git worktree of the product clone. Domain
`README.md` files are standing implementer scope
(`skills/readme-hygiene.md`) and are deliberately **not** listed here; the sweep
below names them so the implementer knows which ones changed.

## Context the implementer needs

`Closes: the wave` — it verifies NW-07, NW-08, NW-31, NW-32, NW-11, NW-12,
NW-13, NW-21 and W16-01 together and records their manual checks.

Decision rows: `reports/architecture/waves/w16.md`, **NW-31**
(delivery-manager cell — the final-integration list, the CI-YAML set, the wave
order and the known gaps).

- **Architecture decisions in force**: **ADR-016** w16 clause 34 (the list) and
  clause 35 (the known-gaps set); **ADR-014** w16 clauses 1–2 (the two-file CI
  set; **one** `integration → main` PR; **no `demo-v*` tag is cut**);
  **ADR-012** §12 (Playwright is runbook evidence, not CI).
- **The README sweep** — these files carry prose the wave falsifies:
  `backend/README.md` (`:213`, `:219`, `:237`, `:384`, `:390`, `:1160`,
  `:2296-2304`, `:2689-2693`), `web/README.md` (**`:1166`** — the stale
  `documentStore.ts` paragraph NW-10 left behind, which the intake deliberately
  gave no task and named "any task that opens `web/README.md`" — **this is that
  task**; `:541-544` is already correct), root `README.md` and `infra/README.md`
  (their reprocess-workflow citations).
- **The three negatives, and how to phrase them.** The `infra/` assertion is a
  **two-dot diff**: `git diff --stat origin/main..HEAD -- infra` must be empty.
  **Never** phrase it as "no infra commits in the range" — the *log* over that
  span is **not** empty (the baseline carries PR #118's unapplied infra change,
  which this wave did not write), and a reviewer reading the log fails a clean
  slice.
- **The paired grep.** `X-Role` and `X-Workspace-Role` must return nothing across
  `backend/src`, `web/`, `web/openapi/` and `.github/` outside historical ADR and
  acceptance records — **and `X-Tenant-Id` must be present and unchanged,
  asserted positively so the sweep cannot overrun**. `X-Tenant-Id` does **not**
  retire: w15 demoted it to a membership-verified authorized selector, and every
  tenant-scoped route depends on it.
- **The `unattributed` grep** is read with the caveat the council allows: three
  doc-comment hits are legitimate, and NW-32's own file closes the one it creates.
- **Known gaps the acceptance doc must carry** (ADR-016 w16 clause 35) — it
  **closes** w15's reprocess row, **inherits four** (the `demo` flags
  `Invitations__Mail__Enabled` and `guest_provisioning_enabled`, both `false` and
  **deliberately so**; the Postgres-only suites; e2e-not-in-CI; the
  verified-domain guest refusal, `docs/waves/w15-acceptance.md:301`), and
  **adds two**: the **bulk whole-tenant reprocess deferred to W17** (so a `demo`
  walker does not read its absence as a regression) and the **baseline's pending
  infra apply**, stated as an applied-or-not **fact**, never an assumption.
- **Do not**: run `terraform fmt` or `terraform validate` (assert the negative
  instead); open any `infra/` file; open any workflow other than the NW-31 pair;
  flip `Invitations__Mail__Enabled` or `guest_provisioning_enabled` on `demo`;
  cut a `demo-v*` tag; touch `.github/workflows/backend.yml` — its stale
  `"eight"` / `"six"` strings stay parked, because **w16 does not legitimately
  open that file** and any diff in it fails this check.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/Raffa.slnx` exit 0 — including the Postgres / Testcontainers suites
- [ ] `cd web && npm run build` exit 0 (runs `generate:api`, `tsc --noEmit`, `vite build`)
- [ ] `cd web && npm test` exit 0
- [ ] `git diff --stat origin/main..HEAD -- infra` prints **nothing**
- [ ] `git diff --name-only origin/main..HEAD -- .github/workflows` lists **exactly** `.github/workflows/reprocess-tenant-documents.yml` (deleted) and `.github/workflows/verify-tenant-corpus.yml` (added) — a third entry fails this task
- [ ] `grep -rn "X-Role\|X-Workspace-Role" backend/src web web/openapi .github` returns **no match**
- [ ] `grep -rn "X-Tenant-Id" backend/src | wc -l` is **greater than zero** and `grep -c "X-Tenant-Id" web/src/api/client.ts` is unchanged from the wave base — the sweep did not overrun
- [ ] `grep -rn "unattributed" backend/src` returns **only** the doc-comment hits the council allows
- [ ] `grep -rn "IQueueConsumer" backend/` returns **no match**
- [ ] `grep -rn "raffa.renewals.actions\|raffa.contract360.steps\|raffa.quotes.negotiationOutcomes" web/src` returns **no match**
- [ ] `grep -n "documentStore.ts" web/README.md` returns **no match**
- [ ] `test -f docs/waves/w16-acceptance.md` exit 0, and it covers all nine in-wave items plus the known-gaps table
- [ ] `git diff --name-only origin/main..HEAD -- .github/workflows/backend.yml` prints **nothing**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| build + test | both trees compile and every suite passes, including the Postgres-backed ones | `dotnet test backend/Raffa.slnx`, `cd web && npm run build && npm test` |
| repo assertion | the wave's file-set is exactly what it declared — zero `infra/`, two workflow files, no `backend.yml` | the `git diff` commands above |
| repo assertion | the paired retirement grep, in both directions | the `grep` commands above |
| e2e (runbook, not CI) | the cases that need no live Foundry: the action / tick / outcome read-backs added by `E19/F07/US01/T01`, and the existing `web/e2e/v2.spec.ts:648` (A8 — the capability catalog answers "cosa sai fare?" with working links), which is the regression cover for NW-31's membership-derived Admin catalog | `web/e2e/v2.spec.ts` |
| manual | every item's check on `dev`, written for the operator | `docs/waves/w16-acceptance.md` |

## Open questions blocking this task

- **OQ-w16-008** — `reports/plan/gates/w15.hitl-ok` is missing and
  `scripts/check_slice_prereqs.py:332` requires it. **Operator action at the w16
  HITL gate** (`python scripts/check_slice_prereqs.py --record-hitl w15`), **not
  a task**. Not blocking.
- **OQ-w16-dm-03** — whether the baseline's infra apply (PR #118) has landed on
  `dev` is not readable from this checkout. It is an **operator** action on an
  already-authorised ADR-005 decision, it **cannot roll the running image back**
  (`ignore_changes` on both container apps), and it **does not gate A16-3**.
  Record it in the known-gaps table as a fact. Not blocking.

## Wave-spec entry

```yaml
- id: E19/F08/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-08-w16-integration/us-01-final-integration/tasks/task-01-w16-integration.md
  produces: [w16-integration]
  depends_on: [worker-r0-queue-deleted, outcome-savings-link, conversation-subject-keying, role-headers-retired, renewal-and-steps-on-screen, quote-outcome-on-screen]
  effort: L
  layer: backend
  status: live
```
