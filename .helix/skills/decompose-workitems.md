# Decomposition — Epic / Feature / User Story / Task

The **full INDEX of accepted ADRs** plus product context become a four-level
tree under `reports/workitems/`, plus `BACKLOG.md`,
`reports/plan/wave-spec.execution.yaml` (complete DAG), and
`reports/plan/slices/*.yaml` (one file per overnight execution).

## Tree on disk

```
reports/workitems/
  BACKLOG.md
  epic-01-<slug>/
    epic-01-<slug>.md
    feature-01-<slug>/
      feature-01-<slug>.md
      us-01-<slug>/
        us-01-<slug>.md
        tasks/
          task-01-<slug>.md
```

Rules the gate checks:

- Every folder holds a file with **the same name as the folder**.
- Task files live **only** under a story's `tasks/` subfolder.
- Ids are monotonic. A later run appends; it never renumbers what exists.
- Slugs are lowercase kebab-case.

## What each level is

| Level | It is | It is not |
|---|---|---|
| **Epic** | A business capability spanning several features | A technical layer ("the Terraform epic") |
| **Feature** | A coherent slice of an epic that could ship on its own | A grab-bag of leftovers |
| **User story** | INVEST-sized value for one user, with acceptance criteria | A technical chore with no user |
| **Task** | One coding session an implementer can finish | "Implement the feature" |

Templates: `templates/epic-template.md`, `templates/feature-template.md`,
`templates/us-template.md`, `templates/task-template.md`.

## Waves follow product §16 and ADR-016 (all of them)

After council-close, decompose **R0 through R4 in the same run**. The first
technical slice (org + four repos + Terraform + CI/CD + git-flow + deployable
API) is **epic-01's content**, not a stop condition.

Stopping after R0 while INDEX lists ADR-016 / R1–R4 jobs is a gap.

- Greenfield / `--fresh` + `contigo-design`: write epic-01…epic-05.
- epic-01 exists, epic-02..05 missing: append R1–R4; do not rewrite epic-01
  unless the checker named an R0 file.
- Target **demo** (+`dev`). No production.
- R3/R4: fixture benchmark adapter, no paid external market API.
- BACKLOG rows for epic-02..05 must not remain `planned (not decomposed)`.

## Nightly slices (passata 2 launch unit)

The master wave-spec is the **full DAG** (cost hub, checker, traceability).
Helix `execution-fanout` must **not** walk that file: from phase 12 it mixes
R1–R4.

After writing the master, run `python scripts/cut_nightly_slices.py`
(default Max 20x window 10 M, 80% fill → 8.0 M cap). Effort `S|M|L` on
tasks is mapped to tokens **in the cutter**, not in the wave-spec (Helix
`TaskNodeDef` forbids extra keys). Token totals land in `slices/MANIFEST.yaml`
and as `# tokens:` comments on each slice YAML.

```
./run.ps1 -Max -Slice r0-a -o execution-fanout
```

`depends_on` that point outside the slice are dropped in the slice file
(producer-completeness). Prior nights are assumed already in `workspace/`.

## Final integration story

The last story **of each wave** is `us-NN-final-integration`, with exactly
**one** task that depends on every leaf artifact of that wave. R4's
integration story is the customer-demo path on `demo` (spec §20). Absence of
any wave's integration story is a gap.

## Traceability — the gate enforces this

- Every in-scope V1 job and **every accepted ADR in INDEX** is covered by at
  least one story / task objective.
- Every story AC traces to at least one task.
- Each story has **2-5 tasks**. Fewer means the story is a task; more means split.
  Exception: the single-task final-integration story.
- Task objectives name real files, projects, SKUs, endpoints, and ADR ids.
- Every live master task sits in **exactly one** nightly slice.

## Single writer per file, per wave-spec phase

Two tasks in the same phase must not modify the same file. Chain `depends_on`.

**Creation counts as writing.** A file one task *creates* may not be listed,
required, mapped or referenced by any other task of the same phase — not even
"map `MapXEndpoints()` in `Program.cs`" while the file is being created by a
sibling. Wire a new endpoint file into `Program.cs` in the phase **after** the
one that creates it. e13 phase 3 had F02/T02 create
`MarketEndpointExtensions.cs` while F06/T01 mapped it: the second task stubbed
the file to compile, the barrier union-merged two whole files, CI broke.
`python scripts/check_single_writer.py --slice <id>` must exit 0 before a slice
is cut; `check_slice_prereqs.py` runs the same check at launch.

Do **not** put domain `README.md` paths (or root `README.md`) in a task's
`## Files to create or modify`. README updates are standing implementer
scope (`skills/readme-hygiene.md`); listing them would make every
same-phase task in that layer a collision. The reviewer still blocks if
public surface changed and the README did not.
