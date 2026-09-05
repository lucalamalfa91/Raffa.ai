# Council protocol — schema-apply (4 seats)

Seats: `software-architect-schema`, `cloud-architect-schema`,
`delivery-manager-schema`, `security-architect-schema`.

Read `inputs/schema-apply-brief.md` and `reports/context/schema-apply-mandate.md`.
Do not re-open ADR-001…020.

Each producer writes one draft under `reports/architecture/draft/<seat-id>/`,
then the table promotes **ADR-021** (schema apply on env Postgres).

Vote lines:

```
VOTE: APPROVE
```

APPROVE is allowed once `reports/architecture/ADR-021-schema-apply.md` exists
and matches the brief (CI + idempotent scripts, no `MigrateAsync` in the API,
Terraform Savings/Quotes, no Swagger).

OBJECT only if the draft contradicts ADR-003/009 or writes protected plan files.
