# ADR — Git flow on the single Raffa monorepo

- **Status**: proposed
- **Date**: 2026-09-01
- **Deciders**: delivery-manager (with reconciliation by council-close; CI→Azure auth is joint with cloud-architect + security-architect; promotion mechanics joint with the same)
- **Locked citations**:
  - Source control — "GitHub account **lucalamalfa91**. **One public** repository [`raffa`](https://github.com/lucalamalfa91/raffa) (see §2). Not four remotes."
  - Delivery — "GitHub CI/CD releases to Azure `dev` and Azure `demo`."
  - Environments — two from day one, isolated, "No production yet."
  - Code authoring — "Claude Code via Helix, for infra, backend, web, and mobile."

## Context and problem statement

The engineering brief (v1.2) locks **one public** monorepo (`lucalamalfa91/raffa`) with
domain folders `infra/`, `backend/`, `web/`, `mobile/` plus `.helix/` — explicitly **not**
four remotes and not a `workspace/<repo>/` stand-in. Brief §2.1 states the git flow is
*guidelines only*: "Do not assume a default branch, GitHub Flow, Git Flow, tags, or
Environment approvals unless the council ADR says so." Passata 2 `fan_out` creates a
worktree (branch per task) of **this one** repo; Claude Code's cwd is that worktree root,
with product files written under the four domain folders.

So the flow must: support per-folder deploy jobs without four remotes (brief §2), keep
`dev` as integration and `demo` as stakeholder-facing (isolated data), make promotion to
`demo` explicit, and give Claude Code (via Helix) an unambiguous instruction set for
branching, PRs, and protections.

## Decision drivers

- **One repo, per-folder deployment** — CI/CD must deploy each deployable folder to both
  environments using path filters / per-folder jobs, not separate remotes.
- **Explicit promotion** — `dev` → `demo` must be a deliberate act, not an accidental copy
  of every `dev` deploy (brief §2.1).
- **Claude Code executes the ADR** — branches, PRs, protections must be expressible as
  deterministic steps a coding agent follows, with a human approval gate only where the
  brief or cost safety demands it.
- **Cost / simplicity** — no production HA, no extra platform machinery beyond what the
  two-env delivery chain needs.

## Considered options

1. **Trunk-based (short-lived feature branches) + mainline `main`, protection on `main`** —
   branch-per-task off `main`, PR back to `main`; `main` auto-deploys to `dev`; tag + manual
   environment approval promotes to `demo`.
2. **Git Flow (long-lived `develop`, `release/*`, `hotfix/*`)** — heavier branching model with
   a permanent `develop` line and release branches.
3. **GitHub Flow (only `main` + feature branches, PR required)** — effectively a minimal
   trunk-based without an explicit long-lived `develop`.

## Decision outcome

**Chosen: Option 1 — trunk-based with a single protected mainline branch `main`.**

One public monorepo, branch-per-task off `main` (Passata 2 worktrees), PR required to merge
to `main`. `main` is the integration line and is auto-deployed to `dev` on merge. Promotion
to `demo` is a **tag + GitHub Environment approval** (separate `demo` environment gated by
required reviewers), which is the explicit, deliberate act the brief demands. No long-lived
`develop` branch (Git Flow rejected as overhead for a single small authoring team of Claude
Code + reviewers); no unprotected `main` (GitHub Flow must be hardened, so it collapses into
this option).

### Consequences

- **Good**: deterministic for Claude Code (branch → PR → merge → tag); per-folder path filters
  drive the right deploy job; promotion is a named, approval-gated step; single source of truth
  for the Helix run repo and the only product remote.
- **Bad**: a failed `dev` deploy from `main` is visible until reverted — mitigated by requiring
  CI green on the PR and a fast revert path (revert PR + re-deploy), not by a frozen `demo`.
- **Neutral**: tags are used only as promotion markers, not as a branching dimension.

## Pros and cons of the options

### Option 1 — Trunk-based + protected `main` (chosen)
- Good: simplest model that satisfies per-folder deploy, explicit promotion, and agent-driven flow.
- Good: one branch line reduces the merge surface for a single authoring team.
- Bad: no dedicated weekend "release train"; `demo` lags `main` only by the tag/approval gate.

### Option 2 — Git Flow
- Good: familiar ceremony, clear `develop`/`release` separation.
- Bad: long-lived branches are overhead for a single-repo, single-team V1; more steps for an agent
  to reason about; no product benefit at this scale.

### Option 3 — GitHub Flow
- Good: minimal.
- Bad: as commonly practiced it lacks an *explicit* `demo` promotion step and defaults protections
  loosely; it must be hardened (protected branch + environments) to meet the brief, at which point
  it is indistinguishable from Option 1.

## Implications for the decomposition

- Every task branch is created from `main` and merged to `main` via a required PR with CI green.
- `main` is protected: no direct push; PR required; status checks required (the per-folder CI jobs).
- `dev` deployment: trigger on merge to `main`, filtered by path (changes under `infra/`, `backend/`,
  `web/`, `mobile/`, or `.helix/plan` as appropriate) — infra and app jobs run for the touched folders.
- `demo` deployment: trigger on a **tag** (e.g. `demo-v*`) on `main` **and** a GitHub Environment
  named `demo` with required reviewers (human approval). No tag → no `demo` deploy.
- Rollback = revert PR to `main` (for `dev`) or re-tag a known-good `main` SHA (for `demo`).
- Passata 2 fan_out worktrees use branch-per-task under this same `main`; `.helix/` stays inside the
  repo and is not its own git toplevel.

## Assumptions

- (open-question OQ-DM-001) GitHub Environments with required reviewers are available on the
  `lucalamalfa91/raffa` public-repo plan; if not, promotion falls back to a protected tag + a PR
  to a `demo/*` pointer, still a manual, explicit step. Recorded in `reports/open-questions.md`.
- (open-question OQ-DM-002) "Team of reviewers" for the `demo` approval gate resolves to the
  council's product-owner + security-architect during V1; authority is council-owned.
- CI→Azure authentication mechanics are decided jointly in `ADR-ci-azure-auth.md`; this ADR only
  fixes the *flow*, not the credential method.
