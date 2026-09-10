# Visual-fidelity artifact check

Helix backend was not on the authoring machine. First operator command:

```
./run-visual.ps1 -Check
```

Expected: OK `docs-intake-visual` … `raffa-visual-design` (6 orchestrations).
Prompt files present. Protected snapshot: e01–e10, four prior wave-specs,
ADR-001…022, epic-01…10.

Until `-Check` prints `OK`, treat the YAML as authored-to-contract, not
runtime-validated.

On disk after Passata 1 authoring:

- `reports/context/visual-fidelity-mandate.md`
- `reports/audit/visual-fidelity-gaps.md`
- `reports/workitems/epic-11-visual-fidelity/`
- `reports/plan/wave-spec.visual.yaml`
- `reports/plan/slices/e11.yaml`
- `MANIFEST.yaml` row `e11` (`previous: e07`)

Passata 2 is **blocked** until `reports/plan/gates/visual-gaps.hitl-ok` exists.
Do not run in parallel with e08. Then, from Studio or:

```
./run.ps1 -Max -Slice e11 -o execution-fanout
```
