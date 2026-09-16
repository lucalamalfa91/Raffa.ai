---
id: E26/F02/US01/T01
type: task
story: us-01-e2e-reconcile
wave: w18
status: live
target_repo: raffa-web
---

# task-01-e2e-reconcile — Reconcile day1.spec.ts to the V2 shell (NW-50)

## Coding objective
Reconcile `web/e2e/day1.spec.ts` (authored pre-V2, where Ask is home) to the V2
shell so the Day-1 walk does not go red for stale reasons: correct the V2-shell
assumptions (Ask is home at `/ask`; the rail item is `/savings`, not "Home";
the V2 Savings KPI cells are Contracts analyzed · Upcoming renewals · Savings
identified — matching `savingsViewModel.ts`, not the pre-V2 six-cell Home KPI),
drop/replace any pre-V2 assertion that no longer matches the real screens, and
keep every remaining step real and unconditional with no unexplained
`test.skip` (the one legitimate `test.skip` remains the missing
`RAFFA_E2E_BASE_URL`/credentials gate, which is a declared prerequisite, not a
smoke-disguising skip). Keep the "runbook spec, never CI" shape (ADR-016): no
workflow runs Playwright; the spec runs under the Playwright harness an
operator invokes.

## Parent story AC covered
- AC-1 V2-shell assumptions corrected.
- AC-2 real unconditional steps, no unexplained skip.
- AC-3 runbook spec, never CI.

## Files to create or modify
| Path | Change |
|------|--------|
| web/e2e/day1.spec.ts | reconcile pre-V2 assumptions to the V2 shell |

## Context the implementer needs

**Closes: NW-50**

- **Architecture decisions in force**: ADR-016 (the walk is a runbook spec, never CI); ADR-012 (the V2 shell is the target — `web/src/routes/savings` is the Savings surface, `/ask` is home).
- **Do not touch**: `web.yml` (no Playwright in CI); the product screens (this is a spec reconciliation only).

## Definition of done
- [ ] `npm run typecheck` exits 0 (the spec's imports/asserts stay valid)
- [ ] `grep` over `web/e2e/day1.spec.ts` returns no pre-V2 "Home" KPI / Ask-is-not-home assertion that contradicts the V2 shell
- [ ] `npx playwright test web/e2e/day1.spec.ts --list` (operator harness) enumerates the reconciled steps with only the declared credentials skip

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| (grep/list) | no stale pre-V2 assertion; steps are reconciled | `web/e2e/day1.spec.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E26/F02/US01/T01
  prompt: reports/workitems/epic-26-contract-and-ops-residuals/feature-02-e2e-reconcile/us-01-e2e-reconcile/tasks/task-01-e2e-reconcile.md
  produces: [e2e-reconciled]
  depends_on: []
  effort: M
  layer: frontend
  status: live
```
