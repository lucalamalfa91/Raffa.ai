---
id: E19/F02/US02/T01
type: task
story: us-02-quote-outcome-on-screen
wave: w16
status: live
target_repo: raffa-web
---

# task-01-quote-outcome-on-screen — Render the server's negotiation outcome; delete the write-only store

## Coding objective

`web/src/routes/quotes/quoteOutcomeStore.ts` (key
`raffa.quotes.negotiationOutcomes`) is **write-only in production**:
`web/src/routes/quotes/index.tsx:30` imports only `rememberNegotiationOutcome`
and calls it once at `:290`, while `loadNegotiationOutcomes` and
`sumRealizedSavings` have **zero callers under `web/src`**. So today no surface
renders a recorded outcome at all, not even in the same tab.

Add **one `apiClient.getQuote(workspace.id, quoteId)` call on mount** in
`web/src/routes/quotes/index.tsx`, alongside the existing `load()` →
`recalculateQuoteAssessment` (`:91`) inside the route effect at `:118-132`, and
render the recorded outcomes the server returns. Then delete the store and its
test, and remove the `rememberNegotiationOutcome` call at `:290` so recording an
outcome writes **nothing** to `sessionStorage`.

Give the outcome panel the ADR-018 loading and error states.

## Parent story AC covered

- AC-1 `/quotes/:quoteId` calls `GET /api/quotes/{id}` on mount and renders the recorded outcome.
- AC-2 Reload and a second browser show the same outcome; clearing `sessionStorage` changes nothing.
- AC-3 `quoteOutcomeStore.ts` no longer exists and no production module imports it.
- AC-4 Recording an outcome writes nothing to `sessionStorage` / `localStorage`.
- AC-5 Loading and error states follow ADR-018 `:112-119`.

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/quotes/quoteOutcomeStore.ts` | deleted |
| `web/src/routes/quotes/index.tsx` | `:30` store import removed; the mount effect `:118-132` also calls `getQuote`; the recorded outcomes render from the server response; `:290`'s `rememberNegotiationOutcome` call removed; `:274-282`'s capture body is **unchanged** |
| `web/tests/routes/quotes/quoteOutcomeStore.test.ts` | deleted with its store |
| `web/tests/routes/quotes/QuoteCheckRoute.test.tsx` | the screen renders a **server-supplied** outcome with **no session write** |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-12` (its screen half; the API half is `E19/F02/US01/T01`).

Decision row: `reports/architecture/waves/w16.md`, **NW-12** — client-architect
cell, which records a correction to that seat's own lane draft.

- **Architecture decisions in force**: **ADR-012** w16 clause 23 (one
  `GET /api/quotes/{id}` call on mount alongside the existing recalculate; the
  `getQuote` wrapper), clause 21 (`raffa.quotes.negotiationOutcomes` is deleted
  **with** its read-back, never before it); **ADR-001** w16 clause 2 (**no new
  screen**; the read-back and nothing more); **ADR-018** `none` — the route
  `/quotes/:quoteId` already exists at
  `web/src/components/shell/WorkspaceShellApp.tsx:90`, the screen already reads
  it via `useParams` (`web/src/routes/quotes/index.tsx:68`) and upload already
  navigates to it (`:151`), so the quote id survives a reload **today**.
- **The correction that makes this shape the right one**: the screen does **not**
  call `getQuoteAssessment`, on mount or ever. Its mount effect calls
  `apiClient.recalculateQuoteAssessment` (`:91`), because the recalculate
  response is a **superset** of the assessment GET's shape —
  `web/src/api/client.ts:805-813` records exactly that. Riding the outcome on the
  assessment GET would have shipped a read-back **the screen cannot see**.
- **Design boundary** (`inputs/design/prototypes/raffa-v2/screens-v2.md:139-145`):
  the Quote check screen shows supplier quote lines (Quoted · Market band ·
  Position); "Target and negotiation levers are one step further — shown only if
  you want them." The cross-quote **history**, the levers and an Ask-citable list
  are **NW-57 (W18)**. Render the outcome of *this* quote and stop.
- **ADR-018 `:112-119`** — loading = skeleton rows, empty = h3 + one sentence +
  primary action, error = 2px accent left rule + h4 + the **plain-language**
  name ("the quote") + a secondary Retry. **Never the raw path.**
- **Do not touch**: `web/src/api/client.ts`, `web/src/api/generated/schema.ts`,
  `web/openapi/raffa-api.v1.json` (all `E19/F06/US01/T01`, an earlier phase — the
  `getQuote` wrapper already exists by the time this task runs);
  `web/src/routes/renewals/**`, `web/src/routes/contracts/**`,
  `web/src/routes/savings/**` (all `E19/F07/US01/T01`, **this same phase**);
  `web/e2e/v2.spec.ts` (a sibling of this phase owns it). Do **not** send
  `savingsOpportunityId` from the client — NW-21 resolves the link **server-side**
  and the capture body at `:274-282` stays exactly as it is.

## Definition of done

- [ ] `cd web && npm run build` exit 0
- [ ] `cd web && npm test` exit 0
- [ ] `ls web/src/routes/quotes/quoteOutcomeStore.ts` fails — the file is gone
- [ ] `grep -rn "quoteOutcomeStore\|raffa.quotes.negotiationOutcomes" web/src web/tests` returns **no match**
- [ ] `grep -c "savingsOpportunityId" web/src/routes/quotes/index.tsx` is **0** — the client still names no opportunity
- [ ] `git diff --name-only -- web/src/api web/openapi web/e2e web/src/routes/renewals web/src/routes/contracts web/src/routes/savings backend/` is **empty** for this task

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| component | the quote screen renders a **server-supplied** outcome with **no session write** | `web/tests/routes/quotes/QuoteCheckRoute.test.tsx` |
| component | a failed `getQuote` shows the ADR-018 error state naming the surface in plain language, with a Retry | `web/tests/routes/quotes/QuoteCheckRoute.test.tsx` |

## Open questions blocking this task

- none.

## Wave-spec entry

```yaml
- id: E19/F02/US02/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-02-quote-and-outcome-read-back/us-02-quote-outcome-on-screen/tasks/task-01-quote-outcome-on-screen.md
  produces: [quote-outcome-on-screen]
  depends_on: [theme-b-contract-and-client]
  effort: S
  layer: frontend
  status: live
```
