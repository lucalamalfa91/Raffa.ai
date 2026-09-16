# ADR-014 — Git flow on the single Raffa monorepo

- **Status**: accepted
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
`Raffa.*` → `Raffa.*` rename that crosses the **CI ↔ cloud ↔ identity**
boundary. Before `reports/plan/gates/<w>.hitl-ok` is created the operator
verifies, on the rebased base:
(a) `rg -n "Raffa\." backend/src web/src` returns nothing;
(b) `dotnet build` and `npm test` are green;
(c) `rg -ni "raffa" .github/ infra/ scripts/ backend/scripts/` is reviewed
line by line against live Azure / HCP / **Entra**, including **both** ADR-021
schema arrays (`backend.yml:277-285` **and** `:309-317`, kept "in lockstep" by
`:274-275`), the Entra scope literals `api://raffa-<env>-api/Raffa.{Read,Write}`
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

## Amendment (2026-09-13, wave w15)

Items **W15-01**, **NW-27**, **NW-05**, **NW-67**, **NW-68**. Seat:
delivery-manager, reconciled with cloud-architect (the two HARD configuration
orderings) and security-architect (NW-05 fails closed). Everything above is
unchanged, both the body and the w14 footer, and `Status: accepted` stands.
This footer **sharpens** the wave-base rule of w14 clause 1 and **extends** w14
clause 2 for a wave whose Terraform is not empty. It relaxes no protection and
changes no branch model.

**1. The base SHA is read at the gate, never quoted from a wave document.**
w14 clause 1 fixes the *act* (merge `origin/main` into the process branch) but
a wave document also names a *commit*, and a commit moves. On this wave it
already had: `reports/context/waves/w15-requirements.md` records `origin/main`
as `3c89d35` and is stamped `18:15Z`, while `../.git/refs/remotes/origin/main`
held **`6ae21b9`** — the ref advanced three times on 2026-09-13 (`6e14b39 →
adb9ef6 → 3c89d35 → 6ae21b9`, every update a fast-forward). So the rule is: the
wave base is `origin/main` **as read at the gate**, and W15-A1 records the SHA
actually merged. Two reading traps, both verified on this checkout and both
costing an operator a wrong answer rather than an error message:
`../.git/packed-refs` is stale for **every branch ref this wave touches**
(`origin/main` `3e1f359`, `integration` `333be648`, `origin/integration`
`42f1c87a`) — **the loose ref wins**; tags, being immutable, are the one thing
`packed-refs` may be trusted for.

**2. A behind-base wave's merge must leave a zero product-tree delta.** w14
clause 3 proves the base *builds*; it does not prove the base *contains* the
previous wave. Five files differed on w15's base, three of them **shorter on
the process branch** (`backend/README.md`, `web/README.md`,
`web/e2e/day1.spec.ts`) and two absent (`docs/waves/w14-acceptance.md`,
`web/e2e/invite.spec.ts`). A merge that resolves any of the three to the
process-branch side **silently reverts the previous wave's README sweep and its
e2e work**, and no build, no test and no deploy notices. The check is therefore
mechanical and exact: after the merge,
`git diff --stat origin/main..HEAD -- backend web infra .github docs scripts`
is **empty**. "The cited files resolve" proves two of five and is not enough.

**3. Green at the base commit means the test job, not the deploy.** w14 clause
3(d) asks for one green `dev` deploy. The deploy job is `needs: build`
(`backend.yml:83`), so a **red test job means the deploy never runs at all** —
the operator sees *no deploy*, which reads as "nothing happened" rather than
"the base is red". w14 closed blind to exactly this: `main` was red on two
Testcontainers fixtures, fixed afterwards in PR #97. State it directly: **a red
`main` is no `dev` deploy, and no `dev` deploy is no wave.** The
`127.0.0.1:5432 refused` class is a **fixture gap**, never a flake to re-run.

**4. `integration` is re-created from the wave base at the gate, never merged
into.** w14 clause 2 names it as wave-scoped with no protection of its own, so
re-creating it is free and reconciling it is not. It is **already diverged** on
this clone: local `integration` `8ed3af1a`, `origin/integration` `271c3ae1`,
neither an ancestor of the other by inspection. A wave that starts on a
diverged `integration` either resurrects a previous wave's commits into its PR
or loses its own. The operator also confirms no second wave is live against the
same `integration` (`reports/execution/wave-close-e13.md:84-86`).

