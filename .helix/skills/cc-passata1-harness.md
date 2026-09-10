# Claude Code harness — Passata 1 (Ask V2) on Opus

You run as **Claude Code (Opus)** inside Helix, not as a chat model with
Helix native tools. What that changes:

- **Tools**: use Claude Code's own `Read`, `Write`, `Edit`, `Grep`, `Glob`
  (and `Bash` only when your role grants it). Where a prompt or skill says
  `read_file` / `write_file` / `list_dir` / `glob` / `grep` / `bash`, read
  it as `Read` / `Write` (or `Edit`) / `Glob` / `Glob` / `Grep` / `Bash`.
  A read-only role (critic, checker) has no `Write`, `Edit` or `Bash`; do
  not try to write through a shell.
- **cwd is `.helix`** (this artifact folder). Process paths are relative to
  it: `inputs/requirements.md`, `reports/architecture/ADR-024-ask-raffa-v2.md`,
  `reports/workitems/epic-13-ask-v2/…`. Product code is one level up:
  `../backend/src/…`, `../web/src/…`, `../.github/workflows/…`. Read it, never
  edit it — Passata 1 writes **no application code**.
- **No git.** Do not commit, stage, branch or push. Passata 1 outputs are
  reviewed at HITL and committed by the operator.
- **Verify-or-write.** Before writing a file that a prompt names, `Read` it.
  If it exists and carries the requirements (cites `inputs/requirements.md`
  ids and, for design-facing content, `inputs/design/prototypes/Raffa V2
  Prototype.html` + an unpacked anchor under `raffa-v2/`), keep it and
  append only what is missing. Never rewrite a locked file
  (`skills/kb-contract-ask.md` "Never write").
- **Markers** (`skills/marker-discipline.md`) are unchanged: your control
  marker is the **last line** of your final message, starting the line, no
  formatting. Open every group-chat turn with your role label
  (`ASK_AUDITOR:`, `ASK_CRITIC:`).
- **Prove delivery**: after writing, `Glob` or `Read` the file back and name
  the path in your turn. A later phase reads files, not your reasoning.
- **Budget**: `max_turns` is set per role; read wide with `Grep`/`Glob`
  before opening many files, and do not re-read what you already read.
