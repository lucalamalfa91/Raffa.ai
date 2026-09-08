# Ask V2 artifact check

First operator command:

```
./run-ask.ps1 -Check
```

Expected: OK `docs-intake-ask` … `contigo-ask-design` (6 orchestrations).
Prompt files present. Model: one `claude-code` model `cc-opus` =
`${ANTHROPIC_DEFAULT_OPUS_MODEL}` (Claude Code Opus; `.env`); no DeepSeek
model in the allow-list. Run the council with `./run-ask.ps1 -Max`. Protected snapshot: e01–e11, e1011, e12, five prior
wave-specs, locked ADRs (002/003/005–010/012–017/019/021/022/023),
epic-01…12, `inputs/ask-copilot-brief.md`. Writable: ADR-001/004/011/018/020
footers, ADR-024, epic-13, `wave-spec.ask.yaml`, `slices/e13.yaml`.

Until `-Check` prints `OK`, treat the YAML as authored-to-contract, not
runtime-validated.

On disk after Passata 1 authoring (2026-09-08):

- `inputs/requirements.md` (oracle) + `inputs/design/prototypes/contigo-v2/`
  (unpacked design: `app.jsx`, `markup.html`, `styles.css`, `ia-v2.md`, `screens-v2.md`)
- `reports/context/ask-v2-mandate.md`
- `reports/audit/ask-v2-gaps.md`, `reports/audit/ask-v2-hitl.md`
- `reports/architecture/ADR-024-ask-contigo-v2.md`; ADR-023 superseded footer;
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

Passata 2 is **blocked** until `reports/plan/gates/ask-v2.hitl-ok` exists.
Then, from **Helix Studio** (`contigo-process.yaml` → `execution-fanout`) or:

```
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
```
