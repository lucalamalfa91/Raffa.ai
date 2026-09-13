# ADR-016 — Promotion of a release from dev to demo

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: delivery-manager (+ reconciliation with cloud-architect & security-architect at close)
- **Locked citations**:
  - Environments — "Two from day one: `dev` and `demo` … Isolated from each other (data, identities, resource groups)."
  - Delivery — "GitHub CI/CD releases to Azure `dev` and Azure `demo`."
  - Brief §2.1 — "Promotion to `demo` is explicit, not an accidental copy of every `dev` deploy."
  - Brief §4 — "`dev` and `demo` must not share PostgreSQL (or equivalent) or document storage."

## Context and problem statement

`dev` is the integration environment; `demo` is the stakeholder-facing environment. Both are deployed
from the same monorepo by the same CI, but `demo` must not receive every `dev` change automatically —
promotion is a deliberate, reviewable act, and the two environments must never share a database or
document storage (brief §2.1, §4). The flow in `ADR-git-flow.md` establishes trunk-based `main` that
auto-deploys to `dev`; this ADR defines the *explicit* promotion step to `demo`.

## Decision drivers

- **Explicitness** — an uncontrolled copy of every `dev` deploy is explicitly disallowed.
- **Isolation** — promotion must not couple `dev` and `demo` data stores; it only moves code/artifacts,
  not data.
- **Human approval** — `demo` is stakeholder-facing, so a human gate (required reviewer) must be in the
  path.
- **Reproducibility for Claude Code** — the promotion must be a named, single-command step (a tag), not
  a manual re-plumb.

## Considered options

1. **Tag-triggered promotion + `demo` GitHub Environment approval** — a tag on `main` (e.g. `demo-v*`)
   plus a `demo` environment with required reviewers triggers the `demo` deploy jobs.
2. **Manual workflow_dispatch with environment selector** — a human clicks "Run" and chooses `demo`; no
   tag.
3. **Separate `demo` long-lived branch** — a `demo` branch that only receives cherry-picks/merges when
   promotion is wanted.

## Decision outcome

**Chosen: Option 1 — tag-triggered promotion gated by a `demo` GitHub Environment with required
reviewers.**

A promotion is the single act of tagging a `main` commit (`demo-v<seq>`) and approving the resulting
`demo` deploy in the GitHub Environment. No tag, no `demo` deploy. This is explicit, auditable, and maps
to one instruction Claude Code (or a human) executes. The `demo` environment runs against the `demo`
service principal and `demo` resource group only (see `ADR-ci-azure-auth.md`), so data isolation is
structural, not just convention.

### Consequences

- **Good**: promotion is a named, reviewable event; `demo` never drifts onto arbitrary `dev` commits;
  the tag is an immutable rollback point (re-tag a known-good SHA).
- **Bad**: requires discipline to tag and approve; `demo` lags `dev` by however long the approval takes
  (intended — `demo` is curated, not continuous).
- **Neutral**: tags exist only as promotion markers, not as a second long-lived branch.

## Pros and cons of the options

### Option 1 — Tag + environment approval (chosen)
- Good: explicit, auditable, isolated, cheap, minimal ceremony.
- Bad: an extra approval step; humans must review before `demo` is refreshed.

### Option 2 — Manual dispatch
- Good: simplest mechanically.
- Bad: no immutable reference to *what* was promoted (no tag/SHA link is guaranteed), weaker audit, and
  easier to promote a wrong SHA by mistake.

### Option 3 — Long-lived `demo` branch
- Good: an explicit line.
- Bad: re-introduces a second branch, cherry-pick drift, and conflict surface that trunk-based flow
  deliberately avoids; a branch is a *version* not a *when-to-promote* statement.

## Implications for the decomposition

- `dev` deploy: on merge to `main`, gated by path filters; runs under the `dev` CI identity.
- `demo` deploy: on tag `demo-v*` on `main`, gated by a `demo` GitHub Environment with required
  reviewers (human approval); runs under the `demo` CI identity and `demo` resource group.
- Promotion moves **code/artifacts only**; never copies a database, storage account, or secrets between
  environments — each environment's data plane is created/owned in its own Terraform state.
