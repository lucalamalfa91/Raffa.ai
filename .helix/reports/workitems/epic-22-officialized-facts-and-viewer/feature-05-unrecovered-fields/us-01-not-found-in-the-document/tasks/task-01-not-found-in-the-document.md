---
id: E22/F05/US01/T01
type: task
story: us-01-not-found-in-the-document
wave: w17
status: live
target_repo: raffa-web
---

# task-01-not-found-in-the-document — unrecovered fields become empty fillable rows

## Context

**Closes: NW-64.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-64** row (product-owner,
client-architect and ux-ui-designer halves).

ADRs in force: **ADR-001** w17 clause 5; **ADR-012** w17 clause 38; **ADR-019** w17
clause 12; **ADR-020** w17 §13(c)-(e) and §14(f).

⚠ **Single writer by phase.** `web/src/routes/contracts/review/reviewViewModel.ts`,
`ReviewFieldList.tsx` and `web/tests/routes/contracts/review/reviewViewModel.test.ts`
are **two-item files** this wave: NW-71 writes them in **phase 3**
(`E22/F01/US02/T01`), this task in **phase 4**. Constraint 1 *orders* the two items,
but **order is not a phase** — `check_single_writer.py` rejects on the file
(ADR-014 w17 clause 7.4). Start from NW-71's row shape; do not re-derive it.

Already on `main`, so **not** work to redo: the correction write path —
`PATCH /api/contracts/{id}` at `backend/src/Raffa.Api/ContractsEndpointExtensions.cs:78`
(handler `:367`), client wrapper `web/src/api/client.ts:2403-2411`, caller
`web/src/routes/contracts/review/useReviewSession.ts:159-162` — and the `load()`
read-back it already triggers. **This item adds no endpoint and no contract change.**

## Coding objective

In `raffa-web`, stop hiding the fields extraction did not recover.

1. **Keep the row.** `web/src/routes/contracts/review/reviewViewModel.ts:244` today
   reads `if (currentValue === null && (proposedValue === null || proposedValue.trim()
   === "")) continue;` inside `buildReviewFields` (`:231-269`) — verified on this
   checkout. Under NW-64 that row **survives in a `missing` state** instead of being
   dropped. This is deliberate behaviour documented at `:189-191`, so the comment
   moves with the code.
2. **Which fields.** **Every canonical field with no value and no proposal, regardless
   of its `required` flag.** `endDate` (`:82`) and `cancellationDeadline` (`:84`) are
   `required: false`, which is exactly why they vanish today; `annualSpend` (`:85`),
   `totalContractValue` (`:86`) and `renewalTermMonths` (`:88`) are the other three of
   product-owner's five critical fields, and the already-required ones (supplier,
   type, status, currency, auto-renewal) keep their existing behaviour.
3. ⚠ **Only fields with a correctable target appear.** **Termination and price uplift
   have no correctable field at all this wave and are not invented** (ADR-001 w17
   clause 5): an input that writes nowhere is **worse than an absent row** — it takes
   the user's answer and silently drops it. Record them as a known gap; the
   final-integration task carries them into `docs/waves/w17-acceptance.md`.
4. **The section.** A section at the **end of the field-list column**, after the
   `review_required` rows, headed **"Not found in the document"**, with exactly one
   sentence: **"Raffa could not find these in the file. Type the value if you have it
   — it is saved as your correction."** Each row is the **field label** plus an empty
   `.input`, using the locked catalogue (`inputs/design/prototypes/design-system.md:37`, `.input` and
   `.field > label`) — **no new component, no new token**.
5. ⚠ **Placement is beside the evidence column, never inside it.** The raw item says
   "next to the viewer"; the ruling reads that as *beside* the evidence pane, which
   `inputs/design/prototypes/design-system.md:47` sizes at 340–400 px — a fill-in form there is cramped, and
   the pane keeps its single job of showing the page a value came from.
6. ⚠ **No empty state.** When every canonical field was recovered **the section does
   not render at all** — no heading, no "nothing here" sentence. A sentence announcing
   an absence is itself noise.
