---
id: epic-07
type: epic
wave: 7
layer: web
status: active
---

# epic-07-web-contract-intelligence — Web contract intelligence (R1 UI)

## Business capability

Deliver the R1 user-visible ladder in the browser: the portfolio list with
filters and attention strip, the Contract 360 detail (header + 10 tabs), the
review/correction surface with evidence pane, and the Ask Raffa chat with
citations and abstain — so a procurement user can upload contracts and ask
reliable questions with source evidence (spec §16 R1, §20).

## Product coverage

| Source | Item |
|--------|------|
| spec §16 | R1 — Contract Intelligence (definition of success, in-browser) |
| spec §20 | reliable questions w/ source evidence; risks; benchmarks |
| spec §8.1–8.4 | portfolio, Contract 360, Ask, evidence |
| spec §7.3 | confidence thresholds (>95% accept, 80–95% flag, <80% review) |
| ADR-018 | routes /contracts, /contracts/:id, /contracts/:id/review, /ask |
| ADR-019 | semantic confidence/risk mapping |
| ADR-020 | screens 4, 5, 6, 7 |

## Features

| ID | Title | Wave |
|----|------|------|
| feature-01 | portfolio-ui | 7 |
| feature-02 | contract-360-ui | 7 |
| feature-03 | review-correction-ui | 7 |
| feature-04 | ask-raffa-ui | 7 |

## Success looks like

A reviewer browses a filtered portfolio, opens a Contract 360 with facts/AI
separation, corrects a <80% field with evidence, and asks a question that
returns numbered citations (and can abstain) — all in the browser on `demo`,
matching the prototype.

## Architecture decisions in force

- ADR-012, ADR-018, ADR-019, ADR-020. Consumes `inputs/design/prototypes/screens.md` (4–7) and `day1-demo.html`.

## Out of scope

- R2–R4 UIs (renewals, savings, quote check) — epic-08.
- Backend capability logic (extraction, retrieval, citations are E02 done); this epic authors the consuming screens only.
