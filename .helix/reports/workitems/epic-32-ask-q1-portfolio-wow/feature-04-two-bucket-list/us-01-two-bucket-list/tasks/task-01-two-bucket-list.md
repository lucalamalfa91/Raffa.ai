---
id: E32/F04/US01/T01
type: task
story: us-01-two-bucket-list
wave: w19
status: queued
target_repo: raffa-backend
---

# task-01-two-bucket-list — Two-bucket Q1 chat list + cards (NW-89)

## Coding objective
Compose the Q1 `answer` as two labelled groups in lock-2 order (actionable-in-
2026 → impacts-2026-but-locked), each row `{supplier} · {type}` with "X% above
media" when a band exists, a one-line why (including the actionability why), and
citation href `/contracts/{id}`; follow-up chips per actionable supplier; locked
rows get a 360 link only, no 2026-save TODO; price-in-line + worse-conditions
listed with "prezzo ok, condizioni migliorabili" (no invented condition %). The
client renders server facts verbatim, never recomputes %. Queued (W20).

## Parent story AC covered
- AC-1..AC-3.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | two-bucket Q1 pack + narration |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 20); lock 2/3/9.
- **Do not touch**: the calculator (feature-03); `ReplyBody`/`CitationCard` (reuse).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0 (locked row not narrated as a 2026 save)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | two-bucket narration | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none
