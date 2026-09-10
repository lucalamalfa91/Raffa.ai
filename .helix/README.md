# Raffa — Helix design then execution process

Passata 1 designs from inputs: **all ADRs**, then **R0–R4 decomposition**,
then slices. **No application code** until you launch passata 2 by hand.
The engine lives in `helix/src/backend`; this folder is the artifact.

Phase-by-phase mapping: **[PROCESS.md](PROCESS.md)**. Catalogue: **[config.md](config.md)**.

---

## Quick start

```bash
cd raffa-flow/.helix
cp .env.example .env
./run.ps1 --check

# Pre-flight (fail-closed): operator prerequisites + the same-phase
# single-writer check of the slice's task files.
python scripts/check_slice_prereqs.py --slice r0-a

# Passata 2 — one slice wave (not the 103-task YAML).
# Worktrees of the local clone. Green wave → on_orchestration_stop
# opens a GitHub PR integration → origin/main. A task counts as delivered
# only with committed work on wave/<task> (require_delivery); the
# wave-close report audits every task and lists salvage/* tags.
./run.ps1 -Max -Slice r0-a -o execution-fanout
# list: reports/plan/slices/INDEX.md

# Re-analysis ONLY if inputs or ADRs change. Wipes design outputs, then:
# docs → every council ADR → five epics (not R0-only) → cut slices. STOPS.
./run.ps1 --fresh -o raffa-design -i "Raffa V1: full scope from current inputs"
```

`raffa-plan-close` is still `default: true` in the YAML (Studio Run without
`-o`). Do **not** use that for coding. Coding is always `-Slice` +
`execution-fanout`. The launcher inits the local clone as a git toplevel
(worktrees). The PR is the fan-out `on_orchestration_stop` hook, not a
launcher step.

Never Resume a session that failed with DeepSeek 400 on `role: tool`.

---

## What to review after passata 1

| File | Why |
|---|---|
| `reports/architecture/INDEX.md` + `ADR-*.md` | council decisions |
| `reports/plan/wave-spec.execution.yaml` | full DAG (checker only) |
| `reports/plan/slices/INDEX.md` | what passata 2 actually launches |
| `reports/open-questions.md` | assumptions in force |

---

## Why two commands, not one

1. **A `fan_out` cannot nest inside a workflow.** Execution is a separate target.
2. **`governance.hitl` is inert.** The checkpoint is you launching passata 2,
   then the GitHub PR opened on `on_orchestration_stop`.
3. **The master wave-spec mixes R1–R4.** Passata 2 runs one **slice**
   file (`slice.current.yaml`), copied by `-Slice`.

---
