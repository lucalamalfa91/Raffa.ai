---
id: us-01
type: user-story
parent: feature-01
wave: R0
status: active
---

# us-01-github-org-repo-protection — Adopt `lucalamalfa91/raffa` + protect `main`

## Story

As a **platform engineer**, I want the existing **public** GitHub repository
[`lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa) to be the
single monorepo with trunk-based protected `main`, so that every later task has
one source of truth and a protected integration line to merge into.

## Acceptance criteria

- [ ] AC-1 The repository https://github.com/lucalamalfa91/raffa exists under owner **lucalamalfa91** (user account — do not create a GitHub organization named Raffa).
- [ ] AC-2 That single repository `raffa` is **public**, has folders `infra/`, `backend/`, `web/`, `mobile/`, `.helix/` (not four remotes), and description **Raffa platform**.
- [ ] AC-3 `main` is protected: no direct push; pull-request required; status checks required.
- [ ] AC-4 The repo has no committed secrets (no connection strings, keys, SAS tokens).

## Definition of done

- [ ] AC-1..AC-4 verified by scripts that assert owner/repo and branch-protection state.
- [ ] honours ADR-014 (one monorepo, protected main)

## Dependencies

| Depends on | Why |
|------------|-----|
| — | none (first task) |

## Architecture decisions in force

- ADR-014 — trunk-based, single `lucalamalfa91/raffa` monorepo, protected `main`.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Adopt `lucalamalfa91/raffa` + protect `main` | M | phase-1 |

## Council decisions carried into this story

Product remote is **https://github.com/lucalamalfa91/raffa** (owner `lucalamalfa91`, name `raffa`, **public**, description Raffa platform). No GitHub organization. Branch `main` protected with PR + status checks (ADR-014). Folder layout `infra/ backend/ web/ mobile/ .helix/`.

## Open questions

- none (locked-decisions + ADR-014 fixed).
