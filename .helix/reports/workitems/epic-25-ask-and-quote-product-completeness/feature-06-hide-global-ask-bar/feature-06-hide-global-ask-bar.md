---
id: feature-06
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-06-hide-global-ask-bar — Hide the global Ask bar on Ask screens (NW-60)

## Slice
Suppress the global Ask bar on `/ask` and `/ask/:id` (where it duplicates the
composer), keep it everywhere else, and make ⌘K focus the composer. A
route-scoped suppression in `AppShell`.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | hide-global-ask-bar | w18 |

## Architecture decisions in force
- ADR-018 — the `/ask` route owns its composer; ADR-020 — no duplicate bar.

## Target repo
`raffa-web`
