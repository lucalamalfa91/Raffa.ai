---
id: E19/F04/US01/T01
type: task
story: us-01-outcome-resolves-the-opportunity
wave: w16
status: live
target_repo: raffa-backend
---

# task-01-outcome-resolves-the-opportunity — Resolve the savings link server-side, or record the outcome unlinked

## Coding objective

`NegotiationOutcomePropagationService.PropagateAsync`
(`backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs:111-115`) is
complete: it sets `Status = Realized` and inserts the `RealizedSavings` row. It is
never reached, because the guard at
`backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs:97`
(`if (outcome.SavingsOpportunityId is { } savingsOpportunityId)`) only fires when
the request body carries the id, and the single production caller omits it — so
the `201` always reports `savingsPropagated: null` (`:119-120`).

Add the missing half **in `Raffa.Api`**, which is the only project allowed to see
both `Raffa.Quotes` and `Raffa.Savings`. Precedence, in this order:

1. **`savingsOpportunityId` supplied → used verbatim.** Unchanged behaviour. This
   is A16-8's acceptance path and it works today.
2. **Absent → resolve, or decline.** Normalize the quote's supplier name with
   `backend/src/Raffa.Suppliers.Products/Application/SupplierNameNormalizer.cs`,
   look the supplier up **read-only** against the `(tenant_id, normalized_name)`
   unique index
   (`backend/src/Raffa.Suppliers.Products/Infrastructure/Configurations/SupplierConfiguration.cs:39-41`),
   then find the tenant's **open** opportunities carrying that `SupplierId`.
   **Exactly one ⇒ link. Zero, or two or more ⇒ decline**: the outcome is still
   recorded `201`, `savingsPropagated` stays `null`, and **`PropagateAsync` is
   never invoked**.

Then correct the two prose records this makes false — `backend/README.md:2296-2304`
and the doc comment at `NegotiationsEndpointExtensions.cs:53` — both of which
still say the three fields are null together "whenever the caller supplied no
`savingsOpportunityId`".

**No column. No migration. No contract delta. No client change.**

## Parent story AC covered

- AC-1 Explicit id → `Realized`, `realizedAmount` equals the outcome's `realizedSaving`, `savingsPropagated: true`.
- AC-2 No id, exactly one supplier-name match → linked, `savingsPropagated: true`.
- AC-3 No id, zero or ≥2 matches → recorded `201`, unlinked, `savingsPropagated: null`, nothing moves.
- AC-4 Under AC-3 the `GET /api/savings` KPI payload is byte-identical before and after.
- AC-5 Recording an outcome never creates a supplier row.
- AC-6 A second browser agrees with AC-1 and AC-2.
- AC-7 The resolution never crosses a tenant.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs` | the call site at `:95-97` asks the resolver when the body carries no id; the doc comment at `:53` is corrected to the footer's rule |
| `backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs` | the resolution step (normalize → lookup → exactly-one) before `PropagateAsync`; the stale "ADR-010 is not wired in yet" comment at `:94-96` is corrected — ADR-010 **was** wired in w15 and `SystemActor` at `:97` is now permanent and correct |
| `backend/src/Raffa.Suppliers.Products/Application/SupplierNameLookup.cs` | one **read-only** normalized-name → supplier-id lookup added to `ISupplierNameLookup` (today it maps id → name only) |
| `backend/tests/Raffa.IntegrationTests/NegotiationOutcomeResolutionTests.cs` | new — the three-way resolution test and the byte-identical-KPI assertion |
| `backend/tests/Raffa.Suppliers.Products.Tests/` | the lookup's unit cover, including that it never writes |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-21`.

Decision row: `reports/architecture/waves/w16.md`, **NW-21** (software-architect,
product-owner, client-architect), plus the **ADR-028 w16 round-2 footer**.

