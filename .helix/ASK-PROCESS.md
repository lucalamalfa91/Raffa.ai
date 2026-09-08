# Contigo Ask copilot process (epic-12 / e12)

Separate Helix artifact from the live R0–R4 process, web-delta, schema-apply,
demo-readiness, and visual-fidelity.

| File | Role |
|---|---|
| `contigo-process.yaml` + `./run.ps1` | **Do not touch while a wave is running (e1011).** |
| `contigo-visual-process.yaml` + `./run-visual.ps1` | Mockup CSS. Do not mix. |
| `contigo-ask-process.yaml` + `./run-ask.ps1` | Ask savings-copilot Passata 1 only. |

Oracle: `inputs/ask-copilot-brief.md`.

This process **amends** existing ADRs (001, 004, 011, 018, 020) and **adds ADR-023**.
It does not rewrite epic-01…11 or `slice.current.yaml`.

## Launch Passata 1 (optional — outputs already authored)

```powershell
cd .helix
./run-ask.ps1 -Check
./run-ask.ps1
```

`--fresh` and `-Slice` are refused. Hash-lock: e01–e11, e1011, prior wave-specs,
ADR-002/003/005–010/012–017/019/021/022, epic-01…11. Never writes `slice.current.yaml`.

If the council stalls, keep the authored reports (mandate, gap matrix, ADR-023,
amendment footers, epic-12, `e12.yaml`) and still run `-Check`.

## HITL (required before any e12 fan-out)

Review `inputs/ask-copilot-brief.md`, `reports/audit/ask-copilot-gaps.md`,
`reports/architecture/ADR-023-ask-savings-copilot.md`, and the amendment
footers on ADR-001/004/011/018/020. Stamp:

```
reports/plan/gates/ask-copilot.hitl-ok
```

Do **not** launch e12 from this process YAML.

## Passata 2 (Studio idle, **not** parallel with e1011)

`e12.previous` is `e1011`. Wait for `reports/plan/gates/e1011.hitl-ok`.

```powershell
python scripts/check_slice_prereqs.py --slice e12
./run.ps1 -Max -Slice e12 -o execution-fanout
```

Or launch `e12` from Studio on the **live** artifact, not this YAML.
