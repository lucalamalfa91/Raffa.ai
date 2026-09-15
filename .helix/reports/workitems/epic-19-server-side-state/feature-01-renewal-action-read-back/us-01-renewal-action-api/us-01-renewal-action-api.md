---
id: us-01
type: user-story
parent: feature-01
wave: w16
status: active
---

# us-01-renewal-action-api — The action I took on a renewal is readable over HTTP

## Story

As a **Procurement member**, I want the renewal action I recorded to be readable
back from the server, so that **a reload, a second device or a colleague sees the
same action instead of an empty tracker**.

## Acceptance criteria

- [ ] AC-1 `GET /api/renewals/{id}/action` returns `200` with the persisted
  owner, status, action and `updatedAt` for a contract that has one, and `404`
  when it has none. `{id}` means exactly what it means on the existing
  `POST /api/renewals/{id}/action`.
- [ ] AC-2 Every row of `GET /api/renewals` carries the persisted row under the
  field name **`savedAction`** (or `null`). The existing `action` field is
  unchanged and still carries the calculator's recommended action.
- [ ] AC-3 A member of another tenant asking for the same contract id gets `404`
  and **zero rows of tenant A in any form**.
- [ ] AC-4 A caller with no validated token gets `401`; a non-GUID
  `X-Tenant-Id` gets `400`; a well-formed tenant the caller is not a member of
  gets `404`.
- [ ] AC-5 No `DELETE` route is published, and the row survives a `NotStarted`
  post — absence of a row **is** the status `NotStarted`.

Each AC is checkable with `curl` by someone who has not read the code.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours ADR-028 §D1, ADR-026 w16 clause 5, ADR-009 w16 clause 3
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E18/F03/US02/T01` (NW-32) | `RenewalActionService` gains a required actor parameter there; the same file is edited, so it must land in a strictly earlier phase |

## Architecture decisions in force

- **ADR-028 §D1** — 200/404; the row embedded in every list row under
  **`savedAction`, never `action`**; **no widening** with `supplierId` /
  `annualSpend`; **no `DELETE`**; **no migration**.
- **ADR-026** w16 clause 5 — the two contract deltas are published by
  `E19/F06/US01/T01`, one phase later. **This story does not open
  `web/openapi/raffa-api.v1.json`.**
- **ADR-009** w16 clause 3 — `renewal_action` already carries its policy; no RLS
  work, but the cross-tenant negative is still owed.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | renewal-action-api | M | phase-2 |

## Council decisions carried into this story

> **ADR-028 §D1, verbatim on the name**: "the embedded field is `savedAction`,
> never `action` — that name already carries the computed `RecommendedAction`
> (`RenewalsEndpointExtensions.cs:239`), and reusing it would overwrite a
> deterministic calculator's output with user state on a shipped screen."

> **ADR-028 §D1**: "Absence of a row is the status `NotStarted`, not a missing
> fact. No `DELETE` route is published; 'Undo' is a write of `NotStarted`, and
> the upsert on `(tenant_id, contract_id)` means the row legitimately survives
> it."

> **ADR-028 assumption 1**: `{id}` on `POST /api/renewals/{id}/action` is the
> **contract** id (the service keys on `ContractId`); the task **confirms it at
> `RenewalsEndpointExtensions.cs:353`** before the contract is written.

## Open questions

- none. OQ-w16-ca-01 (the Savings pseudo-row) is ruled in ADR-012 w16 clause 30
  and lands in `E19/F07/US01/T01`, not here.
