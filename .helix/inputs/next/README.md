# inputs/next — raw requirements, one file per round

Drop **one Markdown file per feedback round** here. It is the only input the
next-wave process needs besides what is already in the repo. Write it the
way you think: a todo list, notes from a demo, a bug list, a pasted email, a
pointer to a new Claude Design export. Italian or English. The process
(`next-intake`) turns it into `reports/context/waves/<wave>-requirements.md`;
it never edits this file.

Naming: `YYYY-MM-DD-<topic>.md` (e.g. `2026-09-12-demo-acme.md`). The launcher
takes the newest file by name when `-Todo` is not given.

## What helps the intake (all optional)

- **One heading per item** (`### …`), with a stable id if you have one
  (`NW-12`, `FB-03`). Ids are kept; missing ones are assigned (`W15-04`).
- **What you see / what it should be** — the observed behaviour and the
  expected one, in plain words. A screenshot path under
  `inputs/next/attachments/` if you have it.
- **Where** — the screen or the API (`Contract 360`, `POST /api/documents`).
- **Priority** — `must` / `should` / `could` (default `should`).
- **Acceptance** — how you will check it on `dev` ("second browser sees the
  workspace"). These become the story's acceptance criteria and the manual
  checklist in `docs/waves/<wave>-acceptance.md`.
- **Design** — the path of a new prototype or export under `inputs/design/`
  and, if you can, the screen or element it concerns.
- **Cancels** — say explicitly when an item makes an existing epic / task
  obsolete ("this replaces NW-51 / epic-11 F02"). The backlog is append-only;
  only an explicit cancel produces a superseded banner.

Anything else (grouping proposals, order, "do not touch" lists) is read and
respected. The first round of this process used
`2026-09-10-next-waves-todo.md` (the post-e13 inventory).

## Launch

```powershell
cd .helix
./run-next.ps1 -Check                                   # artifact valid?
./run-next.ps1 -Max -Todo inputs/next/2026-09-12-demo-acme.md   # → wave w15 (next free id)
# review reports/audit/w15-hitl.md, then
./run-next.ps1 -Launch -Wave w15                        # or ./run.ps1 -Max -Slice w15 -o execution-fanout
```
