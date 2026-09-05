# HITL — demo-readiness gap report

Operator review **before** any `e10` fan-out. This process does not launch
execution.

## Read

1. `reports/audit/demo-readiness-gaps.md` — matrix and classes
2. `reports/architecture/ADR-022-day1-demo-auth-and-fixture.md`
3. `reports/workitems/epic-10-demo-readiness/epic-10-demo-readiness.md`

## Decide

For each `E10` row, mark **accept as residual** or **promote to BLOCKER**.

Default (ADR-022): `X-Tenant-Id` on the API is **acceptable** for the first
stakeholder `demo`. If you disagree, do **not** stamp below; add an Entra-
on-API story outside this slice.

## Stamp (only after review)

From `.helix`:

```powershell
New-Item -ItemType File -Force reports/plan/gates/readiness-gaps.hitl-ok
```

Then, only when e05 is idle **and** e09 + e06–e08 are closed:

```powershell
python scripts/check_slice_prereqs.py --slice e10
./run.ps1 -Max -Slice e10 -o execution-fanout
```

Or launch `e10` from Helix Studio on the **live** artifact.

Do **not** run `./run-readiness.ps1 -Slice`. Do **not** use `--fresh`.
