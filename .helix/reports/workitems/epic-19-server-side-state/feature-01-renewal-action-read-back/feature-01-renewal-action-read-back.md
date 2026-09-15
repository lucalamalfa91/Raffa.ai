---
id: feature-01
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-03 F03, epic-08 F01
---

# feature-01-renewal-action-read-back — The renewal action a user took is readable over HTTP

## Slice

`POST /api/renewals/{id}/action` already persists to `renewal_action`
(`backend/src/Raffa.Renewals/Application/RenewalActionService.cs:107-128`, unique
`(tenant_id, contract_id)`, RLS-protected). `GetActionAsync` exists at `:158-171`
with **zero production callers** and its own comment at `:150-157` saying no HTTP
route calls it yet. This feature routes it: `GET /api/renewals/{id}/action`
(200/404) plus the persisted row embedded in every `GET /api/renewals` row under
the name **`savedAction`** — never `action`, which already carries the
deterministic calculator's `RecommendedAction` (`RenewalsEndpointExtensions.cs:239`).
Renewals and Savings are *list* surfaces, so a per-row GET would be an N+1;
the embedded field is what lets all three surfaces read one shared fact.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | renewal-action-api | w16 |

The web half of NW-11 is **feature-07** (combined with NW-13, which writes the
same `contract360/index.tsx`), and the contract publication is **feature-06**.

## Architecture decisions in force

- **ADR-028 §D1** — the route, the 200/404, `savedAction` as a binding name, no
  widening with `supplierId` / `annualSpend`, **no `DELETE` route** (absence of a
  row is the status `NotStarted`), **no migration**.
- **ADR-026** w16 clause 5 — both deltas are published by feature-06, not here.
- **ADR-009** w16 clause 3 — `renewal_action` is already `ENABLE` + `FORCE` +
  `tenant_isolation` (`Raffa.Renewals/Migrations/20260904223135_AddTenantRowLevelSecurity.cs:42,52-54`);
  no policy work.
- **ADR-002** w16 clause 4 — `RenewalActionService` takes the actor as a required
  parameter from NW-32, which lands in a strictly earlier phase.

## Target repo

`raffa-backend`
