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

## Amendment (2026-09-13, wave w15)

Items **NW-27**, **NW-67**, **NW-68**, **NW-05**, **NW-58r**, **W15-01**. Seat:
delivery-manager, reconciled with cloud-architect (the resources, the two HARD
orderings, the revision-state finding) and security-architect (fail-closed on
NW-05, no passcode seam). Everything above is unchanged and `Status: accepted`
stands; both w14 footers are intact. Clause numbering **continues at 13**.
This footer adds no promotion path and relaxes no gate — it adds the ordering a
wave with real Terraform needs.

**13. w15 is the wave that breaks clause 1, and it was written to be broken.**
w14 recorded a **zero** Terraform delta "because it is the thing a later wave
will break" (clause 1). w15 changes `infra/` for three features at once —
Service Bus wiring and worker scaling (NW-27), `infra/modules/communication`
with `acs-connection` and four `Invitations__*` keys (NW-68), the Graph
app-role assignment (NW-67) — plus four `AzureAd__*` keys for NW-05. It is
therefore the **first wave to add a per-environment API configuration key**,
exactly the case clause 2 and clause 3 predicted. Clause 2's barrier is
unchanged and is now load-bearing: `backend.yml` deploys `az containerapp
update --image` **only** (`:196-208`), so a new environment variable can arrive
**only** through Terraform plus an HCP apply a human confirms.

**14. One infrastructure PR, merged before the wave PR; `demo` is flag-gated
at that merge.** The Terraform is **one task and one PR** owning all of
`infra/**` — mechanically forced (`check_single_writer.py` fails a slice whose
same-phase tasks claim one file) and also correct: one apply, one operator
confirmation, one blast radius. It merges to `main` **ahead of** the wave PR
(ADR-014 w15 clause 5), which is the only way to satisfy the two HARD
orderings cloud-architect measured — `ConnectionStrings__Storage` on the worker
and `AzureAd__*` on the API are both fail-fast, and an apply triggered *by* the
merge cannot precede the image that merge deploys.

The consequence w14's zero-delta rule hid must be stated plainly: **both HCP
workspaces are VCS-connected to this repo with `trigger-prefixes: infra/`**
(`infra/README.md:8-12`), so merging Terraform to `main` moves the
**stakeholder-facing** environment's infrastructure while it is still serving
the previous wave's images. The control is the per-environment flag, already an
established pattern here (`ai_gateway_wired`, `infra/README.md:193-199`,
`:276-280`):

| Key / resource | `dev` root | `demo` root at the infra merge | Flipped when |
|---|---|---|---|
| `Invitations__Mail__*` (ACS) | `true` | **`false`** | the one-line PR **before** the `demo-v*` tag |
| `guest_provisioning_enabled` (Graph) | `true` | **`false`** | same PR |
| `ServiceBus__*`, `ConnectionStrings__Storage` (worker) | applied | applied (inert — the previous image does not read them) | n/a |
| `AzureAd__*` (API) | applied | applied (inert — the previous image does not read them) | n/a |

The flip is itself Terraform and therefore a second merge, reviewable in
seconds — the same way `ai_gateway_wired` is flipped "with the promotion that
carries the live-Foundry backend".

**15. Required keys, optional keys, and the shape that is forbidden.** Every
new configuration key ships with a documented absent-value behaviour, and **no
key may fail open** — an absent value must never leave the old, weaker path
working, because that is the one failure mode nothing detects.

- **Optional (fail-soft)** — all five `ServiceBus__*` and all
  `Invitations__*`: absent means today's shipped behaviour (the in-process
  queue; `mailDelivered: false` with the copyable link; provisioning off). This
  is why the optional binding shape is *required*, not preferred.
- **Fail-fast** — `ConnectionStrings__Storage` on the worker: absent means the
  worker refuses to start, mirroring `Raffa.Api/Program.cs:66-69`. Correct here
  because the soft alternative is worse: a worker that cannot read the blob it
  is asked to OCR marks **every** document `Failed`, and A15-2 passes while
  every row is terminal-`Failed`. Its control is clause 14's ordering plus
  clause 17's assertion.
