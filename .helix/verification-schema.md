# Schema-apply artifact check

```
./run-schema.ps1 -Check
```

OK `docs-intake-schema` … `contigo-schema-design` (12 orchestrations).
Prompt files present. Protected snapshot: 239 files (e01–e05, execution
wave-spec, ADR-001…020, epic-01…08).

On disk after Passata 1 authoring:

- `reports/context/schema-apply-mandate.md`
- `reports/architecture/ADR-021-schema-apply.md`
- `reports/workitems/epic-09-schema-apply/`
- `reports/plan/wave-spec.schema.yaml`
- `reports/plan/slices/e09.yaml`
- `MANIFEST.yaml` row `e09` (`previous: e05`)

Passata 2 is **blocked** until `reports/plan/gates/e05.hitl-ok` exists.
Then, from Studio or:

```
./run.ps1 -Max -Slice e09 -o execution-fanout
```
