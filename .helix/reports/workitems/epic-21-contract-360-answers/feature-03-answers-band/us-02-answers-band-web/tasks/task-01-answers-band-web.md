---
id: E21/F03/US02/T01
type: task
story: us-02-answers-band-web
wave: w17
status: live
target_repo: raffa-web
---

# task-01-answers-band-web — add the strategy fetch to the 360 and map its two sections

## Context

**Closes: NW-62 (the web half).**

Decision row: `reports/architecture/waves/w17.md` — the **NW-62** row
(client-architect and ux-ui-designer halves), plus the rulings on
**OQ-w17-005**, **OQ-w17-sa-01** and the recorded correction in **ADR-020 w17
§25**.

ADRs in force: **ADR-012** w17 clauses 35, 39 and 41, and §48; **ADR-020** w17
§14(f)-(g) and §25; **ADR-001** w17 clause 4; **ADR-024** w17 clause A4;
**ADR-018** w15 clause 6 (the prohibition on a "re-indexing" banner).

Already on `main`, so **not** work to build: the parallel-GET block
(`web/src/routes/contracts/contract360/index.tsx:115-119`), the degrade rule
documented at `:47-50`, `AnswersBand.tsx` with its three cells (`:55`, `:61`,
`:78`) and `answerDisplayClass` (`:33-34`). Landed in phase 2: the real band on
`/strategy` and the `getContractStrategy` wrapper in `web/src/api/client.ts`.

⚠ **This task is the phase-3 writer of
`web/src/routes/contracts/contract360/contract360ViewModel.ts`**
(single-writer constraint 3). **`E22/F04/US01/T01`** is its phase-4 writer
(`reports/plan/slices/w17.yaml:33`). **Do not open `web/src/api/client.ts`** —
`E22/F03/US01/T01` owns it this phase.

⚠ **`web/e2e/w17-answers.spec.ts` does not exist on this base** (`web/e2e/`
holds `day1.spec.ts`, `invite.spec.ts`, `v2.spec.ts`). **This task creates it**
and is its only writer in the wave — ADR-012 w17 clause 39 keeps `v2.spec.ts`
for NW-73's sweep alone and lands every new W17 case in a per-theme spec, and
clause 41 assigns **N16** to this one.

## Coding objective

In `raffa-web`, make the answers band render the server's answer.

1. **Fetch it.** In `web/src/routes/contracts/contract360/index.tsx`, add
   `getContractStrategy` to the existing `Promise.all` at `:115-119`. It is an
   **optional** source under the rule already documented at `:47-50`: a failure
   degrades **its own answer** to an honest "not yet" and does **not** fail the
   screen.
2. **Map it, do not compute it.** In `contract360ViewModel.ts`, change
   `buildAnswers` (`:160`) so **Where you can save** and **When you must move**
   come from the strategy pack's `whereYouCanPush` and `whenYouMustMove`. The
   function **maps**; it must not compute a second answer. Delete the
   `upliftLever` fallback chain at `:169-175` where it now double-sources the
   same question.
3. **Three real states per cell** (ADR-020 w17 §14(f)):
   - **(i)** a figure and its lever;
   - **(ii)** a **representative** band, with its provenance on the `save.lever`
     detail line (`AnswersBand.tsx:57`) and **never bare** — reuse
     `inputs/design/prototypes/raffa-v2/ia-v2.md:110`'s existing Ask vocabulary
     (`adapter A, n = 214`); do **not** invent a second vocabulary for the same
     idea;
   - **(iii)** **no answer yet**, naming what is missing **and the way to get
     it**. ⚠ A placeholder that names neither is the defect, not the absence.
4. **Keep the constants, narrow their reach.**
   `SAVINGS_NOT_YET_AVAILABLE` (`:150`) and `LEVER_NOT_YET_AVAILABLE`
   (`:151-152`) survive **only** as the absent-or-failed state of a source that
   was **genuinely called**. ⚠ A test asserting merely that "the constant is
   gone" does **not** catch the defect; assert that the strategy call **was
   made** and that the constant appears only when it returned nothing or failed.
5. **The composed state for a missing date.** When "When you must move" has no
   date because extraction missed the fact, the cell reads **"Add the end
   date"** and links to `/contracts/:contractId/review` — **never "Not
   determined"**. The two items compose: the recovery affordance itself is
   `E22/F04/US01/T01`'s section, in phase 4.
