# Schema-apply mandate

Copied from `inputs/schema-apply-brief.md` for later seats. Delta only.

- ADR-001…020 and epic-01…08 are **done**. Do not rewrite them.
- New work is **epic-09 / slice e09**.
- Never write `wave-spec.execution.yaml`, `slices/e01.yaml`–`e05.yaml`, or
  `slice.current.yaml` (e05 may be live on the other artifact).
- Mechanism to lock in ADR-021: CI applies checked-in idempotent EF SQL
  scripts to Azure Flexible Server after `az containerapp update`. No
  `Database.MigrateAsync()` in `Contigo.Api`.
- Terraform must inject `ConnectionStrings__Savings` and
  `ConnectionStrings__Quotes` (secret `pg-cs`). HCP apply, not laptop apply.
- No Swagger UI.

`CONTEXT_READY: schema-apply-mandate`
