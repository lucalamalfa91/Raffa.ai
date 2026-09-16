---
id: E20/F02/US02/T01
type: task
story: us-02-tenant-reprocess-console
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-tenant-reprocess-console — the console project and its one dispatched workflow

## Context

**Closes: NW-73 (the product half).**

Decision row: `reports/architecture/waves/w17.md` — the **NW-73** row
(software-architect, security-architect and delivery-manager halves), plus the
rulings on **OQ-w17-sa-02**, **OQ-w17-sec-01**, **OQ-w17-sec-04** and
**OQ-w17-008**.

ADRs in force: **ADR-002** w17 clause 1; **ADR-027** w17 clause 3; **ADR-009**
w17 clauses 1 and 4; **ADR-011** w17 clauses 20, 23 and **26**; **ADR-022** w17
clauses 1–3 and 6; **ADR-016** w17 clauses 36–42 and round-3 43–45; **ADR-014**
w17 clauses 1–9; **ADR-021** w17 clause 3.

⚠ **This task is the ONLY task in w17 allowed to open `.github/workflows/**`**
(ADR-014 w16 clause 4, restated w17). It is also PR 2's first task; PR 1 is the
Terraform grant, alone.

Already on `main`, so **not** work to redo: `ReprocessAsync` and its whole
pipeline; the `IExtractionQueuePublisher` port
(`backend/src/Raffa.Documents.Contracts/Application/Extraction/IExtractionQueuePublisher.cs:19`)
and its Service Bus adapter
(`backend/src/Raffa.Messaging/ServiceBusExtractionQueuePublisher.cs:24`, sender
`:38`, send `:57`, `MessageId` `:50`), authenticated with
`DefaultAzureCredential` and **no** connection string
(`backend/src/Raffa.Messaging/MessagingServiceCollectionExtensions.cs:79-88`);
the report-only `.github/workflows/verify-tenant-corpus.yml`.

## Coding objective

In `raffa-backend`, add an operator console and the one workflow that dispatches
it.

1. **New project `Raffa.Tools`** under `backend/src/`, registered in
   `backend/Raffa.slnx` (⚠ the solution file is **`.slnx`**, XML — a glob for
   `*.sln` finds nothing; today it lists 17 `src/` and 18 `tests/` projects and
   none named `Raffa.Tools`). It is a **third composition root**: no table, no
   endpoint, no business rule, referenced by nothing. Reference
   `Raffa.Messaging`, which already references `Raffa.SharedKernel` **and**
   `Raffa.Documents.Contracts`, so one reference picks up both halves.
2. **Compose, never hand-build.** Resolve
   `DocumentReprocessService` through the owning modules' own
   `ServiceCollectionExtensions`; it takes seven dependencies
   (`backend/src/Raffa.Documents.Contracts/Application/DocumentReprocessService.cs:37-44`:
   the DbContext, `IDocumentStorage`, `IExtractionQueuePublisher`,
   `EmbeddingRetrievalService`, `ITenantContext`, `IAuditWriter`, `IClock`).
3. **Bind the tenant explicitly — this is the defect that fails silently.**
   The console's **own worklist query** runs outside `ReprocessAsync`'s scope, so
   it must call the **three-argument**
   `backend/src/Raffa.Documents.Contracts/Infrastructure/DocumentsContractsDbContextOptions.cs`
   `Configure` (`:50`), passing `ITenantContext`, inside `BeginScope`. The third
   argument is **optional** (`:50`), so the two-argument form compiles, leaves
   `app.tenant_id` unset, makes the RLS policy's `nullif(...)` NULL, returns
   **zero rows**, and the console **exits green having done nothing**. Never a
   raw `NpgsqlConnection`, never a hand-written `SET app.tenant_id`, never
   `psql`. **One tenant per run, from an explicit GUID argument. No
   all-tenants mode.**
4. **The worklist is the verify job's own predicate.** Run the same predicate as
   `.github/workflows/verify-tenant-corpus.yml:151-160` (`Failed`, or an
   `embedding` still starting with `%PDF`) — **never a second one**, or "what
   needs reprocessing" and "what got reprocessed" drift apart silently.
