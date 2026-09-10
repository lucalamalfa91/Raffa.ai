# Raffa process — phase by phase, and what Helix v0.1 cannot reproduce

This document is the bridge between the mandate
(`.claude/CREATE-PROCESS-PROMPT.md` + `docs/`) and the artifact
(`raffa-process.yaml`). Every phase is **RIPRODOTTA** (with the Helix
expression) or **DICHIARATA** (v0.1 limit). A recorded divergence is a result;
a silent one is a defect.

Language of this file: English (artifact). Deliberation may be Italian.

---

## 1. What the process is

Raffa V1 is an AI-native procurement/contract-intelligence web platform. All
application and infra code is written by **Claude Code through Helix**. Passata 1
designs and decomposes **before any application code is written**.

Two run targets, split by a **human checkpoint**. `governance.hitl` is parsed
and **not enforced**. `require_approval: true` always denies — it is never set.
The stop-after-decomposition **is** the fact that passata 1 is terminal and
passata 2 is a separate `helix run -o execution-fanout` the operator launches.

Git flow, SKUs, region, frontend/mobile stack, and Foundry model IDs are **not**
YAML constants. They are ADR output of the council.

---

## 2. Phases

### 2.1 Docs intake

| Mandate | Helix expression | Status |
|---|---|---|
| Sequential, one producer `docs-ingester` | `docs-intake` topology `sequential` | **RIPRODOTTA** |
| Read `.helix/inputs/*` (copies; native tools refuse `..`) | files copied to `inputs/product-spec.md`, `engineering-brief.md`, `engineering-constraints.md` | **RIPRODOTTA** |
| Produce product-context, locked-decisions, council-open-questions | kb-contract paths; no extra locked rules | **RIPRODOTTA** |

### 2.2 Architecture council — ADRs

| Mandate | Helix expression | Status |
|---|---|---|
| Six specialist seats from the brief, not from bit-flow | agents `product-owner`, `software-architect`, `cloud-architect`, `security-architect`, `client-architect`, `delivery-manager` | **RIPRODOTTA** |
| Independent lanes (no one has read the others) | six `sequential` lanes under `architecture-lanes` `concurrent` `aggregator: concat` | **RIPRODOTTA** (stronger than serial lanes: true parallel) |
| `council-close` group_chat, round_robin, six producers + critic | `council-close`; `max_rounds: 28` = 4 table rounds × 7 | **RIPRODOTTA** |
| Critic only — no write_file, no bash | `council-gate` tools are read-only | **RIPRODOTTA** |
| `termination: on_marker`, `COUNCIL_APPROVED:`, line-anchored | triad + `marker_line_anchored: true` | **RIPRODOTTA** |
| Close gates on ADR files | `close_requires_glob: reports/architecture/*.md` + `COUNCIL_FILES_WRITTEN:` only in the critic | **RIPRODOTTA** |
| Sentinel only in council-gate.md | producers never mention it | **RIPRODOTTA** |

### 2.3 Decomposition

| Mandate | Helix expression | Status |
|---|---|---|
| Decompose **all INDEX ADRs** (R0–R4, five epics) then cut nightly slices | `backlog-decomposer` + `scripts/cut_nightly_slices.py` | **RIPRODOTTA** |
| Placeholder wave-spec on disk at validate | `reports/plan/wave-spec.execution.yaml` committed | **RIPRODOTTA** |
| Waves follow product §16; first slice = org + 4 repos + TF + CI + git-flow + API | `skills/decompose-workitems.md` | **RIPRODOTTA** |
| Gate `DECOMPOSITION_OK:` / `DECOMPOSITION_GAPS:` | registered conditions `decomposition_complete` / `needs_remediation` | **RIPRODOTTA** |
| Remediation loops back to the checker | remediator is chat + `write_file` (D9); workflow back-edge, unconditional; `limits.max_steps: 500000` + `max_iterations: 25` + `run_timeout_s: 28800` | **RIPRODOTTA** |
| `DECOMPOSITION_OK:` ends passata 1 | no outgoing `decomposition_complete` edge; operator launches `execution-fanout` | **RIPRODOTTA** |

