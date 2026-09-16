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
| `w14` | 11 | 15.8M | e13 | E14, E15 | W14 — workspace is real: membership, discovery, roster, role, profile, invitation lifecycle | inputs/next/next-waves-todo.md |
| `w15` | 11 | 16.1M | w14 | E16, E17, E18 | W15 — upload feels instant, and inviting a colleague works end to end | inputs/next/w15-todo.md |
| `w16` | 13 | 16.0M | w15 | E18, E19 | W16 — nothing the product knows lives only in a browser tab | inputs/next/w16-todo.md |
| `w17` | 14 | 20.4M | w16 | E20, E21, E22 | W17 — the product officializes what it knows, shows the page it read it from, and answers where you can save | inputs/next/w17-todo.md |
| `w18` | 20 | 19.2M | w17 | E23, E24, E25, E26 | W18 — the viewer draws the box, the cited phrase is editable, and the lists/Ask/Quote surfaces stop dead-ending | inputs/next/w18-todo.md |
