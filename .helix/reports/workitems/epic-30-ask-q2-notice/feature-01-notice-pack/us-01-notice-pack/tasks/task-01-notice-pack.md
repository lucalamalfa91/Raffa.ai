---
id: E30/F01/US01/T01
type: task
story: us-01-notice-pack
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-notice-pack — Structured notice pack, date + days-if-cited (NW-91/NW-92)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs`, add a structured notice pack
builder for the `notice/preavviso/disdetta` intent (reached via the epic-27
planner reorder). Pack order: the scoped fact (`endDate`, `cancellationDeadline`,
`autoRenewal`, `renewalTermMonths`), `daysUntilNotice` computed host-side via
`RenewalEngine`/`IClock`, then `StrategyPackBuilder.BuildWhenYouMustMove`
explanation (miss / passed / no auto-renew), then an evidence item for the
`cancellationDeadline`/matching clause. Answer the **date**; "N days" only if N
is in the cited clause/span (never `EndDate − deadline`, never model arithmetic);
every calendar date is a `PackValueKind.Date`. `autoRenewal=false` → no notice
window / contract ends on `endDate`; a past deadline → "deadline passed N days
ago" with N a pack value. RAG is fallback only, filtered to the contract
(epic-28), else abstain honestly.

## Parent story AC covered
- AC-1 structured notice pack.
- AC-2 date + days-if-cited, never model-subtract.
- AC-3 fixture date / no-auto-renew / past-deadline cases.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | `BuildNoticePackAsync` + wire into `AskIntent.StructuredFact`/notice path |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 22); lock 8.
- **Do not touch**: `cancellationNoticeDays` (no column this wave); the ranker.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with tests for the fixture date, no-auto-renew, and past-deadline cases (all dates pack values)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | notice pack answers the date; days only if cited | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E30/F01/US01/T01
  prompt: reports/workitems/epic-30-ask-q2-notice/feature-01-notice-pack/us-01-notice-pack/tasks/task-01-notice-pack.md
  produces: [notice-pack]
  depends_on: [citation-ids, engine-scope]
  effort: M
  layer: backend
  status: live
```