5. **Loop.** Call `ReprocessAsync` (`:62`) once per document, with bounded
   concurrency. Re-runs are safe because `MessageId` collapses duplicate
   publishes (`ServiceBusExtractionQueuePublisher.cs:50`). ⚠ **Do not
   re-implement `RequeueClassificationJobAsync` (`:138`)**: it *reuses and
   resets* the latest Classification job (`Status = Queued`, `ClaimedAt = null`
   at `:168`, `ClaimedBy = null` at `:169`) and **deliberately preserves
   `AttemptCount`** (`:135-136`). The `ClaimedAt = null` reset is what lets the
   Worker's compare-and-swap claim re-deliver at all; a psql equivalent drops
   both silently.
6. ⚠ **Stop at the first publish failure.**
   `backend/src/Raffa.Documents.Contracts/Application/EmbeddingRetrievalService.cs`'s
   `RemoveChunksAsync` (`:163`) opens its own tenant scope and commits **its
   own** `SaveChangesAsync` (`:182`) **before** `PublishAsync`
   (`DocumentReprocessService.cs:110-113`) — so the comment at `:107-109`
   ("a publish failure fails the request with nothing changed") is **true of
   the DbContext and false of the embeddings**. On a failed publish the chunks
   are **gone and committed**, `:115` never runs so nothing is requeued, and
   `:117-126` never writes a trail. **Never "log and continue"**: past the first
   failure every iteration destroys and repairs nothing, and a whole-tenant loop
   makes that the tenant's entire corpus. **Do not reverse the
   delete-before-publish order** — deleting after a successful re-index would
   leave superseded text citable by Ask. The failure is made **rarer and
   louder, never reordered**.
7. **Exit codes are the honest report.** Exit **non-zero** if the console did
   not process every document in its worklist, printing processed/total; exit
   **non-zero** if the worklist is **empty under a valid tenant** (that is the
   S17-3 symptom, not a success). A partial run must never read as a completed
   one.
8. **Audit.** Write **one run-scoped row before the loop**: actor the fixed
   literal **`system:bulk-reprocess`**, `Detail` =
   `requestedBy=<triggering actor>; run=<run-id>; tenant=<id>; count=<n>`, with
   an action constant following the existing `document.*` vocabulary. ⚠ **No
   CI-controlled string is ever interpolated into `AuditEvent.Actor`** — not
   `github.actor`, not a run id, not a workflow input: `Actor` asserts an
   identity, `Detail` asserts none, and the append-only trigger makes a wrong
   `Actor` permanent and uncorrectable. Per-document rows keep coming from
   `ReprocessAsync` itself (`:117-126`, constant `:51`).
9. **Credentials.** Use the application's own `postgres-connection` secret, read
   the way `verify-tenant-corpus.yml:102-134` reads it; **never a superuser,
   never `BYPASSRLS`** (`FORCE ROW LEVEL SECURITY` binds the table owner but
   not `BYPASSRLS` — the only escape left). Publish with
   `DefaultAzureCredential`; **do not set `AZURE_CLIENT_ID` on the runner** (it
   selects the user-assigned managed identity, absent from that host).
