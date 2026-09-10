---
id: E01/F03/US02/T01
type: task
story: us-02-per-folder-workflows
wave: R0
status: live
target_repo: raffa-infra
---

# task-01-per-folder-workflows — Author per-folder workflows + `dev` triggers + non-blocking mobile

## Coding objective

Author GitHub Actions workflows for `infra/`, `backend/`, `web/`, and `mobile/`
folders (ADR-014): each uses `paths:` filters on its own folder, and the `dev`
deployment triggers on merge to `main` via the OIDC auth step (ADR-015). The
`mobile/` lane is marked non-blocking — its failure must never block `dev`/`demo`
promotion (ADR-013). The `infra` workflow runs `terraform plan`/`apply` against the
correct HCP workspace; `backend`/`web`/`mobile` build and deploy their artifact to
the `dev` environment.

## Parent story AC covered

- AC-1 (four workflows, path filters)
- AC-2 (`dev` on merge to `main`)
- AC-3 (mobile non-blocking)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/.github/workflows/infra.yml | infra plan/apply to `dev` |
| workspace/raffa-infra/.github/workflows/backend.yml | backend build/deploy `dev` |
| workspace/raffa-infra/.github/workflows/web.yml | web build/deploy `dev` |
| workspace/raffa-infra/.github/workflows/mobile.yml | mobile build (non-blocking) |

## Context the implementer needs

- **Architecture decisions in force**: ADR-014 (path filters, `main`→`dev`), ADR-015 (OIDC), ADR-013 (mobile non-blocking).
- **Do not touch**: `demo` promotion workflow (us-03).

## Definition of done

- [ ] `infra.yml`, `backend.yml`, `web.yml`, `mobile.yml` exist with correct path filters and `mobile` marked non-blocking.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| lint | YAML valid + path filters + non-blocking mobile | `.github/workflows/*.yml` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F03/US02/T01
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-02-per-folder-workflows/tasks/task-01-per-folder-workflows.md
  produces: [ci-cd-workflows]
  depends_on: [ci-azure-auth]
  effort: L
  layer: backend
  status: live
```
