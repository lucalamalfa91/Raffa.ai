# HITL — visual fidelity gaps (e11)

Operator: review `reports/audit/visual-fidelity-gaps.md`.

- OPEN rows → e11 live tasks.
- DEFERRED rows (missing screens) stay out of e11. They ship in e08; restyle later if needed.
- WAVE_COVERED rows must not be re-implemented (do not regress the 40rem fix).

Stamp only after that review:

```
reports/plan/gates/visual-gaps.hitl-ok
```

Then, with Studio idle and **not** in parallel with e08:

```
cd .helix
python scripts/check_slice_prereqs.py --slice e11
./run.ps1 -Max -Slice e11 -o execution-fanout
```

`e11` `previous` is `e07` (HITL already stamped 2026-09-07). Do not wait for e08.
