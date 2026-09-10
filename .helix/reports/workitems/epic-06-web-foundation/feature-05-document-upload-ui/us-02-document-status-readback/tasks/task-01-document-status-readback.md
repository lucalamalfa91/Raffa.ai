---
id: E06/F05/US02/T01
type: task
story: us-02-document-status-readback
wave: 6
status: live
target_repo: raffa-web
---

# task-01-document-status-readback — Document status read-back UI

## Coding objective
Implement the document table with status read-back and Contract 360 cross-link.

## Parent story AC covered
- AC-1 (table), AC-2 (status tags), AC-3 (row cross-link).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/documents/ | status table |
| inputs/design/prototypes/screens.md | screen 3 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (3), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 3), ADR-019 (status tag mapping: needs_review=outline, failed=accent).

## Definition of done
- [ ] Status table renders with correct tags; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | status→tag semantic mapping | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E06/F05/US02/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-05-document-upload-ui/us-02-document-status-readback/tasks/task-01-document-status-readback.md
  produces: [web-document-status]
  depends_on: [web-upload-ui]
  effort: S
  layer: web
  status: live
```
