# Decomposition — next-wave (append new epics, cut ONE wave)

Oracles: `reports/context/waves/<w>-requirements.md` (items, seats, status,
acceptance, proposed epics, selection), `reports/architecture/waves/<w>.md`
(the decisions and ADR actions), the ADRs they name, the design refs, the
code (`../backend`, `../web`, `../infra`, `../.github`), `BACKLOG.md` and the
existing epics (to say what you extend). Templates: `templates/*.md`.

## Tree on disk (append-only)

```
reports/workitems/
  BACKLOG.md                       # appended: epic rows, a "Wave <w>" section, status banners
  epic-NN-<slug>/                  # NEW epics only (next free NN; never renumber)
    epic-NN-<slug>.md              # frontmatter: id, type: epic, wave: <w>, status: active, extends: [epic-07, …]
    feature-NN-<slug>/
      feature-NN-<slug>.md
      us-NN-<slug>/
        us-NN-<slug>.md
        tasks/task-NN-<slug>.md    # frontmatter status: live | queued
```

Rules the checker enforces:

- Every folder holds a file with the same name as the folder; task files
  live only under `tasks/`; ids monotonic; slugs kebab-case.
- **New epics per wave theme** (the `Proposed epics` table of the normalized
  file — one theme = one epic, e.g. `epic-14-workspace-membership`). A
  feature names the epic it extends (`extends: epic-07 F02`) in its
  frontmatter and in `## Slice`. Existing epic folders are **never edited**,
  except a status banner when a new requirement cancels an item (below).
- Task ids `E<NN>/F<NN>/US<NN>/T<NN>` with the new epic number. A story has
  1–5 tasks (delta process: the 2–5 rule is relaxed). Every task cites the
  item ids it closes (`Closes: W14-03, W14-07`) under `## Context`.
- Every task names **real** files (verified with `Glob`/`Grep` in `../`),
  real endpoints, real types, the ADRs in force, and the commands of its
  Definition of done with expected exit code 0. "A suitable X" is a gap.
- **Bug items** (`Kind: bug`): the task starts with a regression test that
  reproduces the defect (named file and test), then the fix; the DoD runs
  that test. A bug task without a named regression test is a gap.
- **Design items**: every web task cites the design oracle
  (`inputs/design/...`) **and** the anchor it implements (a markup string, a
  `.jsx` symbol, a `screens-*.md` / `ia-*.md` section, or the design system
  token). No anchor, no task.
- **Persistence rule** (from the todo, binding): if a human can create or
  change it, another browser / device / session must read it back from
  Postgres under RLS. A task that leaves `localStorage` / `sessionStorage`
  as the system of record for user data is a gap.