### 2.4 Passata 2 — execution

| Mandate | Helix expression | Status |
|---|---|---|
| Separate run target | `raffa-execution` / `execution-fanout`; not in `raffa-design` edges | **RIPRODOTTA** |
| Produce → gate → revise, not a deliberation table | `execution-loop` `workflow`: implementer → reviewer unless `HALTED:`; back-edge only on `IMPLEMENTATION_GAPS:` | **RIPRODOTTA** |
| Pattern E Claude Code; reviewer read-only | harness `allowed_tools` | **RIPRODOTTA** |
| `IMPLEMENTATION_APPROVED:` / `IMPLEMENTATION_GAPS:` / `HALTED:`, line-anchored; not `on_approved_or_halted` | workflow edges + `fan_out.task_failure_markers: ["HALTED:"]` (the engine has no halt-guard for a workflow instance — D2, D13) | **RIPRODOTTA** |
| A task is delivered only with committed work on its branch | `fan_out.require_delivery: true` on the fork-point predicate; `close_wave_slice.py` delivery audit; `salvage/*` tags for dead turns | **RIPRODOTTA** — D13 |
| Fan_out over **one slice wave-spec** | `execution-fanout` `over: slice.current.yaml` | **RIPRODOTTA** — D1 |
| `isolation: git-worktree` + `base_branch: main` + `max_parallel: 3` | worktrees of the local clone | **RIPRODOTTA** (D1) |
| After a green slice, PR to GitHub `main` | `execution-fanout.hooks` `on_orchestration_stop` → `command: scripts/open_fanout_pr.py` (`gh pr create` `integration` → `origin/main`) | **RIPRODOTTA** (`fan_out.write_back` is inert; this is the write-back) |
| Phase-barrier conflicts resolved in-process | `merge_auto` → `merge_resolver: merge-resolve` (agent `conflict-fixer`) → abort; `merge_verify` | **RIPRODOTTA** — operator does not merge by hand |

Product domain READMEs (`infra/README.md`, `backend/README.md`,
`web/README.md`, `mobile/README.md`, root `README.md`) are **standing
hygiene**, not a per-task file claim. Skill `readme-hygiene` is mounted
on implementer, reviewer, and conflict-fixer. An implementer who changes
operator-visible surface updates the matching README in the same commit;
the reviewer treats a stale or missing domain README as blocking. Those
paths stay out of `## Files to create or modify` so same-phase tasks do
not collide on one markdown file (D1 single-writer). Barrier merges union
both sides' factual updates.

No three-pass test pipeline (bit-flow passata 3). Raffa stops at a green
decomposition; code is a separate pass the operator launches. Testing, if added
later, is a **third** top-level target — not this artifact's job.

---

## 3. Declared divergences (v0.1)

### D1 — Fan-out worktrees the local clone; PRs go to GitHub `main`

`fan_out.isolation: git-worktree` + `base_branch: main` is **legacy
ambient-repo**: Helix walks up from the artifact dir
(`run_repo.py::resolve_run_repo`) and worktrees **that** clone. One repo.
Product files go under `infra/`, `backend/`, `web/`, `mobile/` at the
worktree root — not `workspace/<repo>/`, not four remotes (inputs brief).

Dedicated-run-repo (`base_branch` absent) would root the session at
`output_dir` (`reports/`) and hide those folders. That mode is **not** used.

**What we do:** `isolation: git-worktree` + `base_branch: main` +
`max_parallel: 3` + `salvage_uncommitted: true` + `resume_completed: true` +
`merge_auto: true` + `merge_resolver: merge-resolve` + `merge_verify`.
`./run.ps1` / `./run.sh` call `scripts/ensure_artifact_git.py` when this
folder is not already its own toplevel — otherwise Helix would worktree
`helix-artifacts`. Phase barriers merge `wave/*` into `integration`.
On the fan-out **success** path Helix fires `on_orchestration_stop`
(`orchestration_runner.py`, after `emit_fanout_wave_finished`). Two
`command` hooks run, in declaration order:

