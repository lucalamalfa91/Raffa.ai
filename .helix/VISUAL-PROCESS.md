# Contigo visual-fidelity process (epic-11 / e11)

Separate Helix artifact from the live R0–R4 process, web-delta, schema-apply,
and demo-readiness.

| File | Role |
|---|---|
| `contigo-process.yaml` + `./run.ps1` | **Do not touch while a wave is running.** |
| `contigo-web-process.yaml` + `./run-web.ps1` | Web delta. Do not mix. |
| `contigo-schema-process.yaml` + `./run-schema.ps1` | Schema-apply. Do not mix. |
| `contigo-readiness-process.yaml` + `./run-readiness.ps1` | Demo-readiness. Do not mix. |
| `contigo-visual-process.yaml` + `./run-visual.ps1` | Mockup vs `dev` CSS Passata 1 only. |

Oracle: [Claude artifact](https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58)
= `inputs/design/prototypes/day1-demo.html`.
Live evidence: https://mango-pond-061bc6d1e.6.azurestaticapps.net/

## Launch Passata 1 (optional — outputs already authored)

```powershell
cd .helix
./run-visual.ps1 -Check
./run-visual.ps1
```

`--fresh` and `-Slice` are refused. Hash-lock: e01–e10, all prior wave-specs,
ADR-001…022, epic-01…10. Never writes `slice.current.yaml`.

If the council stalls, keep the authored reports (mandate, gap matrix,
epic-11, `e11.yaml`) and still run `-Check`.

## HITL (required before any e11 fan-out)

Review `reports/audit/visual-fidelity-gaps.md` and
`reports/audit/visual-fidelity-hitl.md`. Stamp
`reports/plan/gates/visual-gaps.hitl-ok` only after that review.

Do **not** launch e11 from this process YAML.

## Passata 2 (Studio idle, **not** parallel with e08)

`e11.previous` is `e07` (already HITL-stamped). Launch **before** e08 so
new screens inherit the corrected chrome.

```powershell
python scripts/check_slice_prereqs.py --slice e11
./run.ps1 -Max -Slice e11 -o execution-fanout
```

Or launch `e11` from Studio on the **live** artifact, not this YAML.
