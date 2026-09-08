# Ask-copilot artifact check

First operator command:

```
./run-ask.ps1 -Check
```

Expected: OK `docs-intake-ask` … `contigo-ask-design` (6 orchestrations).
Prompt files present. Protected snapshot: e01–e11, e1011, five prior wave-specs,
locked ADRs, epic-01…11. Writable: ADR-001/004/011/018/020/023.

Until `-Check` prints `OK`, treat the YAML as authored-to-contract, not
runtime-validated.

On disk after Passata 1 authoring:

- `inputs/ask-copilot-brief.md`
- `reports/context/ask-copilot-mandate.md`
- `reports/audit/ask-copilot-gaps.md`
- `reports/architecture/ADR-023-ask-savings-copilot.md`
- amendment footers on ADR-001/004/011/018/020
- `reports/workitems/epic-12-ask-copilot/`
- `reports/plan/wave-spec.ask.yaml`
- `reports/plan/slices/e12.yaml`
- `MANIFEST.yaml` row `e12` (`previous: e1011`)

Passata 2 is **blocked** until `reports/plan/gates/ask-copilot.hitl-ok` exists
**and** `e1011.hitl-ok` is stamped. Do not run in parallel with e1011. Then:

```
./run.ps1 -Max -Slice e12 -o execution-fanout
```
