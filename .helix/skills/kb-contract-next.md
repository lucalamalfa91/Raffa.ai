# KB contract — next-wave process (delta on the existing product)

The next-wave process (`contigo-next-process.yaml`, launcher `run-next.ps1`)
does **not** start from scratch. It starts from the code on the current
checkout (normally `origin/main`), the accepted ADRs, and the full backlog,
and it adds **one wave at a time**. Every agent runs as Claude Code with
cwd = `.helix` (this artifact folder). Product code is one level up:
`../backend`, `../web`, `../infra`, `../.github`, `../docs`, `../scripts`.
Read it, never edit it. Passata 1 writes **no application code**.

## Run parameters (who is this wave)

`reports/plan/next-run.json` is written by `run-next.ps1` before the run:

```json
{"wave": "w14", "todo": "inputs/next/next-waves-todo.md",
 "max_tasks": 20, "max_phases": 5, "focus": "", "previous": "e13",
 "started_at": "2026-09-10T16:00:00Z"}
```

Resolution order when an agent needs a parameter:

1. `reports/plan/next-run.json` if it exists.
2. The workflow input string (`wave=w14 todo=inputs/next/<file>.md
   max_tasks=20 max_phases=5 focus=...`), which Studio runs pass by hand.
3. Defaults: `wave` = `python scripts/register_wave.py --next-id`; `todo` =
   the newest `inputs/next/*.md` in name order (`README.md` excluded);
   `max_tasks` 20; `max_phases` 5; `focus` empty; `previous` = last row of
   `reports/plan/slices/MANIFEST.yaml`.

Later agents read the wave id from `reports/context/waves/*-requirements.md`
(the newest one) when the parameters above are unavailable.

## Oracles (read, never write)

```
inputs/product-spec.md                 # the initial product requirements (unchanged)
inputs/requirements.md                 # Ask V2 requirements (HITL 2026-09-08)
inputs/percorso-pilota-v1.md           # pilot path and demo script
inputs/engineering-brief.md            # locked vs council-owned table
inputs/engineering-constraints.md
inputs/next/<file>.md                  # THE raw requirements of this round (todo, feedback, new doc)
inputs/design/**                       # design exports the raw file points to (Claude Design)
../backend ../web ../infra ../.github ../docs ../scripts   # what exists today
reports/execution/wave-close*.md       # what the last wave delivered, salvage/* tags
reports/architecture/INDEX.md, ADR-*.md
reports/workitems/BACKLOG.md, epic-*/**
reports/plan/slices/MANIFEST.yaml      # wave chain (previous / hitl)
reports/open-questions.md              # assumptions in force
```

## Written by this process, per wave `<w>`

| Path | Writer | Readers |
|---|---|---|
| `reports/context/waves/<w>-requirements.md` | next-intake | every later agent |
| `reports/architecture/waves/<w>.md` (skeleton → completed) | next-intake (skeleton), involved seats (rows), gate (verifies) | decomposer, checker |
| `reports/architecture/draft/next/<seat>/<w>.md` | that seat's lane | the table |
| `reports/architecture/ADR-NNN-*.md` (new) / amendment footers | involved seats at the table | decomposer |
| `reports/architecture/INDEX.md` (appended rows) | involved seats | decomposer, checker |
| `reports/workitems/epic-NN-*/**` (new epics only) | next-decomposer, next-remediator | checker, Passata 2 |
| `reports/workitems/BACKLOG.md` (appended rows / wave section / status banners) | next-decomposer, next-remediator | checker, next intake |
| `reports/plan/slices/<w>.yaml` | next-decomposer, next-remediator | `register_wave.py`, checker, `run.ps1 -Slice <w>` |
| `reports/plan/slices/MANIFEST.yaml` (own row upsert) | `scripts/register_wave.py` | `check_slice_prereqs.py` |
| `reports/plan/slices/INDEX-next.md` | `scripts/register_wave.py` | operator |
| `reports/audit/<w>-hitl.md` | next-decomposer, next-remediator | operator |
| `reports/open-questions.md` (append) | any producer that must assume | every later phase |

## Never write

```
inputs/**                                   # raw inputs are the human's; the process only reads them
reports/plan/wave-spec.*.yaml               # historical masters
reports/plan/slices/<any existing id>.yaml  # e01…e13 and earlier w-waves
reports/plan/slice.current.yaml             # run.ps1 -Slice copies the wave there
reports/context/product-context.md, locked-decisions.md, council-open-questions.md, *-mandate.md
reports/audit/<earlier wave>-*.md
reports/architecture/draft/<seat>/**        # drafts of the initial council and of earlier deltas
reports/workitems/epic-*/**                 # existing epics: only a status banner (see below)
```

## Append-only (the protect script checks it)

- `reports/architecture/ADR-*.md`: the body is never rewritten. New content
  goes under `## Amendment (<YYYY-MM-DD>, wave <w>)` at the end. Superseding
  an ADR = status line `superseded by ADR-NNN` + a `## Superseded (...)`
  footer; the body stays.
- `reports/architecture/INDEX.md`, `reports/workitems/BACKLOG.md`,
  `reports/open-questions.md`: read the whole file, append, write the whole
  file back.
- Existing work-item files: unchanged, except the frontmatter `status:` line
  and an appended `## Superseded (<date>, wave <w>)` banner when a new
  requirement cancels that item.
- `reports/plan/slices/MANIFEST.yaml`: only the row of this wave is upserted
  (by `register_wave.py`); existing rows stay byte-identical.

`run-next.ps1` snapshots these files before the run and verifies after it
(`scripts/assert_next_plan_untouched.py snapshot|verify`). A rewritten locked
file fails the run — and the checker runs `verify` too.

## I/O chain (must close)

```
inputs/next/<file>.md + inputs/design/** + ../<code> + ADRs + BACKLOG + MANIFEST
  -> reports/context/waves/<w>-requirements.md  (+ reports/architecture/waves/<w>.md skeleton)
  -> reports/architecture/draft/next/<seat>/<w>.md            (involved seats only)
  -> reports/architecture/ADR-*.md (new / footers) + INDEX.md + reports/architecture/waves/<w>.md (complete)
  -> reports/workitems/epic-NN-*/** + BACKLOG.md
  -> reports/plan/slices/<w>.yaml + MANIFEST.yaml + INDEX-next.md + reports/audit/<w>-hitl.md
  -> operator: reports/plan/gates/<w>.hitl-ok, then ./run.ps1 -Max -Slice <w> -o execution-fanout
```

Output path of N = input path of N+1. An artefact nothing reads, or a read
with no writer, is a broken chain. Prove delivery on disk: a later phase
reads **files**, not your reasoning.

## Bash allowed (Claude Code `Bash`, read-only on the product tree)

```
git -C .. rev-parse --short HEAD ; git -C .. branch --show-current
git -C .. log --oneline -40 ; git -C .. log --since=<date> --oneline -- <path>
git -C .. diff --stat <sha>..HEAD -- <path> ; git -C .. show --stat <tag>
python scripts/register_wave.py --next-id | --wave <w> [--check]
python scripts/check_single_writer.py --slice <w>
python scripts/assert_next_plan_untouched.py verify
```

Never `git add`, `commit`, `checkout`, `push`, `worktree`, or edit anything
under `..`.