6. **No `OpenWeakFacts`, no "weak".** Render the strategy sections but **not**
   `OpenWeakFacts` as a review statement, and use the word "weak" nowhere on
   this screen. ⚠ It is filtered at `0.8` while Review moves to `0.90`, so it
   would be the **only** place on a screen that renders no confidence at all
   where a **hidden** threshold decides what the user sees — invisible,
   unlabelled, and silently contradicting the 90 % bar Review states out loud.
7. **No component, no CSS.** `AnswersBand.tsx`'s structure and
   `web/src/routes/contracts/contract360/contract360.css` are untouched except
   for copy and the cell contents.
8. **Create `web/e2e/w17-answers.spec.ts` — the wave's answers-theme spec**
   (ADR-012 w17 clauses 39 and 41). It carries **N16**'s browser half: on a
   validated contract, **Where you can save** and **When you must move** both
   render **concrete** with their citation/provenance, and — the case clause 41
   makes explicit — `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE`
   appear **only** where a real source is genuinely absent, never as the screen's
   default. ⚠ Assert the **rendered** state, not the constant's absence: a spec
   that only greps for the string passes on a screen that never called
   `/strategy` (AC-3, and the same trap step 4 names for the unit tests).
   ⚠ **Do not open `web/e2e/v2.spec.ts`** — clause 39 reserves it for NW-73's
   stale-prose sweep (`E20/F02/US02/T01`, phase 2), and it is the collision that
   rule exists to prevent. Per clause 31 and ADR-012 §12 this spec is
   **acceptance-runbook evidence, not a CI gate**: no workflow runs Playwright,
   and no line of this task may present it as one.

⚠ **Do not add a "re-indexing" / "catching up" banner** to this screen for any
reason. Nothing requeues after a corpus delete, so it would be a *not ready yet*
claim that can never resolve (ADR-018 w15 clause 6). **No corpus copy and no
corpus state ships this wave** (ADR-020 w17 §25).

## Parent story AC covered

- AC-1 **Where you can save** shows either a figure with its lever, or a **representative** band whose provenance is on the detail line — never bare. (N16)
- AC-2 **When you must move** shows the notice deadline, days left and auto-renewal. (N16)
- AC-3 The two constants are reachable **only** when the strategy fetch was genuinely made and returned nothing or failed.
- AC-4 A missing end date reads **"Add the end date"** and links to Review — never "Not determined".
- AC-5 A failing strategy fetch degrades its own answer and does **not** fail the screen.
- AC-6 The 360 has **exactly one** answer source.
- AC-7 No copy uses the word **"weak"**, and `OpenWeakFacts` is not rendered as a review statement.
- AC-8 No new component and no CSS change.

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/contracts/contract360/index.tsx` | modify — add the strategy fetch to the `Promise.all` at `:115-119` as an optional source |
| `web/src/routes/contracts/contract360/contract360ViewModel.ts` | modify — `buildAnswers` maps the strategy pack; the double-sourced `upliftLever` chain at `:169-175` goes; the three states; the "Add the end date" copy |
| `web/src/routes/contracts/contract360/AnswersBand.tsx` | modify — copy and the provenance detail line only; no structural change |
| `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` | modify — the three states, and that the constants appear **only** after a real call |
| `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` | modify — a failing strategy fetch degrades one answer and renders the rest of the screen |
| `web/e2e/w17-answers.spec.ts` | **new** — N16: both answer cells concrete with their citation/provenance, and the "not yet available" constants reachable **only** where a source is genuinely absent (ADR-012 w17 clauses 39, 41) |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-012 w17 clause 35** — **one answer source per screen.** Either the 360 reads `/strategy` or the server composes the answer into the 360 payload; **two sources for one question on one screen is not acceptable.**
  - **ADR-012 w17 §48** — a document with no embedding corpus renders the 360's "not yet available" constants, and that is **correct** under clause 35: the source was genuinely called and genuinely has nothing. **No special case and no client task for it.**
  - **ADR-020 w17 §14(f)-(g)** — the three states, the never-bare provenance, and the composed "Add the end date".
  - **ADR-020 w17 §25** — ⚠ a **premise correction**: the 360 reads structured facts and the evidence trail, **not** the embedding corpus, so a destroyed corpus leaves this screen "complete, populated and confident". `SAVINGS_NOT_YET_AVAILABLE` is the **benchmark**'s constant and `:152` asserts the clauses below are **validated** — firing it for a missing corpus would be worse than silence. Ship no corpus copy and no corpus state.
  - **ADR-024 w17 clause A4** — `OpenWeakFacts` stays unrendered; the bars reconcile in W18.
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:97-101` — the
  Answers band is *Where you can save* (estimate + lever), *When you must move*
  (notice deadline, days left, auto-renews), *What to do* (action + rationale +
  primary button). The provenance vocabulary anchor is `ia-v2.md:110`. ⚠ The
  export is not re-drawn by this wave: **do not restore anything the council
  removed** on this screen.
