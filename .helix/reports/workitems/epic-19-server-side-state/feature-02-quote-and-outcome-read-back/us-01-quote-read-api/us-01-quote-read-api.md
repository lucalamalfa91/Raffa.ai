---
id: us-01
type: user-story
parent: feature-02
wave: w16
status: active
---

# us-01-quote-read-api — A quote and its recorded outcomes are readable over HTTP

## Story

As a **Procurement member**, I want to read a quote and the negotiation outcomes
recorded against it, so that **the outcome I recorded is still there after a
reload and visible to a colleague**.

## Acceptance criteria

- [ ] AC-1 `GET /api/quotes` returns the tenant's quotes, and only the tenant's.
- [ ] AC-2 `GET /api/quotes/{id}` returns the quote **with its recorded
  negotiation outcomes embedded, newest first**; a quote with no outcome returns
  an empty list, not `404`.
- [ ] AC-3 An unknown quote id returns `404`; a non-GUID id returns `400`.
- [ ] AC-4 An Admin of tenant B asking for tenant A's quote id gets `404` and
  **zero tenant-A fields in any form**.
- [ ] AC-5 `GET /api/quotes/{id}/assessment` and
  `POST /api/quotes/{id}/assessment/recalculate` are **byte-identical in shape**
  to before — the outcome is not carried on either.
- [ ] AC-6 No `GET /api/negotiations/outcomes` route exists.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-028 §D2, ADR-026 w16 clause 5, ADR-001 w16 clause 2
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E18/F03/US02/T01` (NW-32) | `QuoteUploadService`, `SkuMappingService` and `NegotiationOutcomeService` gain a required actor parameter there; strictly earlier phase |

## Architecture decisions in force

- **ADR-028 §D2** — both routes; outcomes hang off the quote; the assessment
  endpoint is **not** overloaded; **no tenant-wide outcome feed**; a new
  `QuoteQueryService` that **returns stored fields and computes nothing**; **no
  migration** (both tables are already RLS-enabled).
- **ADR-026** w16 clause 5 — the two contract paths are published by
  `E19/F06/US01/T01`. **This story does not open `web/openapi/raffa-api.v1.json`.**
- **ADR-001** w16 clause 2 — read-back only; the history / levers / Ask-citable
  list is **NW-57 (W18)**.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | quote-read-api | M | phase-2 |

## Council decisions carried into this story

> **ADR-028 §D2**: "The assessment endpoint is not overloaded. Carrying the
> outcome on `GET /api/quotes/{id}/assessment` is rejected because
> `POST /api/quotes/{id}/assessment/recalculate` returns that same shape — a
> recalculation would appear to re-report a negotiation record it did not touch.
> One extra GET on mount is the cheaper cost."

> **ADR-028 §D2**: "No `GET /api/negotiations/outcomes` — no caller until NW-57
> (W18); publishing now would pre-empt that item's product shape."

## Open questions

- none.
