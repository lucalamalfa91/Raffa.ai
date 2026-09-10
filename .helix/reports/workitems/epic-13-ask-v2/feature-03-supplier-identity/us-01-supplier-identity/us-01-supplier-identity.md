---
id: us-01
type: user-story
parent: feature-03
wave: 13
status: active
---

# us-01-supplier-identity — Contracts are named by supplier everywhere

## Story

As **procurement**, I want every contract to carry its supplier's name —
in Documents, Portfolio, Renewals and in Ask ("your Allianz contract",
"Salesforce · MSA 2024 · p.12 §8.4") — so that I can talk to Raffa about
a deal the way I name it, not by a guid.

## Acceptance criteria

- [ ] AC-1 A `Supplier` row (tenant-scoped, RLS) exists with name,
      normalized name, aliases, category, country; another tenant cannot
      read it even with a guessed id.
- [ ] AC-2 `ISupplierResolver.ResolveAsync(tenant, name)` matches by
      normalized name or alias, else creates; "Salesforce, Inc." and
      "salesforce" resolve to one row.
- [ ] AC-3 Staged extraction emits the `supplier` fact (value, page, span,
      confidence) as a **critical field**; a weak supplier fact (< 0.8)
      lands in review like any critical field; after extraction
      `Contract.SupplierId` is set through the resolver.
- [ ] AC-4 Reprocessing an existing document back-fills `SupplierId`; the
      Documents list, Portfolio and Renewals API responses carry the
      supplier name.
- [ ] AC-5 A question naming a supplier that is not in the workspace
      resolves to "no such validated contract" (needs-document path),
      never to a wrong contract.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-v2-foundation | `Raffa.Suppliers.Products.Tests` project exists (T01) |
| us-01-documents-v2 | reprocess path (T02) for the back-fill |

## Architecture decisions in force

- ADR-024 — supplier identity; `Raffa.Suppliers.Products` → `[SharedKernel]`
- ADR-002 — Documents may not reference Suppliers; port in SharedKernel; composition in `Raffa.Api`
- ADR-009 — RLS; ADR-021 — `suppliers.sql` in the CI apply list
- spec §7.3 — critical fields

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Supplier entity, resolver port, migration + SQL, RLS | M | phase-2 |
| T02 | `supplier` critical fact, pipeline resolver call, back-fill via reprocess | L | phase-3 |

## Council decisions carried into this story

Port: `Raffa.SharedKernel.Suppliers.ISupplierResolver` (`ResolveAsync(TenantId, string rawName, CancellationToken) → Result<EntityId>`).
Normalization: lower-case, strip legal suffixes (Inc, Ltd, GmbH, AG, SA, SpA, S.r.l., LLC, Corp), punctuation and whitespace. Table `supplier`
with unique `(tenant_id, normalized_name)`. Schema script
`backend/src/Raffa.Suppliers.Products/Migrations/Scripts/suppliers.sql`
added to the fixed-order SCRIPTS list in `.github/workflows/backend.yml`
(Apply schema + Verify schema applied).

## Open questions

- none
