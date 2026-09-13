---
id: feature-02
type: feature
parent: epic-15
wave: w14
status: active
extends: epic-06 F04 (workspace members UI)
---

# feature-02-invitation-web — the Members screen tells the truth

## Slice

Extends **epic-06 F04**'s members screen — whose three acceptance criteria are
still exactly what the product wants (this is amended behaviour inside a
still-wanted story, not a replacement). The table becomes a server read, the
invite result renders the server's `mailDelivered` fact instead of claiming a
mail on any 201, the workspace-domain rule becomes a **non-blocking warning**,
and two destructive affordances appear: **revoke** an `Invited` row and
**remove** an `Active` one — different facts, different consequence copy.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The roster, the honest invite result, and revoke / remove | w14 |

## Architecture decisions in force

- ADR-020 (w14 design footer, screen 10) — the invite outcomes, the domain warning, the status set, the two destructive affordances, the last-Admin control, the email-primary row, the D8 role summaries, the Procurement variant
- ADR-019 (w14 footer) — `Active` · `Invited` · `Expired`; the disabled-CTA rule generalised; **inline, not dialog** confirmation (`--shadow-*` is "dialogs only", `:78`, and the locked catalogue has no dialog); one role vocabulary across two maps
- ADR-018 (w14 footers) — revoke-vs-remove on `/workspace/members`; `:112-119` binds the roster as a list surface (skeleton, error + Retry that **never** renders the last known roster)
- ADR-012 (w14 footer clauses 1, 4) — a client store never stands in for a missing GET; `X-Tenant-Id` is not sent on a route that already carries `{tenantId}`
- ADR-001 (w14 footer) — the workspace-domain restriction is **deferred** to the wave that lands ADR-010

## Target repo

`raffa-web` (this monorepo: `web/`)
