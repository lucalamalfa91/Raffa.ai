---
id: us-01
type: user-story
parent: feature-06
wave: w16
status: active
---

# us-01-theme-b-contract-and-client — Five paths in the contract, three wrappers in the client

## Story

As a **web developer on this codebase**, I want theme B's five routes declared in
`web/openapi/raffa-api.v1.json` and reachable through the generated types and the
typed client, so that **the three retiring `sessionStorage` stores have something
to be replaced by**.

## Acceptance criteria

- [ ] AC-1 The contract declares `GET /api/renewals/{id}/action`,
  `GET /api/quotes`, `GET /api/quotes/{id}`,
  `GET /api/contracts/{id}/negotiation-steps` and
  `PUT /api/contracts/{id}/negotiation-steps`, each with its real response shape.
- [ ] AC-2 `GET /api/renewals`'s row schema carries **`savedAction`**; the
  existing `action` property is unchanged.
- [ ] AC-3 `web/src/api/generated/schema.ts` is regenerated and carries the new
  response types; `cd web && npm run build` exits 0.
- [ ] AC-4 `web/src/api/client.ts` gains exactly three methods — `getQuote`,
  `getNegotiationSteps`, `putNegotiationSteps` — and **no others**.
  `GET /api/renewals/{id}/action` and `GET /api/quotes` are published
  **unwrapped**, with the omission recorded in `info.description` so it reads as
  a decision.
- [ ] AC-5 No route is added, moved or removed in the SPA router; `navItems.ts`,
  `App.tsx` and `WorkspaceShellApp.tsx` are untouched.
- [ ] AC-6 The generated row type for the persisted renewal action is
  **re-exported from `client.ts`** so the four files that import only the
  `TrackedRenewalAction` type have a generated replacement — **no hand-written
  DTO**.

## Definition of done

- [ ] every AC above is verified by at least one test or command named in the task
- [ ] the change honours ADR-026 w16 clause 5, ADR-012 w16 clauses 24–25,
  ADR-028 §D1/§D2/§D3
- [ ] this task is the **only** writer of `web/openapi/raffa-api.v1.json` and of
  `web/src/api/client.ts` in its phase
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E19/F01/US01/T01` | the renewal-action route and `savedAction` must exist before the contract documents them |
| `E19/F02/US01/T01` | the two quote routes |
| `E19/F03/US01/T01` | the two negotiation-step routes |

## Architecture decisions in force

- **ADR-026** w16 clause 5 — the five deltas. **NW-21 contributes none**:
  `savingsPropagated` is already a required `["boolean","null"]` property with no
  `description` (`raffa-api.v1.json:4356`, `:4415-4419`; `schema.ts:504`).
- **ADR-026** w16 clause 4 — the generator parses **only `responses`**; a
  declared parameter never reaches `schema.ts`, so a green build proves nothing
  about a parameter edit.
- **ADR-012** w16 clause 25 — `client.ts` is one-writer-per-phase, and **this
  task owning the wrapper additions is the resolution the clause names**.
- **ADR-012** w16 clause 24 — the wrapper rule and its two published-but-unwrapped
  precedents (`/api/insights/criticality`, `GET /api/contracts/{id}/strategy`).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | theme-b-contract-and-client | M | phase-3 |

## Council decisions carried into this story

> **ADR-012 w16 clause 24, the rule stated as a test**: "A wrapper is added in
> the same task as its first caller. A route with no caller is published in the
> contract and left *unwrapped*, with the omission recorded in
> `info.description` so it reads as a decision and not an oversight."

> **ADR-012 w16 clause 25**: "**Cheapest resolution for the decomposer: the
> theme-B contract task also owns the wrapper additions**, or NW-12's and NW-13's
> web halves land in different phases." — **This wave takes the first branch**,
> so `getNegotiationSteps` / `putNegotiationSteps` land one phase before their
> first caller. Recorded as a deliberate, clause-sanctioned deviation from clause
> 24 in `reports/audit/w16-hitl.md`.

> **ADR-028 §D1**: "the embedded field is `savedAction`, never `action`."

## Open questions

- none.
