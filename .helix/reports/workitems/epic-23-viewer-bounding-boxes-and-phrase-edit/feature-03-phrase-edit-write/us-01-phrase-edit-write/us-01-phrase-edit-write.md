---
id: us-01
type: user-story
parent: feature-03
wave: w18
status: active
---

# us-01-phrase-edit-write — Editing an OCR phrase persists an override, not a rewrite

## Story
As a **reviewer**, I want to correct an OCR phrase through a write that
persists to Postgres under RLS while keeping the model's proposal
distinguishable from my override, so that a reload (and a second browser) agrees
and a reprocess cannot silently revert my correction.

## Acceptance criteria
- [ ] AC-1 a phrase edit writes an override beside the proposal (both persisted, the proposal untouched).
- [ ] AC-2 the write is an authenticated, tenant-scoped endpoint under the same guard-clause shape as every other contract write.
- [ ] AC-3 a second browser / session reads the override back from Postgres.
- [ ] AC-4 a reprocess never silently reverts the override (ADR-027 fence).

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-029, ADR-027, ADR-003
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (evidence-geometry-schema) | the override slot lives on the entity feature-02 added |

## Architecture decisions in force
- ADR-029 clause 1 — override beside proposal; never mutate the proposal.
- ADR-027 — the write is a human correction the reprocess re-derivation must preserve.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | phrase-edit-write | M | phase-3 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- The write reuses the evidence-row override slot (ADR-003 w18 clause 3); the endpoint is contract-scoped (`/api/contracts/{id}/evidence/{fieldName}`), not document-scoped, because the phrase is per (contract, field) evidence.

## Open questions
- none
