# ADR-002 — ASP.NET Core modular monolith + background worker (solution/project layout)

- **Status**: accepted
- **Date**: 2026-09-02
- **Deciders**: software-architect (owner); cloud-architect (host/runtime), security-architect (tenancy/RLS), delivery-manager (CI path filter) reconcile at council-close
- **Locked citations**: Backend — C# / ASP.NET Core (current LTS at implementation time); Modular monolith + background worker; No microservices split in V1 (locked-decisions.md).

## Context and problem statement

The brief locks a C# / ASP.NET Core LTS **modular monolith + background worker**, no microservices split in V1 (brief §7). The product spec's deployable topology names module boundaries explicitly: Identity/Workspace, Documents/Contracts, Suppliers/Products, Renewals/Savings/Quotes, Benchmark Service, and AI Gateway (spec §5.1), plus Chat and Audit (brief §7). The spec also requires that "AI is not the database" — canonical facts live in structured storage, and deterministic arithmetic/date/money calculations stay in code, not the LLM (spec Appendix C rules 1, 6; §7).

The question this ADR answers is **how** these modules are physically laid out in `backend/` such that boundaries stay explicit, the worker can reuse the same domain logic, and there is no accidental microservices split — while remaining separable later *only* when scale or team ownership requires it (spec §5.1).

## Decision drivers

- Module boundaries must follow the product spec, not a technical preference (brief §7).
- The worker must run the same domain/application logic as the API without duplication — extraction, renewal recomputation, and benchmark refresh all need to enqueue and process jobs.
- Domain modules must never call a provider directly (AI → AI Gateway; benchmark → Benchmark Service) — the gateway boundary must be a hard compile-time dependency direction.
- A future microservices split must be possible but must not be done now ("no microservices split in V1").

## Considered options

1. **One project per module + shared kernel + gateway/worker host projects** — separate class-library project per bounded context, referenced by a thin API host and a thin worker host.
2. **Single monolithic project with folders** — one ASP.NET project, modules as folders/namespaces only.
3. **One project per module, each self-hostable** — each module is an independently runnable host (mini-services), which is a de-facto microservices split.

## Decision outcome

