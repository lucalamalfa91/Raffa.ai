---
id: E10/F03/US01/T01
type: task
story: us-01-demo-v-smoke
wave: 10
status: live
target_repo: contigo-infra
---

# task-01-demo-v-runbook — Promote runbook + SWA config smoke

## Coding objective

Write the operator runbook for the first `demo-v*` (ADR-016) and a check
that the demo Static Web App serves a `config.json` aimed at the demo API
+ Entra public client. Reuse `demo-promote.yml` / `web.yml`; do not invent
a second promotion path. Do not implement §20 screens (e08). Do not apply
schema (e09).

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| docs or `.helix/reports/execution/` runbook | first `demo-v*` steps |
| optional CI check | GET SWA `/config.json` and assert API origin |

## Context the implementer needs

- **Architecture decisions in force**: ADR-012, ADR-016, ADR-022.
- `web.yml` already injects `config.json`. This task proves the **demo**
  values landed.
- Reviewers on the `demo` GitHub Environment remain required.

## Definition of done

- [ ] Runbook committed. A recorded check shows demo SWA `config.json` is
      not localhost and not the `dev` API URL (or documents the exact
      blocker if promote has not been approved yet).

## Wave-spec entry

```yaml
- {id: E10/F03/US01/T01, prompt: reports/workitems/epic-10-demo-readiness/feature-03-demo-promote-runbook/us-01-demo-v-smoke/tasks/task-01-demo-v-runbook.md, produces: [demo-v-runbook], depends_on: [foundry-ocr-wired], effort: M, layer: backend, status: live}
```