- Rollback for `demo` = re-tag a known-good `main` SHA (the environment emits which tagged release is
  live).

## Assumptions

- (open-question OQ-DM-005) Tag naming `demo-v*` and environment name `demo` are council-owned and may
  be renamed at close; the requirement is only that the *mechanism* (tag + gated environment) stays.
- (open-question OQ-DM-002) The required reviewers for the `demo` environment are product-owner +
  security-architect during V1 — assumption until the council ratifies ownership.
- No data-plane replication between `dev` and `demo` is ever performed by CI; this is a hard constraint
  of the locked isolation rule, not a policy this ADR may relax.

## Amendment (2026-09-10, wave w14)

Item **NW-58**. Seat: delivery-manager, reconciled with cloud-architect (C4)
and security-architect. Everything above is unchanged and `Status: accepted`
stands. This footer **narrows**: it adds no promotion path and relaxes no gate.

**1. w14 adds no per-environment configuration key, in either environment.**
Cloud-architect confirms the wave's Azure and Terraform delta is **zero**
(`waves/w14.md` C4). This is a property of the contract, not an accident, and
it is recorded here because it is the thing a later wave will break.

**2. The accept link is composed client-side, and that is what keeps clause 1
true.** The invite 201 returns a **site-relative** `acceptUrl`
(`/invite/accept#<token>` — site-relative per cloud-architect's C2, fragment
per security-architect's Rule C9), and the SPA resolves it against
`window.location.origin`. An **absolute** URL would force the API to learn the
SPA origin, which it does not know: `var.spa_host_name` reaches
`modules/containerapps` but feeds only the ingress CORS block (`main.tf:189`),
and `backend.yml` deploys with `az containerapp update --image` **only**
(`:196-208`) — it never sets an env var. So a new `Invitations__AcceptUrlBase`
could arrive **only** through Terraform plus an HCP apply a human confirms in
the UI (`infra.yml:104-135`, "Apply not allowed for workspaces with a VCS
connection … this job does not invoke Terraform"). That is a human-gated
barrier in the middle of a wave, introduced through a *contract* rather than
through infrastructure — which is precisely why one word in ADR-026 §D5 is a
promotion concern and not a detail.

**Pre-empting the two shortcuts.** Adding `--set-env-vars` to `backend.yml`, or
folding a value into `var.ai_gateway_model_env` (the single `dynamic "env"`
block, `main.tf:165-174`, bound by ADR-004's 2026-09-09 amendment to per-role
model ids), are both **drift**: the next HCP apply reverts anything CI sets
behind Terraform's back. The apply barrier is not a convention being defended
here; it is how this repo is wired.

**3. A future mail transport carries its own promotion ordering.** When the
deferred ACS transport is scheduled (ADR-005 w14 footer), it needs a Key Vault
secret **and** a container-app env var **and** an absolute `Web__BaseUrl`, **per
environment**, and the ordering is: HCP apply reaches `CURRENT` for that
environment → `promote-backend` → `promote-web`. Promoting code before that
environment's apply is `CURRENT` ships a **`dev` link into `demo`** — the exact
cross-environment leak the body's "code/artifacts only" rule (`:79-80`) exists
to prevent. An infra change's effect is **not observable by the wave that wrote
it**, so it is a HITL gate, never a `depends_on`.

**4. Seeds and backfills are data-plane acts and are never promoted.** The w14
`workspace_membership` rows are produced by **running the seed/backfill
workflow against each environment**, never by copying rows from `dev`. This is
the body's isolation rule applied to this wave's new deliverable, stated
because "the rows exist on `dev`" is the tempting shortcut.

**5. Two workflow files change in w14, and no more.**
`backfill-workspace-membership.yml` is **new** (operator-triggered only, never
`push`), and `seed-demo-fixture.yml`'s precondition guard (`:123-130`) gains
`workspace_membership` — without it, seeding a `demo` whose schema predates w14
fails obscurely mid-script instead of honestly at the guard. The new public
route `/invite/accept` needs **no** deploy-config change: `staticwebapp.config.json`
rewrites every non-asset path to `/index.html` and excludes only
`/config.json`, `/staticwebapp.config.json` and `/assets/*`.

**6. The standing promotion check runs on every promotion.** After
`promote-web`, take the host from its "Project deployed to" line and run
`python scripts/check_demo_swa_config.py --host <swa-host> --environment demo`
(or dispatch `demo-config-check.yml`); expect `[PASS]`
(`reports/execution/demo-v-promotion-runbook.md:8-11,99-113`). **Path warning**:
that is the **repo-root** `scripts/`, not `.helix/scripts/` — two different
directories, and the kb-contract's `scripts/` means the latter. That checker
asserts on the brand substring `raffa-<env>-api`, so it is also a rename
surface (ADR-014 w14 footer, clause 3c).

**7. Sequence for w14.** `demo-v3` (`2db5734`) is the current live promotion and
`demo-v2` was cancelled, so the next tag is **`demo-v4`**, approved as **four**
separate deployments (`demo-promotion`, `promote-infra`'s apply,
`promote-backend`'s deploy, `promote-web`'s deploy). Acceptance on **deployed
`dev`** precedes the tag — never the reverse. `seed-demo-fixture.yml` runs
against `demo` **after** `promote-backend` has applied the new schema, because
its own guard requires it. Rollback is unchanged (re-tag a known-good `main`
SHA); note that a code rollback **does not un-apply schema** — ADR-021 scripts
are forward-only and idempotent — which is safe here because w14's schema
changes are purely additive.

**Not decided here**: `workflow_dispatch` promotion stays **rejected** (the
body's Option 2; `demo-promote.yml:9-13` — "The tag is that reference; do not
add one back").

## Amendment (2026-09-10, wave w14 — post-close: the two-file set holds, and a browser test is not a CI gate)

Items **W14-01** and **NW-58**. Seat: delivery-manager, after the six post-close
verification turns (software-architect ADR-026 `:418`, cloud-architect ADR-005
`:231`, security-architect ADR-025 `:743`, client-architect ADR-012 `:189`,
ux-ui-designer ADR-020 `:451`). Everything above is unchanged and
`Status: accepted` stands. Clause numbers continue the first w14 footer, so
"ADR-016 w14 clause 9" is unambiguous. This footer **narrows**: it adds no
promotion path, relaxes no gate, and **creates no task**.

**8. The two-file set survives all six post-close turns — verified at the
source, not inferred from the transcript.** Clause 5's assertion is what the
final-integration task enforces, so a post-close decision that quietly needed a
third workflow file would make that task fail a legitimate change. It does not:

- The workflow set is **nine files** — `backend.yml`, `web.yml`, `mobile.yml`,
  `infra.yml`, `demo-promote.yml`, `demo-config-check.yml`,
  `seed-demo-fixture.yml`, `reprocess-tenant-documents.yml`,
  `seed-market-intelligence.yml`. Enumerated, not sampled.
- The three migrations need **no** workflow edit. ADR-003 `:122-123` and
  ADR-026 `:360-362` both assert the script is already in the ADR-021 arrays;
  both cite one source, so this seat checked the source: `backend.yml:277` and
  `:309` each list
  `backend/src/Raffa.Identity.Workspace/Migrations/Scripts/identity-workspace.sql`
  — the exact file all three migrations regenerate. Present in **both** arrays,
  which `:274-275` keeps "in lockstep".
- The post-close footers add **code, one config line, tests and copy** — no
  workflow, no environment key, no secret, no deploy step. Cloud-architect's
  `$0.00` delta and clause 1 stand.

So the planned set is still exactly `backfill-workspace-membership.yml` (new)
and `seed-demo-fixture.yml`'s guard, and ADR-014 w14 clause 4 (`:157-162`) is
unchanged.

**9. A browser-expressed mandatory test is an acceptance step in w14, not a CI
gate.** The two halves of the suite are not equally enforced, and no decision in
this wave says so:

- **Backend gates.** `backend.yml:72-74` runs
  `dotnet test Raffa.slnx --configuration Release --no-build` — **unfiltered**,
  so ADR-025 §H's Postgres/RLS tests (T1c, T1d, T5, T6, T13) really do gate in
  CI, as that ADR states.
- **Browser does not.** `web.yml:79-81` runs `npm test --if-present`, which
  `web/package.json:15` maps to `vitest run`. The e2e harness exists and is
  installed — `"test:e2e": "playwright test"` (`:16`), `@playwright/test` in
  `devDependencies` (`:27`), and two checked-in specs, `web/e2e/v2.spec.ts` and
  `web/e2e/day1.spec.ts` — and **no workflow invokes it**: a grep for
  `playwright|e2e|spec.ts|test:e2e` across all nine files returns nothing.

Consequence for this wave: **T15** (ADR-025 `:807-813`, "joins §H as
non-negotiable" — no browser store holds the token) has **no runner**, and
neither does client-architect's prerequisite that `v2.spec.ts` "goes red at
NW-03 without a seed" — it cannot go red anywhere but a developer's machine.
This is not an argument against either decision; both are right. It fixes where
they are proven: **a task that writes only a spec file has not delivered the
check.** Browser-expressed assertions land in the acceptance doc (clause 11),
with the spec file written alongside for the wave that wires the runner.

**10. Do not wire Playwright into CI in w14 — the obvious fix is the forbidden
one.** Recorded because it is the shortcut clause 9 invites, exactly as clause 2
pre-empts `--set-env-vars`:

- it is a **third** workflow file, which ADR-014 w14 clause 4 makes a defect,
  not a convenience, and the final-integration task fails on it;
- it needs the browser to complete an **interactive MSAL redirect against real
  Entra**, so it needs a non-interactive credential or a test identity — an
  identity-plane change owned by security-architect and **not in this wave**;
- it is already scheduled: **NW-50 is queued W18** (`waves/w14.md:513`).

A wave does not acquire a new CI capability as a side effect of a test it wanted.

**11. The acceptance doc's step list is extended by three post-close decisions.**
`waves/w14.md:508-514` fixes the deliverable
(`docs/workspace-identity-acceptance.md`, operator-runnable against deployed
`dev`, "**not** by CI") and lists N1, N2, N3, N3b, N4, N5, N8, N9, W14-A1,
W14-A2. That list predates the post-close turns and is **incomplete, not wrong**.
It gains, as numbered steps:

- **T15** — land on the accept link while signed out, initiate sign-in, then
  assert no key in `sessionStorage` or `localStorage` holds the token
  (ADR-025 `:807-813`). Its paired config change, Rule **C10a**
  (`navigateToLoginRequestUrl: false`, ADR-025 `:791`), is a one-line diff no
  compiler and no unit test can prove correct — this step is its only proof.
- **The picker empty-state sentence** — client-architect ADR-012 `:189`,
  ux-ui-designer ADR-020 `:451`. Provable only with an invitee who has not
  accepted, i.e. on deployed `dev` with the second account, and it is the only
  on-screen recovery from the dead end NW-01 itself creates.
- **The re-issue affordance**, if the mechanism lands (clause 12).

The decomposer must read this footer to build the acceptance-doc task; the
NW-58 and W14-01 rows already name ADR-016 as governing.

**12. The re-issue mechanism adds no writer, no task and no phase.**
ux-ui-designer's ADR-020 `:451` footer hands the mechanism to software-architect
and security-architect. Both rulings are already absorbed by the ordering on
disk, so the phase plan **does not move** either way:

- **DELETE-then-POST** is client-side over two endpoints ADR-026 §D5 already
  defines — no server change at all;
- **one server-side operation** lands in `InvitationsEndpointExtensions.cs`,
  which ADR-026 `:363-367` already confines to a **single** task (the one file
  in this wave that adds a line to `Program.cs`'s contended tail).

Neither touches `WorkspaceEndpointExtensions.cs`, so the four-way serialization
this seat set for that file is unaffected and **no fifth writer appears**.

**Out of scope, named so it is not "helpfully" fixed.** `backend.yml:289` errors
with "ADR-021 requires all **eight** module scripts to be checked in" while both
arrays list **nine** (`:277-285`, `:309-317`). The message is stale by one; the
check itself is correct. Repairing it in w14 would touch a third workflow file
and fail the clause 8 assertion for a cosmetic gain. It belongs to the next wave
that legitimately opens `backend.yml`.

**Not decided here**: whether NW-50 wires the e2e suite into CI in W18, and
under which identity — that is security-architect's and this seat's joint call
in the wave that schedules it, not a w14 decision.
