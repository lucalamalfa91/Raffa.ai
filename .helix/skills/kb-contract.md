# KB contract — canonical paths for Raffa

Native Helix file tools (`read_file`, `read_files`, `list_dir`, `glob`, `grep`,
`write_file`) resolve **relative to this artifact folder** (`.helix/`). Absolute
paths and `..` are refused. Passata 2 coding-agent cwd is the **per-task
worktree** (a copy of this folder) under `isolation: git-worktree` — see
PROCESS.md D1.

Prove delivery on disk. A later phase reads **files**, not your reasoning.
Default nested hand-off is `final_turn_only`: the last line of your turn must
carry what the next phase needs.

## Inputs (operator / copied ground truth)

```
inputs/product-spec.md                 # WHAT to build (do not re-negotiate)
inputs/engineering-brief.md            # HOW — locked vs council-owned
inputs/engineering-constraints.md      # short mandate
inputs/operator-r0-r4-demo.md          # leftover R0-only continuation; design ignores it
```

## Passata 1 — design artefacts

```
reports/context/product-context.md           # V1 jobs, non-goals, topology, R0-R4
reports/context/locked-decisions.md          # verbatim locked table from the brief
reports/context/council-open-questions.md    # "Council decides" list, unanswered

reports/architecture/draft/<seat>/*.md       # independent lane drafts (not final)
reports/architecture/ADR-NNN-<slug>.md       # accepted ADRs after council-close
reports/architecture/INDEX.md                # one-line index of accepted ADRs

reports/workitems/BACKLOG.md
reports/workitems/epic-NN-<slug>/
    epic-NN-<slug>.md
    feature-NN-<slug>/
        feature-NN-<slug>.md
        us-NN-<slug>/
            us-NN-<slug>.md
            tasks/task-NN-<slug>.md

reports/plan/wave-spec.execution.yaml        # full DAG (checker). MUST exist
reports/plan/slices/<id>.yaml                # one overnight wave-spec
reports/plan/slices/INDEX.md
reports/plan/slice.current.yaml              # fan_out.over; launcher copies a slice here
reports/plan/SLICE-PREREQS.md                # human prereq gate (passata 2)
reports/plan/gates/<id>.hitl-ok              # morning HITL stamp; next slice requires it

reports/open-questions.md                    # assumptions in force — not a halt
reports/audit/                               # hook jsonl — never hand-edited
```

Passata 2 implementer/reviewer also mount the `afi` skill. Relationship
queries (callers, imports, blast radius) go through AFI, not grep.

Passata 2 also mounts `readme-hygiene`. Product READMEs live at the
**worktree root** (not under this artefact): `README.md`,
`infra/README.md`, `backend/README.md`, `web/README.md`,
`mobile/README.md`. They are operator-facing; update them in the same
commit as public-surface code. They are **not** a kb-contract I/O-chain
file — later phases do not read them to decide routing.

## Passata 2 — code sandbox (declared, not GitHub)

Application code, if written, lives under `workspace/<repo>/` inside this
artifact (`raffa-infra`, `raffa-backend`, `raffa-web`, `raffa-mobile`).
Those directories are **not** the Raffa GitHub org. Fan-out worktrees isolate
**this artifact** (`.helix` git repo, `base_branch: main`), not those remotes
(PROCESS.md D1). Do not invent paths into a remote you cannot see.

## I/O chain (must close)

```
inputs/*.md
  -> reports/context/{product-context,locked-decisions,council-open-questions}.md
  -> reports/architecture/*.md
  -> reports/workitems/** + reports/plan/wave-spec.execution.yaml
  -> reports/plan/slices/*.yaml
  -> reports/open-questions.md
```

Output path of N = input path of N+1. An artefact nothing reads, or a read with
no writer, is a broken chain.

## Who writes what

| Path | Writer | Readers |
|---|---|---|
| `reports/context/*` | docs-ingester | every later producer |
| `reports/architecture/draft/<seat>/*` | that seat's independent lane | council-close producers + gate |
| `reports/architecture/ADR-*.md` | the six council producers at close | decomposer |
| `reports/workitems/**` + master wave-spec + `slices/*.yaml` + `slices/MANIFEST.yaml` | backlog-decomposer + remediator + `cut_nightly_slices.py` | checker, passata 2, `check_slice_prereqs.py` |
| `reports/open-questions.md` | any producer that must assume | every later phase |

## Rules

- Do not invent extra locked platform rules. Cite `locked-decisions.md`.
- Do not pick git flow, SKUs, region, frontend/mobile stack, or Foundry model IDs
  in docs-intake — those are ADR output of the council.
- Passata 1 writes **no application code**.
