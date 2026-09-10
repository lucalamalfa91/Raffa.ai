---
id: F06
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-06-ask-engine — Domain gate, planner, context pack, answer contract, guards, golden set

## Slice

The copilot itself. Authorization first; a domain gate (`greeting`,
`off_domain`, `legal`, `capability`, `needs_document`, `in_domain`) that
never retrieves off-domain; a deterministic planner over fixed intents
(`structured_fact`, `clause`, `market_compare`, `renewal_strategy`,
`portfolio_strategy`, `savings`, `document_status`, `quote_route`,
`navigate`); a context pack assembled in `Raffa.Api` from the three
sources (validated contracts, market feed, capability catalog) plus the
Insights calculators; the Foundry `answer` role with a versioned persona
prompt and structured JSON, **no tools**; grounding + numeric + action
guards; the reply contract of `inputs/requirements.md` §6 behind
`POST /api/conversations/{id}/messages`; audit per turn. Then the golden
set (`Raffa.AiEval`, ≥ 40 questions, 0 numeric-guard interventions) that
keeps it honest (R-ASK-01…10, R-CMP, R-STR, R-PORT narration, R-EVD-03).
Absorbs e12 F03.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Raffa answers as a savings specialist, cites, routes, or abstains | 13 |

## Architecture decisions in force

- ADR-024 — engine pipeline, reply contract, guards, no tools
- ADR-004 (amended) — structured `answer` output, `classify` for the gate
- ADR-011 (amended) — authz before retrieval, off-domain retrieves nothing, hash-only logs
- ADR-002 — `Raffa.Chat` stays `[SharedKernel, AiGateway]`; composition in `Raffa.Api`
- spec §8.3–§8.4, §10.4, §12.1, §15.3, Appendix C rules 2, 6, 10

## Target repo

`raffa-backend`
