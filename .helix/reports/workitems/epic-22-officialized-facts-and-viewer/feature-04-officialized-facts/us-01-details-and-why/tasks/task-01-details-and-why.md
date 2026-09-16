---
id: E22/F04/US01/T01
type: task
story: us-01-details-and-why
wave: w17
status: live
target_repo: raffa-web
---

# task-01-details-and-why — Contract 360 shows only officialized facts, and the Why row speaks leverage

## Context

**Closes: NW-65, NW-66.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-65** row (client-architect
and ux-ui-designer halves) and the **NW-66** row (product-owner, client-architect and
ux-ui-designer halves), plus the rulings on **OQ-w17-cl-02** and **OQ-w17-ux-01**.

ADRs in force: **ADR-001** w17 clause 6; **ADR-012** w17 clause 36; **ADR-018** w17
clause 10; **ADR-019** w17 clauses 9, 11, 12; **ADR-020** w17 §14(a)–(e), §17.

⚠ **This task is `contract360ViewModel.ts`'s writer in phase 4.** Its phase-3 writer
is `E21/F03/US02/T01` (NW-62, `buildAnswers`). NW-71 does **not** open this file
(ADR-012 w17 clause 36a) and NW-20 gets **no client task** (clause 45), so the
contender set is exactly three and they are separated by phase — not by hope.

⚠ **This task also owns `FactTable.tsx`.** It was the one finding of the w17 table
that no ADR closed (ADR-014 w17 clause 7.7), reached independently by
client-architect, ux-ui-designer and delivery-manager. Unassigned, NW-65's
officialized gate would ship on **Key terms alone** while Products, Obligations and
Risks still render unofficialized values and confidence tags — a screen inconsistent
with itself, which is worse than either end state.

Already on `main`, so **not** work to redo: the route
`/documents/:documentId/viewer` (registered in phase 3 by `E22/F03/US01/T01`); the
persisted per-field `decision` (phase 2, `E22/F01/US01/T01`) which the count line
reads; `ClauseHighlight` itself, which already renders the original wording
(`web/src/routes/contracts/contract360/ClauseHighlight.tsx:28-36`).

## Coding objective

In `raffa-web`, make Contract 360 a page of facts the product can stand behind, in
negotiation language.

1. **Delete the "facts you still need to decide" block.**
   `web/src/routes/contracts/contract360/DetailsSection.tsx:78` (`<h6>Facts you still
   need to decide</h6>`, verified present on this checkout) goes, together with the
   attention rows at `:83-93`. **"Review all →" (`:79-81`) stays**, re-homed as the
   column's single trailing line carrying a count: **"N facts still need you — Review
   all →"**. ⚠ **When N = 0 the line does not render at all** — no sentence, no
   placeholder.
2. **Delete `NO_ATTENTION_MESSAGE`** (`web/src/routes/contracts/contract360/contract360ViewModel.ts:586`,
   verified: `"None — every fact is above 95% or signed off by you."`). Its
   *"or signed off by you"* half is **untrue on this checkout** — `computeNeedsAttention`
   (`:568-583`) reads **no human-decision state at all**. ⚠ **Do not reword it.** When
   the system does not hold a claim the honest repair is to stop making it, and with
   AC-1's zero case the sentence has no place left to render.
3. ⚠ **Rewrite the gate: it is a colour check today** (**OQ-w17-cl-02**).
   `contract360ViewModel.ts:575` tests `tag.variant !== "neutral"`, inferring a
   decision from **presentation**, against `web/src/styles/semantics.ts:45-46`'s
   explicit instruction not to. Today that happens to mean "at or below 95 %"; NW-71
   (phase 2/3) collapses the bands, so left as-is this line **silently changes which
   facts the list counts with nobody editing it**. The count reads the **server's
   persisted `decision`** (`review_required`), never a tag variant and never a
   percentage. This task rewrites the function anyway — but unnamed, working code is
   preserved verbatim.
