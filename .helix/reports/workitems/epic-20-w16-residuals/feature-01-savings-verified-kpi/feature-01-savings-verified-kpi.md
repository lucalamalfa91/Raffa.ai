---
id: feature-01
type: feature
parent: epic-20
wave: w17
status: active
extends: epic-04 F02/F03
---

# feature-01-savings-verified-kpi — the Savings band shows verified money, not a count

## Slice

The Savings KPI band gains a **fourth equal cell**, "Savings verified", fed by
the `RealizedSavings` rows the product already writes at
`backend/src/Raffa.Savings/Application/SavingsOpportunityService.cs:325` and has
never once read. `SavingsKpiCalculator.Bucket(materialized,
SavingsOpportunityStatus.Realized)` (`:86`) stops summing
`EstimatedSavingsLow`/`EstimatedSavingsHigh` (`:103-104`) — the opportunity's own
**pre-negotiation estimate** — and the realized member changes type from
`SavingsRangeByCurrency` (a band) to `RealizedSavingsByCurrency(Currency,
Amount, Count)` (one number plus the count of outcomes behind it). Grouped by
currency, **never summed across currencies**: no conversion service exists
(`SavingsKpiCalculator.cs:40-44`).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | savings-verified-kpi | w17 |

## Extends

**epic-04 F03 `us-01-savings-kpis`** — its AC-1 asked for realized savings in the
KPI band and only the **count** shipped
(`reports/workitems/epic-04-savings-intelligence/feature-03-savings-dashboard/us-01-savings-kpis/us-01-savings-kpis.md:19`).
That story stays **`active`**, receives **no status banner** and no
`superseded:` line: this feature *completes* an AC, it does not cancel one
(`w17-requirements.md` §6). Also extends **epic-04 F02**
(`feature-02-savings-engine`) — the engine that writes the `RealizedSavings`
row.

## Architecture decisions in force

- **ADR-001 w17 clause 1** — realized money renders **only** from
  `RealizedSavings` rows, grouped by currency, never summed across them. The
  estimate-summed `Realized` range is a defect to fix, never to render. An
  outcome with `savingsPropagated: null` enters **no** total. ADR-001's w16
  clause 4 money fence is **lifted and replaced**, not deleted.
- **ADR-001 w17 clause 10** — the cell is labelled **"Savings verified"**.
  `inputs/product-spec.md:447` is a two-column row whose *label* column reads
  "Savings Realized" and whose *meaning* column reads "**Verified**
  negotiated/implemented savings": what the spec fixes is the **evidence
  standard**, not the string. "Realized" is the word attached to the defect.
- **ADR-028 w17 footer + clause 6** — `RealizedSavingsByCurrency(Currency,
  Amount, Count)` replaces `SavingsRangeByCurrency` on the realized member: a
  **declared type change on one member, not a rename**. The member keeps the
  identifier `Realized`. `RealizedAmount` stays PATCH-only. The calculator
  stays **pure** — a second input sequence, never an injected service
  (Appendix C rule 6).
- **ADR-028 w17 clause 7** — ⚠ **the wire key is `savingsRealized`, not
  `realized`** (`web/openapi/raffa-api.v1.json:5051`,
  `web/src/api/client.ts:987`). Clause 6's own citation was wrong. **Nothing is
  renamed this wave**; "Savings verified" stops at the view model and never
  becomes a DTO member, a wire property, a column or a domain type.
- **ADR-012 w17 clause 37 + clause 39 / OQ-w17-cl-01** — `web/src/api/client.ts`
  is one-writer-per-phase and this feature takes the **free escape**: it adds
  **no alias**, indexing the generated body inline the way `client.ts:986-987`
  already do. `client.ts` therefore has exactly one writer this wave (NW-63).
- **ADR-012 w17 §42–§44** — the alias-free idiom is
  `SavingsKpiSummaryBody["savingsRealized"][number]`; a declared break **names
  its call sites** because `countOf` (`savingsViewModel.ts:72`) is typed
  structurally (`ReadonlyArray<{ count: number }>`) and so compiles unchanged
  while the population behind the number changes; `lines: []` means "no figure"
  and the reason rides **`meta`**, never the dash.
- **ADR-020 w17 §15, §17, §20–§21** — a **fourth equal cell**, never a second
  line in the third (`inputs/design/prototypes/design-system.md:45` specifies a row of **equal** cells,
  and `inputs/design/prototypes/design-system.md:48`'s facts-vs-AI rule refuses letting a verified amount
  share a cell with a pre-negotiation estimate). **Four cells in every state** —
  three literals fix the arity. Zero state renders `—` with a meta sentence,
  **never a fabricated `0`**. The divergence from `screens-v2.md:134-135`'s KPI
  triple is **ratified**; a task must not "restore" the export.

## Target repo

mixed — `raffa-backend` (`backend/src/Raffa.Savings`, `backend/src/Raffa.Api`)
and `raffa-web` (`web/openapi`, `web/src/api/generated`, `web/src/routes/savings`).
One task owns both halves plus the contract, so the declared wire break and its
consumer land together.
