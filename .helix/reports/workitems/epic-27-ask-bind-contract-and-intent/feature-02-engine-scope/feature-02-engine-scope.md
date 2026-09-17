---
id: feature-02
type: feature
parent: epic-27
wave: w19
status: active
---

# feature-02-engine-scope — Engine consumes `scopeContractId` (NW-76)

## Slice
`AskAsync` takes the conversation's `ScopeContractId`; deixis resolves to it; a
scoped id wins over a same-name hit; an unseen id is a 404/N11 refusal. Q1
(portfolio market) still ranks the workspace portfolio even scoped (lock 4).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | engine-scope | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 12) — scope consumed in the engine; lock 4 exception for `PortfolioMarketPosition`.

## Target repo
`raffa-backend`
