---
id: E22/F01/US02/T01
type: task
story: us-02-review-renders-the-decision
wave: w17
status: live
target_repo: raffa-web
---

# task-01-review-renders-the-decision — render the server's decision, floor the percentage, retire the session store

## Context

**Closes: NW-71** (web half).

Decision row: `reports/architecture/waves/w17.md` — the **NW-71** row
(client-architect and ux-ui-designer halves) and the rulings on **OQ-w17-002**
(client half) and **OQ-w17-cl-02**.

ADRs in force: **ADR-012** w17 clauses 34 and 36; **ADR-019** w17 clauses 7, 8,
10 and 11; **ADR-020** w17 §13 and §17; **ADR-001** w17 clauses 2 and 8.

⚠ **This task is the sole writer this wave of `web/src/styles/semantics.ts`**
(`w17-requirements.md` §5 constraint 2) and the **phase-3 writer** of
`web/src/routes/contracts/review/reviewViewModel.ts`, `ReviewFieldList.tsx` and
`web/tests/routes/contracts/review/reviewViewModel.test.ts`.
`E22/F05/US01/T01` (NW-64) owns those three review files in **phase 4** — order
is not a phase, and `check_single_writer.py` rejects on the file, which is why
the two are phase-separated.

Already on `main`, so **not** a dependency to build: `useReviewSession.ts:124`
already calls `getContractEvidence` on every `load()`, so the decision arrives
with **zero new endpoints and zero new fetches**.

**✔ Seed the band collapse from the operator stash, do not re-derive it.**

⚠ **Resolve the stash by its MESSAGE, never by its index.** The stash stack has
already shifted once since the council wrote this row: the entry was `stash@{2}`
at the w17 table and is **`stash@{3}` as of 2026-09-15**, because a new
`stash@{0}` (*"WIP on feat/instant-identity-ingest"*) was pushed in between.
`stash@{2}` today is *"w14 engine outputs … parked"* — **the wrong stash**, and
applying it would drop engine artefacts into the wave. Resolve it with:

```
git -C .. stash list | grep "review auto-accept and next-waves-todo"
```

and use the `stash@{N}` that command prints. If it prints nothing, stop and ask
the operator — **do not re-derive the band collapse by hand**.

That entry (*"wip: review auto-accept and next-waves-todo (not w14)"*) carries
exactly this file set
(`web/src/styles/semantics.ts`, `ReviewHeader.tsx`, `ReviewFieldList.tsx`,
`reviewViewModel.ts`, `web/README.md` and three test files) and introduces
`AUTO_ACCEPT_MIN_PCT = 90` collapsing the bands to *≥ 90 → Accepted / below →
Review*. **Take the collapse; drop the `AUTO_ACCEPT_MIN_PCT` constant and its
rounded `>=` comparison** — a client-side comparison against the bar is the
recomputation ADR-012 w17 clause 34 removes, and keeping it would put a second,
rounded decision beside the server's unrounded one.

## Coding objective

In `raffa-web`, make the Review screen render the **server's** three-state
decision, and delete the client-side rules it replaces.

1. **`semantics.ts` becomes label-only.** `getConfidenceTag` (`:32-41`) stops
   deciding acceptance and paints a label for a decision it is handed:
   `auto_accepted` → `{ variant: "neutral", label: "Accepted automatically ·
   NN%" }`; `human_accepted` → `{ variant: "neutral", label: "Accepted by you" }`
   with **no percentage**; `review_required` → `{ variant: "outline", label:
   "Review · NN%" }`. **Keep the exported signature compatible** so
   `WhyClauses.tsx:40`, `FactTable.tsx:35` and `contract360ViewModel.ts:574`
   still compile — deleting those three call sites belongs to
   `E22/F04/US01/T01` in phase 4.
2. **Floor, never round** (ADR-019 w17 clause 8). `:33`'s
   `Math.round(confidencePct)` renders a `review_required` field at `0.895` as
   **"Review · 90 %"** — a tag contradicting itself inside one string. Use
   `Math.floor`.
3. **`.tag-accent` leaves confidence.** The `>= 80 → accent "Flagged"` branch
   (`:37-38`) goes; only `neutral` and `outline` remain in this function. The
   two accepted states are distinguished by the **label**, never by the variant
   — **ADR-019 `## Accessibility baseline` (`reports/architecture/ADR-019-web-design-system.md:124-125`)**,
   *"No colour-only semantics: every tag/urgency indicator is paired with a text
   label"*, requires the text to be the carrier. ⚠ This rule is **ADR-019's, not
   the design export's** — `inputs/design/prototypes/design-system.md` is 70 lines
   long and has no `:124-125`. Minting
   a third variant would build OQ-w17-cl-02's colour-as-decision defect into the
   system on purpose.
