# Wave close — `wave-v1-night-r0-a` (retrospective)

- **Helix run**: `c773f0fd-e9e0-412e-9028-4276957010b8`
- **Target**: `execution-fanout` / `raffa-process.yaml`
- **Window**: 2026-09-01 22:15:56 → 23:27:56 UTC
- **Studio status**: `completed`, `failed_task_ids: []`, `skipped_task_ids: []`
- **Claude session T01**: `1d0d3c3d-ba70-4d2b-ad03-fa5fecd24005`
- **PR (opened later, by hand)**: https://github.com/lucalamalfa91/raffa/pull/1

## What Studio green meant (and what it did not)

Helix finished the orchestration. Every r0-a task reached
`IMPLEMENTATION_APPROVED:` (or equivalent reviewer close) and
`helix.fanout.wave_finished` fired. That is **not** “a PR exists” and
**not** “zero impediments”.

`on_orchestration_stop` is an **observation** hook (Helix ADR-0063):
fail-open. A hook error is recorded; the wave still completes. The r0-a
PR hook failed; Studio stayed green.

## Execution (descriptive)

Phase 1 ran T01 (adopt `lucalamalfa91/raffa`, five folders, protect
`main`). The first implementer turn burned time on AFI/`npm`/Glob; a
later turn in the same session wrote the scripts, README, folders, and
applied branch protection (`required_approving_review_count: 0` so the
single owner is not deadlocked). Reviewer closed. Phase 2 ran T02
(secret/folder scan) and US02/T01 (HCP org `raffa-platform` +
workspaces `raffa-dev`/`raffa-demo`) in parallel. Phase 3 ran
US02/T02 (assert remote execution + VCS classification). Reviewers
approved all four. Helix merged `wave/*` into local product
`integration` (`c62a875`).

A prior run (`65a054cf…`) had failed T01 in one second and skipped the
rest. That is not this wave.

## Execution (technical)

| Task | Marker | Product commit |
|------|--------|----------------|
| E01/F01/US01/T01 | implementer + reviewer closed | `c22e366` |
| E01/F01/US01/T02 | `IMPLEMENTATION_APPROVED` | `de0a83c` |
| E01/F01/US02/T01 | `IMPLEMENTATION_APPROVED` | `5e78f5b` |
| E01/F01/US02/T02 | `IMPLEMENTATION_APPROVED` | `c62a875` |

Live HCP check (T02): execution-mode `remote` for both workspaces; no
tfstate in git; **VCS `pending`** (zero oauth-clients). Reviewer treated
that WARN as non-blocking by script contract.

## Why the PR hook did not open a PR

`scripts/open_fanout_pr.py` used `cwd = .helix` (`HERE = parents[1]` of
the script). `run.ps1` had run `ensure_artifact_git.py`, which `git init`'d
`.helix` so Helix would not worktree `helix-artifacts`. After `.helix`
lived *inside* the product clone, that nest became the hook’s git:

- toplevel: `raffa/.helix` (not `raffa`)
- `origin`: **none**
- `main`: `1bddb75 helix: initialize artifact repo…`
- `integration`: one unrelated commit (`4259404`, “four Raffa repos”)

The hook saw `integration` ahead of `main`, then
`git remote get-url origin` failed → **exit 1**. Observation / fail-open
→ wave `completed`. Product `integration` (`c62a875`, the real work)
was never pushed by the hook. The PR was opened later by hand.

Child Claude runs (`f0a379b9…`, `ef1ef0b0…`, …) may still show
`running` in the run index; that is stale, not an open wave.

## Open points (HITL)

All three closed 2026-09-02 (issue #2).

1. **HCP GitHub oauth-client** — **done.** Org `raffa-platform` has the
   GitHub VCS client. `hcp_vcs_wiring.py` attached both workspaces to
   `lucalamalfa91/raffa` / `main` with `trigger-prefixes=['infra/']`
   (`raffa-dev` `ws-DoMFTT8KwDihojKn`, `raffa-demo`
   `ws-5qb5w1ySjg5arWbE`). Per-env working dir:
   `infra/environments/{dev,demo}`.
2. **Leftover nested `.helix/.git`** — **done.** Renamed to
   `.git.nest.bak` after the last fan-out finished. `git -C .helix
   rev-parse --show-toplevel` is the product clone. Do not recreate the
   nest (`ensure_artifact_git` refuses when the parent is already
   `lucalamalfa91/raffa`).
3. **Anti-AFI instruction edits** — **done.** Landed on `integration`
   with the product-repo PR hook, `close_wave_slice.py`, and wave-close
   reports. AFI remains mandatory once `backend/` / `web/` have source.

## Process change after this wave

- `open_fanout_pr.py` resolves the product clone (`_product_repo.py`).
- `ensure_artifact_git.py` will not nest `.helix` when the parent is
  already `lucalamalfa91/raffa`.
- `close_wave_slice.py` always writes `reports/execution/wave-close.md`
  and, if open points remain, opens a GitHub issue labelled `hitl`
  (predefined HITL channel). Optional `RAFFA_HITL_WEBHOOK_URL`.
