---
id: us-01
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-01-async-processing-schema — The schema can hold a claim, an attempt count and a refusal

## Story

As the **platform**, I want the `extraction_job` row to record who claimed it,
when, and how many attempts it has had, and the `document` row to be able to say
it was refused, so that at-least-once delivery becomes exactly-once work and a
restart in the middle of a batch loses nothing.

## Acceptance criteria

- [ ] AC-1 `extraction_job` gains `attempt_count` (defaulted), `claimed_at` (nullable) and `claimed_by` (nullable); `document` gains the three nullable columns the async path needs. **No column is dropped, no type is changed, and no `NOT NULL` is added to an existing column.**
- [ ] AC-2 The previous API image runs unchanged against the new schema — the migration is purely additive, so a code revert needs no schema rollback.
- [ ] AC-3 `Rejected` joins `DocumentProcessingStatus` **with no DDL**: `processing_status` is `character varying(30)` with no CHECK and no default (`documents-contracts.sql:110`, its only occurrence).
- [ ] AC-4 A conditional `UPDATE` claim is expressible and proven: two concurrent claims of the same job leave exactly one winner, and the loser observes that it lost.
- [ ] AC-5 The regenerated `documents-contracts.sql` is idempotent and re-runnable, and `extraction_job` keeps `ENABLE`/`FORCE ROW LEVEL SECURITY` and its `tenant_isolation` policy (`:476-478`) untouched.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| — | Phase 1. It depends on nothing in this wave |

## Architecture decisions in force

- **ADR-027 §D1, §D6** — the `ExtractionJob` row **is** the work; the message is only a pointer. The rejection reason is persisted.
- **ADR-021** — `none`: no DDL is needed for the new status value, so `documents-contracts.sql` stays in both `backend.yml` arrays and neither moves.
- **ADR-009** — `FORCE ROW LEVEL SECURITY` on `extraction_job` is why no poller and no sweeper may exist; the schema must not be relaxed to make one possible.
- **ADR-003** — the migration regenerates one checked-in script, as epic-14 F01 established.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Additive migration: claim columns, attempt count, refusal record, `Rejected` | M | phase-1 |

## Council decisions carried into this story

From OQ-w15-012(b), answered by software-architect: *"The migration is purely
additive: three nullable columns on `document`, three on `extraction_job`
(`attempt_count` defaulted, `claimed_at`/`claimed_by` nullable), and `Rejected`
needs **no DDL at all**. No column is dropped, no type changed, no `NOT NULL`
added to an existing column — so the previous image runs unchanged against the
new schema, and rollback is a container revert with no schema rollback."*

Idempotency is a **conditional `UPDATE` claim, never broker dedup**
(ADR-027 §D1). The intake's claim that "a new document status value is a
migration" is **false on this schema** and was corrected at the table.

## Open questions

- **OQ-w15-004** — resolved at the table: the gate **splits** and a refused file becomes a terminal `Rejected` row. This story provides the column space; us-03 writes the row.
- **OQ-w15-012** — resolved: additive migration, rollback is a container revert.
