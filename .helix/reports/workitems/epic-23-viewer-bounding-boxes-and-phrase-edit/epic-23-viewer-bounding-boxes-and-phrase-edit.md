---
id: epic-23
type: epic
wave: w18
status: active
extends: [epic-22]
---

# epic-23-viewer-bounding-boxes-and-phrase-edit — Bounding-box overlay + editable OCR phrases

## Business capability
Complete the document viewer the w17 wave officialized: a reviewer opening a
cited field on a validated Northwind PDF sees the page **and** a pixel-exact
box over the cited phrase (not only w17's text-level `SourceSpan` highlight),
and can correct an OCR phrase through a write that persists to Postgres under
RLS while keeping the model's proposal distinguishable from the human override.
This is the NW-63 remainder the ADR-029 split pre-decided; it is not re-litigated.

## Product coverage
| Source | Item |
|--------|------|
| ADR-029 | NW-63 split; phrase-edit provenance + box-overlay screen promise |
| ADR-003 w18 | geometry + override columns on the evidence tables |
| ADR-017 w18 | `prebuilt-layout` called, widened `words`/`polygon` wire, `AiOcrPage` geometry |
| ADR-027 | reprocess never silently reverts a human correction |
| screens-v2.md:95-112 | viewer box overlay / phrase-edit affordance |
| inputs/next/w18-todo.md §1 | NW-63 remainder (must) |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | gateway-geometry | w18 |
| feature-02 | evidence-geometry-and-override | w18 |
| feature-03 | phrase-edit-write | w18 |
| feature-04 | viewer-box-and-edit | w18 |
| feature-05 | w18-final-integration | w18 |

## Success looks like
On a validated Northwind PDF: the viewer draws a box on the page **and** shows
the text; an OCR phrase edit persists (reload and a second browser agree) and
the proposal is still distinguishable from the override. A document rasterised
before this lands still renders with text-level highlight (null box), never an
error.

## Architecture decisions in force
- ADR-029 — box overlay ships with its copy in one task (never copy first, box later); phrase edit is an override, never in-place.
- ADR-003 — geometry + override land as nullable columns on the existing `extraction_evidence`; one migration regenerating `documents-contracts.sql`, single writer.
- ADR-017 — `prebuilt-layout` supplies `words`/`polygon`; `prebuilt-read` stays the text path; no new SKU/role.
- ADR-027 — a reprocess never silently re-auto-accepts a field a human decided.

## Out of scope
- No new runtime dependency in the SPA (ADR-012): overlay is absolutely-positioned DOM over the existing `<img>`.
- No new gateway role or SKU (ADR-017/ADR-008; cloud-architect `none`).
- No phrase-edit that mutates the proposal in place (ADR-029 clause 1).
