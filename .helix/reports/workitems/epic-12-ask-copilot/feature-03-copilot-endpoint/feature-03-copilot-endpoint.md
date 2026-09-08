---
id: F03
type: feature
parent: epic-12
wave: 12
status: active
---

# feature-03-copilot-endpoint — Domain gate, context pack, chat API

## Slice

Remove “not wired”. Build a domain gate (greeting / off-topic / legal).
Assemble a context pack from tenant RAG + `IBenchmarkService` + existing
negotiation calculators. Foundry `answer` narrates; API returns markdown,
citations, actions.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Copilot chat contract | 12 |

## Architecture decisions in force

- ADR-023, ADR-004 amendment, ADR-011 amendment

## Target repo

`contigo-backend`
