---
id: E01/F03/US03/T01
type: task
story: us-03-promotion-dev-demo
wave: R0
status: live
target_repo: raffa-infra
---

# task-01-promotion-workflow — Author `demo` promotion workflow (tag + environment + reviewers)

## Coding objective

Author the `demo` promotion workflow (ADR-016): it triggers on a tag matching
`demo-v*` on `main`, runs under a GitHub Environment named `demo` with required
reviewers (product-owner + security-architect, OQ-DM-002), and reuses the per-folder
deploy jobs running under the `demo` CI identity / `demo` resource group (ADR-015).
Promotion moves code/artifacts only — it must never copy a database, storage
account, or secret between environments (ADR-016, ADR-001).

## Parent story AC covered

- AC-1 (`demo-v*` tag triggers)
- AC-2 (`demo` environment + required reviewers)
- AC-3 (code/artifacts only, no data)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/.github/workflows/demo-promote.yml | tag-triggered, env-gated promotion |

## Context the implementer needs

- **Architecture decisions in force**: ADR-016 (tag + env + reviewers, code-only), ADR-015 (demo SP), ADR-014 (same repo).
- **Do not touch**: `dev` workflow triggers.

## Definition of done

- [ ] `demo-promote.yml` present: `on: push tags: demo-v*`, `environment: demo`, required reviewers documented.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| lint | YAML valid, tag trigger + environment + reviewers | `.github/workflows/demo-promote.yml` |

## Open questions blocking this task

- OQ-DM-001, OQ-DM-002 — assumptions in force as above.

## Wave-spec entry

```yaml
- id: E01/F03/US03/T01
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-03-promotion-dev-demo/tasks/task-01-promotion-workflow.md
  produces: [demo-promotion]
  depends_on: [ci-cd-workflows]
  effort: M
  layer: backend
  status: live
```
