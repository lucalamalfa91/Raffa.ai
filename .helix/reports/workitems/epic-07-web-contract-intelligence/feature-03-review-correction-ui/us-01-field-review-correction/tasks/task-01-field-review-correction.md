---
id: E07/F03/US01/T01
type: task
story: us-01-field-review-correction
wave: 7
status: live
target_repo: raffa-web
---

# task-01-field-review-correction — Field review + correction + evidence UI

## Coding objective
Implement the review queue with field decisions, confidence tags, evidence pane,
and gated "Mark as validated".

## Parent story AC covered
- AC-1 (4-col list), AC-2 (confidence mapping), AC-3 (evidence pane), AC-4 (gated CTA).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/contracts/:id/review/ | review UI |
| inputs/design/prototypes/screens.md | screen 6 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (6), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 6), ADR-019 (confidence thresholds §7.3; disabled CTA with reason).

## Definition of done
- [ ] Review surface works with gated CTA on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | confidence→tag mapping; CTA disabled until <80% resolved | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E07/F03/US01/T01
  prompt: reports/workitems/epic-07-web-contract-intelligence/feature-03-review-correction-ui/us-01-field-review-correction/tasks/task-01-field-review-correction.md
  produces: [web-review-ui]
  depends_on: [web-contract-360]
  effort: L
  layer: web
  status: live
```
