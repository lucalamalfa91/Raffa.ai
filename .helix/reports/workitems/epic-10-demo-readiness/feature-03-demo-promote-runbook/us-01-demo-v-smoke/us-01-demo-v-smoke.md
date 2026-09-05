---
id: us-01
type: user-story
parent: feature-03
wave: 10
status: active
---

# us-01-demo-v-smoke — `demo-v*` runbook + SWA config check

## Acceptance criteria

- [ ] AC-1 A short runbook: tag `demo-v*`, GitHub Environment reviewers,
      `demo-promote.yml` (ADR-016).
- [ ] AC-2 After promote, `demo` SWA `/config.json` has the demo API URL
      and Entra public client (not localhost, not `dev`).
- [ ] AC-3 Does not rebuild e08 final-integration screens; this is infra
      smoke so that walk can start.
