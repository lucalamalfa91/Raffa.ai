---
id: E32/F02/US01/T01
type: task
story: us-01-candidate-set-2026
wave: w19
status: queued
target_repo: raffa-backend
---

# task-01-candidate-set-2026 — Active + validated impacting-2026 candidate set (NW-87)

## Coding objective
In `AskCopilotService`, replace the unfiltered `GetPortfolioAsync(..., None,
pageSize=100)` with the Q1 candidate query: validated (ADR-026 §D2
`CountValidatedContractsAsync` definition) **and** `Status = "Active"` **and**
option-3 "impacts 2026 costs" (paying in 2026 / driving spend; ended-2025 out,
2025-2028 term in, start-2026-06-01 in). Batch 360+benchmark resolution and
bound N×lines (raise `MaxPricedLinesForBenchmark` with a documented cap, or fail
visibly) — no unbounded N+1. Queued (head of W20).

## Parent story AC covered
- AC-1..AC-2.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | Q1 candidate-set query + batch/bound |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 18); lock 2 (option 3).
- **Do not touch**: `PortfolioQueryService` (reuse the filter).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0 (processing shell and ended-2025 never listed; 2025-2028 and start-2026-06-01 listed)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | option-3 candidate set | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none
