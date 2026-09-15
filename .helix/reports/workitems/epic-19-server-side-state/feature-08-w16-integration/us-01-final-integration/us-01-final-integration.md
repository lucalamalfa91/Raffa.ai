---
id: us-01
type: user-story
parent: feature-08
wave: w16
status: active
---

# us-01-final-integration — w16 is green, swept, and walkable on `dev`

## Story

As the **operator taking w16 to HITL**, I want one task that proves both trees
build and test, that the wave changed only what it said it would, and that hands
me a per-item manual walk on `dev`, so that **"the PR is green" and "the wave
works" mean the same thing**.

## Acceptance criteria

- [ ] AC-1 `dotnet build backend/Raffa.slnx` and `dotnet test backend/Raffa.slnx`
  exit 0, including the Postgres / Testcontainers suites. A
  `127.0.0.1:5432 refused` is treated as a **fixture gap to fix**, never a flake
  to re-run.
- [ ] AC-2 `cd web && npm run build` (which runs `generate:api`, `tsc --noEmit`
  and `vite build`) and `cd web && npm test` exit 0.
- [ ] AC-3 `git diff --stat origin/main..HEAD -- infra` is **empty** — stated as
  a two-dot diff, never as "no infra commits in the range".
- [ ] AC-4 `git diff --name-only origin/main..HEAD -- .github/workflows` lists
  **exactly two** files: `reprocess-tenant-documents.yml` (deleted) and
  `verify-tenant-corpus.yml` (added). Any third file is a defect and this task
  fails on it. `.github/workflows/backend.yml` is **not** among them.
- [ ] AC-5 The **paired** retirement grep: `X-Role` and `X-Workspace-Role` return
  nothing across `backend/src`, `web/`, `web/openapi/` and `.github/` outside
  historical ADR / acceptance records — **and `X-Tenant-Id` is present and
  unchanged, asserted positively so the sweep cannot overrun**.
- [ ] AC-6 `grep -rn "unattributed" backend/src` returns nothing outside the doc
  comments the council allows.
- [ ] AC-7 `docs/waves/w16-acceptance.md` exists in the w14/w15 shape and covers
  every one of the nine in-wave items, ending with the known-gaps table.
- [ ] AC-8 The README sweep is done, including `web/README.md:1166`'s stale
  `documentStore.ts` paragraph.

## Definition of done

- [ ] every AC above is verified by a command in the task with exit code 0
- [ ] the change honours ADR-016 w16 clauses 34–35 and ADR-014 w16 clauses 1–2
- [ ] the task depends on **every leaf artifact of the wave**
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| every leaf task of w16 | it is the wave's integration gate; it may not run before the work it verifies |

## Architecture decisions in force

- **ADR-016** w16 clause 34 — the list above; **no `terraform fmt/validate`**.
- **ADR-016** w16 clause 35 — the known-gaps set (closes 1, inherits 4, adds 2).
- **ADR-014** w16 clause 1 — the two-file CI-YAML set; an unplanned
  `.github/workflows/**` diff is a defect. Clause 2 — **one** PR, **no `demo-v*`
  tag**.
- **ADR-012** §12 — Playwright is runbook evidence, not CI.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | w16-integration | L | phase-5 |

## Council decisions carried into this story

> **ADR-014 w16 clause 2**: "w15 clause 5 (PR 1 infrastructure + PR 2 wave) fires
> **only when the wave changes `infra/`**. w16's `infra/` delta is zero ⇒ clause
> 5 does **not** apply. Single `integration → main` PR … It cuts **no `demo-v*`
> tag**."

> **ADR-014 w16 clause 3**: the close record is
> `reports/execution/wave-close-w16.md`, **never** the generic `wave-close.md`,
> written after the `integration → main` PR merges and stating the promotion
> outcome even when there is none. *(An artefact of the Passata-1 process, not a
> product file — recorded here so the operator writes it, not this task.)*

## Open questions

- **OQ-w16-008** — `reports/plan/gates/w15.hitl-ok` does not exist and
  `scripts/check_slice_prereqs.py:332` requires it. **Ruled: the operator stamps
  it at the w16 HITL gate** with
  `python scripts/check_slice_prereqs.py --record-hitl w15`. **No task.** It is an
  operator prerequisite recorded in `reports/audit/w16-hitl.md`.
- **OQ-w16-dm-03** — the baseline's pending infra apply (PR #118). **Operator
  action, never a wave task**; it does not gate A16-3; it appears in this task's
  known-gaps table as a **fact**, not an assumption.
