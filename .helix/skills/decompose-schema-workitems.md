# Decomposition — schema-apply (epic-09 / e09)

## Locked

Do not rewrite epic-01…08, ADR-001…020, `wave-spec.execution.yaml`,
`slices/e01.yaml`–`e08.yaml`, `slice.current.yaml`.

Ids start at **E09/F01/US01/T01**. `layer: backend`. `target_repo: raffa-backend`.

## Shape (keep it small)

- **F01 scripts** — idempotent SQL for Identity, Audit, Renewals, Savings, Quotes
  (Documents already has a script). Tests like `DocumentsContractsMigrationScriptTests`.
- **F02 apply-on-deploy** — Terraform `ConnectionStrings__Savings` /
  `ConnectionStrings__Quotes`; `backend.yml` applies scripts after container
  update; prove tables on `dev`.

No Swagger tasks. No `MigrateAsync` in `Raffa.Api`.

After `wave-spec.schema.yaml`, run only:

```
python scripts/cut_schema_slices.py
```

## Checker fail

- missing epic-09 or `e09.yaml`
- any write to e01–e08 / `slice.current.yaml` / `wave-spec.execution.yaml`
- task that adds Swagger UI
- task that calls `MigrateAsync` in the API host
