# KB contract — Ask savings copilot delta

Append-only. Starts at **epic-12 / e12**.

## Never write

```
reports/plan/wave-spec.execution.yaml
reports/plan/wave-spec.web.yaml
reports/plan/wave-spec.schema.yaml
reports/plan/wave-spec.readiness.yaml
reports/plan/wave-spec.visual.yaml
reports/plan/slices/e01.yaml … e11.yaml
reports/plan/slices/e1011.yaml
reports/plan/slice.current.yaml
reports/architecture/ADR-002*.md
reports/architecture/ADR-003*.md
reports/architecture/ADR-005*.md … ADR-010*.md
reports/architecture/ADR-012*.md … ADR-017*.md
reports/architecture/ADR-019*.md
reports/architecture/ADR-021*.md
reports/architecture/ADR-022*.md
reports/workitems/epic-01-*/ … epic-11-*/
```

## May write (amend existing ADRs; do not replace the original decision)

```
reports/architecture/ADR-001-scope-r0-r4.md          # amendment footer only
reports/architecture/ADR-004-foundry-models.md       # amendment footer only
reports/architecture/ADR-011-secrets-and-rag.md      # amendment footer only
reports/architecture/ADR-018-web-information-architecture.md
reports/architecture/ADR-020-web-screen-inventory.md
reports/architecture/ADR-023-ask-savings-copilot.md  # new
reports/context/ask-copilot-mandate.md
reports/audit/ask-copilot-gaps.md
reports/audit/ask-copilot-hitl.md
reports/architecture/draft/*-ask/
reports/workitems/epic-12-*/
reports/plan/wave-spec.ask.yaml
reports/plan/slices/e12.yaml
reports/plan/slices/INDEX-ask.md
reports/plan/slices/MANIFEST-ask.yaml
```

`INDEX.md` / `BACKLOG.md`: read full file, append, write complete file back.
`MANIFEST.yaml`: upsert an `e12` row only (`previous: e1011`). Do not change
e01–e1011 rows.

## Bash allowed

```
python scripts/cut_ask_slices.py
```
