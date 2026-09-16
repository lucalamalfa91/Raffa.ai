---
id: E22/F03/US01/T01
type: task
story: us-01-document-viewer-route
wave: w17
status: live
target_repo: raffa-web
---

# task-01-document-viewer-route — the viewer route, the page fetch, and the 404 that names the right thing

## Context

**Closes: NW-63** (w17 half — the W18 remainder is bounding-box overlays and
editable OCR phrases, pre-shaped in ADR-029 and ADR-017's w17 footer).

Decision row: `reports/architecture/waves/w17.md` — the **NW-63** row (all three
seat halves), the **Client note (round 3) on NW-63**, the **Round 3 — design**
section, and the rulings on **OQ-w17-001**, **OQ-w17-cl-01**, **OQ-w17-sa-04**
and **OQ-w17-cl-03** (`w17.md:417` — **both halves are this task's**: the check
is owed by NW-63's task by name, and the dropped-citation copy is owed
*whether or not the check passes*).

ADRs in force: **ADR-012** w17 clauses 32, 33, 39 and §46–§48; **ADR-012 w14
footer** (`:189-285`, *"the screen the redirect actually lands on"* — the clause
OQ-w17-cl-03's check would amend, and which this task must **not** amend
itself); **ADR-018** w17 clauses 9–15; **ADR-020** w17 §16, §18, §24;
**ADR-029** clauses 4–7 and its round-3 footer; **ADR-003** w17 clause 2
(geometry columns **refused** this wave); **ADR-017** w17 footer
(`prebuilt-layout` stays uncalled).

⚠ **This task is the phase-3 writer of `web/src/api/client.ts`.**
`E21/F03/US01/T01` was its phase-2 writer; `E20/F01/US01/T01` and
`E22/F01/US02/T01` are both explicitly forbidden from opening it this phase.

⚠ **`web/e2e/w17-viewer.spec.ts` does not exist on this base** (`web/e2e/` holds
`day1.spec.ts`, `invite.spec.ts`, `v2.spec.ts`). **This task creates it.**
`E22/F04/US01/T01` **extends** it in phase 4 — one writer per phase, satisfied
by phase.

Already on `main`, so **not** a dependency to build: `getDocumentPreviewUrl`
(`client.ts:2082-2113`), the object-URL ownership contract (`:418-426`), the
`?clause=&page=` citation-landing convention (`contract360/index.tsx:52`,
`:65-67`, `:113`), and the `ClauseHighlight` text-level `<mark>`
(`contract360/ClauseHighlight.tsx:28-36`).

## Coding objective

In `raffa-web`, add **one route** that shows the document's own page.

1. **The route.** `/documents/:documentId/viewer?page=<n>&clause=<clauseId>`,
   registered as **one `<Route>` line above the `*` catch-all** in
   `web/src/components/shell/WorkspaceShellApp.tsx:94` (the map has no
   `documents/:documentId` segment today). **`navItems.ts` gains no row** — the
   viewer is a **citation-reached state, not a rail destination**, the same call
   the V2 IA already made for Review (`navItems.ts:18`).
2. **The page lives in the URL, not in React state.** Read and write `?page=`
   through the router so a reload and a deep link land on the same page.
   `?clause=` selects the clause and seeds the initial page from its
   `sourcePage`. Reuse the existing convention; do not invent a second one.
3. **PNG pages, no new dependency.** Because NW-26 rasterises server-side, the
   viewer renders **PNG pages**: **no PDF library**, this wave or in the W18
   remainder — a bounding-box overlay is absolutely-positioned DOM over an
   `<img>`. `web/package.json:19-25` carries exactly **five** runtime
   dependencies and must still carry five. Do **not** add `react-pdf`, `pdfjs`,
   `pdf-lib` or any viewer package.
4. **Fetch, render, revoke.** A plain `<img src="/api/…">` **cannot work**:
   `getDocumentPreviewUrl` is an authenticated fetch (`client.ts:2085-2088`)
   returning `URL.createObjectURL(blob)` (`:2100`). Every page fetch is revoked
   in the **`useEffect` cleanup that owns it**, and the viewer holds **at most
   the current page** alive — otherwise a 40-page document paged end to end leaks
   one blob per page. There are **zero callers of this method today**, so that
   contract has never been exercised.
5. **No page cache across a navigation** (ADR-012 w17 §46). Every page view is a
   fetch (already `cache: "no-store"` at `:2087`). This is both the memory rule
   and the correctness rule: NW-73 re-renders a whole tenant while this viewer is
   the first surface that holds pages open, the key carries **no discriminator**
   (ADR-029 round-3 clause 1), `pageCount` is unchanged by a same-length
   re-render, the document read model has no other candidate
   (`documentProgress.ts:65`, `documentTable.ts:145`), and **the poll does not
   help** — `useDocumentsList.ts:148` runs only while a row is
   `Uploaded`/`Processing` and `:55` stops it after five minutes, so in the
   steady state where a reviewer opens a viewer **no poll is running**.
6. **Repair the 404 branch, inside this task's own `client.ts` edit**
   (ADR-012 w17 §47). `client.ts:2103-2104` answers **every** 404 with
   `No document found for id ${id}.`, while ADR-029 clause 5 makes 404 the
   **page**-out-of-range answer and round-3 clause 2 makes "that page was
   reaped" an ordinary outcome. Add the `page` parameter to the method and
   discriminate the two causes; `page > pageCount` needs **no fetch** at all,
   because the viewer already holds `pageCount`. ⚠ **No `onError` fallback to
   page 1 and no clamp**: ADR-029 clause 5 refuses that at the route, and the
   client can recreate the identical lie **above a correct server** in three
   lines. ADR-018 clause 15 keeps the URL on the page that was asked for, so the
   failure stays reproducible.
7. **Four states, not three** (ADR-018 w17 clause 12):
   - **loading** — a skeleton at the page's aspect and the **same footprint**,
     so nothing jumps;
   - **empty** — "no page image yet", the honest fallback the server already
     degrades to, never a broken `<img>`;
   - **error** — 2 px accent rule + `h4` + the plain service name + **Retry**;
   - **not found** — the **route's own** state for a bad `documentId` or an
     out-of-range `?page=`, **never** the shell's `*` catch-all, which redirects
     to `/` and would land a broken citation on Ask with nothing saying anything
     went wrong.
8. **The not-found copy: one state, two causes, two sentences** (ADR-020 w17
   §24):
   - `page > pageCount` → *"This document has N pages."* + a **Go to page 1**
     button (no fetch);
   - a server 404 for a page the tab still counts → *"This page is no longer
     part of this document — it was re-processed after this link was made."* +
     a **Reload the document** button.
   ⚠ They are **one URL in sequence**: after the reload the tab holds the
   shorter `pageCount` and the same address becomes the first sentence. Read
   cold they look mutually exclusive and the repair an implementer reaches for
   is to delete one — **keep both**. Three prohibitions: neither sentence may
   say the **document** was not found (the document is open in front of the
   user); **not found takes the *empty* treatment and never *error*'s Retry**
   (after the reap it is an **ordinary outcome**, and a Retry invites the user
   to press it until they conclude the product is broken); and the action is **a
   button the user presses**, never an automatic clamp — refused at the route,
   in the client, and here in the copy, where *"showing page 1 instead"* would
   license it by describing it.
9. **Chrome from the locked catalogue** (ADR-020 w17 §16). **No export exists
   for this surface**, so: page canvas on `--color-surface` with a **2 px left
   rule** (`inputs/design/prototypes/design-system.md:47`, the detail-pane idiom), **zero radius**, page
   navigation as a **native control row** (`.btn-ghost` prev/next + "Page N of
   M") and **never a floating overlay** — `--shadow-*` is dialogs-only (`:25`),
   so a hovering page control would be the **first shadow inside the app**. The
   highlight is a `--color-accent-100/200` **tint**, never a new colour. **No new
   token and no new component.**
10. **The promise the split can break.** w17 ships the page image plus the
    **text-level** highlight (reuse `ClauseHighlight.tsx:28-36`); the
    bounding-box overlay is W18. The affordance reads **"Page N — the wording is
    highlighted below"** and **must not promise a box drawn on the page**. The
    split is invisible to the user *unless the words create an expectation the
    screen then breaks*.
11. **Null `pageCount`** (OQ-w17-sa-04, second half): show page 1 and say
    **"Page 1 · total unknown"** — never a guessed total.
12. **The citation that could not be restored** (OQ-w17-cl-03, UX half;
    ADR-020 w17 §16). This is a **fifth** state of the page, not a sixth state
    of the not-found block. When the route is reached and the citation **cannot
    be resolved**, it renders page 1 **in the ordinary page state** *and* says
    the citation could not be restored, with the document **open and
    navigable**. ⚠ The prohibition is the point: it must **not** silently render
    page 1 **as though that were the citation**. That is the same class of
    defect as the `*` catch-all in ADR-018 w17 clause 12 — *a wrong destination
    with no error* — and the **more dangerous** of the two, because the user
    does not conclude "the link is broken", they conclude "**the clause is not
    in this document**" and stop looking. Three binding consequences:
    - **The affordance is gated on a resolved clause.** Step 10's "Page N — the
      wording is highlighted below" and the `ClauseHighlight` `<mark>` render
      **only** when a `clause` resolved. No `clause`, no highlight, no sentence
      claiming one.
    - **Reachable triggers**, each of which the route can actually detect: a
      `?clause=` that is **not in this document**; a `clause` with no resolvable
      `sourcePage`; a `page` that is not a positive integer; and the
      arrived-cold case step 13's check decides. ⚠ A query lost to a **truncated
      paste** is undetectable and is exactly why the copy is owed unconditionally
      — do not make the sentence depend on the check's outcome.
    - **Treatment**: an inline notice in the page's own column, taking the
      **empty** treatment of step 8's rule. It is **not** the not-found state
      (the document is open in front of the user), it carries **no Retry**, and
      it **never** says the *document* was not found.
13. **Run OQ-w17-cl-03's check and record its outcome** — it is a deliverable of
    this task, not a background assumption. Deep-link
    `/documents/:documentId/viewer?page=3&clause=<id>` **while signed out**,
    complete the OIDC round trip, and land on **page 3 with the clause still
    selected**. ⚠ This is the **first** route in the product whose query string
    carries meaning a user can arrive on **cold** — every earlier deep link
    either had no query or was reached from inside an authenticated session — so
    nothing on this base establishes that the post-login return URL preserves a
    query string, and **this task may not assume it either way**.
    - **If the query survives**: say so in the PR body, quoting the landed URL,
      and keep the passing case in `w17-viewer.spec.ts`.
    - **If the query is dropped**: the fix is in the **auth landing, not in the
      viewer**. ⚠ Do **not** repair it here — no stashing the query in
      `sessionStorage`/`localStorage`, no re-deriving the page from anything,
      no second redirect. Step 12's sentence lands (it is owed regardless), the
      finding goes in the PR body naming **ADR-012's w14 footer** (`:189-285`)
      as the clause a **later** wave amends, and this wave ships the honest
      state rather than a silent page 1.
    Either way the outcome is **written down**, not felt:
    `reports/open-questions.md` carries OQ-w17-cl-03 with its assumption in
    force, and the operator closes it from this task's PR body.

## Parent story AC covered

- AC-1 The route renders the document's page image with a "Page N of M" navigation row. (N17)
- AC-2 The current page lives in the URL; reloading `?page=3` lands on page 3.
- AC-3 `?clause=` lands on the clause's `sourcePage` with the wording highlighted **below** the page; the copy never promises a box.
- AC-4 Paging a 40-page document end to end leaks **no** object URLs.
- AC-5 A page beyond `pageCount` renders the route's own not-found state with **no fetch** and the URL still reading `?page=N`.
- AC-6 A server 404 for a counted page renders the other sentence; neither says the **document** was not found.
- AC-7 Not found takes the **empty** treatment, not *error*'s Retry.
- AC-8 A null `pageCount` shows page 1 and says the total is unknown.
- AC-9 `web/package.json` still lists exactly **five** runtime dependencies.
- AC-10 `navItems.ts` gains **no** row.
- AC-11 An unresolvable citation renders page 1 in the **ordinary page state** and says the **citation could not be restored** — document still open and navigable, no highlight affordance, empty treatment, no Retry, and never "the document was not found". (OQ-w17-cl-03)
- AC-12 The signed-out deep-link check is **run and its outcome recorded** in the PR body; if the query is dropped the repair is the **auth landing**'s, never the viewer's and never a browser store. (OQ-w17-cl-03)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/documents/viewer/index.tsx` | **new** — the route component: page image, native prev/next row, `?page=`/`?clause=` from the URL, the four states, **and the unresolvable-citation notice of step 12** (the highlight affordance gated on a resolved clause) |
| `web/src/routes/documents/viewer/documentViewerViewModel.ts` | **new** — page bounds from `pageCount`, the two not-found causes, the "Page N of M" / "Page 1 · total unknown" strings, **and the citation-resolution result** (resolved / unresolvable) that drives step 12's notice |
| `web/src/routes/documents/viewer/documentViewer.css` | **new** — page canvas on `--color-surface`, 2 px left rule, zero radius; no shadow, no new token |
| `web/src/api/client.ts` | modify — `getDocumentPreviewUrl` gains the optional `page` parameter; the 404 branch (`:2103-2104`) discriminates page-out-of-range from document-not-found; the `:418-426` ownership comment is updated now that it has a caller |
| `web/src/components/shell/WorkspaceShellApp.tsx` | modify — **one** `<Route path="documents/:documentId/viewer" …>` line above the `*` catch-all (`:94`) |
| `web/tests/routes/documents/viewer/documentViewerViewModel.test.ts` | **new** — page bounds, the two not-found causes, the null-`pageCount` string, **and each unresolvable-citation trigger of step 12** |
| `web/tests/routes/documents/viewer/DocumentViewerRoute.test.tsx` | **new** — the four states; `?page=` drives the render; `page > pageCount` renders not-found **without** calling the client; revoke-on-unmount and revoke-on-page-change; **an unresolvable citation renders page 1 + the notice, no highlight and no Retry** |
| `web/tests/api/client.test.ts` | modify — `getDocumentPreviewUrl` sends `page`, and a 404 no longer claims the document is missing |
| `web/e2e/w17-viewer.spec.ts` | **new** — N17: open a validated document's viewer, page to 2, deep-link `?page=` beyond `pageCount` and land on not-found **with the URL unchanged**; a reprocessed document's viewer stops painting the pre-reprocess page; **plus OQ-w17-cl-03's signed-out deep-link case (step 13), whose recorded outcome is the check** |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-012 w17 clause 32 / OQ-w17-001 (CA half)** — the split costs **no
    runtime dependency, this wave or the next**. This is the rule that removes
    the one cost that would have argued for landing NW-63 whole.
  - **ADR-012 w17 clause 33** — the caller owns `URL.revokeObjectURL`; the
    client does not track outstanding object URLs itself (`client.ts:418-426`),
    and this is its **first** consumer.
  - **ADR-012 w17 §46** — ⚠ the reap does **not** cross into the browser, and
    `cache: "no-store"` is what hides it. Do not propose "the poll will tell us":
    verified, it is not running in the steady state.
  - **ADR-018 w17 clause 13** — ⚠ a correction on the record: the design lane
    first ruled the viewer "a state of screens 4/5, **not a route**".
    Client-architect owns ADR-018's route half and ruled otherwise, and is
    right: a full-page surface with a shareable, deep-linkable URL **is** a
    route. The lane's **IA** half survives intact — **no rail row**.
  - **ADR-020 w17 §16** — no export exists for viewer chrome anywhere under
    `inputs/design/**`. It is decided from ADR-019's locked catalogue rather
    than deferred, so this task **does not stall on a Claude Design
    round-trip**.
  - **ADR-003 w17 clause 2 / ADR-017 w17 footer** — geometry columns and
    `prebuilt-layout` are **refused this wave**. Bounding boxes are W18. Do not
    widen `DocumentIntelligenceContracts.cs` or `AiOcrPage.cs`, and do not add a
    `bbox`/`polygon` column anywhere.
  - **ADR-029** — there is **no phrase-edit endpoint this wave**: there is no
    `MapPatch`/`MapPut` on documents at all
    (`DocumentsEndpointExtensions.cs:90-96`), and editing an OCR *phrase* is a
    different provenance question that would blur the proposal-vs-override
    distinction `ExtractionEvidence.cs:14-19` exists to keep.
- **Do not touch**: `web/src/routes/contracts/contract360/**` — the Why-clause
  **link** is `E22/F04/US01/T01`'s, phase 4 (which is why that task depends on
  this one); `web/src/routes/contracts/review/**` and `web/src/styles/semantics.ts`
  (`E22/F01/US02/T01`, **this same phase**);
  `web/src/routes/savings/**`, `web/openapi/raffa-api.v1.json` and
  `web/src/api/generated/schema.ts` (`E20/F01/US01/T01`, **this same phase**);
  `web/src/components/shell/navItems.ts` (**no rail row**);
  `web/tests/components/shell/WorkspaceShellApp.test.tsx`
  (`E20/F01/US01/T01`'s KPI fixture edit, this same phase — add route coverage in
  this task's own new test files instead);
  `backend/**` (NW-26 shipped the server half in phase 1).

## Definition of done

- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `cd web && npm test` exits 0
- [ ] `cd web && npx playwright test e2e/w17-viewer.spec.ts` exits 0
- [ ] `git diff --stat -- web/package.json web/package-lock.json` is **empty** for this task's commits
- [ ] `grep -c '"' web/package.json` aside, the `dependencies` block still lists exactly **five** packages
- [ ] `grep -rn "pdfjs\|react-pdf\|pdf-lib" web/src web/package.json` returns nothing
- [ ] `grep -rn "revokeObjectURL" web/src/routes/documents/viewer/` returns at least one call inside a `useEffect` cleanup
- [ ] `git diff --stat -- web/src/components/shell/navItems.ts` is **empty**
- [ ] `grep -rn "No document found for id" web/src/api/client.ts` shows the string reachable **only** on a document-level 404, not on a page-level one
- [ ] `grep -rn "Math.min\|clamp\|page = 1" web/src/routes/documents/viewer/` shows **no** automatic fallback to page 1
- [ ] `grep -rni "box\|overlay\|rectangle" web/src/routes/documents/viewer/` shows **no** copy promising a box drawn on the page
- [ ] **the unresolvable-citation sentence exists and the highlight is gated**: `grep -rn "could not be restored" web/src/routes/documents/viewer/` returns the notice, and `grep -rn "highlighted below" web/src/routes/documents/viewer/` shows that affordance rendered **only** on a resolved clause
- [ ] `grep -rn "sessionStorage\|localStorage" web/src/routes/documents/viewer/` returns **nothing** — a dropped query is **never** repaired with a browser store (step 13), and the viewer holds no user state the server does not
- [ ] `grep -rni "document.*not found\|Retry" web/src/routes/documents/viewer/` shows neither string inside the unresolvable-citation branch (the document **is** open; the notice takes the empty treatment)
- [ ] **OQ-w17-cl-03's check is recorded, not assumed**: the PR body quotes the URL landed on after a **signed-out** `?page=3&clause=…` deep link and the OIDC round trip, and states plainly whether the query survived. If it did not, the PR body names **ADR-012 w14 footer `:189-285`** as the clause a later wave amends and confirms **no** compensating change was made in the viewer
- [ ] `git diff --stat -- web/src/components/shell/WorkspaceShellApp.tsx` shows a **one-line** route addition

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | page bounds from `pageCount`; `page > pageCount` yields the first not-found cause; a server 404 for a counted page yields the second; a null `pageCount` yields "Page 1 · total unknown" | `web/tests/routes/documents/viewer/documentViewerViewModel.test.ts` |
| component | the four states render; `?page=` drives which page is fetched; `page > pageCount` renders not-found **with zero client calls**; unmount and page-change both revoke the object URL; not-found shows no Retry | `web/tests/routes/documents/viewer/DocumentViewerRoute.test.tsx` |
| unit | `getDocumentPreviewUrl` sends the `page` query parameter and a page-level 404 does not claim the document is missing | `web/tests/api/client.test.ts` |
| unit | each of step 12's triggers (no `clause`, an unknown `clause`, a `clause` with no `sourcePage`, a non-positive `page`) yields the **unresolvable** citation result and an ordinary page-1 state; a resolved clause yields the highlight affordance | `web/tests/routes/documents/viewer/documentViewerViewModel.test.ts` |
| component | an unresolvable citation renders **page 1 + the notice**, with the document navigable, **no** highlight, **no** Retry and no "document not found" copy | `web/tests/routes/documents/viewer/DocumentViewerRoute.test.tsx` |
| e2e | **N17** — a validated document's viewer shows its own page; paging to 2 works; a deep link beyond `pageCount` lands on not-found **with the URL still reading `?page=N`**; a reprocessed document's viewer stops painting the pre-reprocess page | `web/e2e/w17-viewer.spec.ts` |
| e2e | **OQ-w17-cl-03's check** — a **signed-out** `?page=3&clause=…` deep link through the OIDC round trip; the spec asserts the branch that actually held and the PR body records it. Runbook evidence, **not** a CI gate | `web/e2e/w17-viewer.spec.ts` |

## Open questions blocking this task

- **none blocking.** OQ-w17-001's split is **ratified** by product-owner,
  software-architect, client-architect and ux-ui-designer. OQ-w17-cl-01
  (`client.ts` writers) and OQ-w17-sa-04 (null `pageCount`) are ruled.
- ⚠ **OQ-w17-cl-03 is open and is this task's to discharge, in two different
  ways — do not collapse them into one.** The **UX half is a rule**: step 12's
  copy ships unconditionally (AC-11), because a query string can also be lost to
  a truncated paste, so it must **not** be made conditional on the check. The
  **CA half is a check with a recorded outcome** (step 13, AC-12), and its
  possible failure is **not** this task's to repair: a dropped return-URL query
  is fixed in the **auth landing**, amending **ADR-012's w14 footer**
  (`:189-285`) in a later wave. ⚠ The predictable wrong move is to "fix" it here
  by stashing the query in `sessionStorage` — which is both the wrong layer and
  the persistence rule's own prohibition. Write the finding down; do not code
  around it.

## Wave-spec entry

```yaml
- id: E22/F03/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-03-document-viewer/us-01-document-viewer-route/tasks/task-01-document-viewer-route.md
  produces: [document-viewer]
  depends_on: [document-page-render, api-contract-a]
  effort: L
  layer: frontend
  status: live
```
