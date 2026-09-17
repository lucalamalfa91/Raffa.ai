---
id: feature-02
type: feature
parent: epic-30
wave: w19
status: active
---

# feature-02-notice-fallbacks — Five server-decided notice fallbacks (NW-94)

## Slice
The notice reply is one of five server-decided cases (deadline+evidence /
deadline-no-span / clause-only / neither / unscoped-deictic); a scoped turn never
asks "which supplier"; the client renders verbatim.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | notice-fallbacks | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 22) — five fallbacks, server-decided; never "which supplier" scoped.

## Target repo
`raffa-backend`