- **Do not touch**: `web/src/api/client.ts` (`E22/F03/US01/T01` owns it this
  phase — the wrapper it needs landed in phase 2);
  `web/src/routes/contracts/contract360/DetailsSection.tsx`, `FactTable.tsx`,
  `WhyClauses.tsx`, `ClauseHighlight.tsx` (**`E22/F04/US01/T01`**'s, phase 4);
  `web/src/styles/semantics.ts` (`E22/F01/US02/T01`'s, this phase);
  `web/openapi/raffa-api.v1.json` and `web/src/api/generated/schema.ts`
  (`E20/F01/US01/T01`'s this phase); `web/e2e/v2.spec.ts` (NW-73's sweep,
  `E20/F02/US02/T01`, phase 2 — ADR-012 w17 clause 39) and every other
  `web/e2e/*.spec.ts`: **this task writes `w17-answers.spec.ts` and nothing else
  under `web/e2e/`**.
- ⚠ `computeNeedsAttention` (`:568`) and `NO_ATTENTION_MESSAGE` (`:586`) are in
  the same file but belong to **`E22/F04/US01/T01`** in phase 4. **Do not change
  them** — and do not "tidy" them while you are in the file.

## Definition of done

- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npm test` exits 0
- [ ] `cd web && npx playwright test e2e/w17-answers.spec.ts` exits 0
- [ ] `git diff --stat origin/main -- web/e2e/v2.spec.ts` is **empty** for this task's commits (ADR-012 w17 clause 39 — that file is NW-73's sweep alone)
- [ ] `grep -n "upliftLever" web/src/routes/contracts/contract360/contract360ViewModel.ts` returns nothing (the dead double-source is gone)
- [ ] `grep -rni "weak" web/src/routes/contracts/contract360/` returns nothing
- [ ] `grep -rn "openWeakFacts" web/src/routes/contracts/` returns nothing
- [ ] `grep -rn "Not determined" web/src/routes/contracts/contract360/` returns nothing
- [ ] `grep -rni "re-indexing\|catching up" web/src/routes/contracts/ web/src/routes/ask/` returns nothing
- [ ] `grep -n "getContractStrategy" web/src/routes/contracts/contract360/index.tsx` returns a hit inside the `Promise.all`
- [ ] `git diff --stat origin/main -- web/src/api/client.ts` is **empty** for this task's commits
- [ ] a reviewer reading `contract360ViewModel.test.ts` finds a case asserting the constant appears **only** when the strategy source was called and answered nothing

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | state (i): a figure and its lever render; state (ii): a representative band renders **with** adapter and sample size on the detail line; state (iii): the missing-fact cell names the way to get the fact | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | the two constants appear **only** when the strategy source was called and returned nothing — not when it was never called | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | a missing end date yields "Add the end date" with a link to Review, and never "Not determined" | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| component | a rejected strategy fetch degrades that one answer and still renders the header, Why and Details | `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` |
| e2e | **N16** — both answer cells render concrete with their citation/provenance on a validated contract, and the two "not yet available" constants appear **only** where a source is genuinely absent (clause 41's explicit case). Runbook evidence, **not** a CI gate — no workflow runs Playwright | `web/e2e/w17-answers.spec.ts` (**created by this task**) |
| manual (`dev`) | N16: both answers concrete with citations on a validated Northwind contract | `docs/waves/w17-acceptance.md`, written by `E22/F06/US01/T01` |

## Open questions blocking this task

- **none blocking.** OQ-w17-005 and OQ-w17-sa-01 are ruled; OQ-w17-ux-05 is open
  but explicitly ships **no** copy, signal or state this wave.

## Wave-spec entry

```yaml
- id: E21/F03/US02/T01
  prompt: reports/workitems/epic-21-contract-360-answers/feature-03-answers-band/us-02-answers-band-web/tasks/task-01-answers-band-web.md
  produces: [contract360-answers-web]
  depends_on: [strategy-benchmark-wired, renewal-market-position]
  effort: M
  layer: frontend
  status: live
```
