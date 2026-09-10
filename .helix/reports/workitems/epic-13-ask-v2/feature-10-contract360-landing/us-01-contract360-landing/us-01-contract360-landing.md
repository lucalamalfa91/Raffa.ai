---
id: us-01
type: user-story
parent: feature-10
wave: 13
status: active
---

# us-01-contract360-landing — A citation opens the proof, and the proof opens a chat

## Story

As **procurement**, I want to click a citation in Ask and land on the
exact clause in Contract 360 with its original wording highlighted, and
from Contract 360 to open a chat about that contract with one click, so
that every claim is one click from its proof and every proof is one click
from a question.

## Acceptance criteria

- [ ] AC-1 `/contracts/:id?clause=<clauseId>` (or `?page=<n>` for a
      page-level citation) opens Contract 360 with that clause highlighted
      and its original wording visible (evidence quote: before / **quote** /
      after when the clause has a span); the back label reads "Ask Raffa"
      when arriving from a chat.
- [ ] AC-2 Contract 360 offers **Ask about it**, navigating to
      `/ask?scope=<contractId>`; the chips shown there name the supplier
      ("When must we give notice to Salesforce?", "What is our liability
      cap with Salesforce?").
- [ ] AC-3 The supplier name (never a guid) appears in the header and in
      the "Ask about it" chips.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-web-v2 | rich reply components (T02) for the citation card styles reused on the evidence quote |

## Architecture decisions in force

- ADR-024 — citation landing, scoped conversations
- ADR-020 (amended) — screen 5 in `screens-v2.md`
- ADR-019 — tokens unchanged; spec §8.4 evidence

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Contract 360 citation landing (`?clause=` / `?page=` highlight, original wording) + "Ask about it" | M | phase-3 |

## Council decisions carried into this story

Design anchors: `raffa-v2/app.jsx` → `hl`, `citedOpened`, `back`,
`backLabels`, `c360Chips`, `clauses[].show`; `raffa-v2/markup.html`
"{{ hl.quote }}" evidence block and "← {{ backLabel }}"; `screens-v2.md` §5.
The full V2 answers-band / details / tracker layout is this feature's
follow-up after acceptance (`inputs/requirements.md` R-WEB-06, P2).

## Open questions

- none
