---
id: E19/F07/US01/T01
type: task
story: us-01-actions-and-ticks-on-screen
wave: w16
status: live
target_repo: raffa-web
---

# task-01-actions-and-ticks-on-screen — Retire two session stores; read the shared fact from the server

## Coding objective

Delete `web/src/routes/renewals/renewalActionStore.ts` (key
`raffa.renewals.actions`) and
`web/src/routes/contracts/contract360/negotiationStepsStore.ts` (key prefix
`raffa.contract360.steps.`) and point every surface that read them at the server.

**Renewals, Contract 360 and Savings all reach the action through the existing
`getRenewals` wrapper** — Contract 360 already calls
`apiClient.getRenewals(workspace.id)` at
`web/src/routes/contracts/contract360/index.tsx:115`, in the same `Promise.all`
as `getRenewalPriority` at `:116`, deriving its row at `:227`. So **no new
`ApiClient` method is needed for the action**; read `savedAction` off the row.
Four of the seven importers use only the *type* `TrackedRenewalAction`: replace
it with the generated row type re-exported from `web/src/api/client.ts` — **no
hand-written DTO**.

For the ticks, `handleToggleStep`
(`web/src/routes/contracts/contract360/index.tsx:271-276`) becomes **write +
read-back** through `putNegotiationSteps` / `getNegotiationSteps`. The wire
carries the four **named** keys; the rendered labels stay where they are
(`contract360ViewModel.ts:211-218`, two of them parameterized).
`NEGOTIATION_STEP_COUNT = 4` stops being a client constant that shapes the wire.

`handleUndo` (`:261-269`) becomes **two idempotent writes, ticks `PUT` first then
the action `POST` of `NotStarted`**. `forgetRenewalAction` and
`clearNegotiationSteps` die with their stores.

Finally retire `buildTrackedOpportunityRow`
(`web/src/routes/savings/savingsViewModel.ts:216-228`, `estimate:
NOT_YET_AVAILABLE` at `:223`) and stop `buildOpportunityRows` (`:236-245`) from
prepending tracked actions: Savings renders only real `SavingsOpportunity` rows.

## Parent story AC covered

- AC-1 Post an action → reload and second browser → the same action on Renewals, Contract 360 and Savings.
- AC-2 Tick 2 of 4 on contract X → reload and second browser → the same 2 ticked; contract Y unaffected.
- AC-3 A failed tick `PUT` reverts the optimistic tick and shows the ADR-018 error state.
- AC-4 Undo is two idempotent writes, ticks first; `NotStarted` renders as "no action taken" everywhere.
- AC-5 Unknown server step key is ignored; a missing key is unticked.
- AC-6 Both stores are gone; nothing under `web/src` writes their keys.
- AC-7 Savings shows only real savings opportunities.
- AC-8 The realized KPI stays a count, never a money amount.

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/renewals/renewalActionStore.ts` | deleted |
| `web/src/routes/contracts/contract360/negotiationStepsStore.ts` | deleted |
| `web/src/routes/renewals/index.tsx` | `:50` stops seeding state from the store; `:97-101`/`:103-106` build from the list's `savedAction`; `:144-145` re-reads after the post |
| `web/src/routes/renewals/renewalPipelineViewModel.ts` | takes the generated row type instead of `TrackedRenewalAction` |
| `web/src/routes/renewals/InsightCard.tsx` | same type swap |
| `web/src/routes/contracts/contract360/index.tsx` | store imports at `:6` and `:20` removed; the row at `:227` supplies `savedAction`; `handleUndo` `:261-269` becomes ticks-`PUT`-then-action-`POST`; `handleToggleStep` `:271-276` becomes write + read-back with a reverting tick and the ADR-018 error state; ticks are read on mount alongside `:93`/`:115` |
| `web/src/routes/contracts/contract360/AnswersBand.tsx` | `:10-11` takes the ticked **keys**, not a positional `boolean[]`; `:104-112` renders from the key set |
| `web/src/routes/contracts/contract360/contract360ViewModel.ts` | `:211-218` keeps the labels client-side and maps each to its canonical key |
| `web/src/routes/savings/index.tsx` | `:95`/`:97` stop reading the store; rows come from the opportunities payload only |
| `web/src/routes/savings/savingsViewModel.ts` | `buildTrackedOpportunityRow` retired; `buildOpportunityRows` renders only real opportunities; `:110`'s realized **count** is unchanged |
| `web/tests/routes/renewals/renewalActionStore.test.ts` | deleted with its store |
| `web/tests/routes/renewals/renewalPipelineViewModel.test.ts` | reads a **server** row |
| `web/tests/routes/renewals/RenewalsRoute.test.tsx` | no session write on post |
| `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` | the reverting tick, the two-write Undo, `NotStarted` rendered as "no action taken" |
| `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` | key-based ticks; unknown key ignored, missing key unticked |
| `web/tests/routes/savings/savingsViewModel.test.ts` | only real opportunities; the realized KPI is still a count |
| `web/e2e/v2.spec.ts` | append one case to the existing spec: post an action → reload → the same action on all three surfaces; tick a step → reload → still ticked |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-11, NW-13` (their web halves). One task rather than two because both
retire a store **inside the same file** — `contract360/index.tsx` imports one at
`:6` and the other at `:20`, and both touch `handleUndo` at `:261-269`
(ADR-012 w16 clause 30; ADR-014 w16 clause 5 constraint 5).

Decision rows: `reports/architecture/waves/w16.md`, **NW-11** and **NW-13**,
client-architect cells.

