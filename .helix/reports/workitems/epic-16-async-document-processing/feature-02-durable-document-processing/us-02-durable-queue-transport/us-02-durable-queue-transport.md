---
id: us-02
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-02-durable-queue-transport — A durable message carries the work to the Worker

## Story

As the **platform**, I want a durable Service Bus message to carry a pointer to
the already-written `ExtractionJob` row, and `Raffa.Worker` to claim that row
and run the real pipeline, so that processing survives an API restart, a worker
restart and a redeploy — and so that the worker stops logging a message id and
completing it without doing anything.

## Acceptance criteria

- [ ] AC-1 `Raffa.Worker` consumes the **`document-processing`** subscription of the `extraction-events` topic and runs the real extraction pipeline. The existing hosted service, which logs a message id and completes it (`QueueConsumerHostedService.cs:36-37`), no longer stands in for a handler.
- [ ] AC-2 The message is a **pointer carrying ids only** — tenant id, document id, extraction-job id — and **never a storage path**. The worker derives the blob location from the document row inside the tenant scope it opened.
- [ ] AC-3 Work is claimed by a **conditional `UPDATE`**, never by broker de-duplication. Two deliveries of the same message do one unit of work.
- [ ] AC-4 The pipeline **replaces** a document's extracted facts rather than appending; a redelivery leaves exactly one set of facts, not two.
- [ ] AC-5 `MaxAttempts = 3` and `MaxAutoLockRenewalMinutes = 30` (matched to `lock_duration = PT5M`), strictly below the broker's `max_delivery_count = 8`, so the handler writes `Failed` **before** the broker dead-letters.
- [ ] AC-6 A delivery whose `ExtractionJob` row is not visible is handled by `DeliveryCount`: present ⇒ complete; absent and `< 2` ⇒ abandon; absent and `≥ 2` ⇒ dead-letter with reason `job-not-found`. The check runs **inside the message's own tenant scope** and consults no other tenant.
- [ ] AC-7 The transport is Service Bus when the **namespace** is configured and the existing in-process queue otherwise, chosen by one predicate; the host logs at startup which transport it selected **and the namespace**, so a missing role assignment is visible before the first send fails.
- [ ] AC-8 `IDocumentStorage` resolves in the Worker: the blob adapter moves out of `Raffa.Api` into a new **`Raffa.Storage`** project that both hosts reference.
- [ ] AC-9 `Azure.Identity` is legal in `Raffa.Worker` by a **package-scoped** amendment to `SdkAllowListTests`, and `Azure.AI.*` remains illegal there and in `Raffa.Api`.
- [ ] AC-10 `ConnectionStrings__Storage` is **fail-fast** in the Worker; every `ServiceBus__*` key binds **optionally**, so CI and local runs stay green with no namespace configured.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-async-processing-schema | the claim is a conditional `UPDATE` on columns that must already exist |

## Architecture decisions in force

- **ADR-027 §D1–§D5, §D10, §D12** and the round-2 (§C2–§C4, §C8) and round-3 (§C6, §C7, §C10, §C11) footers.
- **ADR-002** (w15 footers) — the queue port becomes real; **`Raffa.Storage`** is a provider adapter in the `Raffa.AiGateway` shape, **not** a domain module: no `DbContext`, no migration, no `.sql`, so `backend.yml` is untouched and w15's CI-YAML set stays **zero** (ADR-027 §C10).
- **ADR-009** — the worker opens its own tenant scope from the message; the GUC stays **parameter-bound** (ADR-009 w15 §5: an `oid` is a GUID and therefore *looks* safe to interpolate, which is exactly the reasoning that would remove the binding).
- **ADR-011** (w15 §2a–§2b) — managed identity + RBAC, **no Service Bus secret**, fully-qualified namespace as non-secret config.
- **ADR-005** (w15 clauses 9–10) — `max_delivery_count = 8`; the dead-letter queue is durable and swept by nothing.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Durable transport, `Raffa.Storage`, and a Worker that actually processes | L | phase-2 |

## Council decisions carried into this story

**Publish before commit.** `extraction_job` carries `FORCE ROW LEVEL SECURITY`
(`documents-contracts.sql:476-477`), so every poller *and* every sweeper is a
cross-tenant read ADR-009 forbids; a lost commit leaves a harmless phantom
message, and that is the trade the design takes deliberately.

**The limit is recorded rather than hidden** (ADR-027 §C6, round 3): D2's
"nothing is lost" is **withdrawn**. A commit that lands after the delivery
window is still stranded — visible in the dead-letter queue and recoverable
through the admin-driven re-enqueue, never by a cross-tenant sweep. **A
non-empty DLQ therefore means either "an upload's commit failed" or "a defect",
and the operator reads it with both meanings** (ADR-005 clause 10, ADR-016
clause 24).

**`MaxAttempts = 3`, and strictly-below is necessary but not sufficient.**
Deliveries and attempts do not advance together — a delivery is spent by any
failure at or before the claim, including §C6's abandons, plus scale-in eviction
under `min_replicas = 0` and the 30-minute renewal ceiling. `max_delivery_count`
was raised 5 → 8 to carry that slack; `MaxAttempts` stays 3 and `3 < 8` holds.

**The subscription is `document-processing`.** ADR-027 §D2's code block once
read `extraction-worker`; §C8 repaired it. One name, or the Terraform and the
consumer disagree about which subscription holds the messages — and a receiver
opened on a subscription Terraform never created fails exactly where a broken
worker still yields a **green** `backend.yml` run.

## Open questions

- **OQ-w15-sec-04** — resolved: identity and RBAC, no Service Bus secret. ADR-027 §D12's Key Vault wording is narrowed by ADR-011 w15 §2a–§2b; its other requirements (one subscription, dead-lettering, `max_delivery_count` strictly above `MaxAttempts`) stand.
- **OQ-w15-012(a)** — resolved: a topic with exactly one subscription **is** a work queue; competing receivers on one subscription share it. A second subscription would silently process every document twice.
