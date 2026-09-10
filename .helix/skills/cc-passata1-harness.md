# Claude Code harness — Passata 1 on Opus

You run as **Claude Code (Opus)** inside Helix, not as a chat model with
Helix native tools. What that changes:

- **Tools**: use Claude Code's own `Read`, `Write`, `Edit`, `Grep`, `Glob`
  (and `Bash` only when your role grants it). Where a prompt or skill says
  `read_file` / `write_file` / `list_dir` / `glob` / `grep` / `bash`, read
  it as `Read` / `Write` (or `Edit`) / `Glob` / `Glob` / `Grep` / `Bash`.
  A read-only role (gate, checker) has no `Write` or `Edit`; the checker's
  `Bash` is for the verification scripts named in its prompt, never for
  writing (`>`, `sed -i`, `tee`, `patch` are forbidden).
- **cwd is `.helix`** (this artifact folder). Process paths are relative to
  it: `inputs/next/…`, `reports/context/waves/…`, `reports/workitems/…`.
  Product code is one level up: `../backend/src/…`, `../web/src/…`,
  `../infra/…`, `../.github/workflows/…`, `../docs/…`. Read it with
  absolute or `../` paths; **never edit it** — Passata 1 writes no
  application code. Ignore the harness "WORKSPACE" note when it tells you to
  create files with plain filenames: the kb-contract paths are the rule.
- **Check the cwd before anything else** when you are the first agent of a
  run or of a re-entry (intake, decomposer, checker): `Glob
  contigo-next-process.yaml` must find the artifact in the cwd (or `pwd` in
  Bash must end with `.helix`). If it does not — Studio was launched with
  another working directory, e.g. `.helix/.git.nest.bak` — stop at once with
  the last line `HALTED: cwd is <path>, not the artifact folder — relaunch
  with the working directory = .helix (Studio: pick the folder, or leave it
  unset) or via run-next.ps1`. The engine anchors its close gates and file
  checks to that cwd: writing to the right place with absolute paths does
  not save the run (run f3018639, 2026-09-10, lost its council on this).
- **No git writes.** Do not `add`, `commit`, `stage`, `branch`, `checkout`,
  `push`. Read-only git on the product clone is allowed and expected
  (`git -C .. log`, `diff`, `rev-parse`, `show`) when your role has `Bash`.
  Passata 1 outputs are reviewed at HITL and committed by the operator.
- **Verify-or-write.** Before writing a file a prompt names, `Read` it. If it
  exists and already carries what this wave needs, keep it and add only what
  is missing. Never rewrite a locked file (`skills/kb-contract-next.md`,
  "Never write" / "Append-only"). Re-running the process for the same wave
  must converge, not duplicate.
- **Markers** (`skills/marker-discipline.md`): your control marker is the
  **last line** of your final message, starting the line, no formatting.
  Open every group-chat turn with your role label (`PRODUCT_OWNER:`,
  `COUNCIL_GATE:`, …) on its own line.
- **Prove delivery**: after writing, `Glob` or `Read` the file back and name
  the path in your turn. A later phase reads files, not your reasoning.
- **Budget**: `max_turns` is set per role. `Grep`/`Glob` wide before opening
  many files; do not re-read what you already read; keep every `Bash`
  command under a minute. Use `python`, not `python3`. Helix Bash has
  `python`, `git` and `gh`; it does not have `npm` or `node`.
- **Language**: the raw input may be Italian or English; everything you write
  under `reports/` is English (quote the raw wording when it matters).
