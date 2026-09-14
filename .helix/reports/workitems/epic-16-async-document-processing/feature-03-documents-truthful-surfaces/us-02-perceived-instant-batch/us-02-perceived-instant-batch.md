---
id: us-02
type: user-story
parent: feature-03
wave: w15
status: active
---

# us-02-perceived-instant-batch — A batch reads as loaded, the wait moves to a screen of its own, and the open document goes first

Built by hand on 2026-09-14, after the first real twenty-file batch on `dev`
(see `docs/waves/w15-acceptance.md` §0.2) showed the honest failure mode us-01's
own design left standing: twenty rows reading "Processing" with a bar stuck at
0% for as long as the Worker takes to reach each one (~30 s cold start,
~100 s/document measured) — nothing false, and all of it reading as stalled.

## Story

As a **procurement user** who just dropped a batch of contracts, I want every
row to read as loaded the instant it appears, the six-stage wait to live in a
screen I only see if I ask for it, and the document I open to be worked on
next, so that dropping twenty files never feels like the product hung.

## Acceptance criteria

- [ ] AC-1 A server row at `Uploaded` reads the tag **"Uploaded"**, no bar, next
  step **"Processing in the background"** — never "Queued…", never a
  progress bar at 0%. Only a genuinely `Processing` row (the Worker has
  claimed it) shows the real stage and the bar.
- [ ] AC-2 A local row — the instant a file is picked, before the POST even
  resolves — reads the identical "Uploaded" chip and "Processing in the
  background" next step, never "Uploading…". This is the one label on this
  screen that runs ahead of a server fact; it is scoped to this row's own
  text, only while its own request is in flight, and it resolves to a real
  terminal state (including `"failed"`) the moment the request answers.
- [ ] AC-3 The filename of an `"uploaded"`/`"processing"` row is a link to
  `/documents?progress=<id>` — the one primary interactive surface these two
  statuses previously had none of.
- [ ] AC-4 Opening that link shows a fourth state of Documents: the six real
  stage names as a checklist (done/current/todo against the document's own
  `stage`), the headline **"Queued, starting shortly"** for a `null` stage or
  the real stage name for a claimed one, and — once — a call to
  `POST /api/documents/{id}/prioritise`. The panel never auto-navigates: a
  document that finishes while it is open shows a link to `?review=`, to its
  contract, or to Quote check, and the user clicks through on their own turn.
- [ ] AC-5 The prioritised document's classification job is taken by the next
  Worker delivery in its own tenant ahead of that delivery's own job, bounded
  to one prioritised job per delivery, with **no second Service Bus
  subscription** and **no duplicate run** of either job (the superseded
  message's own delivery loses its claim and completes, ADR-027 §C6
  unchanged).
- [ ] AC-6 `POST /api/documents/{id}/prioritise` is idempotent: a job already
  claimed, already prioritised, or already terminal is a no-op that still
  answers `204`. `401` with no identity, `404` for an unknown or foreign
  document, `400` for a malformed id — never `403` (ADR-025 Rule B1).
  Exactly one `document.prioritised` audit row per document that actually
  changed.
- [ ] AC-7 Reprocessing a document clears any stale `PrioritisedAt` on the
  fresh classification job it queues — a reprocessed document re-enters the
  FIFO exactly where a first upload would.
- [ ] AC-8 The stopped-poll notice (ADR-020 w15 §8) reaches the progress
  panel unchanged: the same sentence, the same "Check again" control, while
  the document is still waiting.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-documents-states-and-counts | this story's row reading builds directly on `RowStatus`/`getRowStatusTag`/`getOpenTarget` and the server `counts` us-01 shipped; the progress panel reuses `useDocumentsList.ts`'s own poll and stopped-poll budget rather than adding a second one |
| ADR-027 D1–D12, C1–C11 (async pipeline, in force since w15 phase 1) | the queue-jump is four lines inside the Worker's existing `ExtractionRequestedHandler`, and reuses C6's settlement outcome (`ClaimLost`) rather than adding one |

## Architecture decisions in force

- **ADR-027** (D1–D12, C1–C11 stand; **this story's own footer C12–C13**) — priority by claim: one nullable column, one bounded work-steal inside the existing handler, no second subscription (OQ-w15-012 stays exactly one).
- **ADR-020** (w15 §1, §2, §6, §8 stand; **this story's own footer §10–12**) — the row reads "Uploaded"; the stage checklist and the priority call move into a fourth state of Documents, `?progress=<id>`.
- **ADR-012** (clause 1's one `Authorization` choke point; **this story's own footer §21**) — `prioritiseDocument` goes through it like every other call; the completeness gate now counts 37.
- **ADR-009** — the Worker's prioritised-job query stays inside the message's own tenant scope; no cross-tenant read.
- **ADR-019** — no new token, no new component: the "Uploaded" tag reuses the existing neutral variant.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Priority by claim: the column, the service, the endpoint, the Worker's queue-jump | M | phase-4 |
| task-02 | The perceived-instant batch: the row reading and the progress panel | M | phase-4 |

## Open questions

None raised by this story. OQ-w15-012 (exactly one Service Bus subscription) is
inherited, unchanged, and is the reason the queue-jump lives on the row rather
than on the broker.
