---
id: E27/F03/US01/T01
type: task
story: us-01-bar-scope
wave: w19
status: live
target_repo: raffa-web
---

# task-01-bar-scope — 360 global bar passes origin scope (NW-77)

## Coding objective
In `web/src/components/ask-bar/GlobalAskBar.tsx`, `submit` currently
`navigate("/ask", { state: { query, newChat: true } })`. When the current route
is `/contracts/:contractId` (read via `useLocation`/`useParams`), navigate
`/ask?scope=<contractId>` with the query in state so `AskRoute`'s
`parseScopeContractId` (w18) creates the scoped conversation. In
`web/src/components/ask-bar/askSuggestions.ts`, source the notice chip's supplier
name from the open contract (the same `getContract360` supplier read
`askViewModel.buildScopedSuggestions` already does), so it is the real name, not
the hard-coded "this supplier", when a contract is open; Portfolio/Ask-home
chips stay unscoped. Cite the IA anchor `ia-v2.md` (Ask bar + `/contracts/:id`).

## Parent story AC covered
- AC-1 bar navigates `/ask?scope=<id>`.
- AC-2 notice chip uses the open contract's supplier name.
- AC-3 Portfolio/Ask-home chips unscoped.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/components/ask-bar/GlobalAskBar.tsx | navigate `/ask?scope=<id>` on `/contracts/:id` |
| web/src/components/ask-bar/askSuggestions.ts | scoped supplier name for the c360 notice chip |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012 (cl. 49) / ADR-020 (37.1).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/ia-v2.md` route map.
- **Do not touch**: `AskRoute` scope parse (w18, reuse); the binding chip (feature-04).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test that the bar on `/contracts/:id` navigates `?scope=<id>` and the notice chip carries the contract's supplier

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | scoped navigation + chip supplier name | `web/src/components/ask-bar/*.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E27/F03/US01/T01
  prompt: reports/workitems/epic-27-ask-bind-contract-and-intent/feature-03-bar-scope/us-01-bar-scope/tasks/task-01-bar-scope.md
  produces: [bar-scope]
  depends_on: [engine-scope]
  effort: S
  layer: frontend
  status: live
```
