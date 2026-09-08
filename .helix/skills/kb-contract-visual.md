# KB contract — visual fidelity delta

Append-only. Starts at **epic-11 / e11**.

## Never write

```
reports/plan/wave-spec.execution.yaml
reports/plan/wave-spec.web.yaml
reports/plan/wave-spec.schema.yaml
reports/plan/wave-spec.readiness.yaml
reports/plan/slices/e01.yaml … e10.yaml
reports/plan/slice.current.yaml
reports/architecture/ADR-001*.md … ADR-022*.md
reports/workitems/epic-01-*/ … epic-10-*/
```

## May write

```
reports/context/visual-fidelity-mandate.md
reports/audit/visual-fidelity-gaps.md
reports/architecture/draft/*-visual/
reports/workitems/epic-11-*/
reports/plan/wave-spec.visual.yaml
reports/plan/slices/e11.yaml
reports/plan/slices/INDEX-visual.md
reports/plan/slices/MANIFEST-visual.yaml
```

`INDEX.md` / `BACKLOG.md`: read full file, append, write complete file back.
`MANIFEST.yaml`: append an `e11` row only (`previous: e07`). Do not change
e08's row.

## Bash allowed

```
python scripts/cut_visual_slices.py
```
