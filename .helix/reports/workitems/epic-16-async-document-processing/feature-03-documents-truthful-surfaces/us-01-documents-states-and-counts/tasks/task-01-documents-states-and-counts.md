---
id: E16/F03/US01/T01
type: task
story: us-01-documents-states-and-counts
wave: w15
status: live
target_repo: raffa-web
---

# task-01-documents-states-and-counts — Server-read counts, the refusal row, the stopped poll, the rail badge and three gated screens

## Coding objective

Make every number and every wait on the web read a server fact. On the Documents
screen: replace `allCount`/`attentionCount` (`useDocumentsList.ts:122-123`, a
client filter over a page capped at 100 at `:15-18`) with ADR-027 §D7's
`counts`; add the third chip `Not added · K`; render the `Rejected` row with the
label and tag lifted from the card being retired; hand the optimistic row off on
evidence instead of at `:140-141`'s drop-then-load; and give the 2 s poll
(`:110-119`) a no-change budget with an explicit resume.

Delete `documentStore.ts` outright and give `RailNav` a `useDocumentCounts` hook
over `listDocuments(pageSize: 1)` reading the same `counts` — `RailNav.tsx:61-66`
still claims "There is still no `GET /api/documents` collection endpoint", which
has been stale since epic-13 shipped it.

Then close the completeness gate on the three screens that have none: Ask's
`buildOffCopy` widens from a boolean to a three-way state, Portfolio gains a
third variant with no new string, and Contract 360 gains a fifth state driven by
ADR-027 §D8's `readiness` and **never** inferred from an empty clause array —
that array is also what a genuinely factless contract returns.

## Parent story AC covered

- AC-1 … AC-11 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/documents/useDocumentsList.ts` | counters read `counts` (`:122-123`); the optimistic entry is dropped only when a server row with the 201's id is present (`:140-141`); the poll gains a five-minute no-change budget and a resume (`:105-119`); the `status` fetch for the third chip |
| `web/src/routes/documents/DocumentStatusTable.tsx` | the `Rejected` row, its hint and empty action cell; **`:94-96`'s local-entry ternary gains a third branch**; "Queued…" for a null server stage (`:155`); the list-level stopped-updates notice using the `.hint` + `.btn-secondary` pair already at `:100-103` |
| `web/src/routes/documents/documentTable.ts` | `RowStatus` gains `"rejected"`; `isAttentionStatus` (`:141-143`) mirrors the server's *not `Completed` and not `Rejected`*; `buildKbSummary` (`:154-160`) becomes a function of `counts` and loses its "M askable" segment |
| `web/src/styles/semantics.ts` | **one** semantic row: `Rejected → .tag-outline`, label "Not added". `:52`'s map is the second file of the three-site edit below |
| `web/src/routes/documents/uploadPipeline.ts` | `LocalUploadEntry.phase` gains `"rejected"`; the oversize check (`:105-108`) and 413/415 (`:122-125`) produce a local row; **`:123`'s `result.error ?? …` stops rendering API prose**; the `"Not added: "` lead-in is removed inside the two copy functions (`:39-52`); the client-owned upload deadline |
| `web/src/routes/documents/UploadResultCard.tsx` | **deleted**, together with `RejectedFileOutcome`, `rejected`, `dismissRejected` and `onDismissRejected` |
| `web/src/routes/documents/AttentionFilter.tsx` | the third chip, rendered only while `counts.rejected > 0` (`:22-27`) |
| `web/src/routes/documents/index.tsx` | wires the above; removes the card's slot |
| `web/src/routes/documents/documentStore.ts` | **deleted outright** — `rememberDocument` has zero production call sites |
| `web/src/components/shell/useDocumentCounts.ts` | **new** — mirrors `useValidatedContractCount.ts`'s shape over `listDocuments(pageSize: 1)` |
| `web/src/components/shell/RailNav.tsx` | `:67`'s `loadTrackedDocuments()` → the new hook; delete the stale comment at `:61-66` |
| `web/src/components/shell/navItems.ts` | the badge rule (`:84-107`); the honest-absence rule is unchanged |
| `web/src/components/shell/AppShell.tsx` | pass the counts down the `Outlet` context beside the existing contract count (`:59`) |
| `web/src/routes/ask/askViewModel.ts` | `buildOffCopy(hasAnyDocument: boolean)` (`:250-261`) widens to a three-way state |
| `web/src/routes/ask/index.tsx` | the gate branches on the server's not-yet-terminal count, not `page.totalCount > 0` (`:99-105`); the one-shot fetch at `:100-105` becomes a 2 s re-read while the gate is off, under the shared budget |
| `web/src/routes/contracts/index.tsx` | the third Portfolio variant replacing "Nothing to triage yet" (`:130-139`) |
| `web/src/routes/contracts/contract360/index.tsx` | the fifth state, driven by `readiness` (`:72-117` load, `:229-255` render) |
| `web/src/routes/contracts/contract360/contract360ViewModel.ts` | the two readings of `readiness.state` |
| `web/tests/` and the co-located `*.test.ts(x)` files | the vitest cases below |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-61 (web half), NW-10, NW-27 (states half), NW-69 (the shared poll
budget's Documents surface)`.

