---
id: us-02
type: user-story
parent: feature-01
wave: w17
status: active
---

# us-02-review-renders-the-decision — the screen renders the server's decision and the session store retires

## Story

As a **procurement reviewer**, I want the Review screen to **show the decision
the server already took**, so that **what I saw before a reload is what I see
after it, and in another browser — and the percentage beside a word never
contradicts the word**.

## Acceptance criteria

- [ ] AC-1 A field the server accepted automatically renders **"Accepted
      automatically · NN%"**; a field a human accepted renders **"Accepted by
      you"** with **no percentage**; everything else renders **"Review · NN%"**.
      The two accepted states are distinguished by the **label**, never by the
      colour variant. (A17-3)
- [ ] AC-2 The percentages are **floored, never rounded up across the bar**: a
      field at `0.895` reads **"Review · 89 %"**, never "Review · 90 %".
- [ ] AC-3 The web never contains the number `90` as a rule: the legend and any
      threshold text come from the server's response.
- [ ] AC-4 Reloading the Review screen shows the same three-state picture, and a
      **second browser** signed in to the same workspace agrees — because
      nothing about acceptance lives in React state any more.
- [ ] AC-5 The screen title no longer names a threshold: "N facts below 80% —
      you decide" becomes **"N facts need you — you decide"**.
- [ ] AC-6 The CTA gate reads the **decision**, not a `< 80` comparison.
- [ ] AC-7 No confidence tag renders on Contract 360 as a result of this change:
      `getConfidenceTag` keeps its signature so the 360 call sites compile
      untouched, and the two that must disappear are deleted by the items that
      own them (`us-01` of feature-04).

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E22/F01/US01/T01` (phase 2) | the decision, the three values and the threshold must exist on the wire and in the regenerated `schema.ts` before the screen can render them |

## Architecture decisions in force

- **ADR-012 w17 clause 34** — the web **renders** the server's decision and
  never recomputes it. The operator stash's band collapse is taken (the stash is
  resolved **by message**, `"review auto-accept and next-waves-todo"` — its index
  has already shifted from `stash@{2}` to `stash@{3}`); its **rounded
  `>=` comparison is dropped** — a client-side comparison against the bar is
  exactly the recomputation this ruling removes.
- **ADR-012 w17 clause 36** — `acceptedThisSession` retires. `accept()` today
  has two branches that are not equal (`useReviewSession.ts:177-182` does a real
  `PATCH` + `load()`; `:184` is a `setState` with **no network call**), painted
  identically, the second vanishing on reload. NW-71 would add a third. One
  screen, three durabilities, one paint is the cache-instead-of-a-GET ADR-012 §1
  forbids.
- **ADR-019 w17 clause 7** — `auto_accepted` → `.tag-neutral` "Accepted
  automatically · NN%"; `human_accepted` → `.tag-neutral` "Accepted by you",
  **no percentage**; `review_required` → `.tag-outline` "Review · NN%".
- **ADR-019 w17 clause 8** — **floor, never round.** `semantics.ts:33`'s
  `Math.round` is the one-line defect.
- **ADR-019 w17 clause 11** — `.tag-accent` is **released from confidence
  entirely**; the one-accent rule gets stronger.
- **ADR-020 w17 §13** — the title stops naming a threshold; the legend is
  **two items, server-fed**.
- **ADR-001 w17 clause 8** — the three acceptance states stay distinguishable
  and an automatic acceptance is never painted as a human one.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | review-renders-the-decision | M | phase-3 |

## Council decisions carried into this story

- `web/src/styles/semantics.ts` is a **one-writer-per-phase file and this story
  owns it** (`w17-requirements.md` §5 constraint 2). `getConfidenceTag` becomes
  **label-only** — it paints a label, it no longer decides acceptance.
- `getConfidenceTag` **keeps its signature**, so the three Contract 360 call
  sites (`WhyClauses.tsx:40`, `FactTable.tsx:35`,
  `contract360ViewModel.ts:574`) compile untouched; deleting them belongs to
  feature-04.
- `semantics.ts:getRiskTag` (`:91-96`) is a **different function** on Portfolio
  and Renewals and is **not** renamed or touched.
- **Vocabulary fence**: `officialized` is an ADR word and **never appears on a
  screen**.
- The read-back is **free**: `useReviewSession.ts:124` already calls
  `getContractEvidence` on every `load()`, so a decision stored on
  `extraction_evidence` needs **zero new endpoints and zero new fetches**.

## Open questions

- **none blocking.** OQ-w17-002's client half is ruled (render, never
  recompute; drop the stash's rounded comparison). OQ-w17-cl-02 is ruled and
  belongs to feature-04's task, which rewrites `computeNeedsAttention`.
