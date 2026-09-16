---
id: feature-04
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-07 F01
---

# feature-04-officialized-facts — Contract 360 carries only facts it can stand behind

## Slice

Two items, **one task**, because both write
`web/src/routes/contracts/contract360/contract360ViewModel.ts` and constraint 3
gives that file one writer per phase (its third contender, NW-62, is
phase-separated in phase 3).

**NW-65** deletes `DetailsSection.tsx:78`'s "Facts you still need to decide"
block and its rows (`:83-93`), keeps **"Review all →"** (`:79-81`) re-homed as a
single trailing count line, and deletes `NO_ATTENTION_MESSAGE`
(`contract360ViewModel.ts:586`) — whose *"or signed off by you"* clause is
**untrue on this base**, because `computeNeedsAttention` (`:568-583`) reads no
human-decision state at all. When the system does not hold a claim, the honest
repair is to **stop making it**, not to phrase it more carefully.

**NW-66** takes the original quote and the confidence percentage off the Why
row, replaces the raw `ContractRiskLevel` enum with **leverage** words — *Push
to change* (Critical/High) · *Worth raising* (Medium) · *Standard terms* (Low) —
moves the quote into the existing `ClauseHighlight` (the "specchietto" is **not
a new component**), and links the row to NW-63's viewer route.

This task also **owns `FactTable.tsx`** — the one finding of the w17 table that
no ADR closes. It is a `getConfidenceTag` consumer (`:35`) that appears in no
item's file set, and unassigned, NW-65's officialized gate would ship on **Key
terms alone** while Products, Obligations and Risks still show unofficialized
values: a screen inconsistent with itself, which is worse than either end state.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | details-and-why | w17 |

## Architecture decisions in force

- **ADR-020** w17 §14(a)-(e), §17 — the deletion, the single trailing count
  line, the em-dash rule that keeps every row, the Why row's new shape, and the
  ratified divergences from `screens-v2.md:101-104`.
- **ADR-019** w17 clause 9 — **label-only**: `getClauseRiskTag`
  (`contract360ViewModel.ts:255-259`) **already** computes `High || Critical →
  accent`, everything else → `neutral`, with its own docstring (`:254`) saying
  *"Text first, colour only as emphasis."* Semantic-mapping row `:106` is
  **unchanged**; Medium and Low keep sharing `.tag-neutral`, distinguished by
  their **words**.
- **ADR-019** w17 clause 11 — **no confidence tag renders on Contract 360 at
  all**.
- **ADR-012** w17 clause 36 — the officialized gate moves into the view model so
  the column is not left sparse.
- **ADR-018** w17 clause 10 — three surfaces link to the viewer, which is why
  the route shape had to be decided this wave.
- **ADR-001** w17 clause 6 — confidence lives in **Review**; Contract 360 never
  renders it. Appendix C `product-spec.md:954` is superseded **in its rendering
  half, on 360 only**; it stands verbatim in Review and in Ask, and its
  **storage half (`:127`) is untouched**.

## Target repo

`raffa-web`.
