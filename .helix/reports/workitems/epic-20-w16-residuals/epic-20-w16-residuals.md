---
id: epic-20
type: epic
wave: w17
status: active
extends: [epic-04, epic-13, epic-18]
---

# epic-20-w16-residuals — the two residuals the w16 table ruled into W17

## Business capability

Savings stops reporting a **pre-negotiation estimate** as if it were money the
customer kept: the KPI band gains a fourth cell showing the **verified** amount,
grouped by currency, read from the `RealizedSavings` rows the product already
writes and has never read. And an operator can re-run extraction across a whole
tenant **through the product's own code path** — the `extraction_job` row, the
`document.reprocessed` audit row and ADR-027's replace step all execute, because
the console calls `DocumentReprocessService.ReprocessAsync` per document instead
of being re-implemented in bash.

Both are residuals the **w16 council ruled into W17** rather than defects this
wave discovered: `BACKLOG.md:350-356`, `reports/audit/w16-hitl.md:156-159`,
`../docs/waves/w16-acceptance.md` known-gap #1.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w17-todo.md` §1 | NW-72 — Savings KPI realized amount |
| `inputs/next/w17-todo.md` §1 | NW-73 — bulk whole-tenant reprocess console |
| `inputs/product-spec.md` `:447` | "Savings Realized" — **Verified** negotiated/implemented savings |
| `inputs/requirements.md` R-DOC-07 | bulk reprocess capability |
| OQ-w16-po-01 | the unmet realized half of `E04/F03/US01` AC-1 |
| ADR-016 w16 clause 31 | the bulk console shape, designed at the w16 table and scheduled here |
| ADR-022 §4 `:250-254` | "owed to W17" — may a CI principal hold a Send right |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | savings-verified-kpi | w17 |
| feature-02 | bulk-reprocess-console | w17 |

## Extends

- **epic-04 F02/F03** (`feature-02-savings-engine`, `feature-03-savings-dashboard`) —
  NW-72 discharges the realized half of `E04/F03/US01` AC-1. That story stays
  `active`, gets **no status banner** and no `superseded:` line
  (`w17-requirements.md` §6).
- **epic-13 F03** (`feature-03-supplier-identity`) — the supplier/market context
  the savings provenance rides on.
- **epic-18 F03** (`feature-03-interim-posture-retirement`) — the owner of the
  CI-YAML set in w16; NW-73 is the next and **only** task this wave allowed to
  open `.github/workflows/**`.

## Success looks like

- Recording an outcome that realizes an opportunity makes the Savings band show
  a **money figure** labelled "Savings verified", per currency, never summed
  across currencies, and a second browser agrees after reload.
- An operator dispatches one `workflow_dispatch` workflow against a `dev`
  tenant; every document in `verify-tenant-corpus.yml`'s own worklist is
  re-enqueued **through `ReprocessAsync`**, reaches a terminal state, and the
  verify workflow then reports the worklist empty and `%PDF` gone.
- `raffa-sp-<env>` holds exactly one new Azure right: `Azure Service Bus Data
  Sender`, topic-scoped on `extraction-events`. No Receiver, no `Manage`, no
  namespace scope, no SAS key.

## Architecture decisions in force

- **ADR-001** — w17 clauses 1, 10: realized money renders only from
  `RealizedSavings`, grouped by currency; the cell is labelled **"Savings
  verified"**; an outcome with `savingsPropagated: null` enters no total.
- **ADR-028** — w17 footer + clauses 6, 7: `RealizedSavingsByCurrency` replaces
  `SavingsRangeByCurrency` on the realized member — a **declared type change,
  not a rename**; the wire key stays `savingsRealized`.
- **ADR-012** — w17 clauses 37, 39, §42–§44: `client.ts` is one-writer-per-phase
  and NW-72 stays out of it; the KPI band's four cells; `lines: []` means "no
  figure" and the reason rides `meta`.
- **ADR-020** — w17 §15, §17, §20–§21: a **fourth equal cell**, not a second
  line in the third; four cells in **every** state; the divergence from
  `screens-v2.md:134-135`'s KPI triple is ratified.
- **ADR-002** — w17 clause 1: `Raffa.Tools` is a **third composition root** —
  no table, no endpoint, no business rule, referenced by nothing; it joins
  `AllRaffaProjects` and **not** the domain-module array or the allow-list.
- **ADR-027** — w17 clause 3, and ADR-016 w17 clauses 36–45: the console calls
  the product path and never re-implements `RequeueClassificationJobAsync`.
- **ADR-022** — w17 clauses 1–3, 6: the Admin gate is **relocated to the CI
  plane**, not bypassed; `target_environment` ships **`dev` only`**.
- **ADR-009** — w17 clauses 1, 4: three-argument `Configure` inside
  `BeginScope`; zero rows under a valid tenant exits **non-zero**.
- **ADR-011** — w17 clauses 20, 23, 26: actor is the fixed literal
  `system:bulk-reprocess`; no CI-controlled string ever reaches
  `AuditEvent.Actor`; the console **stops at the first publish failure**.
- **ADR-005** — w17 §15–§18, §22, §27 and **ADR-007** w17 §5–§9: one
  topic-scoped `azurerm_role_assignment` carrying the same
  `lifecycle { ignore_changes = … }` both existing grants carry, wired in
  **both** env roots in the same PR. **$0.00/month.**
- **ADR-014** — w17 clauses 1–9: the Terraform task is **PR 1, alone**; the wave
  is **two PRs**; the phase graph is unchanged by round 3.

## Out of scope

- **NW-74** (hide admin-gated Ask chips from a non-Admin) — `should`, **queued
  to the head of W18**. Not decided at the w17 table, therefore not decomposed
  here. ADR-022 S16-11 stands: presentation, never a control, and
  `GET /api/capabilities` stays un-gated.
- **NW-75** (a designed Savings section for tracked renewal actions) — **ruled
  OUT for V1** by product-owner at the table (ADR-001 w17 clause 7). No task,
  no story, no ADR-020 change, and **no work item is cancelled**. It returns as
  a *new* item once a tracked action can carry a system-held estimate, i.e.
  after NW-62's benchmark join.
- **A cross-currency total.** No conversion service exists
  (`SavingsKpiCalculator.cs:40-44`), so realized money is grouped by currency
  and never summed across them.
- **Renaming anything.** `RealizedSavings`, `RealizedAmount`, `realizedAmount`,
  `Realized` and `savingsRealized` all keep their names; "Savings verified"
  stops at the view model (ADR-028 w17 clause 6).
- **Containerising the console.** Not a Container Apps Job, not an ACR image,
  not a deploy-path change (ADR-016 w17 clause 36). The refusal is recorded so
  W18 does not re-open it as "the cheaper option".
- **Widening the console beyond `dev`.** The `options:` list stays `[dev]`;
  widening is gated on the `raffa-demo` apply, OQ-w17-ux-05's corpus-health
  signal and OQ-w17-dm-04's approval test being made runnable.
- **ACR pruning** (OQ-w17-ca-04) — pre-existing, recorded, **no task minted**.
