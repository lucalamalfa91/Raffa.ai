---
id: feature-07
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-07 F02, epic-08 F01, epic-08 F02
---

# feature-07-session-stores-retired — Renewals, Contract 360 and Savings read one shared server fact

## Slice

NW-11 and NW-13 both retire a `sessionStorage` store **inside the same file** —
`web/src/routes/contracts/contract360/index.tsx` imports one at `:6` and the
other at `:20`, and both touch `handleUndo` at `:261-269`. They are therefore one
task, not two (ADR-012 w16 clause 30 / ADR-014 w16 clause 5 constraint 5).

This feature deletes `renewalActionStore.ts` and `negotiationStepsStore.ts`,
points Renewals, Contract 360 and Savings at the server's `savedAction` (already
reaching all three through the **existing** `getRenewals` wrapper), makes the
step ticks a write + read-back whose optimistic tick **reverts** on failure, and
retires the Savings pseudo-opportunity row that a naive store→GET swap would turn
into a tenant-wide flood.

The prototype models `racts` as **one shared fact** across all three surfaces
(`inputs/design/prototypes/raffa-v2/app.jsx:109,166`;
`screens-v2.md:130` "Status shared with the Contract 360 tracker (`racts`)") —
which is exactly the server row this feature finally reads.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | actions-and-ticks-on-screen | w16 |

## Architecture decisions in force

- **ADR-012** w16 clause 21 — the disposition table gains
  `raffa.renewals.actions` and `raffa.contract360.steps.<id>`, both **deleted**;
  binding ordering: **a store is deleted *with* its read-back, never before it**.
- **ADR-012** w16 clause 22 — **NW-11 adds no `ApiClient` method at all**;
  Contract 360 already calls `getRenewals` (`contract360/index.tsx:115`). The
  four files that import only the *type* `TrackedRenewalAction` take the
  generated row type re-exported from `client.ts` — **no hand-written DTO**.
  `handleUndo` posts `NotStarted` and the server row survives it, so **every
  surface must render `NotStarted` as "no action taken"** or Undo looks broken.
  `forgetRenewalAction` dies with the store; no `DELETE` is requested.
- **ADR-012** w16 clause 26 — keys never indices; an **unknown step key from the
  server is ignored**, never rendered as a fifth tick; a **missing key is
  unticked**; `NEGOTIATION_STEP_COUNT = 4` stops shaping the wire; **the
  optimistic tick reverts to the last server-known set on failure** and shows
  ADR-018 `:117`'s error state. *A tick the server rejected must never survive on
  screen.*
- **ADR-012** w16 clause 30 — **`buildTrackedOpportunityRow` is retired**;
  Savings renders only real `SavingsOpportunity` rows. Read back from the server
  the tracked set becomes every renewal action in the tenant, permanently — a
  flood nobody asked for as a side effect of a read-back.
- **ADR-028 §D4** — ticks `PUT` first, then the action `POST`. The order is
  load-bearing: an action in progress with no ticks is honest; `NotStarted` with
  four ticks is a contradiction no flow produces.
- **ADR-018** `:112-119` — the IA-level empty / error / loading contract; the
  error state is a 2px accent left rule + h4 + the **plain-language** surface
  name + a secondary Retry. *Never the raw path* —
  `GET /api/contracts/{id}/negotiation-steps` must never reach a procurement user.

## Target repo

`raffa-web`
