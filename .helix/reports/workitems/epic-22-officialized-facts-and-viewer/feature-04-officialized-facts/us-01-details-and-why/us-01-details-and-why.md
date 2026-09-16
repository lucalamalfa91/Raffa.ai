---
id: us-01
type: user-story
parent: feature-04
wave: w17
status: active
---

# us-01-details-and-why — only officialized facts, and a Why row that speaks leverage

## Story

As a **procurement lead**, I want Contract 360 to **show only facts the product
can stand behind, in negotiation language**, so that **I can read the page as a
statement of what is true rather than a list of things the model is unsure
about**.

## Acceptance criteria

- [ ] AC-1 The "Facts you still need to decide" block is **gone** from Details.
      In its place, column 2 ends with a single line — **"N facts still need you
      — Review all →"** — and **when N = 0 the line does not render at all**.
      (N19)
- [ ] AC-2 No sentence on the page claims a fact was "signed off by you".
- [ ] AC-3 **Every** product/term row still renders. An unofficialized value
      renders the **em-dash placeholder already in this view model's
      vocabulary** — a row is never dropped, because the user cannot tell an
      absent line from an unextracted one.
- [ ] AC-4 The officialized gate applies to **Key terms, Products, Obligations
      and Risks** — not to Key terms alone.
- [ ] AC-5 **No confidence tag renders anywhere on Contract 360.** (N19, N20)
- [ ] AC-6 A Why row shows type · officialized value · a **leverage** tag · a
      one-line why · **Open in document viewer**. The **source cell and the
      confidence tag are gone**. (N20)
- [ ] AC-7 The leverage words are **Push to change** (Critical/High) · **Worth
      raising** (Medium) · **Standard terms** (Low). The raw
      `ContractRiskLevel` enum never reaches the screen, and a **null** risk
      level stays **null** — "Standard terms" is never synthesised where risk was
      not determined.
- [ ] AC-8 A three-term **legend** renders in the hint slot the component already
      has, so the vocabulary is not something the user must infer.
- [ ] AC-9 The original quote appears **only** inside `ClauseHighlight`, and the
      short `p.N · §` reference adopts **Review's existing 60-character cap** —
      not a second truncation rule for a neighbouring screen.
- [ ] AC-10 **Open in document viewer** links to
      `/documents/:documentId/viewer?page=<sourcePage>&clause=<clauseId>` and the
      link **lands**. (N20)

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E22/F03/US01/T01` (phase 3) | e2e **N20** asserts the deep link **lands**, which needs the route **registered**, not merely ADR-decided |
| `E22/F01/US02/T01` (phase 3) | NW-71 rewrites the bands these rows render, and its persisted decision is what makes the count line true for the first time |
| `E21/F03/US02/T01` (phase 3) | it is `contract360ViewModel.ts`'s phase-3 writer; this story is its phase-4 writer |

## Architecture decisions in force

- **ADR-020 w17 §14(a)-(d)** — delete the block, keep "Review all →" re-homed
  with a count, and keep every row via the em-dash placeholder.
- **ADR-020 w17 §14(e)** — the Why row's new shape and the legend.
- **ADR-020 w17 §17** — two of the five ratified divergences are this story's:
  `screens-v2.md:101-102` (the export's Why row carries **confidence**) and
  `:103-104` (the export's Details carries **facts still to decide**).
- **ADR-019 w17 clause 9** — **label-only**; the variant expression stays
  byte-identical and the precedent is already in the file.
- **ADR-019 w17 clause 11** — no confidence tag on Contract 360 at all.
- **ADR-012 w17 clause 36** — the gate lives in the view model.
- **ADR-001 w17 clause 6** — confidence lives in Review; Appendix C's rendering
  half is superseded on 360 only, its storage half untouched.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | details-and-why | L | phase-4 |

## Council decisions carried into this story

- **`contract360ViewModel.ts` is a one-writer-per-phase file and this story owns
  it in phase 4.** Its three contenders are NW-62 (phase 3), NW-65 and NW-66
  (both here). NW-71 **need not open it at all** and NW-20 gets **no client
  task**, so the contender set is exactly three.
- **`FactTable.tsx` is assigned here.** It is the only finding of the w17 table
  that no ADR closes (ADR-014 w17 clause 7.7), reached independently by
  client-architect, ux-ui-designer and delivery-manager.
- ⚠ **`computeNeedsAttention`'s gate is a colour check** (OQ-w17-cl-02):
  `contract360ViewModel.ts:575` tests `tag.variant !== "neutral"`, inferring a
  decision from **presentation** against `semantics.ts:45-46`'s explicit
  instruction not to. NW-71 collapsing the bands would **silently change which
  facts this list shows with nobody editing the line**. This story rewrites the
  function anyway, so the fix is free — but unnamed it would be preserved
  verbatim as working code.
- The leverage map lives in `contract360ViewModel.ts`, **not** in
  `semantics.ts`, which constraint 2 reserves for NW-71.
  `semantics.ts:getRiskTag` (`:91-96`) is a **different function** on Portfolio
  and Renewals and is **not renamed**.
- **The sparse-column risk is the filter, not the deletion**, and the raw file
  points at the wrong one: `contract360.css:391-394` is
  `repeat(2, minmax(0,1fr))` with a full-width third child (`:400-402`), and the
  columns are **independent grid items** — so removing the second half of column
  2 leaves Key terms | Documents intact. What *would* leave a hole is dropping
  unofficialized **rows** from Key terms, which the em-dash rule prevents
  structurally.

## Open questions

- **none blocking.** OQ-w17-cl-02 is ruled (rewrite the gate to read the
  persisted decision, not a colour). OQ-w17-sa-01's UX half is ruled and lands
  on `E21/F03/US02/T01`, not here.
