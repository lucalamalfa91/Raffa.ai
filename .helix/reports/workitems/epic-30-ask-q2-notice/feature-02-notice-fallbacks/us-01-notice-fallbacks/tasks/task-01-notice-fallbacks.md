---
id: E30/F02/US01/T01
type: task
story: us-01-notice-fallbacks
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-notice-fallbacks — Five server-decided notice fallbacks (NW-94)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs` (notice pack composition), decide
the five notice outcomes server-side and emit the matching reply:
(1) deadline + evidence → `answer` with the deep-link (two-CTA ids, epic-28);
(2) deadline, no span → `answer` with the date + a 360 Review CTA (no fabricated
page); (3) no deadline, clause text only → quote the clause + Review recovery;
(4) neither → `abstain` naming this contract + a 360 Review action (NW-59
pattern); (5) unscoped deictic → `abstain` + Portfolio recovery. A scoped turn
never produces a "which supplier" abstain. Actions come from
`CapabilityRouting.ResolveActions` (360/Review/Portfolio), never model-authored.

## Parent story AC covered
- AC-1..AC-3 the five fallback cases.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | notice fallback decision + reply/action wiring |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 22); NW-59 recovery-action pattern.
- **Do not touch**: the pack (feature-01); the client (renders verbatim).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test per fallback case (and no scoped "which supplier" abstain)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | five fallback cases | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E30/F02/US01/T01
  prompt: reports/workitems/epic-30-ask-q2-notice/feature-02-notice-fallbacks/us-01-notice-fallbacks/tasks/task-01-notice-fallbacks.md
  produces: [notice-fallbacks]
  depends_on: [notice-pack, citation-two-cta]
  effort: M
  layer: backend
  status: live
```
