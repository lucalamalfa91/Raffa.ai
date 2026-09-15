---
id: us-01
type: user-story
parent: feature-04
wave: w16
status: active
---

# us-01-outcome-resolves-the-opportunity — A recorded outcome moves its savings opportunity

## Story

As a **Procurement member**, I want the negotiation outcome I record to move the
savings opportunity it belongs to, so that **the savings picture reflects the
deal I actually closed instead of staying on an estimate forever**.

## Acceptance criteria

- [ ] AC-1 `POST /api/negotiations/outcomes` **with** `savingsOpportunityId`
  links verbatim: that opportunity's status becomes `Realized`, its
  `realizedAmount` equals the outcome's `realizedSaving`, and the `201` reports
  `savingsPropagated: true`. *(This is A16-8's deterministic path and it works
  today.)*
- [ ] AC-2 `POST /api/negotiations/outcomes` **without** the id, where the
  quote's supplier name normalizes to **exactly one** open opportunity in the
  tenant, links to that opportunity and reports `savingsPropagated: true`.
- [ ] AC-3 The same call where **zero** or **two or more** open opportunities
  match: the outcome is still recorded `201`, **unlinked**,
  `savingsPropagated: null`, and **nothing moves** — no opportunity row updates,
  `Status` stays `Identified`, no `RealizedSavings` row is inserted.
- [ ] AC-4 Under AC-3, the `GET /api/savings` KPI payload is **byte-identical**
  before and after the call.
- [ ] AC-5 Recording an outcome **never creates a supplier row**.
- [ ] AC-6 A second browser agrees with AC-1 and AC-2.
- [ ] AC-7 The resolution never crosses a tenant: an opportunity of tenant B is
  never linked to an outcome of tenant A.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-028 §D5 + the w16 round-2 footer, ADR-001 w16
  clauses 4 and 7, ADR-002 w16 clauses 2–3
- [ ] **no column, no migration, no contract delta, no client change** was added
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E18/F03/US02/T01` (NW-32) | `NegotiationOutcomeService` and `SavingsOpportunityService` gain the required actor there, and `NegotiationOutcomePropagationService` is edited to pass it; strictly earlier phase |

## Architecture decisions in force

- **ADR-028 §D5** — the precedence; the lookup is against the
  `(tenant_id, normalized_name)` **unique index**, not a heuristic;
  **`SupplierResolver` is never called on this path** (it resolves *or creates*);
  `savings_opportunity.quote_id` and any client-side match are rejected.
- **ADR-028 w16 round-2 footer** — Fence 2 holds **by construction**, with the
  byte-identical-KPI test as its proof; and the footer's corrected rule replaces
  the now-false prose in this item's own files.
- **ADR-001** w16 clause 4 — the **status** move closes A16-8; **no w16 task may
  render `SavingsKpiSummary.Realized` as a money amount**; resolved, never
  guessed. Clause 7 — Fence 1: a decline is a **pass**.
- **ADR-002** w16 clauses 2–3 — composed in `Raffa.Api`; one read-only lookup in
  `Raffa.Suppliers.Products`; no allow-list widens.
- **ADR-009** w16 clause 3b — the resolver is an **ordinary tenant-scoped read**
  inside the request's own scope. *There is no cross-tenant read in this product.*

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | outcome-resolves-the-opportunity | M | phase-2 |

## Council decisions carried into this story

> **ADR-028 §D5**: "exactly one ⇒ link; zero or two or more ⇒ unlinked,
> `savingsPropagated: null`, outcome still recorded `201`."

> **ADR-028 w16 round-2 footer, corrected rule, verbatim**: "`savingsPropagated`
> is `null` exactly when no opportunity was linked — because none was named
> **and** none resolved unambiguously; it is `true`/`false` whenever a link was
> attempted, by either route."

> **ADR-001 w16 clause 7, Fence 1**: "A16-8's deterministic close is the
> explicit-id path. Clause 2's *decline* is a **pass, not a failure**; no
> acceptance step may depend on the resolver firing." The pilot corpus may
> contain no quote whose supplier resolves unambiguously — expected and harmless.

## Open questions

- **OQ-w16-005** — ruled (ADR-001 w16 clause 4): the **status** move. The
  realized-amount fix is an unmet AC of the still-`active` `E04/F03/US01` and is
  the **head of W17** (OQ-w16-po-01). No task here.
- **OQ-w16-sa-01** — ruled (ADR-001 w16 clause 7): §D5 clause 2 ships in w16.
- **OQ-w16-ca-03** — answered: server-side ⇒ zero client edit ⇒ ux-ui-designer
  stays unseated.
