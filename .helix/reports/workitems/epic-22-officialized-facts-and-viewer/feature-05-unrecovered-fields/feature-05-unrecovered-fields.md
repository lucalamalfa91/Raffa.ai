---
id: feature-05
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-07 F03
---

# feature-05-unrecovered-fields — a field OCR did not recover is shown, not hidden

## Slice

Review stops **hiding** the fields extraction missed. Today
`web/src/routes/contracts/review/reviewViewModel.ts:244` drops any row whose
`currentValue` is null and whose proposal is null or blank, and because `endDate`
(`:82`) and `cancellationDeadline` (`:84`) are `required: false`, an OCR miss makes
them **vanish from the screen** — the product asserting a completeness it does not
have. This feature keeps those rows and offers them as **empty, fillable** inputs
under a section headed **"Not found in the document"**, writing through the
correction path the rows above them already use.

Pure web: **no new endpoint, no contract change, no migration**. The fill reuses
`PATCH /api/contracts/{id}` (`web/src/api/client.ts:2403-2411` via
`web/src/routes/contracts/review/useReviewSession.ts:159-162`), and the value reads
back through the `load()` the correction already triggers — which is what makes it
survive a reload and a second browser.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | not-found-in-the-document | w17 |

## Architecture decisions in force

- **ADR-001** w17 clause 5 — the five critical fields (`annualSpend`,
  `totalContractValue`, `cancellationDeadline`, `endDate`, `renewalTermMonths`) plus
  the already-required ones are **always shown, recovered or not**. ⚠ **Termination
  and price uplift have no correctable field at all and are not invented this wave** —
  a bounded, recorded gap and a W18 candidate.
- **ADR-012** w17 clause 38 — no new endpoint; the row survives in a `missing` state;
  **the test that locks today's hiding behaviour is rewritten, never deleted**.
- **ADR-020** w17 §13(c)-(e) — the heading, the one sentence, the placement at the end
  of the field-list column, "only fields with a correctable target", and **no empty
  state at all**.
- **ADR-019** w17 clause 12 — no new token and no new component; the rows use the
  locked `.input` / `.field > label` catalogue.

## Target repo

`raffa-web`

## Single-writer note

`reviewViewModel.ts`, `ReviewFieldList.tsx` and
`web/tests/routes/contracts/review/reviewViewModel.test.ts` are **two-item files**
this wave: NW-71 writes them in **phase 3** (`E22/F01/US02/T01`), this feature in
**phase 4**. Constraint 1 *orders* the two items, but **order is not a phase** —
`check_single_writer.py` rejects on the file, so the separation is by phase
(ADR-014 w17 clause 7.4).