- **Acceptance**: every story's ACs come from the item's `Acceptance` column
  (the raw file's checks, e.g. N1…N18) reworded as observable checks on
  `dev`; the final-integration task copies them into
  `docs/waves/<w>-acceptance.md` for the operator.
- Do not put domain `README.md` paths in `## Files to create or modify`
  (README hygiene is standing implementer scope).

## Cap and queue — one wave per run

The wave takes at most `max_tasks` live tasks in at most `max_phases`
phases (defaults 20 / 5, `reports/plan/next-run.json`). Fill by priority:
`must` items first (and any `carry-over` items queued by an earlier run),
then `should`, then `could`; keep a story's tasks together; stop before the
cap. Everything else is still decomposed (the backlog must be complete) but
its task files carry `status: queued` and are **not** in the wave file;
list them under `## Queued for the next wave` in `BACKLOG.md`'s wave section
and in `reports/audit/<w>-hitl.md`. The next run's intake picks queued tasks
first.

## Phases and single writer per file, per phase

Phases follow dependencies, not layers: phase 1 = tasks with no dependency in
this wave. `depends_on` names artifacts produced in a **strictly earlier**
phase. **Two tasks in the same phase must not modify the same file, and a
file one task creates may not be named, mapped or required by another task
of the same phase** (e13 lesson: `MarketEndpointExtensions.cs` was created
by one task and mapped in `Program.cs` by a sibling; the barrier union-merged
two files and CI failed). Wire a new endpoint file one phase later than its
creation, or make the call site the creator's own deliverable.

Nominate **one writer per phase** for the files that state current state:
`backend/src/Raffa.Api/Program.cs`, `backend/Raffa.slnx`,
`DependencyDirectionTests.cs`, `web/openapi/raffa-api.v1.json`,
`web/src/api/client.ts` + `generated/schema.ts`, every
`*EndpointExtensions.cs`, `ServiceCollectionExtensions.cs` of a module,
`appsettings*.json`, `.github/workflows/*.yml`, `infra/**/main.tf` of a
module, `web/src/components/shell/navItems.ts`, `web/src/router*.tsx`.
Write the table into `reports/audit/<w>-hitl.md`. Then run
`python scripts/check_single_writer.py --slice <w>`; it must exit 0.

## Final integration task (always the last phase)

The last story of the wave is `us-NN-final-integration` with exactly one
task depending on every leaf artifact of the wave. It: builds and tests the
backend (`dotnet build backend/Raffa.slnx`, `dotnet test` per project),
type-checks, lints and tests the web (`npm run lint`, `npm run typecheck`,
`npm test`, the Playwright spec the wave added), runs the e2e cases that do
not need live Foundry, sweeps the READMEs whose public surface changed, and
writes `docs/waves/<w>-acceptance.md`: per item, the manual check on `dev`
(what to click, what to expect), taken from the ACs. This is what makes the
wave "immediately working": the PR is not green until this task passes.

## Superseding existing work

Only when the normalized file's §6 says a new requirement **cancels** an
existing epic/feature/story/task: set its frontmatter `status: superseded`
and append `## Superseded (<date>, wave <w>)` naming the new item. Update its
row in `BACKLOG.md`. Never delete files, never edit the rest of that file.
If the wave file `slices/<old>.yaml` still lists that task as `live`, say so
in the hitl doc (the operator decides; this process never edits old slices).

## BACKLOG.md

Read the whole file. Append: one row per new epic in the `## Epics` table
(`| epic-14 | workspace-membership | w14 | active — decomposed (next-wave) |`),
a `## Wave <w> (<date>)` section at the end with: source file, items in the
wave (id → task ids), queued tasks, superseded items, ADRs touched. Write the
whole file back. Never remove a row.

## Wave file and registration

Write `reports/plan/slices/<w>.yaml` in the grammar of
`skills/wavespec-next.md`, then:

```
python scripts/register_wave.py --wave <w>
python scripts/check_single_writer.py --slice <w>
```

Both must exit 0. `register_wave.py` validates the DAG and the caps, runs
`helix validate-wavespec` when the backend is reachable, upserts the
`MANIFEST.yaml` row (`previous` = the last wave), and refreshes
`slices/INDEX-next.md`.

## `reports/audit/<w>-hitl.md` (the operator's review page)

Sections: what to review (files), items in the wave with their tasks and
phases, the single-writer table, queued tasks, superseded items, ADR
actions, open questions / assumptions, and the launch:

```
python scripts/check_slice_prereqs.py --slice <w>
./run.ps1 -Max -Slice <w> -o execution-fanout        # or ./run-next.ps1 -Launch -Wave <w>
```

## Checker fail (summary)

- a file of the chain missing (requirements, decision record, epics, wave
  yaml, hitl doc, MANIFEST row);
- an item selected for the wave (status OPEN / PARTIAL, in §5 "In wave")
  with no task citing it; a decision with a code consequence and no task;
- a task without real files, DoD commands, tests; a bug task without a
  regression test; a design task without the design anchor;
- caps exceeded; `depends_on` not strictly earlier; a prompt path that does
  not exist; two same-phase tasks on one file; a created file named by a
  sibling of the same phase;
- `register_wave.py --wave <w> --check` or `check_single_writer.py` non-zero;
- an existing epic file rewritten beyond a status banner
  (`assert_next_plan_untouched.py verify` exit 1);
- no final-integration task, or it does not depend on every leaf artifact.