- **Architecture decisions in force**: **ADR-028 §D5** (the precedence, the
  unique-index lookup, the rejected alternatives) and the **w16 round-2 footer**
  (Fence 2 is structural; clause 2 costs no contract delta; the stale prose this
  creates is corrected inside this task's own files); **ADR-001** w16 clauses 4
  and 7; **ADR-002** w16 clauses 2–3 (composed in `Raffa.Api`; one read-only
  lookup; no allow-list widens); **ADR-009** w16 clause 3b (an ordinary
  tenant-scoped read inside the request's own scope — there is no cross-tenant
  read in this product).
- **`SupplierResolver` is NEVER called on this path.** It resolves **or creates**
  (`backend/src/Raffa.Suppliers.Products/Application/SupplierResolver.cs`), and
  recording a negotiation outcome must not mint a supplier row as a side effect.
  Use `SupplierNameNormalizer` + a lookup, nothing else.
- **Resolved, never guessed** (ADR-001 w16 clause 4): ambiguity ⇒ decline. This
  is the rule the product already uses to decide "Salesforce, Inc." and
  "salesforce" are one supplier, enforced by a unique index rather than a
  heuristic. **Never a nearest match.**
- **Fence 2, and why it is structural rather than a promise**: when the resolver
  declines, `PropagateAsync` is not invoked, so no opportunity row updates,
  `Status` stays `Identified`, and no `RealizedSavings` row is inserted. Because
  `SavingsKpiCalculator` reads opportunity rows, **no total can absorb an outcome
  that wrote none**.
- **Fence 1** (ADR-001 w16 clause 7): A16-8's deterministic close is the
  **explicit-id** path. The pilot corpus may contain no quote whose supplier name
  resolves unambiguously — that is expected and harmless, and **a decline is a
  pass, never a failed acceptance**.
- **The money fence** (ADR-001 w16 clause 4): **no w16 task may render
  `SavingsKpiSummary.Realized` as a money amount**, and
  `backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs:103-104` — which
  sums `EstimatedSavingsLow/High` in every bucket including `Realized` — **is not
  changed by this task**. That fix is the head of W17 (OQ-w16-po-01).
- **Design oracle** (recorded as the ground for the status-not-amount ruling):
  `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137` — the opportunities
  table column is **"Estimate"** and the KPI triple is *Contracts analyzed ·
  Upcoming renewals · Savings identified*. The oracle carries **no realized-money
  KPI at all**.
- **Do not touch**: `backend/src/Raffa.Api/QuotesEndpointExtensions.cs` or
  `backend/src/Raffa.Quotes/Application/` (that is `E19/F02/US01/T01`'s, **this
  same phase**); `backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs`;
  `web/**` (NW-21's client cost is **zero** —
  `web/src/routes/quotes/index.tsx:274-282` stays exactly as it is);
  `web/openapi/raffa-api.v1.json` (`savingsPropagated` is **already** a required
  `["boolean","null"]` property with no `description` at `:4356`, `:4415-4419`,
  verified at the table — clause 2 edits no JSON).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter NegotiationOutcomeResolutionTests` exit 0
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter NegotiationOutcomePropagationEndToEndTests` exit 0 — the existing explicit-id assertions (`:65`, `:114`) still pass unchanged
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter R4EndToEndTests` exit 0 — `:339`'s explicit-id assertion still passes
- [ ] `dotnet test backend/tests/Raffa.Suppliers.Products.Tests` exit 0
- [ ] `git diff --name-only -- web/ backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs` is **empty** for this task
- [ ] `grep -n "whenever the caller supplied no" backend/README.md` returns **no match**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| API | **the three-way resolution** — explicit id links; one unambiguous name match links; zero or ≥2 matches record the outcome unlinked with `savingsPropagated: null` | `backend/tests/Raffa.IntegrationTests/NegotiationOutcomeResolutionTests.cs` |
| API | **Fence 2** — capture a declining outcome and assert the `GET /api/savings` KPI payload is **byte-identical** before and after | `backend/tests/Raffa.IntegrationTests/NegotiationOutcomeResolutionTests.cs` |
| API | **cross-tenant negative** — an opportunity of tenant B is never linked to an outcome of tenant A | `backend/tests/Raffa.IntegrationTests/NegotiationOutcomeResolutionTests.cs` |
| unit | the name → id lookup is read-only: a name with no supplier returns nothing and **creates no row** | `backend/tests/Raffa.Suppliers.Products.Tests/` |

## Open questions blocking this task

- none. **OQ-w16-005** ruled (status move), **OQ-w16-sa-01** ruled (§D5 clause 2
  ships), **OQ-w16-ca-03** answered (server-side ⇒ zero client edit).
  **OQ-w16-po-01** — the realized **amount** in the KPI — is the head of W17 and
  generates no task here.

## Wave-spec entry

```yaml
- id: E19/F04/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-04-outcome-links-savings/us-01-outcome-resolves-the-opportunity/tasks/task-01-outcome-resolves-the-opportunity.md
  produces: [outcome-savings-link]
  depends_on: [actor-on-every-write]
  effort: M
  layer: backend
  status: live
```
