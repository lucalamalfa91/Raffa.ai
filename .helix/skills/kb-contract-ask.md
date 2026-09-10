# KB contract — Ask Raffa V2 delta (epic-13 / e13)

Append-only. Starts at **epic-13 / e13**. epic-12 / e12 are **superseded**
(HITL 2026-09-08, `inputs/requirements.md` §0 D4) and stay on disk with a
superseded banner.

## Oracles (read, never write)

```
inputs/requirements.md                                    # binding requirements, HITL D1–D8
inputs/design/prototypes/Raffa V2 Prototype.html        # bundled design export (pixel reference)
inputs/design/prototypes/raffa-v2/app.jsx               # unpacked logic (Ask intents, renderVals)
inputs/design/prototypes/raffa-v2/markup.html           # unpacked screens + bindings
inputs/design/prototypes/raffa-v2/styles.css            # unpacked CSS
inputs/design/prototypes/raffa-v2/ia-v2.md              # V2 IA, routes, cross-links, divergences
inputs/design/prototypes/raffa-v2/screens-v2.md         # V2 screens, states, verbatim copy
inputs/design/prototypes/design-system.md                 # tokens (ADR-019)
```

## Never write

```
reports/plan/wave-spec.execution.yaml
reports/plan/wave-spec.web.yaml
reports/plan/wave-spec.schema.yaml
reports/plan/wave-spec.readiness.yaml
reports/plan/wave-spec.visual.yaml
reports/plan/slices/e01.yaml … e11.yaml
reports/plan/slices/e1011.yaml
reports/plan/slices/e12.yaml            # superseded, keep as is
reports/plan/slice.current.yaml         # the operator / launcher copies e13 here
reports/architecture/ADR-002*.md
reports/architecture/ADR-003*.md
reports/architecture/ADR-005*.md … ADR-010*.md
reports/architecture/ADR-012*.md … ADR-017*.md
reports/architecture/ADR-019*.md
reports/architecture/ADR-021*.md
reports/architecture/ADR-022*.md
reports/architecture/ADR-023*.md        # superseded footer already applied
reports/workitems/epic-01-*/ … epic-12-*/   # epic-12 carries its superseded banner
inputs/ask-copilot-brief.md             # superseded banner already applied
```

## May write (amend existing ADRs; do not replace the original decision)

```
reports/architecture/ADR-001-scope-r0-r4.md                 # amendment footer only
reports/architecture/ADR-004-foundry-models.md              # amendment footer only
reports/architecture/ADR-011-secrets-and-rag.md             # amendment footer only
reports/architecture/ADR-018-web-information-architecture.md # amendment footer only
reports/architecture/ADR-020-web-screen-inventory.md        # amendment footer only
reports/architecture/ADR-024-ask-raffa-v2.md              # new (supersedes ADR-023)
reports/context/ask-v2-mandate.md
reports/audit/ask-v2-gaps.md
reports/audit/ask-v2-hitl.md
reports/architecture/draft/*-ask/
reports/workitems/epic-13-ask-v2/
reports/plan/wave-spec.ask.yaml
reports/plan/slices/e13.yaml
reports/plan/slices/INDEX-ask.md
reports/plan/slices/MANIFEST-ask.yaml
```

`INDEX.md` / `BACKLOG.md`: read full file, append, write complete file back.
`MANIFEST.yaml`: upsert an `e13` row only (`previous: e1011`). Do not change
e01–e1011 rows; the `e12` row keeps its `superseded_by: e13` mark.

## Bash allowed

```
python scripts/cut_ask_slices.py
```
