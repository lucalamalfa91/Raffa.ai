---
id: feature-04
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-04 F03, epic-05 F03
---

# feature-04-outcome-links-savings — A recorded outcome moves the opportunity it belongs to

## Slice

`NegotiationOutcomePropagationService.PropagateAsync` is complete and correct:
it sets `Status = Realized` and inserts a `RealizedSavings` row. It is simply
never reached, because the guard at `NegotiationsEndpointExtensions.cs:97` only
fires when the request body carries `savingsOpportunityId`, and the single
production caller (`web/src/routes/quotes/index.tsx:274-282`) builds six fields
and omits it. So every capture reports `savingsPropagated: null`.

This feature adds the missing half **server-side**: when no id is supplied,
`Raffa.Api` resolves one through the product's own supplier-identity rule —
`SupplierNameNormalizer` plus the `(tenant_id, normalized_name)` unique index —
and links **only if exactly one** open opportunity matches. Zero, two or more ⇒
the outcome is still recorded `201`, **unlinked**, `savingsPropagated: null`, and
nothing moves.

**No column, no migration, no contract delta, no client change.**

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | outcome-resolves-the-opportunity | w16 |

## Architecture decisions in force

- **ADR-028 §D5** — the two-step precedence (explicit id verbatim, else resolve
  or decline); **`SupplierResolver` is never called on this path** (it resolves
  *or creates*, and recording an outcome must never mint a supplier row);
  `savings_opportunity.quote_id` and a client-side match are both rejected.
- **ADR-028 w16 round-2 footer** — **Fence 2 is structural**: when the resolver
  declines, `PropagateAsync` is **never invoked**, so no opportunity row updates,
  `Status` stays `Identified` and no `RealizedSavings` row is inserted. The task
  carries the byte-identical-KPI test. The footer also names the stale prose this
  creates (`backend/README.md:2296-2304` and the doc comment at
  `NegotiationsEndpointExtensions.cs:53`) and corrects it inside this item's own
  single-writer file.
- **ADR-001** w16 clauses 4 and 7 — A16-8 closes on the **status** move;
  **no w16 task may render `SavingsKpiSummary.Realized` as a money amount**;
  "resolved, never guessed"; **Fence 1** — the acceptance walk drives the
  explicit-id path and a decline is a **pass**, never a failed acceptance.
- **ADR-002** w16 clauses 2–3 — composed in `Raffa.Api`;
  `Raffa.Suppliers.Products` gains one **read-only** name → id lookup; no
  allow-list widens.
- **ADR-012** w16 clause 29 — client cost is **zero**; ux-ui-designer stays
  unseated.

## Target repo

`raffa-backend`