10. **The workflow.** New `.github/workflows/reprocess-tenant-documents.yml`,
    copying `verify-tenant-corpus.yml`'s gating and credential idiom **at least
    in full**: `workflow_dispatch` **only** (never `push`, never `schedule`);
    **required** `tenant_id` input; top-level `permissions: contents: read`; a
    concurrency group keyed by environment+tenant (`:40-42`'s idiom);
    `environment: ${{ inputs.target_environment }}` on the job that holds
    `id-token: write` and logs into Azure (`:52-55` placement); the credential
    chain verbatim — `./.github/actions/azure-login` (`:67-72`) →
    `az keyvault secret show` (`:112`) → `python3 scripts/pg_connection_string_env.py`
    (`:120`) → masked `PG*` in `$GITHUB_ENV` (`:119-134`). ⚠ **Narrow
    `target_environment` to `options: [dev]`** — the inherited list is
    `[dev, demo]` (`:26`) and a task will otherwise reproduce it verbatim.
    ⚠ **Edit the inherited comment**: `verify-tenant-corpus.yml:15-16` explains
    `environment:` as "so demo's required reviewers still gate…", which is
    **false** with a `dev`-only list — describe the `dev` boundary honestly as
    **repository write plus an explicit dispatch**. Log ids, counts, statuses,
    job ids and file names (`:143`'s precedent) — **never** chunk text,
    extracted values, document bytes or any vault value. **Never add it to
    `demo-promote.yml`**, whose header (`:43-50`) forbids steps that move data.
11. **Architecture test.** In
    `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs`, add the
    console to **`AllRaffaProjects` (`:36`)** and to nothing else. ⚠ This is a
    **DoD line, not an option**: that set is the **membership filter on the
    violation check** (`.Where(r => AllRaffaProjects.Contains(r) && !allowed.Contains(r))`
    at `:103`, and again in `ArchitectureRuleEnforcementTests.cs:33`, `:83`), so
    a project **absent** from it is **invisible to the theory** and a later
    domain module referencing this host would be **silently legal with the
    suite green**. Do **not** add it to `DomainModules` (`:18`) or
    `AllowedReferences` (`:60`) — it is a host, not a module with a dependency
    budget.
12. **Sweep the stale prose** that still names the deleted workflow as though it
    existed or as though nothing replaced it: `web/e2e/v2.spec.ts:117` and
    `:431` (**executable test text**), `docs/ask-v2-acceptance.md:602`, and the
    comment cross-references in
    `.github/workflows/backfill-workspace-membership.yml:168` and `:221`.
    `infra/README.md` and `backend/README.md` are standing implementer scope.

**No containerisation.** `backend.yml:83-85` already runs
`dotnet test Raffa.slnx`, so a project registered in the solution is built and
tested with **no `backend.yml` edit**, and `:142-155` builds exactly **two**
Dockerfiles — the console is not containerised, not pushed to ACR, not a
Container App. Zero deploy-path change, zero new image, zero new environment
key.

## Parent story AC covered

- AC-1 Dispatching the workflow with a `tenant_id` re-enqueues every document in the verify job's worklist predicate, each through `ReprocessAsync`, and each reaches a terminal state.
- AC-2 Running the verify workflow afterwards reports the worklist **empty** and `%PDF` gone.
- AC-3 The single-document Admin path is unchanged and A16-3 stays green.
- AC-4 Every reprocessed document has a `document.reprocessed` audit row with `Actor` exactly `system:bulk-reprocess`, plus one run-scoped row before the loop carrying `requestedBy`, `run`, `tenant`, `count`. No CI-controlled string in any `Actor`.
- AC-5 A short run exits **non-zero** with processed/total; zero rows under a valid tenant exits **non-zero**.
- AC-6 The console **stops at the first publish failure**.
- AC-7 `dotnet build backend/Raffa.slnx` builds the console with **no `backend.yml` edit**, and `DependencyDirectionTests` passes with the console in the all-projects set only.
- AC-8 `workflow_dispatch` only; `tenant_id` required; `target_environment` offers **`dev` only**; `permissions: contents: read`; a concurrency group keyed by environment+tenant; not referenced by `demo-promote.yml`.
- AC-9 No log line contains chunk text, an extracted value, document bytes or a vault value.
- AC-10 The stale `reprocess-tenant-documents` prose is corrected.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Tools/Raffa.Tools.csproj` | **new** — console project, references `Raffa.Messaging` only |
| `backend/src/Raffa.Tools/Program.cs` | **new** — entry point: parse `--tenant-id`, compose the modules' DI, three-argument `Configure` inside `BeginScope`, exit codes |
| `backend/src/Raffa.Tools/BulkReprocessRunner.cs` | **new** — the worklist query (the verify job's predicate), the bounded loop calling `ReprocessAsync`, stop-at-first-publish-failure, the run-scoped audit row, processed/total reporting |
| `backend/Raffa.slnx` | modify — add the `Raffa.Tools` project entry under `/src/` (XML `<Solution>`; 17 `src` entries today) |
| `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` | modify — add the console to the all-projects set **only** |
| `backend/tests/Raffa.Documents.Contracts.Tests/BulkReprocessTenantBindingTests.cs` | **new** — the tenant-binding proof and the stop-at-first-publish-failure proof |
| `backend/tests/Raffa.Audit.Tests/BulkReprocessAuditActorTests.cs` | **new** — the run-scoped row's `Actor`/`Detail` split |
| `.github/workflows/reprocess-tenant-documents.yml` | **new** — `workflow_dispatch` only, required `tenant_id`, `options: [dev]`, `permissions: contents: read`, concurrency by environment+tenant, `environment:` on the Azure-login job, the credential chain copied verbatim, the inherited comment corrected |
| `web/e2e/v2.spec.ts` | modify — correct the stale workflow claims at `:117` and `:431` |
| `docs/ask-v2-acceptance.md` | modify — correct the stale reference at `:602` |
| `.github/workflows/backfill-workspace-membership.yml` | modify — correct the comment cross-references at `:168` and `:221` |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-009 w17 clause 1** — five binding rules: three-argument `Configure` inside `BeginScope`; never raw SQL or `psql`; one tenant per run from an explicit GUID; **zero rows under a valid tenant exits non-zero**; the app's own secret, never `BYPASSRLS`.
  - **ADR-011 w17 clause 20b** — the run-scoped row and the `Actor`/`Detail` split.
  - **ADR-011 w17 clause 26** — the delete-before-publish window; stop at the first failure; do not reorder.
  - **ADR-022 w17 clause 3** — the Admin gate is **relocated**, and the security property is checkable: *the set of humans who can trigger a bulk reprocess must be no wider than the set who could do the same as tenant Admins through the API*. On this wave that holds because **`dev`'s tenants are the team's own test tenants**, not because a reviewer gate holds.
  - **ADR-022 w17 clause 6a** — `environment:` goes on the job with `id-token: write`, the sound idiom (`verify-tenant-corpus.yml:52-55`), **not** on an echo job the way `infra.yml:121` does it.
  - **ADR-022 w17 clause 6b** — `dev` only.
  - **ADR-016 w17 clause 38** — ⚠ **`AuthenticationSeamAbsenceTests.cs` scans `.github/workflows/**`** (roots at `:62-68`, `.github/workflows` at `:67`, scan `:74`, assert `:76-84`) and runs inside `backend.yml`'s `dotnet test`, the required check on `main`. It flags an auth-disabling config key, a fixed/minted passcode or token, and **a branch keyed on an environment name that skips a credential check**. It does **not** flag connection strings. A careless line here turns `main` red, and a red `main` is no `dev` deploy (ADR-014 w15 clause 3).
  - **ADR-016 w17 clause 39** — the two operator jobs compose; A17-S2 is self-proving (verify → console → verify).
  - **ADR-016 w17 clause 45(a)** — the `environment:` line stays but **may not be described as a reviewer gate on `dev`** in the workflow, the DoD, a runbook or the acceptance doc.
- **The three shortcuts that stay refused** (ADR-016 w16 clause 31): a Service
  Bus SAS key, any `X-*` identity header, inserting `extraction_job` rows via
  psql. ⚠ The SAS shortcut is **mechanically available today** —
  `backend.yml:262` records `raffa-sp-<env>` as Contributor on the resource
  group, so `az servicebus namespace authorization-rule keys list` is reachable.
  An explicit topic-scoped Send makes the forbidden path the anomalous one; it
  does not make it impossible.
- ⚠ **Do not write a "the approval gate blocks the run" assertion.** There is no
  reachable approval-pending state on `dev` (OQ-w17-dm-04, assumption in force:
  `dev` carries no required reviewers), and a task handed an unrunnable
  assertion weakens it into a YAML-shape check. That test travels to W18 with
  the widening.
- **Do not touch**: `backend/src/Raffa.Documents.Contracts/**` (the reprocess
  path is called, never edited); `.github/workflows/backend.yml`,
  `infra.yml`, `demo-promote.yml`, `web.yml`; anything under `infra/` (the
  Terraform grant is `E20/F02/US01/T01`'s, in PR 1); `verify-tenant-corpus.yml`
  itself — its predicate is **read and reused**, not moved.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `git diff --stat origin/main -- .github/workflows/backend.yml` is **empty** (a `backend.yml` diff from this task is a defect)
- [ ] `git diff --stat origin/main -- infra/` is **empty** (the grant is PR 1's)
- [ ] `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/reprocess-tenant-documents.yml'))"` exits 0
- [ ] `grep -n "on:" -A6 .github/workflows/reprocess-tenant-documents.yml` shows `workflow_dispatch` and **no** `push` and **no** `schedule`
- [ ] `grep -n "options:" .github/workflows/reprocess-tenant-documents.yml` shows `[dev]` and **not** `demo`
- [ ] `grep -n "required: true" .github/workflows/reprocess-tenant-documents.yml` covers `tenant_id`
- [ ] `grep -n "concurrency" .github/workflows/reprocess-tenant-documents.yml` returns a group keyed by environment and tenant
- [ ] `grep -n "required reviewers\|reviewer gate" .github/workflows/reprocess-tenant-documents.yml` returns nothing
- [ ] `grep -rn "reprocess-tenant-documents" .github/workflows/demo-promote.yml` returns nothing
- [ ] `grep -n "AZURE_CLIENT_ID" .github/workflows/reprocess-tenant-documents.yml` returns nothing
- [ ] `grep -rn "NpgsqlConnection\|SET app.tenant_id\|psql\|BYPASSRLS" backend/src/Raffa.Tools/` returns nothing
- [ ] `grep -rn "Actor" backend/src/Raffa.Tools/` shows only the literal `"system:bulk-reprocess"` — no interpolation, no `github.actor`
- [ ] running the console against a `dev` tenant whose worklist is empty exits **non-zero** and says so
- [ ] the acceptance walk records: verify → worklist N → console → verify → worklist **0** and `%PDF` gone

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the worklist query binds the tenant with the three-argument `Configure`; with the two-argument form the test **fails** rather than returning zero rows silently | `backend/tests/Raffa.Documents.Contracts.Tests/BulkReprocessTenantBindingTests.cs` (**new**) |
| unit | a publish failure on document 2 of 5 stops the loop, exits non-zero, prints `1/5`, and does **not** attempt documents 3–5 | `backend/tests/Raffa.Documents.Contracts.Tests/BulkReprocessTenantBindingTests.cs` (**new**) |
| unit | the run-scoped audit row carries `requestedBy` in `Detail` and `system:bulk-reprocess` in `Actor`; no input string reaches `Actor` | `backend/tests/Raffa.Audit.Tests/BulkReprocessAuditActorTests.cs` (**new**) |
| architecture | the console is in the all-projects set and **absent** from the domain-module set and the allow-list; a domain module referencing it is a violation the theory now catches | `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` |
| architecture | the new workflow does not trip the auth-seam scanner | `backend/tests/Raffa.ArchitectureTests/AuthenticationSeamAbsenceTests.cs` (existing, must stay green) |
| integration | the single-document Admin reprocess path is unchanged (A16-3) | `backend/tests/Raffa.Api.Tests/DocumentAdminActionsAuthorizationTests.cs` |
| manual (`dev`) | A17-S2: verify → console → verify, worklist 0 and `%PDF` gone, every document terminal | `docs/waves/w17-acceptance.md`, written by `E22/F06/US01/T01` |

## Open questions blocking this task

- **none blocking.** OQ-w17-sa-02, OQ-w17-sec-01, OQ-w17-sec-04 and OQ-w17-008
  are all ruled.
- **OQ-w17-dm-04 is open and deliberately produces no DoD line here**
  (assumption in force: `dev` carries no required reviewers). Do not invent a
  weaker substitute assertion.
- ⚠ **Operator sequencing, not a task**: the `raffa-dev` apply of PR 1 must be
  **confirmed landed in the HCP UI** before this workflow is dispatched even
  once (ADR-016 w17 clause 44).
- ⚠ **This task `depends_on` nothing in the wave** (ADR-014 w17 clause 7.2), and
  nothing in the wave `depends_on` the Terraform task at build time (clause 7.1).
  The console **compiles and its tests pass without the role assignment** — the
  grant is needed to *dispatch* it, which is an operator act. Do not re-introduce
  a build edge onto `servicebus-ci-send-grant`: it would make the DAG assert a
  dependency that does not exist and blur the PR 1 / PR 2 split.

## Wave-spec entry

```yaml
- id: E20/F02/US02/T01
  prompt: reports/workitems/epic-20-w16-residuals/feature-02-bulk-reprocess-console/us-02-tenant-reprocess-console/tasks/task-01-tenant-reprocess-console.md
  produces: [bulk-reprocess-console]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
