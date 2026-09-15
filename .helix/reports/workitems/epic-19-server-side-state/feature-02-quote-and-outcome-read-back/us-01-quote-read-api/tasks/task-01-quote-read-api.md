---
id: E19/F02/US01/T01
type: task
story: us-01-quote-read-api
wave: w16
status: live
target_repo: raffa-backend
---

# task-01-quote-read-api — The quote becomes a readable resource, with its outcomes on it

## Coding objective

`backend/src/Raffa.Api/QuotesEndpointExtensions.cs:34-42` maps exactly three
routes — `POST /api/quotes` (`:36`), `GET /api/quotes/{id}/assessment` (`:38`),
`POST /api/quotes/{id}/assessment/recalculate` (`:40`) — and none of them reads a
quote back. `backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs:34` maps only
the outcome **POST**. The data is already in Postgres and already RLS-enabled.

Add a new **`QuoteQueryService`** in `backend/src/Raffa.Quotes/Application/`,
modelled on the existing `PortfolioQueryService` / `DocumentQueryService` read
services. **It returns stored fields and computes nothing.** Then map
**`GET /api/quotes`** (the tenant's quotes) and **`GET /api/quotes/{id}`** (the
quote **with its recorded negotiation outcomes embedded, newest first**) in
`QuotesEndpointExtensions.cs`, using the same `ICallerContext.ResolveTenantAsync`
ladder the sibling routes already use.

`NegotiationOutcome` is keyed by `QuoteId` and is append-only, so the outcomes
are a property of the quote. **Do not add a tenant-wide
`GET /api/negotiations/outcomes`** and **do not carry the outcome on the
assessment endpoint**.

## Parent story AC covered

- AC-1 `GET /api/quotes` returns the tenant's quotes and only the tenant's.
- AC-2 `GET /api/quotes/{id}` embeds the recorded outcomes, newest first; none ⇒ empty list, not `404`.
- AC-3 Unknown id `404`; non-GUID id `400`.
- AC-4 Tenant B asking for tenant A's quote gets `404` and zero tenant-A fields.
- AC-5 The assessment and recalculate shapes are byte-identical to before.
- AC-6 No `GET /api/negotiations/outcomes` route exists.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Quotes/Application/QuoteQueryService.cs` | new — `ListAsync(tenantId, ct)` and `GetAsync(tenantId, quoteId, ct)`; the second projects the quote plus its `negotiation_outcome` rows ordered newest first |
| `backend/src/Raffa.Quotes/Infrastructure/ServiceCollectionExtensions.cs` | register the new query service in the module's own registration |
| `backend/src/Raffa.Api/QuotesEndpointExtensions.cs` | map `GET /api/quotes` and `GET /api/quotes/{id}` + their handlers |
| `backend/tests/Raffa.IntegrationTests/QuoteReadBackTests.cs` | new — both routes, the embedding order and the cross-tenant negative |
| `backend/tests/Raffa.Quotes.Tests/` | the query service's unit cover |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-12` (its API half; the screen half is `E19/F02/US02/T01`).

Decision row: `reports/architecture/waves/w16.md`, **NW-12**
(software-architect, client-architect, product-owner).

- **Architecture decisions in force**: **ADR-028 §D2** (both routes; outcomes
  hang off the quote; a new `QuoteQueryService` that computes nothing; **no
  migration** — `quote` and `negotiation_outcome` are already RLS-enabled at
  `backend/src/Raffa.Quotes/Migrations/20260905123141_AddTenantRowLevelSecurity.cs:42-44`
  and `20260905170108_AddNegotiationOutcome.cs:28`); **ADR-026** w16 clause 5
  (the contract publication belongs to `E19/F06/US01/T01`); **ADR-001** w16
  clause 2 (read-back only).
- **Why the assessment endpoint is not overloaded** — ADR-028 §D2, and this is
  the decline's stated reason: `POST /api/quotes/{id}/assessment/recalculate`
  returns the **same shape** as the assessment GET, so a recalculation would
  appear to re-report a negotiation record it never touched. One extra GET on
  mount is the cheaper cost.
- **Why there is no outcomes feed**: a tenant-wide list has **no caller** until
  **NW-57 (W18)**, and publishing it now pre-empts that item's product shape.
- **Boundary to hold** (`inputs/design/prototypes/raffa-v2/screens-v2.md:139-145`):
  the Quote check screen shows lines and market band; "Target and negotiation
  levers are one step further". The cross-quote **history** UX is NW-57's, not
  this wave's.
- **Do not touch**: `backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs` or
  `backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs` — those are
  `E19/F04/US01/T01`'s single-writer files **in this same phase**;
  `web/openapi/raffa-api.v1.json`, `web/src/api/client.ts`,
  `web/src/api/generated/schema.ts` (all `E19/F06/US01/T01`, one phase later);
  `backend/tests/Raffa.IntegrationTests/R0IntegrationFixture.cs` (a sibling of
  this phase is rewriting it).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Quotes.Tests` exit 0
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter QuoteReadBackTests` exit 0
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter R4EndToEndTests` exit 0 — the assessment and recalculate shapes did not move
- [ ] `grep -rn "api/negotiations/outcomes" backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs | grep -c MapGet` is **0**
- [ ] `git diff --name-only -- web/ backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs` is **empty** for this task

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| API | `GET /api/quotes/{id}` returns the quote with its outcomes newest first; a quote with none returns an empty list, not `404` | `backend/tests/Raffa.IntegrationTests/QuoteReadBackTests.cs` |
| API | **second-caller read-back** — record an outcome in session A, read the quote in a different session, see it | `backend/tests/Raffa.IntegrationTests/QuoteReadBackTests.cs` |
| API | **cross-tenant negative** — tenant B gets `404` on tenant A's quote id and `GET /api/quotes` never lists it | `backend/tests/Raffa.IntegrationTests/QuoteReadBackTests.cs` |
| unit | the query service returns stored fields only — no recomputation of the assessment | `backend/tests/Raffa.Quotes.Tests/` |

## Open questions blocking this task

- none.

## Wave-spec entry

```yaml
- id: E19/F02/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-02-quote-and-outcome-read-back/us-01-quote-read-api/tasks/task-01-quote-read-api.md
  produces: [quote-read-api]
  depends_on: [actor-on-every-write]
  effort: M
  layer: backend
  status: live
```
