---
id: feature-05
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-16 F02
---

# feature-05-worker-r0-queue-deleted — One hosted service for document extraction, not two

## Slice

`backend/src/Raffa.Worker/WorkerServiceCollectionExtensions.cs:70-72`
unconditionally registers the R0 placeholder queue trio **alongside** the real
ADR-027 Service Bus consumer. It is a second live `IHostedService` in every
Worker process, including deployed environments. Nothing publishes to it, so it
is inert — but `Queue/IQueueConsumer.cs:13-18` still describes itself as an "R0
placeholder" and calls a Service Bus implementation "a later task", which has
been false since NW-27 landed in w15. This feature performs the deletion ADR-027
superseded but never executed.

Intake-originated (W16-01), `could`, contained: no contract, no migration, no
infra. The Worker container declares no `Queue__*` env var anywhere in its block
(`infra/modules/containerapps/main.tf:348-461`), so the deletion leaves no dead
key.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | dead-r0-queue-deleted | w16 |

## Architecture decisions in force

- **ADR-027** w16 clause 2 — delete `Raffa.Worker/Queue/IQueueConsumer.cs`,
  `Queue/InMemoryQueueConsumer.cs`, `Queue/QueueConsumerHostedService.cs` and
  their registration at `WorkerServiceCollectionExtensions.cs:70-72`.
- **ADR-002** w16 clause 5 — the same deletion, and the line that must not be
  crossed: **`InMemoryExtractionQueue` in `Raffa.Messaging`
  (`MessagingServiceCollectionExtensions.cs:37-41,61-65`) stays.** It is the
  documented CI/local transport. *A task that confuses the two breaks local and
  CI processing.*
- **ADR-001** w16 clause 6 — this is the wave's only `could` and the **first**
  release valve; if it is cut it goes to the head of W17's `could` tier and
  never displaces an item already queued for W17.

## Target repo

`raffa-backend`
