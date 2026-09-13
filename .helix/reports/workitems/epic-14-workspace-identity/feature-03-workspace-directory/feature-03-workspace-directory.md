---
id: feature-03
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-01 F05 (identity-workspace), epic-06 F03 (sign-in / workspace picker)
---

# feature-03-workspace-directory — the list of your workspaces is a server answer

## Slice

Extends **epic-01 F05** with `GET /api/workspaces` — the endpoint the SPA has
been substituting with a `localStorage` array under
`raffa.signin.knownWorkspaces.{homeAccountId}`
(`web/src/routes/signin/workspaceStore.ts:32,88-106`, whose own header comment
states the gap) — and **epic-06 F03**'s sign-in screen with the resolution rule
that consumes it: empty → create form; exactly one membership → **enter it, no
picker**; several → picker. The row carries the real validated-contract count
(NW-09), the real server role (NW-14's visible half) and the workspace's own
country and currency (NW-24), so screen 1 stops asserting three facts the system
does not hold.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | `GET /api/workspaces` for the signed-in identity | w14 |
| us-02 | Sign-in resolves the workspace from the server, and hosts the public accept route | w14 |

## Architecture decisions in force

- ADR-026 §D1 (two-phase discovery, no tenant input, 200 + `[]` never 404), §D2 (the count definition already in code), §D5 (the create body)
- ADR-025 §F.1 (discovery is gated on a live membership row, capped at 50), §F.2, §F.3 (the multi-scope exception and its four bounds)
- ADR-009 (w14 footer clauses 1–5)
- ADR-003 (w14 footer) + ADR-020 (w14 design footer, screen 1) — `industry` / `country`, derived `currency`, the pick-row segment contract
- ADR-012 (w14 footer clauses 1, 2, 5, 7) — a client store never stands in for a missing GET; the session hint and its resolution order; the client role is presentation-only and defaults to least privilege; a count has one definition and it is the server's
- ADR-018 (w14 route footer) — the `BrowserRouter` hoist and the public `/invite/accept` route; ADR-018 `:112-119` binds every list surface to skeleton + error/Retry

## Target repo

mixed — us-01 is `raffa-backend` (`backend/`, plus the OpenAPI/generated client
under `web/`), us-02 is `raffa-web` (`web/`)
