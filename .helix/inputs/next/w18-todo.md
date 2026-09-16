# Raffa — next-waves input · W18

Status: **binding input** for the W18 requirements document. Written 2026-09-15
after passata 1 of **w17 closed on disk** (`reports/plan/slices/w17.yaml`,
`reports/audit/w17-hitl.md`). IDs continue from `next-waves-todo.md` /
`w17-todo.md`. Item bodies already written there and in ADR-029 are **not
re-derived**; the intake re-audits *status today* against this checkout.

| | |
|---|---|
| Previous wave | w17 — "the product officializes what it knows, shows the page it read it from, and answers where you can save" — `reports/context/waves/w17-requirements.md`, `reports/architecture/waves/w17.md`, ADR-029, epics 20–22 |
| Oracle for carry-over | `reports/audit/w17-hitl.md` §5 (queued for W18) and ADR-029 "NW-63 split" |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, `inputs/percorso-pilota-v1.md` |
| Full item text | `inputs/next/w17-todo.md` (NW-23, NW-25, NW-74), `inputs/next/next-waves-todo.md` (NW-30…NW-60), ADR-029 (NW-63 remainder) |

## 0. Binding instructions (unchanged from w15 §0; stakeholder 2026-09-13)

1. **Every wave the previous intake queued is executed, in order, in full.**
   This run **is W18**. Nothing is dropped and nothing is pushed to a later
   wave than the one it is already queued for; an item that does not fit this
   wave's cap becomes the **head of W19**, never its tail.
2. **An item this file marks `must` or "no longer deferred" is never queued
   beyond the next wave** and is never demoted without a written reason.
3. **Carry-over first.** `w17-hitl.md` §5 is the head of this wave. The W18
   intake picks those items up without re-auditing the *intent*; it **does**
   re-audit *status today* against this checkout.
4. **NW-63's W18 remainder is pre-decided** (ADR-029). Do not re-litigate the
   split. Do not pull the w17 half (viewer over real pages, `SourcePage` +
   `SourceSpan` text highlight) back into this wave.
5. **NW-75 is OUT** (ADR-001 w17 clause 7 / `w17-hitl.md` §5). Do not queue it.
   It returns as a *new* item only once a tracked action can carry a
   system-held estimate.
6. **Do not skip ahead to W19.** Anything not in §1–§2 stays out unless the
   `dev` walk of w17 (not yet run) surfaces a `must` — none is recorded here.

Persistence rule (unchanged): **if a human can create or change it, another
browser / device / session must read it back from Postgres (RLS).** Browser
storage is not a system of record.

## 1. Head of W18 — carry-over from w17 (`w17-hitl.md` §5)

Order is binding. A `must`'s remainder outranks a `should`/`could` overflow.

### NW-63 remainder — bounding-box overlay + phrase-edit (must, reduced)

- **Status:** OPEN — **must** remainder. w17 ships the viewer over real pages
  with jump-to-page on existing `SourcePage` + `SourceSpan`. **This wave** is
  the deferred half. Shape **pre-decided in ADR-029**, do not re-open:
  (a) `prebuilt-layout` plus a widened wire (`words` / `polygon`) and a
  widened `AiOcrPage`; (b) geometry columns on the evidence tables (ADR-003
  w17 clause 2 refused them in w17); (c) the phrase-edit write path
  (provenance: proposal vs override — `ExtractionEvidence.cs:14-19`).
- **Must:** the viewer draws a box on the page; an OCR phrase is editable
  through a write that does not blur proposal vs override; copy that promised
  a box in w17 is already fenced — honour that fence until this lands.
- **Acceptance:** N17 remainder — opening a cited field on a validated
  Northwind PDF shows the page **and** a box; editing a phrase persists and
  a second browser agrees.
- **Seats (hint):** software-architect, cloud-architect (gateway model),
  client-architect, ux-ui-designer, product-owner (the split already ruled).
- **Oracle:** ADR-029 "The NW-63 split"; `w17-hitl.md` §5 item 1; OQ-w17-001.

### NW-23 — Portfolio has no `category` filter (could)

- **Status:** OPEN — **could**. Full text: `inputs/next/w17-todo.md` NW-23.
  OQ-w17-007 travels with it (supplier category or contract type?).
- **Must:** filter the portfolio by category / Type, or the council records
  why V1 still cannot. screens-v2 §6 table word is **Type** if Category is
  not a domain concept yet.
- **Acceptance:** operator can restrict the portfolio list by type/category
  on `dev`.
- **Seats (hint):** software-architect, product-owner, client-architect,
  ux-ui-designer.

### NW-25 — Savings list has no filters (could)

