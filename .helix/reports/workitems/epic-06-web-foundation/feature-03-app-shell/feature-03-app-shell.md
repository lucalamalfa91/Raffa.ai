---
id: feature-03
type: feature
parent: epic-06
wave: 6
status: active
---

# feature-03-app-shell — App shell (sign-in/workspace + nav + guards)

## Slice

Sign-in → workspace picker screen, the 224px left-rail navigation shell with
admin/procurement route guards, and the global Ask bar scaffold, wired to the
real `config.json`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Sign-in + workspace picker | 6 |
| us-02 | Left-rail shell + role guards + global Ask | 6 |

## Architecture decisions in force

- ADR-012, ADR-018 (route map, roles), ADR-019 (shell pattern).

## Target repo

`raffa-web`