4. **Retire the client rule.** `isConfidenceBlocking` (`:48-49`, `< 80`) stops
   being the gate. In `reviewViewModel.ts`, `fieldTag` (`:279-284`) and
   `isFieldBlocking` (`:292-295`) read the row's **server decision** instead of
   its percentage. Rewrite the module docstrings at `semantics.ts:9-13`,
   `:28-31`, `:43-47` and `reviewViewModel.ts:271-278`, `:286-291` — all of them
   quote spec §7.3's three bands, which **NW-71 supersedes on the record**.
5. **Retire `acceptedThisSession`.** Delete the `useState` at
   `useReviewSession.ts:92`, its `setAcceptedThisSession(...)` branch at `:184`,
   its argument at `:145` and the `acceptedThisSession` parameter of
   `buildReviewFields` (`reviewViewModel.ts:234`, consumed `:251`). The
   `proposalPending` branch at `:177-182` — a real `PATCH` + `load()` — **stays**
   and becomes the only `accept()` path; the row's decision comes back from the
   server on the next `load()`.
6. **Legend and title.** `ReviewHeader.tsx:89,92,95` becomes **two** items,
   fed from the response's `autoAcceptThreshold` — **the web never hardcodes
   `90`**. The title stops naming a threshold: `screens-v2.md:83`'s "N facts
   below 80% — you decide" becomes **"N facts need you — you decide"**. That
   sentence has now gone stale twice for naming the bar; the count is what the
   user acts on.
7. **Rewrite the tests that lock the old bands**, do not delete them:
   `web/tests/styles/semantics.test.ts:15-37`,
   `web/tests/routes/contracts/review/reviewViewModel.test.ts:192` and
   `web/tests/routes/contracts/review/ReviewRoute.test.tsx:492` (which asserts
   the stale legend copy). Add the boundary case: `0.895` renders
   **"Review · 89 %"**.

**Vocabulary fence**: `officialized` is an ADR word and **never appears on a
screen**.

## Parent story AC covered

