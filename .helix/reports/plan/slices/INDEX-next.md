# Next-wave slices

Produced by `python scripts/register_wave.py --wave <id>` from the wave the
next-wave process (`raffa-next-process.yaml`) cut. One wave per run; the
backlog keeps everything, the wave carries at most the cap.

Launch (after reviewing `reports/audit/<id>-hitl.md`):

```
python scripts/check_slice_prereqs.py --slice <id>
./run.ps1 -Max -Slice <id> -o execution-fanout      # or ./run-next.ps1 -LaunchOnly -Wave <id>
```

| Slice | Tasks | Tokens | Previous | Epics | Title | Source |
|-------|-------|--------|----------|-------|-------|--------|
