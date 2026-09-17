---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-q3-route — The AsterCloud strategy question plans `RenewalStrategy`

## Story
As the **Ask engine**, I want the named-supplier negotiation question to plan
`RenewalStrategy` (not Clause RAG) with the right corpora, so `kind=answer`
ranked, never an abstain.

## Acceptance criteria
- [ ] AC-1 "sul contratto di AsterCloud GmbH quali sono i maggiori punti su cui posso contrattare…" → `RenewalStrategy` named AsterCloud.
- [ ] AC-2 pack corpora = calc + tenant (+ market + raffa Renewals item).
- [ ] AC-3 a missing deadline is a named point, not an abstain; `kind=answer`.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 23)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| epic-27 lexicon (NW-79) routed the intent; epic-28 priced-lines (NW-82) feed the pack |

## Architecture decisions in force
- ADR-024 w19 (cl. 23) — Q3 `RenewalStrategy`; corpora; missing deadline a point.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | q3-route | M | phase-3 |

## Council decisions carried into this story
- ranked `answer`, not abstain; corpra calc+tenant(+market+raffa Renewals).

## Open questions
- none
