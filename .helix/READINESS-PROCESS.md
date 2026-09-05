# Contigo demo-readiness process (epic-10 / e10)

Separate Helix artifact from the live R0–R4 process, the web-delta, and schema-apply.

| File | Role |
|---|---|
| `contigo-process.yaml` + `./run.ps1` | **Do not touch while e05 is running.** |
| `contigo-web-process.yaml` + `./run-web.ps1` | Web delta. Do not mix. |
| `contigo-schema-process.yaml` + `./run-schema.ps1` | Schema-apply. Do not mix. |
| `contigo-readiness-process.yaml` + `./run-readiness.ps1` | Demo-readiness Passata 1 only. |

## Launch Passata 1 (safe while e05 runs)

```powershell
cd .helix
./run-readiness.ps1 -Check
./run-readiness.ps1
```

`--fresh` and `-Slice` are refused. Hash-lock: e01–e05, `wave-spec.execution.yaml`,
ADR-001…021, epic-01…09. Never writes `slice.current.yaml`.

If the DeepSeek council stalls, author the reports the same way as ADR-021
(mandate, gap matrix, ADR-022, epic-10, `e10.yaml`) and still run `-Check`.

## HITL (required before any e10 fan-out)

Review `reports/audit/demo-readiness-gaps.md` and
`reports/audit/demo-readiness-hitl.md`. Classify each `BLOCKER` vs acceptable.
Stamp `reports/plan/gates/readiness-gaps.hitl-ok` only after that review.

Do **not** launch e10 from this process.

## Passata 2 (after e05 idle + e09 + e06–e08)

```powershell
python scripts/check_slice_prereqs.py --record-hitl e09
# also close e06–e08 on the live process before e10
python scripts/check_slice_prereqs.py --slice e10
./run.ps1 -Max -Slice e10 -o execution-fanout
```

Or launch `e10` from Studio on the **live** artifact, not this YAML.

Sequence: `e05 HITL → e09 → e06–e08 → e10 → demo-v*`.
