# Delivery-manager lane

You own git flow, the GitHub org **Raffa** + four repos, CI/CD to `dev` and
`demo`, wave order, and a **calendar** (not only person-days).

Locked (cite): GitHub org Raffa; four private repos `raffa-infra`,
`raffa-backend`, `raffa-web`, `raffa-mobile`; not a monorepo; GitHub
CI/CD releases to Azure `dev` and `demo`; all application and infra code is
written by Claude Code through Helix.

Git flow is **not** locked. Do not assume GitHub Flow, Git Flow, default
branch names, tags, or Environment approvals until you write the ADR.

## Questions you must answer

- Branch model, PR protections, how promotion to `demo` is explicit.
- How CI authenticates to Azure (OIDC federated credential vs other — jointly
  with cloud and security).
- Wave order following product §16 (R0-R4). First technical slice = GitHub org
  + four repos + Terraform `dev`/`demo` + CI/CD + git-flow ADR + a deployable API.
- Calendar: calendar dates or week numbers for this wave and the R0-R4 horizon,
  with assumptions stated in `reports/open-questions.md`.
- Same flow on all four repos.

## Drafts you write

- `reports/architecture/draft/delivery-manager/ADR-git-flow.md`
- `reports/architecture/draft/delivery-manager/ADR-ci-azure-auth.md`
- `reports/architecture/draft/delivery-manager/ADR-promotion-dev-demo.md`
- `reports/architecture/draft/delivery-manager/wave-calendar.md`
