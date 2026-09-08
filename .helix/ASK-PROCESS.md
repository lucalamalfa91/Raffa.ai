# Contigo Ask V2 process (epic-13 / e13 — replaces epic-12 / e12)

Separate Helix artifact from the live R0–R4 process, web-delta, schema-apply,
demo-readiness, and visual-fidelity.

| File | Role |
|---|---|
| `contigo-process.yaml` + `./run.ps1` / **Helix Studio** | **Live artifact. Passata 2 (the wave) runs here.** |
| `contigo-visual-process.yaml` + `./run-visual.ps1` | Mockup CSS. Do not mix. |
| `contigo-ask-process.yaml` + `./run-ask.ps1` | Ask V2 Passata 1 only (ADRs + work items). |

Oracle: `inputs/requirements.md` (HITL decisions D1–D8, 2026-09-08).
Design oracle: `inputs/design/prototypes/Contigo V2 Prototype.html`, unpacked
and searchable under `inputs/design/prototypes/contigo-v2/` (`app.jsx`,
`markup.html`, `styles.css`, `ia-v2.md`, `screens-v2.md`). Every web task
cites both; the requirements win where the prototype differs (divergence
table in `ia-v2.md`).

This process **supersedes ADR-023 with ADR-024**, amends ADR-001/004/011/018/020
(footers), and writes **epic-13** + `e13.yaml`. epic-12, `e12.yaml` and
`inputs/ask-copilot-brief.md` stay on disk with a superseded banner and are
hash-locked. It never writes `slice.current.yaml`.

## Model — Claude Code Opus, not DeepSeek (operator decision 2026-09-08)

Every Passata 1 agent of this artifact (`docs-ingester-ask`, `ask-auditor`,
`ask-critic`, `backlog-decomposer-ask`, `decomposition-checker-ask`,
`decomposition-remediator-ask`) runs on **Claude Code Opus** through the
Pattern E harness (`harness.backend: external-coding-agent / claude-code`),
model id `${ANTHROPIC_DEFAULT_OPUS_MODEL}` from `.env`. No DeepSeek model
is declared or allow-listed. Consequences:

- agents use Claude Code's own `Read` / `Write` / `Edit` / `Grep` / `Glob`
  (`Bash` only for the decomposer / remediator, to run the cutter); critic
  and checker are read-only (`skills/cc-passata1-harness.md`);
- cwd is `.helix`; product code is read at `../backend`, `../web`;
- billing is the Claude Code **Max** login (PROCESS.md D11): run with `-Max`
  or keep `ANTHROPIC_API_KEY` unset; the launcher refuses a set key;
- the turn deadline is `.env` `HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS`
  (3600 s), so authoring many files in one turn no longer hits the 600 s
  default that motivated PROCESS.md D9.

The live `contigo-process.yaml` (Passata 2) keeps `coding-primary` =
`claude-sonnet-5` for implementer / reviewer / conflict-fixer; switching the
wave to Opus is one line (`models[coding-primary].model`) and a cost call
the operator makes at HITL.

## Passata 1 (outputs already authored at HITL — the run is verify-or-write)

```powershell
cd .helix
./run-ask.ps1 -Check
./run-ask.ps1 -Max
```

`--fresh` and `-Slice` are refused. Hash-lock: e01–e11, e1011, e12, prior
wave-specs, ADR-002/003/005–010/012–017/019/021/022/023, epic-01…12,
`inputs/ask-copilot-brief.md`. Every producer keeps an existing output that
carries the requirements and only fills gaps, so a run over the authored
tree is a validation pass, not a rewrite.

If the council stalls, keep the authored reports (mandate, gap matrix,
ADR-024, amendment footers, epic-13, `e13.yaml`) and still run `-Check`.

## HITL (required before the e13 wave)

Review `inputs/requirements.md`, `reports/audit/ask-v2-gaps.md`,
`reports/architecture/ADR-024-ask-contigo-v2.md`, the amendment footers on
ADR-001/004/011/018/020, `reports/workitems/epic-13-ask-v2/`, and
`reports/plan/slices/e13.yaml`. Then stamp the gate by hand (same format
as `gates/readiness-gaps.hitl-ok`; `--record-hitl` only knows slice ids):

```powershell
@"
slice: ask-v2
stamped_at: $(Get-Date -AsUTC -Format s)Z
reviewed: inputs/requirements.md, reports/audit/ask-v2-gaps.md, ADR-024, epic-13, slices/e13.yaml
decision: accept e13 (Ask Contigo V2) as the next wave; e12 superseded
blockers: none
"@ | Set-Content -Encoding ascii reports/plan/gates/ask-v2.hitl-ok
```

`e1011.hitl-ok` is already stamped; `e13.previous` is `e1011` (e12 is
skipped: never launched).

## Passata 2 — the wave, from Helix Studio

1. `reports/plan/slice.current.yaml` must be `slices/e13.yaml` (already
   copied by this authoring pass; `./run.ps1 -Slice e13` re-copies it).
2. Studio idle, no other wave running. Claude Code Max login active
   (`ANTHROPIC_API_KEY` unset — PROCESS.md D11).
3. In Helix Studio open **`.helix/contigo-process.yaml`** (the live artifact,
   not this YAML), select orchestration **`execution-fanout`**, Run.
   It walks `slice.current.yaml` (twenty E13 tasks, five phases, three
   worktrees in parallel), merges `wave/*` into `integration` at each
   barrier, and on stop opens the PR `integration → main` and writes
   `reports/execution/wave-close.md` (+ a `hitl` issue if open points).

PowerShell equivalent:

```powershell
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
```

Studio green ≠ PR opened; read `reports/execution/wave-close.md`.
