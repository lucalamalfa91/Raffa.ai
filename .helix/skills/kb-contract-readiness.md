# KB contract — demo-readiness delta

Append-only. Starts at **epic-10 / e10**.

## Never write

```
reports/plan/wave-spec.execution.yaml
reports/plan/wave-spec.web.yaml
reports/plan/wave-spec.schema.yaml
reports/plan/slices/e01.yaml … e09.yaml
reports/plan/slice.current.yaml
reports/architecture/ADR-001*.md … ADR-021*.md
reports/workitems/epic-01-*/ … epic-09-*/
```

## May write

```
reports/context/demo-readiness-mandate.md
reports/audit/demo-readiness-gaps.md
reports/architecture/ADR-022-*.md
reports/architecture/draft/*-readiness/
reports/workitems/epic-10-*/
reports/plan/wave-spec.readiness.yaml
reports/plan/slices/e10.yaml
reports/plan/slices/INDEX-readiness.md
reports/plan/slices/MANIFEST-readiness.yaml
```

`INDEX.md` / `BACKLOG.md`: read full file, append, write complete file back.
`MANIFEST.yaml`: append an `e10` row only (`previous: e09`).

## Bash allowed

```
python scripts/cut_readiness_slices.py
```