1. `scripts/open_fanout_pr.py` — push product `integration` and open a
   GitHub PR to `origin/main`. It resolves the *product* clone
   (`lucalamalfa91/raffa`), never the leftover nested `.helix` git
   (that nest has no `origin`; r0-a exited 1 there and Studio stayed
   green).
2. `scripts/close_wave_slice.py` — write
   `reports/execution/wave-close.md`. If any open point remains (no PR,
   HCP VCS pending, …) open a GitHub issue labelled `hitl` on the
   product remote (predefined HITL channel). Optional
   `RAFFA_HITL_WEBHOOK_URL` when the script is run outside the
   stripped hook env.

Neither hook merges onto local `main`. `fan_out.write_back` is **inert**
(manual §12.6). Both hooks are observation-only: a failed push/PR is
recorded and the wave still completed (manual §10.3). **Studio green
does not mean a PR exists or that there were zero warnings.** Child env
is `PATH`/`HOME`/`LANG`/`TMPDIR` only — `gh auth login` must live under
`HOME`. Deadline hard-capped at 60s each.

Same-phase tasks that edit one file are still a wave-spec defect. Recovery
is **in-process**: `merge_auto` (rerere + union) then orchestration
`merge-resolve` (`conflict-fixer` in the integration checkout). Each rung
is gated by `merge_verify`. Implementers must not commit `.helix/` (especially
`reports/open-questions.md`). Uncommitted process edits are wiped when Helix
checks out `integration`/`main` — this wiring lives on `main`.

### D2 — `HALTED:` closes the execution loop immediately

`on_approved_or_halted` is an unanchored substring match on `APPROVED:` (would
also fire on `IMPLEMENTATION_APPROVED:` buried in prose). We do not use it.

`execution-loop` is a `workflow`. `HALTED:` from the implementer skips the
reviewer (`on_marker_absent` `HALTED:`). `HALTED:` from the reviewer has no
back-edge. Either ends **this task** at once — no extra implementer/reviewer
laps. The engine does **not** raise on it: a turn that matches no edge ends
the instance as a normal stop (`stop_reason.kind = no_edge_matched`), and
until 2026-09-09 the fan-out counted that as a delivered task (e13: F04/T02,
F03/T02, F06/T02). `fan_out.task_failure_markers: ["HALTED:"]` now records the
task as failed from its last line; `on_task_failure: block-dependents` skips
only tasks that need this one's `produces`. Independent tasks in the same
wave still run. The only loop-back is `IMPLEMENTATION_GAPS:`.
`IMPLEMENTATION_APPROVED:` has no outgoing edge (success). `max_iterations: 10`
bounds GAPS laps, not halt.

### D3 — Decomposition gate fail-open if no marker is emitted

If `decomposition-checker` emits neither `DECOMPOSITION_OK:` nor
`DECOMPOSITION_GAPS:`, no conditional edge fires and the workflow **ends
silently**. Mitigation: the checker prompt requires the verdict as the last
line. Alternative fail-closed (`on_marker_absent`) is not adopted — it would
spend `max_steps` instead.

`DECOMPOSITION_OK:` (`decomposition_complete`) also has **no outgoing edge**.
That is the intended terminal: the operator then launches `execution-fanout`.

### D4 — Command hooks gated open for the fan-out PR only

`defaults.harness.governance.hooks.allow_external: true` so the
`execution-fanout` `on_orchestration_stop` `command` is admitted
(manual §10.5; a `command` without the flag is `SpecError`). No other
`command` binding is declared. Arithmetic stays a native `bash` tool
call, not a hook.

