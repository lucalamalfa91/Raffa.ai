# ADR-014 — Git flow on the single Contigo monorepo

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: delivery-manager (with reconciliation by council-close; CI→Azure auth is joint with cloud-architect + security-architect; promotion mechanics joint with the same)
- **Locked citations**:
  - Source control — "GitHub account **lucalamalfa91**. **One public** repository [`contigo`](https://github.com/lucalamalfa91/contigo) (see §2). Not four remotes."
  - Delivery — "GitHub CI/CD releases to Azure `dev` and Azure `demo`."
  - Environments — two from day one, isolated, "No production yet."
  - Code authoring — "Claude Code via Helix, for infra, backend, web, and mobile."

## Context and problem statement

The engineering brief (v1.2) locks **one public** monorepo (`lucalamalfa91/contigo`) with
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
  `lucalamalfa91/contigo` public-repo plan; if not, promotion falls back to a protected tag + a PR
  to a `demo/*` pointer, still a manual, explicit step. Recorded in `reports/open-questions.md`.
- (open-question OQ-DM-002) "Team of reviewers" for the `demo` approval gate resolves to the
  council's product-owner + security-architect during V1; authority is council-owned.
- CI→Azure authentication mechanics are decided jointly in `ADR-ci-azure-auth.md`; this ADR only
  fixes the *flow*, not the credential method.

## Amendment (2026-09-10, wave w14)

Item **W14-01**. Seat: delivery-manager. Everything above is unchanged and
`Status: accepted` stands — this footer **adds** the wave-base and integration
rules the body never covered. It relaxes no protection.

**1. The wave base is a reconciliation, not a fork.** The body fixes
"every task branch is created from `main`" (`:90`) but says nothing about the
Helix artefact branch, which diverges from `main` *by construction*: `.helix/`
lives in the repo and is not its own git toplevel (`:97-98`), so the process
branch carries artefacts `main` lacks while `main` carries product commits the
process branch lacks. **Neither side is a subset of the other**, so a wave base
is produced by **merging** `origin/main` into the process branch at HITL, never
by branching afresh from either. This is an operator act **before**
`register_wave.py` and before fan-out; it is not a fan-out task.

**2. `integration` is named here for the first time.** The word does not occur
in the body, yet the delivered structure is two-level — `wave/<ID>-*` task
branches → `integration` → one PR → `main` (`reports/execution/wave-close-e13.md:7,20`).
That is recorded now so it is not re-derived each wave. `integration` is a
**wave-scoped** branch: it is created for a wave, merged by one PR, and carries
no protection of its own; `main` keeps every protection the body assigns it.
The PR `integration → main` is the wave's single merge event.

**3. A wave base is proven green before its first task (W14-A1).** The body has
no such rule; w14 needs one because `origin/main` carries a mechanical
`Contigo.*` → `Raffa.*` rename that crosses the **CI ↔ cloud ↔ identity**
boundary. Before `reports/plan/gates/<w>.hitl-ok` is created the operator
verifies, on the rebased base:
(a) `rg -n "Contigo\." backend/src web/src` returns nothing;
(b) `dotnet build` and `npm test` are green;
(c) `rg -ni "contigo" .github/ infra/ scripts/ backend/scripts/` is reviewed
line by line against live Azure / HCP / **Entra**, including **both** ADR-021
schema arrays (`backend.yml:277-285` **and** `:309-317`, kept "in lockstep" by
`:274-275`), the Entra scope literals `api://contigo-<env>-api/Contigo.{Read,Write}`
(`web.yml:204-205`), and the brand-asserting checker
`scripts/check_demo_swa_config.py` with its unit tests;
(d) **one throwaway deploy to `dev` from the rebased base is green**, reaching
`backend.yml`'s "Verify schema applied (ADR-021)" step;
(e) **one interactive sign-in on deployed `dev` returns a token carrying the
expected scope.**
Points (d) and (e) are the load-bearing ones. A resource-name mismatch fails a
deploy loudly at `az … show`; a **renamed Entra scope that does not match the
app registration fails at token acquisition, in the browser, after CI is
green** — and it is invisible to every check that stops at "the build passed".
The identity-plane half of this risk is recorded in ADR-010's w14 footer.

**4. A feature wave does not edit CI YAML beyond what its wave plan names.**
If a task's diff touches `.github/workflows/**` and the wave plan did not name
that file, it is a defect, not a convenience, and the final-integration task
fails on it. For w14 the planned set is **exactly two files**: the new
`backfill-workspace-membership.yml` and a one-line guard change in
`seed-demo-fixture.yml` (ADR-016 w14 footer, clause 5).

**5. W14-01 produces no task.** Its dependency is the HITL gate itself: no
`depends_on` edge points at it, and `reports/plan/gates/<w>.hitl-ok` is not
created until clause 3 passes. If clause 3 fails, the wave does not start —
that is the whole of its enforcement.

**Not decided here** (unchanged by this footer): branch protections, the
promotion mechanism, and the CI credential method, which stay with the body,
ADR-016 and ADR-015 respectively.