4. **The officialized gate keeps every row.** A value that is not officialized is not
   rendered *as a value*; **its row stays** and the value shows the **em-dash
   placeholder already in this view model's vocabulary**
   (`contract360ViewModel.ts:375-400`, `:387`, `:391`, `:392`). Deleting rows would
   hide contract content, and a user cannot tell an absent product line from an
   unextracted one. ⚠ The sparse-column risk is the **filter, not the deletion**:
   `contract360.css:391-394` is `repeat(2, minmax(0,1fr))` with a full-width third
   child (`:400-402`) and the columns are **independent grid items**, so removing the
   second half of column 2 leaves *Key terms | Documents* intact.
5. **Extend the gate to Products, Obligations and Risks**, which render through
   `web/src/routes/contracts/contract360/FactTable.tsx` — and **remove its
   `getConfidenceTag` call at `:35`**. **No confidence tag renders anywhere on
   Contract 360** (ADR-019 w17 clause 11). Its docstring is at **`FactTable.tsx:11-15`**
   ("Term / Value / Source / Confidence list used inside the 'Details ▾' drawer…")
   and is rewritten **with** the code — it names a Confidence column this task
   removes. ⚠ **Not `:26-31`**: that is the `<tr><th>` header row in this file, and
   `:26-31` is `DetailsSection.tsx`'s docstring anchor (which does quote "facts
   still to decide"). Do not edit the header row expecting a comment.
6. **The Why row becomes: type · officialized value · leverage tag · one-line why ·
   "Open in document viewer".** In
   `web/src/routes/contracts/contract360/WhyClauses.tsx`, the **source cell (`:54`)
   and the confidence tag (`:56`, computed at `:40`) go**; the row keeps `:51` and
   `:52`. The docstring at `:16-19` quotes `screens-v2.md` #5 verbatim and is
   rewritten with the row.
7. **Leverage words, exactly three** (product-owner's; ux-ui-designer's four-word set
   was withdrawn at the table): **Push to change** (Critical, High) · **Worth
   raising** (Medium) · **Standard terms** (Low). The raw `ContractRiskLevel` enum
   string **never reaches the screen**.
   ⚠ **Label-only change.** `getClauseRiskTag` (`contract360ViewModel.ts:255-259`)
   already computes `High || Critical → accent`, everything else → `neutral`
   (verified at `:257`); **the variant expression stays byte-identical** and Medium
   and Low keep sharing `.tag-neutral`, distinguished by their words. Its docstring
   (`:254`) already says "Text first, colour only as emphasis."
   ⚠ **A null risk level stays null** (`:256` returns `null`): "Standard terms" is a
   statement *about the clause*, so synthesising it where risk was never determined
   invents the kind of fact this wave exists to stop.
