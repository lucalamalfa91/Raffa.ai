---
id: E19/F01/US01/T01
type: task
story: us-01-renewal-action-api
wave: w16
status: live
target_repo: raffa-backend
---

# task-01-renewal-action-api — Route the renewal action that is already persisted

## Coding objective

`RenewalActionService.GetActionAsync`
(`backend/src/Raffa.Renewals/Application/RenewalActionService.cs:158-171`) exists,
is tenant-scoped, returns `RenewalActionResult(ContractId, Owner, Status, Action,
UpdatedAt)`, and has **zero production callers** — its own comment at `:150-157`
says "no HTTP route calls this yet". Route it.

In `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` (which maps three routes
today at `:95`, `:96`, `:97`), add **`GET /api/renewals/{id}/action`** returning
`200` with the persisted row and `404` when the contract has none, using the same
`ICallerContext.ResolveTenantAsync` ladder the sibling routes use. **`{id}` keeps
exactly the meaning it has on the existing `POST /api/renewals/{id}/action`** —
confirm that against the POST handler `PostRenewalActionAsync` at `:353` **before
writing anything**, and state the confirmed meaning in the handler's doc comment
so `E19/F06/US01/T01` can document it truthfully.

Then embed the persisted row in **every** `GET /api/renewals` row under the field
name **`savedAction`** (or `null`). Renewals and Savings are list surfaces and a
per-row GET would be an N+1 across the portfolio; the embedded field is what lets
all three surfaces read one shared fact with one call.

## Parent story AC covered

- AC-1 `GET /api/renewals/{id}/action` → `200` with the persisted row, `404` when none exists.
- AC-2 Every `GET /api/renewals` row carries `savedAction`; the existing `action` field is unchanged.
- AC-3 Another tenant asking for the same contract id gets `404` and zero tenant-A rows.
- AC-4 No token `401`; non-GUID `X-Tenant-Id` `400`; non-member `404`.
- AC-5 No `DELETE` route; the row survives a `NotStarted` post.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` | map `GET /api/renewals/{id}/action` + its handler; add `savedAction` to the `GET /api/renewals` row projection **beside** the existing `action` at `:239` |
| `backend/src/Raffa.Renewals/Application/RenewalActionService.cs` | add the batch read the list projection needs (one query for the page's contract ids), beside the existing `GetActionAsync` at `:158-171`; retire its "no HTTP route calls this yet" comment at `:150-157` |
| `backend/tests/Raffa.IntegrationTests/RenewalActionReadBackTests.cs` | new — the route, the list embedding and the cross-tenant negative |
| `backend/tests/Raffa.Renewals.Tests/` | the batch read's unit cover |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-11`.

Decision row: `reports/architecture/waves/w16.md`, **NW-11**
(software-architect, client-architect).

- **Architecture decisions in force**: **ADR-028 §D1** (the route, the embedded
  field, the binding name, no widening, no `DELETE`, **no migration**);
  **ADR-026** w16 clause 5 (the contract publication belongs to
  `E19/F06/US01/T01`); **ADR-009** w16 clause 3 (`renewal_action` is already
  `ENABLE` + `FORCE` + `tenant_isolation` —
  `backend/src/Raffa.Renewals/Migrations/20260904223135_AddTenantRowLevelSecurity.cs:42,52-54`
  — so there is **no policy work**, but the cross-tenant negative is still owed);
  **ADR-002** w16 clause 4 (this file's service already takes the required actor
  parameter from `E18/F03/US02/T01`, a strictly earlier phase — do not re-open
  that signature).
- **THE NAME IS BINDING, NOT A PREFERENCE.** ADR-028 §D1: the embedded field is
  **`savedAction`, never `action`**. `action` on `GET /api/renewals`
  (`RenewalsEndpointExtensions.cs:239`) is the deterministic calculator's
  `RecommendedAction`, rendered on a shipped screen; reusing the name would
  overwrite a calculator's output with user state.
- **Do not widen the row** with `supplierId` or `annualSpend` — the client
  already holds both (client C9, adopted by §D1).
- **No `DELETE` route.** Absence of a row **is** the status `NotStarted`. The
  table is an upsert on `(tenant_id, contract_id)`
  (`backend/src/Raffa.Renewals/Infrastructure/Configurations/RenewalActionConfiguration.cs:41`),
  so the row legitimately survives an Undo; the web half renders `NotStarted` as
  "no action taken".
- **Design oracle** (this is a backend task, recorded because it is *why* the
  field is shared): `inputs/design/prototypes/raffa-v2/screens-v2.md:130`
  — "Status shared with the Contract 360 tracker (`racts`)" — and
  `inputs/design/prototypes/raffa-v2/app.jsx:109,166`, where `racts` is **one
  shared fact** across Renewals, Contract 360 and Savings. That single fact is
  the row this route exposes.
- **Do not touch**: `web/openapi/raffa-api.v1.json`, `web/src/api/client.ts`,
  `web/src/api/generated/schema.ts` (all `E19/F06/US01/T01`, one phase later);
  anything under `web/src/routes/` (that is `E19/F07/US01/T01`);
  `backend/tests/Raffa.IntegrationTests/R0IntegrationFixture.cs` (a sibling task
  of this phase is rewriting it).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Renewals.Tests` exit 0
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter RenewalActionReadBackTests` exit 0
- [ ] `grep -n "savedAction" backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` returns at least one match, and `grep -c "action = recommendations.RecommendedAction" backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` is still **1** — the calculator's field was not renamed or overwritten
- [ ] `grep -rn "MapDelete(\"/api/renewals" backend/src` returns **no match**
- [ ] `git diff --name-only -- web/` is **empty** for this task

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| API | `GET /api/renewals/{id}/action` returns the persisted row, and `404` for a contract with none | `backend/tests/Raffa.IntegrationTests/RenewalActionReadBackTests.cs` |
| API | **second-caller read-back** — write as session A, read as a different session, get the same fact | `backend/tests/Raffa.IntegrationTests/RenewalActionReadBackTests.cs` |
| API | **cross-tenant negative** — tenant B never sees tenant A's action, on the route **and** in the list row | `backend/tests/Raffa.IntegrationTests/RenewalActionReadBackTests.cs` |
| API | a `NotStarted` post leaves the row in place and the list still reports it as `savedAction` | `backend/tests/Raffa.IntegrationTests/RenewalActionReadBackTests.cs` |
| unit | the batch read returns one row per contract id and nothing for the rest | `backend/tests/Raffa.Renewals.Tests/` |

## Open questions blocking this task

- none. **ADR-028 assumption 1** (that `{id}` is the contract id) is the one thing
  to confirm on disk before writing, at `RenewalsEndpointExtensions.cs:353`.

## Wave-spec entry

```yaml
- id: E19/F01/US01/T01
  prompt: reports/workitems/epic-19-server-side-state/feature-01-renewal-action-read-back/us-01-renewal-action-api/tasks/task-01-renewal-action-api.md
  produces: [renewal-action-api]
  depends_on: [actor-on-every-write]
  effort: M
  layer: backend
  status: live
```
