# ADR-028 — Server-side state for renewal actions, quote outcomes and negotiation steps, and how an outcome finds its savings opportunity

- **Status**: accepted
- **Date**: 2026-09-14
- **Deciders**: software-architect (owner); client-architect (contract shape, the three store retirements, read-back discipline — draft C5–C13); product-owner (ADR-001 w16 footer clauses 2–4: the read-back boundary, the four canonical steps, the status-move fence); security-architect (RLS on the one new tenant table, membership before every read); delivery-manager (phase order and the single-writer contract file)
- **Wave**: w16 — "nothing the product knows lives only in a browser tab"
- **Items served**: NW-11, NW-12, NW-13, NW-21
- **Locked citations**: modular monolith + background worker (ADR-002); PostgreSQL + EF Core/npgsql (ADR-003); RLS on every tenant table (ADR-009); CI applies checked-in idempotent SQL, never `MigrateAsync` (ADR-021); a client store never stands in for a missing GET, and one task owns the contract file per phase (ADR-012 §1/§3). None is re-opened here.

## Context and problem statement

Three browser stores hold facts the product presents as its own:
`raffa.renewals.actions` (`renewalActionStore.ts:28`),
`raffa.quotes.negotiationOutcomes` (`quoteOutcomeStore.ts:31`) and
`raffa.contract360.steps.<id>` (`negotiationStepsStore.ts:10`), all
`sessionStorage`. Two of the three already have a server row and no route to
read it back; the third has no server representation at all. A fourth item,
NW-21, has a complete server propagation path that nothing can reach because
the field that triggers it is never populated.

The wave is therefore **not** four features. It is one rule applied four times:
*a fact the product states must be readable from the server by the next
request, from any browser.* What each item needs is a route, a shape, and — for
exactly one of them — a table.

## Decision drivers

- **ADR-012 §1 (w14)**: a client store never stands in for a missing GET. The
  stores are the defect, not the design.
- **ADR-002's allow-lists are real and are enforced by a test**
  (`Raffa.ArchitectureTests.DependencyDirectionTests`). `Raffa.Renewals` and
  `Raffa.Savings` are both `[SharedKernel, Benchmark]`, so neither can reach a
  contract, a quote or a supplier. Where a decision needs two modules, the
  composition point is `Raffa.Api` and nowhere else.
- **Never invent a fact.** Spec §2's "AI is not the database" has a data-model
  twin: the server must not guess a link, and the client must not infer a
  server fact (ADR-012 w15 clause 4).
- **The cheapest honest shape.** Every one of these items can be closed without
  a schema change except one; the wave's schema delta should be exactly that
  one.
- **The 5-phase / 20-task cap** and ADR-012 §3's one-writer-per-phase rule on
  `web/openapi/raffa-api.v1.json`.

## Considered options

1. **A store-shaped API** — publish what each store holds, one route per store.
   Rejected: it would freeze the client's accidental shapes (a positional
   `boolean[4]`, a duplicated `supplierId`) into a contract.
2. **A generic "client state" table** keyed by `(tenant, user, key)` with a JSON
   blob. Rejected: it is `sessionStorage` with a database behind it — no
   tenant-level meaning, no queryability, no calculator can ever read it, and
   RLS would protect a document nobody can interpret.
3. **Per-item modelling against the rows that already exist** (chosen): three
   read paths over existing tables, one new table for the only fact with no
   server representation, and a resolution rule for the missing link.

## Decision outcome

### D1 — NW-11: the renewal action is read back on the surface that needs it, under a name that is free

- **`GET /api/renewals/{id}/action`** → `200` with the persisted row, `404`
  when none exists. `{id}` keeps **exactly** the meaning it has on the existing
  `POST /api/renewals/{id}/action`; the task confirms it at
  `RenewalsEndpointExtensions.cs:353` and the contract states it.
- **Plus the row embedded in every `GET /api/renewals` list row.** Renewals and
  Savings are *list* surfaces; a per-row GET would be an N+1 across the
  portfolio.
