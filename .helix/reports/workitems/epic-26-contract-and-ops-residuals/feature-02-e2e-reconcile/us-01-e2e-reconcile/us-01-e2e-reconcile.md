---
id: us-01
type: user-story
parent: feature-02
wave: w18
status: active
---

# us-01-e2e-reconcile — The Day-1 spec matches the real V2 shell

## Story
As the **operator**, I want `web/e2e/day1.spec.ts` to match the real V2 shell
(Ask is not home; no pre-V2 gates), so the Day-1 walk does not go red for the
wrong reason.

## Acceptance criteria
- [ ] AC-1 the spec's V2-shell assumptions are corrected (Ask is home, `/ask`; no pre-V2 "Home" KPI assertions vs the V2 `/savings` surface, etc.).
- [ ] AC-2 every remaining step is real and unconditional, with no unexplained `test.skip`.
- [ ] AC-3 the spec runs under the vitest/Playwright harness (never CI).

## Definition of done
- [ ] every AC above is verified by at least one test/command named in a task
- [ ] honours ADR-016 (runbook spec, never CI)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | the spec already exists (`web/e2e/day1.spec.ts`); only the V2 reconciliation is owed |

## Architecture decisions in force
- ADR-016 — the walk is a runbook spec, never CI; ADR-012 — the V2 shell is the target.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | e2e-reconcile | M | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Reconcile, never paper over with an unexplained skip; run under the Playwright harness, never CI.

## Open questions
- none