### D5 — Inert knobs (never presented as wired)

Not in the YAML, because they would be decoration:

- `governance.hitl` — parsed, not enforced as a Studio question gate.
  Wave-close HITL is `scripts/close_wave_slice.py` → GitHub issue
  label `hitl` (and optional webhook). The other human checkpoint is
  the **separate launch** of passata 2, then the GitHub PR to `main`.
- `governance.policy.require_approval: true` — enforced as **always deny**.
- `harness.workspace`, `result_contract`, `observability`, `cost`, `audit` —
  typed, no binder reads them. Tracing is env (`OTEL_ENABLED`).
- `fan_out.as_input` / `write_back` — inert.
- `skills[].load: on_demand` — raises `NotImplementedError` at bind.

`governance.policy.model_allowlist` **is** enforced fail-closed and is set.

Ids are unique across the **whole** document (models, tools, MCP, skills, agents,
orchestrations).

### D6 — No schema validator hook

`close_requires_glob` proves a fresh file exists, not that it matches a schema.
Conformity is template + critic.

### D7 — Nested `reset_globs` is inert

`reset_globs` is declared on the run target, not on nested nodes. Nested
`council-close` does not reset. **Neither run target resets ADRs or context**
(a `reports/architecture/*.md` glob deleted INDEX + ADR-001…016 on 2026-08-27;
drafts survived in subfolders). Wipe only with `./run.ps1 --fresh`.
Freshness of ADRs still requires `close_requires_glob` mtime >= session start.

Wave-spec is **not** in `reset_globs`: deleting it would make the next
document load fail (`fan_out.over` existence check). The decomposer overwrites
the placeholder in place.

### D8 — DeepSeek thinking mode + tools = 400

Passata 1 chat agents all use tools (`read_file`, `write_file`, …). DeepSeek
V4 thinking mode (default on v4-* and on the `deepseek-chat` alias) requires
every later request to echo `reasoning_content`. Helix does not. **RIPRODOTTA**
by `models[].options.extra_body.thinking.type: disabled` on every chat model
(Helix transport merges it into `chat.completions.create`). Changing the
model id alone does not disable thinking.

### D9 — Decomposition writers are not Pattern E (`CodingAgentTurnTimeout`)

The mandate used Pattern E (Claude Code) for *authoring many files*. A coding-
agent turn had a **600 s wall-clock** deadline in the engine when this was
decided; writing or rewriting the four-level tree in one subprocess exceeded it
(`CodingAgentTurnTimeout`, retryable). Since 2026-09-09 the deadline is an
**inactivity** deadline (D13) and this reason no longer applies; the split
stays because the chat writers are cheaper per file and the tree is stable.
That timeout is not a YAML field.

**What we do:** `backlog-decomposer` and `decomposition-remediator` are
`deepseek-reasoning` + native `write_file` / `glob` / `list_dir` (same path as
`docs-ingester`). Each file is its own chat tool round (`timeout: 900` per
model call). Vocabulary degrades (`Write`/`Edit`/`Bash` → `write_file` /
`glob`). `implementer` and `reviewer` stay Pattern E (application code).

Chat `write_file` inflates `limits.max_steps` (streamed chunks for the whole
passata, not turns). 60000 died with `MaxStepsExceeded` after the switch.
The cap is **500000**; the checker↔remediator brake remains
`max_iterations: 25`. `run_timeout_s` is **28800** (8 h) against the 4 h
default, which is too tight for council + tree.

### D10 — DeepSeek + Helix local-model middleware = 400 on `role: tool`

Helix `chat_completions` mounts two llama.cpp middlewares by default:
`normalize_turns` (relabels assistant-after-assistant to `user`) and
`tool_turn_cue` (appends a user cue when the last turn is assistant and
tools are present). Either can sit a `user` turn between `tool_calls` and
`role: tool` results. DeepSeek then returns 400: *Messages with role 'tool'
must be a response to a preceding message with 'tool_calls'* (reproduced on
`software-architect` in council-close).

