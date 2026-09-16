---
id: us-01
type: user-story
parent: feature-02
wave: w18
status: active
---

# us-01-evidence-geometry-schema — Evidence carries geometry and an override slot

## Story
As a **reviewer**, I want the evidence read-back to carry the box coordinates
for each extracted phrase (and the phrase-edit override beside the proposal),
so that the viewer can draw the box and a correction never blurs proposal vs
override.

## Acceptance criteria
- [ ] AC-1 `extraction_evidence` gains nullable geometry columns (word/polygon box) and a nullable override slot beside the proposal `Value`.
- [ ] AC-2 the change is one EF Core migration regenerating `Migrations/Scripts/documents-contracts.sql` (byte-compared, never hand-edited).
- [ ] AC-3 `GET /api/contracts/{id}/evidence` returns the box (null-able) beside `sourcePage`/`sourceSpan`/`passage`.
- [ ] AC-4 a row with a null box renders as text-level highlight only, never an error.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-003 w18 and ADR-029
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-01 (ocr-geometry-wire) | the geometry columns hold what the widened gateway wire produces |

## Architecture decisions in force
- ADR-003 w18 — geometry + override are nullable columns on the existing evidence table; `ContractEvidenceSchemaTests` gains the columns.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | evidence-geometry-and-override | L | phase-2 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Columns land on `extraction_evidence` (not a new table); nullable; `words`/`polygon` geometry normalized to `SourcePage`; the override slot is the ADR-029 phrase-edit provenance with the proposal untouched.

## Open questions
- none