- **The embedded field is `savedAction`, never `action`.** `GET /api/renewals`
  already returns `action` (`RenewalsEndpointExtensions.cs:239`) and it is the
  **computed `RecommendedAction`** of a deterministic calculator. Reusing the
  name would overwrite a calculator's output with user state on a shipped
  screen. This is binding, not a preference (client-architect C9's hard
  constraint, adopted).
- **No schema change.** `RenewalActionService.GetActionAsync` already exists
  (`:158-171`) with zero production callers and its own comment (`:150-157`)
  anticipating this route; `renewal_action` is unique on
  `(tenant_id, contract_id)` and already carries its RLS policy
  (`20260904223135_AddTenantRowLevelSecurity.cs:42,52-54`).
- **The row is not widened** with `supplierId` / `annualSpend` (client C9): the
  client already holds both from fetches it has made.
- **Absence of a row is the status `NotStarted`, not a missing fact.** No
  `DELETE` route is published; "Undo" is a write of `NotStarted`, and the
  upsert on `(tenant_id, contract_id)` means the row legitimately survives it
  (client C8).

### D2 — NW-12: the quote is a resource; its outcomes hang off it

- **`GET /api/quotes`** — tenant-scoped list.
- **`GET /api/quotes/{id}`** — the quote **with its recorded negotiation
  outcomes embedded**, newest first. `NegotiationOutcome` is keyed by `QuoteId`
  and is append-only, so the outcomes are a property of the quote, not a
  tenant-wide feed.
- **The assessment endpoint is not overloaded.** Carrying the outcome on
  `GET /api/quotes/{id}/assessment` would save the client one call, but
  `POST /api/quotes/{id}/assessment/recalculate` returns that same shape — a
  recalculation would then appear to re-report a negotiation record it did not
  touch. One extra GET on mount is the cheaper of the two costs. *(This
  declines client-architect C10's shape preference, with the reason; the client
  keeps its single-call mount by reading `GET /api/quotes/{id}` instead of the
  assessment where it needs the outcome.)*
- **No `GET /api/negotiations/outcomes`.** A bare tenant-wide outcome list has
  no caller until NW-57 (W18) builds the history surface, and publishing it now
  would pre-empt that item's product shape.
- **New `QuoteQueryService`** in `Raffa.Quotes.Application` (none exists;
  analogues are `PortfolioQueryService` / `DocumentQueryService`). It returns
  stored fields and computes nothing.
- **No schema change** — `quote` and `negotiation_outcome` are both already
  RLS-enabled (`20260905123141_…:42-44`, `20260905170108_…:28`).

### D3 — NW-13: one new table, owned by the contracts module, keyed by the step's name

Answers **OQ-w16-006**'s module half (product-owner ruled its product half in
ADR-001's w16 footer clause 3).

- **Owner: `Raffa.Documents.Contracts`.** `Raffa.Renewals` cannot own it — its
  ADR-002 allow-list is `[SharedKernel, Benchmark]`
  (`RenewalActionService.cs:23`), so it cannot reference a contract.
- **Routes `GET` / `PUT /api/contracts/{id}/negotiation-steps`**, following
  `GET /api/contracts/{id}/corrections` verbatim
  (`ContractsEndpointExtensions.cs:66`, handler `:431-470`: resolve the tenant
  first, non-GUID → 400, unknown contract → 404).
- **One new tenant table `contract_negotiation_step`**, unique
  `(tenant_id, contract_id, step)`, with `ENABLE` + `FORCE ROW LEVEL SECURITY`
  and its `tenant_isolation` policy **in the same migration as the table**
  (ADR-009; the rule ADR-003's w14 footer already applied to
  `workspace_invitation`).
- **`step` is a closed enum stored by name**, never an array index and never an
  ordinal — the convention `NegotiationOutcome.LeversUsed` already uses
  (`:112-116`). The four names are the design oracle's
  (`screens-v2.md:106-108`). The client's `boolean[4]`
  (`negotiationStepsStore.ts:11,18,24,31`) carries no names at all, and two of
  the four rendered labels are parameterized
  (`contract360ViewModel.ts:211-218`) — an index key silently re-points every
  tick the day a step is inserted, and a stored label would freeze a fact that
  changes.
