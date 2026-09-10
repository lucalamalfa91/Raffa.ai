---
id: epic-12
type: epic
wave: 12
status: superseded
superseded_by: epic-13
---

> **Superseded (2026-09-08).** Replaced by `epic-13-ask-v2` (ADR-024, `inputs/requirements.md` D4). Never launched; slice `e12` is kept for the record only.

# epic-12-ask-copilot — Ask Raffa savings copilot

## Business capability

Procurement can talk to Raffa in natural language and learn whether a
contract is in line, below, or above the representative market band, and
where they can push in negotiation — without legal advice and without
dumping raw PDFs.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/ask-copilot-brief.md` | Full requirements |
| ADR-023 | Two corpora, Foundry answer role, rich reply |
| ADR-001 amendment | Fixture is the market corpus |
| ADR-004 amendment | Answer = copilot, not chunk echo |
| ADR-011 amendment | Tenant RAG ≠ market catalog |
| ADR-018 / ADR-020 amendments | `/ask` screen 7 |

## Features

| ID | Title | Wave |
|----|-------|------|
| F01 | Foundry AI Gateway | 12 |
| F02 | Expand fixture market catalog | 12 |
| F03 | Domain gate + context pack + chat API | 12 |
| F04 | Load + re-OCR / re-embed | 12 |
| F05 | Rich Ask UI | 12 |

## Success looks like

Brief acceptance A1–A6 on `demo` `/ask`.

## Architecture decisions in force

- ADR-023, plus amended ADR-001 / 004 / 011 / 018 / 020.

## Out of scope

- Paid Tropic/Vendr adapter
- Legal advice
- Cross-tenant RAG
- Parallel launch with e1011
