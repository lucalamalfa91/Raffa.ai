---
id: E16/F04/US01/T01
type: task
story: us-01-final-integration
wave: w15
status: live
target_repo: raffa-backend
---

# task-01-w15-integration — Build, test, sweep, and write the w15 acceptance runbook

## Coding objective

Prove the wave holds together and hand the operator a runbook. Build and test
the backend solution and every test project; type-check, build and test the web;
run the Playwright specs that do not need live Foundry. Sweep the READMEs whose
public surface this wave changed. Then write `docs/waves/w15-acceptance.md`: per
item, the manual check on deployed `dev` — what to click and what to expect —
taken from the acceptance criteria, plus W15-A1, the known-gaps table, the
post-deploy assertions and the promotion sequence.

This is what makes the wave "immediately working": the PR is not green until this
task passes, and the wave is not done until the runbook walks.

## Parent story AC covered

- AC-1 … AC-10 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `docs/waves/w15-acceptance.md` | **new** — the operator runbook described below. **Single-writer of this file in the wave** |

No product code changes here. If a build or test fails, the fix belongs in the
task that owns the file — report it rather than patching across ownership
boundaries. README hygiene is standing implementer scope and is not listed as a
file here; the sweep is a Definition-of-done box below.

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: W15-01 (its proof), and the acceptance half of NW-27, NW-61, NW-10,
NW-67, NW-68, NW-69, NW-58r, NW-05, NW-06`.

Decision rows: `reports/architecture/waves/w15.md` — **W15-01**
(delivery-manager cell, W15-A1 in six points), **NW-27** (the revision-state
assertion and the `dev` walk), **NW-58r** (N3b as a numbered step), **NW-05** (the
rollback rehearsal and the A15-8 walk), and **OQ-w15-dm-03** (the `demo-v4`
ruling).

- **Architecture decisions in force**: **ADR-016** w15 clauses 17, 19, 22–24;
  **ADR-014** w15 clauses 1–7; **ADR-005** w15 clause 14; **ADR-027 §C6**;
  **ADR-001** w15 addendum clause 12.
- **`web/package.json` has no `lint` and no `typecheck` script.** Its scripts are
  `dev`, `generate:api`, `build`, `preview`, `test`, `test:e2e`,
  `test:e2e:report`. `npm run build` **is** the type-check —
  `generate:api && tsc --noEmit && vite build`. Do not invent `npm run lint`.
- **W15-A1, in six points**, which only this task can record because the base is
  an operator act with no fan-out task: (a) the SHA of `origin/main` **actually
  merged**, read from the **loose ref** `.git/refs/remotes/origin/main` and never
  from `packed-refs`, which is stale for every branch ref this wave touches;
  (b) `git diff --stat origin/main..HEAD -- backend web infra .github docs
  scripts` is **empty** — three of the five differing files are *shorter* on the
  process branch, so resolving any of them the wrong way silently reverts w14's
  README and e2e work and nothing notices; (c) `docs/waves/w14-acceptance.md` and
  `web/e2e/invite.spec.ts` resolve; (d) the `build + test` job is green **at the
  base commit**; (e) one `dev` deploy is green; (f) **one interactive sign-in on
  deployed `dev` returns a token carrying the `Raffa.Read` / `Raffa.Write`
  scopes** — the pre-wave check that makes `web.yml:204-205`'s hand-copied scope
  literals an equality someone verified rather than assumed.
- **The acceptance walk starts only when both deploys are green.**
  Web-before-backend ordering was refused with evidence; the 401 window is
  symmetric and bounded.
- **Do not** add a Playwright runner to CI (NW-50, W18), un-skip `invite.spec.ts`,
  or touch `.github/workflows/**` — w15's CI-YAML set is **zero files**.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/Raffa.slnx` exit 0 — and, named individually because a solution-wide run can mask a skipped project: `Raffa.Api.Tests`, `Raffa.Documents.Contracts.Tests`, `Raffa.Identity.Workspace.Tests`, `Raffa.Worker.Tests`, `Raffa.Chat.Tests`, `Raffa.Audit.Tests`, `Raffa.AiGateway.Tests`, `Raffa.ArchitectureTests`, `Raffa.IntegrationTests` each exit 0
- [ ] `cd web && npm run build` exit 0 (runs `generate:api && tsc --noEmit && vite build`), and `git diff --stat web/src/api/generated/` is **empty** afterwards — no contract drift
- [ ] `cd web && npm test` exit 0
- [ ] `cd web && npm run test:e2e` exit 0 against a locally served build — `day1.spec.ts` and `v2.spec.ts` run; `invite.spec.ts` reports **skipped** with the passcode reason
- [ ] `terraform fmt -check -recursive infra` exit 0 and `terraform validate` exit 0 in both environment roots
- [ ] `git diff --stat .github/` is **empty** — w15's CI-YAML set is zero files
- [ ] READMEs whose public surface changed are swept: at least `backend/README.md` (the audit-route sentence at `:243` is unchanged this wave but the async upload contract is not), `web/README.md`, `infra/README.md` (its layout tree gained `communication/` in phase 1)
- [ ] `docs/waves/w15-acceptance.md` exists and every numbered step below is present

## The runbook `docs/waves/w15-acceptance.md` must contain

| Step | What to walk on `dev` | From |
|---|---|---|
| **W15-A1** | the six points above, with the merged SHA written down | ADR-014 w15 |
| **A15-1** | drop 15 PDFs → within 2 s all 15 rows exist server-side, "All documents · 15"; survive a reload and a second browser; each reaches Needs review / Completed / Failed **with the browser closed**; `POST /api/documents` p95 < 2 s; the rail badge matches the list | NW-27, NW-61, NW-10 |
| **A15-2** | restart API and Worker mid-batch → no document lost, every row terminal. Walked with the worker **scaled the way `demo` will run it** | NW-27 |
| **A15-3** | open Ask / Portfolio / Contract 360 while a document is processing → "still processing", never an empty contract or an invented fact | NW-61 |
| **A15-4** | invite an external address the tenant has never seen → mail within 1 min → link → passcode sign-in → inside the workspace as Procurement, **no "Join" click, no Azure portal**; roster Active | NW-67, NW-68 |
| **A15-5** | invite an address already in the tenant → same flow, **no duplicate guest**, indistinguishable in shape | NW-67 |
| **A15-6** | mail transport failing → the designed "could not be sent" copy + the working link; `mailDelivered: false` | NW-68, NW-69 |
| **A15-7** | the directory permission missing → **named error on the pane, audit row, and no invitation claiming "sent"** | NW-67, NW-69 |
| **A15-8** | both halves, **before the `demo-v*` tag**: a forged `X-User-Id`/`X-Tenant-Id` with no token → **401**; and one interactive sign-in → **200** on a real tenant-scoped route, with the role the membership row says | NW-05, NW-06 |
| **N3b** | the invitation e2e walked **by hand** against the operator's external mailbox | NW-58r |

Plus these non-item sections:

- **Post-deploy revision-state assertion, per environment**: the worker revision exists and is healthy **when given work** (A15-2's walk); the `document-processing` subscription exists; the scale rule exists; both topic-scoped role assignments exist. **Zero replicas at rest is a PASS** — a check that fails on a healthy environment gets waived, and the waiver is what the next silent worker death hides behind.
- **Dead-letter queue**: empty before the walk; afterwards read with **two** meanings — `job-not-found` ⇒ an upload's commit failed; anything else ⇒ a defect — and **routed, never drained**, because nothing sweeps it and ADR-009 forbids a cross-tenant sweep.
- **Promotion sequence**: `demo-v4` cut on current `main` **before w15 starts**; w15 promotes as **`demo-v5`**; the `demo` flag-flip PR lands **before** the tag; `promote-backend` only after that apply is `CURRENT`; the `demo` invitation walk uses a **second** external address.
- **`demo`'s three outstanding data-plane steps, which belong to w14**: the w14+w15 schema apply, `seed-demo-fixture.yml`, `backfill-workspace-membership.yml`, then `docs/waves/w14-acceptance.md` N1–N9 + W14-A2.
- **Rollback rehearsal for NW-05**, the one change whose failure mode is "nobody can use `dev`": revert PR to `main` → `dev` redeploys the previous image; the env vars stay and are harmless, being unread by that image; **no apply, no HCP wait**.
- **Known gaps**: `reprocess-tenant-documents.yml` is out of service from this wave (401 on a read, exit 1 before any write — no partial reprocess, no unattributed audit rows); **NW-31 owns it in W16**. **A15-4 / A15-5 / A15-7 are `dev` acceptance and are not walkable on `demo` this wave**, because `Invitations__Mail__Enabled` and `guest_provisioning_enabled` are `false` there — a reviewer must not read that as NW-67/NW-68 undelivered, and **no task may switch either flag on for `demo`** to make them pass.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| build | both trees compile and the contract has not drifted | `dotnet build backend/Raffa.slnx`, `cd web && npm run build` |
| suite | every backend test project and the web vitest suite pass together | the commands above |
| e2e | the browser paths that need no live Foundry still pass | `cd web && npm run test:e2e` |
| manual | every acceptance step above, on deployed `dev` | `docs/waves/w15-acceptance.md` |

## Open questions blocking this task

- **OQ-w15-dm-03** — the `demo-v4` cut is the operator's decision at HITL. The runbook must record **both** branches so the wave closes either way. Not blocking the build.
- **OQ-w15-ca-02** — if NW-05 slipped, A15-4 is walked against a **real B2B guest**. Not blocking.

## Wave-spec entry
```yaml
- id: E16/F04/US01/T01
  prompt: reports/workitems/epic-16-async-document-processing/feature-04-w15-integration/us-01-final-integration/tasks/task-01-w15-integration.md
  produces: [w15-integration]
  depends_on: [documents-truthful-surfaces, invite-pane-and-accept, invitation-e2e-and-seam-ban]
  effort: L
  layer: backend
  status: live
```
