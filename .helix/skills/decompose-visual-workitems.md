# Decomposition — visual fidelity (epic-11 / e11)

Ids start at **E11/F01/US01/T01**. All `layer: web`, `target_repo: contigo-web`.

Oracle: `inputs/design/prototypes/day1-demo.html`. Live SWA is evidence, not
a second design.

## Only visual deltas on screens that already exist

Do **not** add tasks that build Home, Ask chat, Members, Renewals, or Quote
check (e08 + recoveries). Do **not** re-remove `max-width: 40rem` (WAVE_COVERED).

Typical e11 stories (drop any the gap report marked WAVE_COVERED):

- hide `/health` probe from the canvas (keep the call + test id)
- sign-in 1:1 with the SIGN-IN block in `day1-demo.html`
- shell + global Ask bar vs export
- Documents / Portfolio / Contract 360 / Review CSS vs export
- regression tests that lock prototype measurements

Single-writer per file per phase. Sign-in files ≠ shell files ≠
`styles/components.css` owner.

After `wave-spec.visual.yaml`:

```
python scripts/cut_visual_slices.py
```

## Checker fail

- missing `visual-fidelity-gaps.md` or `e11.yaml`
- a write to e01–e10 / `slice.current.yaml`
- a task that implements a `ScaffoldScreen` route
