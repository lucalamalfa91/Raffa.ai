---
id: feature-02
type: feature
parent: epic-24
wave: w18
status: active
---

# feature-02-savings-filters — Savings opportunities list filters

## Slice
Add supplier / status / currency filters to the Savings opportunities list
(NW-25), as client-side restrictions of the already-loaded server data (the
council's pick) — never a client store standing in for a missing GET, and never
an invented category (Estimate is a sort, not a filter).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | savings-filters | w18 |

## Architecture decisions in force
- ADR-020 — filters are presentation; no client store for a missing GET; no invented category.

## Target repo
`raffa-web`
