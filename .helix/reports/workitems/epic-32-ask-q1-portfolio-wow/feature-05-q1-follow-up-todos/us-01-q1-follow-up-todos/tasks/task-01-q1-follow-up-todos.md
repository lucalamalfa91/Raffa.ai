---
id: E32/F05/US01/T01
type: task
story: us-01-q1-follow-up-todos
wave: w19
status: queued
target_repo: raffa-backend
---

# task-01-q1-follow-up-todos — Q1 follow-up writes Renewals TODOs (NW-90)

## Coding objective
In `AskCopilotService`, handle the Q1 follow-up "su quali punti posso contrattare
sul supplier X": resolve X via NW-80 (multiple X spelled out, never merged);
build the benchmarked strategy + `NegotiationPointRanker` + upsert all points
(`persistTodos=true`) via the epic-29 host upsert; narrate the top 3 in chat;
inject `/renewals?select={contractId}`. A locked-for-2026 row gets no 2026-save
TODO (360 link only). Queued (W20).

## Parent story AC covered
- AC-1..AC-3.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | Q1 follow-up composition |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 21); lock 7/9.
- **Do not touch**: the ranker (epic-31); the TODO entity (epic-29).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0 (top-3 + persist-all + locked no TODO)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | Q1 follow-up persist + inject | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none