- **`PUT` of the whole set**, idempotent, no merge semantics: the client holds
  all four ticks and writes the set. An unknown step name is a 400; a missing
  one is untucked.
- The **rendered label stays client-side** (ADR-001 w16 clause 3); the wire
  carries keys only.

### D4 — NW-13: "Undo" is two idempotent writes, not one composed call

Answers **OQ-w16-ca-02**. The client asked for one call so it has no
partial-failure state to invent.

- **Ruling: two calls — the ticks `PUT` first, then the action `POST`** — and
  the client invents nothing because **both writes are idempotent and both
  surfaces already re-read on mount**: the recovery from a partial failure is a
  re-read, never a compensating write.
- **Rejected: composing the tick-clear into the renewal-action POST.** It would
  make a write named "set the renewal action" silently mutate another module's
  table through `Raffa.Api`, so any future non-UI caller posting `NotStarted` —
  a script, a test, the W17 operator console — would erase a negotiation record
  it never named. Ticks-first is the ordering that leaves the less misleading
  intermediate state (an action still in progress with no ticks reads as
  honest; `NotStarted` with four ticks reads as a contradiction).

### D5 — NW-21: the link is named, or deterministically resolved, and never guessed

- **No column, no migration, no contract delta, no client change.** The field
  is already on the wire (`NegotiationOutcomeCaptureRequest.cs:55`), on the
  entity (`NegotiationOutcomeService.cs:173`), typed in the generated client
  (`client.ts:877`), and the propagation behind it is complete
  (`NegotiationsEndpointExtensions.cs:97` → `PropagateAsync:111-171` →
  `SavingsOpportunityService.UpdateAsync`, which sets `Status = Realized` and
  inserts the `RealizedSavings` row, `:292,300-308`).
- **Precedence, in this order:**
  1. **`savingsOpportunityId` supplied in the body → used verbatim.** This is
     the acceptance path for A16-8 and it works today.
  2. **Absent → `Raffa.Api` resolves it, using the product's own definition of
     supplier identity, or records the outcome unlinked.**
     `SupplierNameNormalizer.Normalize(quote.Supplier)` → a **lookup-only**
     match against the `(tenant_id, normalized_name)` unique index
     (`SupplierConfiguration.cs:39-41`, R-SUP-02 / us-01-supplier-identity
     AC-2) → the tenant's open opportunities carrying that `SupplierId` →
     **exactly one ⇒ link; zero or two or more ⇒ unlinked**,
     `savingsPropagated: null`, outcome still recorded `201`.
- **This is resolution, not inference**: the matching rule is the one the
  product already uses to decide that "Salesforce, Inc." and "salesforce" are
  one supplier, enforced by a unique index rather than by a heuristic. Where it
  is ambiguous it declines — which is product-owner's ADR-001 w16 clause 4
  verbatim ("may be resolved, never guessed").
- **`SupplierResolver` is never called on this path.** It resolves *or creates*
  (`SupplierConfiguration.cs:9-12` describes its check-then-act insert):
  recording a negotiation outcome must never mint a supplier row as a side
  effect. `Raffa.Suppliers.Products` gains **one read-only name → id lookup**;
  the existing `ISupplierNameLookup` maps id → name only
  (`SupplierNameLookup.cs:16-29`).
- **Rejected: `savings_opportunity.quote_id` + migration.** It would invent the
  quote-originated opportunity `SavingsOpportunity.cs:23-27` explicitly places
  in R4 ("that future task's own migration to add"), and **nothing in this wave
  would populate it** — opportunities originate from a contract comparison,
  before any quote exists.
- **Rejected: a client-side match** on `contractId` / `supplierId` — the client
  would be inferring a server fact (ADR-012 w15 clause 4), and it holds neither
  side of the join reliably.
- **Answers OQ-w16-ca-03**: server-side resolution, so
  `quotes/index.tsx:274-282` is unchanged, NW-21's client cost is zero and
  **ux-ui-designer stays unseated** — no picker control, no new empty state.

