---
id: feature-03
type: feature
parent: epic-01
wave: R0
status: active
---

# feature-03-ci-cd-delivery — CI/CD pipelines + promotion

## Slice

Wire GitHub Actions to Azure `dev`/`demo` without stored secrets: OIDC federated
service principals per environment, per-folder path-filtered workflows, and an
explicit tag + `demo` GitHub Environment with required reviewers for promotion.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | CI → Azure OIDC federation (ADR-015) | R0 |
| us-02 | Per-folder path-filtered workflows (ADR-014) | R0 |
| us-03 | Promotion dev→demo tag + environment (ADR-016) | R0 |

## Architecture decisions in force

- ADR-015 — OIDC federated credentials, per-env least-privilege SPs, no stored secrets.
- ADR-014 — per-folder deploy jobs, path filters.
- ADR-016 — tag-triggered `demo` promotion with required reviewers.

## Target repo

`raffa-infra`
