# Raffa next-wave process — phase by phase

`raffa-next-process.yaml` + `run-next.ps1`. The process used after the
initial build: stakeholder tests, customer demos, feedback, new ideas, bug
fixes. It does the same loop as `raffa-process.yaml` (intake → council →
ADRs → decomposition → wave) but **starts from the existing code,
infrastructure, ADRs and backlog**, reasons on three things — the initial
requirements, the new requirements of this round, what is already built —
and ends with **one wave ready to launch**. It never removes earlier work:
the backlog only grows, ADRs only get footers, old slices stay.

| File | Role |
|---|---|
| `raffa-process.yaml` + `run.ps1` / Helix Studio | the initial design process **and the live Passata 2** (`execution-fanout`). Unchanged. |
| `raffa-next-process.yaml` + `run-next.ps1` | Passata 1 for every later wave. Produces `reports/plan/slices/<w>.yaml`; the wave runs on the file above. |

Language of this file: English (artifact). Deliberation may be Italian.

---

## 1. Inputs and outputs

**Read** (never written): the initial requirements (`inputs/product-spec.md`,
`inputs/requirements.md`, `inputs/percorso-pilota-v1.md`,
`inputs/engineering-brief.md`), **the raw requirements of this round**
(`inputs/next/<file>.md` — a todo, demo notes, a bug list, a new doc, a
pointer to a Claude Design export under `inputs/design/`), the product code
on this checkout (`../backend`, `../web`, `../infra`, `../.github`), the
ADRs, the backlog, the wave chain (`MANIFEST.yaml`), the wave-close
reports.

**Written**, per wave `<w>` (`skills/kb-contract-next.md` has the table):

```
inputs/next/<file>.md + inputs/design/** + ../<code> + ADRs + BACKLOG + MANIFEST
  -> reports/context/waves/<w>-requirements.md        (normalized requirements, seat roster)
     reports/architecture/waves/<w>.md                (decision record skeleton)
  -> reports/architecture/draft/next/<seat>/<w>.md    (involved seats only)
  -> reports/architecture/ADR-*.md (new / footers) + INDEX.md + waves/<w>.md (complete)
  -> reports/workitems/epic-NN-*/** + BACKLOG.md      (new epics, append-only)
  -> reports/plan/slices/<w>.yaml + MANIFEST.yaml row + INDEX-next.md + reports/audit/<w>-hitl.md
  -> operator: gates/<w>.hitl-ok, ./run.ps1 -Max -Slice <w> -o execution-fanout
```

The raw file is transformed, never edited. The normalized file is the
process oracle: one row per item with kind, priority, area, **status today
(evidence in code)**, **seats needed**, ADR touchpoints, design refs,
acceptance, proposed epic. Template: `templates/next-requirements-template.md`.

---

## 2. Phases

Every agent runs on **Claude Code Opus** through the coding-agent harness
(operator decision 2026-09-08, `models[cc-opus].model =
${ANTHROPIC_DEFAULT_OPUS_MODEL}`); cwd = `.helix`; product code is read at
`../`. Passata 1 writes no application code and never commits.

### 2.1 Intake (`next-intake-phase`)

