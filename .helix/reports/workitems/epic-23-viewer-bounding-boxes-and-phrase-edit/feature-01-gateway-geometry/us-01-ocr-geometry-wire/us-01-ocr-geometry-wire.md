---
id: us-01
type: user-story
parent: feature-01
wave: w18
status: active
---

# us-01-ocr-geometry-wire — The gateway wire carries page geometry

## Story
As a **reviewer**, I want the OCR result to carry each cited phrase's pixel
box on its page, so that the viewer can draw a box over the exact wording
instead of only a text-level highlight.

## Acceptance criteria
- [ ] AC-1 `prebuilt-layout` is called alongside `prebuilt-read`, behind the AI Gateway, on the same account (no new role, no SKU).
- [ ] AC-2 `DocumentIntelligencePage` gains `Words`/`Polygon` geometry beside its existing `Spans`.
- [ ] AC-3 `AiOcrPage` carries the matching geometry so page + text + box travel together.
- [ ] AC-4 A page with no geometry (rasterised before this lands) still maps to `AiOcrPage` with a null box, never an error.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-017 w18 and ADR-002
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | builds on the existing `FoundryOcrClient` / wire types already on main |

## Architecture decisions in force
- ADR-017 w18 — `prebuilt-layout` supplies `words`/`polygon`; `prebuilt-read` stays the text path.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | ocr-geometry-wire | L | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Model called: `prebuilt-layout` (2024-11-30), in addition to `prebuilt-read`, both behind the AI Gateway on the existing account.
- Wire shapes: `DocumentIntelligencePage.Words`/`Polygon`; `AiOcrPage` grows geometry; the `ocr` role and Terraform binding are unchanged.

## Open questions
- none
