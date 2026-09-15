---
id: us-01
type: user-story
parent: feature-07
wave: w16
status: active
---

# us-01-actions-and-ticks-on-screen — One shared fact on Renewals, Contract 360 and Savings

## Story

As a **Procurement member**, I want the renewal action I took and the negotiation
steps I ticked to be read from the server on every surface that shows them, so
that **a reload, a second device or a colleague sees the same state instead of an
empty tracker**.

## Acceptance criteria

- [ ] AC-1 Post an action on Renewals → reload **and** open a second browser →
  the same action appears on **Renewals, Contract 360 and Savings**.
- [ ] AC-2 Tick 2 of the 4 negotiation steps on contract X → reload **and** second
  browser → the same 2 are ticked; contract Y is unaffected.
- [ ] AC-3 When a tick `PUT` fails, the optimistic tick **reverts to the last
  server-known set** and the screen shows ADR-018 `:112-119`'s error state,
  naming the surface in plain language — never a raw path.
- [ ] AC-4 "Undo" issues **two idempotent writes, ticks `PUT` first then the
  action `POST` of `NotStarted`**, and every surface renders the surviving
  `NotStarted` row as "no action taken".
- [ ] AC-5 A step key the server returns that the client does not know is
  **ignored**, never rendered as a fifth tick; a key the server omits is
  unticked.
- [ ] AC-6 `renewalActionStore.ts` and `negotiationStepsStore.ts` no longer
  exist; nothing under `web/src` writes `raffa.renewals.actions` or
  `raffa.contract360.steps.*`.
- [ ] AC-7 The Savings opportunities table shows **only real savings
  opportunities** — no estimate-less pseudo-row for a tracked renewal action.
- [ ] AC-8 No surface renders `SavingsKpiSummary.Realized` as a money amount;
  the realized KPI stays a **count**.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-012 w16 clauses 21, 22, 26, 30; ADR-028 §D4;
  ADR-018 `:112-119`
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E19/F06/US01/T01` | the `savedAction` type, the two step wrappers and the regenerated `schema.ts`; **a store is deleted with its read-back, never before it** |

## Architecture decisions in force

- **ADR-012** w16 clause 22 — **NW-11 adds no `ApiClient` method**; Contract 360
  already calls `getRenewals` (`contract360/index.tsx:115`, same `Promise.all` as
  `getRenewalPriority` at `:116`, row derived at `:227`). The four type-only
  importers take the generated row type re-exported from `client.ts`.
- **ADR-012** w16 clause 26 — keys never indices; the reverting tick; §D4's
  ordering ratified from the client side; recovery is a **re-read, never a
  compensating write**.
- **ADR-012** w16 clause 30 — `buildTrackedOpportunityRow` is **retired**.
- **ADR-012** w16 clause 29 — the money fence is already satisfied by
  `savingsViewModel.ts:110` (`countOf(kpis.savingsRealized)`); **the fence's
  whole cost is that no task may change it**.
- **ADR-012** §12 — the Playwright case is acceptance-runbook evidence, **not a
  CI gate**, and must not be presented as one.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | actions-and-ticks-on-screen | L | phase-4 |

## Council decisions carried into this story

> **ADR-012 w16 clause 22**: "`handleUndo` posts `NotStarted` **and** calls
> `forgetRenewalAction`; the server row is an upsert on
> `(tenant_id, contract_id)` and **survives** the undo … After the read-back the
> row returns as `NotStarted` and every surface must render it as 'no action
> taken', **or Undo will look broken**."

> **ADR-012 w16 clause 26**: "A tick the server rejected must never survive on
> screen — that is w15 clause 4's fabricated-fact class, not a cosmetic concern."

> **ADR-012 w16 clause 30**: read back from the server the tracked set becomes
> "**every renewal action in the tenant, permanently**, filling the table with
> rows that are not opportunities and carry no estimate. **A naive store→GET swap
> ships that flood.**"

> **ADR-028 §D4**: "ticks `PUT` first, then the action `POST` … an action still
> in progress with no ticks is an honest state a user can reach, while
> `NotStarted` with four ticks is a contradiction no flow produces. **The order
> is load-bearing, not arbitrary.**"

## Open questions

- **OQ-w16-ca-01** — ruled here in its client half (clause 30). Its **product**
  half — whether Savings should show tracked renewal actions at all, as a
  *designed* section — is deferred to **W17** and seats ux-ui-designer. No task.
