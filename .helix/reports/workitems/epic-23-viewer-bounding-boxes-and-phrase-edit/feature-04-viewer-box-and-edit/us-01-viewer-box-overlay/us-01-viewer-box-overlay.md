---
id: us-01
type: user-story
parent: feature-04
wave: w18
status: active
---

# us-01-viewer-box-overlay — Viewer draws the box; the edit persists and re-reads

## Story
As a **reviewer** opening a cited field, I want the viewer to draw a box on the
page over the cited phrase (not only a text highlight), and to edit the OCR
phrase from Review so that my correction persists and a second browser agrees
while the proposal stays distinguishable.

## Acceptance criteria
- [ ] AC-1 opening a cited field on a validated Northwind PDF shows the page **and** a box over the phrase.
- [ ] AC-2 the phrase-edit affordance lives in Review only (never draw→edit on the viewer), reusing the existing Review `PATCH` flow.
- [ ] AC-3 editing a phrase persists and a second browser reads the override back.
- [ ] AC-4 the proposal stays distinguishable from the override.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-029, ADR-012, ADR-018
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-03 (phrase-edit-write) | the edit writes through the feature-03 endpoint |
| feature-02 (evidence-geometry-schema) | the box comes from the widened evidence read |

## Architecture decisions in force
- ADR-029 clause 2 — box + wording ship together; box is DOM over `<img>`.
- ADR-012 — no new runtime dependency.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | viewer-box-and-edit | L | phase-4 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Overlay is absolutely-positioned DOM over the existing `<img>`; phrase edit is a Review-only affordance; clients re-read the server-persisted override (never a client store).

## Open questions
- none
