---
id: us-02
type: user-story
parent: feature-03
wave: w17
status: active
---

# us-02-answers-band-web — the band renders the server's answer, in three real states

## Story

As a **procurement lead** opening Contract 360, I want the first two answers to
be concrete — where I can save and when I must move, with citations — so that I
stop reading a placeholder that the screen was never able to replace.

## Acceptance criteria

- [ ] AC-1 On a validated contract whose strategy answers, **Where you can
  save** shows either a figure with its lever, or a **representative** band
  whose provenance (adapter, sample size, as-of) is on the detail line — never
  bare. (N16)
- [ ] AC-2 **When you must move** shows the notice deadline, the days left and
  whether it auto-renews. (N16)
- [ ] AC-3 `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE` are reachable
  **only** when the strategy fetch was genuinely made and returned nothing or
  failed. They are **not** the steady state of a source that is never called.
- [ ] AC-4 When "When you must move" has no date because extraction missed it,
  the cell reads **"Add the end date"** and links to Review — never "Not
  determined".
- [ ] AC-5 A failing strategy fetch **degrades its own answer** to an honest
  "not yet" and does **not** fail the screen.
- [ ] AC-6 The 360 has **exactly one** answer source: no second computation of
  either answer exists in the view model.
- [ ] AC-7 No copy on Contract 360 uses the word **"weak"**, and
  `OpenWeakFacts` is not rendered as a review statement.
- [ ] AC-8 No new component and no CSS change: the band renders through the
  existing `AnswersBand.tsx` and its existing `answerDisplayClass` / `is-prose`
  behaviour.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E21/F03/US01` (phase 2) | the band's data and the `client.ts` wrapper that fetches it |
| `E21/F02/US01` (phase 2) | `marketPosition` stops being null, so the `lever` read at `contract360ViewModel.ts:175` becomes reachable |

## Architecture decisions in force

- **ADR-012** w17 clause 35 — **one answer source per screen**; the constants
  survive only as the absent-or-failed state of a source really called.
- **ADR-020** w17 §14(f)-(g) — three real states per cell; provenance never
  bare; the missing-fact state names the way to get the fact; no "weak".
- **ADR-001** w17 clause 4 — representative, or explicit insufficiency.
- **ADR-024** w17 clause A4 — `OpenWeakFacts` renders no review statement.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | answers-band-web | M | phase-3 |

## Council decisions carried into this story

- **The cost is already paid** (client-architect): `buildAnswers`
  (`web/src/routes/contracts/contract360/contract360ViewModel.ts:160`) **maps** a
  server answer; it does not compute one.
  `web/src/routes/contracts/contract360/index.tsx:115-119` **already** runs
  parallel GETs after the 360, and `:47-50` **already** documents the degrade
  rule — an optional source that fails *"degrades its own answer to an honest
  'not yet' rather than failing the screen"*. Adding the strategy fetch to that
  `Promise.all` is a **wiring change, not an architecture change**.
- **The acceptance line, stated so a test can catch it**:
  `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE` (`:150-152`) survive
  **only** as the absent-or-failed state of a source that was really called —
  **never as the steady state of a source that is never called**, which is
  precisely the defect today. ⚠ **A test that only asserts "the constant is
  gone" would not catch this.**
- **Three real states per cell** (ADR-020 w17 §14(f)): (i) a figure and its
  lever; (ii) a **representative** band whose provenance rides the `save.lever`
  detail line (`AnswersBand.tsx:56`) and is **never bare**, reusing
  `ia-v2.md:110`'s existing Ask vocabulary (`adapter A, n = 214`) rather than
  inventing a second one; (iii) **no answer yet**, naming what is missing **and
  the way to get it** — a placeholder that names neither is the defect, not the
  absence.
- **The items compose instead of each inventing a dead end** (ADR-020 w17
  §14(g)): "When you must move" is answerable now, so its state (iii) is
  reached **only** on an extraction miss — which is `E22/F04/US01`'s section —
  and it therefore reads "**Add the end date**" and links to Review.
- **No component and no CSS change**: `answerDisplayClass` (`:33-34`) already
  drops text over 28 characters to `is-prose`, so a real figure stays
  display-size and a gap sentence stays readable. ⚠ The band was **designed**
  for the gap, which is exactly why the gap reads as normal today and why state
  (iii) must be visibly an **edge** state.
- ⚠ **ADR-012 §48's ruling stands with a corrected premise** (ADR-020 w17 §25):
  a document whose embedding corpus was destroyed renders a 360 that is
  **complete, populated and confident** — `contract360ViewModel.ts` has **zero**
  occurrences of `corpus` or `chunk`. `SAVINGS_NOT_YET_AVAILABLE` is the
  **benchmark**'s constant, not a corpus state, and `:152` asserts the clauses
  below are **validated** — firing it for a missing corpus would be **worse than
  silence**. **No corpus copy and no corpus state ships this wave.**
- ⚠ **One prohibition**: no task may add a reassuring **"re-indexing" /
  "catching up"** banner to Contract 360 or Ask — nothing requeues after a
  corpus delete, so it would be a *not ready yet* claim that can never resolve
  (ADR-018 w15 clause 6).

## Open questions

- **OQ-w17-005** — **ruled**: consume the endpoint. The rule this story adds to
  it is AC-3.
- **OQ-w17-sa-01** — **ruled**: `OpenWeakFacts` renders no review statement and
  **no copy on this screen uses the word "weak"**.
- **OQ-w17-ux-05** — **open, a W18 condition on widening the bulk console beyond
  `dev`.** The surface cannot distinguish *retrieved nothing* from *has nothing
  to retrieve*, and no read-model member carries the difference.
  **Assumption in force: `dev`-only**, so **w17 ships no signal, no copy and no
  state** for it.
