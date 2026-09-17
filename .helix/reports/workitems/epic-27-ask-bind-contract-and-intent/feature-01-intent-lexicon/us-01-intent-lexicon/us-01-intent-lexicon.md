---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-intent-lexicon — The three screenshot sentences route to their target intents

## Story
As the **Ask engine**, I want the planner to route the three demo phrasings
(IT+EN) to their target intents, so the wow answers are reached without the
model recovering a missed intent.

## Acceptance criteria
- [ ] AC-1 Q1 ("quali contratti sono mal posizionati … risparmiare sull'anno 2026") → `PortfolioMarketPosition`, never `QuoteRoute`.
- [ ] AC-2 Q2 notice/preavviso/disdetta → structured notice pack, never unscoped Clause RAG.
- [ ] AC-3 Q3 "sul contratto di AsterCloud … quali punti su cui contrattare nel prossimo rinnovo" → `RenewalStrategy` named supplier.

## Definition of done
- [ ] every AC above verified by `IntentPlannerTests` rows named in a task
- [ ] honours ADR-024 w19 (cl. 13); no fallthrough to unscoped Clause RAG when facts exist
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | pure planner change on existing `IntentPlanner`/`AskIntent` |

## Architecture decisions in force
- ADR-024 w19 (cl. 13) — new `PortfolioMarketPosition`; `risparm*` before `mercato`; `contrattare`/`rinnovo` hit `RenewalStrategyPattern`.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | intent-lexicon | M | phase-1 |

## Council decisions carried into this story
- R-SYS-02 narrowed (lock 6): Quote check stays the new-market-proposal handler; Q1 is Ask.

## Open questions
- none
