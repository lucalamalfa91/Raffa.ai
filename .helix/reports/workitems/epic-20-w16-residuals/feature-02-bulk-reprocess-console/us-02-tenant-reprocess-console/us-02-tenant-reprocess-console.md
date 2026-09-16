---
id: us-02
type: user-story
parent: feature-02
wave: w17
status: active
---

# us-02-tenant-reprocess-console — re-run one tenant's documents through the product path

## Story

As the **operator**, I want to re-run extraction over every document of a
`dev` tenant with one dispatched workflow, so that a corpus damaged by an old
pipeline is repaired **by the product's own reprocess path** — job reset,
publish, chunk removal and audit row included — and not by hand-written SQL.

## Acceptance criteria

- [ ] AC-1 Dispatching the workflow with a `tenant_id` re-enqueues every
  document in `verify-tenant-corpus.yml`'s own worklist predicate, each through
  `DocumentReprocessService.ReprocessAsync`, and each reaches a terminal state.
  (A17-S2)
- [ ] AC-2 Running `verify-tenant-corpus.yml` afterwards reports the worklist
  **empty** and `%PDF` gone. (A17-S2)
- [ ] AC-3 The single-document Admin path `POST /api/documents/{id}/reprocess`
  is unchanged and A16-3 stays green.
- [ ] AC-4 Every reprocessed document has a `document.reprocessed` audit row
  whose `Actor` is exactly `system:bulk-reprocess`, plus **one run-scoped row
  written before the loop** whose `Detail` carries `requestedBy`, `run`,
  `tenant` and `count`. No CI-controlled string appears in any `Actor`.
- [ ] AC-5 A run against a tenant with a non-empty worklist that processes
  **fewer** documents than the worklist exits **non-zero** and prints
  processed/total. A run that finds **zero rows under a valid tenant** exits
  **non-zero**.
- [ ] AC-6 The console **stops at the first publish failure** — it does not log
  and continue.
- [ ] AC-7 `dotnet build backend/Raffa.slnx` builds the new console with **no
  edit to `.github/workflows/backend.yml`**, and `DependencyDirectionTests`
  passes with the console in the all-projects set and **absent** from the
  domain-module set and the allow-list.
- [ ] AC-8 The workflow is `workflow_dispatch` only, `tenant_id` is required,
  `target_environment` offers **`dev` only**, top-level `permissions` is
  `contents: read`, there is a concurrency group keyed by environment+tenant,
  and it is **not** referenced by `demo-promote.yml`.
- [ ] AC-9 No log line contains chunk text, an extracted value, document bytes
  or a vault value. Ids, counts, statuses, job ids and file names only.
