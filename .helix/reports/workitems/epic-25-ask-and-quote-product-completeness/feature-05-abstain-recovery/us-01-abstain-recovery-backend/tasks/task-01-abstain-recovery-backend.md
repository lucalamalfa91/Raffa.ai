---
id: E25/F05/US01/T01
type: task
story: us-01-abstain-recovery-backend
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-abstain-recovery-backend — Every abstain reply carries a recovery action

## Coding objective
Emit a recovery action on every abstain/error path so Ask never dead-ends. In
`backend/src/Raffa.Chat/Application/Reply/CopilotReplyBuilder.cs`, the abstain
branch (`FromGuardedResult`, the `!CanDetermine` return) must carry a
non-empty `actions` list (a real catalog action from `CapabilityRouting`, not
model-authored) instead of `[]`; in `backend/src/Raffa.Api/AskCopilotService.cs`,
the two other abstain paths — the empty-pack abstain in
`BuildInDomainReplyAsync` (which already resolves `emptyActions` for one case)
and the composer-failure abstain (`composed.IsFailure`) — must also resolve and
attach a recovery action via `capabilityRouting.ResolveActions` (e.g. the
Documents upload action for an empty tenant, the "ask about dates/spend/clauses"
hint action otherwise). The `CopilotAction.Kind` stays `Navigate`/`Upload` as
today; the product-owner's "secondary" is a rendering concern (web), not a wire
change.

## Parent story AC covered
- AC-1 every abstain carries ≥1 recovery action.
- AC-2 action is a real catalog href, never model-authored.
- AC-3 gateway-failure and empty-pack abstains also carry it.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Chat/Application/Reply/CopilotReplyBuilder.cs | abstain branch carries a non-empty `actions` list |
| backend/src/Raffa.Api/AskCopilotService.cs | empty-pack + composer-failure abstains resolve a recovery action |

## Context the implementer needs

**Closes: NW-59**

- **Architecture decisions in force**: ADR-024 (reply kinds; actions from `CapabilityRouting.ResolveActions` only); ADR-020 (copy/actions).
- **Do not touch**: the `abstain`/`error` wire `kind` (unchanged); `RedirectReplyBuilder` (redirect/refusal already carry actions).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests` + `Raffa.Api.Tests` exit 0, with a test proving every abstain path's reply has a non-empty `actions` list

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | abstain reply carries a real catalog recovery action | `backend/tests/Raffa.Chat.Tests` |
| integration | empty-pack + failure abstains carry an action | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F05/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-05-abstain-recovery/us-01-abstain-recovery-backend/tasks/task-01-abstain-recovery-backend.md
  produces: [abstain-recovery-action]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