Decision rows: `reports/architecture/waves/w15.md` — **NW-61** (client-architect
and ux-ui-designer cells), **NW-27** (ux-ui-designer cell), **NW-10** (`task
only` — ADR-012's w14 footer already decided it).

- **Architecture decisions in force**: **ADR-012** w15 §4–§6, §10, §13.4–§13.8,
  §17–§18; **ADR-018** w15 clauses 1–3 and 6; **ADR-020** w15 §1, §2, §6, §8;
  **ADR-019** w15 clauses 1–2 and 4–6; **ADR-024** (Ask answers only from
  validated facts).
- **Design oracle and anchors.** `inputs/design/prototypes/raffa-v2/screens-v2.md`
  — **`:59-79`** for the six stage strings, the state inventory including
  `rejected (not added)`, and the "Not added" card this task retires; **`:25-30`**
  for `askOffReason`, whose sentence is already shipped verbatim at
  `askViewModel.ts:254`. Three states in this task have **no oracle** and are
  decided in ADR-020 w15 §6 and §8 instead: the **local** refusal row (the export
  has a card, not a local row), the **stopped-updates** notice (`app.jsx:46-48`
  advances a document every 700 ms, so the prototype has no wait that can fail to
  end), and Ask's **third** off-state. Each is derived from a shipped idiom, and
  all three are on the `HITL_CLAUDE_DESIGN` owed-exports list without blocking
  w15.
- **The export is pre-rebrand and retyping copy from it reverts the rebrand.**
  `screens-v2.md:26` and `:73` say "**Raffa**"; every shipped string says
  "**Raffa.ai**" (`SignInScreen.tsx:52`, `askViewModel.ts:232`/`:258`,
  `uploadPipeline.ts:42`, `contracts/index.tsx:57`). Copy strings from the
  **code**, not from the export (ADR-020 w15 §0).
- **Ask's third variant, verbatim**: *"Raffa.ai could not finish processing your
  documents. Ask only answers from facts that passed validation — so it never
  guesses."* CTA **Go to Documents**. Its second clause is the shipped string
  verbatim, so exactly one new sentence enters the product. It must **not** be
  collapsed back into the "still processing" sentence.
- **The stopped-poll notice, verbatim**: *"Nothing has changed for five minutes,
  so this page stopped checking for updates."* + **"Check again"**. It is a
  **list-level** notice, not a row state and not the error treatment; the actor
  is **the page**, never Raffa.ai; it never implies failure and never claims work
  is in progress.
- **Two hazards `tsc` will not report.** (1) `documentTable.ts:97-99` is an
  unchecked cast (`status as DocumentStatus`), so adding `"rejected"` to
  `RowStatus` and forgetting `semantics.ts:52` **compiles clean** and falls
  through an exhaustive switch to `undefined`. (2) `DocumentStatusTable.tsx:94-96`
  maps the local entry with a **ternary**, so widening `LocalUploadEntry.phase`
  without a third branch **compiles clean** and renders a refused file as
  *Processing* forever. The refusal edit is a **three-site** edit and the
  compiler asks for none of them.
- **The third chip cannot filter the page.** The grid fetches one page
  (`LIST_PAGE_SIZE = 100`) and buckets client-side by design; `counts.all`
  excludes `Rejected`, so a client bucket would need the page to carry rows "All
  documents" says it does not contain — two definitions of one list.
- **The card may not simply be deleted.** Of the four refusal producers only one
  becomes a server row. The browser-side oversize check makes **no HTTP call at
  all** and 413/415 stays in-request under the split gate; deleting the card with
  no replacement makes an oversized file a **silent drop**, a worse defect than
  the two-surfaces one the deletion fixes. The local row is the replacement.
- **Do not touch the hand-written API wrapper or the generated client** — the
  fields and the `status` filter value were published by `E16/F02/US03/T01` in
  phase 3, and the phase-4 writer of the wrapper is `E17/F02/US01/T01`. Do not add a
  route or touch the router table (ADR-018: no route moves this wave). Do not
  re-label a stranded row `Failed` — the server says `Uploaded` and saying
  otherwise is the fabricated fact A15-3 forbids. Do not add a client timer
  feeding the progress bar. Do not add a client store remembering this session's
  refusals: a blank hint is a gap a user can ask about; a remembered one is a lie
  the next browser tells.

## Definition of done

- [ ] `cd web && npm run build` exit 0 (runs `generate:api && tsc --noEmit && vite build`)
- [ ] `cd web && npm test` exit 0 — the vitest cases below
- [ ] `grep -rn "documentStore" web/src/` returns **no match**
- [ ] `grep -rn "UploadResultCard\|RejectedFileOutcome\|dismissRejected" web/src/` returns **no match**
- [ ] `grep -rn "documents.length" web/src/routes/documents/` shows no count derived from the fetched page
- [ ] `grep -rn "\bRaffa\b(?!\.ai)" web/src/routes/documents web/src/routes/ask web/src/routes/contracts` (or an equivalent read) shows no string reverted to the pre-rebrand "Raffa"
- [ ] A manual read confirms the three-site refusal edit landed in **all three**: `documentTable.ts` (`RowStatus`), `semantics.ts:52`, `DocumentStatusTable.tsx:94-96`

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | the chips render `counts`, not the page — fifteen rows never sit under "Needs your attention · 0" | `web/src/routes/documents/*.test.ts(x)` |
| unit | the optimistic row survives a failed `load()` and is dropped only on a matching server id | `web/src/routes/documents/*.test.ts(x)` |
| unit | an oversize file and a 415 each render **one local row**, never a silent drop | `web/src/routes/documents/*.test.ts(x)` |
| unit | a `rejected` status maps to the "Not added" label and `.tag-outline` — the exhaustive switch has no `undefined` fall-through | `web/tests/styles/semantics.test.ts` |
| unit | `buildOffCopy`'s three states, including *only refused files ⇒ the first variant* | `web/src/routes/ask/*.test.ts` |
| unit | the poll stops after the no-change budget and resumes on "Check again", without re-labelling any row | `web/src/routes/documents/*.test.ts(x)` |

`web.yml` runs `npm test` (vitest) today, so these are a real gate this wave.
A15-1 and A15-3 are walked in a browser on deployed `dev` by `E16/F04/US01/T01`;
no Playwright runner exists and adding one is NW-50, queued W18.

## Open questions blocking this task

- **OQ-w15-ca-01** — 120 s upload deadline, derivation recorded here: it must exceed the time to *send* a 50 MB body (`MAX_FILE_BYTES`, `uploadPipeline.ts:19`) on a slow link and sit below the ~240 s Container Apps platform default, which ADR-005 clause 13 deliberately leaves unpinned. Not blocking — it is a safety net, not the mechanism.
- **OQ-w15-008**, **OQ-w15-ca-05**, **OQ-w15-D2** — resolved or assumed; see the story. None blocking.

## Wave-spec entry
```yaml
- id: E16/F03/US01/T01
  prompt: reports/workitems/epic-16-async-document-processing/feature-03-documents-truthful-surfaces/us-01-documents-states-and-counts/tasks/task-01-documents-states-and-counts.md
  produces: [documents-truthful-surfaces]
  depends_on: [documents-async-api]
  effort: L
  layer: frontend
  status: live
```
