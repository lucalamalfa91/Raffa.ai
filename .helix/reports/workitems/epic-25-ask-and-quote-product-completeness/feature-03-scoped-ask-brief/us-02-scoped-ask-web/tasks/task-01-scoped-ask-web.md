---
id: E25/F03/US02/T01
type: task
story: us-02-scoped-ask-web
wave: w18
status: live
target_repo: raffa-web
---

# task-01-scoped-ask-web — The Ask screen briefs the scoped contract

## Coding objective
In `web/src/routes/ask/index.tsx`, when `?scope=<contractId>` is set (already
parsed by `parseScopeContractId` and the supplier fetched via
`getContract360`), render the scoped brief instead of the generic `ASK_HELLO`:
a supplier kicker + scope line naming the contract, plus the
supplier-templated suggestion chips (`buildScopedSuggestions`, already in
`askViewModel.ts`) rather than the generic Ask chips. The `scopeContractId`
already reaches `createConversationAndAsk`; ensure the heading copy reflects
the briefed contract while the turn is being created. Cite the design oracle
`inputs/design/prototypes/raffa-v2/screens-v2.md:95-112` (§5) and anchor the
brief in the new-chat state.

## Parent story AC covered
- AC-1 heading shows supplier kicker + scope line.
- AC-2 scoped supplier-templated chips, not generic.
- AC-3 the `?scope=` greeting briefs the contract, not the generic hello.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/ask/index.tsx | render scoped brief (kicker + scope line + scoped chips) |
| web/src/routes/ask/askViewModel.ts | a `buildScopedBrief` helper (name + scope line) |

## Context the implementer needs

**Closes: NW-56**

- **Architecture decisions in force**: ADR-024 (scoped entry); ADR-020 (heading copy).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md:95-112`.
- **Do not touch**: `client.ts` / `raffa-api.v1.json` (no wire change); the backend scope threading (phase 2).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving a scoped `?scope=` renders the brief and scoped chips instead of the generic hello

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | scoped entry renders brief + scoped chips; not the generic hello | `web/src/routes/ask/askViewModel.test.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F03/US02/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-03-scoped-ask-brief/us-02-scoped-ask-web/tasks/task-01-scoped-ask-web.md
  produces: [scoped-ask-brief]
  depends_on: [scoped-ask-planner]
  effort: S
  layer: frontend
  status: live
```
