# AFI — structural graph for implementer and reviewer

AFI (Agentic File Index) is the typed call/import graph of the worktree.
Use it for **relationships** (who calls, who imports, blast radius, implementations).
Use Grep/Read for **text presence** and for a file path you already know.
Do not grep a symbol to find its users.

You run as Claude Code in the per-task worktree.

## Helix harness (Raffa) — skip before you fail

`CLAUDE_PLUGIN_ROOT` is unset. Helix Bash PATH has `python` and `gh`, not
`npm`/`node`. The POSIX file `…/agentic-file-index/scripts/afi` runs `npm`
and **exits 127**. Run 1d0d3c3d burned a session on that. Do not invoke it.
Do not `which npm`. Do not look for the plugin root.

**Skip immediately** (write `AFI: n/a — no SCIP-indexable source` and stop
using this skill) when any of these is true:

- the task only creates folders, README, `.gitignore`, or GitHub/Terraform scripts
- `backend/` and `web/` have no application source yet
- you would need `npm`/`node` to even start the wrapper

When source exists and you must query, PowerShell only:

```powershell
& "C:\Users\luca.la-malfa\source\repos\agentic-file-index\scripts\afi.cmd" status --json
```

If `afi.cmd` also fails: `AFI: n/a — wrapper unavailable in harness` and
use Read/Grep. Do not retry the POSIX wrapper.

## Readiness

```bash
"$AFI" status --json
```

| `readiness.state` | Action |
|---|---|
| `fresh` | Query. |
| `absent` / `stale` and `autoScanSafe: true` | Background `scan` of the trees you touch; Grep/Read meanwhile; re-query before you close. |
| `stale` and `autoScanSafe: false` | Multi-language graph. Do not bare-scan (it wipes other languages). Re-scan each lang with `--append` after the first. |
| no SCIP-indexable source yet | Record `AFI: n/a — <why>` and continue. Do not invent query output. |

`scan` provisions the container. First image build on a host is 5–15 minutes — announce it; do not ask permission. Never `env rebuild` / `env rm` unprompted (destroys the DB). `E_AFI_CONTAINER_STALE` and `E_AFI_DOCKER_UNREACHABLE` are stops.

Scan the tree you actually edit (`backend/`, `web/`, `infra/`, `mobile/`, or `workspace/<repo>/`):

```bash
"$AFI" scan <tree> --lang typescript          # first language this DB
"$AFI" scan <tree> --lang typescript --append # later languages
```

`dotnet` (C# backend) cannot run inside AFI — produce SCIP on the host and pass `--lang dotnet --scip <index>`. Terraform/`infra/` is not SCIP-indexable.

## Never guess a ref

```bash
"$AFI" query --list-functions | grep "Name"
```

Then use the printed `<file>::<lexicalPath>` exactly.

## Queries

```bash
"$AFI" query --structure-of <file>
"$AFI" query --called-by '<file>::<fn>'
"$AFI" query --calls-from '<file>::<fn>'
"$AFI" query --callers-of '<file>::<fn>'
"$AFI" query --imported-by <file>
"$AFI" query --dependents-of <file>
"$AFI" query --impact-of '<file>::<fn>'
"$AFI" query --search "identifier or description"
```

Cite raw query output in the turn. A paraphrase is not evidence.
