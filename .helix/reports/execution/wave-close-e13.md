# Wave close — `wave-v1-epic-e13` (operator post-mortem, 2026-09-09)

Companion to the hook-written `wave-close.md`, which lists the commits on
`integration` and closes with *Open points: 0*, on top of a fan-out that
reported *completed 20, failed 0*. That record is honest about the
orchestration and wrong about the product: **five** of the twenty tasks
finished **without a commit**.

- **Helix run**: `5dec6283-bd05-4779-bda4-b91b9f0408b0`
- **Target**: `execution-fanout` / `contigo-process.yaml`
- **Window**: 2026-09-08 19:43:33 → 2026-09-09 09:30:19 UTC
- **Studio status**: `completed`, `failed_task_ids: []`, `skipped_task_ids: []`
- **`helix.fanout.wave_started`**: 5 phases, 20 live tasks, `maxParallel: 3`
- **`helix.fanout.wave_finished`**: `completedCount: 20, failedCount: 0, skippedCount: 0`
- **PR**: https://github.com/lucalamalfa91/contigo/pull/67 (`integration → main`, open)

## What landed on `integration` from the wave (15 of 20 tasks)

| Phase | Task | Commit |
|---|---|---|
| 1 | E13/F01/US01/T01 v2-scaffold | `6c65a5d` |
| 1 | E13/F01/US01/T02 foundry-gateway | `227a116` |
| 1 | E13/F05/US01/T01 conversations-store | `420f9b3` |
| 1 | E13/F09/US01/T01 web-shell-v2 | `fb8e687` |
| 2 | E13/F02/US01/T01 market-feed-mock | `d6e29ed` |
| 2 | E13/F03/US01/T01 supplier-entity | `5cee4c7` |
| 2 | E13/F05/US01/T02 conversations-api | `b9e4355` |
| 2 | E13/F07/US01/T01 insights-calculators | `dc79ae4` |
| 2 | E13/F08/US01/T01 capability-catalog | `0a3197c` |
| 2 | E13/F09/US01/T02 web-rich-reply | `f44618e` |
| 3 | E13/F02/US01/T02 market-index | `ed22edf` + `c1480d6` |
| 3 | E13/F06/US01/T01 ask-engine | `cfeb45a` |
| 3 | E13/F09/US01/T03 web-documents-v2 | `f09e75a` |
| 3 | E13/F10/US01/T01 web-contract360-landing | `8b587b1` |
| 4 | E13/F09/US01/T04 web-ask-v2 | `1f581f4` |

`git log origin/main..integration` is 30 commits: the 16 task commits above,
the 12 phase-barrier merges (`9fd5ed4` `84427cf` `cd64e0a` `b1e3c66` —
phase 1; `34e2660` `b845139` `4d1467a` `8aed9c1` `97b8d84` — phase 2;
`057fe32` `7b23fa3` `8267d3f` — phase 3), and the two post-wave repair
commits below. Phases 4 and 5 produced no merge commit: `1f581f4`
fast-forwarded and the other two branches were empty. Use `origin/main`, not
the local `main`, which is behind.

## What did not land (5 of 20) — and why

