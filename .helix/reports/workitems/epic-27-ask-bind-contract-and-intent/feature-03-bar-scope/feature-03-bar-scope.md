---
id: feature-03
type: feature
parent: epic-27
wave: w19
status: active
---

# feature-03-bar-scope — Global Ask bar on 360 passes origin scope (NW-77)

## Slice
From `/contracts/:contractId`, the header and global-bar chips (and a typed
query) create the new conversation with `scopeContractId` = that id (reusing the
w18 `?scope=` parse), so the notice chip is scoped, not hard-coded "this
supplier".

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | bar-scope | w19 |

## Architecture decisions in force
- ADR-012 (cl. 49) — `navigate("/ask?scope=<id>")`, reusing w18 `AskRoute` parse; ADR-020 (37.1).

## Target repo
`raffa-web`
