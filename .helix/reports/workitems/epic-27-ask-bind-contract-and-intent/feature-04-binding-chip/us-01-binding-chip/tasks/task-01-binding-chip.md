---
id: E27/F04/US01/T01
type: task
story: us-01-binding-chip
wave: w19
status: live
target_repo: raffa-web
---

# task-01-binding-chip — Persistent contract-binding chip (NW-78)

## Coding objective
In `web/src/routes/ask/index.tsx`, while the conversation's persisted
`scopeContractId` is set (already on the conversation detail wire), render a
`.tag-neutral` chip `{supplierName} · {type}` linking `/contracts/{id}` above the
thread. Rebuild it from the conversation detail's `scopeContractId` + the 360
header (`getContract360`), never from the transient `?scope=` query, so it
survives resume. Unrender it (or show a neutral pending state) until the
contract is resolvable. Cite `screens-v2.md` §2 (scope line) as the anchor.

## Parent story AC covered
- AC-1 chip renders `{supplierName} · {type}` linking 360.
- AC-2 survives resume from persisted id.
- AC-3 unrendered while unresolvable.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/ask/index.tsx | render the binding chip from the persisted scope |
| web/src/routes/ask/askViewModel.ts | a `buildBoundContractChip` helper + supplier/type fetch |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012 (cl. 49) / ADR-020 (37.2).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §2.
- **Do not touch**: `GlobalAskBar` (bar scope, feature-03); `?scope=` parse (reuse).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test that a scoped conversation renders the chip and it survives resume

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | chip renders + survives resume | `web/src/routes/ask/askViewModel.test.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E27/F04/US01/T01
  prompt: reports/workitems/epic-27-ask-bind-contract-and-intent/feature-04-binding-chip/us-01-binding-chip/tasks/task-01-binding-chip.md
  produces: [binding-chip]
  depends_on: [bar-scope]
  effort: S
  layer: frontend
  status: live
```