**5. A wave that changes `infra/` has two merges to `main`, and the
infrastructure one goes first.** This extends w14 clause 2; it does not relax
it. The reason is mechanical, not stylistic: **an HCP apply is triggered by the
merge**, so a single merge event cannot satisfy an ordering of the form "the
apply is `CURRENT` before the image that reads it deploys" — and w15 carries
two such orderings, both from cloud-architect and both fail-fast:
`ConnectionStrings__Storage` on the worker, and the four `AzureAd__*` keys on
the API. So:

- **PR 1 — infrastructure only.** Opened from the wave's single `infra/`
  writer (one task, phase 1), containing **only** files under `infra/` and
  **no** application code. It is safe by construction: `backend.yml` and
  `web.yml` are path-filtered to `backend/**` / `web/**` plus their own file
  and two helper scripts (`backend.yml:16-23`, `web.yml:17-23`) — `infra/**`
  appears in **neither**, so this merge deploys no image. Every key it adds is
  either optional or unread by the running image, so the applies are
  behaviourally inert; the `demo` root's per-environment flags default `false`
  (ADR-016 w15 clause 14). The operator confirms both HCP workspaces reach
  `CURRENT` before PR 2.
- **PR 2 — the wave.** `integration → main`, still the wave's single merge
  event **for code**, exactly as w14 clause 2 says.
- It blocks nothing. PR 1 may merge while the later phases are still running:
  an infra change's effect is not observable by the wave that wrote it
  (ADR-016 w14 clause 3), so no `depends_on` edge points at it either way.
- **Fallback if the operator cannot merge mid-wave**: the single-merge path
  still works and is strictly worse — the worker crash-loops on its fail-fast
  storage key and the API answers 401 until the apply lands and Terraform rolls
  a new revision. Bounded, self-healing, and visible; expect it rather than
  treat it as an incident.

**6. w15's planned CI-YAML set is ZERO files.** w14 clause 4 applies unchanged:
an unplanned `.github/workflows/**` diff is a defect and the final-integration
task fails on it (`git diff --stat origin/main -- .github/workflows` must show
no files). Two temptations are named so they are refused rather than discovered:
repairing `backend.yml`'s stale "eight"/"six" strings (parked by ADR-016 w14 for
"the next wave that legitimately opens `backend.yml`" — w15 does not), and
repairing `reprocess-tenant-documents.yml`, which **NW-05 takes out of service**
(ADR-016 w15 clause 21). If NW-27 turns out to need a new .NET module with its
own migration script, or NW-05 a different Entra scope, the set becomes **one
named file** and that is a table decision, never a task-time discovery.

**7. W15-A1, the gate's content.** On the merged base, before
`reports/plan/gates/w15.hitl-ok`: (a) `git fetch origin` and
`git merge-base --is-ancestor origin/main HEAD` exits 0, **with the SHA written
down** (clause 1); (b) the zero product-tree delta of clause 2; (c)
`docs/waves/w14-acceptance.md` and `web/e2e/invite.spec.ts` resolve (implied by
(b), listed because the wave cites them by name); (d) `backend.yml`'s
**`build + test`** job and `web.yml`'s are green **at the base commit** (clause
3); (e) one deploy to `dev` from the base is green, reaching "Verify schema
applied (ADR-021)" (`backend.yml:305`); (f) **one interactive sign-in on
deployed `dev` returns a token carrying `api://raffa-dev-api/Raffa.Read`**
(`web.yml:204`) — carried forward from W14-A1 point 5 because **NW-05 makes
that token load-bearing for the first time**: today the SPA requests the scope
and discards it, so a mismatch is invisible now and becomes a total `dev`
outage the moment NW-05 lands. Operator prerequisites recorded beside it in
`reports/audit/w15-hitl.md`: an external mailbox the tenant has never seen
(A15-4/A15-5), the apply identity's directory rights (ADR-015 w15 clause 2),
`integration` re-created (clause 4), and the `demo-v4` decision (ADR-016 w15
clause 18).

**Not decided here** (unchanged): branch protections, the promotion mechanism
and the CI credential method stay with the body, ADR-016 and ADR-015.

## Amendment (2026-09-15, wave w16)

Item **NW-31**, plus the process question **OQ-w16-008** and this wave's order.
Seat: delivery-manager, reconciled with cloud-architect (`PASS` — zero `infra/`
delta), client-architect (`ADR-012` w16 clauses 25, 30 — two single-writer
collisions this seat's draft did not carry) and product-owner (`ADR-001` w16
clause 8). Everything above is unchanged — the body, the w14 footer and the w15
footer — and `Status: accepted` stands. **Nothing is superseded.** No branch
model changes and no protection is relaxed.

