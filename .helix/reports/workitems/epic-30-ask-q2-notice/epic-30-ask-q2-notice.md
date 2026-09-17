---
id: epic-30
type: epic
wave: w19
status: active
extends: [epic-13, epic-07 F02]
---

# epic-30-ask-q2-notice — Notice wow: structured pack + honest fallbacks

## Business capability
A scoped notice question ("When must we give notice to this supplier?") is
answered from a structured pack (endDate / cancellationDeadline / autoRenewal /
renewalTermMonths + `WhenYouMustMove` + evidence), never tenant-wide Clause RAG;
the answer states the calendar **date** (days only if the clause cites N), and
the five server-decided fallbacks never ask "which supplier" when scoped.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §4 | NW-91, NW-92, NW-94 (NW-93's card is epic-28 F03) |
| ADR-024 w19 | clause 22 (notice pack + fallbacks) |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | notice-pack | w19 |
| feature-02 | notice-fallbacks | w19 |

## Success looks like
The scoped notice question answers "18 Oct 2026" (+ "if missed, renews 12
months") with pack-backed dates; no invented day count; a scoped turn never
asks which supplier.

## Architecture decisions in force
- ADR-024 w19 (cl. 22) — structured notice pack; RAG only as a contract-filtered fallback; date, never model-subtract.

## Out of scope
- No `cancellationNoticeDays` column (R5a → W20); V1 = non-renewal/auto-renew notice regime only.
