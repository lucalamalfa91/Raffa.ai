---
id: E23/F04/US01/T01
type: task
story: us-01-viewer-box-overlay
wave: w18
status: live
target_repo: raffa-web
---

# task-01-viewer-box-and-edit — Draw the box and wire the phrase edit to the override

## Coding objective
In the V2 viewer (route `/documents/:documentId/viewer`, w17 task) draw the
bounding box as absolutely-positioned DOM over the existing `<img>`: read the
`box` (and `sourcePage`/`sourceSpan`/`passage`) from the widened
`GET /api/contracts/{id}/evidence` response (or the viewer's own evidence read)
and render one positioned `<div>` per cited phrase, scaled to the rendered
image; a `null` box renders the existing text-level `SourceSpan` highlight
only. Add the phrase-edit affordance to the Review surface (`?review=` state /
`EvidencePane`) — never on the viewer itself — writing through the
feature-03 `PATCH /api/contracts/{id}/evidence/{fieldName}` wrapper and
re-reading the server-persisted override so the proposal stays distinguishable
from the override. The box and its wording ("the box is drawn on the page over
the cited phrase") ship together in this task (ADR-029 clause 2). Cite the
design oracle `inputs/design/prototypes/raffa-v2/screens-v2.md:95-112` and
anchor the box on the viewer's existing page-`<img>` element.

## Parent story AC covered
- AC-1 page + box over the cited phrase.
- AC-2 phrase-edit affordance in Review only, reusing the Review flow.
- AC-3 edit persists, second browser agrees.
- AC-4 proposal stays distinguishable from override.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/documents/viewer/BoxOverlay.tsx | new: positioned box DOM over the page `<img>` |
| web/src/routes/documents/viewer/viewer.css | box overlay positioning styles |
| web/src/routes/contracts/contract360/EvidencePane.tsx | phrase-edit affordance (Review-only) + re-read |
| web/src/api/client.ts | call the `editEvidencePhrase` wrapper (feature-03) where Review saves |
| web/src/routes/contracts/contract360/reviewViewModel.ts | map override back into the review screen state |

## Context the implementer needs

**Closes: NW-63r**

- **Architecture decisions in force**: ADR-029 clause 2 (box + wording together; DOM over `<img>`, no new dependency); ADR-012 §3 (single-writer: this task is the client wrapper's only consumer this phase); ADR-018 (viewer route).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md:95-112` — the box overlay and phrase-edit affordance.
- **Do not touch**: the viewer's page rasterisation / routing (w17); the gateway/migration (backend phases).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test asserting a non-null `box` renders the overlay and a null `box` renders the text highlight
- [ ] a Story/Playwright assertion shows the box over the page `<img>` for a cited field

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | box overlay scales and renders; null box degrades to text highlight | `web/src/routes/documents/viewer/*.test.tsx` |
| unit | phrase edit calls the override endpoint and re-reads | `web/src/routes/contracts/contract360/*.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E23/F04/US01/T01
  prompt: reports/workitems/epic-23-viewer-bounding-boxes-and-phrase-edit/feature-04-viewer-box-and-edit/us-01-viewer-box-overlay/tasks/task-01-viewer-box-and-edit.md
  produces: [viewer-box-overlay]
  depends_on: [phrase-edit-endpoint, evidence-geometry-schema]
  effort: L
  layer: frontend
  status: live
```
