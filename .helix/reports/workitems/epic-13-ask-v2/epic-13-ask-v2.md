---
id: epic-13
type: epic
wave: 13
status: active
supersedes: epic-12
---

# epic-13-ask-v2 — Ask Contigo V2: Documents intake, conversations, market feed, strategies, system-aware routing

## Business capability

Procurement drops one or more contracts into Contigo (anything that is not
a contract is refused and never stored), reviews the weak facts, and then
talks to Contigo — the home of the product — about **that contract against
the market**, about **how to approach its renewal**, and, in a new chat,
about **the whole portfolio** (most critical contracts, where to save, what
to improve). Every answer is articulated prose whose every claim cites a
validated contract page, a market record with provenance, or a Contigo
capability, and every answer routes the user to the right screen
(Contract 360, Renewals, Documents, Quote check, Savings). Nothing is
invented, nothing comes from the web, another tenant never appears.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/requirements.md` | Full requirements, HITL decisions D1–D8, API §6, data §7, acceptance §10 |
| `inputs/design/prototypes/Contigo V2 Prototype.html` (unpacked `contigo-v2/`) | Pixel + behaviour reference for every web task (`ia-v2.md`, `screens-v2.md`) |
| ADR-024 | Supersedes ADR-023: intake gate, conversations, market feed + index, engine contract, strategies, catalog, V2 IA, module map |
| ADR-001 / 004 / 011 / 018 / 020 amendments | Market feed as Internal Dataset; Foundry roles + no tools; two corpora + conversations under RLS; V2 IA; V2 screens |
| spec §4.1, §7, §8.3–§8.4, §9, §10, §12.1, §14.2, §15.3 | Types, statuses, evidence, renewals, benchmarks, negotiation, AI privacy, AI evaluation |

## Features

| ID | Title | Wave |
|----|-------|------|
| F01 | V2 foundation: solution scaffold + Foundry five-role gateway | 13 |
| F02 | Market intelligence: feed seam, mock, benchmark projection, market index | 13 |
| F03 | Supplier identity: entity, resolver, extraction, back-fill | 13 |
| F04 | Documents V2 API: admission gate, formats, list, load, reprocess, preview, delete | 13 |
| F05 | Conversations: store + API under RLS | 13 |
| F06 | Ask engine V2: gate, planner, pack, answer contract, guards, golden set | 13 |
| F07 | Insights: criticality, contract-level levers, strategy pack | 13 |
| F08 | Capability catalog and system-aware routing | 13 |
| F09 | Web V2: shell, rich reply, Documents V2, Ask V2 | 13 |
| F10 | Contract 360 citation landing + "Ask about it" | 13 |
| F11 | Integration: CI jobs, golden set in CI, e2e V2 path, demo acceptance | 13 |

## Success looks like

`inputs/requirements.md` §10 A1–A14 pass on `demo`: the V2 pilot path
(sign in → drop contracts, non-contracts refused → review → ask contract vs
market → renewal strategy → new chat portfolio strategy → capability
routing → citation landing in Contract 360 → Track it in Renewals) works
end to end with live Foundry, and the golden set shows zero numeric-guard
interventions.

## Architecture decisions in force

- ADR-024 — Ask Contigo V2 (supersedes ADR-023)
- ADR-001, 004, 011, 018, 020 — epic-13 amendment footers
- ADR-002, 003, 009, 017, 019, 021, 022 — unchanged and binding

## Out of scope

- Attachments inside the chat (D1); persisting rejected documents (D3)
- Legal advice; web / browsing / tool grounding in any model role
- Paid market API client as a `demo` dependency; cross-tenant RAG
- Full Contract 360 V2 answers-band layout (F10 follow-up after acceptance)
- Worker-queued upload (OQ-askv2-007); mobile
