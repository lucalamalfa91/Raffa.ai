# Ask V2 artifact check

First operator command:

```
./run-ask.ps1 -Check
```

Expected: OK `docs-intake-ask` … `raffa-ask-design` (6 orchestrations).
Prompt files present. Model: one `claude-code` model `cc-opus` =
`${ANTHROPIC_DEFAULT_OPUS_MODEL}` (Claude Code Opus; `.env`); no DeepSeek
model in the allow-list. Run the council with `./run-ask.ps1 -Max`. Protected snapshot: e01–e11, e1011, e12, five prior
wave-specs, locked ADRs (002/003/005–010/012–017/019/021/022/023),
epic-01…12, `inputs/ask-copilot-brief.md`. Writable: ADR-001/004/011/018/020
footers, ADR-024, epic-13, `wave-spec.ask.yaml`, `slices/e13.yaml`.

Until `-Check` prints `OK`, treat the YAML as authored-to-contract, not
runtime-validated.

On disk after Passata 1 authoring (2026-09-08):

- `inputs/requirements.md` (oracle) + `inputs/design/prototypes/raffa-v2/`
  (unpacked design: `app.jsx`, `markup.html`, `styles.css`, `ia-v2.md`, `screens-v2.md`)
- `reports/context/ask-v2-mandate.md`
- `reports/audit/ask-v2-gaps.md`, `reports/audit/ask-v2-hitl.md`
- `reports/architecture/ADR-024-ask-raffa-v2.md`; ADR-023 superseded footer;
  epic-13 amendment footers on ADR-001/004/011/018/020; INDEX section
- `reports/workitems/epic-13-ask-v2/` (11 features, 11 stories, 20 tasks);
  BACKLOG rows (epic-12 superseded, epic-13 active)
- `reports/plan/wave-spec.ask.yaml` (E13 only)
- `reports/plan/slices/e13.yaml`, `INDEX-ask.md`, `MANIFEST-ask.yaml`;
  `MANIFEST.yaml` row `e13` (`previous: e1011`), row `e12` marked `superseded_by: e13`
- `reports/plan/slice.current.yaml` = `e13.yaml` (ready for Studio)
- `reports/open-questions.md` — OQ-askv2-001…009 (assumptions in force)

Wave-spec validity (fail-closed, five DAG checks):

```
<helix backend>\.venv\Scripts\helix.exe validate-wavespec reports\plan\slices\e13.yaml
```

On disk after Passata 2 (the `e13` wave, 2026-09-09):

The wave **has run**. Helix run `5dec6283-bd05-4779-bda4-b91b9f0408b0`,
`execution-fanout` on `raffa-process.yaml`, 2026-09-08 19:43:33 →
2026-09-09 09:30:19 UTC, five phases, 20 live tasks, `maxParallel: 3`.

- Studio finished green: `completed`, `failed_task_ids: []`,
  `skipped_task_ids: []`, `helix.fanout.wave_finished` `completedCount: 20`.
  **Green is the orchestration, not the product** — only **15 of the 20**
  tasks produced a commit.
- Five tasks finished with no commit: E13/F04/US01/T01, E13/F04/US01/T02,
  E13/F03/US01/T02, E13/F06/US01/T02, E13/F11/US01/T01. Three hit
  `CodingAgentTurnTimeout` (`HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS=3600` in
  `.env`); the other two closed with `HALTED:` because the phase-1 task they
  depend on had produced nothing. Their `wave/*` branches point at a barrier
  merge or an unrelated commit; part of the lost work survives as
  `salvage/E13-*` tags.
- Two phase-barrier union merges had to be repaired by hand on `integration`:
  `50b38a7` (`MarketEndpointExtensions.cs`, created by two phase-3 tasks — it
  broke the build on PR #67) and `1ca7888` (`backend/README.md`, three
  interleaved copies of the `## Solution` section).
- The five missing tasks are being finished **directly on `integration`**
  (PR #67), not by a second slice: E13/F04/US01/T01 landed that way as
  `1ca7888`, the other four are in progress and have no commit yet. `e13` is
  still the only slice; there is no `e13b`.
  `reports/plan/slice.current.yaml` is unchanged and still equals
  `reports/plan/slices/e13.yaml`; `status: planned` in every slice file is the
  cutter's fixed field, not a live run state.
- Full post-mortem with the task → commit table and the run-index evidence:
  `reports/execution/wave-close-e13.md`. Hook-written close record:
  `reports/execution/wave-close.md`.

The launch gate that actually ran was `hitl_previous` —
`check_slice_prereqs.py` looks for `reports/plan/gates/<previous>.hitl-ok`,
i.e. `e1011.hitl-ok` for `e13`; there is no `ask-v2.hitl-ok` check in that
script. For reference, the launch was:

```
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
```

or **Helix Studio** (`raffa-process.yaml` → `execution-fanout`).
