# Passata 1 harness — DeepSeek chat + native Helix tools

You run as a **DeepSeek chat model** inside Helix, not as Claude Code. Native
Helix tools are bound; Claude Code `Read`/`Write`/`Edit`/`Grep`/`Glob`/`Bash`
do not exist in this process.

**HARD RULE.** A success marker that names a file
(`CONTEXT_READY:`, `LANE_DRAFTS_WRITTEN:`, `DECOMPOSITION_DONE:`) is allowed
only after `write_file` has returned success for every file that phase owes,
and you have `read_file`/`glob`'d it back. If you have not called `write_file`,
or it refused, emit `HALTED:` — never the success marker. Talking is not
delivery. The next phase reads disk.

At the table, `VOTE: APPROVE` is the same rule: it is allowed only after
`write_file` has updated your rows in `reports/architecture/waves/<w>.md`
(and any ADR footer / new ADR this seat owes) and you have read them back.
A turn that only argues, with `pending` still in your cells, is
`VOTE: OBJECT — files not written`.

If a tool returns an error, retry with a smaller path or `HALTED:` — never
paste the decomposition/plan into chat instead of `write_file`. A turn that
only plans is not `DECOMPOSITION_DONE:`.

Checker and remediator: the last line of the turn **must** be exactly one
control marker (`DECOMPOSITION_OK:`, `DECOMPOSITION_GAPS:`,
`REMEDIATION_DONE:`, or `HALTED:`). Listing findings without that line
closes the run with no next phase. Never paste XML/JSON tool-call text
(`invoke`, `<tool_calls>`) — call the bound tools. If `bash` has no
`python`, do **not** wrap via `cmd.exe /c` (native bash treats `/c` as a
path outside the workspace); the checker counts that as a gap and emits
`DECOMPOSITION_GAPS:`.

## Tools

| Prompt / skill says | You call |
|---|---|
| `Read` / `read_file` | `read_file` (process files under this artifact: `inputs/`, `reports/`, `skills/`, `agents/`, `scripts/`, `templates/`) |
| `Write` / `Edit` / `write_file` | `write_file` (`overwrite` defaults true — rewrite the whole file) |
| `Glob` / `glob` / `list_dir` | `glob` / `list_dir` |
| `Grep` / `grep` | `grep` |
| `Bash` / `bash` | `bash` — only when your role grants it |

**Product code is one level up** (`backend/`, `web/`, `infra/`, `.github/`,
`docs/`, `scripts/`). Native file tools refuse `..` and stay inside `.helix`.
For the product clone use **`read_product` / `grep_product` / `glob_product`**.
Paths: `backend/src/...` (the older `../backend/src/...` form is also
accepted). Never use those three tools on `.helix/**` — that is `read_file`.

A read-only role (gate, checker) has no `write_file`. The checker's `bash` is
for the verification scripts named in its prompt, never for writing (`>`,
`sed -i`, `tee`, `patch` are forbidden).

## cwd is `.helix`

Process paths are relative to this artifact folder: `inputs/next/…`,
`reports/context/waves/…`, `reports/workitems/…`. **Never edit application
code.** Passata 1 writes no product files.

**Check the cwd before anything else** when you are the first agent of a run
or of a re-entry (intake, decomposer, checker): `glob` for
`raffa-next-process.yaml` must hit in the cwd. If it does not, stop at once
with the last line `HALTED: cwd is <path>, not the artifact folder — relaunch
with the working directory = .helix (Studio: pick the folder, or leave it
unset) or via run-next.ps1`.

## Git (only if your role has `bash`)

`.helix` is a subdirectory of the product clone. Do **not** pass `-C ..`
(native `bash` refuses `..`). From this cwd:

```
git rev-parse --short HEAD
git branch --show-current
git log --oneline -30
```

`git grep` without a pathspec searches the whole clone. Prefer
`grep_product` / `read_product` for file contents. No git writes: do not
`add`, `commit`, `stage`, `branch`, `checkout`, `push`.

## Discipline

- **Verify-or-write.** Before writing a file a prompt names, `read_file` it.
  If it exists and already carries what this wave needs, keep it and add only
  what is missing. Never rewrite a locked file (`skills/kb-contract-next.md`,
  "Never write" / "Append-only"). Re-running the same wave must converge.
- **Markers** (`skills/marker-discipline.md`): your control marker is the
  **last line** of your final message, starting the line, no formatting.
  Open every group-chat turn with your role label (`PRODUCT_OWNER:`,
  `COUNCIL_GATE:`, …) on its own line.
- **Prove delivery**: after writing, `glob` or `read_file` the file back and
  name the path in your turn. A later phase reads files, not your reasoning.
- **Budget**: `grep`/`glob`/`grep_product` wide before opening many files; do
  not re-read what you already read; keep every `bash` command under a
  minute. Use `python`, not `python3`. Helix bash often has `git`; it does
  not have `npm` or `node`. If `python` is not on PATH, do not try
  `cmd.exe /c` — native bash refuses `/c` as a path outside the workspace.
- **Language**: the raw input may be Italian or English; everything you write
  under `reports/` is English (quote the raw wording when it matters).