**RIPRODOTTA** on every DeepSeek model: `normalize_turns: false`,
`tool_turn_cue: false`, `tool_call_content: ensure` (not Moonshot `strip`).
Do not Resume a session that already 400'd — the poisoned transcript
replays. Start a **new** run. Default orchestration is `raffa-plan-r0-r4`
(no council); `software-architect` is not a participant.

---

## 4. Control graph

```
PASSATA 1 -- raffa-design (NOT default). Re-analysis: --fresh then this.

  docs-intake
        |
        v
  architecture-council     (ALL ADRs in INDEX — not an R0 subset)
        v
  decomposition            (epic-01..05 from INDEX; then cut_nightly_slices.py)
        v
  decomposition-check ⇄ remediation
        v
  DECOMPOSITION_OK:     TERMINAL  (no outgoing edge)

PASSATA 1b -- raffa-plan-r0-r4  leftover append-R1–R4 without re-council
PASSATA 1c -- raffa-plan-close (DEFAULT)  check ⇄ remediator → STOP

=========== human checkpoint: review the tree, then launch passata 2 ===========

PASSATA 2 -- ./run.ps1 -Max -Slice r0-a -o execution-fanout
  copies reports/plan/slices/r0-a.yaml → slice.current.yaml
  scripts/ensure_artifact_git.py  (local clone is a git toplevel on main)
  execution-fanout walks THAT file only (not the 103-task master)
    isolation git-worktree of the local clone, max_parallel 3, resume_completed
    barrier: merge_auto → merge-resolve (conflict-fixer) → abort (no hand merge)
    HALTED: ends that task immediately (no extra implementer/reviewer laps);
    block-dependents skips only tasks that need its produces
  on_orchestration_stop → scripts/open_fanout_pr.py (product clone)
    push origin/integration → gh pr create --base main
  on_orchestration_stop → scripts/close_wave_slice.py
    reports/execution/wave-close.md; HITL issue if open points
```

### D11 — Passata 2 bills Claude Code Max, not Console API

Operator rule: Helix implementer/reviewer/conflict-fixer run on a **Max** seat (`claude login`),
not `ANTHROPIC_API_KEY`. Hub `ANTHROPIC_AUTH_TOKEN` is unset for that run
(`./run.ps1 -Max`). There is no overnight wrapper. Morning HITL is the `hitl` GitHub issue
opened when `close_wave_slice.py` finds open points after a green wave.

**What we do:** launch **one slice** with
`./run.ps1 -Max -Slice r0-a -o execution-fanout`. That copies
`reports/plan/slices/r0-a.yaml` onto `slice.current.yaml` (the only file
`execution-fanout` walks). `fan_out.resume_completed: true` skips tasks
whose `wave/*` branch carries committed work beyond its fork point (D13).
Intra-phase `max_parallel: 3` shortens wall-clock on wide slices; it does
**not** reduce tokens. `max_task_attempts: 3` retries a transient death
(connection lost, hung subprocess) on a fresh worktree; an
`IMPLEMENTATION_GAPS:` lap is not an attempt.

### D12 — Cost hub and CEO briefing are not in this artifact

The original mandate ran cost-hub (three Firecrawl spokes + rollup) then
`ceo-briefing` after a green decomposition. Those orchestrations, agents,
skills, and the Firecrawl MCP are **unwired**. Passata 1 ends on
`DECOMPOSITION_OK:`. The operator reviews the tree and launches
`execution-fanout` by hand. Leftover `reports/costs/` and `reports/briefing/`
files from earlier runs are not inputs to passata 2.

### D13 — Delivery contract: a task is done only with committed work (2026-09-09)

