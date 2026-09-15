---
id: epic-19
type: epic
wave: w16
status: active
extends: [epic-03, epic-04, epic-05, epic-07, epic-08, epic-16]
---

# epic-19-server-side-state — Nothing the product knows lives only in a browser tab

## Business capability

Three facts a procurement user creates today — the action they took on a
renewal, the outcome they negotiated on a quote, and the negotiation steps they
ticked off on Contract 360 — survive only in the tab that produced them. Reload,
switch device, hand the contract to a colleague, and the work is gone. This epic
makes each of those facts a tenant-scoped row in Postgres under RLS, exposed by
an HTTP read-back, and retires the three `sessionStorage` stores that stood in
for the missing GETs. It also makes a recorded negotiation outcome actually move
the savings opportunity it belongs to, and deletes a dead in-process queue that
still boots a second hosted service inside every deployed Worker.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w16-todo.md` §2 | NW-11, NW-12, NW-13, NW-21 |
| `reports/context/waves/w16-requirements.md` §2 | NW-11, NW-12, NW-13, NW-21, W16-01 |
| `inputs/product-spec.md` §7 (`:214`) | trackable SavingsOpportunity with status, owner and realized outcome |
| `inputs/product-spec.md` §12.2 (`:562`), `:925` | negotiation outcome capture, `POST /api/negotiations/outcomes` |
| `inputs/design/prototypes/raffa-v2/screens-v2.md:106-108` | the four named negotiation steps |
| `inputs/design/prototypes/raffa-v2/screens-v2.md:123-130` + `app.jsx:109,166` | `racts` is one shared fact across Renewals, Contract 360 and Savings |
| ADR-028 | the new ADR this epic implements (§D1–§D6 + the w16 round-2 footer) |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | renewal-action-read-back (NW-11) | w16 |
| feature-02 | quote-and-outcome-read-back (NW-12) | w16 |
| feature-03 | negotiation-step-ticks (NW-13) | w16 |
| feature-04 | outcome-links-savings (NW-21) | w16 |
| feature-05 | worker-r0-queue-deleted (W16-01) | w16 |
| feature-06 | contract-and-client-publication (NW-11 + NW-12 + NW-13) | w16 |
| feature-07 | session-stores-retired (NW-11 + NW-13) | w16 |
| feature-08 | w16-integration | w16 |

## Success looks like

A user posts a renewal action, records a negotiation outcome and ticks two of
the four negotiation steps on `dev`. A second browser, signed in as the same
user, sees all three. `grep -r "sessionStorage" web/src/routes` returns no store
that is the system of record for user data. A negotiation outcome tied to a
savings opportunity moves that opportunity to `Realized`, and an outcome that
resolves to no unambiguous opportunity is recorded, visibly unlinked, and moves
nothing. `Raffa.Worker` boots exactly one hosted service for document
extraction.

## Architecture decisions in force

- **ADR-028** — server-side state (new at the w16 table): §D1 the renewal-action
  read-back and the `savedAction` field name; §D2 the quote resource and its
  embedded outcomes; §D3 `contract_negotiation_step`, keyed by step **name**;
  §D4 Undo is two idempotent writes, ticks `PUT` first; §D5 the link is named or
  deterministically resolved, **never guessed**; §D6 what this wave does not
  deliver (the realized *amount* in the KPI — head of W17).
- **ADR-012** w16 clauses 21–31 — the three stores retire, each **with** its
  read-back and never before it; `web/src/api/client.ts` is a one-writer-per-phase
  file; the reverting tick; the pseudo-opportunity row is retired.
- **ADR-026** w16 clause 5 — the five contract paths this epic publishes.
- **ADR-003** w16 clause 1 + **ADR-021** w16 — the one new table and the one
  regenerated script; no `backend.yml` array moves.
- **ADR-009** w16 clause 2 — `ENABLE` + `FORCE` + a `tenant_isolation` policy
  with both `USING` and `WITH CHECK`, in the table's own migration.
- **ADR-002** w16 clauses 1–3, 5 — `Raffa.Documents.Contracts` owns the ticks;
  `Raffa.Api` composes the savings link; `Raffa.Suppliers.Products` gains one
  read-only lookup; the Worker drops the R0 trio.
- **ADR-001** w16 clauses 0, 2, 3, 4, 7 — zero new capability; NW-12 is the
  read-back and nothing more; the four canonical named steps, per contract;
  A16-8 closes on the **status** move and no w16 task may render
  `SavingsKpiSummary.Realized` as a money amount.
- **ADR-027** w16 clause 2 — the R0 queue deletion; `InMemoryExtractionQueue`
  stays.
- **ADR-018** `:112-119` — the IA-level empty / error / loading contract every
  retiring surface inherits.

## Out of scope

- A cross-quote negotiation **history**, the levers view, or an Ask-citable
  outcome list — **NW-57, W18** (`screens-v2.md:139-145`).
- A tenant-wide `GET /api/negotiations/outcomes` feed — it has no caller until
  NW-57 and publishing it now pre-empts that item (ADR-028 §D2).
- The **realized amount** in the Savings KPI. `SavingsKpiCalculator.cs:103-104`
  keeps summing `EstimatedSavingsLow/High`; the fix is an unmet AC of the still
  `active` story `E04/F03/US01` and is the **head of W17** (OQ-w16-po-01).
- A `DELETE` route for the renewal action — absence of a row **is** the status
  `NotStarted` (ADR-028 §D1, client C8).
- Any tick **history** per renewal cycle — ticks are per contract (ADR-001 w16
  clause 3).
- Showing tracked renewal actions on Savings at all: the pseudo-opportunity row
  is retired here, and whether Savings should carry a *designed* section for
  them is product-owner's, seating ux-ui-designer in **W17** (OQ-w16-ca-01).
- Anything under `infra/` — the wave's cloud delta is **zero** (ADR-016 w16
  clause 27).
