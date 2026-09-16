---
id: E20/F01/US01/T01
type: task
story: us-01-savings-verified-kpi
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-savings-verified-kpi — read the verified amount and paint a fourth KPI cell

## Context

**Closes: NW-72.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-72** row
(product-owner, software-architect, client-architect, ux-ui-designer, all four
halves) and the rulings on **OQ-w17-006** and **OQ-w17-ux-04**.

ADRs in force: **ADR-001** w17 clauses 1 and 10; **ADR-028** w17 footer and
clauses 6 and 7; **ADR-012** w17 clauses 37, 39, 41 and §42–§44; **ADR-020** w17
§15, §17, §20–§21.

Already on `main`, so **not** a dependency to build: the `RealizedSavings`
entity, its `RealizedSavingsRecords` DbSet and the single `.Add` that writes it
(w16, NW-21). What is missing is a **reader** — there is exactly one `.Add` and
no consumer anywhere.

This task is the **phase-4 owner** of `web/openapi/raffa-api.v1.json` and of the
regenerated `web/src/api/generated/schema.ts` (contract-B). `E22/F01/US01/T01`
owned them in **phase 2** (contract-A); regenerate on top of that merged
contract, never from an older copy.

⚠ **`web/e2e/w17-savings.spec.ts` does not exist on this base** (`web/e2e/`
holds `day1.spec.ts`, `invite.spec.ts`, `v2.spec.ts`). **This task creates it**
and is its only writer in the wave — ADR-012 w17 clause 39 keeps `v2.spec.ts`
for NW-73's sweep alone and lands every new W17 case in a per-theme spec, and
clause 41 assigns **A17-S1** to this one. The two other phase-4 specs are
different files (`w17-viewer.spec.ts` is `E22/F04/US01/T01`'s extension,
`w17-review.spec.ts` is `E22/F05/US01/T01`'s), so the phase holds one writer
per file.

## Coding objective

In `raffa-backend`, make the Savings KPI report **verified money** instead of a
summed estimate, and paint it as a fourth cell in the web band.

1. **Change the realized shape.** In
   `backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs`, add
   `RealizedSavingsByCurrency(string Currency, decimal Amount, int Count)`
   beside `SavingsRangeByCurrency` (`:35`) and change
   `SavingsKpiSummary.Realized` (`:59`) to
   `IReadOnlyList<RealizedSavingsByCurrency>`. **Keep the member identifier
   `Realized`** and keep `Identified` (`:57`) and `InProgress` (`:58`) on
   `SavingsRangeByCurrency`. This is a **declared type change on one member, not
   a rename** (ADR-028 w17 clause 6).
2. **Stop summing estimates into it.** `Summarize` (`:77`) must no longer route
   `SavingsOpportunityStatus.Realized` through `Bucket` (`:86`, `:89`), whose
   `.Select` sums `EstimatedSavingsLow`/`EstimatedSavingsHigh` (`:103-104`).
   The calculator takes a **second input sequence** of realized rows and groups
   it by currency, summing `Amount` and counting rows. It stays **pure** — no
   new constructor parameter, no injected service (Appendix C rule 6).
3. **Read the rows.** In
   `backend/src/Raffa.Savings/Application/SavingsKpiQueryService.cs`, alongside
   the existing `dbContext.SavingsOpportunities` projection (`:32`, `:35-36`),
   project `dbContext.RealizedSavingsRecords` (declared
   `backend/src/Raffa.Savings/Infrastructure/SavingsDbContext.cs:29`) into a
   snapshot carrying `Amount` (`Domain/RealizedSavings.cs:45`) and `Currency`
   (`:51`), and pass it to `Summarize` as the second sequence. **Group by
   currency; never sum across currencies** — no conversion service exists
   (`SavingsKpiCalculator.cs:27-29` — the doc comment that states no
   currency-conversion service exists anywhere in this codebase; **not** `:40-44`,
   which is the tail of the `SavingsRangeByCurrency` record).
4. **Publish it.** In `backend/src/Raffa.Api/SavingsKpiEndpointExtensions.cs`,
   the `savingsRealized` key (`:105`) now emits objects of the new shape.
   **Do not rename the wire key.** `SavingsEndpointExtensions.cs`'s
   `realizedAmount` (`:171`) stays PATCH-only and is not touched.
5. **Update the contract and regenerate.** In
   `web/openapi/raffa-api.v1.json`, change the `savingsRealized` array item
   (property at `:5051`, required at `:4962`) from the range shape
   (`currency`/`low`/`high`/`count`/`averageConfidence`) to
   `currency`/`amount`/`count`. Leave `savingsIdentified` (`:4991`) and
   `savingsInProgress` (`:5021`) unchanged. Then run `npm run generate:api`
   in `web/` and commit the regenerated `web/src/api/generated/schema.ts`.
6. **Paint the fourth cell.** In `web/src/routes/savings/savingsViewModel.ts`,
   widen the closed `key` union on `KpiCellView` (`:61`) by one literal and make
   **both** branches of `buildKpiCells` (`:82`) return **four** cells in the same
   key order — the `kpis === null` branch (`:83-88`) and the ready branch
   (`:93-112`). Label: **"Savings verified"**. The cell's `lines` is one
   formatted money line **per currency**, exactly the idiom `:91`/`:98` already
   use for `annualSpendAnalyzed`; its `meta` carries provenance ("from N
   recorded outcomes"). Remove the `· N realized` fragment from the
   "Savings identified" cell (`:110`) and sweep the w16 money-fence comment at
   `:92`. Read the new body **without an alias**:
   `SavingsKpiSummaryBody["savingsRealized"][number]`, the inline idiom
   `web/src/api/client.ts:986-987` already use — **do not add a type alias to
   `client.ts`** (ADR-012 w17 clause 39: that is the escape that keeps that file
   at one writer this wave).
7. **Fix the skeleton arity.** `web/src/routes/savings/KpiRow.tsx:20` renders
   `Array.from({ length: 3 })`. Make it four, or the band reflows 3 skeleton
   cells into 4 real ones on every load (ADR-020 w17 §20).
8. **Zero state.** `lines: []` renders the existing `—` (`KpiRow.tsx:49`) with
   meta "no verified savings recorded yet". **Never `0`.** Correct
   `savingsViewModel.ts`'s own docstring at `:63-64`, which claims `lines` is
   "empty **only** when `kpis` is `null`" — that invariant is falsified by this
   state; the discriminator is `meta === null` (not loaded) vs `meta !== null`
   with empty `lines` (loaded and empty) (ADR-012 w17 §44, ADR-020 w17 §21).
9. **Sweep the two stale comments** that assert this gap still exists:
   `SavingsKpiCalculator.cs:42-55` (the paragraph at `:46-54`) and
   `backend/src/Raffa.Savings/Domain/SavingsOpportunityStatus.cs:36-45`.
   Both say no audit-tracked realized-value record exists. It exists and is now
   read.

10. **Create `web/e2e/w17-savings.spec.ts` — the wave's savings-theme spec**
    (ADR-012 w17 clauses 39 and 41). It carries **A17-S1**: record an outcome
    that realizes an opportunity, then the band shows **realized money grouped
    by currency** — one formatted line per currency — under the label **"Savings
    verified"**, and that figure **agrees after a reload and in a second browser
    context** (AC-3: the server is the record, not the tab). Two negative cases
    travel with it: an outcome whose `savingsPropagated` is `null` moves **no**
    figure (AC-5), and the pre-negotiation **estimate** never appears in that
    cell (AC-2). ⚠ **Do not open `web/e2e/v2.spec.ts`** — clause 39 reserves it
    for NW-73's stale-prose sweep (`E20/F02/US02/T01`, phase 2), and it is the
    collision that rule exists to prevent. Per clause 31 and ADR-012 §12 this
    spec is **acceptance-runbook evidence, not a CI gate**: no workflow runs
    Playwright, and no line of this task may present it as one.

**Nothing is renamed this wave.** `RealizedSavings`,
`RealizedSavingsRecords`, `RealizedAmount`, `realizedAmount`, `Realized` and
`savingsRealized` all keep their names; "Savings verified" stops at the view
model (ADR-028 w17 clause 6).

## Parent story AC covered

- AC-1 Recording an outcome that realizes an opportunity makes the Savings KPI band show a **money figure** for verified savings — one formatted line per currency — not only a count. (A17-S1)
- AC-2 The cell is labelled **"Savings verified"**. The word "Realized" — the estimate bucket's own word — never labels it.
- AC-3 Reloading the page shows the same figure, and a **second browser** signed in to the same workspace agrees.
- AC-4 Currencies are **never summed**.
- AC-5 An outcome whose `savingsPropagated` is `null` enters **no** total.
- AC-6 With no verified savings recorded, the cell renders **`—`** with the meta line "no verified savings recorded yet" — never `0` and never an absent cell. The band shows **four** cells in every state.
- AC-7 The verified figure comes from the `RealizedSavings` rows.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs` | modify — add `RealizedSavingsByCurrency`; `SavingsKpiSummary.Realized` changes type; `Summarize` takes a second input sequence; `Realized` stops going through `Bucket`; sweep the stale comment at `:42-55` |
| `backend/src/Raffa.Savings/Application/SavingsKpiQueryService.cs` | modify — project `RealizedSavingsRecords` and pass it as the second sequence |
| `backend/src/Raffa.Savings/Domain/SavingsOpportunityStatus.cs` | modify — sweep the stale comment at `:36-45` only |
| `backend/src/Raffa.Api/SavingsKpiEndpointExtensions.cs` | modify — `savingsRealized` emits the new shape; key unchanged |
| `web/openapi/raffa-api.v1.json` | modify — `savingsRealized` item becomes `currency`/`amount`/`count` |
| `web/src/api/generated/schema.ts` | modify — regenerated by `npm run generate:api` |
| `web/src/routes/savings/savingsViewModel.ts` | modify — fourth cell in **both** branches, per-currency money lines, provenance `meta`, corrected `lines` docstring, `· N realized` removed from the identified cell |
| `web/src/routes/savings/KpiRow.tsx` | modify — skeleton arity 3 → 4 |
| `backend/tests/Raffa.Savings.Tests/SavingsKpiCalculatorTests.cs` | modify — realized is summed money per currency, not an estimate band; no cross-currency total |
| `backend/tests/Raffa.Savings.Tests/SavingsKpiQueryServiceTests.cs` | modify — the realized rows are projected and reach the summary |
| `backend/tests/Raffa.Api.Tests/SavingsKpiEndpointTests.cs` | modify — `savingsRealized` on the wire carries `amount` and `count` |
| `web/tests/routes/savings/savingsViewModel.test.ts` | modify — four cells in both branches; per-currency lines; the `—` + meta zero state |
| `web/tests/routes/savings/SavingsRoute.test.tsx` | modify — the band renders four cells and the "Savings verified" label |
| `web/tests/api/client.test.ts` | modify — the KPI body fixture at `:1716-1720` carries the new realized shape |
| `web/tests/App.test.tsx` | modify — KPI body fixture at `:124-128` |
| `web/tests/components/shell/WorkspaceShellApp.test.tsx` | modify — KPI body fixture at `:134-138` |
| `web/e2e/w17-savings.spec.ts` | **new** — A17-S1: realized money grouped by currency under "Savings verified", agreeing after a reload and in a second browser context; a `savingsPropagated: null` outcome moves no figure (ADR-012 w17 clauses 39, 41) |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-001 w17 clause 1** — realized money renders only from `RealizedSavings`, per currency, never summed; a `savingsPropagated: null` outcome enters no total.
  - **ADR-001 w17 clause 10** — the label is **"Savings verified"**; the screen word and the domain word may differ and nothing is renamed to align them.
  - **ADR-028 w17 clause 6** — the break is a **type** change, not a rename.
  - **ADR-028 w17 clause 7** — the wire key is **`savingsRealized`**. An implementer who renames it to `realized` "to comply" ships a second, undeclared break and breaks `client.ts:985-987`.
  - **ADR-012 w17 clause 39** — **no alias in `client.ts`.** Index the generated body inline.
  - **ADR-012 w17 §43** — `countOf` (`savingsViewModel.ts:72`) is typed `ReadonlyArray<{ count: number }>`, **structurally not nominally**, so a shape that keeps `Count` compiles unchanged at `:110` while the population behind the number changes. Regeneration plus `tsc --noEmit` will **not** catch it. That call site is named here for exactly that reason.
  - **ADR-020 w17 §20** — the band's **arity is a design property** (`inputs/design/prototypes/design-system.md:45`, "a row of equal cells") and **three** literals fix it: `KpiRow.tsx:20`, `savingsViewModel.ts:83-88` and `:93-112`. `buildKpiCells` returns `readonly KpiCellView[]`, so widening the `key` union binds **no** branch to carry the new literal — the divergence is **invisible to `tsc --noEmit`**.
  - **ADR-020 w17 §21** — `kpis === null` is **not** loading, it is a **failed fetch** (`KpiRow.tsx:16-25` returns the skeleton first; only `reduceKpiFetch` at `savingsViewModel.ts:56-57` reaches it), and that state already paints `.error-state` (`KpiRow.tsx:32-41`) and a per-cell **"Stale"** tag (`:55`). A missing fourth cell there loses the **label**, where no dash can rescue it.
- **Design oracle and the ratified divergence**: `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137` — the KPI triple is *Contracts analyzed · Upcoming renewals · Savings identified* and the opportunities column is **Estimate**. **The oracle carries no realized-money KPI at all.** The fourth cell is a **deliberate divergence ratified by ADR-020 w17 §15/§17**; do **not** "restore" the export to three cells, and do not fold verified money into "Savings identified" — `inputs/design/prototypes/design-system.md:48`'s facts-vs-AI rule refuses it, because a verified amount and a pre-negotiation estimate are different kinds of money.
- **Do not touch**: `web/src/api/client.ts` (NW-63 is its only writer this wave — ADR-012 w17 clause 39); `SavingsEndpointExtensions.cs`'s `realizedAmount` PATCH path (`:126`, `:170-171`); `SavingsProvenanceClassifier.cs`'s `0.7`/`0.4` bands (`:37`, `:46` — provenance banding, not a review decision, out of scope per OQ-w17-003); the `Identified` and `InProgress` members and their wire keys; any cross-currency conversion.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Savings.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `cd web && npm run generate:api` exits 0 and leaves `web/src/api/generated/schema.ts` with `savingsRealized` items carrying `amount`
- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `cd web && npm run build` exits 0 (it **is** the type-check: `generate:api && tsc --noEmit && vite build`). ⚠ `web/package.json:10-18` has **no** `lint` and **no** `typecheck` script — do not invent one
- [ ] `cd web && npm test` exits 0
- [ ] `cd web && npx playwright test e2e/w17-savings.spec.ts` exits 0
- [ ] `git diff --stat origin/main -- web/e2e/v2.spec.ts` is **empty** for this task's commits (ADR-012 w17 clause 39 — that file is NW-73's sweep alone)
- [ ] `grep -n "savingsRealized" web/openapi/raffa-api.v1.json web/src/routes/savings/savingsViewModel.ts` shows the key unchanged and no occurrence of a bare `"realized"` key
- [ ] `grep -rn "SavingsKpiSummaryBody" web/src/api/client.ts` shows **no** new alias line added beside `:985-987`
- [ ] `grep -n "length: 3" web/src/routes/savings/KpiRow.tsx` returns nothing
- [ ] **the stale-comment sweep is verifiable, not asserted**: `grep -n "E04/F02/US02/T02" backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs backend/src/Raffa.Savings/Domain/SavingsOpportunityStatus.cs` returns **nothing** (it returns **2 hits today** — `SavingsKpiCalculator.cs:52` and `SavingsOpportunityStatus.cs:39` — each introducing prose that calls the `RealizedSavings` record a *future* deliverable. The record exists and this task makes it read; both comments become false on merge). ⚠ Do **not** widen this grep past these two files: the same id legitimately appears in six other `Raffa.Savings` files that this task does not touch
- [ ] a reviewer reading `savingsViewModel.ts` finds `buildKpiCells(null)` and `buildKpiCells(summary)` returning the **same four keys in the same order**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | two realized rows in one currency sum to one money figure with `Count = 2`; two currencies produce two entries and **no** combined total; an opportunity's estimate range never reaches the realized member | `backend/tests/Raffa.Savings.Tests/SavingsKpiCalculatorTests.cs` |
| unit | the query service projects `RealizedSavingsRecords` and the summary carries them; an opportunity with no realized row contributes nothing | `backend/tests/Raffa.Savings.Tests/SavingsKpiQueryServiceTests.cs` |
| integration | `GET` the savings KPI endpoint → `savingsRealized[0]` carries `currency`, `amount`, `count`; `savingsIdentified` still carries `low`/`high`; a second tenant sees none of it | `backend/tests/Raffa.Api.Tests/SavingsKpiEndpointTests.cs` |
| unit | `buildKpiCells(null)` and `buildKpiCells(summary)` both return **four** cells with identical keys; the verified cell renders one line per currency; `lines: []` yields `—` and a non-null `meta` | `web/tests/routes/savings/savingsViewModel.test.ts` |
| component | the band renders four cells and the label "Savings verified"; the failed-fetch state still shows four cells, the `.error-state` block and the "Stale" tag | `web/tests/routes/savings/SavingsRoute.test.tsx` |
| e2e | **A17-S1** — realized money grouped by currency under "Savings verified", agreeing after a reload **and in a second browser context**; a `savingsPropagated: null` outcome moves no figure; the estimate never appears in that cell. Runbook evidence, **not** a CI gate — no workflow runs Playwright | `web/e2e/w17-savings.spec.ts` (**created by this task**) |

## Open questions blocking this task

- **none blocking.** OQ-w17-006 is ruled (the `RealizedSavings` rows grouped by
  currency), OQ-w17-ux-04 is ruled ("Savings verified") and OQ-w17-cl-01 is
  ruled (drop the alias; `client.ts` is not this task's file).

## Wave-spec entry

```yaml
- id: E20/F01/US01/T01
  prompt: reports/workitems/epic-20-w16-residuals/feature-01-savings-verified-kpi/us-01-savings-verified-kpi/tasks/task-01-savings-verified-kpi.md
  produces: [savings-verified-kpi]
  depends_on: [api-contract-a]
  effort: L
  layer: backend
  status: live
```
