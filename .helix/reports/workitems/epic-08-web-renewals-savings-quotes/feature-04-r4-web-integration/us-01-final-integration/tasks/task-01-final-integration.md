---
id: E08/F04/US01/T01
type: task
story: us-01-final-integration
wave: 8
status: live
target_repo: raffa-web
---

# task-01-final-integration — Browser Day-1 walk on demo

## Coding objective
Wire and verify the full §20 Day-1 path in a browser on `demo` against the
prototype, recording the web-pass integration gate.

## Parent story AC covered
- AC-1 (full Day-1 walk), AC-2 (matches prototype), AC-3 (demo-v* promotion).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/e2e/day1.spec.ts | end-to-end Day-1 smoke |
| inputs/design/prototypes/day1-demo.html | reference (cite) |
| inputs/design/prototypes/ia.md | Day-1 path (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/day1-demo.html` + `ia.md` Day-1 path; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-016 (promotion demo-v*), ADR-018 (Day-1 path), ADR-020.

## Definition of done
- [ ] Browser Day-1 walk on `demo` passes (not `dotnet test`, not Swagger); smoking recorded.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| e2e | full §20 Day-1 flow in browser | workspace/raffa-web/e2e |

## Wave-spec entry
```yaml
- id: E08/F04/US01/T01
  prompt: reports/workitems/epic-08-web-renewals-savings-quotes/feature-04-r4-web-integration/us-01-final-integration/tasks/task-01-final-integration.md
  produces: [web-day1-integration]
  depends_on: [web-renewal-ui, web-savings-ui, web-quote-ui]
  effort: L
  layer: web
  status: live
```
