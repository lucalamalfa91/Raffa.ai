---
id: feature-04
type: feature
parent: epic-23
wave: w18
status: active
---

# feature-04-viewer-box-and-edit — Viewer draws the box; phrase edit re-reads the server

## Slice
The SPA draws the box overlay (absolutely-positioned DOM over the existing
`<img>`) for a cited phrase, and exposes the phrase edit in Review only —
never draw→edit on the viewer. On save it writes through the feature-03
endpoint and re-reads the server-persisted override, so a second browser agrees.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | viewer-box-overlay | w18 |

## Architecture decisions in force
- ADR-029 clause 2 — box and its wording ship in the same task (never copy first, box later).
- ADR-012 — overlay is DOM, no new runtime dependency; overlay + affordance are client-architect's / ux-ui-designer's.
- ADR-018 — the viewer route `/documents/:id/viewer?page&clause` (w17) hosts the box.

## Target repo
`raffa-web`
