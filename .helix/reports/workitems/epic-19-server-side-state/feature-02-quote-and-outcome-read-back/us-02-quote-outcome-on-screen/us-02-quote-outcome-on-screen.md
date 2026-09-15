---
id: us-02
type: user-story
parent: feature-02
wave: w16
status: active
---

# us-02-quote-outcome-on-screen — The quote screen renders the server's outcome

## Story

As a **Procurement member**, I want the quote check screen to show the outcome I
recorded, read from the server, so that **it is still there after a reload, on
another device, and for a colleague**.

## Acceptance criteria

- [ ] AC-1 Opening `/quotes/:quoteId` calls `GET /api/quotes/{id}` on mount and
  renders the recorded outcome with its server-computed values.
- [ ] AC-2 Reloading the page, or opening the same URL in a second browser,
  shows the same outcome. Clearing `sessionStorage` changes nothing.
- [ ] AC-3 `web/src/routes/quotes/quoteOutcomeStore.ts` no longer exists and
  **no production module imports it**.
- [ ] AC-4 Recording an outcome writes **nothing** to `sessionStorage` /
  `localStorage`.
- [ ] AC-5 The screen's loading and error states follow ADR-018 `:112-119` — the
  error names the surface in plain language ("the quote"), never a raw path.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-012 w16 clauses 21, 23–25 and ADR-001 w16 clause 2
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E19/F02/US01/T01` | the route it calls |
| `E19/F06/US01/T01` | the `getQuote` wrapper and the regenerated `schema.ts` types; a store is deleted **with** its read-back, never before it |

## Architecture decisions in force

- **ADR-012** w16 clause 23 — **one `GET /api/quotes/{id}` call on mount
  alongside the existing recalculate**. The screen does **not** call
  `getQuoteAssessment` on mount or ever (`quotes/index.tsx:118-132` → `load()` →
  `apiClient.recalculateQuoteAssessment` at `:91`; `client.ts:805-813` records
  why), which is why the "ride the assessment GET" shape was declined.
- **ADR-012** w16 clause 21 — `raffa.quotes.negotiationOutcomes` is **deleted**,
  with its read-back and never before it.
- **ADR-001** w16 clause 2 — **no new screen**; `/quotes/:quoteId` already exists
  (`WorkspaceShellApp.tsx:90`) so **ADR-018 stays `none`**.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | quote-outcome-on-screen | S | phase-4 |

## Council decisions carried into this story

> **ADR-001 w16 clause 2, A16-6**: "on `dev`: upload a quote → record an outcome
> → reload **and** a second browser → the outcome renders with its
> server-computed values; clearing `sessionStorage` changes nothing; no
> production module imports `quoteOutcomeStore`."

> **ADR-001 w16 clause 2**: `E08/F03/US01` **AC-4 is re-pointed** to NW-12 +
> NW-21 as its honest server-side implementation — **not cancelled**. The story
> stays `active`, gets **no status banner and no `superseded:` line**. Two
> wording corrections on the record: its surface is **Savings**, not "Home", and
> what updates is the realized **count**, not a money tile.

## Open questions

- none.
