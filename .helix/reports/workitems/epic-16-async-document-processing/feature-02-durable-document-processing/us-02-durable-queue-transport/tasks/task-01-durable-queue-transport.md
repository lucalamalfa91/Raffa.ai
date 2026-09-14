---
id: E16/F02/US02/T01
type: task
story: us-02-durable-queue-transport
wave: w15
status: live
target_repo: raffa-backend
---

# task-01-durable-queue-transport — Durable transport, `Raffa.Storage`, and a Worker that actually processes

## Coding objective

Make `Raffa.Worker` do the work. Add an `Azure.Messaging.ServiceBus` receiver on
the **`document-processing`** subscription of the `extraction-events` topic,
authenticated with `DefaultAzureCredential` (the container app already publishes
`AZURE_CLIENT_ID` at `infra/modules/containerapps/main.tf:278`), and a handler
that claims the `ExtractionJob` row with the conditional `UPDATE` from us-01,
opens the tenant scope from the message, runs
`DocumentProcessingPipeline.ProcessAsync`, and writes the terminal state. Add
the matching publisher behind a port on the API side, publishing **before** the
transaction commits. Extract the blob adapter into a new **`Raffa.Storage`**
project so the Worker can resolve `IDocumentStorage` at all — today
`AzureBlobDocumentStorage` lives inside `Raffa.Api`, which is the quiet reason
the Worker cannot read the file it is asked to OCR.

Keep the existing in-process path working: the transport is Service Bus when the
namespace is configured and the in-process queue otherwise, chosen by one
predicate, with a startup log naming the transport **and the namespace**. That
is what keeps CI green with no Service Bus and makes a missing role assignment
visible at startup rather than at first send.

## Parent story AC covered

