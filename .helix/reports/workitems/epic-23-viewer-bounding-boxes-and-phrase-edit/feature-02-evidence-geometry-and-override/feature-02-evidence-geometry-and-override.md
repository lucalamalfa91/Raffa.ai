---
id: feature-02
type: feature
parent: epic-23
wave: w18
status: active
---

# feature-02-evidence-geometry-and-override — Geometry + override columns and the box read

## Slice
Persist the box a phrase occupies as nullable geometry columns (and the
phrase-edit override beside the proposal) on the existing `extraction_evidence`
table in one migration, and widen the already-shipped
`GET /api/contracts/{id}/evidence` read to carry the box so the viewer can draw
on the page. A null box means "text-level highlight only" (w17's state), never
an error.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | evidence-geometry-schema | w18 |

## Architecture decisions in force
- ADR-003 w18 — geometry + override land as nullable columns on `extraction_evidence`; one migration, single writer.
- ADR-029 — the box overlay must not be made before it is real; null box = text-level highlight.

## Target repo
`raffa-backend`