- **Architecture decisions in force**: **ADR-012** w16 clause 21 (both stores
  join §1's disposition table, deleted **with** their read-back and never before
  it), clause 22 (no new `ApiClient` method for NW-11; the `NotStarted` rendering
  rule), clause 26 (keys never indices; the **reverting tick**; §D4's ordering),
  clause 29 (the money fence is already satisfied — **the fence's whole cost is
  that no task may change it**), clause 30 (`buildTrackedOpportunityRow`
  retired); **ADR-028 §D4** (two idempotent writes, ticks first, recovery is a
  **re-read never a compensating write**); **ADR-018** `:112-119`.
- **Design oracle and the anchors this task implements**:
  - `inputs/design/prototypes/raffa-v2/screens-v2.md:106-108` — the four named,
    ordered steps: **Notify · Request revised pricing · Counter with the market
    benchmark · Sign or send non-renewal notice**. These are the wire keys; the
    rendered labels stay parameterized client-side.
  - `inputs/design/prototypes/raffa-v2/screens-v2.md:123-130` — Renewals, with
    `:130` "Status shared with the Contract 360 tracker (`racts`)", and
    `inputs/design/prototypes/raffa-v2/app.jsx:109,166` where `racts` is **one
    shared fact** across Renewals, Contract 360 and Savings. That single fact is
    what `savedAction` now carries.
  - `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137` — the opportunities
    table is **Supplier · Action · Estimate · Status**. An estimate-less
    pseudo-row is a row the oracle does not define, which is the design-side
    ground for clause 30.
  - `reports/architecture/ADR-018-web-information-architecture.md:112-119` — the
    error state is a **2px accent left rule + h4 + the plain-language surface
    name + a secondary Retry**. *"Plain endpoint/job name" means the plain-language
    name ("negotiation steps", "the renewal action"), **never the raw path**:
    `GET /api/contracts/{id}/negotiation-steps` must never reach a procurement
    user.* Note the file name: `ADR-018-web-information-architecture.md`, not
    `ADR-018-information-architecture.md`.
- **Why the flood matters** (clause 30): read back from the server, the tracked
  set stops being *this session's* handful and becomes **every renewal action in
  the tenant, permanently**. A naive store→GET swap ships that flood into the
  opportunities table as a side effect of a read-back nobody asked to change
  Savings.
- **Why the Undo order is load-bearing** (§D4): an action still in progress with
  no ticks is an honest state a user can reach; `NotStarted` with four ticks is a
  contradiction no flow produces. Both writes are idempotent and both facts are
  re-read on mount, so **recovery from a partial failure is a re-read, never a
  compensating write**.
- **Do not touch**: `web/src/api/client.ts`, `web/src/api/generated/schema.ts`,
  `web/openapi/raffa-api.v1.json` (all `E19/F06/US01/T01`, an earlier phase — the
  three wrappers already exist by the time this task runs);
  `web/src/routes/quotes/**` (that is `E19/F02/US02/T01`, **this same phase**);
  `web/src/components/shell/navItems.ts`, `web/src/App.tsx`. Do not add a
  `DELETE` call for the renewal action — none is published.
- **The Playwright case is acceptance-runbook evidence, not a CI gate**
  (ADR-012 §12): no workflow runs Playwright, and it must not be presented as
  one.

## Definition of done

- [ ] `cd web && npm run build` exit 0
- [ ] `cd web && npm test` exit 0
- [ ] `grep -rn "raffa.renewals.actions\|raffa.contract360.steps" web/src` returns **no match**
- [ ] `ls web/src/routes/renewals/renewalActionStore.ts web/src/routes/contracts/contract360/negotiationStepsStore.ts` fails — both files are gone
- [ ] `grep -rn "buildTrackedOpportunityRow" web/src` returns **no match**
- [ ] `grep -n "countOf(kpis.savingsRealized)" web/src/routes/savings/savingsViewModel.ts` still returns line **110** — the realized KPI is still a count, not a money amount
- [ ] `git diff --name-only -- web/src/api web/openapi web/src/routes/quotes backend/` is **empty** for this task

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `renewalPipelineViewModel` builds rows from a **server** row, with no session read | `web/tests/routes/renewals/renewalPipelineViewModel.test.ts` |
| unit | `contract360ViewModel` ticks from **named keys**; an unknown server key is ignored and never rendered as a fifth tick; a missing key is unticked | `web/tests/routes/contracts/contract360/contract360ViewModel.test.ts` |
| unit | `savingsViewModel` renders **only real opportunities**, and the realized KPI is a count | `web/tests/routes/savings/savingsViewModel.test.ts` |
| component | a failed tick `PUT` **reverts** the tick and shows the ADR-018 error state; Undo issues ticks-`PUT`-then-action-`POST`; a `NotStarted` row renders as "no action taken" | `web/tests/routes/contracts/contract360/Contract360Route.test.tsx` |
| component | posting an action writes **nothing** to `sessionStorage` | `web/tests/routes/renewals/RenewalsRoute.test.tsx` |
| e2e (runbook, not CI) | post an action → reload → the same action on Renewals, Contract 360 and Savings; tick a step → reload → still ticked | `web/e2e/v2.spec.ts` |

## Open questions blocking this task

- none. **OQ-w16-ca-01**'s client half is ruled (ADR-012 w16 clause 30); its
  product half — a *designed* Savings section for tracked actions — is **W17**
  and seats ux-ui-designer. Do not build it here.

## Wave-spec entry

```yaml
- id: E19/F07/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-07-session-stores-retired/us-01-actions-and-ticks-on-screen/tasks/task-01-actions-and-ticks-on-screen.md
  produces: [renewal-and-steps-on-screen]
  depends_on: [theme-b-contract-and-client]
  effort: L
  layer: frontend
  status: live
```