- AC-1 … AC-10 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Storage/Raffa.Storage.csproj` | **new** — a provider adapter project in the `Raffa.AiGateway` shape. **No `DbContext`, no migration, no `.sql`** |
| `backend/src/Raffa.Storage/AzureBlobDocumentStorage.cs` | **moved** from `backend/src/Raffa.Api/Infrastructure/AzureBlobDocumentStorage.cs`, made public to its consumers |
| `backend/src/Raffa.Storage/StorageServiceCollectionExtensions.cs` | **new** — the registration both hosts call |
| `backend/src/Raffa.Api/Infrastructure/DocumentStorageServiceCollectionExtensions.cs` | delegates to `Raffa.Storage`; the `IDocumentStorage` interface stays where it is, in `backend/src/Raffa.SharedKernel/Storage/IDocumentStorage.cs` |
| `backend/Raffa.slnx` | add `Raffa.Storage` (and its test project, if one is added) |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/` | **new** outbound port for "this job is ready to be worked", plus the message contract carrying **ids only** |
| `backend/src/Raffa.Api/Infrastructure/` | **new** Service Bus publisher adapter implementing that port; the in-process publisher kept as the fallback implementation |
| `backend/src/Raffa.Api/Program.cs` | register the publisher behind the transport predicate; emit the startup log naming transport **and** namespace. **Single-writer of this file in phase 2** |
| `backend/src/Raffa.Api/Raffa.Api.csproj` | `Azure.Messaging.ServiceBus`, `Azure.Identity`, project reference to `Raffa.Storage` |
| `backend/src/Raffa.Worker/Queue/ServiceBusQueueConsumer.cs` | **new** — the receiver, `MaxAutoLockRenewalMinutes = 30`, subscription constant **`document-processing`** |
| `backend/src/Raffa.Worker/Queue/IQueueConsumer.cs`, `InMemoryQueueConsumer.cs` | the port gains what the Service Bus implementation needs (`DeliveryCount`, complete / abandon / dead-letter); the in-process implementation keeps working |
| `backend/src/Raffa.Worker/Queue/QueueConsumerHostedService.cs` | **stop logging and completing** (`:36-37`); dispatch to the new handler |
| `backend/src/Raffa.Worker/Queue/DocumentProcessingMessageHandler.cs` | **new** — claim, scope, pipeline, terminal state, `MaxAttempts = 3`, the §C6 `DeliveryCount` split |
| `backend/src/Raffa.Worker/WorkerServiceCollectionExtensions.cs`, `Program.cs` | wire the handler, the storage adapter and the transport predicate; `ConnectionStrings__Storage` **fail-fast**, every `ServiceBus__*` **optional** — the shape `Raffa.Worker/Program.cs:44` already uses for `Market` |
| `backend/src/Raffa.Worker/Raffa.Worker.csproj` | `Azure.Messaging.ServiceBus`, `Azure.Identity`, project references to `Raffa.Storage` and `Raffa.Documents.Contracts` |
| `backend/src/Raffa.Worker/appsettings.json` | the optional `ServiceBus` section shape. **Single-writer of this file in phase 2** |
| `backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs` | **package-scoped** amendment: `Azure.Identity` legal in `Raffa.Worker`. **Single-writer of this file in phase 2** |
| `backend/tests/Raffa.Worker.Tests/` | the handler tests below |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-27 (transport half)`.

Decision row: `reports/architecture/waves/w15.md` — **NW-27**,
software-architect cell (the queue contract, publish-before-commit, the claim,
`Raffa.Storage`, the inline-publisher fallback) and cloud-architect cell (the
subscription, the RBAC, the worker's missing storage connection).

- **Architecture decisions in force**: **ADR-027** §D1–§D5, §D10, §D12 plus
  §C2–§C4, §C6, §C7, §C8, §C10, §C11; **ADR-002** w15 footers (`Raffa.Storage`
  is an adapter, not a domain module); **ADR-009** (tenant scope from the
  message; the GUC stays **parameter-bound**); **ADR-011** w15 §2a–§2b (no
  Service Bus secret); **ADR-005** w15 clauses 9–10.
- **The message carries ids only and never a storage path.** A path travelling
  beside its own tenant id makes `EnsureWithinTenant` self-referential —
  it would confirm anything self-consistent (ADR-011 w15 clause 11, ADR-027
  §C11). Derive the blob location from the document row after the scope is open.
- **Publish before commit.** A lost commit leaves a harmless phantom message; a
  poller or sweeper over `extraction_job` is a cross-tenant read ADR-009
  forbids. Do not add one, in any form, including a "reconciliation" background
  service.
- **The pipeline must replace, not append.** At-least-once delivery otherwise
  duplicates every extracted fact — and §D5's replace step would mask a
  double-processing bug as *lost work* rather than as an error.
- **§C6's three cases are not one case.** A commit slower than the publish delay
  makes the row invisible at delivery. Completing the message there strands the
  document at `Uploaded` **permanently, on a POST that returned 201** — A15-2
  failing silently on the *success* path. Split on `DeliveryCount`: row present
  ⇒ complete; absent and `< 2` ⇒ **abandon**; absent and `≥ 2` ⇒ **dead-letter
  with reason `job-not-found`**. Two abandons, capped, no handler state. The
  check runs **inside the message's own tenant scope** and consults no other
  tenant (ADR-011 w15 clause 12).
- **`SdkAllowListTests` must be amended package-scoped.** Widening its single
  `AllowedProjectName` (`:25`, `:40-43`) is the one-word edit a task will reach
  for and it would make **`Azure.AI.*` legal outside `Raffa.AiGateway`**,
  silently un-guarding the Foundry boundary and making the non-vacuity check at
  `:62-77` meaningless. The wave-wide map, stated once so two tasks do not each
  invent one: `Azure.AI.*` → `Raffa.AiGateway`; `Azure.Identity` →
  `Raffa.AiGateway` + `Raffa.Api` + **`Raffa.Worker`**; `Microsoft.Graph` →
  `Raffa.Api` (that last entry is `E17/F01/US01/T01`'s, in a later phase — do
  not add it here).
- **Do not touch**: `.github/workflows/**` — the worker image is already built
  (`backend.yml:138-144`) and deployed (`:203-208`) in the same job as the API,
  and w15's CI-YAML set is **zero files**. Do not touch
  `DocumentsEndpointExtensions.cs` — the endpoint's own change is
  `E16/F02/US03/T01` in the next phase. Do not enable Service Bus sessions.
  Do not add a Key Vault secret for Service Bus.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Worker.Tests` exit 0 — claim-once under two deliveries; replace-not-append; the three `DeliveryCount` branches; `MaxAttempts = 3` writes `Failed` before any dead-letter
- [ ] `dotnet test backend/tests/Raffa.AiGateway.Tests` exit 0 — `SdkAllowListTests` allows `Azure.Identity` in `Raffa.Worker` **and still fails** a hypothetical `Azure.AI.*` reference in `Raffa.Api` or `Raffa.Worker`
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests` exit 0 — `DependencyDirectionTests` still passes with `Raffa.Storage` in the graph
- [ ] `dotnet test backend/Raffa.slnx` exit 0 — with **no** `ServiceBus__*` configured, the host starts, selects the in-process transport and logs it
- [ ] `grep -rn "extraction-worker" backend/src/` returns **no match** (the subscription is `document-processing`)
- [ ] `grep -rn "ServiceBus" backend/src/Raffa.Documents.Contracts/` returns **no match** — the transport is an adapter concern, never the domain module's (ADR-002)

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the conditional claim makes two deliveries one unit of work | `backend/tests/Raffa.Worker.Tests/` |
| unit | redelivery leaves one set of facts, not two (replace, not append) | `backend/tests/Raffa.Worker.Tests/` |
| unit | `DeliveryCount` 0/1 abandons, ≥ 2 dead-letters `job-not-found`, row present completes | `backend/tests/Raffa.Worker.Tests/` |
| unit | the transport predicate selects in-process with no namespace, and logs the choice | `backend/tests/Raffa.Worker.Tests/` |
| architecture | `Azure.Identity` allowed in `Raffa.Worker`; `Azure.AI.*` still refused everywhere but `Raffa.AiGateway` | `backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs` |

A15-1 and A15-2 are **not** provable here: CI has no Service Bus, no worker and
no browser. They are walked on deployed `dev` by `E16/F04/US01/T01`.

## Open questions blocking this task

- **OQ-w15-sec-04** — resolved: identity + RBAC, no Service Bus secret. Not blocking.
- **OQ-w15-012(a)** — resolved: one subscription is a work queue. Not blocking.

## Wave-spec entry
```yaml
- id: E16/F02/US02/T01
  prompt: reports/workitems/epic-16-async-document-processing/feature-02-durable-document-processing/us-02-durable-queue-transport/tasks/task-01-durable-queue-transport.md
  produces: [document-queue-transport]
  depends_on: [documents-async-schema]
  effort: L
  layer: backend
  status: live
```
