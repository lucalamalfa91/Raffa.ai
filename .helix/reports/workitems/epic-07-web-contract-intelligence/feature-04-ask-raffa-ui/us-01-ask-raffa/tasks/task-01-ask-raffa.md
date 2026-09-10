---
id: E07/F04/US01/T01
type: task
story: us-01-ask-raffa
wave: 7
status: live
target_repo: raffa-web
---

# task-01-ask-raffa — Ask chat + citations + abstain UI

## Coding objective
Implement the Ask Raffa chat with route line, numbered citations, and abstain.

## Parent story AC covered
- AC-1 (chat + route line), AC-2 (citations), AC-3 (abstain), AC-4 (states).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-web/src/routes/ask/ | Ask UI |
| inputs/design/prototypes/screens.md | screen 7 (read, cite) |
| inputs/design/prototypes/day1-demo.html | reference (cite) |

## Context the implementer needs
- **Claude Design handoff**: `inputs/design/prototypes/screens.md` (7), `day1-demo.html`; prefer `/design-sync`.
- **Architecture decisions in force**: ADR-020 (screen 7), ADR-019 (abstain block), ADR-018 (global Ask, ⌘K).

## Definition of done
- [ ] Ask surfaces citations + abstain on `demo`; `npm run build` exits 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | citation chip opens Contract 360 Clauses; abstain renders accent block | workspace/raffa-web/tests |

## Wave-spec entry
```yaml
- id: E07/F04/US01/T01
  prompt: reports/workitems/epic-07-web-contract-intelligence/feature-04-ask-raffa-ui/us-01-ask-raffa/tasks/task-01-ask-raffa.md
  produces: [web-ask-ui]
  depends_on: [web-contract-360, web-app-shell]
  effort: L
  layer: web
  status: live
```
