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
the decomposer and the reviewer.

### Consequences

- **Good**: one source of truth for the decomposer and the day-after reviewer;
  explicitly closes the §16/§20 coverage question by naming a screen per row.
- **Bad**: the matrix is compact and must be re-checked if the prototype's
  screens change; it is traceability, not a pixel spec (the pixels live in the
  prototype files it cites).
- **Neutral**: empty/error/loading states are called in-scope but their exact
  copy lives in the prototype, not this ADR.

## Screen inventory (from screens.md)

1. **Sign-in → workspace** — R0.
2. **Members & roles** — R0 (invite; admin vs procurement).
3. **Upload → document status** — R0/R1 (processing, needs_review, failed).
4. **Portfolio** — R1 (columns, filters, attention strip).
5. **Contract 360** — R1 (header + 10 tabs).
6. **Review / correction** — R1 (confidence thresholds, evidence pane).
7. **Ask Raffa** — R1 (chat, citations, abstain).
8. **Renewal pipeline** — R2 (threshold strip, table, insight card + actions).
9. **Home / Savings** — R3 (6 KPIs, opportunities table).
10. **Quote check** — R4 (Extract → Assessment → Target → Negotiation).

## §16 traceability

| Release | Definition of success | Screen(s) |
|---|---|---|
| R0 — Foundation | Secure workspace can ingest documents | 1, 2, 3 |
| R1 — Contract Intelligence | Upload contracts + ask reliable questions | 3, 4, 5, 6, 7 |
| R2 — Renewals | Procurement doesn't miss material renewal windows | 8, 5 (Renewal tab), 4 (attention strip) |
| R3 — Savings | Quantifies credible savings opportunities | 9, 5 (Benchmark tab) |
| R4 — Quote Check | New proposal assessed in minutes | 10 (→ 9 outcome) |

## §20 traceability

| Definition of V1 done | Screen(s) |
|---|---|
| Create workspace + invite Procurement | 1, 2 |
| Upload portfolio; classify/extract/structure | 3 |
| Reliable questions with source evidence | 7 (→ 5 Clauses) |
| Renewal + cancellation deadlines | 4, 5, 8 |
| Contract/commercial risks | 5 (Risks, Overview) |
| Market benchmarks where available | 5 (Benchmark) + 10 |
| Prioritized savings opportunities | 9 |
| Quote → line-level assessment → target → strategy | 10 |
| Record outcome, track realized savings | 10 → 9 |

Every row resolves to a screen; **no §16/§20 step is a named non-goal** — the
entire Day-1 ladder is in scope and has a screen. Non-goals (spec §1.2) are the
platform-wide exclusions already carried in BACKLOG.md (CLM authoring,
e-signature, PO/invoice, supplier onboarding, sourcing/RFP, ERP replacement,
autonomous supplier comms, enterprise approval orchestration); none of them
maps to a §16/§20 success criterion, so none needs a screen.

## Empty / error / loading — in scope on the Day-1 path

Spec §7.1 encodes needs_review and failed (password-protected PDF) as
**first-class statuses, not edge cases**. Screens 3, 4, 7, 8, 9 must ship their
empty/error/loading states per ADR-018/019. Specifically named in the
prototype: upload failed + retry; portfolio no-match-for-filter and 503+retry;
Ask abstain and unknown-question fallback; renewal engine-unavailable; savings
benchmark-provider-unreachable with KPIs stale-labelled.

## Implications for the decomposition

- The decomposer must produce ≥1 `layer: web` story per screen above (screen 3
  may be two: upload UI + document-status read-back), each citing
  `inputs/design/prototypes/day1-demo.html` and `screens.md`.
- The final web-wave integration story walks the full Day-1 path (§20) in a
  browser on `demo`, matching the prototype — not `dotnet test`, not a Swagger
  page.
- Review/correct (screen 6) is a required screen, not optimisable away, because
  §7.3 (<80% require review) is a §16 R1 definition-of-success component.

## Assumptions

- The ten-screen prototype is complete enough to be the pixel reference for all
  of §20; any screen the implementer finds missing is a defect to raise, not a
  silent gap to fill with a divergent UI. (The export is 361KB and screen-complete
  per screens.md.)
