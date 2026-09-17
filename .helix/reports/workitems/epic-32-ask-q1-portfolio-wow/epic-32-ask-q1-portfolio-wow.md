---
id: epic-32
type: epic
wave: w19
status: active
extends: [epic-13, epic-25]
---

# epic-32-ask-q1-portfolio-wow — Portfolio mal-position / 2026 savings (Q1)

## Business capability
The unscoped (and scoped) "quali contratti sono mal posizionati / dove posso
risparmiare nel 2026" question is answered in Ask — never routed to Quote check
(which stays the *new market proposal* handler). It ranks the
Active + validated contracts impacting 2026 costs, computes a pure
mal-position % vs the representative market average, and narrates two honest
buckets (actionable-in-2026 vs locked-for-2026).

**This is the heaviest flow and overflows to the head of W20** (the 20-task
cap physically binds and the raw's own build sequence places Q1 last). It is
**not demoted**: every item stays `must` and is decomposed here with
`status: queued`.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §3 | NW-86, NW-87, NW-88, NW-89, NW-90 |
| ADR-024 w19 | clauses 13 (intent), 18 (candidate set), 19 (calc), 20 (buckets), 21 (follow-up) |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | portfolio-market-intent | w20 |
| feature-02 | candidate-set-2026 | w20 |
| feature-03 | malposition-percent | w20 |
| feature-04 | two-bucket-list | w20 |
| feature-05 | q1-follow-up-todos | w20 |

## Success looks like
The screenshot Q1 lists above-average (+ worse-conditions) Active-2026
contracts, split into two honest buckets with a % where a band exists, citing
`/contracts/{id}` — never a "That's a job for Quote check" routing.

## Architecture decisions in force
- ADR-024 w19 (cl. 13, 18–21); lock 1/2/3/4/6/7/9.

## Out of scope
- Quote check remains (new market proposal only); no invented %, day count, or 2026 save; no paid market API.
