# Wave close — `wave-v1-epic-e13` (operator post-mortem, 2026-09-09)

Companion to the hook-written `wave-close.md`, which reports *completed 20,
failed 0, open points 0*. That report described the orchestration, not the
product: **five of the twenty tasks delivered nothing**, and one phase-3
merge shipped a broken build. PR #67 (`integration → main`) was merged after
the build was reconciled by hand (`50b38a7`).

## What landed on `integration` (15 of 20)

| Phase | Delivered | Commit |
|---|---|---|
| 1 | F01/T01 scaffold · F01/T02 Foundry gateway · F05/T01 conversations store · F09/T01 web shell V2 | `6c65a5d` `227a116` `420f9b3` `fb8e687` |
| 2 | F02/T01 market feed mock · F03/T01 supplier entity · F05/T02 conversations API · F07/T01 insights · F08/T01 capability catalog · F09/T02 rich reply | `d6e29ed` `5cee4c7` `b9e4355` `dc79ae4` `0a3197c` `f44618e` |
| 3 | F02/T02 market index · F06/T01 ask engine · F09/T03 documents V2 web · F10/T01 contract 360 landing | `c1480d6` `cfeb45a` `f09e75a` `8b587b1` |
| 4 | F09/T04 Ask V2 web | `1f581f4` |

## What did not land (5 of 20) — and exactly why

Evidence: `~/.helix/run-index.db`, run `5dec6283`, and the `wave/E13-*` branch
reflogs. Each branch's own diff beyond its creation point is **zero files**.

| Task | Phase | What happened | Recoverable work |
|---|---|---|---|
| F04/T01 documents admission | 1 | Implementer turn killed at the 3600 s turn deadline (20:43 UTC) before any commit. Retries 2 and 3 died in seconds: the worktree could not be cleared (locked `backend/**/bin`), `git worktree add` refused it. Soft-accept then counted `main..branch` = 2 inherited docs commits as "work" → *finished*. | none |
| F04/T02 documents V2 API | 2 | Implementer correctly `HALTED:` after 8 minutes (T01 absent). The workflow ended as a normal stop (`no_edge_matched`) → *finished*. | none (no edits) |
| F03/T02 supplier extraction | 3 | Implementer wrote the extraction code, then `HALTED:` (F04 absent); reviewer echoed the halt → *finished*. No commit; no salvage tag. | none |
| F06/T02 AI golden set | 4 | Attempt 1 killed at 3600 s. Attempt 2 wrote the whole golden set, never committed; reviewer closed with "No verdict on this turn" → *finished*. | `salvage/E13-F06-US01-T02/1` and `/3` (Contigo.AiEval: 3 tenant fixtures, golden JSON, harness) |
| F11/T01 V2 integration | 5 | Killed at 3600 s before commit; retries burned on the locked worktree; soft-accept counted 30 inherited commits → *finished*. | `salvage/E13-F11-US01-T01/1` (both CI workflows, `docs/ask-v2-acceptance.md`, `web/e2e/v2.spec.ts`, README sweep, a reconciled `MarketEndpointExtensions.cs`) |

Recover a tag onto a fresh branch from `integration`:

```bash
git show --stat salvage/E13-F11-US01-T01/1
git checkout -b wave/E13-F11-US01-T01-recovered integration
git checkout salvage/E13-F11-US01-T01/1 -- .github docs web/e2e backend/README.md web/README.md
```

## Root causes — engine or process?

Both, and the engine defects were the ones that turned losses into lies.

**Helix engine** (fixed on branch `fix/fanout-delivery-contract`):

1. **Turn deadline was a total cap.** `HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS`
   bounded the whole session; four healthy, streaming implementer turns were
   killed at exactly 3600 s. It is now an inactivity deadline (re-armed on
   every streamed message) with an optional absolute cap
   `HELIX_CODING_AGENT_TURN_MAX_SECONDS`.
2. **Delivery measured against `main`.** `soft-accept`
   (`rev-list --count main..branch`), `resume_completed` and the retry
   checkpoint (`diff main...branch`) all read inherited commits as the task's
   work: any phase ≥ 2 branch is "done" the moment it is created. They now
   measure the branch's own diff beyond a pinned fork point
   (`refs/helix/fork/<branch>`; reflog creation entry for older branches).
3. **No halt-guard.** A workflow instance ending on `HALTED:` or on a turn
   without a marker was a success. New `fan_out.task_failure_markers` and
   `fan_out.require_delivery` record such tasks as failed and block dependents.
4. **Retry could not reprovision on Windows.** `_is_child_alive` is hard-wired
   to `False`; a test host or build server that outlives the coding agent keeps
   the worktree locked, and every retry failed at `git worktree add`. A retry
   now provisions at `<id>.r<n>` when the directory survives its reset.
5. **Union merge of source files.** The deterministic auto-pass unioned two
   whole `MarketEndpointExtensions.cs` files (add/add) and the marker-only
   `merge_verify` accepted it. Union is now limited to prose; code goes to
   `conflict-fixer`, then abort.
6. **No per-attempt evidence.** Attempt reasons were logged only; the run index
   showed the retries without their cause. `helix.fanout.task_attempt_failed`
   now carries each attempt's reason.

**Contigo process** (this PR):

- `contigo-process.yaml` assumed an engine halt-guard that never existed; it
  now sets `require_delivery: true` and `task_failure_markers: ["HALTED:"]`.
- The implementer committed only at the end of the turn; it now commits WIP
  checkpoints, keeps commands under ten minutes and shuts the build server
  down. The reviewer verifies the commit and may not end a turn without a
  marker.
- `merge_verify.py` runs `dotnet build` when a barrier conflict touched
  backend C#; `close_wave_slice.py` audits delivery per task and lists the
  salvage tags; `check_single_writer.py` refuses a slice whose same-phase tasks
  claim one file (creation included) and runs as a start hook.
- Operator hazard recorded in `PROCESS.md` D13: during phase 1 the operator
  committed docs on `integration` and pulled it; the barrier merged on top of
  those commits and a phase-1 retry forked from them. Never touch
  `integration` or the clone's HEAD while a wave runs.
- `.env`: `HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS=900` (inactivity),
  `HELIX_CODING_AGENT_TURN_MAX_SECONDS=10800`. Restart the Studio backend after
  changing them.

## How to finish the five tasks

PR #67 is merged, so `main` carries the fifteen delivered tasks. The remaining
five are implemented directly (order: F04/T01 → F04/T02 → F03/T02 → F06/T02 →
F11/T01), each as one commit with the task id in the subject, starting from
the salvage tags where they exist. With the fixed engine a relaunch of `e13`
would also be honest: `resume_completed` now skips only branches with their own
committed work, and the five empty branches would re-run — but the direct route
is shorter and the branches should be deleted first either way:

```bash
git branch -D wave/E13-F04-US01-T01 wave/E13-F04-US01-T02 wave/E13-F03-US01-T02 wave/E13-F06-US01-T02 wave/E13-F11-US01-T01
```

Then `docs/ask-v2-acceptance.md` A1–A14 on `dev`, then `demo-v*`.