**Chosen: Option 1** — one class-library project per bounded context, a thin Composition Root (the API host) that wires them via in-process mediator/dependency injection, and a thin Worker host that references the same libraries and consumes the same queue. This is because it makes module boundaries compile-time explicit (satisfying the spec's naming and the future-split intent) without introducing the network/process boundaries that would be a microservices split.

### Consequences

- **Good**: Module boundaries are enforced by project references (a domain module cannot reference the AI Gateway or Benchmark Service implementation, only their abstractions). The worker reuses the same application/domain libraries, so "recompute renewals" and "run extraction" are shared code, not copies. Future split = extract a module + its host without rewriting logic.
- **Bad**: More `.csproj` files to manage than a single folder project; a careless project-reference addition can still couple modules, so a code-review/architecture-test rule must police dependency direction.
- **Neutral**: API versioning, mediator choice, and ORM/access library are separate decisions (see ADR-data-store, and council-open-questions CQ-005).

## Pros and cons of the options

### Option 1 — one project per module + shared kernel + hosts
- Good: compile-time boundaries; worker reuses logic; explicit direction of dependencies; aligns with spec module naming.
- Bad: more projects; needs an architecture test to keep the layering honest.

### Option 2 — single project, folders
- Good: simplest to start; no project-reference discipline.
- Bad: boundaries are convention-only (a module can reach into another) — does not satisfy the explicit-boundary intent of spec §5.1; worker would have to reference the whole monolith.

### Option 3 — each module self-hostable
- Good: maximum independence.
- Bad: that is a microservices split by another name; explicitly against the locked "no microservices split in V1".

## Implications for the decomposition

Every task that creates or extends a domain capability must add code to the matching bounded-context project and must **not** reference a provider (AI or benchmark) directly. The AI Gateway and Benchmark Service are separate projects exposing interfaces consumed by domain modules; their implementations live behind the gateway/service project boundary. Architecture tests must fail the build if a domain project references a provider SDK or another domain project's internals. The worker host and API host share the same application services; queue message handlers belong to the worker host, not to domain projects.

## Assumptions

- "Current LTS" is .NET 10 (the LTS at the expected implementation date); final target confirmed at implementation time.
- API versioning scheme (URI prefix `/api/v1` vs header) is an open question carried in reports/open-questions.md (CQ-005 subset).
- The mediator/DI pattern is in-process; no durable outbox/messaging middleware is assumed beyond the queue at R0.

## Amendment (2026-09-13, wave w15 — the queue becomes real, and a second host-shared project appears)

Serves **NW-27**. The **Decision outcome above is unchanged and still in force**:
one class-library project per bounded context, a thin API host, a thin Worker
host referencing the same libraries and consuming the same queue, and **no
microservices split in V1**. Nothing in this footer moves a module boundary.
Detail lives in **ADR-027**; this footer records only what changes in ADR-002's
own subject — the project layout and the dependency rules.

**1. The queue stops being a diagram and becomes a port.** ADR-002's Decision
already says the Worker "consumes the same queue"; until w15 no queue existed in
code. NW-27 adds `Raffa.SharedKernel/Messaging/` — `ExtractionRequested`,
`IExtractionQueuePublisher`, `ExtractionQueueNames` — and an adapter per host.
The port sits in `Raffa.SharedKernel` for the same reason `IDocumentStorage`
(`Storage/IDocumentStorage.cs:23`) and `IAuditWriter` (`IAuditWriter.cs:8`)
already do: it is a cross-host port, and `Raffa.Api` cannot see `Raffa.Worker`'s
types (`IQueueConsumer` and `QueueMessage` are `internal` with
`InternalsVisibleTo("Raffa.Worker.Tests")` only — `Raffa.Worker/AssemblyInfo.cs:9`).

**The rule this ADR's Implications section already states is unchanged and now
binds for the first time**: *"queue message handlers belong to the worker host,
not to domain projects."* `ExtractionMessageHandler` and the Service Bus
consumer are `internal` to `Raffa.Worker`, which is also what keeps
`Host_must_not_contain_domain_types` green.

**2. One new project: `Raffa.Storage`.** `AzureBlobDocumentStorage` and its
`ServiceCollectionExtensions` move out of `Raffa.Api/Infrastructure/`, taking
`Azure.Storage.Blobs` with them. Referenced by **the two hosts and by nothing
else** — the `Raffa.AiGateway` shape.

This is forced, not stylistic. The adapter is `internal sealed` to a **host**
(`AzureBlobDocumentStorage.cs:14`) and is registered in exactly one place
(`Raffa.Api/Infrastructure/DocumentStorageServiceCollectionExtensions.cs:26`),
while four services the Worker now needs require `IDocumentStorage`. The
codebase already names this failure mode in the abstract:
`WorkerServiceCollectionExtensions.AddWorkerHost`'s doc comment (`:28-40`) warns
that a module's `AddXxx` "registers that service in *any* host that calls it —
including this one … a landmine under any host-builder configuration that
validates the DI graph eagerly". That warning was written about `IAuditWriter`
and closed by calling `AddAuditModule`; **the same remedy is unavailable for
`IDocumentStorage` precisely because the adapter belongs to a host, not to a
module.** Duplicating it into `Raffa.Worker` is rejected: `DocumentStoragePath`
exists because the tenant-path guard is security-relevant
(`SharedKernel/Storage/DocumentStoragePath.cs:24`), and two copies of it will
diverge.

`Raffa.Storage` is **not a bounded context** and holds no domain type. It is an
infrastructure adapter project of exactly the kind ADR-002 already sanctions for
`Raffa.AiGateway`, so **the module map of the product spec §5.1 is unchanged**
and the "no microservices split" lock is untouched — this adds no host, no
process and no network boundary.

**3. Dependency direction, stated so the architecture tests can enforce it.**
`Raffa.Storage` → `[Raffa.SharedKernel]` only. No domain module may reference
it; they depend on `IDocumentStorage` in the shared kernel exactly as they do
today, so **no domain project's reference list changes**. `Raffa.slnx` and
`DependencyDirectionTests.cs` are both single-writer files this wave and are
edited once, by the task that creates the project.

**4. `Microsoft.Graph` joins the forbidden SDK prefixes** (NW-67, ADR-026's w15
footer). `DependencyDirectionTests.ForbiddenSdkPrefixes` (`:76-84`) does not
currently list it, so ADR-002's rule that a domain module never holds a provider
SDK is **stated but unenforced** for Graph. Adding the prefix makes the existing
rule testable; it changes no boundary.

**5. What retires from the Assumptions.** *"No durable outbox/messaging
middleware is assumed beyond the queue at R0"* — w15 lands the queue it
anticipated. **It is a broker, not an outbox**: ADR-027 §D2 deliberately
publishes *before* the commit and keeps the already-written `ExtractionJob` row
as the durable work record, because a transactional-outbox table would need a
cross-tenant sweep that ADR-009 forbids. The two other assumptions (.NET LTS,
API versioning as an open question) are untouched.

**`waves/w15.md` records this under NW-27.** No endpoint, table or module
boundary in ADR-002's body moves.

## Amendment (2026-09-13, wave w15 round 2 — clause 4's guard is corrected: the enforcing test is `SdkAllowListTests`, and it must be package-scoped)

Serves **NW-67**, and **NW-27** inherits it. The **Decision outcome is unchanged**,
and so are clauses **1, 2, 3 and 5** of the first w15 footer — the messaging port,
`Raffa.Storage`, the dependency direction and the retired assumption all stand as
written. **Clause 4 is corrected**, adopting security-architect's ADR-025 §J.1.
Verified here at the source rather than taken on their word, because the
difference is between a rule and a build failure.

**What clause 4 got wrong.** It claimed that adding `Microsoft.Graph` to
`DependencyDirectionTests.ForbiddenSdkPrefixes` makes ADR-002's provider-SDK rule
*"enforced rather than merely stated"*. It does not.
`Domain_module_must_not_reference_provider_sdks` builds its path as
`src/{moduleName}/{moduleName}.csproj` (`DependencyDirectionTests.cs:111`) from
the **fixed ADR-002 domain-module list** (`:60-73`), and `SdkAllowListTests.cs:13-19`
already says so in its own words. **The Graph adapter lives in `Raffa.Api` — a
host that list never scans** (and deliberately: the same test must not forbid
`Raffa.Api`'s legitimate `Azure.Storage.Blobs`). Clause 4 as written therefore
enforces nothing where the SDK actually lands.

**The correction has two halves, and the first is that clause 4 is not deleted.**

1. **`Microsoft.Graph` still joins `ForbiddenSdkPrefixes`, narrowed to what that
   list can do.** The existing entries are `"Azure."`, `"Microsoft.Azure."`,
   `"Microsoft.AI."`, `"OpenAI"`, `"Google.Cloud."`, `"Amazon."` (`:78-83`) —
   **none matches `Microsoft.Graph`**, so a domain module could take a direct
   Graph dependency today and no test would object. That is a real hole in
   ADR-002's own rule and closing it is correct. It is simply not the rule about
   the host.
2. **The host rule belongs to `SdkAllowListTests`**
   (`backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs`), which scans every
   `*.csproj` under the solution root (`:31`) — *"hosts and tests included"*
   (`:19`). Its amendment is **package-scoped: a per-prefix map, never a second
   `AllowedProjectName`.** Widening the single allowed project (`:25`, `:40-43`)
   is the one-word edit a task will reach for, and it would make **`Azure.AI.*`
   legal in `Raffa.Api`** — silently un-guarding the ADR-004 / ADR-017 Foundry
   boundary that this ADR's "only the gateway holds a provider SDK" rule exists
   to protect, and making the test's own non-vacuity proof meaningless.

**The map w15 needs, stated once here so two items do not each invent one:**

| Prefix | Projects permitted to reference it | Why |
|---|---|---|
| `Azure.AI.*` | `Raffa.AiGateway` | unchanged — ADR-004 / ADR-017 |
| `Azure.Identity` | `Raffa.AiGateway`, `Raffa.Api`, `Raffa.Worker` | Graph in the API (NW-67); Service Bus RBAC in **both** hosts (NW-27 — cloud-architect's topic-scoped role assignments mean `DefaultAzureCredential`, not a connection string) |
| `Microsoft.Graph` | `Raffa.Api` | this is what actually enforces "one Graph call site" |

`SdkAllowListTests.cs` is consequently a **single-writer file contended by NW-27
and NW-67** — one owner or an explicit sequence, exactly like `Program.cs`. The
task that edits it must extend the class doc comment to say why each prefix has
the allow-list it has; the comment is already the place a future reader will
look, and `:13-19` already records the `Azure.Storage.Blobs` precedent that makes
this amendment run *with* the test's stated intent rather than against it.

**ADR-026's first w15 footer §7 item 3** instructs a task to add `Microsoft.Graph`
to `ForbiddenSdkPrefixes` citing *"ADR-002 w15 footer clause 4"*. That instruction
is **still correct and now known to be insufficient**; ADR-026's second w15 footer
§10 carries the rest.

No module boundary, project layout, dependency direction or host count changes.
**`waves/w15.md` records this under NW-67.**

## Amendment (2026-09-14, wave w16 — where the new state lives, where two modules are composed, and one dead host service is removed)

Written by software-architect (owner) at the w16 council table. Serves
**NW-13, NW-21, NW-32, NW-08, W16-01**. The **Decision outcome above is
unchanged** — modular monolith, one project per bounded context, shared kernel,
thin API/worker hosts, allow-listed dependency direction — and so are both w15
footers. **No project is added, no host is added, and no module's allow-list
moves.** Five clauses.

**1. `contract_negotiation_step` is owned by `Raffa.Documents.Contracts`**
(OQ-w16-006's module half; its product half is ADR-001's w16 clause 3).
`Raffa.Renewals` is the tempting owner — the ticks sit beside a renewal action
on the same screen — and it is **structurally impossible**: that module's
allow-list is `[SharedKernel, Benchmark]` (`RenewalActionService.cs:23`),
enforced by `DependencyDirectionTests`, so it cannot reference a contract. The
table, the keys and the routes are **ADR-028 §D3**.

**2. NW-21's savings resolution is composed in `Raffa.Api`** — the one project
allowed to reference every module, and where `NegotiationOutcomePropagationService`
already sits. `Raffa.Quotes` cannot see `Raffa.Savings`, and `Raffa.Savings`
cannot see suppliers or contracts; `SavingsOpportunity.cs:19-22` says exactly
that and names `Raffa.Api` as the place such a cross-module check belongs. No
allow-list is widened to make the link work. **ADR-028 §D5.**

**3. `Raffa.Suppliers.Products` gains one read-only lookup** (normalized name →
supplier id). No module gains a dependency — `Raffa.Api` already references the
module — and the lookup **never writes**: `SupplierResolver`'s resolve-or-create
path is off-limits to the outcome flow, because recording a negotiation outcome
must not mint a supplier row as a side effect.

**4. NW-32's actor is a signature change across five modules, not a boundary
change.** Nine service types take a **required `string` actor, positionally
after the entity id** — the shape the three already-correct document paths use
(`DocumentsEndpointExtensions.cs:147,512,576`, all passing `caller.Identity!`).
**No new type and no ambient accessor**: an ambient actor would let the two
caller-less sites compile and silently write nothing, which is the defect under
a new name. Caller-less writes take a reserved **`system:<component>`**
principal (security-architect S16-9) — the convention already live at
`NegotiationOutcomePropagationService.cs:97` and `QuoteExtractionPipeline.SystemActor`.
No module's allow-list changes; the five modules keep their dependencies.

**5. Two deletions this ADR authorises.**

- **`WorkspacePrincipalAuthorization` is deleted whole**, not merely stripped of
  its `tenant_id` constant — adopting security-architect **S16-5**. Its only
  non-test consumer is the audit route NW-08 re-guards; leaving the type behind
  leaves a working claims-based authorizer for the next endpoint to pick up,
  which is precisely how this defect arrived. `TestPrincipalStartupFilter.cs:27-42`
  is test-only and its own comment `:11-12` says it is never registered by
  production `Program`.
- **`Raffa.Worker` drops the R0 queue trio** — `Queue/IQueueConsumer.cs`,
  `Queue/InMemoryQueueConsumer.cs`, `Queue/QueueConsumerHostedService.cs` and
  their registration at `WorkerServiceCollectionExtensions.cs:70-72` (W16-01), a
  second live `IHostedService` in every deployed Worker. **`InMemoryExtractionQueue`
  (`Raffa.Messaging`, `MessagingServiceCollectionExtensions.cs:37-41,61-65`)
  stays** — it is the documented CI/local transport. The two must not be
  confused; see the ADR-027 w16 footer.

`waves/w16.md` records this under NW-13, NW-21, NW-32 and W16-01.
