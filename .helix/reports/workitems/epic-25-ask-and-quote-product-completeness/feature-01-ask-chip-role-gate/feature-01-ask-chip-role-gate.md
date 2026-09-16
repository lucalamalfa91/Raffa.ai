---
id: feature-01
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-01-ask-chip-role-gate — Hide admin-gated Ask chips from a non-Admin (NW-74)

## Slice
Thread the server-derived `role` (already at `AppShell.tsx:40` and passed to
`RailNav`, not to `GlobalAskBar`) into the two chip selectors, so an
admin-gated capability's chip is dropped for a non-Admin. Presentation, never a
security fix: `GET /api/capabilities` stays un-gated and identical for both
roles.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | ask-chip-role-gate | w18 |

## Architecture decisions in force
- ADR-022 S16-11 + ADR-012 w17 cl 40 — chip hide is presentation; catalog un-gated.

## Target repo
`raffa-web`
