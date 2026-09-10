# Contigo — Helix processes

Two artifacts, one engine (`helix/src/backend`; this folder is the artifact).

| Artifact | When | Doc |
|---|---|---|
| `contigo-process.yaml` + `run.ps1` / Helix Studio | The **initial** design (docs → all ADRs → R0–R4 epics → slices) and the **live Passata 2** (`execution-fanout`) every wave runs on. | [PROCESS.md](PROCESS.md), [config.md](config.md) |
| `contigo-next-process.yaml` + `run-next.ps1` | Every **later wave**: a raw requirements file (todo, demo feedback, bug list, new design) → normalized requirements → dynamic council (only the seats each item needs) → ADR footers / new ADRs → new epics appended to the backlog → **one wave** ready to launch. | [NEXT-PROCESS.md](NEXT-PROCESS.md) |

The five delta processes used between waves 6 and 13 (web, schema,
readiness, visual, ask) were retired on 2026-09-10; their outputs (ADR-018…024,
epic-06…13, slices e06…e13) stay under `reports/` and are the baseline the
next-wave process builds on.

---

## Quick start — a new round of feedback

```powershell
cd .helix
cp .env.example .env            # once; Claude Code Opus id + Max login
./run-next.ps1 -Check           # artifact parses, refs resolve, prompt files exist

# 1. drop the raw requirements: inputs/next/YYYY-MM-DD-<topic>.md  (see inputs/next/README.md)
# 2. Passata 1 → wave w<N>
./run-next.ps1 -Max -Todo inputs/next/next-waves-todo.md

# 3. review reports/context/waves/w14-requirements.md, reports/architecture/waves/w14.md,
#    reports/audit/w14-hitl.md, reports/plan/slices/w14.yaml — edit, re-run partially if needed
./run-next.ps1 -Max -Wave w14 -o next-from-council   # after editing the normalized file
./run-next.ps1 -Max -Wave w14 -o next-plan-close     # after editing tasks

# 4. the wave (Passata 2, contigo-process.yaml)
./run-next.ps1 -LaunchOnly -Wave w14                 # = check_slice_prereqs + ./run.ps1 -Max -Slice w14 -o execution-fanout
```

`-Launch` chains steps 2 and 4 in one command. Studio green ≠ PR opened:
read `reports/execution/wave-close.md`. From Helix Studio, leave the working
directory unset or pick `.helix` itself (NEXT-PROCESS.md D-N11), and merge the
reviewed plan to `main` before launching the wave.

## Quick start — the initial process (kept as is)

```powershell
./run.ps1 --check
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
./run.ps1 --fresh -o contigo-design -i "Contigo V1: full scope from current inputs"   # full re-analysis only
```

---

## What to review after a next-wave run

| File | Why |
|---|---|
| `reports/context/waves/<w>-requirements.md` | every item: status today (evidence), seats, acceptance, selection |
| `reports/architecture/waves/<w>.md` + `INDEX.md` + ADR footers | what the council decided, per item |
| `reports/audit/<w>-hitl.md` | phases, single-writer table, queued tasks, superseded items, launch |
| `reports/plan/slices/<w>.yaml` | the wave the fan-out will walk |
| `reports/plan/slices/INDEX-next.md` | all next-wave slices, previous chain |

## Rules that never change

- Passata 1 writes no application code and never commits; the operator
  commits the reviewed plan.
- The backlog is append-only; a new requirement cancels an item only with
  an explicit "cancels / replaces" and a superseded banner.
- Never commit on `integration` while a wave runs; never resume a poisoned
  session; restart the Studio backend after `.env` changes.
- Passata 1 and 2 bill the Claude Code **Max** login (`-Max`), not the
  Console API.