- **Status:** OPEN — **could**. Full text: `inputs/next/w17-todo.md` NW-25.
- **Must:** the operator can restrict the opportunities list (supplier /
  status / currency — council picks the set). Do not invent a category the
  domain does not have.
- **Acceptance:** Savings list is filterable on `dev`.
- **Seats (hint):** client-architect, ux-ui-designer, product-owner.

### NW-74 — Hide admin-gated Ask chips from a non-Admin (should)

- **Status:** OPEN — **should**. **Not demoted.** Full text:
  `inputs/next/w17-todo.md` NW-74. Shape recorded in ADR-012 w17 clause 40.
  ADR-022 S16-11 stands — **presentation, never a security fix**;
  `GET /api/capabilities` stays un-gated.
- **Acceptance:** A17-S3 — Procurement member does not see admin-gated chips;
  Admin does; capabilities GET is identical for both.
- **Seats (hint):** client-architect, ux-ui-designer; security-architect only
  to confirm it is still not a control.

## 2. Recorded W18 queue (`w16-requirements.md` / `w17-todo.md` §5 — unchanged, not displaced)

Full text lives in `inputs/next/next-waves-todo.md`. Re-audit status today.

### NW-30 — Hand-authored OpenAPI gaps (should)

conversations `requestBody`, no `GET /api/audit`. Close the hand-authored
holes against the generated client (ADR-012).

### NW-40 — Confirm HCP apply has CA env vars + Foundry RBAC (should)

Ops proof on `dev`/`demo`, not a new SKU. Evidence in HCP apply / CA env.

### NW-41 — Prove deployed API serves seeded `market_record` rows (should)

A `dev` walker can `GET` market rows the fixture / feed actually stored.

### NW-50 — `web/e2e/day1.spec.ts` red against the V2 shell (should)

Day-1 Playwright vs the V2 shell. Do not paper over with a skip without a
written reason.

### NW-55 — Ask citation cards: real page preview or a section link (should)

Never the empty placeholder. Tenant citations: page preview / open at the
span (now that w17 has a viewer). Product citations: CTA to the section, no
preview slot. Full text: `next-waves-todo.md` NW-55.

### NW-56 — Contract 360 “Ask about it” must brief that contract (should)

Not the upload onboarding. Scoped chat, immediate answer, contract-specific
follow-ups. Full text: `next-waves-todo.md` NW-56.

### NW-57 — Quote check: market benchmark, not a manual savings worksheet (should)

Keep every request; no “send to Home”. Full text: `next-waves-todo.md` NW-57.

### NW-59 — Ask never dead-ends (should)

Every abstain/error has a clickable next step. Full text: `next-waves-todo.md`
NW-59.

### NW-60 — Hide the global Ask bar on Ask screens (should)

Keep it everywhere else. Full text: `next-waves-todo.md` NW-60.

## 3. Out of this wave

| ID | Why |
|---|---|
| NW-72, NW-73, NW-71, NW-20, NW-22, NW-62, NW-26, NW-63 (w17 half), NW-64, NW-65, NW-66 | **w17** — planned on disk; do not reopen as live W18 tasks unless status today shows they never landed |
| NW-75 | **OUT** (ADR-001 w17 clause 7) — not queued |
| NW-51 | design-alignment residual; not in the recorded W18 queue |
| NW-52, NW-53, NW-54 | **DEFERRED** (paid market API / mobile beyond scaffold / extra roles in nav) |

## 4. Full remaining schedule

| Wave | Queue (head first) |
|---|---|
| **W18 (this run)** | NW-63 remainder, NW-23, NW-25, NW-74 (w17 overflow / split, head), then NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 |
| **W19** | whatever does not fit this wave's cap, as the **head**, never the tail |

## 5. Suggested grouping and seats

| Theme | Items | Seats |
|---|---|---|
| A · viewer remainder | NW-63 remainder | software-architect, cloud-architect, client-architect, ux-ui-designer, product-owner |
| B · list filters | NW-23, NW-25 | product-owner, software-architect (NW-23), client-architect, ux-ui-designer |
| C · Ask chips (presentation) | NW-74 | client-architect, ux-ui-designer; security-architect confirm-not-a-control |
| D · Ask / Quote UX | NW-55, NW-56, NW-57, NW-59, NW-60 | product-owner, client-architect, ux-ui-designer, software-architect |
| E · contract / ops residuals | NW-30, NW-40, NW-41, NW-50 | software-architect, delivery-manager, cloud-architect as touched |

**A before D** where they share the viewer / citation preview. Cap 20 tasks /
5 phases; overflow → **head of W19**.