7. ⚠ **A missing field is never given a confidence tag.** It has no confidence, and
   `—` is not a value. The field list gains a **fourth row state** (`not found`) while
   the **screen's** four states (`screens-v2.md:93`) are unchanged.
8. **The write is the existing one.** Filling a row calls the same correction path the
   rows above it use — `useReviewSession.ts:159-162` → `client.ts:2403-2411` — and the
   persisted value reads back through the `load()` that correction already triggers.
   **No new endpoint, no new fetch, no client-side store.**
9. **"Mark as validated" still reflects the decided set.** A missing field must not
   silently unblock or silently block the CTA beyond the rule NW-71 set in phase 3,
   and the CTA keeps its **visible reason** (ADR-019 `:119-121`).
10. ⚠ **The test is the task.**
    `web/tests/routes/contracts/review/reviewViewModel.test.ts:347` — verified on this
    checkout as `it("a proposal with an empty value does not create a row (nothing to
    review)", …)` — **locks today's skip and must be rewritten to assert the new row.
    Deleting it is how a behaviour change becomes unobservable.**

## Parent story AC covered

- AC-1 A canonical field with **no value and no proposal** renders as an empty fillable row instead of being dropped — regardless of its `required` flag. (N18)
- AC-2 The rows sit in a section headed **"Not found in the document"** at the **end of the field-list column**, carrying the one sentence above.
- AC-3 Filling a row **persists**: the value survives a reload **and** a second browser, through the **existing** `PATCH /api/contracts/{id}`. **No new endpoint.** (N18)
- AC-4 **Only fields with a correctable target appear.**
- AC-5 **The section does not render at all** when every canonical field was recovered.
- AC-6 **A missing field is never given a confidence tag.**
- AC-7 **"Mark as validated" still reflects the decided set.**
- AC-8 The test at `reviewViewModel.test.ts:347` is **rewritten, never deleted**. (N18)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/contracts/review/reviewViewModel.ts` | modify — `:244` stops dropping the row; a `missing` row state; the always-shown canonical set regardless of `required` (`:82`, `:84`, `:85`, `:86`, `:88`); the `:189-191` comment moves with the behaviour; a missing row carries **no** confidence tag |
| `web/src/routes/contracts/review/ReviewFieldList.tsx` | modify — the fourth row state (`not found`) and the "Not found in the document" section at the end of the column: heading, the one sentence, label + empty `.input` per row |
| `web/src/routes/contracts/review/index.tsx` | modify — render the section beside the evidence column, never inside it; nothing renders when the set is empty |
| `web/src/routes/contracts/review/review.css` | modify — only if the section needs a rule; **no new token, no new component, no shadow** |
| `web/tests/routes/contracts/review/reviewViewModel.test.ts` | modify — **rewrite** `:347` to assert the surviving row; a recovered field produces no missing row; a field with no correctable target never appears |
| `web/tests/routes/contracts/review/ReviewRoute.test.tsx` | modify — the section renders with its heading and sentence; it is **absent** when every field was recovered; no confidence tag on a missing row |
| `web/e2e/w17-review.spec.ts` | **new** — **N18**: a document whose OCR missed end date and cancellation deadline shows both as empty fillable rows; filling one persists across a reload |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Design oracle and the anchors this task implements** — `Raffa V2 Prototype.html`
  (unpacked: `inputs/design/prototypes/raffa-v2/screens-v2.md`):
  - **`screens-v2.md:81-93`**, screen **§4 Review** — `:87` is the field list this
    section extends (*"Field list with confidence tags…"*), `:93` fixes the screen's
    four states (*"weak facts open · all decided · correcting · validated"*), which
    **this task does not change**.
  - `inputs/design/prototypes/design-system.md:37` — `.input`, `.field > label`: the
    locked controls the rows are built from.
  - `inputs/design/prototypes/design-system.md:47` — *"Detail pane: 340–400px,
    --color-surface, 2px left rule"*: the **stated reason** the section is beside the
    evidence pane rather than inside it.
  - Export **(15)** (the two-item legend and this section) is **owed and blocks
    nothing** (ADR-020 w17 §18) — the copy above is decided from the locked catalogue.
- **Architecture decisions in force**:
  - **ADR-001 w17 clause 5** — the always-shown set; **termination and price uplift
    are not invented this wave**, recorded as a bounded gap.
  - **ADR-012 w17 clause 38** — pure web, no new endpoint, the read-back is free, and
    the locked test is rewritten rather than deleted.
  - **ADR-020 w17 §13(c)-(e)** — heading, sentence, placement, correctable-target
    rule, **no empty state**, no confidence tag on a missing field.
  - **ADR-020 w17 §14(f)** — the composition with Contract 360's "When you must move"
    cell, whose *no answer yet* state reads **"Add the end date"** and links here. The
    360 half is `E21/F03/US02/T01`'s (phase 3); **do not implement it here**.
  - **ADR-019 w17 clause 12** — no new token, no new component.
- ⚠ **Vocabulary fence**: `officialized` is an **ADR word** and never appears on a
  screen.
- **Do not touch**: `web/src/styles/semantics.ts` and
  `web/src/routes/contracts/review/ReviewHeader.tsx` / `useReviewSession.ts`'s
  decision plumbing (NW-71's, phase 3 — consume the row shape it produced);
  `web/src/routes/contracts/contract360/**` (this phase's other web task,
  `E22/F04/US01/T01`); `web/src/api/client.ts`; `web/openapi/raffa-api.v1.json`;
  `web/e2e/v2.spec.ts` (NW-73's alone this wave); `web/package.json`.

## Definition of done

- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npm test` exits 0, with the **rewritten** `reviewViewModel.test.ts:347` case present (`grep -n "nothing to review" web/tests/routes/contracts/review/reviewViewModel.test.ts` no longer asserts the row is absent)
- [ ] `cd web && npx playwright test e2e/w17-review.spec.ts` exits 0
- [ ] `grep -rn "Not found in the document" web/src/routes/contracts/review/` returns the heading exactly once
- [ ] `grep -rn "getConfidenceTag\|confidencePct" web/src/routes/contracts/review/ReviewFieldList.tsx` shows **no** use on a missing row
- [ ] `git diff --stat origin/main -- web/src/api/client.ts web/openapi/raffa-api.v1.json` shows **no change attributable to this task** (no new endpoint, no contract delta)
- [ ] `git diff --stat origin/main -- web/package.json` is **empty** (still five runtime dependencies)
- [ ] on `dev`: **N18** — a document where OCR misses end date and cancellation deadline shows both as **empty fillable rows**; filling one persists across a reload **and** a second browser; "Mark as validated" still reflects the decided set

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | a canonical field with no value and no proposal **produces a row** (the rewritten lock at `:347`), and the row carries **no** confidence tag | `web/tests/routes/contracts/review/reviewViewModel.test.ts` |
| unit | a recovered field produces **no** missing row, and a field with no correctable target never appears in the section | `web/tests/routes/contracts/review/reviewViewModel.test.ts` |
| component | the section renders with its heading and sentence; it is **absent** when every canonical field was recovered | `web/tests/routes/contracts/review/ReviewRoute.test.tsx` |
| component | filling a row calls the existing correction path and the value comes back from `load()` — **not** from local state | `web/tests/routes/contracts/review/ReviewRoute.test.tsx` |
| e2e | **N18** — the missing fields render fillable and the fill **survives reload** | `web/e2e/w17-review.spec.ts` (**new**) |
| manual (`dev`) | **N18** as written above, second browser included | `docs/waves/w17-acceptance.md`, written by `E22/F06/US01/T01` |

## Open questions blocking this task

- **none blocking.**
- ⚠ **Recorded gap, not a question**: termination and price uplift have **no
  correctable field** this wave (ADR-001 w17 clause 5). Do not add an input for them;
  the final-integration task carries them into the acceptance doc's known-gaps table
  as W18 candidates.

## Wave-spec entry

```yaml
- id: E22/F05/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-05-unrecovered-fields/us-01-not-found-in-the-document/tasks/task-01-not-found-in-the-document.md
  produces: [review-unrecovered-fields]
  depends_on: [auto-accept-web]
  effort: M
  layer: frontend
  status: live
```