Each row's *branch head* is what `wave/<TASK>` actually points at: a commit
that belongs to something else (a docs commit, a barrier merge, another
task's commit), i.e. the branch carries no task work at all.

| Task | Phase | Branch head | Evidence (run `5dec6283`) |
|---|---|---|---|
| E13/F04/US01/T01 documents-admission | 1 | `71ea8bb` (a `docs(architecture)` commit) | `RUN_FINISHED` 20:43:47 UTC, `error: CodingAgentTurnTimeout('coding-agent turn exceeded its 3600s deadline …')`, `failed_agent_id: implementer`; `helix.fanout.task_finished` 25 s later (20:44:12) under a fresh `taskRunId` `486ed882` |
| E13/F04/US01/T02 documents-v2-api | 2 | `b1e3c66` (last phase-1 barrier merge) | `RUN_FINISHED` 22:54:56 UTC, no error; the implementer closed with `HALTED:` — `wave/E13-F04-US01-T01` "carries zero commits", `Program.cs` still had the inline `/api/documents` handlers, no `DocumentAdmissionGate` |
| E13/F03/US01/T02 supplier-extraction | 3 | `97b8d84` (last phase-2 barrier merge) | `RUN_FINISHED` 01:45:43 UTC, no error; the reviewer closed with `HALTED:` naming E13/F04/US01/T01 **and** E13/F04/US01/T02 as absent from `integration` |
| E13/F06/US01/T02 ask-golden-set | 4 | `8267d3f` (last phase-3 barrier merge) | first attempt `d1cc679f`: `RUN_FINISHED` 06:34:04 UTC, `error: CodingAgentTurnTimeout(… 3600s …)`; retry `cbbfd148`: `task_finished` 08:28:51 UTC with the reviewer's own words — "No verdict on this turn", waiting for a commit that never came |
| E13/F11/US01/T01 v2-integration | 5 | `1f581f4` (phase-4 head) | attempt `1283906c`: `RUN_FINISHED` 09:29:24 UTC, `error: CodingAgentTurnTimeout(… 3600s …)`; `task_finished` 09:30:05 UTC under a fresh `taskRunId` `4956ace0` |

Two distinct causes, not one:

1. **The turn deadline** (E13/F04/US01/T01, E13/F06/US01/T02, E13/F11/US01/T01).
   `.helix/.env` sets `HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS=3600`, which is
   the "3600s deadline" the error names. The implementer turn ended before
   `git commit`. In each case the fan-out then emitted `task_finished` under a
   new `taskRunId`, so the wave counted the task as done
   (`failed_task_ids: []`).
2. **A dependency cascade from cause 1** (E13/F04/US01/T02, E13/F03/US01/T02).
   Both ran to a clean `HALTED:` and made no edits, because `documents-admission`
   — a phase-1 task — had produced nothing for them to build on. The phase
   barrier merged an empty branch and the next phase started anyway.

### `salvage_uncommitted` did keep work — check the tags before re-running

`git tag -l "salvage/E13*"` (verified):

| Tag | Commit | Content |
|---|---|---|
| `salvage/E13-F04-US01-T01/1` | `71ea8bb` | branch head — **nothing salvaged** |
| `salvage/E13-F06-US01-T02/1` | `ac4c626` | 18 files, +1901/−99 (`Contigo.AiEval` golden set, three tenant fixtures) |
| `salvage/E13-F06-US01-T02/2` | `8267d3f` | barrier merge — nothing salvaged |
| `salvage/E13-F06-US01-T02/3` | `9aae41d` | 15 files, +1716/−93 (second attempt of the same golden set) |
| `salvage/E13-F11-US01-T01/1` | `baccab8` | 9 files, +1374/−107 (`seed-market-intelligence.yml`, `reprocess-tenant-documents.yml`, `web/e2e/v2.spec.ts`, `docs/ask-v2-acceptance.md`, README sweep) |
| `salvage/E13-F11-US01-T01/2`, `/3` | `1f581f4` | phase-4 head — nothing salvaged |

E13/F04/US01/T02 and E13/F03/US01/T02 have no salvage tag at all, consistent
with their `HALTED:` reports ("`git status --short` is clean — there is
nothing to commit"). So: the golden set and the integration task are **not**
lost; they are reachable from the tags above and are worth reading before
re-writing them from scratch.

## Two union-merge defects from the phase barriers — both repaired

**1. `backend/src/Contigo.Api/MarketEndpointExtensions.cs` — the build break.**
`git log --diff-filter=A` shows the file was *created* by two different
phase-3 tasks: `ed22edf` (E13/F02/US01/T02) and `cfeb45a` (E13/F06/US01/T01).
F06/T01's own doc comment shows it had no way to know: it calls itself "this
endpoint's first writer" because the market-ingestion task "has not landed in
this wave" — it was running in a sibling worktree of the same phase.
The barrier `merge_auto` union merge concatenated both handlers (duplicate
`deal`, undefined `endpoints`/`detail`, repeated anonymous-type properties)
and broke `dotnet build Contigo.slnx` on PR #67. Reconciled in `50b38a7`
(65 insertions / 70 deletions in that one file): one handler for
`GET /api/market/records/{id}`, persisted `market_record` store when the
Market module has a connection string, in-memory feed otherwise, response in
the OpenAPI `getMarketRecord` shape the web panel expects. That commit records
build 0 errors and `dotnet test Contigo.slnx` 1330 passed / 0 failed.

**2. `backend/README.md` — the same damage in prose.** Ten of the wave's task
commits touch this file (the `readme-hygiene` skill asks them to), and its
`## Solution` section does not accumulate rows — it *describes the current
state*, so each task rewrote it. At `50b38a7` the module tree listed
`Contigo.Documents.Contracts/`, `Contigo.Audit/` and `Contigo.AiGateway/`
three times each and `Contigo.Market/` / `Contigo.Insights/` twice, and the
"**V2 scaffold (task E13/F01/US01/T01)**" paragraph appeared twice with a
third version spliced headerless into the middle of the first — three mutually
contradicting descriptions of the same modules. The damage accumulated barrier
by barrier: one copy at the phase-1 barrier (`b1e3c66`), two at the phase-3
barrier (`8267d3f`), three by `50b38a7`. Rebuilt in `1ca7888`, which says so
in its own message; that commit records `dotnet test Contigo.slnx`
1377 passed / 0 failed.

Union merge is a *concatenator*, not a reconciler. It is safe for
append-only lists and unsafe for anything a second task also rewrites — a
new file, or a prose section that describes the current state.

## Decision: finish on `integration`, do not re-run the wave

The five missing tasks are being implemented **directly on the `integration`
branch (PR #67)** rather than by a second Helix slice. There is no `e13b`
slice, none was ever cut, and nothing should refer to one: the only slice on
disk is `e13` (`reports/plan/slices/e13.yaml`, byte-identical to
`reports/plan/slice.current.yaml`).

Why not re-run: `e13.yaml` carries all 20 tasks with `status: live`, so
re-launching the slice would dispatch the 15 already delivered ones again; and
the two cascade tasks never failed on their own merits — they were starved by
a phase-1 task that produced nothing, which a re-run of the same slice would
not by itself prevent.

State of the five:

| Task | State |
|---|---|
| E13/F04/US01/T01 | **landed** on `integration` as `1ca7888` — admission gate before persistence, format sniffing, documents endpoints out of `Program.cs`; it also repaired the README (defect 2 above) |
| E13/F04/US01/T02 | in progress — no commit yet |
| E13/F03/US01/T02 | in progress — no commit yet |
| E13/F06/US01/T02 | in progress — no commit yet; start from `salvage/E13-F06-US01-T02/1` and `/3` |
| E13/F11/US01/T01 | in progress — no commit yet; start from `salvage/E13-F11-US01-T01/1` |

Remaining, once all five are on `integration`:

1. Green CI on PR #67, then merge into `main`.
2. `docs/ask-v2-acceptance.md` A1–A14 on `dev`, then `demo-v*`.
3. Delete the five empty wave branches so nothing is mistaken for done:
   `git branch -D wave/E13-F04-US01-T01 wave/E13-F04-US01-T02 wave/E13-F03-US01-T02 wave/E13-F06-US01-T02 wave/E13-F11-US01-T01`
   — but keep the `salvage/E13-*` tags until their tasks are committed.

## Process changes this wave earns

- **`HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS`** is still `3600` in
  `.helix/.env`. Three of the five losses are that number. The tasks that hit
  it are the biggest single sessions of the wave (admission gate + format
  sniffing + endpoint move; ≥ 40 golden cases with three tenant fixtures; two
  CI jobs + e2e + acceptance doc + README sweep). Either raise it for waves
  with L-effort backend tasks, or split those tasks.
- **A `task_finished` is not a delivery signal.** The fan-out emitted one for
  every task that timed out, under a fresh `taskRunId`. The close record must
  compare tasks against commits, not against `failed_task_ids`.
- **A phase barrier must not merge an empty branch silently.** A task whose
  branch head is the barrier merge it started from produced nothing, and every
  later task that declares it in `depends_on` will halt. That is checkable at
  the barrier, before the next phase starts.
- **Decomposition** — recorded in `skills/decompose-ask-workitems.md`: a file
  whose *creation* is owned by one task must not be *required to exist* by
  another task of the same phase, and any prose file several tasks append to
  needs a single writer per phase.