**What happened (e13, run `5dec6283`).** Studio reported 20/20 tasks
finished, 0 failed, and the stop hook opened PR #67. Five tasks had delivered
nothing: F04/T01, F11/T01 (implementer turn killed at the 3600 s deadline
before `git commit`, then every retry burned in seconds), F04/T02 and F03/T02
(implementer `HALTED:` because F04/T01 was missing), F06/T02 (reviewer closed
without a verdict). A sixth defect shipped a broken build: F02/T02 and F06/T01
both created `MarketEndpointExtensions.cs` in phase 3 and the barrier
union-merged the two files.

**Engine defects, fixed in Helix (branch `fix/fanout-delivery-contract`):**

1. The turn deadline was a total cap that killed healthy sessions. It is now
   an inactivity deadline (`HELIX_CODING_AGENT_TURN_DEADLINE_SECONDS`, re-armed
   on every streamed message) plus an optional absolute cap
   (`HELIX_CODING_AGENT_TURN_MAX_SECONDS`).
2. `soft-accept`, `resume_completed` and the retry checkpoint measured a task
   branch against `main`. A phase ≥ 2 branch forks from the advanced
   `integration` tip, so it "had commits" at creation. They now measure the
   branch's own diff beyond its fork point (`refs/helix/fork/<branch>`,
   pinned at provisioning; reflog creation entry for older branches).
3. A workflow instance that ended on `HALTED:` or with no marker was a
   success. `fan_out.task_failure_markers` and `fan_out.require_delivery`
   record such tasks as failed; dependents are blocked.
4. A stale worktree that Windows could not delete (a build server or test
   host outliving the coding agent) made `git worktree add` refuse every
   retry. A retry now provisions at `<id>.r<n>` when the directory survives.
5. The deterministic union pass unioned source files. It is limited to prose
   (`.md`, `.txt`, `.gitignore`, …); code conflicts go to `conflict-fixer`,
   then abort.
6. Every failed attempt now emits `helix.fanout.task_attempt_failed` with its
   reason, so the run index explains retries.

**Process changes (this artifact):** `require_delivery: true`,
`task_failure_markers: ["HALTED:"]`; implementer commits WIP checkpoints and
runs `dotnet build-server shutdown`; reviewer verifies the commit and never
ends a turn without a marker; `merge_verify.py` runs `dotnet build` when a
barrier conflict touched backend C#; `close_wave_slice.py` audits delivery
per task and lists `salvage/*` tags; `check_single_writer.py` refuses a slice
whose same-phase tasks claim one file (also a start hook).

**Operator rules.**

- Never commit on `integration`, `git pull` it, or move the clone's HEAD while
  a wave runs: the clone's HEAD is the barrier's checkout and worktrees fork
  from it (e13 phase 1 forked from an operator docs commit).
- A dead turn's uncommitted files are on `salvage/<task>/<n>` tags:
  `git show --stat <tag>`, then `git checkout <tag> -- <paths>` on a branch
  from `integration`. The wave-close report lists them per undelivered task.
- After changing the deadline variables in `.env`, restart the Studio
  backend (they are read at import).
- Run `python scripts/check_slice_prereqs.py --slice <id>` before every
  launch; it fails closed on a same-phase single-writer collision.

---

## 5. I/O chain (closed)

```
inputs/*.md
  -> reports/context/{product-context,locked-decisions,council-open-questions}.md
  -> reports/architecture/*.md
  -> reports/workitems/** + reports/plan/wave-spec.execution.yaml
  -> reports/plan/slices/*.yaml  (cut_nightly_slices.py; passata 2 unit)
  -> reports/open-questions.md
```

Proved in `skills/kb-contract.md`. Sequential phases have no close-glob (that
field binds on `group_chat` only); the next phase `HALTED:` if the file is
missing.

---

## 6. What we did not clone from bit-flow

ADO/Confluence intake, alm-sync, plan-publisher / `$BITFLOW_TARGET_REPO`,
test-planner/executor/gate/reporter, refinement loop, three-pass test pipeline.
Those agents and skills were deleted from this folder, not extended.
