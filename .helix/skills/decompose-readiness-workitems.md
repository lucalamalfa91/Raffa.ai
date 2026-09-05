# Decomposition — demo-readiness (epic-10 / e10)

Ids start at **E10/F01/US01/T01**. Mix of `layer: backend` and infra/CI.
`target_repo` is `contigo-backend` or `contigo-infra` as appropriate.

## Only residuals

Do **not** add tasks that belong to e05 (quotes API), e09 (SQL scripts +
apply + Savings/Quotes conn strings), or e06–e08 (web screens).

Typical e10 stories (drop any the gap report marked WAVE_COVERED):

- seed fixture data on `contigo_demo` (and optionally `contigo_dev`)
- Foundry/OCR env on Container Apps if missing
- `demo-v*` / SWA config smoke

After `wave-spec.readiness.yaml`:

```
python scripts/cut_readiness_slices.py
```

## Checker fail

- missing `demo-readiness-gaps.md` or `e10.yaml`
- a write to e01–e09 / `slice.current.yaml`
- task that regenerates EF scripts or builds web screens
