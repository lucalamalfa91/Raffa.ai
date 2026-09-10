---
id: us-01
type: user-story
parent: feature-06
wave: 13
status: active
---

# us-01-ask-engine — Raffa answers as a savings specialist, cites, routes, or abstains

## Story

As **procurement**, I want to ask Raffa in my own words about a contract,
its renewal, or my whole portfolio and get an articulated answer whose
every claim cites a validated contract page, a market record with
provenance, or a Raffa feature — with a button to the right screen —
and a warm redirect when I greet it, ask about carbonara, name a supplier
I have not uploaded, or ask for legal advice; so that I trust every number
and never wonder whether it came from the web.

## Acceptance criteria

- [ ] AC-1 "ciao" and "ricetta della carbonara" → kind `redirect`, warm
      decline naming a real contract or saving of this tenant (or an upload
      invite when empty), **zero retrieval calls** (asserted on recording
      fakes).
- [ ] AC-2 "posso fare causa a Salesforce?" → kind `refusal`, no legal
      advice, the commercial analogue, an action to Contract 360.
- [ ] AC-3 "Which contracts renew in the next 120 days?" → structured facts
      from the tenant store (no "not wired"), cited per contract, action to
      Renewals.
- [ ] AC-4 "Is my Allianz contract above market?" → below / in line / above
      per priced line from P25–P75 **or** insufficient data; citations to
      the contract page and to the market record with the *representative
      · mock feed · updated* label.
- [ ] AC-5 "How should I approach the Salesforce renewal?" → When you must
      move → Where you can push → Targets → Next steps; dates equal the
      renewal engine, opening / range / walk-away equal the calculator to
      the cent; actions to Contract 360 and Renewals.
- [ ] AC-6 New chat "Which contracts are most critical and where can we
      save?" → top-5 with component explanations, totals equal calculator
      sums, one action per row; empty portfolio → upload invite.
- [ ] AC-7 A fake model answering "P50 is CHF 140" when the pack holds 132,
      citing a key not in the pack, or emitting an href not in the catalog
      → regenerated once, then downgraded to `abstain` with the pack's
      facts; audit records `abstainGuardIntervened=true`.
- [ ] AC-8 `POST /api/conversations/{id}/messages` returns the reply
      contract of `inputs/requirements.md` §6 (`kind`, `answerMarkdown`,
      `citations[]` with corpus / title / subtitle / snippet / page /
      section / href / previewUrl / recordId, `actions[]`, `provenance`,
      `followUps[]`); no guid, no route line; the Foundry request carries
      no tools / grounding.
- [ ] AC-9 Another tenant's contracts never appear in evidence (existing
      isolation test extended to the messages endpoint); golden set of
      ≥ 40 questions × 3 tenant fixtures passes with 0 numeric-guard
      interventions on the fixture gateway.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-v2-foundation | Foundry `answer` / `classify` roles |
| us-01-market-intelligence | benchmark bands + market notes retrieval (T01) |
| us-01-supplier-identity | supplier name resolution in questions |
| us-01-documents-v2 | page-aware chunks, document list for status intents |
| us-01-conversations | store + API the messages endpoint extends |
| us-01-insights | calculators for strategy / criticality packs |
| us-01-capability-catalog | actions and feature cards |

## Architecture decisions in force

- ADR-024 — engine pipeline, reply contract, guards, no tools
- ADR-004 (amended) — structured `answer`, `classify` for the gate
- ADR-011 (amended) — authz before retrieval; off-domain retrieves nothing; hash-only audit
- ADR-002 — `Raffa.Chat` allow-list `[SharedKernel, AiGateway]`; pack composition in `Raffa.Api`
- spec §8.3–§8.4, §10.4, §12.1, §15.3; Appendix C rules 2, 6, 10

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Domain gate, planner, context pack, answer contract, guards, messages endpoint, host wiring | L | phase-3 |
| T02 | AI golden set (`Raffa.AiEval`) on the fixture gateway | M | phase-4 |

## Council decisions carried into this story

Gate labels `greeting`, `off_domain`, `legal`, `capability`,
`needs_document`, `in_domain`; intents `structured_fact`, `clause`,
`market_compare`, `renewal_strategy`, `portfolio_strategy`, `savings`,
`document_status`, `quote_route`, `navigate`. Prototype oracle for the
intent outcomes: `inputs/design/prototypes/raffa-v2/app.jsx` → `ask()`
and `ia-v2.md` "Ask intents". Persona prompt versioned as
`backend/src/Raffa.Chat/Prompts/answer/v2.1.md` (version logged).
Temperature ≤ 0.2. Pack token budget `Chat:PackTokenBudget`. Reply kinds
`answer` / `abstain` / `redirect` / `refusal`. Audit actions
`chat.answered`, `chat.redirected`, `chat.refused`, `chat.abstained`.

## Open questions

- OQ-askv2-006 — answer language follows the question (assumed)
- OQ-askv2-009 — live Foundry for acceptance; fixture in CI (assumed)
