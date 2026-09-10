# wave-spec — Ask Raffa V2 (`wave-spec.ask.yaml`)

Same grammar as `skills/wavespec-schema.md`. This master is **E13 only**
(the previous E12 content is superseded and no longer in this file).

Never write `wave-spec.execution.yaml`, `wave-spec.web.yaml`,
`wave-spec.schema.yaml`, `wave-spec.readiness.yaml`,
`wave-spec.visual.yaml`, `slices/e12.yaml`, or `slice.current.yaml`.

Phases follow `skills/decompose-ask-workitems.md` (five phases, twenty
tasks). `depends_on` names artifacts produced in a strictly earlier phase.
`layer` is `backend` or `web` (the cutter maps `web` → `frontend`).

After the master exists:

```
python scripts/cut_ask_slices.py
```

The cutter writes `slices/e13.yaml` (waveId `wave-v1-epic-e13`), upserts
the `e13` row in `slices/MANIFEST.yaml` (`previous: e1011`), marks the `e12`
row `superseded_by: e13`, and refreshes `INDEX-ask.md` / `MANIFEST-ask.yaml`.
It never touches `slice.current.yaml`; the operator or `run.ps1 -Slice e13`
copies the slice there for Studio / fan-out.
