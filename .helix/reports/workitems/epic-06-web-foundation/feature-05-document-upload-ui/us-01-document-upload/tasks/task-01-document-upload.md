---
id: E06/F05/US01/T01
type: task
story: us-01-document-upload
wave: 6
status: live
target_repo: raffa-web
---

# task-01-document-upload — Upload + processing pipeline UI

## Coding objective
Implement the upload dropzone + 6-stage processing pipeline + result cards.

## Parent story AC covered
- AC-1 (dropzone + strip), AC-2 (pipeline), AC-3 (result cards).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/documents/ | upload UI |
| inputs/design/prototypes/screens.md | screen 3 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (3), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 3), ADR-019 (states).

## Definition of done
- [ ] Upload → processing renders on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | outcome cards by status | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E06/F05/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-05-document-upload-ui/us-01-document-upload/tasks/task-01-document-upload.md
  produces: [web-upload-ui]
  depends_on: [web-app-shell]
  effort: M
  layer: web
  status: live
```