**Clause numbering restarts at 1**, which is this ADR's own convention (w14
clauses 1–5, w15 clauses 1–7, every cross-reference wave-qualified as "w14
clause 4" / "w15 clause 5"). Recorded because this seat's lane draft proposed
"continue at **8**" — carried over from ADR-016, which numbers **continuously**
and says so in its own text ("Clause numbering continues at 23"). **The two
ADRs use different conventions and the draft applied the wrong one**; a footer
numbered 8–12 here would have produced citations ("ADR-014 w16 clause 8") that
match nothing a reader can locate by wave. Corrected before promotion, not after.

**1. w16's planned CI-YAML set is exactly TWO files, and it is a file-set
change with a single writer.** w14 clause 4 applies unchanged: an unplanned
`.github/workflows/**` diff is a defect, not a convenience, and the
final-integration task fails on it. For w16 the set is
`reprocess-tenant-documents.yml` (**deleted**) and `verify-tenant-corpus.yml`
(**added**) — ADR-016 w16 clause 29. Verified on baseline `f0b3436`: that file
is the **only** one under `.github/workflows` carrying any retired identity
header (`X-Role` `:205`/`:256`, `X-Workspace-Role` `:206`/`:257`, `X-User-Id`
`:204`/`:255`, `OPERATOR_USER_ID` `:95`). **`X-Tenant-Id` is not retired** and
must survive the sweep (security S16-7). One temptation is named and refused
again: `backend.yml`'s stale "eight"/"six" strings, parked since w14 for "the
next wave that legitimately opens `backend.yml`" — **w16 does not**.

**ADR-016 w15 clause 26's inherited question is asked and answered**: w16
changes **no `infra/` file**, so nothing `dist/config.json` is built from moves
— **no forced SPA redeploy is owed, and no `workflow_dispatch` is added to
`web.yml`.** Recorded as a negative result rather than omitted, exactly as
clause 26 requires of the wave that inherits it.

**2. One PR this wave, and the two-merge shape must not be re-derived.**
w15 clause 5 splits a wave into PR 1 (infrastructure) + PR 2 (the wave) **only
when the wave changes `infra/`**. w16's `infra/` delta is **zero**, so clause 5
does not apply: the wave is a single `integration → main` PR, exactly as w14
clause 2 says. It cuts **no `demo-v*` tag** (ADR-016 w16 clause 32).

**The trap this clause exists to close**: the wave *base* carries an unapplied
`infra/` change it did not write (PR #118 — ADR-016 w16 clause 33). That apply
is an **operator action on an already-authorised ADR-005 decision**; it is
**not** a second PR, **not** a wave task, and **not** clause 5 firing. A
reviewer who sees `infra/` in the recent *log* and reaches for the two-merge
shape is reading the baseline as the wave. The assertion is a **two-dot diff** —
`git diff --stat origin/main..HEAD -- infra` is empty — never "no infra commits
in the range".

**3. The close record is wave-scoped, and it is written after the merge.**
`reports/execution/wave-close.md` is stamped `2026-09-14T02:53:30Z` — **before**
PR #103 (`e52f663`) merged `integration` into `main`, and before #111–#117 — and
it lists **9 of 11 w15 tasks as undelivered for a wave that is fully
delivered**. Its **generic filename is what made it a trap**: it is the newest
file with that name, so it reads as current forever. It cost this wave a
seven-line banner at the top of `w16-requirements.md` to neutralise, and w15's
gate stamp was never written either.

**Rule**: the close record is **`reports/execution/wave-close-<w>.md`**, never
the generic name, and it is written or refreshed **after** the
`integration → main` PR merges. For w16 that is `wave-close-w16.md`, and per
ADR-016 clause 18 it **states the promotion outcome even when there is none**.

**OQ-w16-008 is ruled, and it is the same root cause.**
`reports/plan/gates/` holds `e01…e13`, `readiness-gaps` and `w14` — **no
`w15.hitl-ok`** — while `scripts/check_slice_prereqs.py:332` requires
`<previous>.hitl-ok`. With `previous: w15`, `run.ps1 -Slice w16` **fails its
prerequisites before fan-out**. **The operator stamps it at the w16 HITL gate**
— `python scripts/check_slice_prereqs.py --record-hitl w15` — which is
**accurate, not a rubber stamp**: w15 merged as PR #103 plus #111–#117 and every
w15 area was re-verified against this checkout. **No task is created**; it
belongs in `reports/audit/w16-hitl.md` as an operator prerequisite.

**4. W16-A1, the gate's content.** Short, because the base is clean. On the wave
base, before `reports/plan/gates/w16.hitl-ok` is created:

(a) `git fetch origin`; **the base SHA is read at the gate, never quoted from a
wave document** (w15 clause 1) — expect `origin/main == helix/w16 == f0b3436`;
if it moved, merge and re-check. *This clause just proved itself twice: the SHA
this wave's own documents quoted went stale **inside one wave, between two
passes of the same council** (`ff66ee6` → `f0b3436`). Read it, never quote it.*
(b) zero product-tree delta —
`git diff --stat origin/main..HEAD -- backend web infra .github docs scripts`
is empty (w15 clause 2);
(c) `integration` **re-created** from the wave base, never merged into (w15
clause 4); no second wave live against it;
(d) `backend.yml`'s **build + test** job green **at the base commit** (w15
clause 3 — a red `main` is no `dev` deploy, and no `dev` deploy is no wave).
**Actually run it this wave**: the base is two merges newer than the green state
first cited, and PR #119 changed `DocumentQueryService.cs` *together with*
`DocumentsListCountsTests.cs` — the exact shape that produces a Testcontainers
fixture gap;
(e) `python scripts/check_slice_prereqs.py --record-hitl w15` (clause 3);
(f) `git tag -l "demo-v*"` read and **written down** (ADR-016 w16 clause 32);
(g) **the baseline's infra apply is confirmed landed on `dev`** before any
acceptance walk, and **the running image tag is checked against `main`** — a
skipped deploy behind a red test job is silent, and a throughput judgement taken
before the apply measures the previous config (ADR-016 w16 clause 33).