- AC-1 Automatic acceptance renders "Accepted automatically · NN%"; human acceptance renders "Accepted by you" with no percentage; everything else "Review · NN%". The two accepted states differ by **label**, never by variant. (A17-3)
- AC-2 Percentages are **floored**: `0.895` reads "Review · 89 %".
- AC-3 The web never contains `90` as a rule; the legend is server-fed.
- AC-4 Reload and a second browser agree, because acceptance no longer lives in React state.
- AC-5 The title reads "N facts need you — you decide".
- AC-6 The CTA gate reads the **decision**, not `< 80`.
- AC-7 `getConfidenceTag` keeps its signature so the three 360 call sites compile untouched.

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/styles/semantics.ts` | modify — `getConfidenceTag` becomes label-only over the three server decisions; `Math.round` → `Math.floor`; the `accent`/"Flagged" branch goes; `isConfidenceBlocking` retired as a rule; docstrings `:9-13`, `:28-31`, `:43-47` rewritten |
| `web/src/routes/contracts/review/reviewViewModel.ts` | modify — the row carries the server `decision`; `fieldTag` (`:279-284`) and `isFieldBlocking` (`:292-295`) read it; the `acceptedThisSession` parameter (`:234`, `:251`) removed; docstrings `:226-229`, `:271-278`, `:286-291` rewritten |
| `web/src/routes/contracts/review/useReviewSession.ts` | modify — delete the `acceptedThisSession` state (`:92`), its setter branch (`:184`) and its use at `:145`/`:147`; `accept()` keeps only the `PATCH` + `load()` path (`:177-182`) |
| `web/src/routes/contracts/review/ReviewFieldList.tsx` | modify — render the decision label; no client-side band computation |
| `web/src/routes/contracts/review/ReviewHeader.tsx` | modify — two-item, server-fed legend (`:89`, `:92`, `:95`); the title stops naming a threshold |
| `web/tests/styles/semantics.test.ts` | modify — rewrite `:15-37`: the three decisions, the two neutral labels distinguished by text, the floor at `0.895` |
| `web/tests/routes/contracts/review/reviewViewModel.test.ts` | modify — rewrite `:192`: the tag and the gate come from the decision, not the percentage |
| `web/tests/routes/contracts/review/ReviewRoute.test.tsx` | modify — rewrite `:492`: the legend is server-fed and has two items; the title carries no threshold |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-012 w17 clause 34 / OQ-w17-002 (CA half)** — render, never recompute.
    Take the operator stash's band collapse (resolve it by message — see
    `## Context`, it is **not** `stash@{2}` any more); **drop its rounded `>=`
    comparison** and its `AUTO_ACCEPT_MIN_PCT` constant.
  - **ADR-012 w17 clause 36** — `acceptedThisSession` retires. The warrant:
    `accept()`'s two branches are **not equal** — `:177-182` is a real network
    write with a read-back, `:184` is a `setState` with **no network call at
    all** — painted identically, the second vanishing on reload. The product has
    retired one of these before (NW-10's `sessionStorage` tracker).
  - **ADR-019 w17 clause 7** — the **first change to ADR-019's confidence rows
    since it was accepted**; its w14 footer §6 deferred them to this wave by
    name. A human decision has **no confidence**, so printing the model's score
    beside a person's judgement asserts that the judgement is uncertain.
  - **ADR-019 w17 clause 10** — ⚠ a correction on the record: §6 declined to
    ratify the bands citing a 2026-09-10 HITL "≥90%" comment in
    `semantics.ts:9-13`. Verified first-hand on this base: **that docstring
    cites spec §7.3's three bands and contains no `90`, no HITL date and no
    supersession sentence.** §6's conclusion was right and its evidence is not
    on `main` (the 90 % comment lives only in the operator stash). Do not go
    looking for it.
  - **ADR-020 w17 §17** — `screens-v2.md:89-91`'s confidence tip quotes the old
    bands verbatim. It is a **ratified divergence**: do **not** restore the
    three-band tip on the grounds that the prototype still shows it.
    (`:87` is the field-list line, not the tip — the anchor used by
    `reports/architecture/waves/w17.md:498` and by `E22/F01/US01/T01`.)
  - **ADR-001 w17 clause 2** — a percentage beside a decision is **floored,
    never rounded up across the bar**, or omitted.
- **Do not touch**: `web/src/routes/contracts/contract360/**` (that is
  `E21/F03/US02/T01` in **this same phase** for `contract360ViewModel.ts`, and
  `E22/F04/US01/T01` in phase 4 for `DetailsSection.tsx`, `WhyClauses.tsx`,
  `FactTable.tsx`); `web/src/api/client.ts` (`E22/F03/US01/T01` owns it **this
  phase**); `web/openapi/raffa-api.v1.json` and `web/src/api/generated/schema.ts`
  (`E20/F01/US01/T01` owns them **this phase**); `semantics.ts:getRiskTag`
  (`:91-96`) — a **different** function on Portfolio and Renewals, not renamed
  and not touched (renaming it would silently change rows no item in this wave
  touches, and would put NW-66 into this file);
  `reviewViewModel.ts:244`'s skip of a null-valued row — that line is
  `E22/F05/US01/T01`'s (NW-64), phase 4.

## Definition of done

- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `cd web && npm test` exits 0
- [ ] `grep -rn "Math.round" web/src/styles/semantics.ts` returns nothing
- [ ] `grep -rn "acceptedThisSession" web/src` returns nothing
- [ ] `grep -rn "AUTO_ACCEPT_MIN_PCT\|>= *90\|> *95\|>= *80" web/src` returns nothing
- [ ] `grep -rn "below 80" web/src` returns nothing
- [ ] `grep -rn "tag-accent\|\"accent\"" web/src/styles/semantics.ts` shows **no** accent branch inside `getConfidenceTag`
- [ ] `grep -rn "officialized" web/src` returns nothing
- [ ] `cd web && npx tsc --noEmit` still passes with `web/src/routes/contracts/contract360/` **unmodified** — proving `getConfidenceTag` kept a compatible signature

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the three decisions map to the three labels; `auto_accepted` and `human_accepted` share `.tag-neutral` and differ only in text; `human_accepted` carries **no** percentage; a `review_required` field at `0.895` reads "Review · 89 %" | `web/tests/styles/semantics.test.ts` |
| unit | `fieldTag` and `isFieldBlocking` read the server decision; a `< 80` field the server accepted does **not** block; `buildReviewFields` no longer takes a session set | `web/tests/routes/contracts/review/reviewViewModel.test.ts` |
| component | the legend has two items and its threshold text comes from the response; the title reads "N facts need you — you decide"; a re-render with the same server payload paints the same tags (nothing is session-held) | `web/tests/routes/contracts/review/ReviewRoute.test.tsx` |

## Open questions blocking this task

- **none blocking.** OQ-w17-002's client half is ruled. OQ-w17-cl-02 (the
  colour-as-decision gate at `contract360ViewModel.ts:575`) is ruled and is
  **`E22/F04/US01/T01`'s** to fix — this task must not open that file.

## Wave-spec entry

```yaml
- id: E22/F01/US02/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-01-auto-accept-decision/us-02-review-renders-the-decision/tasks/task-01-review-renders-the-decision.md
  produces: [auto-accept-web]
  depends_on: [api-contract-a]
  effort: M
  layer: frontend
  status: live
```
