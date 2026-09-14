# ADR-027 — Asynchronous document processing: the extraction queue, the rejection state and the completeness contract

- **Status**: accepted
- **Date**: 2026-09-13
- **Deciders**: software-architect (owner); cloud-architect (Service Bus topology, worker runtime and env keys); security-architect (the worker's tenant scope, the pre-verdict blob window, audit verbs); product-owner (ADR-001 w15 footer — scope, the refusal record, *never counted*); ux-ui-designer (the states contract and the rejected row's treatment); client-architect (contract shape and generator limits); delivery-manager (deploy order, acceptance on deployed `dev`)
- **Wave**: w15 — "upload feels instant, and inviting a colleague works end to end"
- **Items served**: NW-27, NW-61, NW-10
- **Locked citations**: Backend — C#/ASP.NET Core LTS; modular monolith **+ background worker** (ADR-002); PostgreSQL + EF Core/npgsql (ADR-003); Microsoft Foundry only, behind the AI Gateway (ADR-004, ADR-017); RLS on every tenant table, no `BYPASSRLS`, no cross-tenant query path (ADR-009); CI applies checked-in idempotent SQL, never `MigrateAsync` (ADR-021). None is re-opened here.

## Context and problem statement

`POST /api/documents` today does the whole job on the request thread: it
parses or OCRs the file, calls the Foundry `classify` role, and only then
stores anything (ADR-024's intake row). The upload therefore takes as long as
the AI pipeline takes, and the pilot's first gesture — drop fifteen PDFs — is
the product's slowest moment. NW-27 requires the response to arrive once the
file is **stored**, with processing continuing on `Raffa.Worker` and
completing with the browser closed (A15-1, A15-2).

The worker host exists, is deployed, and consumes nothing:
`QueueConsumerHostedService` logs a message id and completes it
(`../backend/src/Raffa.Worker/Queue/QueueConsumerHostedService.cs:36-37`).
The `ExtractionJob` row that a queued document would need **is already
written** by the upload
(`DocumentUploadService.cs:114-121`, `Stage = Classification`,
`Status = Queued`), and its own comment describes a consumer that was never
built. So this is not a greenfield design; it is the completion of a shape the
codebase already laid out and the closing of the three gaps that stopped it.

Two constraints make the design non-obvious, and both are discovered in the
schema rather than in the requirements:

1. **`extraction_job` carries `ENABLE` *and* `FORCE ROW LEVEL SECURITY`**
   (`Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql:476-477`,
   policy `tenant_isolation` at `:478`). `FORCE` binds the table owner too, and
   `TenantRlsConnectionInterceptor` sets exactly one tenant uuid per connection
   (`Raffa.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs:232-238`).
   **"Select every queued job across tenants" returns zero rows**, and it cannot
   be made to return more without the two exits ADR-009 forbids outright
   (`ADR-009:12-13`, `:57-58`).
2. **A message cannot carry the parsed document.** `AdmissionDecision.Pages`
   holds the parsed text in memory (`AdmissionDecision.cs:67`) and the upload
   handler passes it straight into the pipeline's pages+classification overload
   (`DocumentsEndpointExtensions.cs:265-273`;
   `DocumentProcessingPipeline.cs:174-182`) — **one parse per document** today.

The second problem this ADR closes is NW-61's half that no client fix can
supply: **four incompatible definitions of "ready" are live in the product**,
and one of them is already stating a fabricated fact to users (§D8).

## Decision drivers

- ADR-009 is not negotiable. Any recovery mechanism that needs a cross-tenant
  read is not a candidate, however conventional it looks.
- At-least-once delivery is the only delivery guarantee on offer, so **every
  handler property must be stated against redelivery**, not against the happy
  path.
- A15-2 ("every row reaches a terminal state") must be a property of the
  **schema**, not of broker configuration an operator can change.
- The existing test suite asserts a processed document immediately after the
  POST; a design that requires rewriting those suites to land is more expensive
  than the feature.
- "No evidence, no claim" (spec §8.4, ADR-024) applies to *completeness* too: a
  screen that cannot tell "still processing" from "nothing found" will state
  the wrong one.
- Cheapest correct change: no new isolation axis, no denormalised projection,
  no second source of truth for what work is outstanding.

## Considered options

1. **Worker polls the database for queued jobs** — no broker, no message.
2. **Commit the row, then publish; a sweeper re-enqueues what the publish lost.**
3. **Publish before the commit; the `ExtractionJob` row is the durable record
   and a lost commit leaves a harmless phantom message** — chosen.
4. **One message per pipeline stage**, the worker re-publishing as it advances.

## Decision outcome

**Chosen: Option 3.** Publish-before-commit is not merely preferable here; it
is **the only ordering compatible with ADR-009 that needs no new isolation
axis**. Options 1 and 2 both require a query that finds work across tenants —
option 1 continuously, option 2 on recovery — and that query is exactly what
`FORCE ROW LEVEL SECURITY` makes impossible and ADR-009 makes unfixable. The
two orderings fail differently, and only one failure is cheap:

| Ordering | Crash window | Recovery |
|---|---|---|
| commit → publish | row committed, message never sent | needs a **cross-tenant sweep** — forbidden — or a second non-RLS ledger table |
| **publish → commit** | message sent, row never committed | **phantom message**: the handler finds no job, completes, done. Costs nothing. |

### D1 — the request keeps every check that is free; everything needing a model or OCR moves

| Stays in the request | Moves to the Worker |
|---|---|
| form read + `ContentLength` / `MaxFileBytes` → **413** (`DocumentsEndpointExtensions.cs:171-220`) | parse / OCR (`DocumentAdmissionGate.cs:107-117`) |
| `DocumentFormatSniffer.TryDetect` → **415** (`:222-227`) | Foundry `classify` — the admission verdict (`:133-148`) |
| `storage.SaveAsync` + `document` + `document_version` + `extraction_job` rows + the `document.uploaded` audit row (`DocumentUploadService.cs:80-145`) | staged extraction, supplier link, per-page embedding (`DocumentProcessingPipeline.cs:262-278`) |
| **publish**, then commit (D2) | every `processing_status` transition after `Uploaded` |

This split is what product-owner's ADR-001 w15 footer clause 2 records: format
and size are decided in the request, so **nothing is stored for a non-document**
and no `document` row is created for a file that is not one; content
classification is decided on the worker.

**`201` is kept; `202 Accepted` is rejected.** 202 reads more correctly, but the
resource genuinely is created, `Location` is genuinely valid, and moving an
operation's success code in a hand-written 5 425-line contract and its generated
client buys no behaviour. The honest asynchrony signal is `processingStatus`
plus the list's `stage` — both already exist and both already render.

**`422` leaves `POST /api/documents`.** After the split the only synchronous
refusals are 413 and 415; `not_a_contract` and `no_readable_text` both require a
parse. The 422 block (`DocumentsEndpointExtensions.cs:242-252`) is deleted and
its operation entry removed from the contract. A dead response code left in a
hand-written contract is worse than a removed one the client seat is told about.

### D2 — the message is a pointer; the `ExtractionJob` row is the work

```
Raffa.SharedKernel/Messaging/
  ExtractionRequested.cs         // sealed record (Guid TenantId, Guid DocumentId,
                                 //                Guid ExtractionJobId, int SchemaVersion)
  IExtractionQueuePublisher.cs   // Task PublishAsync(ExtractionRequested, CancellationToken)
  ExtractionQueueNames.cs        // TopicName = "extraction-events"; SubscriptionName = "extraction-worker"
```

The port lives in `Raffa.SharedKernel` for the reason `IDocumentStorage`
(`SharedKernel/Storage/IDocumentStorage.cs:23`) and `IAuditWriter`
(`SharedKernel/IAuditWriter.cs:8`) already do: it is a cross-host port, and
`Raffa.Api` cannot see `Raffa.Worker`'s types — `IQueueConsumer` and
`QueueMessage` are `internal` with `InternalsVisibleTo("Raffa.Worker.Tests")`
only (`Raffa.Worker/AssemblyInfo.cs:9`).

`DocumentUploadService` publishes after building the three entities and
**before** `SaveChangesAsync`. The ids are known because `ExtractionJob.Id` is
`ValueGeneratedNever` (`ExtractionJobConfiguration.cs:14-16`). The message is
**scheduled a configurable delay ahead (default 2 s)** so the common path never
races the commit. If the commit then fails, the message is a phantom: the
handler finds no job, completes it, and nothing is lost.

### D3 — idempotency is a database claim, never a broker feature

Service Bus duplicate detection is **not** required and must not be relied on.
The handler claims inside the message's own tenant scope:

```sql
UPDATE extraction_job
   SET status = 'Running', claimed_at = @now, claimed_by = @instance,
       attempt_count = attempt_count + 1, started_at = COALESCE(started_at, @now)
 WHERE id = @jobId AND tenant_id = @tenantId
   AND status IN ('Queued','Failed')
   AND (claimed_at IS NULL OR claimed_at < @now - @lease)
```

0 rows affected → a duplicate, a phantom or a late delivery → complete the
message and do nothing. A worker that dies mid-run holds the lease only until it
expires, and Service Bus redelivers the message it never completed — so the
crash case needs no sweeper, which is what keeps D-choice 3 free of a
cross-tenant read.

**The database, not the dead-letter queue, owns the terminal state.** When
`attempt_count` reaches the application's `MaxAttempts`, the handler itself
writes `processing_status = Failed` with the reason and **completes** the
message. `max_delivery_count` on the subscription is therefore set **strictly
above** `MaxAttempts`, so the DLQ stays empty in normal operation and a
non-empty DLQ is an operational alert rather than a product path. **This is what
makes A15-2 a property of the schema rather than of broker configuration.**

### D4 — one message per document, not one per stage

The worker runs the whole pipeline under one claim, advancing
`ExtractionJob.Stage` so the six live stage strings keep rendering
(`DocumentProcessingStageMap.cs:20-39`, already implemented verbatim from
`screens-v2.md:59-79`). The SDK processor auto-renews the lock. Option 4 is
rejected as premature: it would make the Worker a publisher too and multiply the
claim surface by eight (`ExtractionStage.cs:15-33`) for no acceptance benefit.
**Recorded as the named upgrade path** if one stage ever outlives a renewable
lock.

### D5 — the pipeline must replace, not append; under retry this is not optional

`StagedExtractionService.RunAsync` **appends on every run**: a new
`ExtractionJob` row per stage (`StagedExtractionService.cs:341-350`) plus
`ExtractionEvidences.Add` (`:522`, `:911`), `ContractLineItems.Add` (`:670`),
`Clauses.Add` (`:726`), `Obligations.Add` (`:778`), `Risks.Add` (`:832`). There
is no `RemoveRange` or `ExecuteDelete` anywhere in that file. The only
delete-then-replace in the product is for embeddings and lives in the
*reprocess* service (`EmbeddingRetrievalService.RemoveChunksAsync:163-181`,
called from `DocumentReprocessService.cs:84-87`), not in the pipeline.

Today nothing retries, so nobody has seen it. **At-least-once delivery makes it
acute: one redelivery duplicates every extracted fact of a contract.** So NW-27
lands a replace step at the head of the handler — delete this document's prior
`extraction_evidence`, `contract_line_item`, `clause`, `obligation`, `risk` and
non-`Classification` `extraction_job` rows, and reuse the existing
`RemoveChunksAsync` for embeddings — before the pipeline runs.

Two things fall out for free: **reprocess collapses into "re-enqueue"** (same
handler, same replace step), and a latent duplicate-facts defect in today's
reprocess path is fixed by the same code. **If the wave's cap binds, this is not
the thing to narrow** — it is exactly what A15-2 protects.

### D6 — a refused file becomes a terminal `Rejected` row, and its bytes are deleted

`DocumentProcessingStatus` has five values and no `Rejected`
(`Documents.Contracts/Domain/DocumentProcessingStatus.cs`). It is persisted as
the enum **name** into `processing_status character varying(30)` with **no
CHECK, no enum type and no default** (`documents-contracts.sql:110` — the only
occurrence of that identifier in the script), so the sixth value is a
**code-only change**. The intake's "a new status value is a migration" is, on
this schema, not true, and **ADR-021 needs no amendment for it**.

On `AdmissionOutcome.Rejected` (`AdmissionDecision.cs:23-32`: `NotAContract` |
`NoReadableText`, the only two) the worker:

1. sets `processing_status = Rejected`, terminal;
2. persists the rejection reason, detected type and confidence on the `document`
   row — three nullable columns, and **this** is the migration;
3. deletes the blob, so the bytes do not persist;
4. extracts nothing, creates no `contract` row, and puts nothing in the tenant
   `embedding` index — **ADR-024's isolation property is untouched**;
5. writes the existing `document.rejected` audit row unchanged, hash-keyed and
   content-free (`DocumentAdmissionGate.cs:222-251`).

**"Never store" survives in substance.** ADR-024 D3's rule is about a
non-contract's *content* living in the tenant's corpus. What persists after a
refusal is a content-free record of the refusal, which the audit row already
persisted before this wave.

**Counted is a server fact.** `Rejected` is **excluded from the `all` count**
(D7) while still being listed — which is how the design oracle's *never
counted* rule (`screens-v2.md:75`, `requirements.md` R-DOC-04) is preserved
**exactly**, and additionally survives a reload and a second browser, which the
session-only version cannot. It is never askable and never in the review queue.

**`DELETE /api/documents/{id}` needs no change — verified, not assumed.**
Product-owner's footer requires a rejected row to be removable by the existing
delete. `DocumentDeleteService.DeleteAsync:91-102` turns any storage exception
into a failure, so a blob this design already deleted could have broken it; but
`AzureBlobDocumentStorage.DeleteAsync:61-67` calls `DeleteIfExistsAsync` and its
doc comment states the property outright — *"idempotent delete — an
already-absent blob is a success"*. The clause holds for free.

### D7 — `GET /api/documents` gains a server-computed `counts` object

```
GET /api/documents   200 -> { items[], page, pageSize, totalCount,
                              counts: { all, needsAttention, processing, rejected } }
```

- `all` — every document **except** `Rejected` (this is the "All documents · N"
  number, and it is what makes *never counted* a server fact);
- `needsAttention` — `NeedsReview` + `Failed`, the definition the client's
  attention filter already applies, moved to the server rather than re-invented;
- `processing` — `Uploaded` + `Processing` (non-terminal);
- `rejected` — `Rejected`.

**The counts are tenant-wide, never page-wide.** The list is paged and the
client fetches `pageSize = 100` and buckets locally
(`useDocumentsList.ts:18`, `:121-123`); a page-scoped count would be a new lie
at a higher page. `DocumentQueryService.ListAsync` issues one `CountAsync` over
the filtered set (`DocumentQueryService.cs:100`) and has no
`GroupBy(ProcessingStatus)` anywhere — the counts are **one added grouped
query**, unfiltered by `?status=`, not four round trips.

This one field closes NW-61's second defect **and NW-10**: `RailNav` stops
reading a dead `sessionStorage` key and reads a number the server computed,
exactly as its sibling `useValidatedContractCount` already does. NW-10 needs no
seat and no ADR, but it needs this field to exist — which is why co-locating it
with NW-61 is the right call (OQ-w15-008).

### D8 — one definition of "ready", and the fabricated sentence it removes

Four incompatible definitions are live today:

| # | Definition | Where |
|---|---|---|
| 1 | `document.ProcessingStatus == Completed` on a linked document — **canonical**, and ADR-026 §D2's | `PortfolioQueryService.cs:238-247` (SQL `EXISTS`), `:190-193` |
| 2 | `Contract.Status != "completed"` — free text whose bootstrap value is the literal `"processing"` | `AskCopilotService.cs:790-792` |
| 3 | `portfolio.Items.Count` / `TotalCount`, **unfiltered**, passed as `validatedContractCount` | `AskCopilotService.cs:159, 178, 191, 200, 259`; `RoutingContext.cs:41` |
| 4 | `processingStatus ∈ {Uploaded, Processing}`, client-side | `web/src/routes/documents/useDocumentsList.ts:105-108` |

**#3 is a defect against ADR-024, not a style difference, and it is already
user-visible.** `AskCopilotService.cs:294` composes the abstain reply as
`$"Nothing in the {portfolio.Items.Count} validated contract(s) supports a
reliable answer."` from the **unfiltered** count — so the product **asserts to
the user** that N contracts are *validated* when N includes every bootstrap
shell a still-processing document created. That is precisely the "never a
fabricated fact" promise A15-3 exists to protect, broken before this wave
changes anything.

**Decision: #1 is the only definition.** #2 and #3 are deleted and call
ADR-026 §D2's `CountValidatedContractsAsync`; #4 becomes a render of D7's server
field. One swap fixes the routing gate and the sentence together.

### D9 — Contract 360, Portfolio and Ask get told, never left to infer

Contract 360 already carries `tabs.documents[].processingStatus`
(`ContractsEndpointExtensions.cs:288-296`), but there is no top-level signal, no
`stage`, and `Contract360QueryService` never queries `ExtractionJobs`. A
contract mid-pipeline therefore returns **200 with a fully shaped, entirely
empty tab tree** — `products`, `clauses`, `obligations`, `risks` all `[]` — and
the only way to distinguish that from "extraction found nothing" is to scan an
array client-side. That is the inference ADR-012 forbids.

```
GET /api/contracts/{id}   200 -> { ..., readiness: { state, stage, documentCount, completedDocumentCount } }
```

`state` derives from definition #1 over the contract's linked documents:
`ready` (≥ 1 `Completed`), `processing` (none `Completed`, ≥ 1 non-terminal),
`unavailable` (every linked document terminal, none `Completed`). `stage`
mirrors the Documents list's live string and is null unless `state = processing`.
One object, so a screen asks one question.

- `GET /api/contracts` gains `processingDocumentCount` beside its existing
  envelope (`PortfolioEndpointExtensions.cs:83-104`), so the empty state can say
  "3 documents still processing" instead of "no contracts". No row-level field.
- **Ask's pre-question off-state** is driven by D7's counts; the `askOffReason`
  copy already ships verbatim (`askViewModel.ts:254` from `screens-v2.md:25-30`).
  No new endpoint.
- Ask's **server** turn stops grounding on unvalidated contracts via D8. The
  no-tools / grounding contract is untouched: `AnswerPromptV2.cs:33-35` and
  `GroundingGuard.cs:40-70` stay exactly as they are — this changes only what is
  allowed into the pack.

### D10 — transport selection keeps CI green, and must announce itself

`ServiceBusExtractionQueuePublisher` is registered when the Service Bus
connection is configured; otherwise an **`InlineExtractionQueuePublisher` runs
the handler synchronously in-process**. This is the codebase's own idiom —
Foundry when `AiGateway:Endpoint` is set, fixture otherwise (ADR-024) — and it
is what keeps developer machines and the existing suites working:
`DocumentUploadEndpointTests`, `R1EndToEndTests` and `R1DocumentsV2EndToEndTests`
all assert a processed document immediately after the POST.

**The trap is real and the task must close it**: an environment that forgets the
connection string silently keeps synchronous uploads and still passes every
test. So **the host logs the selected transport by name at startup**, and
**A15-1 / A15-2 are acceptance on deployed `dev`, never in CI**.

`Azure.Messaging.ServiceBus` lands in exactly two csproj files. It trips neither
guard: `DependencyDirectionTests.ForbiddenSdkPrefixes` applies only to the fixed
domain-module list (`DependencyDirectionTests.cs:18-33,76-84`), and
`SdkAllowListTests` forbids only `Azure.AI.*` and `Azure.Identity`
(`SdkAllowListTests.cs:23`) — which is why the transport authenticates with a
**Key Vault connection string**, exactly as `pg-cs` and `st-cs` already do, and
not with `DefaultAzureCredential`.

### D11 — `Raffa.Storage`, because the Worker cannot read the blob it is asked to OCR

`IDocumentStorage` is registered in exactly one place —
`AddSingleton<IDocumentStorage, AzureBlobDocumentStorage>`
(`Raffa.Api/Infrastructure/DocumentStorageServiceCollectionExtensions.cs:26`,
called from `Program.cs:71`) — and the adapter is `internal sealed` in
`Raffa.Api` (`AzureBlobDocumentStorage.cs:14`). `DocumentUploadService` (`:35`),
`DocumentReprocessService` (`:39`), `DocumentDeleteService` (`:45`) and
`DocumentPreviewService` (`:30`) all require it.

The codebase already documents this exact landmine and already closed it once
for a different port: `WorkerServiceCollectionExtensions.AddWorkerHost`'s doc
comment (`:28-40`) warns that `AddDocumentsContractsModule` "registers that
service in *any* host that calls it — including this one … a registered
`DocumentUploadService` whose own dependency graph can never resolve — harmless
while nothing in this host asks for it (today), but a landmine under any
host-builder configuration that validates the DI graph eagerly". It was written
about `IAuditWriter` and closed by calling `AddAuditModule`. **`IDocumentStorage`
is the same landmine, still armed**, and NW-27 is the task that makes this host
ask for it.

The `IAuditWriter` remedy is unavailable here, because the adapter is `internal`
to a **host**, not to a module. **Decision: promote it to a new `Raffa.Storage`
project** referenced by the two hosts and by nothing else — the `Raffa.AiGateway`
shape, and the only legal home for code two hosts share. Duplicating a
tenant-path-guarded adapter into `Raffa.Worker` is rejected:
`DocumentStoragePath.EnsureWithinTenant` exists precisely because that guard is
security-relevant (`SharedKernel/Storage/DocumentStoragePath.cs:24`), and two
copies of it will diverge.

**Positive finding, recorded so the decomposer does not re-solve it**: the
Documents/Contracts, Audit and Renewals modules **are already registered in the
worker** (`AddWorkerHost:66-68`) and `Raffa.Worker/Program.cs:16-34` fail-fasts
on their three connection strings. The DbContext and the tenant RLS interceptor
are therefore already composed in the worker — the hard half of "the worker opens
its own tenant scope" is done.

### D12 — the infrastructure this decision requires (cloud-architect owns; stated here as a contract)

The namespace and topic exist and are wired to nothing
(`infra/modules/servicebus/main.tf:11-23`). What this design needs:

- one **subscription** on `extraction-events` (proposed `extraction-worker`),
  with dead-lettering on expiry, a lock duration the SDK can renew, and
  `max_delivery_count` **strictly above** the application's `MaxAttempts` (D3);
- the connection as a Key Vault secret → container-app secret handle → env var,
  on **both** hosts (the API publishes, the Worker consumes);
- the worker app's missing keys. Verified exactly: the worker holds **one**
  secret, `pg-cs` (`infra/modules/containerapps/main.tf:224-228`), and five
  `ConnectionStrings__*` env vars. **Absent: `ConnectionStrings__Storage`, every
  `AiGateway__*`, and `AZURE_CLIENT_ID`.** A worker that must call `ocr`,
  `classify`, `extract` and `embed`, and must read the blob, needs all of them;
- worker `min_replicas ≥ 1` (it is `0` today, `:231-232`) — an app scaled to zero
  holds no subscription open. `max_replicas = 1` is **not** a blocker but is an
  uncosted throughput ceiling: A15-1 drops 15 PDFs and one replica drains them
  serially. A15-1 sets no deadline, so it **passes as written**; the number
  deserves a decision rather than a default, and D3's claim/lease model is safe
  at any replica count, which makes raising it a pure cost call.

### Consequences

- **Good**: the upload becomes as fast as a blob write; processing survives the
  browser; a redelivery cannot duplicate a fact (D5); terminal state is a schema
  property (D3); one definition of "ready" replaces four and removes a
  user-visible false statement (D8); reprocess collapses into re-enqueue; the
  existing suites keep passing through the inline publisher (D10).
- **Bad**: the product gains a broker on its critical path, and a
  mis-provisioned subscription is invisible from the API's side — which is why
  D10 mandates a startup log and delivery-manager's acceptance is on deployed
  `dev`. A phantom message is a real (harmless) artefact operators will
  eventually see in a log. `Raffa.Storage` is a new project, so `Raffa.slnx` and
  `DependencyDirectionTests` both move.
- **Neutral**: `201` and the 201 body shape are unchanged; the six stage strings,
  the grounding guards and the no-tools `answer` contract are untouched.

## Pros and cons of the options

### Option 1 — worker polls the database
- Good: no broker, no message, no ordering question.
- Bad: **impossible** under `FORCE` RLS without a cross-tenant read (ADR-009
  `:12-13`, `:57-58`). Not a trade-off — a dead end.

### Option 2 — commit, then publish, with a sweeper
- Good: the conventional ordering; no phantom messages.
- Bad: its recovery path is the same forbidden cross-tenant scan, or a second
  non-RLS ledger table — a second isolation axis, a dual write on every upload,
  and a drift direction of "work silently not done".

### Option 3 — publish, then commit (chosen)
- Good: the only ordering whose failure mode is recoverable *without* a new
  isolation axis; the durable record already exists and is already written.
- Bad: a phantom message on a failed commit; requires the handler to treat
  "no such job" as success rather than as an error.

### Option 4 — one message per stage
- Good: finer-grained retry; a long stage cannot outlive its lock.
- Bad: the Worker becomes a publisher; eight claim surfaces instead of one; no
  acceptance benefit this wave. Retained as the named upgrade path.

## Implications for the decomposition

1. **`Raffa.Storage` lands first and lands alone.** It moves two files out of
   `Raffa.Api/Infrastructure/`, adds a project to `Raffa.slnx` and edits
   `DependencyDirectionTests.cs` — all single-writer files. Every other NW-27
   task depends on it.
2. **The replace step (D5) ships in the same task as the consumer**, never
   later. A consumer without it turns the first redelivery into duplicated
   contract facts.
3. **One script, `documents-contracts.sql`, is regenerated by every NW-27
   migration.** It is byte-compared by a stale-check test, so it must never be
   hand-edited and **two tasks regenerating it concurrently will conflict** —
   order them. Regenerate with `dotnet ef migrations script --idempotent` from
   `backend/src/Raffa.Documents.Contracts`. The script is already in both CI
   arrays (`.github/workflows/backend.yml:276-286`, `:308-318`), so **no
   workflow edit** is needed (ADR-021 unchanged).
4. **Contract deltas** (`web/openapi/raffa-api.v1.json`, hand-written, no
   `$ref`, no `components`): `POST /api/documents` — remove the 422;
   `GET /api/documents` — add `counts`, add `Rejected` to the `processingStatus`
   enum, add the rejection fields; `GET /api/contracts/{id}` — add `readiness`;
   `GET /api/contracts` — add `processingDocumentCount`.
   **`processingStatus` appears in eight places, and the eighth is a query
   parameter** (`:864-878`) the generator never parses — so a task that edits
   only the response schemas leaves a contract that is wrong in a way **no build
   and no `tsc --noEmit` will catch** (client-architect's §8.1). The check is a
   grep of the contract, not a green build.
5. **`counts` and `readiness` are nested objects**, which
   `generate-api-client.mjs:97-119` renders as `unknown`. Either flatten them
   (`countsAll`, `countsProcessing`, …) or extend the generator **in the same
   task** — do not discover this at build time. `readiness.stage` is nullable and
   must therefore **not** carry an `enum` (the generator checks `enum` at
   `:63-65` before the nullable branch at `:72-76`, so a nullable enum silently
   loses its `null`).
6. **One task owns the contract file per phase**; `schema.ts` regenerates
   wholesale.
7. **Single-writer hotspots**: `Raffa.Api/Program.cs` (NW-27 publisher + storage
   move, NW-05 middleware, NW-68 mailer — three disjoint regions, one owner or a
   sequence); `Raffa.Api.csproj` (three package references across NW-27/67/68);
   `DocumentsEndpointExtensions.cs` (NW-27, NW-61, NW-05 — the wave's most
   contended endpoint file).
8. **Tests**: queue round-trip and claim idempotency (a redelivered message must
   not duplicate a fact — D5's property); restart-mid-batch durability (A15-2);
   `Rejected` terminal state and blob deletion; counts correct across pages;
   readiness on a mid-pipeline contract. **Changed**: every upload test that
   asserts a processed document post-POST keeps working through the inline
   publisher (D10) — that is this design's main regression budget.
9. **A new tenant-scoped table, if any task adds one, is not covered by the
   automatic RLS check.** `TenantRlsMigrationCheckTests` discovers tables from
   `TenantScopedEntity` subclasses **in `DocumentsContractsDbContext`**; a new
   table in another context needs a hand-written test, as `workspace_invitation`
   did in w14 (security-architect's §13.2). This design adds **columns, not
   tables**, so the check stays sufficient — stated so a task that changes that
   knows it must add the test.

## Assumptions

1. **Service Bus message size.** The 256 KB Standard-tier cap is the reason the
   message is a pointer; not re-verified against current Azure documentation
   this wave. The design does not become wrong if the cap is larger — a pointer
   is still right.
2. **`Rejected` needs no DDL.** Rests on `processing_status` being
   `character varying(30)` with no CHECK — read on this tree
   (`documents-contracts.sql:110`). The rejection **detail** columns still need a
   migration.
3. **The inline publisher keeps CI green.** Asserted from the fixtures' shape,
   not from a run: the Postgres Testcontainers suites cannot run on the
   operator's machine (Docker does not start), so no claim here is backed by a
   green local run.
4. **Worker throughput is a default, not a decision** (`max_replicas = 1`).
   Recorded for cloud-architect rather than assumed.
5. **The pre-verdict blob window** — a refused file's bytes sit in the tenant's
   own container, written by the tenant's own user, between upload and verdict,
   and are deleted on refusal. **Security-architect ratifies**; nothing is
   indexed and no `contract` row is created in that window.

## Amendment (2026-09-13, wave w15 round 2 — three corrections adopted at the table, and the askable count ruled)

Serves **NW-27, NW-61, NW-10**. **D1–D11 are unchanged and still in force**, as
are the considered options, the Consequences and the option comparison. This
footer corrects **D12** and **implication 5**, and answers ux-ui-designer's ask
on D7. All three corrections are re-verified at the source here, not adopted on
a peer's word — and all three are corrected rather than left silent **because
each prescribes work**: a decomposer reading them as written would do the wrong
thing, not merely know less.

### C1 — implication 5 is withdrawn: `counts` and `readiness` land inline, and the generator is not touched

Client-architect's correction, verified: `renderSchemaType`'s `"object"` case
**recurses** — `generate-api-client.mjs:109-111` maps every property through
`renderSchemaType(propSchema)` and emits a nested inline type. It returns
`unknown` only when `properties` is absent (`:107`) or the schema carries no
`type` the switch handles (`:114-118`, i.e. `$ref` / `oneOf`).

So **both remedies implication 5 offered are wrong work**: flattening to
`countsAll` / `countsProcessing` is unnecessary, and "extend the generator in the
same task" extends something that already works. The third and likeliest
outcome — a hand-written DTO in `client.ts` in front of an `unknown` — is
forbidden outright by ADR-012 `:53`. **Land `counts` and `readiness` inline as
D7/D8 specify, touch no generator, flatten nothing, hand-write nothing.**

**The trap that produced this error is in the source, and the task removes it in
the same edit.** That case's own doc comment (`:98-106`) reads *"Flat property
maps only … Extend this case … when a future endpoint needs a property whose
value is itself an object"* — a statement the next two lines of its own body
already falsify. It is stale in exactly the way
`DocumentProcessingPipeline.cs:65-77` (OQ-w15-003) and
`NullInvitationMailer.cs:28-29` are, both already named this wave; w15's
contract task is the first to exercise the recursion, so it is the one that owes
the correction.

**What survives implication 5 verbatim**: `readiness.stage` is nullable and must
therefore **not** carry an `enum` — `:63-65` checks `enum` *before* the nullable
union at `:72-76`, so a nullable enum silently loses its `null`. That half was
right and is unchanged.

### C2 — D12's worker key list is false, and acting on it fails the apply

Cloud-architect's correction, verified against
`infra/modules/containerapps/main.tf`: the **worker** container app already sets
`AZURE_CLIENT_ID` (`:278`) and all three `AiGateway__*` keys (`:283`, `:288`,
`:293`), placed by task E10/F02/US01/T01. D12's *"Absent: `ConnectionStrings__Storage`,
every `AiGateway__*`, and `AZURE_CLIENT_ID`"* is wrong on two of its three items;
`ConnectionStrings__Storage` at `:138` is in the **API** block, not the worker's.

This is not a cosmetic slip. A task adding the keys D12 lists would emit
**duplicate `env` names** in one container-app block — an **apply-time error**,
discovered at the HITL gate rather than in review. **The worker's one genuinely
missing key is `ConnectionStrings__Storage`**, it needs **no new module variable
and no root change** (`var.storage_connection_secret_id` already reaches the
module), and per cloud-architect it stays **fail-fast** while every
`ServiceBus__*` binds **optionally** — the shape `Raffa.Worker/Program.cs:44`
already uses for `Market`.

### C3 — D12's remaining bullets yield to ADR-005's w15 footer, which is the ruling D12 asked for

D12 is headed *"cloud-architect owns; stated here as a contract"*. That seat has
now ruled, so where the two differ **ADR-005's w15 footer governs** and this
footer records the deltas so no task builds from the superseded text:

- **`min_replicas ≥ 1` is rejected, not adopted.** ~$14/env/month × two
  environments is a new fixed monthly line the lock forbids. A KEDA
  `azure-servicebus` scale rule raises the worker from `0` instead, and
  `max_replicas` goes `1 → 3` on **both** apps. **D12's fourth bullet and
  Assumption 4 are superseded**: the uncosted throughput ceiling they flagged is
  real and is answered by the scale rule, not by a replica floor.
- **The subscription is `document-processing`**, not D12's proposed
  `extraction-worker`. One name, or the Terraform and the consumer disagree
  about which subscription holds the messages.
- **D12's second bullet is superseded: NW-27 adds no Key Vault secret.** The
  transport authenticates as the existing workload managed identity through two
  **topic-scoped** role assignments (Data Sender on the API, Data Receiver on
  the Worker). ADR-011's w15 secret ledger — `acs-connection` is the wave's
  **one** new entry — is therefore exact, and this ADR must not be read as
  asking for a second.
- **D10's selection predicate inherits the change**: the transport is Service
  Bus when the **namespace** is configured
  (`ServiceBus__FullyQualifiedNamespace`), inline otherwise. D10's mandated
  startup log is unchanged and now names the namespace beside the transport —
  which matters more under RBAC, where a missing role assignment fails at first
  send rather than at startup.
- `max_delivery_count = 5` strictly above the application's `MaxAttempts`,
  `lock_duration = PT5M` coupled to `ServiceBus__MaxAutoLockRenewalMinutes`,
  `default_message_ttl = P1D`, sessions deliberately **not** enabled — all
  cloud-architect's values, not restated here. **D3 is unchanged**: those
  numbers exist to keep the **database** the owner of the terminal state, which
  is what makes A15-2 a property of the schema.

### C4 — a consequence of C3 that no lane named, and it fails the *build*

Managed-identity auth puts `DefaultAzureCredential` — that is **`Azure.Identity`** —
in **both hosts**, the API as publisher and the Worker as consumer.
`SdkAllowListTests` (`backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs`)
scans **every** `*.csproj` under the solution root (`:31`), "hosts and tests
included" (`:19`), and forbids `Azure.Identity` outside `Raffa.AiGateway` (`:23`,
`:25`, `:40-43`). ADR-026's first w15 footer §2 amends that test for
**`Raffa.Api` only**, for Graph.

So **NW-27 turns that test red unless the same amendment also permits
`Azure.Identity` in `Raffa.Worker`** — and the tempting one-word repair (widen
`AllowedProjectName`) is precisely the edit security-architect's ADR-025 §J.1
forbids, because it would make `Azure.AI.*` legal in every host. The per-prefix
map for the whole wave is in **ADR-026's second w15 footer §10** and in
**ADR-002's second w15 footer**; the operative fact here is that
**`SdkAllowListTests.cs` is a single-writer file contended by NW-27 and NW-67** —
one owner or an explicit sequence, exactly like `Program.cs`.

### C5 — `counts` does not gain `askable`; the number that segment wants already exists

ux-ui-designer's ask on NW-61: `buildKbSummary` (`documentTable.ts:154-160`)
renders *"N documents · M askable · K waiting for your review"* and is wrong
twice — it counts the **fetched page** (`items.length`, `:155`), and it equates
askable with `processingStatus == "Completed"` (`:156`), a **document**-level
test where ADR-026 §D2's *validated* is a **contract**-level one. They asked for
`askable` on `counts` **or** the segment's removal.

**Ruled: `askable` does not join `counts`.** A document envelope cannot carry a
contract fact without minting a **fifth** definition of ready in the very wave
whose D8 collapses four into one. Each of the three numbers instead gets an
existing server source:

- **`N documents` → `counts.all`** (D7, tenant-wide, excludes `Rejected`).
- **`M askable` → the shell's existing §D2 number** — `contractCount` on the
  `GET /api/workspaces` row, which `useValidatedContractCount.ts:66` already
  reads and `AppShell.tsx:59` already passes down the `Outlet` context. **Zero
  new fields, zero new round trips, and it is by construction the same number
  the rail and Ask show** (D8's whole point). Whether the segment renders it or
  is dropped is **copy, and ux-ui-designer's** — the architectural half is only
  that it never recomputes askability from a page of documents.
- **`K waiting for your review` → `counts` gains `needsReview`.** D7's
  `needsAttention` is `NeedsReview + Failed`; this clause means `NeedsReview`
  alone, and rendering the wider number under the narrower words is the same
  class of defect D8 removes. It costs nothing: **D7's counts are projections of
  one grouped query**, so a bucket is a member, not a round trip.

**D7's object is therefore `counts { all, needsAttention, needsReview,
processing, rejected }`**, every member keeping its D7 definition. This is the
only change to a D-section's wire shape in this footer.

**`waves/w15.md` records this under NW-27 and NW-61.** No endpoint, table,
module boundary, migration or pipeline stage changes here: C1 removes work, C2
and C3 correct instructions, C4 names a contended file, and C5 adds one integer
to an object D7 already introduced.

## Amendment (2026-09-13, wave w15 round 3 — a stranded-document hole in D3, the app-side retry number, and three asks discharged)

Serves **NW-27, NW-61**. **D1–D2, D4–D12 stand; C1–C5 stand.** This footer
corrects **D3's disposition rule** (C6), sets the number ADR-005 assigns to this
seat (C7), repairs a constant C3 left stale (C8), adopts ADR-018 clause 2b into
D7 (C9), and discharges delivery-manager's and security-architect's asks (C10,
C11). C6 was found here, at this table, by reading D2 and D3 against each other
while discharging C7 — **it is a hole in this seat's own design, not a peer's
correction**, and it is the reason this round was worth taking.

### C6 — D3 completes a message that may not be a phantom, and the document is then stranded forever

D2 publishes **before** the commit, scheduled `2 s` ahead so "the common path
never races the commit". D3 then disposes of a claim that matches nothing:
*"0 rows affected → a duplicate, a phantom or a late delivery → complete the
message and do nothing."* **Those three cases are not one case, and one of them
is not safe to complete.**

A commit slower than the delay — 15 concurrent uploads under A15-1, pool
contention, a lock wait — makes the row invisible at delivery time and *visible
200 ms later*. D3 completes the message; the commit then lands; the
`extraction_job` row is `Queued` and **no message will ever be delivered again**.
There is no sweeper and there cannot be one: §0.1 and D-choice 3 rest on the fact
that a cross-tenant scan for orphaned work is impossible under `FORCE` RLS. So
the document sits at `Uploaded` **permanently**, on a POST that returned **201**.
That is A15-2 failing silently on the success path — the exact opposite of the
property D3 exists to guarantee.

The two failing cases are distinguishable, and only by facts the handler already
has:

| Observation | Meaning | Disposition |
|---|---|---|
| row exists, already `Running`/`Completed`, or lease live | duplicate or redelivery | **complete** — unchanged from D3 |
| row **absent** and `DeliveryCount < 2` | commit may still be in flight | **abandon** — let the broker redeliver |
| row **absent** and `DeliveryCount ≥ 2` | true phantom (the commit failed) | **dead-letter explicitly**, reason `job-not-found` |

`ServiceBusReceivedMessage.DeliveryCount` is the bound, so the abandons are
capped at **2** without any handler state, and the phantom gets an explicit,
traceable end instead of a silent one. Abandon is immediate in Service Bus and
supplies no backoff of its own, so the handler waits the same configurable delay
once and re-queries before abandoning.

**What this honestly does and does not buy.** It converts a silent permanent
stranding into a bounded window (~2 deliveries) plus an alarm. It does **not**
close the case where a commit lands after that window: that row is stranded too
— but it is now **visible in the DLQ** and **recoverable**, because D5 makes
reprocess a re-enqueue and an admin inside that tenant can drive it without any
cross-tenant read. **D3's "the database owns the terminal state" is restated
precisely: it holds for every job the handler ever claims; the residual case is
bounded, alarmed and tenant-recoverable, and closing it completely would require
the cross-tenant sweep ADR-009 forbids.** D2's "nothing is lost" is too strong
and is corrected to this.

**Hand-off**: a non-empty DLQ now also means *"an upload's commit failed"*, not
only *"a bug"* — **cloud-architect** and **delivery-manager** should read the
alert that way rather than as a defect signal.

### C7 — `MaxAttempts = 3`, and the slack is the point rather than the strictness

`ADR-005:358` sets `max_delivery_count = 5` and states *"The app-side number is
software-architect's; the inequality is joint and normative."* **Discharged:
`MaxAttempts = 3`.**

Strictly-below is necessary but not sufficient, because **broker deliveries and
application attempts do not advance together**: `attempt_count` rises only on a
successful claim (D3's `UPDATE`), while a delivery is consumed by any failure at
or before the claim — a transient Postgres error on the `UPDATE` itself, and now
C6's bounded abandons. `4` would satisfy the inequality and still let the broker
dead-letter **before** the handler ever wrote `Failed`, which is A15-2 failing
through arithmetic. `3` leaves the two deliveries C6 spends, and the worst case
closes exactly: 2 abandons + 3 claimed attempts = 5, with the third attempt
writing the terminal row and **completing**, so the ceiling is reached and never
breached.

`ServiceBus__MaxAutoLockRenewalMinutes = 30` (`ADR-005:520`) binds to
`ServiceBusProcessorOptions.MaxAutoLockRenewalDuration`; D4's "the SDK processor
auto-renews the lock" is that key and no other, against `lock_duration = PT5M`
(`ADR-005:361`). Confirmed as cloud-architect asked.

### C8 — D2's subscription constant is stale against C3, and it fails at first receive

C3 ruled the subscription is **`document-processing`** (`ADR-005:357`, `:519`),
but it corrected D12's prose and **left D2's code block** — `ExtractionQueueNames.cs
// SubscriptionName = "extraction-worker"` (`:124`) — untouched. D2 is the clause
that names the constant the code will actually carry, so a task implementing D2
verbatim opens a receiver on a subscription Terraform never created.

That failure is invisible where delivery-manager already warned it would be: a
worker that cannot start still produces a **green** `backend.yml` run. **`ExtractionQueueNames.SubscriptionName = "document-processing"`**; the value is
also configurable through `ServiceBus__SubscriptionName` (`ADR-005:519`,
optional). `TopicName = "extraction-events"` is **correct and unchanged** —
re-verified at source, `infra/modules/servicebus/main.tf:20-21`, and that file
ends at `:24` with **no subscription resource of any name**, which is what makes
the Terraform task's addition the single source of the name.

### C9 — D7's `needsAttention` takes ADR-018 clause 2b's definition

ux-ui-designer routed this to this seat's file. **The premise D7 gave is false,
and I re-read it at source rather than adopting it**: `isAttentionStatus` is
`processingStatus !== "Completed"` (`documentTable.ts:141-143`), its doc comment
`:136-138` derives it from the oracle (`app.jsx`'s `attnDocs`, R-DOC-06) and
marks the filter **default**, and `getFilterHint:163-166` promises only that
*"Completed documents are hidden."* So D7's *"the definition the client's
attention filter already applies"* was never true, and shipping it would list
fifteen just-dropped rows under a chip reading **0** — NW-61's reported defect,
re-created server-side under a server number.

**D7's bullet is replaced**: `needsAttention` is *not `Completed` **and** not
`Rejected`* — `Uploaded + Processing + NeedsReview + Failed`. `all`, `processing`
and `rejected` are unchanged; C5's `needsReview` is unchanged. Their rule — *a
chip's number and the rows it filters to are the same set* — governs, and their
rejected alternative (narrowing the filter) is not reached for here either.

Two consequences that are this seat's to state, because they follow from the
shape rather than from the screen:

1. **`counts` is five overlapping projections, not a partition.** `needsAttention`
   now **contains** `processing`, and `needsReview` is inside both. No member is
   derivable from the others by arithmetic and the members do not sum to
   `totalCount`. A test asserting a sum is wrong, not the endpoint.
2. **`all − needsAttention` must never be rendered as "askable".** It equals the
   count of `Completed` **documents** — precisely the fifth, document-level
   definition of ready that C5 refused to mint. C5's answer stands: askability is
   the contract-level §D2 number the shell already carries.

`Rejected` is new this wave, so `isAttentionStatus` needs the matching
`&& !== "Rejected"` or the filter and the chip disagree in mirror image. That is
a **third** site in `documentTable.ts`, beside the `RowStatus` and `semantics.ts:52`
pair ux-ui-designer flagged as compiling clean — **one task, three sites, checked
by grep and not by `tsc`**, as client-architect asked.

### C10 — `Raffa.Storage` adds no migration, and delivery-manager's CI-YAML set stays zero

Asked at this table: declare any new .NET module that carries its own migration
script, since that is the one thing that moves w15's CI-YAML set from zero to
one. **Declared: `Raffa.Storage` carries no `DbContext`, no `Migrations/`, no
`.sql` script and no connection string.** D11 promotes one `internal sealed`
blob adapter and its DI extension out of a host so two hosts may share it; it is
the `Raffa.AiGateway` shape — a provider adapter, not a domain module.

Therefore **`backend.yml`'s two script arrays are not edited** and w15's planned
CI-YAML set remains **ZERO files**, exactly as delivery-manager recorded. The
only script this wave regenerates is the existing `documents-contracts.sql`,
already in both arrays (D-implications). `Raffa.slnx` still gains the project,
which is a single-writer file this wave and is already listed as one.

### C11 — the message stays ids-only, and `AzureAd__` is unclaimed on this tree

Security-architect's ask, adopted as a **prohibition** rather than a note:
`ExtractionRequested` carries `TenantId`, `DocumentId`, `ExtractionJobId`,
`SchemaVersion` and **never a storage path**. The reason is theirs and is exact —
`DocumentStoragePath.EnsureWithinTenant` validates a path against *the tenant the
caller passes*, so a path travelling beside its own tenant id in one message
makes the guard self-referential and it would confirm anything self-consistent.
The worker re-derives the path from the ids inside its own tenant scope. Named
here because "carry the path and save a read" is exactly the optimisation a task
would reach for.

`AzureAd__`: confirmed as cloud-architect asked — **no `AzureAd*` key is read
anywhere in `backend/src` today** (the only hits are two doc comments recording
that this host wires no `AddAuthentication`/`AddJwtBearer` yet:
`ConversationsEndpointExtensions.cs:27`, `WorkspacePrincipalAuthorization.cs:15`).
The prefix is free, collides with nothing, and NW-05 mints it — bound by the host
in the established `GetSection(...).Bind(...)` shape.

**No endpoint, table, module boundary, migration or pipeline stage is added
here.** C6 changes one disposition rule inside the worker's handler, C7 and C8
set two values, C9 restates one wire field's predicate, C10 and C11 record a
declaration and a prohibition. `waves/w15.md` records this round under NW-27 and
NW-61.
