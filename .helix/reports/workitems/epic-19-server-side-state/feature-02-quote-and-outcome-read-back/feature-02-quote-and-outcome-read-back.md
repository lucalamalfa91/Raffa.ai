---
id: feature-02
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-05 F03, epic-08 F03
---

# feature-02-quote-and-outcome-read-back — The recorded outcome of the quote in front of you

## Slice

`backend/src/Raffa.Api/QuotesEndpointExtensions.cs:34-42` maps exactly three
routes and none of them reads a quote back. The data is already in Postgres and
already RLS-enabled — `quote` and `negotiation_outcome` — and the client store
`web/src/routes/quotes/quoteOutcomeStore.ts` that stands in for the missing read
is **write-only in production**: `loadNegotiationOutcomes` and
`sumRealizedSavings` have zero callers under `web/src`. So today **no surface
renders a recorded outcome at all**, not even in the same tab.

This feature adds `GET /api/quotes` (tenant-scoped list) and
`GET /api/quotes/{id}` **with its recorded negotiation outcomes embedded, newest
first**, backed by a new `QuoteQueryService`; then makes the quote screen render
the server's outcome and deletes the store. The cross-quote history, the levers
and an Ask-citable list stay **NW-57 (W18)**.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | quote-read-api | w16 |
| us-02 | quote-outcome-on-screen | w16 |

## Architecture decisions in force

- **ADR-028 §D2** — the two routes; outcomes hang off the **quote**; the
  assessment endpoint is **not** overloaded (`POST /{id}/assessment/recalculate`
  returns the same shape, so a recalculation would appear to re-report a
  negotiation it never touched); **no `GET /api/negotiations/outcomes`**; new
  `QuoteQueryService` that returns stored fields and computes nothing; **no
  migration**.
- **ADR-012** w16 clauses 21, 23–25 — the quote screen gains **one
  `GET /api/quotes/{id}` call on mount** alongside the existing recalculate;
  `getQuote` is wrapped, `GET /api/quotes` is **not**;
  `raffa.quotes.negotiationOutcomes` is deleted.
- **ADR-001** w16 clause 2 — W16 renders the recorded outcome of the quote in
  front of you and nothing more; **no new screen**; `E08/F03/US01` **AC-4 is
  re-pointed to NW-12 + NW-21, not cancelled** (no status banner, no
  `superseded:` line, the story stays `active`); the surface is **Savings**, not
  "Home", and what updates is the realized **count**.
- **ADR-018** `none` — `/quotes/:quoteId` already exists
  (`web/src/components/shell/WorkspaceShellApp.tsx:90`), so the quote id already
  survives a reload.

## Target repo

mixed — `raffa-backend` (us-01), `raffa-web` (us-02)
