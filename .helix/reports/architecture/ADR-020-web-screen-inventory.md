# ADR-020 — Web screen inventory mapped to spec §16 (R0–R4) and §20

- **Status**: accepted
- **Date**: 2026-09-04
- **Deciders**: ux-ui-designer (draft), product-owner (concur), council-close
- **Locked citations**: "Every spec §16 row and §20 Day-1 step has at least one web story — 'API exists' is not enough" (brief §6); product-spec §16 (§ delivery ladder) and §20 (Definition of V1 done), quoted in web-integration-mandate §6.

## Context and problem statement

The pass exists because E02–E05 were decomposed `layer: backend` and no screen
was authored (brief §1). Decomposition is only allowed to finish when "every
spec §16 row has a screen (or a named non-goal against spec §1.2) and every §20
Day-1 step is reachable in the browser" (web-integration-mandate §6). This ADR
is that proof: a 1:1 screen inventory mapping every §16 definition-of-success
and every §20 definition-of-done step to a concrete screen in the Claude Design
prototype, with its empty/error/loading states in scope and any non-goal named.

The inventory's canonical form is `inputs/design/prototypes/screens.md`; the
clickable implementation is `inputs/design/prototypes/day1-demo.html`. This ADR
adopts that inventory and makes the mapping decomposable.

## Decision drivers

- **Closure of the "API-only" gap** — each §16 success criterion must resolve to
  a screen a user reaches, not an endpoint.
- **Traceability** — the decomposer needs one table that says "§16 R2 → routes
  /renewals, /contracts/:id (Renewal tab)".
- **No screen for a screen's sake** — anything that is genuinely out of V1 scope
  (spec §1.2) is named a non-goal rather than a phantom screen.

## Considered options

1. **One inventory ADR that is the §16/§20 traceability matrix** (chosen).
2. **Separate per-release ADRs** (R0–R4 each their own inventory).
3. **Fold the inventory into the IA ADR only**, without a §16/§20 traceability
   table.

## Decision outcome

**Chosen: Option 1** — a single screen-inventory ADR carrying the full §16 and
§20 traceability matrix, referencing the ten screens in `screens.md`. One
table keeps the "no success criterion without a screen" proof in one place for
the decomposer and the day-after reviewer.

### Consequences

- **Good**: one source of truth for the decomposer and the day-after reviewer;
  explicitly closes the §16/§20 coverage question by naming a screen per row.
- **Bad**: the matrix is compact and must be re-checked if the prototype's
  screens change; it is traceability, not a pixel spec (the pixels live in the
  prototype files it cites).
- **Neutral**: empty/error/loading states are called in-scope but their exact
  copy lives in the prototype, not this ADR.

## Amendment (2026-09-16, wave w18 — citation-card treatments split by corpus)

Serves **NW-55**. The Decision outcome and the screen inventory stand; every
w14/w15 amendment stands. Screen 2 (Ask) gains a corpus rule for its citation
cards.

**1. The dead placeholder dies only where a real link exists (NW-55).**
`CitationCard.tsx:42` today renders a blanket "No page preview available" and
`previewUrl` is almost never set. The w17 viewer route now exists, so the
placeholder splits by `corpus` into **two treatments**, and is never fabricated:

- **Tenant** (`corpus === "tenant"`, `documentId` + `page` present, `previewUrl`
  null): the placeholder becomes a `.btn-ghost` **"Open in document viewer →"**
  deep-linking `/documents/:documentId/viewer?page=<n>&clause=<id>` (ADR-018 w17
  clause 9). Neither preview nor id → keep the honest placeholder verbatim.
- **Market / Raffa** (`corpus === "raffa"` or product): no page by design; the
  placeholder copy says *why*, with **no dead CTA** — the card's whole-body
  `onOpen` already resolves navigation, so the placeholder must never add a
  competing native link.

**2. No `previewUrl` is ever fabricated for either corpus** (ADR-012 w14 clause
1). `PackItem.PreviewUrl` is software-architect's; the client renders only what
the server supplies, and a `corpus === "raffa"` item never carries a
page-preview slot.

Anchors: `CitationCard.tsx:39-45`; `replyTypes.ts:30/54`; `getCorpusBadge`;
`screens-v2.md` §2.

`waves/w18.md` records this under NW-55.