8. ⚠ **Scope fence — do not rename `semantics.ts:getRiskTag`** (`:91-96`, "High
   risk" / "Medium risk" / "Low risk"). It is a **different function on Portfolio and
   Renewals**; renaming it would silently change rows no item in this wave touches
   **and** would put this task into `web/src/styles/semantics.ts`, which the wave
   reserves for NW-71. The leverage map lives in `contract360ViewModel.ts`.
9. **The quote moves into the existing `ClauseHighlight`** (`WhyClauses.tsx:64`) —
   the "specchietto" is **not a new component**. `formatSource`
   (`contract360ViewModel.ts:361-367`, which joins an **uncapped** `sourceSpan`)
   **stops feeding the row**; the short `p.N · §` reference lives in the highlight's
   header and **adopts Review's existing 60-character cap**
   (`web/src/routes/contracts/review/ReviewFieldList.tsx:84`, `:92-96`) — **not a
   second truncation rule for a neighbouring screen**.
10. **A three-term legend** renders in the hint slot the component already has
    (`WhyClauses.tsx:29`, today "Click a clause to read the original wording"),
    because the words no longer name a risk level and a vocabulary the user must
    infer means nothing.
11. **"Open in document viewer" links to
    `/documents/:documentId/viewer?page=<sourcePage>&clause=<clauseId>`** — the route
    `E22/F03/US01/T01` registers in phase 3, reused **verbatim**, never a second
    scheme (ADR-018 w17 clauses 9–10). The `documentId` comes from the clause's own
    document; a clause with no resolvable document or no `sourcePage` renders the row
    **without** the link rather than with a dead one.

**No new component, no new token, no new CSS variable** (ADR-019 w17 clause 12).
`web/README.md:630` still describes the deleted block — README hygiene is standing
implementer scope, swept there, not listed below.

## Parent story AC covered

- AC-1 The "Facts you still need to decide" block is **gone** from Details. In its place, column 2 ends with a single line — **"N facts still need you — Review all →"** — and **when N = 0 the line does not render at all**. (N19)
- AC-2 No sentence on the page claims a fact was "signed off by you".
- AC-3 **Every** product/term row still renders. An unofficialized value renders the **em-dash placeholder already in this view model's vocabulary** — a row is never dropped.
- AC-4 The officialized gate applies to **Key terms, Products, Obligations and Risks** — not to Key terms alone.
- AC-5 **No confidence tag renders anywhere on Contract 360.** (N19, N20)
- AC-6 A Why row shows type · officialized value · a **leverage** tag · a one-line why · **Open in document viewer**. The **source cell and the confidence tag are gone**. (N20)
- AC-7 The leverage words are **Push to change** (Critical/High) · **Worth raising** (Medium) · **Standard terms** (Low). The raw `ContractRiskLevel` enum never reaches the screen, and a **null** risk level stays **null**.
- AC-8 A three-term **legend** renders in the hint slot the component already has.
- AC-9 The original quote appears **only** inside `ClauseHighlight`, and the short `p.N · §` reference adopts **Review's existing 60-character cap**.
- AC-10 **Open in document viewer** links to `/documents/:documentId/viewer?page=<sourcePage>&clause=<clauseId>` and the link **lands**. (N20)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/contracts/contract360/DetailsSection.tsx` | modify — delete `:78`'s heading and the attention rows `:83-93`; keep "Review all →" (`:79-81`) as the column's single trailing count line, absent when N = 0; rewrite the docstring `:26-31` |
| `web/src/routes/contracts/contract360/contract360ViewModel.ts` | modify — `computeNeedsAttention` (`:568-583`) counts on the server `decision`, never on `tag.variant` (`:575`); delete `NO_ATTENTION_MESSAGE` (`:586`); `getClauseRiskTag` (`:255-259`) returns the three leverage labels with the variant expression **byte-identical** and null still null (`:256`); the officialized gate over Key terms / Products / Obligations / Risks keeps every row on the em-dash idiom (`:375-400`); `formatSource` (`:361-367`) stops feeding the Why row; the viewer link target |
| `web/src/routes/contracts/contract360/WhyClauses.tsx` | modify — drop the source cell (`:54`) and the confidence tag (`:56`, computed `:40`); add the leverage tag, the one-line why and "Open in document viewer"; three-term legend in the existing hint slot (`:29`); rewrite the docstring `:16-19` |
| `web/src/routes/contracts/contract360/ClauseHighlight.tsx` | modify — the header carries the short `p.N · §` reference capped at 60 characters, Review's existing cap; the quote renders here and nowhere else |
| `web/src/routes/contracts/contract360/FactTable.tsx` | modify — the officialized gate for Products / Obligations / Risks; remove the `getConfidenceTag` call at `:35`; rewrite the docstring at **`:11-15`** (not `:26-31`, which is the `<th>` header row) |
| `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` | modify — rewrite the test-locked cases at `:402` and `:452`; the count comes from the decision, not a variant; a null risk level stays null; the em-dash keeps unofficialized rows |
| `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` | modify — no confidence tag anywhere on the screen; the count line is absent at N = 0; the Why row carries a leverage word and a viewer link |
| `web/e2e/w17-viewer.spec.ts` | modify — **N20** appended (the file is created in phase 3 by `E22/F03/US01/T01`): a clause row shows no quote and no percentage, carries a leverage word, and its link **lands on the cited page** |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Design oracle and the anchors this task implements** — `Raffa V2 Prototype.html`
  (unpacked: `inputs/design/prototypes/raffa-v2/screens-v2.md`):
  - **`screens-v2.md:103-104`** — *"**Details ▾** (\"All terms, documents and open
    facts ▾\"): key terms table, documents in family, **facts still to decide**."*
    ⚠ **Ratified divergence** (ADR-020 w17 §17 row 4): the third block is deleted.
  - **`screens-v2.md:101-102`** — *"**Why**: 2–3 clauses (type · normalized · page § ·
    risk · **confidence**), click → original wording highlighted (hl; citation
    landing…)"*. ⚠ **Ratified divergence** (§17 row 3): confidence **and** the source
    cell leave the row; a leverage word replaces the raw enum.
  - `inputs/design/prototypes/design-system.md:59` — `| Risk High | .tag-accent;
    Medium/Low .tag-neutral |`. **This row does not move**; the change is the label.
  - `inputs/design/prototypes/design-system.md:35` (`.tag` variants) and `:48`
    (facts vs AI) — the locked catalogue this task composes from.
  - ⚠ **A task must not "restore" the export**: do not re-add the confidence tag to
    the Why row or the third Details block on the grounds that the prototype shows
    them. `inputs/**` is the operator's and is **never edited**; re-export (16) and
    (17) are owed and block nothing (ADR-020 w17 §18).
- **Architecture decisions in force**:
  - **ADR-020 w17 §14(a)-(d)** — delete the block, re-home "Review all →" with a
    count, keep every row on the em-dash placeholder; **§14(e)** — the Why row's new
    shape and the legend; **§17** — the two ratified divergences above.
  - **ADR-019 w17 clause 9** — the relabel is **label-only**; the variant expression
    is byte-identical and the precedent is already in the file. **Clause 11** — no
    confidence tag renders on Contract 360 at all; on a 360 fact row `.tag-neutral`
    means *leverage*, on Review it means *accepted*, and no screen has both meanings
    in play at once. **Clause 12** — no new token and no new component.
  - **ADR-012 w17 clause 36** — the writer math (`getConfidenceTag` keeps its
    signature, so this task deletes the two 360 call sites rather than NW-71
    reaching into this file) and the OQ-w17-cl-02 colour-check defect.
  - **ADR-018 w17 clause 10** — three surfaces link to the viewer; **NW-66 lands
    after NW-63** because e2e N20 asserts the link *lands*.
  - **ADR-001 w17 clause 6** — confidence lives in Review; Contract 360 never renders
    it. Appendix C (`product-spec.md:954`) is superseded **in its rendering half, on
    360 only**; it stands verbatim in Review and in Ask and its **storage half is
    untouched**.
- ⚠ **Vocabulary fence**: `officialized` is an **ADR word** and **never appears on a
  screen**.
- ⚠ **No "re-indexing" / "catching up" banner** may be added to this screen
  (ADR-020 w17 §25): nothing requeues after ADR-011 w17 clause 26, so it would be a
  *not ready yet* claim that can never resolve — already forbidden by ADR-018 w15
  clause 6.
- **Do not touch**: `web/src/styles/semantics.ts` (NW-71's, phase 3 — including
  `getRiskTag` at `:91-96`); `web/src/routes/contracts/contract360/AnswersBand.tsx`
  and `buildAnswers` (`contract360ViewModel.ts:160-199`) — NW-62's, phase 3;
  `web/src/routes/contracts/review/**` (NW-71 phase 3, NW-64 this phase);
  `web/src/api/client.ts`; `web/openapi/raffa-api.v1.json`; `web/package.json` (five
  runtime dependencies, unchanged).

## Definition of done

- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npm test` exits 0
- [ ] `cd web && npx playwright test e2e/w17-viewer.spec.ts` exits 0 (N17 from phase 3 and N20 from this task)
- [ ] `grep -rn "getConfidenceTag" web/src/routes/contracts/contract360/` returns **nothing**
- [ ] `grep -rn "NO_ATTENTION_MESSAGE\|signed off by you\|Facts you still need to decide" web/src/` returns **nothing**
- [ ] `grep -rn "tag.variant" web/src/routes/contracts/contract360/contract360ViewModel.ts` returns **nothing** (the gate reads the decision, not a colour)
- [ ] `git diff --stat origin/main -- web/src/styles/semantics.ts` shows **no change attributable to this task** — the leverage map lives in `contract360ViewModel.ts`, and `getRiskTag` is **not** renamed (`grep -n "getRiskTag" web/src/styles/semantics.ts` still returns its three "High risk"/"Medium risk"/"Low risk" labels)
- [ ] `grep -rn "Push to change\|Worth raising\|Standard terms" web/src/routes/contracts/contract360/contract360ViewModel.ts` returns all three
- [ ] `grep -rn "documents/.*\/viewer?page=" web/src/routes/contracts/contract360/` shows the link built with both `page` and `clause`
- [ ] `git diff --stat origin/main -- web/package.json` is **empty** (still five runtime dependencies)
- [ ] on `dev`: **N19** — Details carries no "facts still to decide" list; a contract with every fact decided shows **no** trailing line; a contract with two undecided facts shows "2 facts still need you — Review all →"
- [ ] on `dev`: **N20** — a Why row shows type, normalized value, page · section and exactly one leverage tag in those words; **no percentage and no raw enum string anywhere on Contract 360**; the full quote is one click away and is never rendered untruncated in the row

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `computeNeedsAttention` counts on the server `decision` and **not** on `tag.variant`; collapsing the confidence bands does not change the count | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | an unofficialized value keeps its row and renders the em-dash; **no row is dropped** | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | `getClauseRiskTag` returns the three leverage labels, the variant expression is unchanged (`High`/`Critical` → `accent`), and a **null** risk level stays `null` | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | the short reference is capped at 60 characters and the untruncated quote never reaches the row | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| component | no confidence tag renders anywhere on Contract 360; the trailing count line is absent at N = 0; the Why row carries the leverage word, the legend and the viewer link | `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` |
| e2e | **N20** — a clause row shows no quote and no percentage, carries a leverage word, and its link **lands on the cited page** | `web/e2e/w17-viewer.spec.ts` (created in phase 3, extended here) |
| manual (`dev`) | **N19** and **N20** as written above | `docs/waves/w17-acceptance.md`, written by `E22/F06/US01/T01` |

## Open questions blocking this task

- **none blocking.** **OQ-w17-cl-02** is ruled (rewrite the gate to read the
  persisted decision, never a colour). **OQ-w17-ux-01** is closed — product-owner's
  three leverage words are adopted and this seat's four-word set was withdrawn at the
  table. **OQ-w17-sa-01**'s UX half (`OpenWeakFacts` renders no review statement and
  no copy uses the word "weak") lands on `E21/F03/US02/T01`, not here.
- ⚠ **Ordering, not a question**: this task runs **after** `E22/F03/US01/T01`
  (the route must be registered, not merely ADR-decided) and **after**
  `E22/F01/US02/T01` (the persisted decision is what makes the count line true).

## Wave-spec entry

```yaml
- id: E22/F04/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-04-officialized-facts/us-01-details-and-why/tasks/task-01-details-and-why.md
  produces: [contract360-officialized-facts]
  depends_on: [document-viewer, auto-accept-web, contract360-answers-web]
  effort: L
  layer: frontend
  status: live
```
