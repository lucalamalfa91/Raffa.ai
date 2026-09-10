---
id: E01/F01/US01/T01
type: task
story: us-01-github-org-repo-protection
wave: R0
status: live
target_repo: raffa
# requires: [github_admin]
---

# task-01-github-org-repo-protection — Adopt `lucalamalfa91/raffa` + protect `main`

## Coding objective

Do **not** create a GitHub organization. The product remote already exists:
[`https://github.com/lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa)
(owner `lucalamalfa91`, name `raffa`, description **Raffa platform**,
**public**). That repository is the Helix run repo and the only product remote
(ADR-014: one public monorepo, not four remotes). Confirm it, keep it public,
keep the description, and ensure the
domain folders `infra/`, `backend/`, `web/`, `mobile/`, and `.helix/` exist at
the repo root. On `main` apply branch protection: require pull-request review
before merge, require status checks to pass, and disallow direct pushes and
force-pushes. Use `scripts/verify_github_repos.py` and
`scripts/apply_github_branch_protection.py` (or the GitHub REST API) so the
shape and protection are reproducible, not a manual console gesture. Confirm no
secret material (connection string, key, SAS token, PAT) is committed anywhere
in the repo.

## Parent story AC covered

- AC-1 (`lucalamalfa91/raffa` exists — user account, not a Raffa org)
- AC-2 (single **public** `raffa` monorepo with the five folders + description Raffa platform)
- AC-3 (`main` protected: PR + status checks, no direct push)

## Files to create or modify

| Path | Change |
|------|--------|
| README.md | repo bootstrap doc naming owner/`raffa` + folder layout + the GitHub URL |
| scripts/verify_github_repos.py | assert owner `lucalamalfa91`, repo `raffa`, public, description, default `main` |
| scripts/apply_github_branch_protection.py | assert + set `main` protection on that one repo |
| .gitignore | exclude secrets and local env files |
| infra/, backend/, web/, mobile/, .helix/ | ensure the five domain folders exist (placeholders ok) |

## Context the implementer needs

- **Architecture decisions in force**: ADR-014 (trunk-based, one monorepo, protected `main`). Read `.helix/reports/architecture/ADR-014-git-flow.md` — do not Glob for it.
- **Product remote**: `lucalamalfa91/raffa` — https://github.com/lucalamalfa91/raffa (public)
- **Do not touch**: no application code; no Terraform state yet; do not create a GitHub org; do not make the repo private; do not create `raffa-infra` / `raffa-backend` / `raffa-web` / `raffa-mobile` remotes.

## Exact sequence (run 1d0d3c3d burned turns — do not repeat)

Already true. Do not rediscover:

- `gh` is logged in as `lucalamalfa91`. Remote exists and is public.
- cwd = worktree root. `.helix/` is already there. Missing at root: `README.md`, `infra/`, `backend/`, `web/`, `mobile/`, `scripts/`.
- The two scripts **already exist**: `.helix/scripts/verify_github_repos.py` and `.helix/scripts/apply_github_branch_protection.py`.
- `AFI: n/a — no SCIP-indexable source`. Helix Bash has no `npm`. Do not call AFI.

Do, from the worktree root, in this order:

1. `mkdir -p scripts infra backend web mobile` and add `.gitkeep` in the four domain folders.
2. Copy the two scripts to repo-root `scripts/` (same filenames). Do not rewrite them.
3. Write `README.md`: owner `lucalamalfa91`, repo `raffa`, URL, description Raffa platform, folders `infra/ backend/ web/ mobile/ .helix/`.
4. Keep the existing root `.gitignore`; add ignores for `.env`, `*.tfstate`, secrets. Do not remove the AFI/helix lines.
5. `python scripts/verify_github_repos.py` — must exit 0. Paste stdout + exit code.
6. `python scripts/apply_github_branch_protection.py` — must exit 0. Paste stdout + exit code.
7. Confirm no committed secrets (no `.env`, PAT, connection string, key, SAS in tracked files).
8. `git add -A && git commit -m "E01/F01/US01/T01: adopt lucalamalfa91/raffa and protect main"`. Never push.

Do not: create an org; create extra remotes; rewrite `.helix/scripts/*`; Glob `reports/architecture/**/*`; invoke AFI/`npm`/`node`; push/force-push/rebase.

## Definition of done

- [ ] `python scripts/verify_github_repos.py` exits 0 and reports `lucalamalfa91/raffa` (public, description Raffa platform, default branch `main`).
- [ ] `python scripts/apply_github_branch_protection.py` exits 0 and reports `main` is protected (PR + status checks).
- [ ] The repo contains the five folders and no committed secrets (scan exits clean).

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| script | owner/repo/description + `main` protection present | `scripts/verify_github_repos.py`, `scripts/apply_github_branch_protection.py` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F01/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-01-platform-bootstrap/us-01-github-org-repo-protection/tasks/task-01-github-org-repo-protection.md
  produces: [github-org-repos, github-branch-protection]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