### D6 — what this ADR deliberately does not deliver

- **The realized *amount* in the Savings KPI.** `SavingsKpiCalculator.cs:103-104`
  sums `EstimatedSavingsLow/High` in every bucket including `Realized` (the gap
  is documented at `:46-54`). Product-owner ruled the status move for w16 and
  the amount as the head of W17 (ADR-001 w16 clause 4, OQ-w16-po-01). **No w16
  task changes that calculator.**
- **A cross-quote outcome history, levers or an Ask-citable list** — NW-57,
  W18 (`screens-v2.md:139-145`).
- **Any read-back of a fact this wave does not already store.**

## Implications for the decomposition

**Contract deltas (`web/openapi/raffa-api.v1.json`) — five, all in theme B:**
`GET /api/renewals/{id}/action` (new); the `savedAction` field on
`GET /api/renewals` rows (existing path); `GET /api/quotes` (new);
`GET /api/quotes/{id}` (new); `GET`+`PUT /api/contracts/{id}/negotiation-steps`
(new). **NW-21 has zero contract delta** — so five items touch that file this
wave, not six (NW-08 and NW-31 in theme A; NW-11, NW-12, NW-13 in theme B), and
the intake's two-contract-task split fits inside the 5-phase cap with room.

**Migrations — exactly one in the wave**: `Raffa.Documents.Contracts` gains
`contract_negotiation_step` + its RLS policy in a single migration, regenerating
`Migrations/Scripts/documents-contracts.sql` (byte-compared by its stale-check
test, never hand-edited, one writer). **No CI YAML moves** — ADR-021's arrays
list modules and that module is already listed
(`.github/workflows/backend.yml:277-285`, `:309-317`). See the ADR-003 and
ADR-021 w16 footers.

**Single-writer files**: `RenewalsEndpointExtensions.cs` (NW-11),
`QuotesEndpointExtensions.cs` (NW-12), `ContractsEndpointExtensions.cs`
(NW-13), `NegotiationsEndpointExtensions.cs` +
`NegotiationOutcomePropagationService.cs` (NW-21), `Program.cs` (NW-13 only —
the one new service registration this ADR creates). **Theme A lands before
theme B**: NW-32 changes the signatures of the same five services NW-11, NW-12
and NW-21 modify.