W15-A1's interactive-sign-in item is **not** carried forward as a gate item —
NW-05 has landed and the token path is in daily use; it becomes an acceptance
step for NW-08 instead.

**5. The wave's order, and a correction to this seat's own published skeleton.**
Binding constraints (the intake's §5 six, plus this seat's):

1. **The workflow task is independent and stands alone** — it deletes API calls,
   so nothing depends on it and it depends on nothing. Its own task, so a phase
   boundary never blocks it and **exactly one task in the wave claims
   `.github/workflows/**`** (`check_single_writer.py`).
2. **Theme A strictly before theme B** — NW-32 edits the same five services
   NW-11/NW-12/NW-21 modify; sharing a phase fails `check_single_writer.py`.
3. **Two contract tasks, never six** (ADR-012 §3) — one theme-A
   (NW-08 + NW-31), one theme-B — in **different phases**.
4. **`web/src/api/client.ts` is a one-writer-per-phase file** —
   client-architect's ADR-012 w16 clause 25. It is hand-written glue, not
   generated, and gains **three** methods (`getQuote` from NW-12;
   `getNegotiationSteps` + `putNegotiationSteps` from NW-13).
5. **NW-11 and NW-13 collide inside `web/src/routes/contracts/contract360/index.tsx`**
   (`:6`, `:20`, shared `handleUndo` `:261-269`) ⇒ **one task, or different
   phases** (ADR-012 w16 clause 30).
6. **NW-12 with or before NW-21**; **W16-01 last and first to cut**; **the
   final-integration task is alone in the last phase**.
7. **No task opens `infra/`, and no task opens a workflow other than the NW-31
   pair.**

**The correction.** This seat's lane draft recommended
`P3 = NW-11 · NW-12 · NW-13`. **That skeleton fails `check_single_writer.py`
twice** — on `client.ts` (NW-12's wrapper against NW-13's two) and on
`contract360/index.tsx` (NW-11 and NW-13 both retiring a store and both touching
`handleUndo`). **Neither collision was in the intake's constraint list**; both
came from client-architect at the table, and both are inside the one artefact
this seat owns — the wave's order. Recorded as a correction rather than quietly
re-worded, because a decomposer reads the draft.

**Corrected skeleton (a hint for the decomposer, not a ruling — ≤ 5 phases,
≤ 20 tasks; ≈ 13 tasks):**

- **P1** — NW-07 · NW-08 backend · NW-31 capabilities + tests · **NW-31 workflow**
- **P2** — NW-32 (nine services, five modules) · **contract-A** (`/api/audit`
  path **with** the `info.description` sentence that currently denies it, delete
  the `X-Role` parameter `:5239`/`:5236` and the stale `X-Workspace-Role` prose
  `:1227`, regenerate `schema.ts`)
