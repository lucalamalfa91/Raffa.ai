---
id: us-01
type: user-story
parent: feature-03
wave: w17
status: active
---

# us-01-document-viewer-route — open the document at the page a clause came from

## Story

As a **procurement reviewer**, I want to **open the document at the page a
clause was read from**, so that **I can check the wording against the file
itself instead of trusting a normalized value**.

## Acceptance criteria

- [ ] AC-1 `/documents/:documentId/viewer` renders the document's **page
      image**, with a page-navigation row reading "Page N of M". (N17)
- [ ] AC-2 The current page lives **in the URL**: reloading
      `?page=3` lands on page 3, and the address is shareable.
- [ ] AC-3 `?clause=<clauseId>` lands on that clause's `sourcePage` and shows
      the wording **highlighted below the page**. The affordance says
      "*Page N — the wording is highlighted below*" and **never promises a box
      drawn on the page**.
- [ ] AC-4 Paging end to end through a 40-page document leaks **no object
      URLs**: each page fetch is revoked in the cleanup that owns it and at most
      the current page is held alive.
- [ ] AC-5 A page beyond the document's `pageCount` renders the route's own
      **not found** state — *"This document has N pages."* + **Go to page 1** —
      with **no fetch** and with the URL **still reading `?page=N`**. It is
      never the shell's `*` catch-all and never a silent clamp to page 1.
- [ ] AC-6 A 404 from the server for a page the tab still counts renders the
      other cause — *"This page is no longer part of this document — it was
      re-processed after this link was made."* + **Reload the document**. Neither
      sentence says the **document** was not found.
- [ ] AC-7 The not-found state takes the **empty** treatment, not *error*'s
      Retry.
- [ ] AC-8 Where `pageCount` is null the viewer shows page 1 and says the count
      is **unknown** — never a guessed total.
- [ ] AC-9 `web/package.json` still lists exactly **five** runtime dependencies.
- [ ] AC-10 `navItems.ts` gains **no** row: the viewer is reached from a
      citation, not from the rail.
- [ ] AC-11 A citation that **cannot be resolved** — no `clause`, a `clause` not
      in this document, a `clause` with no `sourcePage`, or a non-positive
      `page` — renders page 1 in the **ordinary page state** *and* says the
      **citation could not be restored**, with the document still open and
      navigable. It **never** renders page 1 as though that were the citation,
      the highlight affordance does not render without a resolved clause, and
      the notice takes the **empty** treatment (no Retry, and it never says the
      *document* was not found). (OQ-w17-cl-03 UX half, ADR-020 w17 §16)
- [ ] AC-12 The signed-out deep-link check is **run and its outcome recorded**:
      `?page=3&clause=…` opened while signed out, through the OIDC round trip,
      either lands on page 3 with the clause selected, or is recorded as
      dropping the query — in which case the repair belongs to the **auth
      landing** (ADR-012 w14 footer `:189-285`), never to the viewer, and never
      to a browser store. (OQ-w17-cl-03 CA half)

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E22/F02/US01/T01` (phase 1) | the viewer overlays a **real** page; without `?page=n` and `pageCount` it has exactly one page and AC-1/AC-2 are unreachable |
| `E22/F01/US01/T01` (phase 2) | `?page=n` and `pageCount` must be in the contract and in the regenerated `schema.ts` before the client can type them |

## Architecture decisions in force

- **ADR-012 w17 clause 32** — **zero** new runtime dependencies. An implementer
  handed a work item called "document viewer" reaches for `react-pdf`
  reflexively; nothing in the task text would stop them, so it is written as a
  rule rather than left as an outcome.
- **ADR-012 w17 clause 33** — `getDocumentPreviewUrl` performs an
  **authenticated** fetch (`X-Tenant-Id` + bearer, `client.ts:2086`) and returns
  `URL.createObjectURL(blob)` (`:2100`), so a plain `<img src="/api/…">`
  **cannot work at all**. `:418-426` already states the caller owns
  `URL.revokeObjectURL` and that *"this client does not track outstanding object
  URLs itself"* — and there are **zero callers today**, so that contract has
  never been exercised.
- **ADR-012 w17 §46** — no page cache across a navigation; every page view is a
  fetch. The reap in the container does **not** cross into the browser, and
  `cache: "no-store"` is what **hides** that: the HTTP layer provably never
  serves stale bytes, so the paint is assumed fresh when it comes from a blob no
  fetch directive governs.
- **ADR-012 w17 §47** — discriminate the two 404 causes from `pageCount`, with
  no fetch and no contract change; **no clamp, no `onError` swap to page 1**.
- **ADR-018 w17 clauses 9–11** — one `<Route>` above the `*` catch-all
  (`WorkspaceShellApp.tsx:94`); `?clause=&page=` is **already** this product's
  citation-landing convention (`contract360/index.tsx:52`, `:65-67`, `:113`) and
  is reused, not re-invented.
- **ADR-018 w17 clauses 12–14** — four states, not three.
- **ADR-018 w17 clause 15** — the URL stays on the page that was asked for.
- **ADR-020 w17 §16, §24** — chrome and copy from the locked catalogue.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | document-viewer-route | L | phase-3 |

## Council decisions carried into this story

- **`web/src/api/client.ts` is a one-writer-per-phase file and this story owns
  it in phase 3** (ADR-012 w16 clause 25, w17 clause 39 / OQ-w17-cl-01).
  `E21/F03/US01/T01` was its phase-2 writer and `E20/F01/US01/T01` is explicitly
  forbidden from opening it.
- The route is decided **this wave regardless of the split**, because three
  surfaces link to it (Why clauses, Ask citations, the Documents row) and a link
  target that changes shape between waves breaks all three.
- `w17-viewer.spec.ts` is **created here in phase 3** and **extended** by
  `E22/F04/US01/T01` in phase 4. The tempting repair — merging the two tasks so
  they can share the file — would re-collide `contract360ViewModel.ts` and undo
  the phase separation (ADR-014 w17 clause 9).

## Open questions

- **none blocking.** OQ-w17-001's split is **ratified** by all four seats.
  OQ-w17-cl-01 is ruled (`client.ts` keeps one writer per phase; NW-72 drops its
  alias). OQ-w17-sa-04's second half is ruled (null `pageCount` → page 1 and say
  the count is unknown).
- **OQ-w17-cl-03 — open, and its check is this story's deliverable, not a
  blocker.** The UX half is already a rule (AC-11) and ships **whether or not
  the check passes**, since a query can also be lost to a truncated paste. The
  CA half (AC-12) is a **check with a recorded outcome**: if the post-login
  return URL drops the query, the amendment lands on **ADR-012's w14 footer** in
  a later wave, and nothing in this story is repaired to compensate.
