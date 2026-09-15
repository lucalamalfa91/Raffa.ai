---
id: us-01
type: user-story
parent: feature-05
wave: w16
status: active
---

# us-01-dead-r0-queue-deleted — The Worker boots one extraction service, not two

## Story

As the **operator of the deployed Worker**, I want the dead R0 placeholder queue
gone, so that **every Worker process boots exactly one hosted service for
document extraction and no reader is told a Service Bus implementation is still
"a later task"**.

## Acceptance criteria

- [ ] AC-1 `Raffa.Worker` registers and boots **exactly one** `IHostedService`
  for document extraction — the ADR-027 Service Bus consumer.
- [ ] AC-2 `grep -rn "IQueueConsumer" backend/` returns **no match** outside
  historical ADR / acceptance records.
- [ ] AC-3 `InMemoryExtractionQueue` and its registrations in
  `backend/src/Raffa.Messaging/MessagingServiceCollectionExtensions.cs` are
  **unchanged** — local and CI document processing still work.
- [ ] AC-4 `dotnet build backend/Raffa.slnx` and the Worker test project are
  green.

## Definition of done

- [ ] every AC above is verified by at least one test or command named in the task
- [ ] the change honours ADR-027 w16 clause 2 and ADR-002 w16 clause 5
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | contained deletion; no other w16 task opens `Raffa.Worker` |

## Architecture decisions in force

- **ADR-027** w16 clause 2 — the three files and the registration to delete.
- **ADR-002** w16 clause 5 — the same deletion, with the boundary: **the two
  in-memory queues are one grep apart and must not be confused.**

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | dead-r0-queue-deleted | S | phase-1 |

## Council decisions carried into this story

> **ADR-002 w16 clause 5**: "`Raffa.Worker` drops the R0 queue trio …, a second
> live `IHostedService` in every deployed Worker. **`InMemoryExtractionQueue`
> (`Raffa.Messaging`, `MessagingServiceCollectionExtensions.cs:37-41,61-65`)
> stays** — the documented CI/local transport. **The two must not be confused.**"

> **cloud-architect, w16 table**: "W16-01 verified infra-free — the worker
> container declares no `Queue__*` env var anywhere in its block
> (`infra/modules/containerapps/main.tf:348-461`), so deleting the R0 trio leaves
> no dead key."

## Open questions

- none. ADR-027 already superseded this queue in w15; this is the deletion it did
  not do.
