---
id: feature-02
type: feature
parent: epic-20
wave: w17
status: active
extends: epic-18 F03
---

# feature-02-bulk-reprocess-console — re-run a whole tenant through the product's own path

## Slice

An operator can re-run extraction across every document of one tenant by
dispatching a single `workflow_dispatch` workflow. The workflow runs a new
`Raffa.Tools` console on the GitHub runner, and that console **calls
`DocumentReprocessService.ReprocessAsync`
(`backend/src/Raffa.Documents.Contracts/Application/DocumentReprocessService.cs:62`)
once per document** — so the `extraction_job` reset, the Service Bus publish,
the chunk removal and the `document.reprocessed` audit row are all the product's
own code, never re-implemented in bash.

Two stories because they are **two pull requests in a fixed order**: the
Terraform role assignment merges first and is applied by HCP before the console
can publish anything (ADR-014 w17 clauses 1–2, ADR-016 w17 clause 44).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | servicebus-send-grant | w17 |
| us-02 | tenant-reprocess-console | w17 |

## Extends

**epic-18 F03** (`feature-03-interim-posture-retirement`) — w16's NW-31 was the
last task allowed to open `.github/workflows/**`; it deleted
`reprocess-tenant-documents.yml` and added the report-only
`verify-tenant-corpus.yml` in one commit (`21eee60`). This feature is the next
one, and `us-02` is the **only** task in w17 permitted to open CI YAML
(ADR-014 w16 clause 4, restated w17). It also completes **ADR-016 w16 clause
31**, which designed this console's shape and scheduled it here, and discharges
**ADR-022 §4 `:250-254`**'s "Owed to W17".

## Architecture decisions in force

- **ADR-002 w17 clause 1** — `Raffa.Tools` is a **third composition root**: no
  table, no endpoint, no business rule, referenced by nothing. It composes the
  service's seven dependencies
  (`DocumentReprocessService.cs:37-44`) through the owning modules' own
  `ServiceCollectionExtensions`, never hand-built. In
  `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` it joins
  **`AllRaffaProjects` (`:36`)** and **must not** be added to `DomainModules`
  (`:18`) or `AllowedReferences` (`:60`) — it is a host, not a module with a
  dependency budget.
- **ADR-027 w17 clause 3** — the console **calls** `ReprocessAsync` and **must
  not** re-implement `RequeueClassificationJobAsync` (`:138`): the
  `ClaimedAt = null` reset (`:168`) is what lets the Worker's
  compare-and-swap claim re-deliver, and `AttemptCount` is **deliberately
  preserved** (`:135-136`). A psql equivalent silently drops both.
- **ADR-005 w17 §15–§18, §22, §27 · ADR-007 w17 §5–§9** — a **third**
  topic-scoped `azurerm_role_assignment` in `infra/modules/servicebus`, role
  **`Azure Service Bus Data Sender`** and nothing else, carrying the same
  `lifecycle { ignore_changes = … }` both existing grants carry
  (`main.tf:80-86`, `:95-101`). Variable `ci_deploy_principal_id`, **required**,
  wired in **both** env roots in the same PR. **$0.00/month.** ⚠ `demo` moves at
  the **merge**, not at the promotion tag: `scripts/hcp_vcs_wiring.py:104-106`
  wires both workspaces to `main` under `infra/`.
- **ADR-022 w17 clauses 1–3, 6** — the Admin gate is **relocated to the CI
  plane**, not bypassed, and the relocation is written down as a four-link chain
  (trigger · Azure · database · tenant). `Azure Service Bus Data Sender`,
  topic-scoped, is permitted because the message is a **pointer, not content**
  and the Worker re-reads all authority under RLS. **Send crosses no
  confidentiality boundary; Receive would.** `target_environment` ships
  **`dev` only**.
- **ADR-009 w17 clauses 1, 4** — the console's own worklist query uses the
  **three-argument** `DocumentsContractsDbContextOptions.Configure`
  (`backend/src/Raffa.Documents.Contracts/Infrastructure/DocumentsContractsDbContextOptions.cs:50`,
  whose third argument is **optional**) inside `BeginScope`. Zero rows under a
  valid tenant **exits non-zero**. One tenant per run from an explicit GUID; no
  all-tenants mode.
- **ADR-011 w17 clauses 20, 23, 26** — actor is the fixed literal
  `system:bulk-reprocess`; **no CI-controlled string is ever interpolated into
  `AuditEvent.Actor`**; human attribution rides in `Detail`. ⚠ The console
  **stops at the first publish failure** — `RemoveChunksAsync` commits its own
  `SaveChangesAsync` before `PublishAsync`, so a failed publish destroys a
  document's chunks with no requeue and no trail.
- **ADR-016 w17 clauses 36–45 · ADR-014 w17 clauses 1–9** — the console runs
  `dotnet run` on the runner, not as a Container Apps Job; no image, no ACR
  push, no environment key, no `backend.yml` edit; the workflow inherits
  `verify-tenant-corpus.yml`'s gating and credential idiom **at least in full**;
  it is **never** added to `demo-promote.yml`; the two operator jobs **compose** —
  `verify-tenant-corpus.yml:147-169`'s worklist **is** the console's input set.

## Target repo

mixed — `raffa-infra` (`infra/modules/servicebus`, both env roots) for `us-01`
and `raffa-backend` (`backend/src/Raffa.Tools`, `backend/Raffa.slnx`,
`.github/workflows/`) for `us-02`.
