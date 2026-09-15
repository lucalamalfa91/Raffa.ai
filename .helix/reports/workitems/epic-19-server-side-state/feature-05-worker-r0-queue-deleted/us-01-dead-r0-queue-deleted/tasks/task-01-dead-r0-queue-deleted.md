---
id: E19/F05/US01/T01
type: task
story: us-01-dead-r0-queue-deleted
wave: w16
status: live
target_repo: raffa-backend
---

# task-01-dead-r0-queue-deleted — Delete the R0 placeholder queue and its registration

## Coding objective

`backend/src/Raffa.Worker/WorkerServiceCollectionExtensions.cs:70-72`
unconditionally registers the R0 placeholder queue — `AddSingleton<InMemoryQueueConsumer>()`,
`AddSingleton<IQueueConsumer>(...)`, `AddHostedService<QueueConsumerHostedService>()`
— **alongside** the real ADR-027 Service Bus consumer. That is a second live
`IHostedService` in every Worker process, including deployed environments. It is
inert because nothing publishes to it, but its own documentation
(`backend/src/Raffa.Worker/Queue/IQueueConsumer.cs:13-18`) still calls itself an
"R0 placeholder" and a Service Bus implementation "a later task", which has been
false since NW-27 shipped in w15.

Delete the three files and the three registration lines, and clear the one stale
comment inside this module that still points at the deleted type
(`backend/src/Raffa.Worker/Scheduling/IActiveRenewalContractsSource.cs:18`),
because a dangling reference to a deleted placeholder is exactly the stale record
this wave is spending a whole item deleting elsewhere. The API host carries one
more such comment; it belongs to `E18/F03/US01/T01`'s prose sweep in a later
phase and **is not this task's to edit**.

## Parent story AC covered

- AC-1 `Raffa.Worker` registers and boots exactly one `IHostedService` for document extraction.
- AC-2 `grep -rn "IQueueConsumer" backend/` returns no match outside historical ADR / acceptance records.
- AC-3 `InMemoryExtractionQueue` and its registrations are unchanged.
- AC-4 The build and the Worker test project are green.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Worker/Queue/IQueueConsumer.cs` | deleted |
| `backend/src/Raffa.Worker/Queue/InMemoryQueueConsumer.cs` | deleted |
| `backend/src/Raffa.Worker/Queue/QueueConsumerHostedService.cs` | deleted |
| `backend/src/Raffa.Worker/WorkerServiceCollectionExtensions.cs` | drop the three registration lines at `:70-72` |
| `backend/src/Raffa.Worker/Scheduling/IActiveRenewalContractsSource.cs` | the comment at `:18` stops citing the deleted interface |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: W16-01`.

Decision row: `reports/architecture/waves/w16.md`, **W16-01** — `seats: none`;
software-architect recorded the deletion at the table "rather than leaving it to
a task's judgement, because the two in-memory queues are one grep apart".

- **Architecture decisions in force**: **ADR-027** w16 clause 2 (the deletion
  this ADR superseded in w15 but never performed); **ADR-002** w16 clause 5 (the
  same deletion, and the boundary below).
- **THE LINE THIS TASK MUST NOT CROSS.** `InMemoryExtractionQueue` in
  `backend/src/Raffa.Messaging/MessagingServiceCollectionExtensions.cs:37-41`
  (publisher side) and `:61-65` (consumer side) is a **different object with a
  similar name**. It is the documented CI/local transport and it **stays**.
  ADR-002 w16 clause 5: *"The two must not be confused."* A task that deletes it
  breaks local and CI document processing.
- **Infra-free, verified at the table**: the worker container declares no
  `Queue__*` environment variable anywhere in its block
  (`infra/modules/containerapps/main.tf:348-461`), so this deletion leaves no
  dead configuration key. **No `infra/` file is opened by this task** (ADR-016
  w16 clause 27).
- **Do not touch**: anything under `backend/src/Raffa.Messaging/`, the
  `ServiceBusExtractionConsumerHostedService`, or any of the nine services
  `E18/F03/US02/T01` edits in this same phase.
- **Release valve**: this is the wave's only `could` and the **first** item to be
  cut if the cap binds (ADR-001 w16 clause 6). It is not cut in this plan — the
  wave lands 13 live tasks against a cap of 20.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Worker.Tests` exit 0
- [ ] `grep -rn "IQueueConsumer" backend/` returns **no match**
- [ ] `grep -rn "InMemoryExtractionQueue" backend/src/Raffa.Messaging/MessagingServiceCollectionExtensions.cs` still returns **the same matches as before this task** — the CI/local transport is untouched
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter Extraction` exit 0 — local/CI document processing still works

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the Worker's service collection resolves exactly one hosted service for document extraction | `backend/tests/Raffa.Worker.Tests/` |
| integration | the in-memory extraction transport still processes a document end to end without Service Bus | `backend/tests/Raffa.IntegrationTests/` |

## Open questions blocking this task

- none.

## Wave-spec entry

```yaml
- id: E19/F05/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-05-worker-r0-queue-deleted/us-01-dead-r0-queue-deleted/tasks/task-01-dead-r0-queue-deleted.md
  produces: [worker-r0-queue-deleted]
  depends_on: []
  effort: S
  layer: backend
  status: live
```
