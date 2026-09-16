---
id: E23/F05/US01/T01
type: task
story: us-01-w18-final-integration
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-w18-final-integration — Wave w18 integration + dev acceptance runbook

## Coding objective
Run the whole wave green: `dotnet build backend/Raffa.slnx`, per-project
`dotnet test` (AiGateway, Documents.Contracts, Api, plus any touched test
project); `npm run typecheck`, `npm run lint`, `npm test` in `web/`; run the
e2e cases that need no live Foundry (the reconciled `day1.spec.ts` vitest-type
cases). Sweep `backend/README.md` and `web/README.md` for the public surfaces
that changed (the new evidence `box`, the phrase-edit route, the portfolio
category filter, the savings filters, the citation deep-link, the scoped Ask
brief, the quote benchmark, the abstain recovery action, the suppressed global
Ask bar). Write `docs/waves/w18-acceptance.md`: per item, the manual `dev` check
(what to click, what to expect), from the story ACs — N17r, A18-p, A18-s,
A17-S3, A18-o, A18-ops, A18-e2e, N10, N11, N13, N14 — and record the two ops
walks as runbook steps (NW-40: confirm HCP apply landed `ConnectionStrings__Storage`
/ `AiGateway__*` / `AzureAd__*` on `dev`+`demo` and Foundry RBAC on both;
NW-41: a `dev` walk proving a seeded `market_record` row is served by
`GET /api/market/records/{id}`).

## Parent story AC covered
- AC-1 backend build + test exit 0.
- AC-2 web typecheck/lint/test exit 0.
- AC-3 `docs/waves/w18-acceptance.md` lists every item's manual check.
- AC-4 NW-40/NW-41 recorded as runbook steps.

## Files to create or modify
| Path | Change |
|------|--------|
| docs/waves/w18-acceptance.md | new: per-item manual `dev` checks + the two ops walk steps |

## Context the implementer needs

**Closes: the wave — NW-63r, NW-23, NW-25, NW-74, NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60** (NW-40/NW-41 are runbook walks recorded here, no separate task)

- **Architecture decisions in force**: ADR-016 (the runbook is recorded, never dispatched; promotion is the operator's HITL gate); ADR-005 (the ops walks are confirm-only, no Terraform change).
- **Do not touch**: `reports/plan/slice.current.yaml`, `wave-spec.*.yaml`, `reports/plan/slices/w18.yaml` (decomposition artifacts); product code beyond a README sweep.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0 (and each touched test project)
- [ ] `npm run typecheck` and `npm run lint` and `npm test` (in `web/`) exit 0
- [ ] `docs/waves/w18-acceptance.md` exists and lists every item's check

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | the whole wave is green | backend + web test suites |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E23/F05/US01/T01
  prompt: reports/workitems/epic-23-viewer-bounding-boxes-and-phrase-edit/feature-05-w18-final-integration/us-01-w18-final-integration/tasks/task-01-w18-final-integration.md
  produces: [w18-integration]
  depends_on: [ocr-geometry-wire, evidence-geometry-schema, phrase-edit-endpoint, viewer-box-overlay, portfolio-category-filter, portfolio-category-control, savings-filters, ask-chip-role-gate, citation-viewer-deeplink, citation-card-treatments, scoped-ask-planner, scoped-ask-brief, quote-benchmark-history, quote-benchmark-ux, abstain-recovery-action, abstain-recovery-render, global-bar-suppressed, openapi-swept, e2e-reconciled]
  effort: L
  layer: backend
  status: live
```