**Tests each task carries**: a second-caller read-back (write, then read as a
different session, and get the same fact); a cross-tenant negative on every new
route (tenant B never sees tenant A's action, quote, outcome or tick); for D3 a
migration test that the policy ships with the table; for D5 the three-way
resolution test — named id, unambiguous name match, and ambiguous/zero match
recorded **unlinked** with `savingsPropagated: null`.

## Assumptions

1. `{id}` on `POST /api/renewals/{id}/action` is the **contract** id (the
   service keys on `ContractId`); the NW-11 task confirms at
   `RenewalsEndpointExtensions.cs:353` before the contract is written.
2. The pilot corpus may contain **no** quote whose supplier name resolves
   unambiguously. That is expected and harmless: D5's clause 2 is a
   reachability improvement, and **A16-8 closes on clause 1's explicit-id
   path**, which is deterministic.
3. Ticks are **per contract**, not per renewal cycle (ADR-001 w16 clause 3). A
   per-cycle key would add one column to the unique constraint — cheap now,
   a migration later.
4. The three retiring stores are deleted **with** their read-back, never
   before it (client-architect's ordering ask).

## Amendment (2026-09-14, wave w16 — table round 2)

Product-owner ruled **OQ-w16-sa-01** (ADR-001 w16 clause 7): **§D5 clause 2 —
the deterministic supplier-name resolution — ships in w16.** That ratifies the
assumption this ADR was written under, so **nothing in §D5 changes**; the
Decision outcome above stands verbatim. This footer records the ratification,
gives its second fence a server-side mechanism, and reports three checks run at
the table. Items: NW-21 (and one cross-check for NW-31).

**1. Fence 2 is structural, not a rendering promise.** Product-owner's *must
not* — an outcome with `savingsPropagated: null` may never be presented as a
realized saving and no total may absorb it — is enforced **in the data path, by
construction**, not by asking a screen to behave. When clause 2 declines (zero
matches, or two or more), `PropagateAsync` is **never invoked**: no
`savings_opportunity` row is updated, `Status` stays `Identified`, and no
`RealizedSavings` row is inserted (`SavingsOpportunityService.UpdateAsync`
`:292,300-308` is not reached). `SavingsKpiCalculator` reads opportunity rows,
so a total *cannot* absorb an outcome that wrote none. **Test the NW-21 task
carries**: capture a declining outcome and assert the Savings KPI payload is
**byte-identical before and after**. A fence with a test is a fence; a fence in
prose only is a wish.

**2. Clause 2 costs no contract delta — verified on disk at this table, because
the phase plan turns on it.** `savingsPropagated` is already a **required**
property typed `["boolean","null"]` (`web/openapi/raffa-api.v1.json:4356`,
`:4415-4419`) and already typed in the generated client
(`web/src/api/generated/schema.ts:504`); the property carries **no
`description`**, so clause 2 edits no JSON. **§"Implications" is unchanged:
five contract paths, all theme B, two contract tasks.** Waves-record constraint
1 does not move and the ruling adds **no sixth writer** on
`raffa-api.v1.json`.

**3. What clause 2 *does* make stale — and the NW-21 task must correct it.**
`backend/README.md:2296-2304` states the three fields are "`null`/absent-
equivalent together **whenever the caller supplied no `savingsOpportunityId`**".
Under clause 2 that sentence is **false**: with no id supplied the resolver may
link, making `savingsPropagated` `true`/`false`. The same reading is carried by
the doc comment at `NegotiationsEndpointExtensions.cs:53`. **Corrected rule**:
*`savingsPropagated` is `null` exactly when no opportunity was linked — because
none was named **and** none resolved unambiguously; it is `true`/`false`
whenever a link was attempted, by either route.* This is **prose inside files
NW-21 already owns** (`NegotiationsEndpointExtensions.cs` is its single-writer
file), so it adds no task, no writer and no phase. It is recorded rather than
left to the task's judgement because a doc asserting a behaviour the code no
longer has is exactly the stale-record class **NW-31 deletes this wave** —
shipping clause 2 while `README:2300` still states the old rule would re-create
that defect inside the same wave.

**4. The green tests that pin today's semantics stay valid.**
`NegotiationOutcomePropagationEndToEndTests.cs:65,114` and
`R4EndToEndTests.cs:339` all assert on the **explicit-id** path, which clause 1
leaves untouched; clause 2 only adds the no-id case. §"Tests each task carries"
already names the three-way resolution test that covers it.

**5. Cross-check for ADR-001 w16 clause 8 (NW-31) — reachability only; the
wording is product-owner's and is not re-decided here.** Clause 8's acceptance
names "an Admin resubmits a document on deployed `dev` **through the product's
own path**". Verified reachable **today, with zero client work**:
`web/src/routes/documents/useDocumentsList.ts:240` calls
`apiClient.reprocessDocument(workspace.id, documentId)` →
`web/src/api/client.ts:1943-1950`, which sends **only `X-Tenant-Id` and the
bearer** — no `X-Role`, no `X-Workspace-Role`, no `X-User-Id` — and already
renders the membership-derived refusal on 403 (`client.ts:1967`). **NW-31's
header deletion therefore cannot break the acceptance path clause 8 names**:
the wave's two round-2 rulings are compatible, and the re-worded acceptance
smuggles in no client task.

**Unchanged by this footer**: §D1–D4 and §D6 in full; the money-rendering fence
(§D6, ADR-001 w16 clause 4); `SupplierResolver` is still never called on this
path; the client cost of NW-21 is still **zero**, so **ux-ui-designer stays
unseated**. Assumption 2 above is now **Fence 1** rather than a risk: A16-8's
deterministic close is the explicit-id path, and a resolver that never fires on
the pilot corpus is a **pass**, never a failed acceptance.
