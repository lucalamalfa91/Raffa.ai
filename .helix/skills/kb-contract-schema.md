# KB contract — schema-apply delta

Append-only. Starts at **epic-09 / e09**.

## Never write

```
reports/plan/wave-spec.execution.yaml
reports/plan/wave-spec.web.yaml
reports/plan/slices/e01.yaml … e08.yaml
reports/plan/slice.current.yaml
reports/architecture/ADR-001*.md … ADR-020*.md
reports/workitems/epic-01-*/ … epic-08-*/
```

## May write

```
reports/context/schema-apply-mandate.md
reports/architecture/ADR-021-*.md
reports/architecture/draft/*-schema/
reports/workitems/epic-09-*/
reports/plan/wave-spec.schema.yaml
reports/plan/slices/e09.yaml
reports/plan/slices/INDEX-schema.md
reports/plan/slices/MANIFEST-schema.yaml
```

`INDEX.md` / `BACKLOG.md`: read full file, append, write complete file back.

`MANIFEST.yaml` (live HITL table): **append** an `e09` row only (`previous: e05`).
Do not rewrite e01–e05 rows.

## Bash allowed

```
python scripts/cut_schema_slices.py
```

Never `cut_nightly_slices.py`, never `cut_web_slices.py`.
