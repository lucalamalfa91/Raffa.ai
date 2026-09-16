---
id: feature-03
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-07 F01, epic-06 F05
---

# feature-03-document-viewer — one route that shows the page a clause was read from

## Slice

No viewer exists on this checkout: `web/package.json:19-25` lists exactly five
runtime dependencies and none of them renders a document, and
`getDocumentPreviewUrl` (`web/src/api/client.ts:2082-2113`) has **zero component
callers**. Highlighting today is text-level `<mark>`
(`contract360/ClauseHighlight.tsx:28-36`, `review/EvidencePane.tsx:141-160`).

This feature adds **one route** — `/documents/:documentId/viewer?page=<n>&clause=<clauseId>`
— rendering the **PNG pages** NW-26 rasterises, with native prev/next page
navigation and the page number **in the URL, not React state**. Because the
server rasterises, the viewer needs **no PDF library**: `web/package.json` stays
at **five** runtime dependencies, this wave **and** in the W18 remainder, since a
bounding-box overlay is absolutely-positioned DOM over an `<img>`.

The split ratified at OQ-w17-001 is what this feature ships: **real pages with
text-level highlighting**, anchored on the existing `SourcePage`/`SourceSpan`.
**True bounding-box overlays and editable OCR phrases are the head of W18.** The
one thing the split can break is a **promise** — so the affordance reads
"*Page N — the wording is highlighted below*" and **must not promise a box drawn
on the page**.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | document-viewer-route | w17 |

## Architecture decisions in force

- **ADR-029** — the server half is clauses 1–7 and nothing more: no gateway
  change, no migration, no write endpoint. Page-level anchoring is **already
  derivable** from the spans on disk; **bounding boxes are the only missing
  piece**.
- **ADR-012** w17 clauses 32–33 — **zero** new runtime dependencies; every page
  fetch is revoked in the `useEffect` cleanup that owns it and the viewer holds
  **at most the current page** alive.
- **ADR-012** w17 §46 — **no page cache across a page navigation**: every page
  view is a fetch (already `cache: "no-store"`), revoked per clause 33. The
  memory rule and the correctness rule are **one rule**.
- **ADR-012** w17 §47 — `client.ts:2103-2104` answers **every** 404 with
  `No document found for id ${id}.`, while ADR-029 clause 5 makes 404 the
  *page*-out-of-range answer. Repaired **inside this feature's own `client.ts`
  edit**, discriminated from `pageCount` — `page > pageCount` needs **no fetch**.
  **No `onError` fallback to page 1 and no clamp.**
- **ADR-018** w17 clauses 9–11, 15 — one route added, the first since w14;
  `navItems.ts` gains **no row** (a citation-reached state, not a rail
  destination); the URL stays on the page that was asked for, so a failure stays
  reproducible.
- **ADR-018** w17 clauses 12–14 — **four** states: loading (skeleton at the
  page's aspect and footprint), empty ("no page image yet"), error (2 px accent
  rule + h4 + service name + Retry) and **not found** — the route's own state,
  never the shell's `*` catch-all, which redirects to `/` and would land a broken
  citation on Ask with nothing saying anything went wrong.
- **ADR-020** w17 §16, §18, §24 — chrome from the **locked catalogue** (no
  export exists for this surface): page canvas on `--color-surface` with a 2 px
  left rule, zero radius, page navigation as a **native control row**
  (`.btn-ghost` prev/next + "Page N of M") and **never a floating overlay** —
  `--shadow-*` is dialogs-only. The not-found copy is **one state, two causes,
  two sentences**, taking the **empty** treatment, never *error*'s Retry.

## Target repo

`raffa-web`.
