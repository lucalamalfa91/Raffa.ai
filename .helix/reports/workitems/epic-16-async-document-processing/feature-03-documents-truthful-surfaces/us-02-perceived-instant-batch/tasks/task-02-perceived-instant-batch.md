---
id: E16/F03/US02/T02
type: task
story: us-02-perceived-instant-batch
wave: w15
status: live
target_repo: raffa-web
---

# task-02-perceived-instant-batch — The perceived-instant batch: the row reading and the progress panel

Built by hand on 2026-09-14, after the first real twenty-file batch on `dev`
(see `docs/waves/w15-acceptance.md` §0.2) turned up the failure mode us-01's
own design left standing: twenty truthful rows that still read as stalled.

## Coding objective

Close the gap between "truthful" (us-01) and "feels instant". A document reads
**"Uploaded"**, no bar, from the moment its row exists — local, pre-201, or
server — and the six-stage wait moves into a fourth state of Documents opened
on request, which also asks the backend (task-01) to work on that document
next. Nothing here weakens us-01's own rule that a screen states only what a
server fact supports: the one exception (a local row's label running ahead of
the 201) is declared, bounded to that row's own text, and resolves to a real
terminal state the moment the request answers.

## Parent story AC covered

- AC-1 (server `Uploaded` row: tag, no bar, next-step text)
- AC-2 (local pre-201 row: the declared, bounded exception)
- AC-3 (the row's filename becomes a link for `uploaded`/`processing`)
- AC-4 (the progress panel: checklist, headline, priority call, no auto-navigate)
- AC-8 (stopped-poll notice reaches the panel)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/documents/documentTable.ts` | `RowStatus` splits `"uploaded"` out of `"processing"`; `getRowStatus` maps `Uploaded → "uploaded"`; new `getOpenTarget(item, rowStatus)`, shared by the row grid and the panel |
| `web/src/styles/semantics.ts` | `DocumentStatus` gains `"uploaded"`; `getStatusTag` → `{ variant: "neutral", label: "Uploaded" }` |
| `web/src/routes/documents/DocumentStatusTable.tsx` | `localRowStatus`: `queued`/`uploading` → `"uploaded"` (was `"processing"`); local row loses its bar and its "Uploading…" text (→ "Processing in the background"); server row's `openTarget` now uses the shared `getOpenTarget`; next-step cell gains the `"uploaded"` branch |
| `web/src/routes/documents/documentProgress.ts` | **new** — `getProgressStages(stage)`, `getProgressView(item)`: the checklist, the headline, the terminal link |
| `web/src/routes/documents/DocumentProgressPanel.tsx` | **new** — the `?progress=<id>` state; calls `prioritiseDocument` once per document id (ref-guarded); renders the checklist while waiting, a link once terminal, the shared stopped-poll notice |
| `web/src/routes/documents/index.tsx` | `progressDocumentId` from `?progress=`; early-return to `DocumentProgressPanel`, looked up from the already-loaded, already-polling list — no second fetch |
| `web/src/routes/documents/documents.css` | the panel's own layout classes; reuses `.documents-review-back`, `.tag`, `.hint`, `.btn`, `.screen-title` |
| `web/src/api/client.ts` | `prioritiseDocument(tenantId, id)` on `reprocessDocument`'s own shape; `204`/`404`/network-failure, never a throw |
| `web/openapi/raffa-api.v1.json` | already carries the `prioritiseDocument` path (task-01); this task runs `npm run generate:api` against it |
| `web/tests/routes/documents/documentTable.test.ts` | the `getRowStatus`/`getRowStatusTag` table gains `Uploaded → "uploaded"`; new `getOpenTarget` suite |
| `web/tests/styles/semantics.test.ts` | `getStatusTag("uploaded")` |
| `web/tests/routes/documents/DocumentStatusTable.test.tsx` | rewrites the three cases that asserted the old "Queued…"/"Uploading…" reading; adds the "Uploaded" chip + link cases |
| `web/tests/routes/documents/documentProgress.test.ts` | **new** |
| `web/tests/routes/documents/DocumentProgressPanel.test.tsx` | **new** |
| `web/tests/api/client.test.ts` | `prioritiseDocument` cases; the completeness-gate `invocations` table gains an entry |
| `web/tests/routes/documents/DocumentsRoute.test.tsx` | the three route-level cases that asserted "Queued…"/"Uploading…" are rewritten against the new reading |

## Context the implementer needs

**`getOpenTarget` is one function, not two.** The row grid's filename `<Link>`
and the panel's own routing must agree on where `"uploaded"`/`"processing"`
open — factored once in `documentTable.ts` and imported by both
`DocumentStatusTable.tsx` and `documentProgress.ts`, rather than duplicated
and risking drift (the class of bug ADR-012's own `isAttentionStatus` header
comment already names for a different pair of files).

**The panel never fetches on its own.** `index.tsx` looks the target document
up from `useDocumentsList.ts`'s already-loaded `documents` array — the
identical pattern `?review=` already uses — so the panel's own `item` prop
re-renders with a fresher value on every 2 s poll tick for free. Do not add a
second poll, a second `usePollBudget`, or a second fetch of the same list.

**The priority call is fire-and-forget, by design.** `DocumentProgressPanel`
calls `apiClient.prioritiseDocument(tenantId, item.id)` once per document id
(a `useRef` guard against a re-render with the same id; the effect's own
`[apiClient, tenantId, item.id]` dependency array already does most of the
work, the ref is the second line of defence under StrictMode) and never reads
or renders the result — `ApiClient`'s own never-throws shape means there is
nothing to catch. The panel's own copy says what it did, plainly, and claims
nothing the backend's own honest promise ("next free Worker slot in this
tenant", task-01) does not support.

**Never auto-navigate.** If the polled `item` turns terminal while the panel
is open, `getProgressView` changes the headline and offers a link — the
screen does not redirect itself. This mirrors `ReviewState.tsx`'s own posture
for "Mark as validated": the write is real, the navigation is the user's
click, never a side effect of a fact arriving.

**Two hazards `tsc` will not report**, the same class ADR-012's own footers
keep naming for this file pair: (1) `RowStatus` is a closed union consumed by
an exhaustive switch in *three* places (`getRowStatus`, `getStatusTag`,
`getOpenTarget`) — adding `"uploaded"` to the type and forgetting one of the
three switches is a compile error (good), but forgetting to update a
*test's* expectation for the changed `Uploaded` mapping is not — the three
route-level tests that asserted "Queued…" for an `Uploaded` row compile clean
and assert a string the row no longer renders. (2) The local row's own
`phase → RowStatus` mapping (`localRowStatus`) is a switch too, but its
*text* ("Processing in the background") is shared verbatim with the server
`"uploaded"` row's own next-step text — a test asserting on that text alone,
without also asserting on row count or on the link/no-link distinction, can
pass against the wrong row during the local-to-server hand-off window.

**Copy is English**, like the rest of the product; this file's chat is
Italian, the product is not (ADR-020 w15 §0, restated every round).

**Do not touch the backend, the migration, or `ExtractionRequestedHandler.cs`**
— task-01 owns those. **Do not add a retry/error UI for a failed
`prioritiseDocument` call** — the panel's own contract is fire-and-forget; an
error state here would contradict it. **Do not change `getRowAction`'s own
next-step buttons** (`"Review N fields"`, `"Ask about it"`, `"Retry upload"`,
`"Open Quote check"`) — none of them apply to `"uploaded"`/`"processing"`,
and none is touched by this task.

## Definition of done

- [ ] `cd web && npm run build` exit 0 (`generate:api && tsc --noEmit && vite build`)
- [ ] `cd web && npm test` exit 0 — the vitest cases below
- [ ] `git diff --exit-code web/src/api/generated/schema.ts` after a second `npm run generate:api` run — zero drift
- [ ] `grep -n "Queued…" web/src/routes/documents/DocumentStatusTable.tsx` returns no match — that reading now lives only in `documentProgress.ts`
- [ ] a manual read confirms `getOpenTarget` has exactly one definition, imported by both call sites

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `Uploaded` maps to row status `"uploaded"`, distinct from `Processing`; `getOpenTarget` routes every `RowStatus` correctly, including the two new progress-panel cases | `documentTable.test.ts` |
| unit | `getStatusTag("uploaded")` is neutral, labelled "Uploaded" | `semantics.test.ts` |
| unit | the checklist marks done/current/todo against a stage; `null` stage → all todo; a `Uploaded`/`Processing` view is `isWaiting`; a terminal view offers the right link (or none, for failed/rejected) | `documentProgress.test.ts` |
| component | a server `Uploaded` row: "Uploaded" tag, no bar, "Processing in the background", filename links to `?progress=` | `DocumentStatusTable.test.tsx` |
| component | a local pre-201 row reads "Uploaded"/"Processing in the background", never "Uploading…", never a bar, never a link | `DocumentStatusTable.test.tsx` |
| component | `DocumentProgressPanel` calls `prioritiseDocument` exactly once per document id, including across a poll-driven re-render with the same id | `DocumentProgressPanel.test.tsx` |
| component | the panel shows the checklist and the priority sentence while waiting; drops both and shows the terminal link once the polled item turns `NeedsReview`/`Completed` | `DocumentProgressPanel.test.tsx` |
| component | the stopped-poll notice and "Check again" reach the panel, wired to the same props the row grid already receives | `DocumentProgressPanel.test.tsx` |
| integration | `prioritiseDocument` POSTs with `X-Tenant-Id`, no body, reports 204/404/network-failure | `client.test.ts` |
| integration | the `ApiClient` completeness gate still passes with 37 methods, `prioritiseDocument` attaching `Authorization` like every other call | `client.test.ts` |
| route | dropping a batch never shows "Queued…"; the local-to-server hand-off leaves exactly one row, proven on the link (not on shared text) | `DocumentsRoute.test.tsx` |

`web.yml` runs `npm test` (vitest) today, so these are a real gate this wave.
A15-9 (open a queued document, watch it jump the queue) is walked on deployed
`dev` by `E16/F04/US01/T01`'s own runbook step; no Playwright runner exists
this wave.

## Open questions blocking this task

None.

## Wave-spec entry
```yaml
- id: E16/F03/US02/T02
  prompt: reports/workitems/epic-16-async-document-processing/feature-03-documents-truthful-surfaces/us-02-perceived-instant-batch/tasks/task-02-perceived-instant-batch.md
  produces: [documents-perceived-instant-batch]
  depends_on: [documents-truthful-surfaces, documents-priority-by-claim]
  effort: M
  layer: frontend
  status: live
```
