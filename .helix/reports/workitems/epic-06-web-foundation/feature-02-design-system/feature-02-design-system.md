---
id: feature-02
type: feature
parent: epic-06
wave: 6
status: active
---

# feature-02-design-system — Web design system

## Slice

Port the Claude Design Modernist design system into `web/` as the shared token
sheet and component catalogue, consuming `styles.css` tokens rather than forking
them (ADR-019).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Design system tokens + components | 6 |

## Architecture decisions in force

- ADR-019 (adopt export verbatim), ADR-012.

## Target repo

`raffa-web`
