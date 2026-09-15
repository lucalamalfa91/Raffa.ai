---
id: feature-06
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-08 F01, epic-06 F02
---

# feature-06-contract-and-client-publication — Theme B's five paths reach the contract and the typed client

## Slice

`web/openapi/raffa-api.v1.json` is a **one-writer-per-phase** file (ADR-012 §3):
`web/src/api/generated/schema.ts` is regenerated wholesale, so two tasks editing
the contract inside one phase produce a conflicting artefact, not a mergeable
diff. `web/src/api/client.ts` is hand-written glue and carries the same rule
(ADR-012 w16 clause 25). Five theme-B contract deltas and three new `ApiClient`
methods therefore land in **one task, in one phase**, after the backend routes
they document and before the web halves that call them.

The five deltas: `GET /api/renewals/{id}/action` (new), the **`savedAction`**
field on `GET /api/renewals` rows, `GET /api/quotes` (new),
`GET /api/quotes/{id}` (new), `GET` + `PUT /api/contracts/{id}/negotiation-steps`
(new). **NW-21 contributes nothing** — `savingsPropagated` is already a required
`["boolean","null"]` property with no `description`, verified at the table.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | theme-b-contract-and-client | w16 |

## Architecture decisions in force

- **ADR-026** w16 clause 5 — the five paths and their shapes; clause 4 — the
  generator parses **only `responses`**, so a declared parameter never reaches
  `schema.ts` and a contract edit's gate is a **grep, not a green build**.
- **ADR-012** w16 clause 24 — *a wrapper is added in the same task as its first
  caller*; a route with no caller is published and left **unwrapped**, with the
  omission recorded in `info.description`. Clause 25 makes `client.ts`
  one-writer-per-phase and names **this task owning the wrapper additions** as
  the cheapest resolution for the decomposer — which is the branch taken here,
  and the reason `getNegotiationSteps` / `putNegotiationSteps` land one phase
  before their callers. Recorded in `reports/audit/w16-hitl.md`.
- **ADR-028 §D1/§D2/§D3** — the shapes; `savedAction` **never** `action`.
- **ADR-018** `none`, **ADR-020** `none` — no route, no rail entry, no screen.

## Target repo

`raffa-web`
