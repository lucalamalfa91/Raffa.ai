---
id: us-01
type: user-story
parent: feature-01
wave: w17
status: active
---

# us-01-savings-verified-kpi — Savings shows the money that was actually kept

## Story

As a **procurement lead**, I want the Savings band to show the **verified**
amount we actually realized, grouped by currency and with its provenance, so
that I stop reading a pre-negotiation estimate as if it were money in the bank.

## Acceptance criteria

- [ ] AC-1 Recording an outcome that realizes an opportunity makes the Savings
  KPI band show a **money figure** for verified savings — one formatted line per
  currency — not only a count. (A17-S1)
- [ ] AC-2 The cell is labelled **"Savings verified"**. The word "Realized" —
  the estimate bucket's own word — never labels it. (A17-S1, ADR-001 w17
  clause 10)
- [ ] AC-3 Reloading the page shows the same figure, and a **second browser**
  signed in to the same workspace agrees. Nothing about this number lives in a
  browser tab.
- [ ] AC-4 Currencies are **never summed**: two outcomes in EUR and USD render
  two lines, and no combined total appears anywhere.
- [ ] AC-5 An outcome whose `savingsPropagated` is `null` enters **no** total —
  the figure does not move.
- [ ] AC-6 With no verified savings recorded, the cell renders **`—`** with the
  meta line "no verified savings recorded yet" — never `0` and never an absent
  cell. The band shows **four** cells in the loading state, the loaded state and
  the failed-fetch state.
- [ ] AC-7 The verified figure comes from the `RealizedSavings` rows. The
  opportunity's `EstimatedSavingsLow`/`EstimatedSavingsHigh` never reach this
  cell.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E22/F01/US01` (phase 2) | it is the wave's phase-2 owner of `web/openapi/raffa-api.v1.json` and of the regenerated `web/src/api/generated/schema.ts`; this story regenerates on top of that merged contract in phase 3, so the realized break does not drop the phase-2 fields |

## Architecture decisions in force

- **ADR-001** w17 clauses 1, 10 — realized money only from `RealizedSavings`,
  per currency, never summed; the label is "Savings verified"; a
  `savingsPropagated: null` outcome enters no total.
- **ADR-028** w17 footer + clauses 6, 7 — `RealizedSavingsByCurrency` replaces
  `SavingsRangeByCurrency` on the realized member: a **declared type change, not
  a rename**; the wire key stays **`savingsRealized`**; the calculator stays
  pure.
- **ADR-012** w17 clauses 37, 39 and §42–§44 — no `client.ts` alias (the free
  escape of OQ-w17-cl-01); the declared break names its call site because
  `countOf` is structurally typed; `lines: []` means "no figure" and the reason
  rides `meta`.
- **ADR-020** w17 §15, §17, §20–§21 — a fourth **equal** cell; four cells in
  every state; `—` never `0`; the divergence from the export is ratified.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | savings-verified-kpi | L | phase-3 |

## Council decisions carried into this story

- **Which amount is "verified"** (OQ-w17-006): the **`RealizedSavings` rows**
  (the audit-tracked record, written at `SavingsOpportunityService.cs:325`).
  `SavingsOpportunityResult.RealizedAmount` (`:34`) stays **PATCH-only** and is
  not the source of this cell.
- **The shape**: `RealizedSavingsByCurrency(Currency, Amount, Count)`. `Count`
  rides along because "€X verified across N outcomes" is the honest label.
  An estimate is a band; a realized figure is **one number**.
- **The reader**: `SavingsKpiQueryService` projects `RealizedSavings` alongside
  `SavingsOpportunities` and hands the calculator a **second input sequence**.
  The calculator never gains a service dependency.
- **The label**: **"Savings verified"** — the band's other three labels are noun
  phrases ("Contracts analyzed", "Upcoming renewals", "Savings identified",
  `savingsViewModel.ts:85-87`), and in a row of equal cells a bare "Verified"
  would name a **status** rather than a thing counted.
- **The zero state**: `lines: []` → `—` (`KpiRow.tsx:49`) with meta "no verified
  savings recorded yet", reusing `:98`'s own idiom for absent annual spend.
  **Never a fabricated `0`**, which asserts that outcomes were recorded and came
  to nothing.
- **The stale comments are part of the work**: `SavingsKpiCalculator.cs:42-55`
  and `Domain/SavingsOpportunityStatus.cs:36-45` both still assert that no
  audit-tracked realized-value record exists. It exists and is written; after
  this wave those comments are false statements in the tree.
- **No cross-currency total** and **nothing is renamed**.

## Open questions

- **OQ-w17-006** (NW-72: which amount is "verified") — **ruled**: the
  `RealizedSavings` rows grouped by currency; `RealizedAmount` stays PATCH-only;
  the stale comments are swept by this task. ADR-001 w17 clause 1, ADR-028 w17
  footer.
- **OQ-w17-ux-04** (the cell's label) — **ruled** by product-owner: "Savings
  verified", adopted whole. ADR-001 w17 clause 10.
- **OQ-w17-cl-01** (`client.ts` has two contending items) — **ruled**: this
  story takes the free escape and **drops the alias**, leaving `client.ts` with
  one writer. ADR-012 w17 clause 39.
