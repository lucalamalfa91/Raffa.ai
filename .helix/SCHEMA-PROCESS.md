# Contigo schema-apply process (epic-09 / e09)

Separate Helix artifact from the live R0–R4 process and from the web-delta.

| File | Role |
|---|---|
| `contigo-process.yaml` + `./run.ps1` | **Do not touch while e05 is running.** |
| `contigo-web-process.yaml` + `./run-web.ps1` | Web delta. Do not mix. |
| `contigo-schema-process.yaml` + `./run-schema.ps1` | Schema-apply Passata 1 only. |

## Launch Passata 1 (safe while e05 runs)

```powershell
cd .helix
./run-schema.ps1 -Check
./run-schema.ps1
```

`--fresh` and `-Slice` are refused. Hash-lock: e01–e05, `wave-spec.execution.yaml`,
ADR-001…020, epic-01…08. Never writes `slice.current.yaml`.

## Passata 2 (after e05 HITL)

```powershell
python scripts/check_slice_prereqs.py --record-hitl e05
python scripts/check_slice_prereqs.py --slice e09
./run.ps1 -Max -Slice e09 -o execution-fanout
```

Or launch `e09` from Studio on the **live** artifact, not this YAML.