| Mandate | Helix expression | Status |
|---|---|---|
| One producer reads the raw file, the code, the ADRs, the backlog; writes the normalized requirements and the decision-record skeleton; decides the seat roster per item | `sequential` [`next-intake`]; harness `allowed_tools: [Read, Write, Edit, Grep, Glob, Bash]` (Bash = read-only git + sha256 + `register_wave.py --next-id`); `max_turns: 150` | **RIPRODOTTA** |
| Run parameters (wave id, raw file, caps, focus) | `reports/plan/next-run.json` written by the launcher → workflow input string → defaults (`skills/kb-contract-next.md`) | **RIPRODOTTA** (D-N3) |
| Missing raw file stops the run | `HALTED:` last line; the edge to the council is `on_marker CONTEXT_READY:` (fail-closed), so a halt, a silent cut-off or a missing marker ends the run — no edge fires (D-N10) | **RIPRODOTTA** |
| Hand-off | `CONTEXT_READY: <w> — …; seats: …` last line (the edge's marker); the next phase reads the files | **RIPRODOTTA** |

### 2.2 Dynamic council (`next-council` = `next-lanes` → `next-council-close`)

Seven seats + gate are declared; **only the seats the intake listed for an
item work on it**. The others answer in one line. `skills/next-seats.md` is
the routing table; `skills/council-protocol-next.md` the protocol.

| Mandate | Helix expression | Status |
|---|---|---|
| Independent lanes, no one has read the others | seven `sequential` lanes under `next-lanes` `concurrent` `aggregator: concat`; an uninvolved seat emits `LANE_SKIPPED:` at once (no reading beyond the roster) | **RIPRODOTTA** |
| Table with only the necessary people | `next-council-close` `group_chat`, `round_robin`, participants `[7 seats, next-council-gate]`; uninvolved seats: label + one line + `VOTE: PASS`. Helix cannot drop a participant from a running group chat; the PASS turn is the cheapest expressible form (D-N1) | **RIPRODOTTA** with declared cost |
| Producers write ADRs / footers / INDEX / decision rows; the gate closes | `termination: on_marker` `COUNCIL_APPROVED:` `marker_line_anchored: true`; `max_rounds: 24` = 3 table rounds × 8 participants; sentinel only in `agents/next-council-gate.md` | **RIPRODOTTA** |
| Consensus is not delivery | `close_requires_glob: reports/architecture/waves/*.md` (fresh: the intake writes the skeleton in this run, the seats complete it) + `close_requires_marker: COUNCIL_FILES_WRITTEN:` (gate only) | **RIPRODOTTA** |
| Stuck-loop brake | `limits: { max_identical_turns: 9 }` — the fingerprint window is shared by all participants (`builders.py` group_chat), so three consecutive PASS turns would trip a smaller window (D-N1) | **RIPRODOTTA** with declared value |
| Empty wave (no seat involved) | every seat `PASS`, the gate closes on the skeleton (`task only` rows) in one round | **RIPRODOTTA** |

### 2.3 Decomposition (`next-decomposition` → `next-check` ⇄ `next-remediation`)

| Mandate | Helix expression | Status |
|---|---|---|
| Append new epics per wave theme; never remove; supersede only on explicit cancel | `next-decomposer` harness `[Read, Write, Edit, Bash, Grep, Glob]`; `skills/decompose-next-workitems.md`; `assert_next_plan_untouched.py` proves append-only after the run | **RIPRODOTTA** |
| One wave per run, cap `max_tasks` / `max_phases`, overflow queued | wave written directly as `reports/plan/slices/<w>.yaml`; `scripts/register_wave.py` validates caps + DAG + single writer + `helix validate-wavespec`, upserts the MANIFEST row (`previous` = last wave) | **RIPRODOTTA** (the cap is a process rule, not an engine knob — D-N4) |
| Decomposer hand-off | edge `next-decomposition → next-check` fires only on `DECOMPOSITION_DONE:` (fail-closed, D-N10) | **RIPRODOTTA** |
| Gate `DECOMPOSITION_OK:` / `DECOMPOSITION_GAPS:` | `workflow` edges `[next-check, next-remediation, needs_remediation]` + `[next-remediation, next-check]`; `DECOMPOSITION_OK:` has no outgoing edge = terminal | **RIPRODOTTA** |
| Checker is read-only but runs the verification scripts | harness `[Read, Grep, Glob, Bash]`; prompt forbids writing through Bash | **RIPRODOTTA** (D-N5) |
| Immediately working waves | every bug task names a regression test; every wave ends with a final-integration task (build, tests, lint, e2e, README sweep, `docs/waves/<w>-acceptance.md`); single writer per file per phase | **RIPRODOTTA** as decomposition rules the checker enforces |

### 2.4 The wave (Passata 2)

Not in this document. `run-next.ps1 -Launch` / `-LaunchOnly` records the
HITL of the previous wave, runs `check_slice_prereqs.py --slice <w>` and
calls `./run.ps1 -Max -Slice <w> -o execution-fanout` (raffa-process.yaml,
PROCESS.md §2.4 and D13 apply unchanged).

---

## 3. Control graph

```
run-next.ps1 -Max -Todo inputs/next/<file>.md [-Wave w14] [-MaxTasks 20] [-MaxPhases 5] [-Focus "..."]
  writes reports/plan/next-run.json ; snapshot (assert_next_plan_untouched.py)

next-design (DEFAULT, workflow)
  next-intake-phase ──(on_marker CONTEXT_READY:)──▶ next-council
                                                      ├─ next-lanes (concurrent: 7 seats; uninvolved → LANE_SKIPPED:)
                                                      └─ next-council-close (group_chat 8; PASS for uninvolved; gate closes)
                                                              │
                                                              ▼
                                                     next-decomposition ──(on_marker DECOMPOSITION_DONE:)──▶ next-check ⇄ next-remediation (needs_remediation)
                                                                                 │
                                                                        DECOMPOSITION_OK:  TERMINAL

  verify (assert_next_plan_untouched.py) ; print review files + launch command
  [-Launch] → check_slice_prereqs.py --slice w14 → ./run.ps1 -Max -Slice w14 -o execution-fanout

next-from-council       : start at next-council (the operator edited <w>-requirements.md by hand)
next-from-decomposition : start at next-decomposition (the council closed on disk; the run failed outside the plan)
next-plan-close         : next-check ⇄ next-remediation only
next-intake-phase : normalize only, then review
```

Markers (line-anchored, last line): `CONTEXT_READY:` / `HALTED:` (intake),
`LANE_DRAFTS_WRITTEN:` / `LANE_SKIPPED:` (lanes), `VOTE:` (seats, incl.
`PASS`), `COUNCIL_FILES_WRITTEN:` + `COUNCIL_APPROVED:` (gate only),
`DECOMPOSITION_DONE:` (decomposer), `DECOMPOSITION_OK:` /
`DECOMPOSITION_GAPS:` (checker), `REMEDIATION_DONE:` (remediator).

---

## 4. Declared divergences

- **D-N1 — Dynamic participation is emulated.** Helix `group_chat` cannot
  add or drop participants per item. All eight seats are declared; the
  intake's roster tells each seat whether to work or to `PASS`. Cost: one
  short Opus turn per uninvolved seat per table round. Because the
  stuck-loop fingerprint list is **shared across participants**
  (`orchestration/builders.py`, `group_chat_executor.py`), consecutive
  near-identical PASS turns would trip a small `max_identical_turns`; the
  window is set to 9 (> one full table round) and PASS turns must name the
  wave, the round and a seat-specific reason.
- **D-N2 — Close-gate freshness is per run, not per phase.** The decision
  record skeleton the intake writes (and rewrites even when unchanged)
  already satisfies `close_requires_glob` (mtime ≥ run start). The real
  close is `close_requires_marker: COUNCIL_FILES_WRITTEN:`, which only the
  gate emits after its five checks. On a council re-entry
  (`-o next-from-council` / `next-council`) `run-next.ps1` touches
  `reports/architecture/waves/<w>.md` before the run, so an empty wave —
  where no seat rewrites the record — still closes.
- **D-N3 — No declarative parameters.** Helix has no per-run variables;
  the launcher writes `reports/plan/next-run.json` (gitignored) and passes
  the same values in the `-i` input string. Studio runs must pass the string
  by hand or accept the defaults (newest raw file, next free wave id, 20/5).
- **D-N4 — The cap is a process rule.** Neither `fan_out` nor the wave-spec
  loader caps tasks; `register_wave.py` fails closed on `max_tasks` /
  `max_phases`, and the checker fails on its exit code.
- **D-N5 — Read-only checker with Bash.** The checker needs
  `register_wave.py --check`, `check_single_writer.py` and
  `assert_next_plan_untouched.py verify`; `Bash` is granted and the prompt
  forbids writing. Helix cannot restrict Bash to read-only commands
  (PROCESS.md D5: no pre_tool matcher on command content).
- **D-N6 — HITL is the operator.** `governance.hitl` is inert. The review
  is `reports/audit/<w>-hitl.md`; the launch is a separate command;
  `-Launch` stamps the previous wave's HITL because launching the next wave
  implies its review.
- **D-N7 — No schema validator for Markdown.** Conformity of the normalized
  file, the decision record and the work items is template + checker
  prompt; the YAML wave is validated by `register_wave.py` and
  `helix validate-wavespec`.
- **D-N8 — Existing epics are never rewritten**, so a bug in an old feature
  produces a task in a **new** epic that `extends:` the old one (operator
  decision 2026-09-10). Cancelled items get a status banner only.
- **D-N11 — The working directory is the engine's anchor.** Helix binds
  the OUTPUT anchor (`transcript_session.set_output_dir`) to the run's
  working directory (`launch.py`: the explicit override, else the artifact
  folder). Close gates (`marker_guard._check_glob`) and native file tools
  resolve against it, while a Claude Code agent can still write anywhere
  with absolute paths. A Studio run with a working directory other than
  `.helix` therefore produces a complete plan on disk and a failed run.
  Mitigations: `run-next.ps1` never overrides it; the intake, decomposer and
  checker halt at once when the cwd is not the artifact folder
  (`cc-passata1-harness`); `next-from-decomposition` re-enters after a
  council that closed on disk.
- **D-N10 — Fail-closed phase edges.** `next-intake-phase → next-council`
  fires only on `CONTEXT_READY:` and `next-decomposition → next-check` only
  on `DECOMPOSITION_DONE:` (`on_marker`, whole-token, line-anchored). A
  `HALTED:`, a silent `error_max_turns` cut-off (which the SDK client reports
  as success) or a missing marker matches no edge and ends the run instead
  of feeding a phase whose input is missing; `run-next.ps1` then prints
  "no wave file". Accepted on the realization engineer's proposal.
- **D-N9 — Claude Code harness scope note.** The harness appends a
  "WORKSPACE … plain filenames" note to every prompt (`coding_agent/binding.py`);
  the kb-contract paths override it and the prompts say so.

---

## 5. Operator guide

```powershell
cd .helix
./run-next.ps1 -Check                                        # parse + refs + prompt files
./run-next.ps1 -Max -Todo inputs/next/next-waves-todo.md   # first round → w14
```

Then review, in this order: `reports/context/waves/w14-requirements.md`
(items, status today, roster, selection), `reports/architecture/waves/w14.md`
+ the ADR footers / new ADRs in `INDEX.md`, `reports/audit/w14-hitl.md`
(phases, single-writer table, queued tasks), `reports/plan/slices/w14.yaml`.
Edit what you disagree with:

- items / seats / priorities → edit the normalized file, then
  `./run-next.ps1 -Max -Wave w14 -o next-from-council`;
- a task → edit the task md, then `./run-next.ps1 -Max -Wave w14 -o next-plan-close`;
- the council closed on disk (record filled, gate approved in the transcript)
  but the run failed for another reason → `./run-next.ps1 -Max -Wave w14 -o next-from-decomposition`;
- drop a task from this wave → set its frontmatter `status: queued` and
  remove its line from `slices/w14.yaml`, then `python scripts/register_wave.py --wave w14`.

Launch: `./run-next.ps1 -LaunchOnly -Wave w14` (or `python
scripts/check_slice_prereqs.py --slice w14` + `./run.ps1 -Max -Slice w14 -o
execution-fanout`). The wave opens the PR `integration → main`
(`open_fanout_pr.py`) and writes `reports/execution/wave-close.md`. Merge,
let CI deploy `dev`, test, write the next raw file under `inputs/next/`,
repeat: `./run-next.ps1 -Max` (newest file, next id `w15`).

Re-running the same wave id converges: the intake keeps a normalized file
whose `source_sha256` matches, seats keep footers already present, the
decomposer keeps task files that still match, `register_wave.py` upserts
the same MANIFEST row. A changed raw file regenerates the normalized file
(ids of unchanged items are kept).

Operator rules that still apply: never commit on `integration` while a
wave runs; restart the Studio backend after `.env` changes; Passata 1 on
the Max login (`-Max`), never the Console API.

**From Helix Studio**: open `raffa-next-process.yaml`, pick the
orchestration, and either leave the working directory unset ("This run will
use the artifact folder") or pick `.helix` itself. Studio remembers the last
picked folder per session; a stale pick (run `f3018639` ran in
`.helix/.git.nest.bak`) makes the engine evaluate `close_requires_glob` and
the fan-out anchors outside the artifact, and the council fails with
`closed_without_required_artifact` after all its files were written. Pass
the run parameters in the input box (`wave=w14 todo=inputs/next/<file>.md
max_tasks=20 max_phases=5`) — Studio does not write `next-run.json`, so the
protect snapshot is not taken and the checker reports "no snapshot" as a
warning. The launcher is the safer path.

**Before the wave (Passata 2)**: the fan-out forks worktrees from the
**local** `main` and reads the task prompts from `.helix/reports/workitems/`
inside them, so the reviewed plan (epics, wave file, ADR footers) must be
merged to `main` and the local `main` updated before
`./run-next.ps1 -LaunchOnly -Wave <w>`.

---

## 6. Verification

See the "Verification" block at the end of this file, filled with the
verbatim output of the parse / reference check and the script self-tests
when the artifact was authored.

---

## Verification (2026-09-10)

Helix Realization Engineer, artifact `raffa-next-process.yaml` (written
this pass). Runtime re-read for this pass, cited by `file:line` below:
`contract/schema.py`, `contract/validate.py`, `orchestration/registry.py`,
`orchestration/builders.py`, `orchestration/marker_guard.py`,
`orchestration/group_chat_executor.py`, `agent/transcript_session.py`,
`agent/reasoning.py`, `coding_agent/binding.py`,
`coding_agent/agent_sdk_client.py`, `runner/launch.py`,
`runner/orchestration_runner.py`, `runner/runner.py`,
`hooks/builtins_policy.py` (Helix checkout
`C:/Users/luca.la-malfa/source/repos/helix/src/backend/helix/`).

### Parse + reference check (executed, final files)

Command, from `.helix/` (the same command `./run-next.ps1 -Check` runs,
minus `--stub-env`; `-Check` needs a `.env`, which this worktree does not
carry):

```text
"C:/Users/luca.la-malfa/source/repos/helix/src/backend/.venv/Scripts/python.exe" scripts/validate-artifact.py raffa-next-process.yaml --stub-env --helix-backend "C:/Users/luca.la-malfa/source/repos/helix/src/backend"
```

Verbatim output (stdout + stderr), exit code 0:

```text
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-product-owner' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-software-architect' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-cloud-architect' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-security-architect' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-client-architect' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-ux-ui-designer' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-delivery-manager' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
C:\Users\luca.la-malfa\source\repos\helix\src\backend\helix\contract\validate.py:167: UserWarning: participant 'next-council-gate' in group_chat topology 'next-council-close': model 'cc-opus' omits context_window_tokens; without it, context overflow will surface as a runtime error at turn ~5 in long conversations
  _warn_turn_taking_without_context_window(doc)
OK ['next-intake-phase', 'next-lane-product-owner', 'next-lane-software-architect', 'next-lane-cloud-architect', 'next-lane-security-architect', 'next-lane-client-architect', 'next-lane-ux-ui-designer', 'next-lane-delivery-manager', 'next-lanes', 'next-council-close', 'next-council', 'next-decomposition', 'next-check', 'next-remediation', 'next-design', 'next-from-council', 'next-plan-close']
advisory ADR-0103: ran
prompt files: all present
EXIT=0
```

The eight `UserWarning`s are one advisory
(`validate.py:1709-1742`, `_warn_turn_taking_without_context_window`), one
per `group_chat` participant. They are expected and left in place: on the
coding-agent path nothing reads `context_window_tokens`
(`coding_agent/binding.py:128-232` reads `models[].model` only; the Claude
Code SDK manages its own context, Helix compaction is a chat-client feature),
so a value would be a number invented to silence a warning. `./run-next.ps1
-Check` prints them every time.

Second check, the runtime loader one-liner (`HelixDocument.model_validate`
+ `validate_references`, `${ANTHROPIC_DEFAULT_OPUS_MODEL}` stubbed to
`claude-opus-5`, warnings recorded): `OK` with the same 17 orchestration ids,
exactly one `default: true` target (`next-design`), 8 advisories (the same),
no `SpecError` / `ReferenceError`. Every orchestration id `run-next.ps1`
accepts for `-o` (`next-design`, `next-from-council`, `next-plan-close`,
`next-intake-phase`, `next-council`, `next-decomposition`, `next-check`)
exists in the document.

Files as checked (sha256):
`raffa-next-process.yaml`
`b6451b7eeb8406f68e2b2d79ce9f0f06d99d5cac31d44fc79244ed4c5932a03b`,
`agents/next-intake.md`
`c23efb2f341f89d63a55665ab1da16a6d4c4ed32cfab3509885fdedf841b9061`,
`agents/next-checker.md`
`7fb46fb206e20fc2a3dd5c0925a7319f26f558bb1306a46f2dc9d28c892df43b`.

The script self-tests (`register_wave.py`, `check_single_writer.py`,
`assert_next_plan_untouched.py`) are not part of this pass; the prompts
invoke only sub-commands the scripts declare (`--next-id`, `--last-id`,
`--wave <w> [--check]`, `--slice <w>`, `snapshot|verify`, exit 2 = no
snapshot).

### Wiring facts confirmed against the runtime

- **Marker triad on the table.** `termination: on_marker` +
  `marker: "COUNCIL_APPROVED:"` + `marker_line_anchored: true` compiles
  `^COUNCIL_APPROVED:` MULTILINE over every turn (`registry.py:459-493`);
  falsy would be an upper-cased substring anywhere in a turn. The literal is
  in `agents/next-council-gate.md` only (lines 55-56, own line, last two
  lines) among the agent prompts.
- **Close gates.** `marker_guard.py:243-249` holds the terminator until the
  glob matches a file with mtime ≥ run start (`:164-194`) AND
  `COUNCIL_FILES_WRITTEN:` is in the run's assistant messages (`:236-240`);
  at close `assert_close_requirements` (`:275-296`) raises
  `closed_without_required_artifact` if either is still unmet. `**` is
  refused (`:151-160`), so the glob is `reports/architecture/waves/*.md`.
  Base = `get_output_dir()` (`transcript_session.py:166-173`), which is the
  `working_dir` bound at launch (`launch.py:1439-1454`) and defaults to the
  artifact dir — `.helix/`, the same base `raffa-process.yaml`'s
  `reports/architecture/*.md` gate ran on. No `reset_globs`: earlier waves'
  records share the folder and would be deleted at run start.
- **Edges.** `on_marker_absent` is in `MARKER_EDGE_CONDITIONS`
  (`registry.py:368`), accepts the object form and requires `marker`
  (`validate.py:617-633`); anchored, whole-token, case-insensitive by
  default (`registry.py:266-287`, `:411-448`). `needs_remediation` keys on
  `DECOMPOSITION_GAPS:` leading a line (`registry.py:290-297`). Edge text of
  a nested `sequential` is its finalized assistant text
  (`registry.py:227-258` → `transcript_session.py:219-259`), the extraction
  `raffa-process.yaml`'s `decomposition-check → needs_remediation` edge
  already runs on.
- **Loop bound.** `limits.max_iterations` bounds back-edge LAPS; the MAF
  superstep ceiling is derived as `depth × max_iterations + 1`
  (`builders.py:721-748`, `_wire_workflow_edges` `:226-345`), so 15 = 15
  check ⇄ remediation laps. A cyclic edge set without it is a `SpecError`
  (`validate.py:643-682`); `LimitsDef` caps it at 25 (`schema.py:540`).
- **Stuck-loop window.** One fingerprint list is shared by all eight
  executors (`builders.py:770-782`); the window is the last N turns across
  participants, "near-identical" = `SequenceMatcher` ratio ≥ 0.85 on the
  first 400 characters (`group_chat_executor.py:116-136`,
  `reasoning.py:80-99`). 9 > one full round of 8; the gate's votes table
  breaks any run of PASS lines.
- **Harness.** `allowed_tools`, `disallowed_tools`, `max_turns`,
  `permission_mode` are read off `harness.coding_agent`
  (`binding.py:216-219`, `:235-248`) and forwarded to
  `ClaudeAgentOptions` (`agent_sdk_client.py:235-254`). Under
  `permission_mode: acceptEdits` the SDK auto-accepts Write/Edit regardless
  of `allowed_tools`, so read-only roles carry `disallowed_tools` (gate:
  `[Write, Edit, MultiEdit, NotebookEdit, Bash]`; checker: `[Write, Edit,
  MultiEdit, NotebookEdit]`; seats: `[Bash]`). `cwd` = the artifact dir (no
  `workspace_dir`); the harness appends its WORKSPACE note
  (`binding.py:65-67`, D-N9).
- **Ids.** No duplicate across `models ∪ skills ∪ agents ∪ orchestrations`
  (`next-intake` / `next-intake-phase`, `next-check` / `next-checker`,
  `next-decomposition` / `next-decomposer`, `next-remediation` /
  `next-remediator`, `next-council` / `next-council-close` /
  `next-council-gate` are distinct). One `default: true`
  (`schema.py:1310`).

### Phase coverage

| Phase (brief §) | Helix expression | Status |
|---|---|---|
| 2.1 Intake | `next-intake-phase` `sequential` [`next-intake`]; `cc-opus` + external-coding-agent harness, `allowed_tools: [Read, Write, Edit, Grep, Glob, Bash]`, `max_turns: 150`; edge `{from: next-intake-phase, to: next-council, condition: on_marker_absent, marker: "HALTED:"}` | **RIPRODOTTA** |
| 2.1 Run parameters | `reports/plan/next-run.json` + `-i` string, no YAML expression (Helix has no per-run variables) | **DICHIARATA** (D-N3, Completeness Keeper) |
| 2.2 Lanes | seven `sequential` under `next-lanes` `concurrent` `aggregator: concat`; seats `[Read, Write, Edit, Grep, Glob]` + `disallowed_tools: [Bash]`, `max_turns: 80` | **RIPRODOTTA** |
| 2.2 Table | `next-council-close` `group_chat` (8), `round_robin`, `max_rounds: 24` = 3 × 8, marker triad, `close_requires_glob` + `close_requires_marker`, `limits.max_identical_turns: 9`; gate `[Read, Grep, Glob]` + `disallowed_tools`, `max_turns: 40` | **RIPRODOTTA** |
| 2.2 Only the necessary seats | all eight declared; uninvolved seats `VOTE: PASS` | **DICHIARATA** (D-N1) |
| 2.2 Close freshness | fresh skeleton from the intake in `next-design` (intake now re-writes it even when unchanged); `next-from-council` relies on the seats rewriting the record | **RIPRODOTTA** with D-N2 residual (empty wave on re-entry, below) |
| 2.3 Decomposition | `next-decomposition` `sequential` [`next-decomposer`] `[Read, Write, Edit, Bash, Grep, Glob]`, `max_turns: 250`; edge `{from: next-decomposition, to: next-check, condition: on_marker_absent, marker: "HALTED:"}` | **RIPRODOTTA** |
| 2.3 Cap / queue | process rule in `register_wave.py`, checker fails on its exit code | **DICHIARATA** (D-N4) |
| 2.3 Gate loop | `[next-check, next-remediation, needs_remediation]` + `[next-remediation, next-check]`, `limits: { max_steps: 300000, max_iterations: 15, run_timeout_s: 21600 }`; `DECOMPOSITION_OK:` has no edge = terminal | **RIPRODOTTA** |
| 2.3 Read-only checker with Bash | `allowed_tools: [Read, Grep, Glob, Bash]` + `disallowed_tools: [Write, Edit, MultiEdit, NotebookEdit]`; a write through Bash is prompt-forbidden only | **RIPRODOTTA** with D-N5 residual |
| 2.4 Wave | not in this document; `raffa-process.yaml` `execution-fanout` via `run-next.ps1 -Launch` / `./run.ps1 -Max -Slice <w>` | **DICHIARATA** (by design, §2.4) |
| HITL | operator (`reports/audit/<w>-hitl.md`, `gates/<w>.hitl-ok`) | **DICHIARATA** (D-N6) |
| Re-entry targets | `next-from-council` (start `next-council`), `next-plan-close` (start `next-check`, `run_timeout_s: 14400`); standalone `next-intake-phase` / `next-council` / `next-decomposition` / `next-check` | **RIPRODOTTA** |

### Prompt edits made by this pass (minimal, wiring coherence only)

Marker ownership at file level (`marker-discipline` rule 4: the sibling token
is never written outside its owner's prompt). The rule text is kept, the
literal is replaced by a reference. Line numbers are the pre-edit ones.

| File | Lines | Before → After |
|---|---|---|
| `agents/next-product-owner.md` | 46 | ``Never emit `COUNCIL_APPROVED:` or `COUNCIL_FILES_WRITTEN:`.`` → ``Never emit the two close markers of the gate (`next-council-gate` alone owns them; see `marker-discipline`).`` |
| `agents/next-software-architect.md` | 51 | same |
| `agents/next-cloud-architect.md` | 47 | same |
| `agents/next-security-architect.md` | 49 | same |
| `agents/next-client-architect.md` | 48 | same |
| `agents/next-ux-ui-designer.md` | 54 | same |
| `agents/next-delivery-manager.md` | 46 | same |
| `agents/next-decomposer.md` | 78 | ``Never emit `DECOMPOSITION_OK:` or `DECOMPOSITION_GAPS:`.`` → ``Never emit the verdict markers of the checker (`next-checker` alone owns them; see `marker-discipline`).`` |
| `agents/next-remediator.md` | 47 | same |
| `agents/next-intake.md` | 98-99 | ``Never emit `COUNCIL_APPROVED:`, `DECOMPOSITION_OK:` or any other role's marker.`` → ``Never emit another role's marker (the gate's close markers, the checker's verdict — see `marker-discipline`).`` |
| `agents/next-checker.md` | 75-77 | ``Never emit both. Never emit `COUNCIL_APPROVED:`, `REMEDIATION_DONE:` or `IMPLEMENTATION_*`.`` → ``Never emit both. Never emit another role's marker (the gate's close markers, `REMEDIATION_DONE:`, `IMPLEMENTATION_*` — see `marker-discipline`).`` (the routing sentence that follows is unchanged) |

Close-gate freshness (the runtime rule `marker_guard.py:164-194` vs a
verify-or-write producer):

| File | Lines | Change |
|---|---|---|
| `agents/next-intake.md` | 84-85 (§3) | after ``keep an existing file whose rows are already filled by a previous council`` added: ``— but `Write` it back even when its content is unchanged: the council's close gate (`close_requires_glob`) accepts only a file written in the current run (mtime ≥ run start).`` Without it a convergent re-run of the same wave keeps `waves/<w>.md` with an old mtime and the approved table closes with `closed_without_required_artifact`. |

After the edits: `COUNCIL_APPROVED:` / `COUNCIL_FILES_WRITTEN:` appear in
`agents/next-council-gate.md` only; `DECOMPOSITION_OK:` /
`DECOMPOSITION_GAPS:` in `agents/next-checker.md` only; `HALTED:` in
`agents/next-intake.md` and `agents/next-decomposer.md` (the two edge
sources) only. No skill was changed.

### Flagged to the Completeness Keeper (inert knobs, substitutions, residuals)

1. **`context_window_tokens` advisory × 8** — left undeclared on purpose (see
   above). Declare as expected output of `-Check`, not as a defect.
2. **`governance.policy.model_allowlist: [cc-opus]`** — enforced fail-closed
   at one seam: `_enforce_participant_policies`
   (`orchestration_runner.py:2182-2187` → `:5771-5804`) dispatches the
   `pre_model` built-ins for the run target's **direct** participant refs that
   are agents (`runner.py:446-452` for a mono `--agent` run). The direct
   participants of `next-design`, `next-from-council`, `next-plan-close` and
   `next-council` are orchestrations, so on those targets no leaf agent is
   gated at this seam; on `next-intake-phase` / `next-decomposition` /
   `next-check` run alone the agent is. The coding-agent bind path itself
   carries no hook bindings (`launch.py:366-382` returns `[]`). Declarative
   intent; harmless with a single declared model.
3. **`memory: { scope: session }`** — live at run level
   (`runner.py:662-678`); nothing else under `memory` is read.
4. **`governance.hooks.allow_external: false`** — enforced
   (`validate.py:1122-1166`); no `command` hook in this document, so the
   closed gate is honest.
5. **`backend.mode: harness`** — typed (`schema.py:377`), no reader in
   `coding_agent/` / `runner/` (grep): parse-only classification.
   `transport: agent-sdk` is the only local Claude path (manual §9.2).
6. **`close_requires_marker` is run-wide and substring.**
   `_marker_met` (`marker_guard.py:236-240`) calls
   `_structured_marker_found(marker)` without `line_anchored`, over ALL
   assistant messages of the run (`list_run_events(run_id)`,
   `:87-107`), not only the group chat's. Any assistant text in the run that
   contains `COUNCIL_FILES_WRITTEN:` (even mid-prose, even from the intake)
   satisfies that half; the line-anchored `^COUNCIL_APPROVED:` terminator and
   the post-run `assert_marker_in_session_transcript` (`:56-63`, also
   run-wide) remain the real close. Mitigation applied: the literals live in
   the gate prompt only; residual: the two shared skills
   (`skills/council-protocol-next.md` lines 90-91 and 102,
   `skills/marker-discipline.md` tables) fold the literals into every seat's
   composed prompt by design (the gate reads the same protocol).
7. **Helix `hooks[]` do not reach Claude Code's tools.** `pre_tool` /
   `deny_tool` bind on the in-process chat path only; a coding agent owns its
   tools (`binding.py` docstring, `launch.py:366-382`). Hence D-N5 (the
   checker's Bash cannot be restricted to read-only commands) and the use of
   `disallowed_tools` for the read-only roles.
8. **`disallowed_tools` added beyond the seat table** (gate, checker, seats)
   — wired (`binding.py:218` → `agent_sdk_client.py:248-249`); it turns the
   "read-only critic" / "seats have no Bash" design statements into enforced
   properties instead of prompt-only ones. Tool-name check: the gate prompt
   names `Write` / `Edit` / `Bash` only in its prohibition sentence; the
   checker uses `Bash` for its three scripts and names `Write` / `Edit` only
   as prohibitions; no seat prompt names `Bash`.
9. **No `tools[]` block.** Helix native tools are never bound on the
   coding-agent path; a `tools[]` nobody references would only describe a
   chat-model fallback this document does not have. If a chat-model fallback
   is ever introduced, the vocabulary degrades (`Edit` → `write_file`,
   `Read` → `read_file`, `Grep`/`Glob` → native `grep`/`glob`) and every
   prompt must be re-checked.
10. **`on_marker_absent HALTED:` is fail-open on silence.** The registry
    docstring (`registry.py:411-447`) describes this predicate as the
    fail-closed form when keyed on the SUCCESS sentinel; keyed on the failure
    sentinel it routes forward when the producer emits nothing at all — e.g.
    an `error_max_turns` cut-off, which `agent_sdk_client.py:441-452` reports
    as success. The next phase then fails on its missing input (the gate
    refuses a table without a roster; the decomposer halts on a missing
    record). A `HALTED:` on either edge ends the workflow as a normal stop
    (no outgoing edge fires; there is no halt guard for a workflow),
    `run-next.ps1` then reports the missing wave file.
11. **Freshness on `next-from-council` with an empty wave.** No seat rewrites
    `reports/architecture/waves/<w>.md` when nobody is involved, so
    `close_requires_glob` sees a stale file and the approved table closes
    with `closed_without_required_artifact`. Operator rule: re-save the
    record before re-entering (or run `next-design`, whose intake now
    re-writes it). A launcher touch in `run-next.ps1 -o next-from-council`
    would remove the manual step (launcher owner).
12. **`max_steps: 300000`** counts streamed chunks (`MaxStepsExceeded`), not
    turns — sized from `raffa-process.yaml`'s history (60000 died,
    500000/800000 in use there). `run_timeout_s` 21600 / 14400 within
    `1..86400` (`schema.py:541`).

### Proposals for the Process Architect (not applied — the brief fixes the shape)

1. **Fail-closed phase edges.** Same grammar, same anchoring, no prompt
   change (both producers already end on the positive sentinel):
   `{from: next-intake-phase, to: next-council, condition: on_marker, marker: "CONTEXT_READY:"}`
   and `{from: next-decomposition, to: next-check, condition: on_marker, marker: "DECOMPOSITION_DONE:"}`.
   A silent intake / decomposer then stops the run instead of handing an
   empty input to an Opus council.
2. **Launcher touch for `next-from-council`** (item 11 above).

### Coordinator follow-up (2026-09-10, after the engineer's pass)

Two of the engineer's proposals were applied to the final artifact:

1. **Fail-closed phase edges** (D-N10): `next-intake-phase → next-council`
   now fires on `on_marker CONTEXT_READY:` and `next-decomposition →
   next-check` on `on_marker DECOMPOSITION_DONE:` (in `next-design` and
   `next-from-council`), replacing `on_marker_absent HALTED:`.
2. **Council re-entry touch** (D-N2): `run-next.ps1` sets the mtime of
   `reports/architecture/waves/<w>.md` before `-o next-from-council` /
   `next-council`, so an empty wave still satisfies `close_requires_glob`.

Re-run of the check on the final files (venv python, `--stub-env`; the 8
`context_window_tokens` advisories are unchanged and omitted here):

```text
OK ['next-intake-phase', 'next-lane-product-owner', 'next-lane-software-architect', 'next-lane-cloud-architect', 'next-lane-security-architect', 'next-lane-client-architect', 'next-lane-ux-ui-designer', 'next-lane-delivery-manager', 'next-lanes', 'next-council-close', 'next-council', 'next-decomposition', 'next-check', 'next-remediation', 'next-design', 'next-from-council', 'next-plan-close']
advisory ADR-0103: ran
prompt files: all present
```

`./run-next.ps1 -Check` (PowerShell, real `.env`, system python): same `OK`
list, `advisory ADR-0103: SKIPPED (missing module agent_framework:
non-blocking advisories)`, `prompt files: all present`, exit 0.

Script self-tests (cwd `.helix`): `register_wave.py --next-id` → `w14`,
`--last-id` → `e13`; a synthetic `w99.yaml` (3 tasks, 2 phases) registered
(`~4.6M tokens`, MANIFEST row `previous: e13`, INDEX-next refreshed),
`--check` rejected `layer: web` (normalised only in write mode), a same-phase
`depends_on` and an unknown artifact, and caps `--max-phases 1 --max-tasks 2`;
`assert_next_plan_untouched.py snapshot/verify` clean before and after
(118 byte-locked files, 343 append-only, 12 MANIFEST rows); the synthetic
wave was removed. `run-next.ps1` parses under the PowerShell language parser.