- **P3** — NW-12 **full stack** (sole writer of the contract file *and*
  `client.ts` this phase) · NW-11 **backend** · NW-13 **backend** (table +
  migration + endpoints) · NW-21 (backend only — zero contract, zero client) ·
  W16-01
- **P4** — **one combined web task**: NW-11's and NW-13's read-back halves, sole
  writer of `contract360/index.tsx`, `client.ts`, the contract file and
  `schema.ts` (this is constraint 5 resolved as "one task")
- **P5** — final integration, alone

Every phase has **at most one** writer of the contract file, at most one of
`client.ts`, and at most one of `contract360/index.tsx`; theme A completes in
P1–P2 before theme B opens in P3.

**Not decided here** (unchanged): branch protections, the promotion mechanism
and the CI credential method stay with the body, ADR-016 and ADR-015 — and
**ADR-015 is explicitly untouched by w16**: no federated credential, no GitHub
secret, no subject claim and no Graph right changes.

## Amendment (2026-09-15, wave w17)

Item **NW-73**'s CI-YAML set, the wave's **order**, and three process residuals
that did not execute. Seat: delivery-manager, reconciled with client-architect
(ADR-012 w17 clauses 37–45 — three single-writer findings this seat's draft did
not carry), software-architect (ADR-002 w17 clause 1; ADR-029 — NW-63 needs no
migration, which **dissolves** one of this seat's own constraints), cloud-architect
(ADR-005 w17 §24 — an ordering constraint that is an acceptance rule, not a build
one) and product-owner (ADR-001 w17 clause 9). Everything above is unchanged —
the body and the w14, w15 and w16 footers — and `Status: accepted` stands.
**Nothing is superseded.** No branch model changes and no protection is relaxed.
**Clause numbering restarts at 1** (w16 clause at `:301-308`), this ADR's own
convention and the opposite of ADR-016's.

**1. The CI-YAML set is exactly ONE added file, with a single writer.** w14
clause 4 applies unchanged: an unplanned `.github/workflows/**` diff is a defect,
not a convenience, and the final-integration task fails on it. For w17 the set is
**the NW-73 console workflow (added)** and nothing else — `backend.yml`,
`web.yml`, `infra.yml`, `demo-promote.yml` and the seed/backfill jobs are
untouched. **Exactly one task in the wave claims `.github/workflows/**`**
(`check_single_writer.py`), and it is the same task that adds the console
project, because the two are one operator surface.

**2. The two-PR shape fires this wave — and "merged" is not "applied".** w15
clause 5 splits a wave that changes `infra/` into **PR 1 (infrastructure) → PR 2
(the wave)**. w16 escaped it on a zero `infra/` delta (w16 clause 2); **w17 does
not**: ADR-016 w17 clause 37 is a real four-file `infra/` change and the wave's
only one.

1. **PR 1** — the Terraform task **alone**. Merge to `main`.
2. **HCP applies it.** `infra.yml`'s apply job deliberately does not
   (`:114-136`, "Apply not allowed for workspaces with a VCS connection") —
   HCP's VCS run does. **CI green is not the role existing.**
3. **PR 2** — the rest of the wave.

⚠ **A17-S2 cannot pass before step 2.** The console will authenticate and then
fail to send. That is an **operator sequencing fact, not a task**: it is recorded
in `reports/audit/w17-hitl.md` and in the acceptance doc, or the first run reads
as a broken console. **And the w17 gate confirms TWO applies, not one** — this
wave's, and ADR-016 w16 clause 33's still-outstanding PR #118 (OQ-w17-dm-03,
unclosed one wave later because HCP state is not readable from this checkout).
This is also why ADR-016 w17 clause 40 separates the `demo` apply from the `demo`
tag: **the promotion job plans, it does not apply.**

**3. The gate stamp stops being a per-wave rediscovery.** Verified:
`reports/plan/gates/` holds `e01…e13`, `readiness-gaps`, `w14`, `w15` — **no
`w16`**. `check_slice_prereqs.py` requires `<previous>.hitl-ok` and `previous:
w16`, so **`run.ps1 -Slice w17` fails its prerequisites before fan-out**. The
remedy is unchanged and is **never a task**:
`python scripts/check_slice_prereqs.py --record-hitl w16`.

