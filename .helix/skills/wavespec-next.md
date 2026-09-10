# wave-spec — next-wave (`reports/plan/slices/<w>.yaml`)

The next-wave process writes **one wave file per run**, directly in the
slice grammar the fan-out walks (no master wave-spec, no cutter). It is the
file `./run.ps1 -Max -Slice <w> -o execution-fanout` copies onto
`reports/plan/slice.current.yaml`. Never write `slice.current.yaml`, never
touch `wave-spec.*.yaml` or another `slices/<id>.yaml`.

## Grammar (Helix `WaveSpecDef`, `extra="forbid"` on every level)

```yaml
waveId: wave-next-w14
status: planned
# title: W14 — workspace membership, API JWT, no session as source of truth
# source: inputs/next/next-waves-todo.md
# requirements: reports/context/waves/w14-requirements.md
# Launch: ./run.ps1 -Max -Slice w14 -o execution-fanout
phases:
  - id: 1                      # integer, contiguous from 1
    name: phase-1
    tasks:
      - {id: E14/F01/US01/T01, prompt: reports/workitems/epic-14-<slug>/feature-01-<slug>/us-01-<slug>/tasks/task-01-<slug>.md, produces: [workspaces-list-api], depends_on: [], effort: M, layer: backend, status: live}
  - id: 2
    name: phase-2
    tasks:
      - {id: E14/F02/US01/T01, prompt: reports/workitems/…/tasks/task-01-<slug>.md, produces: [web-workspace-picker], depends_on: [workspaces-list-api], effort: M, layer: frontend, status: live}
forks: []
```

- One task per line, flow-mapping style, exactly the keys above.
- `produces`: kebab-case **artifact names**, unique across the wave, never
  file paths. `depends_on`: names produced in a **strictly earlier** phase
  of this wave. A dependency on something already on `main` is not listed
  (it exists; say so in the task's Context instead).
- `effort`: `S | M | L` (mapped to tokens by `register_wave.py` for the
  MANIFEST estimate; not a packing cap). `layer`: `backend | frontend`
  (infra and CI count as `backend`; web as `frontend`).
- `status`: `live` only in the wave file. Queued tasks are **not** in the
  file (their task md carries `status: queued`).
- `forks: []` always.
- The `# title:` comment is the MANIFEST title; keep it on one line.

## Loader rules (fail-closed in `register_wave.py` and in Helix)

1. Acyclic. 2. Phase consistency (strictly earlier). 3. Producer completeness
(every `depends_on` produced by exactly one task of the wave). 4. No fork.
5. Live-set closure. Plus the process caps: `max_tasks` live tasks,
`max_phases` phases (`reports/plan/next-run.json`, defaults 20 / 5), every
`prompt` on disk, ids `E\d+/F\d+/US\d+/T\d+`.

## Registration

```
python scripts/register_wave.py --wave w14            # validate + MANIFEST upsert + INDEX-next.md
python scripts/register_wave.py --wave w14 --check    # validate only (checker)
python scripts/register_wave.py --next-id             # prints the next free wave id
```

The MANIFEST row (`reports/plan/slices/MANIFEST.yaml`, JSON) gets `id`,
`title`, `previous` (the last registered wave), `epic` (the epics in the
wave), `checks: [github_auth, github_org, github_repos, hitl_previous]`,
`stories`, `tasks`, `tokens`, `source`, `requirements`. Existing rows are
never changed. `check_slice_prereqs.py --slice w14` reads that row at
launch and requires `reports/plan/gates/<previous>.hitl-ok`.
