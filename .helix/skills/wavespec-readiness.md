# wave-spec — demo-readiness (`wave-spec.readiness.yaml`)

Same grammar as `skills/wavespec-schema.md`. This master is **E10 only**.

Never write `wave-spec.execution.yaml`, `wave-spec.web.yaml`,
`wave-spec.schema.yaml`, or `slice.current.yaml`.

After the master exists:

```
python scripts/cut_readiness_slices.py
```
