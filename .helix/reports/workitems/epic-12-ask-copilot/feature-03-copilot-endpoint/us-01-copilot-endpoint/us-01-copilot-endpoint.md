---
id: us-01
type: user-story
parent: feature-03
wave: 12
status: active
---

# us-01-copilot-endpoint — Natural-language savings replies

## Story

As **procurement**, I want Raffa to answer in articulated language, compare
my contract to the fixture band, and refuse carbonara / lawsuits while
pointing me at a real deal in my workspace.

## Acceptance criteria

- [ ] AC-1 “ciao” / carbonara → decline + portfolio hook; no PDF dump; no RAG.
- [ ] AC-2 “Is my Allianz above market?” → in line / below / above from
      P25–P75 **or** insufficient data; provenance `fixture`.
- [ ] AC-3 “Can I sue?” → no legal advice; commercial redirect + deep link.
- [ ] AC-4 Opening / target / walk-away numbers match the calculator, not a
      model guess.
- [ ] AC-5 Structured renewals questions no longer return “not wired”.
- [ ] AC-6 JSON includes `answerMarkdown`, `citations[]`, `actions[{label,href}]`.

## Definition of done

- [ ] AC verified by named tests
- [ ] honours ADR-023

## Dependencies

| Depends on | Why |
|------------|-----|
| F01 | Foundry / fixture answer role |
| F02 | market catalog to narrate |

## Architecture decisions in force

- ADR-023 — copilot contract
- ADR-011 — two corpora, no RAG on off-domain

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Domain gate + context pack + endpoint | L | phase-2 |

## Open questions

- none
