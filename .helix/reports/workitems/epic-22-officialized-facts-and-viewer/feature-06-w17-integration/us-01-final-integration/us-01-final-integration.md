---
id: us-01
type: user-story
parent: feature-06
wave: w17
status: active
---

# us-01-final-integration — w17 is provably green and walkable on `dev`

## Story

As the **operator**, I want one task at the end of the wave that **runs every check
and writes the walk**, so that **the PR is not green until the wave is actually
deliverable, and I am not the one discovering on `dev` that a suite never ran**.

## Acceptance criteria

- [ ] AC-1 `dotnet build backend/Raffa.slnx --configuration Release` and
      `dotnet test backend/Raffa.slnx --configuration Release` both exit 0 — the
      unfiltered run, so the Postgres / Testcontainers suites gate here. ⚠ A
      `127.0.0.1:5432 refused` is a **fixture gap, never a re-run**.
- [ ] AC-2 `cd web && npm ci && npm run build && npm test` exits 0, and
      `git diff --stat web/src/api/generated/` is **empty** afterwards — no contract
      drift between `raffa-api.v1.json` and the generated client.
- [ ] AC-3 `cd web && npm run test:e2e` exits 0 against a locally served build:
      `day1.spec.ts`, `v2.spec.ts` and the wave's four `w17-*.spec.ts` files run;
      `invite.spec.ts` reports **skipped** with its passcode reason. No Playwright run
      is presented as a CI gate — **no workflow runs Playwright** (ADR-012 §12).
- [ ] AC-4 `terraform fmt -check -recursive` and `terraform validate` pass on **both**
      `infra/environments/dev` **and** `infra/environments/demo` — not only the
      changed root. `infra.yml` validates by path filter, so a dev-only wiring passes
      `dev` and breaks `demo`.
- [ ] AC-5 The `infra/` diff against `origin/main` is **exactly** the four Terraform
      files of ADR-016 w17 clause 37 (`modules/servicebus/{main,variables}.tf`,
      `environments/dev/main.tf`, `environments/demo/main.tf`) plus `infra/README.md`
      — and **nothing else**.
- [ ] AC-6 `git diff --name-only origin/main -- .github/workflows` lists **exactly one
      added file** (the NW-73 console workflow). `backend.yml`, `web.yml`, `infra.yml`,
      `demo-promote.yml` and the seed/backfill jobs are **unchanged**. ⚠ A
      `backend.yml` diff from a migration is a **defect**.
- [ ] AC-7 No shortcut shipped: `authorization-rule`, `SharedAccessKey` and
      `ServiceBus__ConnectionString` appear **nowhere** under `.github/` or
      `backend/src`.
- [ ] AC-8 `docs/waves/w17-acceptance.md` exists in the w14/w15/w16 shape: a `>`
      blockquote per item (id / ADR / task), **Click path** → **Pass when:** →
      `curl` with exact status codes → `psql` where only SQL proves it →
      **Automated:** naming test classes → a closing **known gaps** table.
- [ ] AC-9 The acceptance doc covers **every in-wave item** with its own check:
      A17-S1 (NW-72), A17-S2 (NW-73), A17-1…A17-5 (NW-71), N16 (NW-20/NW-22/NW-62),
      N17 (NW-26/NW-63), N18 (NW-64), N19 (NW-65), N20 (NW-66).
- [ ] AC-10 The known-gaps table states as **facts, not assumptions**: the two
      outstanding HCP applies (PR #118's and this wave's); the `demo` promotion
      outcome with `git tag -l "demo-v*"` **read**; that clearing the promotion
      backlog **does not** clear the invitation walk (the three `demo` flags stay
      `false`); the W18 remainder of NW-63; and that w17 shipped a page renderer **and**
      a bulk whole-tenant re-render trigger in the same wave.
- [ ] AC-11 The README sweep lands: `backend/README.md`, and the stale
      `reprocess-tenant-documents.yml` prose is gone from the tree (its
      `web/e2e/v2.spec.ts` and `docs/ask-v2-acceptance.md` occurrences are
      `E20/F02/US02/T01`'s to fix in phase 2 — this task **verifies**, it does not
      re-edit them).
- [ ] AC-12 ⚠ No line of the acceptance doc describes the console workflow's
      `environment:` key as a **reviewer gate on `dev`**, and no line cites
      `infra.yml`'s apply-job approval as the control over `demo`'s infra.

## Definition of done

- [ ] every AC above is verified by a command in the task with its exit code
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E20/F02/US02/T01` (phase 2) | the console + the one added workflow — AC-6, AC-7, A17-S2 |
| `E20/F01/US01/T01` (phase 4) | the verified-money KPI — A17-S1 |
| `E22/F04/US01/T01` (phase 4) | Details and Why — N19, N20 |
| `E22/F05/US01/T01` (phase 4) | the unrecovered-fields section — N18 |

Those four are the wave's **leaf** artifacts; everything else in w17 is reachable
from them transitively (the viewer, the auto-accept server rule and its web half, the
360 payload, the renewal band, the strategy wiring and the page renderer).

## Architecture decisions in force

- **ADR-016 w17 clauses 41, 42** — the list and the known-gaps table.
- **ADR-014 w17 clauses 1, 5, 6** — one added CI file; the wave record is the
  acceptance doc; `backend.yml` stays closed.
- **ADR-005 w17 §19, §24** — the 20-file measurement precedes the first bulk run.
- **ADR-016 w17 clauses 44, 45(a)** — the first act of the walk is the apply confirm;
  the `environment:` key is never described as a reviewer gate on `dev`.
- **ADR-022 w17 clause 6a** — `infra.yml`'s apply-job approval attests to nothing and
  may not be cited as a control.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | w17-integration | L | phase-5 |

## Council decisions carried into this story

- ⚠ **The engine's `reports/execution/wave-close.md` is a fan-out delivery report, not
  the wave record** (ADR-014 w17 clause 5). It is stale by design, it is **never cited
  as current**, and its staleness is **expected rather than a defect to banner**. The
  wave record is this acceptance doc plus `reports/audit/w17-hitl.md`.
- ⚠ **Acceptance ordering, not a build dependency** (ADR-005 w17 §24): NW-26's
  **20-file batch on `dev` with zero Worker restarts** runs **before** the first
  whole-tenant reprocess. A bulk run is not a substitute for the measurement —
  `MaxConcurrentCalls = 4` with `min_replicas = 0` scales replicas out and multiplies
  concurrent bitmaps.
- ⚠ **The walk's first act is the HCP confirm** (ADR-016 w17 clause 44): dispatching
  NW-73 before the `raffa-dev` apply has landed **destroys a document's corpus,
  commits, and leaves no trail**. That is gate content, and the acceptance doc states
  it as step 0 of A17-S2.

## Open questions

- **none blocking.** Two operator reads travel with the gate rather than this task:
  **OQ-w17-ca-05** (both HCP workspaces' auto-apply setting, assumed `off`) and
  **OQ-w17-dm-04** (`dev`'s and `demo`'s GitHub environment protection rules, assumed
  `dev` carries no required reviewers). ⚠ **No DoD line is written for either** — a
  task handed an unrunnable assertion weakens it into the YAML-shape check ADR-022
  clause 6a exists to forbid.