**But this is the third wave in a row** (w15 missing → stamped at the w16 gate;
w16 missing → stamped here). **A rule re-applied three times is an unowned step,
not a ruling.** Rule: the stamp for the *previous* wave is a **standing closing
line of every `reports/audit/<w>-hitl.md`**, so the next wave's gate document
carries it by construction instead of the next intake rediscovering it from a
failed prereq check.

**4. W17-A1, the gate's content.** On the wave base, before
`reports/plan/gates/w17.hitl-ok` is created:

(a) `git fetch origin`; **the base SHA is read at the gate, never quoted from a
wave document** (w15 clause 1) — expect `origin/main == helix/w17 == d3d2d24`;
if it moved, merge and re-check;
(b) `git diff --stat origin/main..HEAD -- backend web infra .github docs scripts`
empty (w15 clause 2) — **stated as a two-dot diff**, never "no infra commits in
the range" (w16 clause 2's trap);
(c) `integration` **re-created** from the wave base, never merged into;
(d) `backend.yml`'s **build + test** job green **at the base commit** (w15 clause
3 — a red `main` is no `dev` deploy, and no `dev` deploy is no wave);
(e) `python scripts/check_slice_prereqs.py --record-hitl w16` (clause 3);
(f) `git tag -l "demo-v*"` read and **written down** (ADR-016 w17 clause 40);
(g) **both** outstanding HCP applies confirmed landed — PR #118's and this
wave's — **and the running image tag checked against `main`**, because a skipped
deploy behind a red test job is silent (w16 clause 4(g), which this wave
inherits rather than closes).

