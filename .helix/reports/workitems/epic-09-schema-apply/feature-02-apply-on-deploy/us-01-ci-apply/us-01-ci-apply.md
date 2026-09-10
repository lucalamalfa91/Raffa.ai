---
id: us-01
type: user-story
parent: feature-02
wave: 9
status: active
---

# us-01-ci-apply — Terraform wiring + backend.yml apply

## Acceptance criteria

- [ ] AC-1 API (and worker if it already takes Renewals) get
      `ConnectionStrings__Savings` and `ConnectionStrings__Quotes` → `pg-cs`.
- [ ] AC-2 `backend.yml` applies the six scripts in ADR-021 order after
      container update, using Key Vault `postgres-connection`.
- [ ] AC-3 After the job, `raffa_dev` has app tables +
      `__EFMigrationsHistory` per module (prove via `az`/`psql` in the job
      or a follow-up check). HCP apply is confirmed, not a laptop apply.
