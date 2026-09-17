---
id: epic-31
type: epic
wave: w19
status: active
extends: [epic-13, epic-29]
---

# epic-31-ask-q3-strategy — Strategy wow: grounded ranker + persist + deep-link

## Business capability
A named-supplier negotiation question ("sul contratto di AsterCloud GmbH quali
sono i maggiori punti su cui posso contrattare…") plans `RenewalStrategy`, ranks
only **grounded** points, narrates the top 3 in chat, and upserts **all** points
to the Renewals TODO list with a server-injected `/renewals?select=` action.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §5 | NW-95, NW-96, NW-97 |
| ADR-024 w19 | clause 23 (ranker); clause 21 (persist) |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | q3-route | w19 |
| feature-02 | point-ranker | w19 |
| feature-03 | q3-persist | w19 |
| feature-04 | w19-final-integration | w19 |

## Success looks like
Ask → top-3 grounded points → click Renewals → same contract with the full
TODO list; re-ask adds no duplicate Open and preserves Done.

## Architecture decisions in force
- ADR-024 w19 (cl. 21, 23) — grounded-only ranker, top-3 chat / persist-all, server-injected action.

## Out of scope
- No function-calling on `answer`; no ungrounded "7 lever" dump; Insights stays `[SharedKernel, Benchmark]`.
