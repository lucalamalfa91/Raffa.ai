---
id: us-01
type: user-story
parent: feature-04
wave: 7
status: active
---

# us-01-ask-raffa — Ask chat + citations + abstain

## Story

As a **Procurement user**, I want to ask questions and get cited answers (or an
abstain), so I get reliable, evidence-backed answers (spec §8.3/§8.4).

## Acceptance criteria

- [ ] AC-1 Chat with route line ("Structured query…", "Clause retrieval…").
- [ ] AC-2 Numbered citation chips (doc · page · §) opening Contract 360 › Clauses.
- [ ] AC-3 Abstain block: "Cannot determine reliably" + reason (2px accent rule + accent-100).
- [ ] AC-4 States: empty, thinking (authorise→intent→retrieve), answered, abstain, unknown fallback.

## Definition of done

- [ ] Ask renders in browser on `demo`, matching `inputs/design/prototypes/screens.md` (7) + `day1-demo.html`.
- [ ] honours ADR-020 (screen 7), ADR-019 (abstain), ADR-018 (/ask, global Ask).

## Dependencies

| Depends on | Why |
|------------|-----|
| E02 Ask/citations API (assumed) | answers + citations |
| epic-06 shell (global Ask bar) | entry |

## Architecture decisions in force

- ADR-020 (screen 7), ADR-019 (abstain block), ADR-018 (global Ask).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Ask chat + citations + abstain UI | L | phase-11 |

## Council decisions carried into this story

Abstain is a first-class answer, not an error; citations open Contract 360 Clauses.
