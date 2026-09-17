---
id: feature-03
type: feature
parent: epic-29
wave: w19
status: active
---

# feature-03-renewals-select — Renewals honours `?select=` (NW-84)

## Slice
`renewals/index.tsx` reads `useSearchParams().get("select")` to select that row
(and its insight/TODO pane); an invalid/other-tenant id falls back to the top
row without a 500.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | renewals-select | w19 |

## Architecture decisions in force
- ADR-012 (cl. 51) — `?select=` selects the row; no 500.

## Target repo
`raffa-web`
