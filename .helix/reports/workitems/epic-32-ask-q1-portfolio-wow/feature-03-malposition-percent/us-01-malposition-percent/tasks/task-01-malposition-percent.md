---
id: E32/F03/US01/T01
type: task
story: us-01-malposition-percent
wave: w19
status: queued
target_repo: raffa-backend
---

# task-01-malposition-percent — Pure mal-position % calculator + actionability flag (NW-88)

## Coding objective
Add a named pure calculator (Appendix C rule 6) computing
`linePct = (unitPrice − media)/media × 100` where media = the adapter's P50, no
reuse of Renewals `DetermineMarketPosition` (unit-price band mismatch); list a
contract iff any `linePct > 0` or a worse-condition comparable fact; contract %
= spend-weighted average of positive linePcts; `HasSufficientData=false` → omit
the %; `actionableIn2026` + short why from stored facts/calculator dates, never
model invention. Queued (W20).

## Parent story AC covered
- AC-1..AC-3.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Insights/Application/… | mal-position % calculator |
| backend/src/Raffa.Api/AskCopilotService.cs | actionability flag + where-why |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 19); lock 1/3.
- **Do not touch**: Renewals `DetermineMarketPosition` (reuse forbidden).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Insights.Tests` exits 0 (thin sample omits %; NumericGuard: "18%" only if pack has 18)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | % formula + omit-on-thin + actionability | `backend/tests/Raffa.Insights.Tests` |

## Open questions blocking this task
- none
