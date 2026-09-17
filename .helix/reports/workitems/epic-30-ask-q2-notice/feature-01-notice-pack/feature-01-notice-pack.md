---
id: feature-01
type: feature
parent: epic-30
wave: w19
status: active
---

# feature-01-notice-pack — Structured notice pack (NW-91 + NW-92)

## Slice
notice/preavviso/disdetta plan a structured pack (endDate/cancellationDeadline/
autoRenewal/renewalTermMonths + `WhenYouMustMove` + evidence) instead of Clause
RAG; the answer states the **date** (days only if the cited clause has N), never
model-subtract.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | notice-pack | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 22) — structured notice pack; RAG only as a contract-filtered fallback.

## Target repo
`raffa-backend`