- **Fail-closed, never crash-closed** — `AzureAd__*` on the API: with the keys
  absent the authenticated routes answer **401** and **no header fallback is
  re-added**, not even "temporarily" (security-architect co-signs; the forged
  header is the vulnerability NW-05 exists to remove). The API must still
  **boot**: a boot-crash loop costs the wave its only acceptance environment and
  is indistinguishable from a bad image, whereas a 401 window self-heals the
  moment Terraform rolls the revision. Clause 14's ordering makes the window
  unreachable in the normal path; this rule makes the abnormal path survivable.
- **No `Authentication__Enabled` kill-switch, in any environment** —
  cloud-architect's ruling, co-signed by security-architect and by this seat on
  delivery grounds: flipping it back needs another operator-confirmed HCP apply,
  the slowest rollback in the repo, while reverting the image SHA is one
  `az containerapp update --image` and no apply.

**16. `Invitations__AcceptUrlBase` composes from `var.spa_host_name`, and three
shortcuts stay drift.** The variable already reaches `modules/containerapps`
and feeds only the ingress CORS block (`main.tf:189`), and each environment
root already wires its own `module.staticwebapp.default_host_name` — so the key
is a **reuse of an existing wire**, needs no new value a human types, and makes
it structurally impossible to point `demo` at `dev`'s host. Never hardcode a
hostname in either root: that is the cross-environment leak the body's `:79-80`
exists to prevent, and here it would mail a `dev` link into a `demo`
invitation — an email you cannot recall. It is never derived from a request
header either (security-architect's control). The forbidden shortcuts are now
three: `--set-env-vars` in `backend.yml` (clause 2; zero occurrences across all
ten workflow files today), folding a value into the single `dynamic "env"`
block (scoped to `AiGateway__*` by its own declaration), and — new this wave —
adding a `workflow_dispatch` to `infra.yml` to "apply on demand": it has no
dispatch trigger (`:9-25`) and adding one would apply nothing, because the CLI
apply is *rejected* by HCP (`:106-109`). An out-of-band apply is an HCP UI "New
run", which is an operator act, not a workflow.

**17. A green `backend.yml` run does not prove the worker started.** The deploy
step is `az containerapp update --image` with **no** revision-state,
`runningState` or health assertion anywhere in the workflow, so a worker that
crash-loops or never activates produces a **green** run: the API keeps serving,
uploads keep returning 201, and documents simply never leave `Uploaded` — a
queue backing up behind a dead consumer looks exactly like slow processing
(cloud-architect's finding, adopted). Three independent Terraform gaps produce
that identical symptom today: the topic has **no subscription** (a message with
zero subscriptions is accepted and discarded), the worker has `min_replicas =
0`, no ingress and **no scale rule anywhere in `infra/`**, and Service Bus has
**no role assignment**. Therefore the promotion gate gains one **post-deploy
revision-state assertion** per environment, run by the operator, not by CI: the
worker's active revision is running and healthy, the subscription exists, the
scale rule (or its accepted fallback) is present, and both topic-scoped role
assignments are there. **"The HCP run is `CURRENT`" proves none of them** — an
apply that creates none of them still succeeds. **A15-2 (restart API and worker
mid-batch, every row terminal) is the acceptance step that proves the whole
path, and it is walked on deployed `dev` with the worker scaled the way `demo`
will run it — never in CI**, which has no Service Bus, no worker and no
Playwright.

**18. w14 was never promoted, and the promotion this wave inherits is two
waves wide.** Read from `../.git/packed-refs` (trustworthy for tags, which are
immutable): the complete `demo-v*` set is **`demo-v1 a4e564c`, `demo-v2
22c474f`, `demo-v3 2db5734`** — there is **no `demo-v4`**. Clause 7 planned it;
it was not cut. So `demo` has been running `demo-v3` since **2026-09-04**, and
nothing on this tree says so, because no `wave-close-w14.md` was written.
**Ruling: `demo-v4` is cut on the current `main` at the w15 gate, before the
wave starts**, and w15 promotes as `demo-v5` — which is also the tag
cloud-architect's runbook names, and it is correct **only** because `demo-v4`
is cut first. Grounds: it restores the invariant this ADR exists to protect
(`:41-48` — one promotion is one reviewable wave), it costs no extra
verification because W15-A1 point (d) already proves that commit green, and it
makes a w15 rollback land on **w14** rather than on 2026-09-04. The number is
**read** at the gate (`git tag -l "demo-v*"`, runbook `:71-77`), never assumed.
If the operator declines, the bundled promotion is the fallback and clause 19
step 9 is the honest version of it. Either way `demo` owes three data-plane
steps that belong to the **previous** wave and have never run — they are w14's
work, not w15's, and the wave-close must record them so W16 does not inherit
them a third time. **A wave that closes without a promotion must say so in its
close report**; that omission is what made this invisible.

**19. The w15 promotion sequence.**

1. **PR 1 (infrastructure only) → `main`** (ADR-014 w15 clause 5). `infra.yml`
   runs fmt/validate/plan; `backend.yml` and `web.yml` do **not** fire —
   `infra/**` is in neither path filter (`backend.yml:16-23`, `web.yml:17-23`).
2. **Confirm `raffa-dev` reaches `CURRENT`** in HCP (`infra.yml:132` prints the
   pointer), then confirm the API and worker container apps carry the new keys.
   **Confirm the `raffa-demo` run state explicitly** — whether or not it
   auto-ran — and that its flags are `false`. If the Graph assignment errored,
   stop: `guest_provisioning_enabled` stays `false` and NW-67 degrades (ADR-015
   w15 clause 4); Service Bus and ACS must still have applied.
3. **PR 2 (`integration → main`) → `main`.** `backend.yml` and `web.yml` now
   run in parallel; **nothing orders them, and nothing needs to** — see clause
   20. Expect a bounded window in which authenticated routes fail while the two
   deploys converge. This is designed behaviour, not an incident.
4. **Run `docs/waves/w15-acceptance.md` on deployed `dev`** once *both* deploys
   are green: A15-1, A15-2 (with clause 17's assertion), A15-3, A15-4, A15-5,
   A15-6, A15-7, A15-8 (**both** halves — a forged header with no token returns
   401, **and** an interactive sign-in returns 200 on a real tenant-scoped
   route), N3b by hand, and the NW-05 rollback rehearsal (revert PR → `dev`
   redeploys the previous image; the env vars stay and are harmless).
5. **The `demo` flag-flip PR** — one line per key on the `demo` root
   (`invitation_mail_enabled = true`, and the guest-provisioning flag if the
   table gates it per environment). Merge, then confirm `raffa-demo` is
   `CURRENT`; if that workspace does not start its own run, an HCP UI "New run"
   (`infra/README.md:88-91`). **This step is new to w15 and it is the one
   people will skip.**
6. **Tag and approve.** `demo-v5` (clause 18), then four separate approvals —
   `demo-promotion`, `promote-infra`, `promote-backend`, `promote-web`
   (`demo-promote.yml:98-157`). **`promote-backend` must run after step 5's
   apply is `CURRENT`**, or `demo` gets w15's code against a pre-w15
   environment: mail on in the code and off in the config, or an absent
   `Invitations__AcceptUrlBase`.
7. **Standing check** — `python scripts/check_demo_swa_config.py --host
   <swa-host> --environment demo`, or dispatch `demo-config-check.yml`; expect
   `[PASS]`. That is the **repo-root** `scripts/`, not `.helix/scripts/`.
8. **Clause 17's revision-state assertion on `demo`**, then the invitation walk
   (A15-4 / A15-6 / A15-7) with a **second** external address: an invitation
   email is the one w15 deliverable that leaves the building, and a `dev`-only
   pass proves nothing about `demo`'s sender domain, flag or accept-URL base.
9. **The w14 catch-up `demo` never got** (clause 18), in this order because
   each depends on the one before: confirm `promote-backend`'s "Verify schema
   applied (ADR-021)" is green (it applies w14's **and** w15's schema in one
   pass); dispatch `seed-demo-fixture.yml` — its guard requires
   `workspace_membership`, which only exists after that apply; dispatch
   `backfill-workspace-membership.yml` for every workspace created on `demo`
   before w14 (its verification fails if zero rows land, so a no-op is visible);
   then walk `docs/waves/w14-acceptance.md` N1–N9 and W14-A2.
10. **Rollback** is unchanged — re-tag a known-good `main` SHA. Two w15-specific
    facts: a code rollback **does not un-apply** Terraform (the keys, the ACS
    resource and the Graph assignment survive, and are inert to the old image),
    and the migration is **purely additive** (three nullable columns on
    `document`, three on `extraction_job`, `Rejected` needs no DDL — OQ-w15-012),
    so the previous image runs unchanged against the new schema and there is no
    schema rollback to perform.

**20. The web/backend deploy order is not enforced, and enforcing it would not
help.** Three seats asked for "web deploys before backend" on NW-05. The
mechanism does not exist: `backend.yml` and `web.yml` are separate workflows
triggered by the same push with independent path filters, there is no
cross-workflow dependency, and creating one would gate **every** future wave's
backend deploy on the web build — a permanent cost for a one-wave concern, and
a CI-YAML change in a wave whose planned set is zero (ADR-014 w15 clause 6).
And it would not buy what it is meant to buy: **the window is symmetric**. It
opens at the first deploy and closes at the second whichever lands first,
because after NW-05 the SPA sends only `Authorization` and the API accepts only
`Authorization` — an old SPA against the new API is 401 everywhere, and a new
SPA against the old API is *also* locked out at workspace discovery
(`WorkspaceEndpointExtensions.cs:68-71` 401s with no `X-User-Id`). The
asymmetry that does exist argues the other way: a **new SPA against the old
API** still reaches the nine service paths that default the actor to
`"unattributed"` (`DocumentsEndpointExtensions.cs:569-573` never rejects), so
that ordering can land **writes attributed to nobody** — the exact rows NW-32
exists to delete — while the old-SPA-against-new-API direction produces clean
401s and no writes. So: no CI ordering is added, the window is named and
bounded, the backend-first direction is the *cleaner* of the two if the operator
has any influence at all, and **the acceptance walk starts only when both
deploys are green** (clause 19 step 4). The recorded rename risk stands:
`web.yml:201-205` hardcodes `api://raffa-<env>-api/Raffa.Read|Write`, which must
stay equal to `api_identifier_uri` and its two scope values — a mismatch fails
**at token acquisition in the browser, after CI is green**, which is why
W15-A1 point (f) exists.

**21. `reprocess-tenant-documents.yml` goes out of service at the w15 code
merge, and w15 neither repairs it nor edits it.** NW-05 removes `X-User-Id` as
an identity input, so the job's first authenticated call — `GET /api/documents`
(`:202-207`) — returns **401** and the step exits 1 with `::error::`
(`:208-211`) **before any write**: no partial reprocess, no unattributed audit
rows, no half-run tenant. The file already predicted it (`:48`, "When ADR-010's
API JWT lands, both headers go away together with `X-Tenant-Id`"). It is **not**
repaired in w15: a deploy-time credential that can call the API under the token
regime is an identity-plane change owned by security-architect, and clause 10's
rule holds — *a wave does not acquire a new CI capability as a side effect*. It
is **not** edited either: **NW-31 owns that file and is queued W16**, together
with the `X-Role` / `X-Workspace-Role` removal, so one wave opens it once. The
outage is recorded as a row in `docs/waves/w15-acceptance.md`'s known-gaps
table, and the repair (a token-bearing operator identity, or an operator job
that enqueues on the worker side now that NW-27 makes reprocess asynchronous)
is W16's to design.

**22. What the final-integration task must run.** Build and test both trees
(`backend.yml:72-74` is unfiltered, so the Postgres/Testcontainers suites gate
here); `terraform fmt -check -recursive` and `validate` for **both** roots —
the first wave in a while where that can actually fail; write
**`docs/waves/w15-acceptance.md`** following `docs/waves/w14-acceptance.md`'s
shape exactly (a `>` blockquote per item naming its id/ADR/task, **Click
path** → **Pass when:** → `curl` with exact status codes → `psql` where only
SQL can prove it → **Automated:** naming test classes, and a closing **known
gaps** table — w15 closes w14's gaps 1, 2 and 4, inherits 3, 5, 6 and 7, and
adds clause 21's); a README sweep wider than w14's (`backend/README.md`'s
interim-header section, `web/README.md`'s processing states and server-backed
badge, and **`infra/README.md`** — the new `communication` module in **both**
layout trees, the Service Bus wiring and worker scaler, the Graph permission and
which identity holds it, the new per-environment flags beside `ai_gateway_wired`,
and both "Known gaps" sections); the **no-CI-drift assertion** of ADR-014 w15
clause 6; and the PR `integration → main`. It does **not** cut a `demo-v*` tag —
promotion is a separate operator act after clause 19.

**Not decided here** (unchanged): the queue contract and the message shape
(ADR-027), the resources and their SKUs (ADR-005 / ADR-007), the Graph
permission's scope and the credential posture (ADR-025 §J / ADR-011), and when
NW-50 wires a Playwright runner — still W18, still joint with
security-architect.

## Amendment (2026-09-14, wave w15 — re-entry round: the revision-state assertion is reworded, and the infrastructure plan gains two assertions)

Items **NW-27**, **NW-05**, **W15-01**. Seat: delivery-manager, reconciled with
cloud-architect (`ADR-005` w15 §10, §11, §14) and client-architect (`ADR-012`
w15 §15–§16). Everything above is unchanged — the body, both w14 footers and the
first w15 footer's clauses 13–22 — and `Status: accepted` stands. **Nothing is
superseded.** No promotion path is added, no gate is relaxed, and w15's CI-YAML
set stays **zero files**. Clause numbering continues at **23**.

**23. Clause 17's assertion is reworded, because as written it fails on a
healthy environment.** Clause 17 has the operator confirm "the worker's active
revision is running and healthy". Cloud-architect's `min_replicas = 0` — kept,
with a queue-depth scale rule, because `min_replicas = 1` is a new fixed
~$14/env/month line the lock forbids — makes that **false at rest**: a healthy
worker with an empty queue has **zero replicas**, which is nearly always. Their
correction is adopted in full; the wording is this seat's, as they asked.

- *"A replica of `ca-raffa-<env>-worker` is running"* — **struck.** It fails
  whenever nothing is wrong.
- *"The revision exists"* — **insufficient.** It passes while the image
  crash-loops, which is the hole clause 17 was opened to close.

The check splits, and only the dynamic half proves the path:

| Half | Assertion | What it reads |
|---|---|---|
| **Static** (after the backend deploy) | the worker's **active** revision is the wave's image and has **not failed provisioning**; the `document-processing` subscription exists; the scale rule (or its accepted `listen`-only fallback) is present; both topic-scoped role assignments are present; the worker carries `ConnectionStrings__Storage`. **Zero replicas at rest is a PASS** | configuration and provisioning only |
| **Dynamic** (A15-2, deployed `dev`) | give the worker **work** — enqueue a batch, restart API *and* worker mid-batch, assert **every row reaches a terminal state** | the messaging path, end to end |

The rule behind it is why this is worth a footer: **a gate check that fails when
nothing is wrong gets waived, and the waiver is what the next silent worker
death hides behind.** Clause 17's finding is unchanged — a green `backend.yml`
run still proves nothing, because the deploy step is `az containerapp update
--image` with no revision-state assertion anywhere — and so is its conclusion
that "the HCP run is `CURRENT`" proves none of it. Only the static half's
wording moves.

**24. The dead-letter queue becomes a standing operator condition, and it now
carries two meanings.** ADR-027 §C6 retired the property that made A15-2 a
theorem: a stranded row is bounded and recoverable rather than impossible, so a
non-empty DLQ now also means **"an upload's commit failed"**, not only "a bug".
Cloud-architect accepted the hand-off with no change to the subscription beyond
`max_delivery_count` 5 → 8 (`ADR-005` §9–§10), and it lands here because it is
an operator act, not a resource. Clause 19's sequence gains it at two points:

- **before** the acceptance walk (step 4), the `document-processing` dead-letter
  queue is **empty** — otherwise the walk starts on a previous run's evidence;
- **after** it, a non-empty DLQ is **read and routed, never drained**:
  `job-not-found` ⇒ an upload's commit failed (ADR-027 §C6); anything else ⇒ a
  defect.

Two facts make this executable rather than decorative, both cloud-architect's:
**nothing sweeps the DLQ** — `default_message_ttl` governs the active queue
only, so a dead-lettered message leaves only when something explicitly receives
it, which is precisely what preserves the evidence — and **draining is an admin
action inside the tenant** (§D5's re-enqueue), because no cross-tenant operator
sweep exists and ADR-009 forbids inventing one. The operator's job is to
**notice and route**. It is a standing condition to be **read**, not an event
that passes.

**25. The infrastructure plan gains two assertions before apply, and they are
different assertions.** Clause 19 step 2 has the operator confirm the HCP run
reaches `CURRENT`. For a wave that touches `azuread_application.api` that is not
enough, and two seats found two distinct failure shapes on that one resource.
Both belong at this gate, because the gate is where an operator reads:

1. **`azuread_application.api` shows an in-place update (`~`), never a
   replacement (`-/+`)** — cloud-architect's `ADR-005` §11, co-signed by
   security-architect (`ADR-011` §11) as an *authorization* requirement: that
   registration's client id **is** the `aud` ADR-010 w15 §1.2 pins, so a
   replacement fails every token in flight at once, takes the service principal
   and the pre-authorization with it, and the repair a reviewer reaches for is
   the `ValidateAudience = false` §1.2 forbids.
2. **`identifier_uris` shows no diff at all** — client-architect's `ADR-012`
   §16, and it is the *inversion* of the first: **the SPA binds to the URI
   string.** `web.yml:204-205` hardcodes `api://raffa-<env>-api/Raffa.Read|Write`
   as a hand-copied duplicate of `identity/main.tf:70`, with no test, no build
   step and nothing comparing them — so an in-place `~` that changes
   `identifier_uris` **passes assertion 1 and silently breaks every login.** The
   shape assertion 1 declares safe is the one that breaks the client.

Clause 20's recorded rename risk is unchanged; these two lines are its
plan-time counterpart, making it checkable **before** the apply instead of in a
browser after CI is green.

**26. PR 1 deploys no image — and that is also the one question it must ask.**
Clause 14 and ADR-014 w15 clause 5 rest on `infra/` appearing in neither
`backend.yml`'s nor `web.yml`'s path filter, which is what makes the
infrastructure merge behaviourally inert. Client-architect's `ADR-012` §15 shows
the other edge of that same property: **`web.yml` builds `dist/config.json` in
its deploy job from live Azure lookups plus two hardcoded scope literals
(`:144-205`), and it has no `workflow_dispatch`** — verified on this tree, its
only triggers are `pull_request` (`:10`), `push` (`:17`) and `workflow_call`
(`:28`). So if PR 1 ever changes a value the SPA's config is built from, the SPA
is **not** rebuilt, there is **no manual redeploy path on `dev`**, and it is not
loud at boot either: a stale-but-well-formed client id is neither missing nor
malformed, so the app validates, boots, and every user's sign-in fails in the
browser. **The outage would sit behind no run at all** — worse than clause 20's
window, which at least sits behind a green one.

**For w15 the answer is checked and negative, and it is recorded as a negative
result rather than omitted**: the only registration this wave's apply touches is
`azuread_application.api` (`identity/main.tf:57`), while both SWAs record the
**public-client** id (`identity/main.tf:43-44`), which this wave does not touch.
**No forced SPA redeploy is owed, and the gate adds no step it does not need.**
It becomes a question the **next** wave that changes `infra/` asks rather than
inherits: *does PR 1 change anything `dist/config.json` is built from? If yes,
the SPA needs a redeploy no trigger will perform.* Adding a `workflow_dispatch`
to `web.yml` to solve it is a CI-YAML change and stays refused this wave
(ADR-014 w15 clause 6) — a legitimate W16 question, named here so it is asked
rather than discovered.

**Not decided here** (unchanged): the queue contract and the message shape
(ADR-027), the resources and their SKUs (ADR-005 / ADR-007), the Graph
permission's scope and the credential posture (ADR-025 §J / ADR-011), and when
NW-50 wires a Playwright runner — still W18.