**5. A correction against this seat's own w16 clause 3 — it did not execute, and
it was unenforceable as written.** w16 clause 3 ruled that the close record is
`wave-close-<w>.md`, written after the merge. **On disk `reports/execution/`
holds `wave-close-e13.md`, `wave-close-r0-a.md` and `wave-close.md` — there is no
`wave-close-w16.md`**, and `wave-close.md:1-3` is still the **w15** file stamped
`2026-09-14T02:53:30Z` listing delivered tasks as undelivered. w16 merged (PR
#129, `d3d2d24`) and the record was never written; the w17 intake needed a banner
to neutralise it (`w17-requirements.md` §1) — **exactly the cost clause 3 existed
to remove.**

**Root cause, stated against this seat's own clause**: that file is written by the
**execution engine**, not by an agent or a wave task — its shape
(`wave-close.md:1-8`: generated header, `Product repo`, `Origin`, a per-task
delivery table) is engine output. **A council rule cannot rename an engine
artefact**, so clause 3 bound nobody. Renaming it is a process/engine change and
belongs in its own PR, **never mixed into a wave PR**.

**Enforceable replacement**: the engine's generic `wave-close.md` is a **fan-out
delivery report, not the wave record**. It is **never cited as current**, and its
staleness is **expected rather than a defect to banner**. **The wave record is
`docs/waves/<w>-acceptance.md`** (product tree, written by final integration — it
exists for w14, w15 and w16) plus `reports/audit/<w>-hitl.md`. That binds
**readers**, which the process can actually enforce, instead of binding a writer
that is not an agent.

**6. `backend.yml` stays closed, and its stale counts stay parked — third
wave.** Nothing in w17 legitimately opens it. NW-71's migration regenerates
`documents-contracts.sql`, which is **already** in both arrays, and the console
needs no YAML change (ADR-016 w17 clause 36).

⚠ **Correction for the decomposer**: `w17-requirements.md` §5 constraint 9 cites
`:277`/`:309` for `documents-contracts.sql` — **those lines are
`identity-workspace.sql`**. The correct lines are **`:278`** and **`:310`**. The
claim is right and the cites are off by one; **a task that "fixes" line 277 edits
the wrong migration.**

Still stale, still parked (w16 clause 1): the arrays hold **nine** scripts while
`:289` says "all **eight** module scripts" and `:214` / `:302` both say
"**six**" — **three** wrong counts, not two. Parked again, for the next wave
that legitimately opens the file.

**7. The wave's order, and a second correction to this seat's own published
skeleton.** The intake's §5 constraints stand; these are the deltas the table
produced, three of them against this seat's draft.

1. **The Terraform task is alone and is PR 1** (clause 2). Nothing in the wave
   `depends_on` it at build time; **A17-S2 depends on its *apply*.**
2. **NW-73 console + workflow is one task** — sole claimant of
   `.github/workflows/**` *and* of the new console project. It `depends_on`
   nothing in the wave; do not chain it behind a web phase.
3. ⚠ **Constraint 9 dissolves.** Software-architect ruled NW-63 needs **no
   migration** (ADR-029: it anchors on the existing `SourcePage`/`SourceSpan`),
   so **`documents-contracts.sql` has a single writer, NW-71** — and this seat's
   draft constraint 4, which phase-separated two migrations, **is withdrawn**.
4. ⚠ **`reviewViewModel.ts` is a two-item file and the draft did not carry it**
   (client-architect, ADR-012 w17 clause 38): NW-71 at `:282`/`:294` and NW-64 at
   `:244`. Constraint 1 *orders* them, but **order is not a phase** and
   `check_single_writer.py` rejects on the file. **Two phases, or one task** —
   and the same holds for `ReviewFieldList.tsx` and `reviewViewModel.test.ts`.
5. ⚠ **NW-66 after NW-63** (client-architect): NW-66's row links to NW-63's
   route, and e2e **N20** asserts the link **lands** — which needs the route
   registered, not merely ADR-decided. The draft put both in P4; **they are now
   phase-separated.**
6. **NW-65 + NW-66 are one task** — both write `contract360ViewModel.ts`
   (constraint 3, whose other contender NW-62 is phase-separated). NW-71 **need
   not open that file at all** (ADR-012 clause 36), and NW-20 gets **no client
   task** (clause 45), so the contender set is exactly three.
7. ⚠ **`FactTable.tsx` is claimed by no task** — reached independently by this
   seat, client-architect and ux-ui-designer. **It needs an owner before
   fan-out** (it belongs with NW-65 + NW-66) or NW-65's officialized gate ships
   on part of screen 5.
8. **Final integration alone in the last phase.**

**An acceptance-ordering constraint that is not a build dependency**
(cloud-architect, ADR-005 w17 §24): **w17 ships a page renderer and a bulk
whole-tenant re-render trigger in the same wave**, so NW-26's **20-file
measurement on `dev` runs BEFORE the first whole-tenant reprocess**, never after.
A bulk run is not a substitute for it. This constrains the **acceptance walk**,
not the phase graph, and belongs in `reports/audit/w17-hitl.md`.

**Corrected skeleton (a hint for the decomposer, not a ruling — ≤ 5 phases,
≤ 20 tasks; ≈ 14, leaving headroom if NW-63 splits under OQ-w17-001):**

- **P1** — **NW-73 Terraform (alone → PR 1)** · **NW-26** (ADR-029: the renderer,
  `?page=n`, `pageCount`) · NW-20 backend · NW-22 backend
- **P2** — **NW-73 console + workflow** · NW-71 server rule + migration (sole
  writer of `documents-contracts.sql`) · **NW-72 full-stack** (contract-A folds
  in: sole writer of `raffa-api.v1.json` this phase)
- **P3** — NW-71 web (sole writer of `semantics.ts`, `reviewViewModel.ts`) ·
  NW-62 web (sole writer of `contract360ViewModel.ts`) · **NW-63 viewer web**
  (sole writer of `client.ts`; **registers the route**) · **contract-B**
- **P4** — **NW-65 + NW-66 combined** (sole writer of `contract360ViewModel.ts`;
  **owns `FactTable.tsx`**) · NW-64 (sole writer of `reviewViewModel.ts` this
  phase)
- **P5** — final integration, alone

**Two contract tasks, not five** (§5 constraint 6). Every phase has at most one
writer of the contract file, of `client.ts`, of `semantics.ts`, of
`reviewViewModel.ts`, of `contract360ViewModel.ts` and of
`documents-contracts.sql`. NW-26 (P1) precedes NW-63 (P3); NW-71 web (P3)
precedes NW-64/65/66 (P4); NW-63 (P3) precedes NW-66 (P4) — **each satisfied by
phase rather than by hope.**

**Not decided here** (unchanged): branch protections, the promotion mechanism and
the CI credential method stay with the body, ADR-016 and ADR-015 — and **ADR-015
is explicitly untouched by w17**: the new right is an Azure **data-plane role**
on an existing principal, not a federated credential, a GitHub secret, a subject
claim or a Graph right.

### Round 3 (2026-09-15) — clauses 8–9

Clauses 1–7 above are unchanged and byte-identical. No branch model changes, no
protection is relaxed, nothing is superseded, and **ADR-015 stays `none`** for a
third time — re-verified against every round-3 ruling: no federated credential,
GitHub secret, subject claim or Graph right is touched by a queued VCS run, a
narrowed `workflow_dispatch` input, or a reap of surplus blobs.

**8. W17-A1 gains one line, and clause 2's sequencing fact is promoted from a
note to a gate.**

- **Clause 4 gains (h)**: the **`raffa-dev` HCP VCS apply of ADR-016 w17 clause
  37 confirmed landed in the HCP UI**, and confirmed **before the NW-73 workflow
  is dispatched even once**. The reason is ADR-016 w17 round-3 clause 44 and is
  not repeated here: under security-architect's ADR-011 clause 26 a dispatch
  against a missing Send grant **deletes and commits chunks before the publish
  that fails**, requeues nothing and writes no audit row — so clause 2's *"the
  console will authenticate and then fail to send"* describes the wrong event.
  ⚠ **That is a correction against this seat's own clause 2**, recorded there and
  enforced here.
- ⚠ **Clause 4(g) is corrected in count and in kind.** It reads "**both**
  outstanding HCP applies confirmed landed — PR #118's and this wave's". After
  ADR-016 round-3 clause 43 the wave's own PR 1 merge queues **two** runs, on
  `raffa-dev` **and** `raffa-demo`, so the gate confirms **three runs across two
  workspaces**, not two applies. **They are not interchangeable and must be
  written down separately**: the `raffa-dev` run is a **destruction guard**
  (clause 44, blocks the first dispatch); the `raffa-demo` run is a **promotion
  precondition** (clause 43, blocks the tag); PR #118's is an **inherited
  unknown** (OQ-w17-dm-03, unclosed one wave later). A single tick against "the
  applies" satisfies none of the three.
- Clause 4(a)–(f) are unchanged. The `--record-hitl w16` stamp (clause 3) and its
  standing-closing-line rule stand as written.

**9. The phase graph is UNCHANGED by round 3 — stated rather than assumed,
because five seats appended footers to it.**

Round 3 produced ADR-029 clauses 1–2 (software-architect), ADR-005 §27 and
ADR-007 §9 (cloud-architect), ADR-009 clause 8, ADR-011 clause 26 and ADR-022
clause 6 (security-architect), ADR-012 §46–§48 with ADR-018 clause 15
(client-architect), and ADR-020 §24–§26 (ux-ui-designer). **None moves a phase or
adds a writer.** Checked against clause 7's skeleton one by one, because a
decomposer reading five footers will otherwise re-derive the graph:

- **ADR-029 clause 2**'s reap of `n > pageCount` is a **DoD line on NW-26** (P1),
  in the stage that already writes those keys; it adds no file and no task.
- **ADR-012 §47**'s 404 repair is explicitly *inside NW-63's existing `client.ts`
  edit* — `client.ts` keeps **one writer in P3**, as clause 7's skeleton has it.
- **ADR-018 clause 15** adds **no route** and **ADR-020 §24** adds **no export and
  no component** — both seats say so in their own words, so NW-63 gains no
  dependency and still does not stall on a Claude Design round-trip.
- **ADR-011 clause 26** and **ADR-022 clause 6** are DoD lines on the **single**
  NW-73 task, which already claims both the console project and
  `.github/workflows/**` (clause 1). The CI-YAML set is **still exactly one added
  file**.
- **ADR-012 §48** rules **no client task** for NW-62, so constraint 3's contender
  set for `contract360ViewModel.ts` stays at three and clause 7.6 is unaffected.
- **ADR-009 clause 8** binds the reap's key prefix — backend, inside NW-26.

⚠ **One new-file writer question the round did open, and it is already answered
by phase.** Client-architect's §48 puts two e2e checks in **`w17-viewer.spec.ts`**
— verified: that file **does not exist on this base** (`web/e2e/` holds
`day1.spec.ts`, `invite.spec.ts`, `v2.spec.ts`), so it is a new file — while
NW-66's **N20** asserts its deep link lands. Clause 7.5 already phase-separates
NW-63 (P3) from NW-66 (P4), so `check_single_writer.py` is satisfied **by phase**.
Recorded because the tempting repair is to **merge NW-63 and NW-66 into one task
so they can share the spec file** — which would re-collide
`contract360ViewModel.ts` against clause 7.6 and undo the separation clause 7.5
was written to create. **Two tasks, two phases, one spec file created in P3 and
extended in P4.**

**`FactTable.tsx` is still the one finding of this table that no ADR closes**
(clause 7.7): it belongs with NW-65 + NW-66 and **needs an owner before
fan-out**, or NW-65's gate ships on part of screen 5. Round 3 changed nothing
about it, and no seat claimed it.
