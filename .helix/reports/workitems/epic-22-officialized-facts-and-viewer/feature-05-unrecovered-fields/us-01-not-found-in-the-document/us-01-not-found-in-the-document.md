---
id: us-01
type: user-story
parent: feature-05
wave: w17
status: active
---

# us-01-not-found-in-the-document — the fields extraction missed are offered, not hidden

## Story

As a **procurement lead**, I want the fields Raffa could not find in my document to
**appear as empty rows I can fill**, so that **I can complete the contract myself
instead of discovering months later that a date the screen never mentioned was never
captured**.

## Acceptance criteria

- [ ] AC-1 A canonical field with **no value and no proposal** renders as an empty
      fillable row instead of being dropped — regardless of its `required` flag.
      `endDate` and `cancellationDeadline` are `required: false`
      (`reviewViewModel.ts:82`, `:84`), which is exactly why they vanish today at
      `:244`. (N18)
- [ ] AC-2 The rows sit in a section headed **"Not found in the document"** at the
      **end of the field-list column**, carrying one sentence: *"Raffa could not find
      these in the file. Type the value if you have it — it is saved as your
      correction."*
- [ ] AC-3 Filling a row **persists**: the value survives a reload **and** a second
      browser, written through the **existing** `PATCH /api/contracts/{id}` and read
      back by the `load()` the correction already triggers. **No new endpoint.** (N18)
- [ ] AC-4 **Only fields with a correctable target appear.** Termination and price
      uplift have no correctable field this wave and **must not** be given an input
      that writes nowhere.
- [ ] AC-5 **The section does not render at all** when every canonical field was
      recovered — no heading, no "nothing here" sentence.
- [ ] AC-6 **A missing field is never given a confidence tag.** It has no confidence,
      and `—` is not a value.
- [ ] AC-7 **"Mark as validated" still reflects the decided set** — a missing field
      does not silently unblock or silently block the CTA beyond the rule NW-71
      already set.
- [ ] AC-8 The test that asserts today's hiding behaviour
      (`web/tests/routes/contracts/review/reviewViewModel.test.ts:347`) is
      **rewritten to assert the new row, never deleted**. (N18)

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E22/F01/US02/T01` (phase 3) | NW-71 rewrites `reviewViewModel.ts`'s row shape and its tag/gate functions; this story is the **phase-4** writer of the same file, `ReviewFieldList.tsx` and `reviewViewModel.test.ts` (ADR-014 w17 clause 7.4 — order is not a phase) |

## Architecture decisions in force

- **ADR-001 w17 clause 5** — the always-shown set, and the recorded refusal to invent
  a correctable target for termination and price uplift.
- **ADR-012 w17 clause 38** — pure web, no new endpoint, the read-back is free, and
  **"the test is the task"**.
- **ADR-020 w17 §13(c)-(e)** — heading, sentence, placement, "only correctable
  fields", no empty state, no confidence tag on a missing field.
- **ADR-019 w17 clause 12** — no new component, no new token.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | not-found-in-the-document | M | phase-4 |

## Council decisions carried into this story

- **Placement diverges from the raw file, and the divergence is named.** The item says
  the section sits "next to the viewer"; ux-ui-designer reads that as *beside* the
  evidence column and **not inside it** — `inputs/design/prototypes/design-system.md:47` sizes the detail pane
  at 340–400 px and a fill-in form there is cramped, while the pane keeps its single
  job of showing the page a value came from.
- **The required/optional wording stays product-owner's**; the **visibility** rule
  (every canonical field with no value and no proposal, regardless of `required`) is
  ux-ui-designer's.
- **The field list gains a fourth row state** (`not found`) while the **screen's** four
  states (`screens-v2.md:93`) are unchanged.
- **Cross-item composition** (ADR-020 w17 §14(f)): Contract 360's "When you must move"
  cell reaches its *no answer yet* state **only** on an extraction miss — i.e. this
  section — so that cell reads **"Add the end date"** and links to Review rather than
  "Not determined". The two items compose instead of each inventing a dead end. The
  360 half is `E21/F03/US02/T01`'s, phase 3.

## Open questions

- **none blocking.**