- [ ] AC-10 No `reprocess-tenant-documents` reference survives as a claim that
  the workflow is absent: the stale prose in `web/e2e/v2.spec.ts`,
  `docs/ask-v2-acceptance.md` and
  `.github/workflows/backfill-workspace-membership.yml` is corrected.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E20/F02/US01` (phase 1) | the console cannot publish without the topic-scoped Sender grant. ⚠ Its first run against a **missing** grant is destructive, not merely failed (ADR-011 w17 clause 26), so the grant's **apply** — an operator act — precedes the first dispatch (ADR-016 w17 clause 44) |

## Architecture decisions in force

- **ADR-002** w17 clause 1 — a third composition root; joins `AllRaffaProjects`,
  never the domain-module array or the allow-list.
- **ADR-027** w17 clause 3 — calls `ReprocessAsync`; never re-implements
  `RequeueClassificationJobAsync`.
- **ADR-009** w17 clauses 1, 4 — three-argument `Configure` inside `BeginScope`;
  one tenant per run from an explicit GUID; zero rows exits non-zero; the
  application's own `postgres-connection` secret, never a superuser or
  `BYPASSRLS`.
- **ADR-011** w17 clauses 20, 23, 26 — the fixed literal actor; human
  attribution in `Detail`; **stop at the first publish failure**.
- **ADR-022** w17 clauses 1–3, 6 — the four-link authorization chain;
  `environment:` sits on the job holding `id-token: write`; `dev` only.
- **ADR-016** w17 clauses 36–42 and round-3 clauses 43–45; **ADR-014** w17
  clauses 1–9 — the runner, the CI-scanner gate, the composed worklist, never in
  `demo-promote.yml`, and the wave's order.
- **ADR-021** w17 clause 3 — the CI prose sweep is assigned here, because
  `.github/workflows/**` is this task's alone.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | tenant-reprocess-console | L | phase-2 |

## Council decisions carried into this story

- **The console is a third composition root** with no table, no endpoint and no
  business rule, referenced by nothing. It composes the reprocess service's
  seven dependencies through the owning modules' own `ServiceCollectionExtensions`.
- **The three refused shortcuts stand** (ADR-016 w16 clause 31): a Service Bus
  SAS key, any `X-*` identity header, and inserting `extraction_job` rows via
  psql.
- ⚠ **S17-3, the decisive finding**: the console is the **first code path that
  enumerates a tenant with no HTTP caller**.
  `DocumentsContractsDbContextOptions.Configure`'s third argument is
  **optional**, so the two-argument form leaves `app.tenant_id` unset, the
  policy's `nullif(...)` is NULL, **every query returns zero rows**, and the
  console **exits green having done nothing** — indistinguishable from an empty
  worklist. Hence: three-argument `Configure`, and **zero rows under a valid
  tenant exits non-zero**.
- ⚠ **ADR-011 w17 clause 26**: `EmbeddingRetrievalService.RemoveChunksAsync`
  opens its own tenant scope and commits **its own** `SaveChangesAsync`
  (`:182`) **before** `PublishAsync` (`DocumentReprocessService.cs:110-113`).
  The source comment *"a publish failure fails the request with nothing
  changed"* (`:107-109`) is **true of the DbContext and false of the
  embeddings**: on a failed publish the chunks are gone and committed, nothing
  is requeued (`:115` never runs) and **no audit row is written** (`:117-126`
  is after it). **This task may not inherit that comment as if it were true.**
  Three rules: stop at the first publish failure; exit non-zero with
  processed/total; **do not reverse the delete-before-publish order** — deleting
  after a successful re-index would leave superseded text citable by Ask.
- **The two operator jobs compose**: `verify-tenant-corpus.yml:147-169` already
  computes the worklist (`Failed`, or an `embedding` still starting with
  `%PDF`). **That is the console's input set and the console runs the same
  predicate, never a second one** — otherwise "what needs reprocessing" and
  "what got reprocessed" drift apart silently. This makes A17-S2 self-proving:
  verify → console → verify.
- ⚠ **This is the product's first *mutating* operator workflow** — its
  predecessor's own comment reads *"this job reports; it does not mutate"*
  (`verify-tenant-corpus.yml:8`) — so its controls are **at least** those of
  the read-only one it copies, never fewer.
- ⚠ **The `environment:` key ships but may not be called a reviewer gate.**
  With `options: [dev]` the expression can only resolve to `dev`, and the
  inherited comment (`verify-tenant-corpus.yml:15-16`) names *demo's* reviewers
  as its whole purpose — so the comment is **edited, not inherited**. The honest
  `dev` boundary is **repository write plus an explicit dispatch**.
- **The CI scanner is a real gate**: `AuthenticationSeamAbsenceTests.cs:62-68`
  scans `.github/workflows/**` (`:67`) and runs inside `backend.yml`'s
  `dotnet test`, the required check on `main`. A careless workflow line kills
  the **wave's** deploy, not just its own job. The workflow therefore copies
  `verify-tenant-corpus.yml`'s credential idiom **verbatim**, which is proven
  green against that scanner today.

## Open questions

- **OQ-w17-sa-02** (a console bypasses the Admin gate) — **ruled permitted** by
  security-architect: the gate is **relocated** to a named four-link chain, not
  bypassed. Where this and S17-3 conflict, **S17-3 wins**: the worklist query
  lives in the console, outside `ReprocessAsync`'s scope, and must bind the
  tenant explicitly. ADR-022 w17 clause 3.
- **OQ-w17-sec-01** (who asked) — **ruled**: one run-scoped audit row before
  the loop; `requestedBy` in `Detail`, **never** in `Actor`. ADR-011 w17 clause
  20b.
- **OQ-w17-sec-04** (a mid-run connection failure) — **ruled**: no new control,
  because re-runs are idempotent, but the exit code must be honest. ⚠ Scope
  named: "re-runs are safe by construction" holds against **partial
  completion**, **not** against a missing Send grant.
- **OQ-w17-dm-04** (is there an "approval pending" state on `dev`) — **open, an
  operator read.** **Assumption in force: `dev` carries no required
  reviewers**, therefore **no DoD line is written for the behavioural approval
  test this wave**; it travels as a W18 condition on the widening. A task handed
  an unrunnable assertion weakens it into the YAML-shape check it exists to
  forbid.
- **OQ-w17-ux-05** (a corpus-health signal) — **open, a W18 condition on
  widening beyond `dev`.** ⚠ One prohibition binds now: **no task may add a
  reassuring "re-indexing" / "catching up" banner** to Ask or Contract 360 —
  nothing requeues after a destroyed corpus, so it would be a *not ready yet*
  claim that can never resolve (ADR-018 w15 clause 6).
