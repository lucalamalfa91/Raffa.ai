# Ask V2 slice (e13)

Produced by `python scripts/cut_ask_slices.py` from `reports/plan/wave-spec.ask.yaml`.
Supersedes e12 (never launched). Design oracle:
`inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked under
`inputs/design/prototypes/raffa-v2/`). Requirements: `inputs/requirements.md`.

Launch only after `reports/plan/gates/ask-v2.hitl-ok` exists and no other
wave is running. From Helix Studio: open `raffa-process.yaml`, make sure
`reports/plan/slice.current.yaml` is this slice, run `execution-fanout`.
Or from PowerShell:

```
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
```

## Files

- `e13.yaml`
